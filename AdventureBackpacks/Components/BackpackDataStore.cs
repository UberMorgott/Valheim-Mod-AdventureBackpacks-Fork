using System.Runtime.CompilerServices;
using HarmonyLib;
using JetBrains.Annotations;

namespace AdventureBackpacks.Components;

// Keeps one live BackpackComponent per backpack ItemData. The persistent form is the backpack inventory,
// serialized into the vanilla ItemDrop.ItemData.m_customData, which the game already saves, loads and copies:
// ItemData.Save writes it (ItemDrop.cs:465-518; used by Inventory.Save and ItemDrop.SaveToZDO),
// ItemData.Load reads it (ItemDrop.cs:527-546) and ItemData.Clone copies it (ItemDrop.cs:458-463).
// The live inventory is written back into m_customData right before each Save and Clone.
internal static class BackpackDataStore
{
    // Same key as upstream Adventure Backpacks, so saves stay interchangeable with it.
    internal const string CustomDataKey = "vapok.mods.adventurebackpacks#AdventureBackpacks.Components.BackpackComponent";

    private static readonly ConditionalWeakTable<ItemDrop.ItemData, BackpackComponent> Live = new();

    // Component of an item that already carries backpack data; null otherwise.
    internal static BackpackComponent Get(ItemDrop.ItemData item)
    {
        if (item == null)
            return null;

        if (Live.TryGetValue(item, out var component))
            return component;

        if (!item.m_customData.ContainsKey(CustomDataKey))
            return null;

        component = Attach(item);
        component.Load();
        return component;
    }

    // Component of the item, creating the backpack data on first use.
    internal static BackpackComponent GetOrCreate(ItemDrop.ItemData item)
    {
        var component = Get(item);
        if (component != null)
            return component;

        item.m_customData[CustomDataKey] = "";
        component = Attach(item);
        component.FirstLoad();
        return component;
    }

    // Crafting an upgrade replaces the item (InventoryGui.DoCrafting, InventoryGui.cs:1817-1887: RemoveItem,
    // then AddItem by prefab name), and the new item starts without custom data. Moves the backpack data
    // and its live component over to the upgraded item.
    internal static void MoveToUpgradedItem(BackpackComponent component, ItemDrop.ItemData upgraded)
    {
        var previous = component.Item;
        component.Save();
        upgraded.m_customData[CustomDataKey] = previous.m_customData.TryGetValue(CustomDataKey, out var data) ? data : "";

        Live.Remove(previous);
        Live.Remove(upgraded);
        Live.Add(upgraded, component);
        component.Item = upgraded;
        component.Load();
    }

    private static BackpackComponent Attach(ItemDrop.ItemData item)
    {
        var component = new BackpackComponent(item);
        Live.Add(item, component);
        return component;
    }

    private static void StoreLiveInventory(ItemDrop.ItemData item)
    {
        if (item != null && Live.TryGetValue(item, out var component))
            component.Save();
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.Save))]
    private static class ItemDataSavePatch
    {
        [UsedImplicitly]
        private static void Prefix(ItemDrop.ItemData __instance)
        {
            StoreLiveInventory(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.Clone))]
    private static class ItemDataClonePatch
    {
        [UsedImplicitly]
        private static void Prefix(ItemDrop.ItemData __instance)
        {
            StoreLiveInventory(__instance);
        }
    }
}
