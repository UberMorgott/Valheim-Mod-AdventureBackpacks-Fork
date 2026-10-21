using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace AdventureBackpacks.Patches;

/// <summary>
/// The equipped backpack's own panel in the inventory screen. It is shown whenever the inventory is open and a backpack
/// is worn, with or without a chest, so the player inventory, a chest and the backpack are all usable at the same time.
/// The panel is a clone of InventoryGui.m_container; vanilla's m_container keeps showing only real containers, so the
/// backpack inventory is never shown in two grids.
/// Placement: an external layout owner (RuneSkin "panel.dock") positions it; otherwise <see cref="Place"/> puts it in
/// free space next to the vanilla panels.
/// </summary>
internal static class BackpackPanel
{
    // Space between the panel and the vanilla panel it is placed next to, in the panel parent's units.
    private const float Gap = 8f;

    private static GameObject _panel;
    private static RectTransform _rect;
    private static InventoryGrid _grid;
    private static UIGroupHandler _group;
    // TMP_Text, kept as Component/Traverse: this project does not reference the TextMeshPro assembly.
    private static Traverse _nameText;
    private static Traverse _weightText;
    private static RectTransform _weightBox;
    private static bool _weightMirrored;
    private static Inventory _inventory;
    // The player closed the panel with the hotkey: it stays closed until the inventory is closed.
    private static bool _closedByUser;
    // Fallback placement only (no free space with a chest open, no layout owner): the panel took the crafting panel's place.
    private static bool _craftingHidden;
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly List<Rect> Obstacles = new List<Rect>(4);

    private static event Action<RectTransform> CreatedEvent;

    public static bool IsOpen => _panel != null && _panel.activeSelf;

    /// <summary>The panel root, null until the panel was first opened.</summary>
    public static RectTransform Root => _rect;

    /// <summary>The panel's inventory grid, null until the panel was first opened.</summary>
    public static InventoryGrid Grid => _grid;

    /// <summary>The backpack inventory the panel shows, null while it is closed.</summary>
    public static Inventory ShownInventory => IsOpen ? _inventory : null;

    /// <summary>
    /// Raised once when the panel is created. A handler added after that is called at once with the existing panel.
    /// </summary>
    public static event Action<RectTransform> Created
    {
        add
        {
            CreatedEvent += value;
            if (_rect != null && value != null)
                Notify(value, _rect);
        }
        remove => CreatedEvent -= value;
    }

    /// <summary>
    /// Shared target rules for shift-move and right-click quick transfer while the panel is open:
    /// backpack -> chest (player inventory without a chest), chest -> player inventory (overflowing into the backpack
    /// when it is full), player inventory -> backpack without a chest. Returns false when the caller must keep its
    /// vanilla behaviour (player inventory -> chest, panel closed). Returns true with a null target when the move must
    /// not happen at all (a backpack never goes into a backpack).
    /// </summary>
    public static bool TryRoute(InventoryGui gui, Inventory from, ItemDrop.ItemData item, out Inventory to)
    {
        to = null;
        var player = Player.m_localPlayer;
        if (!IsOpen || _inventory == null || player == null || gui == null || from == null || item == null)
            return false;

        var playerInventory = player.GetInventory();
        var containerInventory = gui.m_currentContainer != null ? gui.m_currentContainer.GetInventory() : null;

        if (from == _inventory)
        {
            to = containerInventory ?? playerInventory;
            return true;
        }

        if (containerInventory != null)
        {
            if (from != containerInventory)
                return false;

            // Inventory.CanAddItem(item) checks free stack space + empty slots for the whole stack
            // (Inventory.cs:93). A backpack never goes into a backpack.
            to = (playerInventory.CanAddItem(item) || item.IsBackpack()) ? playerInventory : _inventory;
            return true;
        }

        if (from != playerInventory)
            return false;

        // No chest: the backpack takes the container's role. Without it vanilla would drop the item on the ground
        // (InventoryGui.OnSelectedItem, InventoryGui.cs:968).
        to = item.IsBackpack() ? null : _inventory;
        return true;
    }

