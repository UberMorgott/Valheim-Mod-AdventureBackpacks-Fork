using System.Linq;
using System.Text.RegularExpressions;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;
using BepInEx.Configuration;
using UnityEngine;

namespace AdventureBackpacks.Features;

public enum ConsumptionPriority
{
    PlayerInventoryFirst,
    BackpackFirst
}

// Crafting and building may use the materials in the equipped backpack, and crafted items may go into it when
// the inventory is full. Settings are per client.
public static class CraftFromBackpack
{
    internal const string Section = "Automation";

    public static ConfigEntry<bool> EnableCraftFromBackpack;
    public static ConfigEntry<bool> EnableCraftOutputToBackpack;
    public static ConfigEntry<ConsumptionPriority> MaterialConsumptionPriority;
    public static ConfigEntry<bool> LeaveOneItemInBackpack;
    public static ConfigEntry<bool> DisplayTotalIngredientCount;
    public static ConfigEntry<string> IngredientCountMatchPattern;

    private static Regex _ingredientRegex;

    internal static void BindConfig()
    {
        ConfigRegistry.BindLocal(Section, "Enable Craft From Backpack", true,
            new ConfigDescription("When enabled, materials from the equipped backpack are considered when crafting or building.",
                null, new ConfigurationManagerAttributes { Order = 6 }), ref EnableCraftFromBackpack);

        ConfigRegistry.BindLocal(Section, "Enable Craft Output To Backpack", true,
            new ConfigDescription("When enabled and player inventory is full, newly crafted items will be placed into the equipped backpack if space is available.",
                null, new ConfigurationManagerAttributes { Order = 5 }), ref EnableCraftOutputToBackpack);

        ConfigRegistry.BindLocal(Section, "Material Consumption Priority", ConsumptionPriority.PlayerInventoryFirst,
            new ConfigDescription("Determines whether materials are drawn from the player inventory or the equipped backpack first during crafting and building.",
                null, new ConfigurationManagerAttributes { Order = 4 }), ref MaterialConsumptionPriority);

        ConfigRegistry.BindLocal(Section, "Leave One Item In Backpack", true,
            new ConfigDescription("When enabled, at least one item of each resource type will remain in the backpack and will not be consumed or counted during crafting and building.",
                null, new ConfigurationManagerAttributes { Order = 3 }), ref LeaveOneItemInBackpack);

        ConfigRegistry.BindLocal(Section, "Display Total Ingredient Count", true,
            new ConfigDescription("When enabled, requirement amounts in the crafting panel display as 'Available/Required' to reflect backpack contents.",
                null, new ConfigurationManagerAttributes { Order = 2 }), ref DisplayTotalIngredientCount);

        ConfigRegistry.BindLocal(Section, "Ingredient Count Match Pattern", @"\d+[/(]\d+",
            new ConfigDescription("Regex pattern used to detect if another mod has already formatted the requirement count label (e.g. '123/50' or '50(123)'). If matched, AdventureBackpacks will not overwrite it.",
                null, new ConfigurationManagerAttributes { Order = 1 }), ref IngredientCountMatchPattern);

        IngredientCountMatchPattern.SettingChanged += (_, _) => _ingredientRegex = null;
    }

    public static Regex IngredientRegex
    {
        get
        {
            if (_ingredientRegex == null && !string.IsNullOrEmpty(IngredientCountMatchPattern?.Value))
            {
                try
                {
                    _ingredientRegex = new Regex(IngredientCountMatchPattern.Value, RegexOptions.Compiled);
                }
                catch (System.ArgumentException)
                {
                    _ingredientRegex = null;
                }
            }

            return _ingredientRegex;
        }
    }

    private static bool LeaveOne => LeaveOneItemInBackpack?.Value ?? false;

    public static bool CanCraftFromBackpack(Player player, out Inventory backpackInventory)
    {
        backpackInventory = null;
        if (EnableCraftFromBackpack == null || !EnableCraftFromBackpack.Value)
            return false;

        return TryGetEquippedBackpackInventory(player, out backpackInventory);
    }

    public static bool CanCraftOutputToBackpack(Player player, out Inventory backpackInventory)
    {
        backpackInventory = null;
        if (EnableCraftOutputToBackpack == null || !EnableCraftOutputToBackpack.Value)
            return false;

        return TryGetEquippedBackpackInventory(player, out backpackInventory);
    }

