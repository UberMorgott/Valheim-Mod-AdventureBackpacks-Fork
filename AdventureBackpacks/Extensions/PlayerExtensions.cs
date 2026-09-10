using AdventureBackpacks.Compats;
using AdventureBackpacks.Components;
using AdventureBackpacks.Patches;
using UnityEngine;
using Vapok.Common.Managers;

namespace AdventureBackpacks.Extensions;

public static class PlayerExtensions
{
    /// <summary>
    /// The single place that answers "which item is this player wearing as a backpack".
    /// With EquipmentAndQuickSlots active the dedicated backpack slot wins; otherwise, and always
    /// for remote players, the vanilla shoulder slot is used.
    /// </summary>
    public static ItemDrop.ItemData GetEquippedBackpackItem(this Player player)
    {
        if (player == null || player.GetInventory() == null)
            return null;

        // The EQS API only ever reports the local player's slots.
        if (EquipmentAndQuickSlotsCompat.IsAvailable && player == Player.m_localPlayer)
        {
            var slotItem = EquipmentAndQuickSlotsCompat.GetSlotItem();
            if (slotItem != null && slotItem.IsBackpack())
                return slotItem;
        }

        var shoulderItem = player.m_shoulderItem;

        if (shoulderItem == null || !shoulderItem.IsBackpack())
            return null;

        return shoulderItem;
    }

    public static bool IsBackpackEquipped(this Player player)
    {
        return player.GetEquippedBackpackItem() != null;
    }

    public static bool IsThisBackpackEquipped(this Player player, ItemDrop.ItemData itemData )
    {
        var equipped = player.GetEquippedBackpackItem();

        return equipped != null && equipped.Equals(itemData);
    }

    public static BackpackComponent GetEquippedBackpack(this Player player)
    {
        return player.GetEquippedBackpackItem()?.Data().GetOrCreate<BackpackComponent>();
    }

    public static bool CanOpenBackpack(this Player player)
    {
        return IsBackpackEquipped(player);
    }

    public static void OpenBackpack(this Player player, InventoryGui instance)
    {
        if (player == null)
            return;
        
        var backpackContainer = player.gameObject.GetComponent<Container>();

        var backpack = player.GetEquippedBackpack();
        backpack?.UpdateContainerSizing(ref backpackContainer);
        
        InventoryGuiPatches.BackpackIsOpen = true;
        instance.Show(backpackContainer);
    }

    public static void QuickDropBackpack(this Player player)
    {
        if (player == null)
            return;
        
        var backpack = GetEquippedBackpack(player);

        if (backpack == null)
            return;

        ItemDrop.ItemData tempItemRemoval = null;
        var swapItemActivated = false;
        var playerInventory = player.GetInventory();
        
        if (!playerInventory.ContainsBackpack(backpack.Item) && !playerInventory.HasEmptySlot())
        {
            tempItemRemoval = playerInventory.FindNonBackpackItem();
            
            if (tempItemRemoval != null && playerInventory.RemoveItem(tempItemRemoval))
                    swapItemActivated = true;
            else
            {
                player.Message(MessageHud.MessageType.Center, "$vapok_mod_quick_drop_unavailable");
                return;
            }
        }

        AdventureBackpacks.QuickDropping = true;
        AdventureBackpacks.Log.Message("Quick dropping backpack.");        
        // Unequip and remove backpack from player's back
        // We need to unequip the item BEFORE we drop it, otherwise when we pick it up again the game thinks
        // we had it equipped all along and fails to update player model, resulting in invisible backpack.
        player.RemoveEquipAction(backpack.Item);
        player.UnequipItem(backpack.Item, true);

        if (!player.m_inventory.RemoveItem(backpack.Item))
        {
            // Removal failed (e.g. another mod blocked it). Do not drop — would duplicate the backpack.
            if (swapItemActivated)
                playerInventory.AddItem(tempItemRemoval);
            AdventureBackpacks.QuickDropping = false;
            player.Message(MessageHud.MessageType.Center, "$vapok_mod_quick_drop_unavailable");
            return;
        }

        // This drops a copy of the backpack itemDrop.itemData
        var itemDrop = ItemDrop.DropItem(backpack.Item, 1, player.transform.position - player.transform.forward + player.transform.up, player.transform.rotation);
        if (itemDrop != null)
        {
            var rb = itemDrop.GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = (Vector3.up - player.transform.forward) * 5f;
            itemDrop.Save();
        }

        player.m_dropEffects.Create(player.transform.position, Quaternion.identity);

        if (swapItemActivated)
            playerInventory.AddItem(tempItemRemoval);
        
        InventoryGuiPatches.BackpackIsOpen = false;
        AdventureBackpacks.QuickDropping = false;
    }
}