    /// <summary>Opens the panel for the equipped backpack (no-op without one).</summary>
    public static void Open(Player player, InventoryGui gui)
    {
        if (player == null || gui == null)
            return;

        var inventory = player.GetEquippedBackpack()?.GetInventory();
        if (inventory == null || ShownByContainer(gui, inventory))
            return;

        if (_panel == null && !Create(gui))
            return;

        _closedByUser = false;
        _inventory = inventory;
        _panel.SetActive(true);
        JoinGroups(gui);

        _nameText?.SetValue(Localization.instance.Localize(inventory.GetName()));
        Refresh(gui, inventory);
        Place(gui);
    }

    /// <summary>Closes the panel; byUser keeps it closed until the inventory is closed.</summary>
    public static void Close(InventoryGui gui, bool byUser = false)
    {
        if (byUser)
            _closedByUser = true;

        if (_panel == null)
            return;

        _panel.SetActive(false);

        if (gui != null)
        {
            LeaveGroups(gui);
            RestoreCrafting(gui);

            // Never leave a drag held from an inventory whose grid just went away.
            if (_inventory != null && gui.m_dragInventory == _inventory)
                gui.SetupDragItem(null, null, 1);
        }

        _inventory = null;
    }

    // Another mod (or an old API caller) shows this inventory in vanilla's container panel: never show it twice.
    private static bool ShownByContainer(InventoryGui gui, Inventory inventory) =>
        gui.m_currentContainer != null && gui.m_currentContainer.GetInventory() == inventory;

    private static bool OpensWithInventory =>
        ConfigRegistry.OpenWithInventory.Value && !ConfigRegistry.OpenWithHoverInteract.Value;

    private static void Refresh(InventoryGui gui, Inventory inventory)
    {
        _grid.UpdateInventory(inventory, null, gui.m_dragItem);
        _weightText?.SetValue(Mathf.CeilToInt(inventory.GetTotalWeight()).ToString());
    }

    /// <summary>
    /// Gamepad focus. Vanilla cycles InventoryGui.m_uiGroups with JoyTabLeft/Right (UpdateGamepad, InventoryGui.cs:591-612)
    /// and SetActiveGroup user-activates exactly one entry (InventoryGui.cs:633-637). The vanilla entries are addressed
    /// by fixed index (0 container, 2 info, 3 crafting: InventoryGui.cs:603-607, 1382, 2333), so the panel's group is
    /// appended as the last entry while the panel is open and removed again when it closes. With group cycling on, a
    /// step left from the player grid without a chest lands on the last entry (InventoryGui.cs:627-629), the backpack.
    /// </summary>
    private static void JoinGroups(InventoryGui gui)
    {
        var groups = gui.m_uiGroups;
        if (_group == null || groups == null || Array.IndexOf(groups, _group) >= 0)
            return;

        var joined = new UIGroupHandler[groups.Length + 1];
        Array.Copy(groups, joined, groups.Length);
        joined[groups.Length] = _group;
        gui.m_uiGroups = joined;
        // Join inactive: the active group stays the one vanilla chose.
        _group.SetActive(false);
    }

    private static void LeaveGroups(InventoryGui gui)
    {
        var groups = gui.m_uiGroups;
        var index = _group == null || groups == null ? -1 : Array.IndexOf(groups, _group);
        if (index < 0)
            return;

        var active = gui.ActiveGroup;
        gui.m_uiGroups = groups.Where((_, i) => i != index).ToArray();
        _group.SetActive(false);

        // Focus on the closed panel goes to the player grid, the group vanilla opens the inventory with
        // (InventoryGui.Show, InventoryGui.cs:1034-1038); a later entry shifts down by one.
        if (active == index)
            gui.SetActiveGroup(1, playSound: false);
        else if (active > index)
            gui.SetActiveGroup(active - 1, playSound: false);
    }

