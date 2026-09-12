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

        // Take all / stack all would act on m_currentContainer (the chest), and their listeners are not
        // cloned anyway, so hide every button of the clone.
        foreach (var button in _panel.GetComponentsInChildren<Button>(true))
            button.gameObject.SetActive(false);

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

    /// <summary>
    /// The clone has the same hierarchy as the original, so the component at the same index is its twin.
    /// Returns a Traverse on the twin's "text" property, or null when it cannot be matched.
    /// </summary>
    private static Traverse TextOfTwin(InventoryGui gui, string fieldName)
    {
        var original = AccessTools.Field(typeof(InventoryGui), fieldName)?.GetValue(gui) as Component;
        if (original == null)
            return null;

        var type = original.GetType();
        var originals = gui.m_container.GetComponentsInChildren(type, true);
        var index = Array.IndexOf(originals, original);
        if (index < 0)
            return null;

        var clones = _panel.transform.GetComponentsInChildren(type, true);
        if (index >= clones.Length)
            return null;

        return Traverse.Create(clones[index]).Property("text");
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
            // Vanilla quick-move (InventoryGui.cs:962-975) only knows player <-> m_currentContainer, and
            // would move a backpack item into the chest using the player inventory as source. From the
            // backpack panel, quick-move goes to the player inventory instead.
            if (!IsOpen || grid == null || grid != _grid || mod != InventoryGrid.Modifier.Move || item == null)
                return true;

            var player = Player.m_localPlayer;
            if (player == null || __instance.m_dragGo != null || item.m_shared.m_questItem)
                return true;

            player.RemoveEquipAction(item);
            player.UnequipItem(item);
            player.GetInventory().MoveItemToThis(grid.GetInventory(), item);
            __instance.m_moveItemEffects.Create(__instance.transform.position, Quaternion.identity);
            return false;
        }
    }
}
