using System;
using AdventureBackpacks.Extensions;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace AdventureBackpacks.Patches;

/// <summary>
/// Extra container panel showing the equipped backpack next to a real container, so the player
/// inventory, the chest and the backpack are all usable at the same time. The panel is a clone of
/// InventoryGui.m_container placed where the crafting panel sits, so the crafting panel is hidden
/// while it is visible.
/// </summary>
internal static class BackpackPanel
{
    private static GameObject _panel;
    private static InventoryGrid _grid;
    // TMP_Text, kept as Component/Traverse: this project does not reference the TextMeshPro assembly.
    private static Traverse _nameText;
    private static Traverse _weightText;
    private static Inventory _inventory;

    public static bool IsOpen => _panel != null && _panel.activeSelf;

    /// <summary>
    /// Shared target rules for shift-move and right-click quick transfer while the panel is open:
    /// backpack -> chest, chest -> player inventory (overflowing into the backpack when it is full).
    /// Returns false when the caller must keep its vanilla behaviour (player grid -> chest, panel closed).
    /// </summary>
    public static bool TryRoute(InventoryGui gui, Inventory from, ItemDrop.ItemData item, out Inventory to)
    {
        to = null;
        var player = Player.m_localPlayer;
        if (!IsOpen || player == null || gui == null || gui.m_currentContainer == null || from == null || item == null)
            return false;

        var containerInventory = gui.m_currentContainer.GetInventory();
        var playerInventory = player.GetInventory();

        if (from == _inventory)
        {
            to = containerInventory;
            return true;
        }

        if (from != containerInventory)
            return false;

        // Inventory.CanAddItem(item) checks free stack space + empty slots for the whole stack
        // (Inventory.cs:93). A backpack never goes into a backpack.
        to = (playerInventory.CanAddItem(item) || item.IsBackpack()) ? playerInventory : _inventory;
        return true;
    }

    public static void Open(Player player, InventoryGui gui)
    {
        if (player == null || gui == null)
            return;

        var inventory = player.GetEquippedBackpack()?.GetInventory();
        if (inventory == null)
            return;

        if (_panel == null && !Create(gui))
            return;

        _inventory = inventory;
        _panel.SetActive(true);
        if (gui.m_crafting != null)
            gui.m_crafting.gameObject.SetActive(false);

        _nameText?.SetValue(Localization.instance.Localize(inventory.GetName()));
        Refresh(gui, inventory);
    }

    public static void Close(InventoryGui gui)
    {
        if (_panel == null)
            return;

        _panel.SetActive(false);

        if (gui != null)
        {
            if (gui.m_crafting != null)
                gui.m_crafting.gameObject.SetActive(true);

            // Never leave a drag held from an inventory whose grid just went away.
            if (_inventory != null && gui.m_dragInventory == _inventory)
                gui.SetupDragItem(null, null, 1);
        }

        _inventory = null;
    }

    private static void Refresh(InventoryGui gui, Inventory inventory)
    {
        _grid.UpdateInventory(inventory, null, gui.m_dragItem);
        _weightText?.SetValue(Mathf.CeilToInt(inventory.GetTotalWeight()).ToString());
    }

    private static bool Create(InventoryGui gui)
    {
        if (gui.m_container == null || gui.m_containerGrid == null)
            return false;

        _panel = UnityEngine.Object.Instantiate(gui.m_container.gameObject, gui.m_container.parent);
        _panel.name = "AB_BackpackPanel";

        _grid = _panel.GetComponentInChildren<InventoryGrid>(true);
        if (_grid == null)
        {
            UnityEngine.Object.Destroy(_panel);
            _panel = null;
            return false;
        }

        // Grid callbacks are assigned at runtime in InventoryGui.Awake (InventoryGui.cs:388-407) and are
        // therefore not copied by Instantiate. Re-point them at the same InventoryGui handlers so items
        // can be dragged between all three grids.
        var source = gui.m_containerGrid;
        _grid.m_onSelected = source.m_onSelected;
        _grid.m_onReleased = source.m_onReleased;
        _grid.m_onRightClick = source.m_onRightClick;
        _grid.m_onEnter = source.m_onEnter;
        _grid.CanDropDragOntoItem = source.CanDropDragOntoItem;

        // Runtime listeners (InventoryGui.Awake, InventoryGui.cs:407-409) are not cloned, and the vanilla
        // handlers act on m_currentContainer, so hide every button and re-wire only take all / stack all
        // against the backpack inventory.
        foreach (var button in _panel.GetComponentsInChildren<Button>(true))
            button.gameObject.SetActive(false);

        WireButton(TwinOf(gui, gui.m_takeAllButton) as Button, OnTakeAll);
        WireButton(TwinOf(gui, gui.m_stackAllButton) as Button, OnStackAll);

        // Gamepad focus: UIGroupHandler registers itself in a global priority list and flips IsActive from
        // its own Update (UIGroupHandler.cs:109-114), so the cloned handler would be gamepad-active at the
        // same time as the real container panel. The clone is not in InventoryGui.m_uiGroups and can never
        // be reached by group cycling anyway, so give the grid a dead handler: disabling the component runs
        // OnDisable (clears IsActive) and stops its Update, which is all InventoryGrid reads.
        var group = _grid.m_uiGroup;
        if (group == null || !group.transform.IsChildOf(_panel.transform))
            group = _panel.AddComponent<UIGroupHandler>();
        group.enabled = false;
        _grid.m_uiGroup = group;

        _nameText = TextOfTwin(gui, "m_containerName");
        _weightText = TextOfTwin(gui, "m_containerWeight");

        // Sit where the crafting panel sits (which is why the crafting panel gets hidden).
        // ponytail: position only, no size fitting — tweak here if the panel ever overlaps.
        var rect = (RectTransform)_panel.transform;
        var crafting = gui.m_crafting;
        rect.anchorMin = crafting.anchorMin;
        rect.anchorMax = crafting.anchorMax;
        rect.pivot = crafting.pivot;
        rect.anchoredPosition = crafting.anchoredPosition;
        return true;
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

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
    private static class InventoryGuiUpdatePatch
    {
        [UsedImplicitly]
        private static void Postfix(InventoryGui __instance)
        {
            if (!IsOpen)
                return;

            var player = Player.m_localPlayer;
            var inventory = player == null ? null : player.GetEquippedBackpack()?.GetInventory();

            // The extra panel only exists next to a real container: GUI closed, chest closed or backpack
            // unequipped all close it again.
            if (inventory == null || !__instance.m_animator.GetBool("visible") || __instance.m_currentContainer == null)
            {
                Close(__instance);
                return;
            }

            _inventory = inventory;
            Refresh(__instance, inventory);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
    private static class OnSelectedItemPatch
    {
        [UsedImplicitly]
        private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            // Vanilla quick-move (InventoryGui.cs:949-972) only knows player <-> m_currentContainer, so with
            // three grids open it would move a backpack item using the player inventory as source. Split,
            // drag/drop and plain select stay vanilla: they already work off grid.GetInventory().
            if (!IsOpen || grid == null || mod != InventoryGrid.Modifier.Move || item == null)
                return true;

            var player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || __instance.m_dragGo != null || item.m_shared.m_questItem)
                return true;

            var from = grid.GetInventory();
            if (!TryRoute(__instance, from, item, out var to) || to == null || to == from)
                return true;

            player.RemoveEquipAction(item);
            player.UnequipItem(item);
            to.MoveItemToThis(from, item);
            __instance.m_moveItemEffects.Create(__instance.transform.position, Quaternion.identity);
            return false;
        }
    }
}