    private static bool Create(InventoryGui gui)
    {
        if (gui.m_container == null || gui.m_containerGrid == null)
            return false;

        var parent = gui.m_crafting != null ? gui.m_crafting.parent : gui.m_inventoryRoot;
        if (parent == null)
            return false;

        // A sibling of the crafting panel, under Inventory_screen/root: m_container itself sits under root/Player.
        var size = gui.m_container.rect.size;
        _panel = UnityEngine.Object.Instantiate(gui.m_container.gameObject, parent);
        _panel.name = "AB_BackpackPanel";
        _panel.SetActive(false);
        _rect = (RectTransform)_panel.transform;

        _grid = _panel.GetComponentInChildren<InventoryGrid>(true);
        if (_grid == null)
        {
            UnityEngine.Object.Destroy(_panel);
            _panel = null;
            _rect = null;
            return false;
        }

        // Point anchors at the parent's centre and m_container's size: the panel is positioned by its pivot
        // (Place, or an external layout owner), never stretched by its parent.
        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta = size;

        // Grid callbacks are assigned at runtime in InventoryGui.Awake (InventoryGui.cs:380-399) and are
        // therefore not copied by Instantiate. Re-point them at the same InventoryGui handlers so items
        // can be dragged between all three grids. OnMoveToUpper/LowerInventoryGrid stay unset: they move the
        // selection between the player grid and m_containerGrid only (InventoryGui.cs:438-463).
        var source = gui.m_containerGrid;
        _grid.m_onSelected = source.m_onSelected;
        _grid.m_onReleased = source.m_onReleased;
        _grid.m_onRightClick = source.m_onRightClick;
        _grid.m_onEnter = source.m_onEnter;
        _grid.OnSetTouchSelection = source.OnSetTouchSelection;
        _grid.CanDropDragOntoItem = source.CanDropDragOntoItem;

        // Runtime listeners (InventoryGui.Awake, InventoryGui.cs:405-410) are not cloned, and the vanilla
        // handlers act on m_currentContainer, so hide every button and re-wire only take all / stack all
        // against the backpack inventory.
        foreach (var button in _panel.GetComponentsInChildren<Button>(true))
            button.gameObject.SetActive(false);

        WireButton(TwinOf(gui, gui.m_takeAllButton) as Button, OnTakeAll);
        WireButton(TwinOf(gui, gui.m_stackAllButton) as Button, OnStackAll);

        // The clone's own UIGroupHandler (the twin of the container panel's) drives its grid and the gamepad hints
        // of its buttons (UIGamePad finds it with GetComponentInParent, UIGamePad.cs:37-39). It registers itself in
        // the global priority list on Awake and is user-active by default (UIGroupHandler.cs:26, 47-50), so it starts
        // inactive and only InventoryGui.SetActiveGroup turns it on, once it is one of m_uiGroups (JoinGroups).
        var group = _grid.m_uiGroup;
        if (group == null || !group.transform.IsChildOf(_panel.transform))
        {
            group = _panel.GetComponent<UIGroupHandler>() ?? _panel.AddComponent<UIGroupHandler>();
            if (gui.m_uiGroups != null && gui.m_uiGroups.Length > 0 && gui.m_uiGroups[0] != null)
                group.m_groupPriority = gui.m_uiGroups[0].m_groupPriority;
        }
        group.enabled = true;
        group.SetActive(false);
        _grid.m_uiGroup = group;
        _group = group;

        _nameText = TextOfTwin(gui, "m_containerName");
        _weightText = TextOfTwin(gui, "m_containerWeight");

        // The container panel's weight box hangs outside its right edge (prefab Container/Weight: anchor x 1, centre
        // +34 px); Place keeps it there and mirrors it outside the left edge only at the crafting panel's place, where
        // the right edge is the screen's. Item tooltips are parented to the grid's m_tooltipAnchor
        // (InventoryGrid.CreateItemTooltip, InventoryGrid.cs:411; UITooltip.AnchorTooltip, UITooltip.cs:101-105), which
        // sits right of the panel (Container/TooltipAnchor, +90 px) and can be off screen; without an anchor UITooltip
        // places the tooltip over the hovered slot and clamps it to the screen (UITooltip.cs:112-116).
        _weightBox = DirectChild(TwinOf(gui, AccessTools.Field(typeof(InventoryGui), "m_containerWeight")?.GetValue(gui) as Component));
        _weightMirrored = false;
        _grid.m_tooltipAnchor = null;

        if (CreatedEvent != null)
            foreach (var handler in CreatedEvent.GetInvocationList().Cast<Action<RectTransform>>())
                Notify(handler, _rect);

        return true;
    }

