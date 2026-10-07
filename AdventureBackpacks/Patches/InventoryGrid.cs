using System;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace AdventureBackpacks.Patches;

public class InventoryGridPatches
{
    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
    public static class UpdateGuiPatch
    {
        [UsedImplicitly]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(InventoryGrid __instance)
        {
            if (!__instance.name.Equals("ContainerGrid", StringComparison.Ordinal)) return true;

            var inventory = __instance.m_inventory;
            if (__instance.m_elements.Count < inventory.m_inventory.Count &&
                __instance.m_width == inventory.m_width && __instance.m_height == inventory.m_height)
            {
                __instance.m_width = inventory.m_width + 1;
                __instance.m_height = inventory.m_height + 1;
            }

            // Vanilla rebuilds the slots only when the inventory size changes (InventoryGrid.UpdateGui,
            // InventoryGrid.cs:258-300) and lays them out from the grid's own width at that moment.
            if (__instance.m_width != inventory.m_width || __instance.m_height != inventory.m_height)
                ContainerPanelWidth.Fit(__instance, inventory.m_width);

            return true;
        }
    }
}

/// <summary>
/// The container panel (InventoryGui.m_container, and BackpackPanel's clone of it) is built for vanilla's widest
/// container, 8 columns (the player grid width; V+ caps chests at 8 too). Rows beyond its height scroll the vanilla
/// way (the grid root grows, InventoryGrid.ResetView, InventoryGrid.cs:92-104), but a wider inventory - a backpack
/// configured past 8 columns - is laid out centred on the grid (InventoryGrid.cs:267-275) and its outer columns are
/// clipped by the panel. Before the slots are rebuilt, the panel takes the width its column count needs: the native
/// width plus one slot pitch (m_elementSpace) per extra column; any container that fits gets the native width back.
/// The panel's pivot is its top-left corner, so it grows to the right, its stretched background, grid and the
/// right-anchored weight box following.
/// </summary>
internal static class ContainerPanelWidth
{
    private static float _nativeWidth = -1f;

    public static void Fit(InventoryGrid grid, int columns)
    {
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_container == null)
            return;
        var panel = PanelOf(grid, gui);
        var gridRect = grid.transform as RectTransform;
        // Only a grid that stretches with its panel follows a wider panel.
        if (panel == null || gridRect == null || Mathf.Approximately(gridRect.anchorMin.x, gridRect.anchorMax.x))
            return;

        // The clone is made from m_container, so both share the prefab's width; read once, before any change.
        if (_nativeWidth < 0f)
            _nativeWidth = gui.m_container.rect.width;

        var nativeGrid = gridRect.rect.width - (panel.rect.width - _nativeWidth);
        var fits = Mathf.FloorToInt(nativeGrid / grid.m_elementSpace);
        var width = _nativeWidth + Mathf.Max(0, columns - fits) * grid.m_elementSpace;
        if (!Mathf.Approximately(panel.rect.width, width))
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }

    // The grid's panel: m_container itself, or BackpackPanel's clone of it next to the crafting panel.
    private static RectTransform PanelOf(InventoryGrid grid, InventoryGui gui)
    {
        var crafting = gui.m_crafting != null ? gui.m_crafting.parent : null;
        for (var t = grid.transform; t != null; t = t.parent)
            if (t == gui.m_container || (crafting != null && t.parent == crafting))
                return t as RectTransform;
        return null;
    }
}