    private static bool TryGetEquippedBackpackInventory(Player player, out Inventory backpackInventory)
    {
        backpackInventory = null;
        if (player == null || player != Player.m_localPlayer || !player.IsBackpackEquipped())
            return false;

        backpackInventory = player.GetEquippedBackpack()?.GetInventory();
        return backpackInventory != null;
    }

    // Backpack count that crafting may use: "Leave One Item In Backpack" holds one back.
    public static int UsableCount(int backpackCount)
    {
        return LeaveOne && backpackCount > 0 ? backpackCount - 1 : backpackCount;
    }

    public static int GetBackpackItemCount(Player player, string itemName, int quality = -1)
    {
        if (string.IsNullOrEmpty(itemName) || !CanCraftFromBackpack(player, out var backpackInventory))
            return 0;

        return UsableCount(quality > 0 ? backpackInventory.CountItems(itemName, quality) : backpackInventory.CountItems(itemName));
    }

    public static int GetPlayerAvailableCount(Player player, string itemName, int itemQuality = -1)
    {
        var items = player?.GetInventory()?.GetAllItems();
        if (items == null || string.IsNullOrEmpty(itemName))
            return 0;

        return items.Where(item => item != null && !item.m_equipped && item.m_shared != null && item.m_shared.m_name == itemName && (itemQuality < 0 || item.m_quality == itemQuality))
            .Sum(item => item.m_stack);
    }

    public static int GetBackpackAvailableCount(Inventory backpackInventory, string itemName, int itemQuality = -1)
    {
        var items = backpackInventory?.GetAllItems();
        if (items == null || string.IsNullOrEmpty(itemName))
            return 0;

        return UsableCount(items.Where(item => item != null && item.m_shared != null && item.m_shared.m_name == itemName && (itemQuality < 0 || item.m_quality == itemQuality))
            .Sum(item => item.m_stack));
    }

    // Vanilla RemoveItem(name, ...) takes the first matching stacks in list order: equipped items with an
    // unequipped twin later in the list go to its end, so the worn one is taken last.
    public static void ProtectEquippedItems(Inventory playerInventory, string itemName)
    {
        var list = playerInventory?.m_inventory;
        if (list == null || string.IsNullOrEmpty(itemName))
            return;

        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item == null || !item.m_equipped || item.m_shared == null || item.m_shared.m_name != itemName)
                continue;

            var hasUnequippedLater = false;
            for (var j = i + 1; j < list.Count; j++)
            {
                var later = list[j];
                if (later != null && !later.m_equipped && later.m_shared != null && later.m_shared.m_name == itemName)
                {
                    hasUnequippedLater = true;
                    break;
                }
            }

            if (!hasUnequippedLater)
                continue;

            list.RemoveAt(i);
            list.Add(item);
            i--;
        }
    }

    // Pays the backpack's share of a cost and returns what the player inventory still has to pay.
    public static int DeductBackpackCraftingItem(Player player, Inventory backpackInventory, string itemName, int amount, int itemQuality = -1)
    {
        if (amount <= 0 || player == null || backpackInventory == null || string.IsNullOrEmpty(itemName))
            return amount;

        var backpackAvailable = GetBackpackAvailableCount(backpackInventory, itemName, itemQuality);
        var fromBackpack = MaterialConsumptionPriority?.Value == ConsumptionPriority.BackpackFirst
            ? Mathf.Min(amount, backpackAvailable)
            : Mathf.Min(Mathf.Max(0, amount - GetPlayerAvailableCount(player, itemName, itemQuality)), backpackAvailable);

        if (fromBackpack > 0)
            backpackInventory.RemoveItem(itemName, fromBackpack, itemQuality);

        return amount - fromBackpack;
    }

    public static int ConsumeCraftingItem(Player player, string itemName, int amount, int itemQuality = -1)
    {
        var remaining = amount;
        if (remaining <= 0 || player == null || string.IsNullOrEmpty(itemName))
            return remaining;

        if (CanCraftFromBackpack(player, out var backpackInventory))
            remaining = DeductBackpackCraftingItem(player, backpackInventory, itemName, remaining, itemQuality);

        var playerInventory = player.GetInventory();
        if (remaining <= 0 || playerInventory == null)
            return remaining;

        foreach (var item in playerInventory.GetAllItems().Where(x => x != null && !x.m_equipped && x.m_shared != null && x.m_shared.m_name == itemName && (itemQuality < 0 || x.m_quality == itemQuality)).ToList())
        {
            if (remaining <= 0)
                break;

            var toRemove = Mathf.Min(item.m_stack, remaining);
            playerInventory.RemoveItem(item, toRemove);
            remaining -= toRemove;
        }

        return remaining;
    }
}