    private static void Notify(Action<RectTransform> handler, RectTransform rect)
    {
        try
        {
            handler(rect);
        }
        catch (Exception e)
        {
            AdventureBackpacks.Log.LogError($"BackpackPanelCreated handler {handler.Method.DeclaringType?.FullName}.{handler.Method.Name} failed: {e}");
        }
    }

    /// <summary>
    /// Vanilla-safe placement when no external layout owner docks the panel. Every candidate place sits next to a vanilla
    /// panel (container, player, info, crafting; read live, so GUI scale, resolution and restyles are honoured), inside
    /// the canvas and overlapping none of them; m_container's place counts as taken even without a chest, so the panel
    /// does not jump when a chest opens. The first free candidate wins. Fallbacks when nothing is free: without a chest,
    /// m_container's own place (where vanilla showed the backpack before); with a chest open, the crafting panel's place,
    /// hiding the crafting panel until a place is free again or the panel closes.
    /// </summary>
    private static void Place(InventoryGui gui)
    {
        if (ExternalLayout.Docks)
        {
            RestoreCrafting(gui);
            return;
        }

        var parent = _rect.parent as RectTransform;
        if (parent == null)
            return;

        var canvas = _rect.GetComponentInParent<Canvas>();
        var canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        if (canvasRect == null)
            return;
        var bounds = LocalRect(parent, canvasRect);

        // Panel and weight box (in its vanilla orientation, right of the panel) relative to the panel's pivot.
        var pivot = (Vector2)_rect.localPosition;
        var panel = Offset(LocalRect(parent, _rect), pivot);
        var footprint = panel;
        if (_weightBox != null && _weightBox.gameObject.activeSelf)
        {
            var weight = Offset(LocalRect(parent, _weightBox), pivot);
            if (_weightMirrored)
                weight = MirrorAbout(weight, panel);
            footprint = Union(panel, weight);
        }

        Obstacles.Clear();
        AddObstacle(parent, gui.m_container);
        AddObstacle(parent, gui.m_player);
        AddObstacle(parent, gui.m_info);
        AddObstacle(parent, gui.m_crafting);

        foreach (var anchor in Obstacles)
            foreach (var candidate in Candidates(anchor, footprint))
            {
                if (!Fits(Offset(footprint, -candidate), bounds))
                    continue;

                Apply(candidate);
                SetWeightMirrored(false);
                RestoreCrafting(gui);
                return;
            }

        if (gui.m_currentContainer == null && gui.m_container != null)
        {
            var container = LocalRect(parent, gui.m_container);
            Apply(new Vector2(container.xMin - panel.xMin, container.yMax - panel.yMax));
            SetWeightMirrored(false);
            RestoreCrafting(gui);
            return;
        }

        if (gui.m_crafting != null)
        {
            var crafting = LocalRect(parent, gui.m_crafting);
            Apply(new Vector2(crafting.xMax - panel.xMax, crafting.yMax - panel.yMax));
            SetWeightMirrored(true);
            if (gui.m_crafting.gameObject.activeSelf)
            {
                gui.m_crafting.gameObject.SetActive(false);
                _craftingHidden = true;
            }
        }
    }

