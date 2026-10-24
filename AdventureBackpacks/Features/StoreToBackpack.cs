using AdventureBackpacks.Assets;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;
using BepInEx.Configuration;

namespace AdventureBackpacks.Features;

// Picked up or looted items go into the equipped backpack when it already holds that item, or when the
// inventory is full. Only whole stacks move, so a ground item is either taken completely or left as it was.
// Moves made by hand while the inventory is open are left alone.
public static class StoreToBackpack
{
    public static ConfigEntry<bool> EnableStoreToBackpack;
    public static ConfigEntry<bool> EnableInventoryOverflowToBackpack;

    internal static void BindConfig()
    {
        ConfigRegistry.BindLocal(CraftFromBackpack.Section, "Enable Auto Store to Backpack", true,
            new ConfigDescription("When enabled, picked up or looted items already present in the equipped backpack are automatically stored in the backpack.",
                null, new ConfigurationManagerAttributes { Order = 8 }), ref EnableStoreToBackpack);

        ConfigRegistry.BindLocal(CraftFromBackpack.Section, "Enable Inventory Overflow To Backpack", true,
            new ConfigDescription("When enabled, if the player inventory is full, picked up or looted items will automatically overflow into the equipped backpack if space is available.",
                null, new ConfigurationManagerAttributes { Order = 7 }), ref EnableInventoryOverflowToBackpack);
    }

    public static bool ShouldStoreToBackpack(Player player, ItemDrop.ItemData item, out Inventory backpackInventory)
    {
        backpackInventory = null;

        if (EnableStoreToBackpack == null || EnableInventoryOverflowToBackpack == null || player == null || player != Player.m_localPlayer || item?.m_shared == null)
            return false;

        if (!EnableStoreToBackpack.Value && !EnableInventoryOverflowToBackpack.Value)
            return false;

        if (item.IsBackpack() || AdventureBackpacks.PerformYardSale || AdventureBackpacks.QuickDropping || AdventureBackpacks.BypassMoveProtection)
            return false;

        if (InventoryGui.IsVisible() || !player.IsBackpackEquipped())
            return false;

        backpackInventory = player.GetEquippedBackpack()?.GetInventory();
        var playerInventory = player.GetInventory();
        if (backpackInventory == null || playerInventory == null || !CanInventoryAccept(backpackInventory, item, item.m_stack))
            return false;

        if (EnableStoreToBackpack.Value && backpackInventory.SafeHaveItem(item.m_shared.m_name))
            return true;

        return EnableInventoryOverflowToBackpack.Value && !CanInventoryAccept(playerInventory, item, item.m_stack);
    }

    public static bool CanInventoryAccept(Inventory inventory, ItemDrop.ItemData item, int stack = -1)
    {
        if (inventory?.m_inventory == null || item?.m_shared == null)
            return false;

        if (inventory.HaveEmptySlot())
            return true;

        if (item.m_shared.m_maxStackSize <= 1)
            return false;

        if (stack <= 0)
            stack = item.m_stack;

        return inventory.SafeFindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel) >= stack;
    }

    public static bool TryStoreItem(ItemDrop.ItemData item, Inventory backpackInventory)
    {
        if (item?.m_shared == null || backpackInventory == null || item.IsBackpack())
            return false;

        if (!Backpacks.CheckForInception(backpackInventory, item) || !CanInventoryAccept(backpackInventory, item, item.m_stack))
            return false;

        return backpackInventory.AddItem(item);
    }
}