    // Pivot positions next to anchor: beside it top/bottom aligned, below/above it left/right aligned.
    private static IEnumerable<Vector2> Candidates(Rect anchor, Rect footprint)
    {
        var leftOf = anchor.xMin - Gap - footprint.xMax;
        var rightOf = anchor.xMax + Gap - footprint.xMin;
        var alignLeft = anchor.xMin - footprint.xMin;
        var alignRight = anchor.xMax - footprint.xMax;
        var below = anchor.yMin - Gap - footprint.yMax;
        var above = anchor.yMax + Gap - footprint.yMin;
        var alignTop = anchor.yMax - footprint.yMax;
        var alignBottom = anchor.yMin - footprint.yMin;

        yield return new Vector2(leftOf, alignTop);
        yield return new Vector2(rightOf, alignTop);
        yield return new Vector2(alignLeft, below);
        yield return new Vector2(alignRight, below);
        yield return new Vector2(leftOf, alignBottom);
        yield return new Vector2(rightOf, alignBottom);
        yield return new Vector2(alignLeft, above);
        yield return new Vector2(alignRight, above);
    }

    private static bool Fits(Rect rect, Rect bounds)
    {
        const float eps = 0.5f;
        if (rect.xMin < bounds.xMin - eps || rect.xMax > bounds.xMax + eps || rect.yMin < bounds.yMin - eps || rect.yMax > bounds.yMax + eps)
            return false;

        foreach (var obstacle in Obstacles)
            if (rect.xMin < obstacle.xMax - eps && obstacle.xMin < rect.xMax - eps && rect.yMin < obstacle.yMax - eps && obstacle.yMin < rect.yMax - eps)
                return false;

        return true;
    }

    private static void AddObstacle(RectTransform parent, RectTransform panel)
    {
        if (panel != null)
            Obstacles.Add(LocalRect(parent, panel));
    }

    private static void Apply(Vector2 position)
    {
        var current = _rect.localPosition;
        if (((Vector2)current - position).sqrMagnitude > 0.01f)
            _rect.localPosition = new Vector3(position.x, position.y, current.z);
    }

    private static void SetWeightMirrored(bool mirrored)
    {
        if (_weightBox == null || _weightMirrored == mirrored)
            return;

        MirrorX(_weightBox);
        _weightMirrored = mirrored;
    }

    private static void RestoreCrafting(InventoryGui gui)
    {
        if (!_craftingHidden)
            return;

        _craftingHidden = false;
        if (gui != null && gui.m_crafting != null)
            gui.m_crafting.gameObject.SetActive(true);
    }

    // A RectTransform's rectangle in parent's local space (scale and rotation of everything in between included).
    private static Rect LocalRect(RectTransform parent, RectTransform rect)
    {
        rect.GetWorldCorners(Corners);
        var a = (Vector2)parent.InverseTransformPoint(Corners[0]);
        var b = (Vector2)parent.InverseTransformPoint(Corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private static Rect Offset(Rect rect, Vector2 origin) =>
        Rect.MinMaxRect(rect.xMin - origin.x, rect.yMin - origin.y, rect.xMax - origin.x, rect.yMax - origin.y);

    private static Rect Union(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // rect mirrored about the vertical centre line of about.
    private static Rect MirrorAbout(Rect rect, Rect about)
    {
        var sum = about.xMin + about.xMax;
        return Rect.MinMaxRect(sum - rect.xMax, rect.yMin, sum - rect.xMin, rect.yMax);
    }

    // The ancestor of a panel element that is a direct child of the panel.
    private static RectTransform DirectChild(Component element)
    {
        for (var t = element == null ? null : element.transform; t != null; t = t.parent)
            if (t.parent == _panel.transform)
                return t as RectTransform;
        return null;
    }

    // Mirror a child's place about the panel's vertical centre line (anchors, pivot, offset).
    private static void MirrorX(RectTransform rect)
    {
        var min = rect.anchorMin;
        var max = rect.anchorMax;
        rect.anchorMin = new Vector2(1f - max.x, min.y);
        rect.anchorMax = new Vector2(1f - min.x, max.y);
        rect.pivot = new Vector2(1f - rect.pivot.x, rect.pivot.y);
        rect.anchoredPosition = new Vector2(-rect.anchoredPosition.x, rect.anchoredPosition.y);
    }

    private static void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
        button.gameObject.SetActive(true);
    }

    // Vanilla OnTakeAll/OnStackAll (InventoryGui.cs:823-840) with m_currentContainer swapped for the backpack.
    private static void OnTakeAll()
    {
        var gui = InventoryGui.instance;
        var player = Player.m_localPlayer;
        if (gui == null || player == null || _inventory == null || player.IsTeleporting())
            return;

        gui.SetupDragItem(null, null, 1);
        player.GetInventory().MoveAll(_inventory);
    }

    private static void OnStackAll()
    {
        var gui = InventoryGui.instance;
        var player = Player.m_localPlayer;
        if (gui == null || player == null || _inventory == null || player.IsTeleporting())
            return;

        gui.SetupDragItem(null, null, 1);
        _inventory.StackAll(player.GetInventory());
    }

    /// <summary>
    /// The clone has the same hierarchy as the original, so the component at the same index is its twin.
    /// </summary>
    private static Component TwinOf(InventoryGui gui, Component original)
    {
        if (original == null)
            return null;

        var type = original.GetType();
        var originals = gui.m_container.GetComponentsInChildren(type, true);
        var index = Array.IndexOf(originals, original);
        if (index < 0)
            return null;

        var clones = _panel.transform.GetComponentsInChildren(type, true);
        return index < clones.Length ? clones[index] : null;
    }

    /// <summary>
    /// Returns a Traverse on the twin's "text" property, or null when it cannot be matched.
    /// </summary>
    private static Traverse TextOfTwin(InventoryGui gui, string fieldName)
    {
        var twin = TwinOf(gui, AccessTools.Field(typeof(InventoryGui), fieldName)?.GetValue(gui) as Component);
        return twin == null ? null : Traverse.Create(twin).Property("text");
    }

    /// <summary>
    /// Used by the UpdateContainer transpiler: the inventory whose drag survives "no container open".
    /// </summary>
    internal static Inventory KeptDragInventory(Inventory playerInventory)
    {
        var gui = InventoryGui.instance;
        return IsOpen && _inventory != null && gui != null && gui.m_dragInventory == _inventory ? _inventory : playerInventory;
    }

    /// <summary>
    /// Soft check for an external layout owner: RuneSkin.API's Skin.Supports("panel.dock") (RuneSkin.API\Skin.cs:43),
    /// resolved once by reflection so the mod runs without RuneSkin. While it docks, the panel is never positioned
    /// here and the crafting panel never hidden.
    /// </summary>
    private static class ExternalLayout
    {
        private static bool _resolved;
        private static Func<string, bool> _supports;

        public static bool Docks
        {
            get
            {
                Resolve();
                if (_supports == null)
                    return false;
                try
                {
                    return _supports("panel.dock");
                }
                catch (Exception e)
                {
                    AdventureBackpacks.Log.LogError($"RuneSkin.API Skin.Supports failed, backpack panel placed by Adventure Backpacks: {e.Message}");
                    _supports = null;
                    return false;
                }
            }
        }

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;

            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RuneSkin.API");
            var method = assembly?.GetType("RuneSkin.API.Skin")?.GetMethod("Supports", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (method != null && method.ReturnType == typeof(bool))
                _supports = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), method);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
    private static class InventoryGuiUpdatePatch
    {
        [UsedImplicitly]
        private static void Postfix(InventoryGui __instance)
        {
            var player = Player.m_localPlayer;

            // Same visibility vanilla gates its container update on (InventoryGui.UpdateContainer, InventoryGui.cs:696).
            if (player == null || !__instance.m_animator.GetBool("visible"))
            {
                _closedByUser = false;
                if (IsOpen)
                    Close(__instance);
                return;
            }

            // Unequipped, or shown by the container panel: close (cancels a drag from it).
            var inventory = player.GetEquippedBackpack()?.GetInventory();
            if (inventory == null || ShownByContainer(__instance, inventory))
            {
                if (IsOpen)
                    Close(__instance);
                return;
            }

            if (!IsOpen)
            {
                if (!_closedByUser && OpensWithInventory)
                    Open(player, __instance);
                return;
            }

            // Another backpack equipped while the panel is shown.
            if (inventory != _inventory)
            {
                if (_inventory != null && __instance.m_dragInventory == _inventory)
                    __instance.SetupDragItem(null, null, 1);
                _inventory = inventory;
                _nameText?.SetValue(Localization.instance.Localize(inventory.GetName()));
            }

            Refresh(__instance, inventory);
            Place(__instance);
        }
    }

    /// <summary>
    /// Vanilla cancels any drag that is not from the player inventory while no container is open
    /// (InventoryGui.UpdateContainer, InventoryGui.cs:736-742): the dragged item's grid is gone. The backpack grid is
    /// still shown without a chest, so a drag from it is kept the same way a player-inventory drag is: the compared
    /// player inventory becomes the backpack inventory while that is the drag source.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateContainer))]
    private static class UpdateContainerPatch
    {
        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var localPlayer = AccessTools.DeclaredField(typeof(Player), nameof(Player.m_localPlayer));
            var getInventory = AccessTools.Method(typeof(Humanoid), nameof(Humanoid.GetInventory));

            var matcher = new CodeMatcher(instructions)
                .MatchEndForward(new CodeMatch(OpCodes.Ldsfld, localPlayer), new CodeMatch(i => i.Calls(getInventory)));

            if (matcher.IsInvalid)
            {
                AdventureBackpacks.Log.LogError($"{nameof(InventoryGui.UpdateContainer)} transpiler: anchor Player.m_localPlayer.GetInventory() not found. Dragging from the backpack panel without a chest open will be cancelled.");
                return instructions;
            }

            return matcher.Advance(1)
                .Insert(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(BackpackPanel), nameof(KeptDragInventory))))
                .Instructions();
        }
    }

    /// <summary>
    /// Gamepad drag icon: vanilla follows the selected element of the player grid or m_containerGrid
    /// (InventoryGui.GetSelectedGamepadElement, InventoryGui.cs:755-767; used by UpdateItemDrag, :798-809) and hides the
    /// icon off screen otherwise. While the backpack group is active, its grid's selection is the selected element.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.GetSelectedGamepadElement))]
    private static class GetSelectedGamepadElementPatch
    {
        [UsedImplicitly]
        private static void Postfix(ref RectTransform __result)
        {
            if (__result == null && IsOpen && _grid != null)
                __result = _grid.GetGamepadSelectedElement();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
    private static class OnSelectedItemPatch
    {
        [UsedImplicitly]
        private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            // Vanilla quick-move (InventoryGui.cs:949-972) only knows player <-> m_currentContainer and drops the item on
            // the ground without a container, so with the backpack panel open it is routed here. Split, drag/drop and
            // plain select stay vanilla: they already work off grid.GetInventory().
            if (!IsOpen || grid == null || mod != InventoryGrid.Modifier.Move || item == null)
                return true;

            var player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || __instance.m_dragGo != null || item.m_shared.m_questItem)
                return true;

            var from = grid.GetInventory();
            if (!TryRoute(__instance, from, item, out var to))
                return true;

            // Same group focus vanilla sets first (InventoryGui.cs:881).
            __instance.SetActiveGroup(grid.m_uiGroup, playSound: false);
            if (to == null || to == from)
                return false;

            player.RemoveEquipAction(item);
            player.UnequipItem(item);
            to.MoveItemToThis(from, item);
            __instance.m_moveItemEffects.Create(__instance.transform.position, Quaternion.identity);
            return false;
        }
    }
}
