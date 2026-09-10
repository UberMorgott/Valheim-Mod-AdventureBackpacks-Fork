using System;
using AdventureBackpacks.Extensions;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace AdventureBackpacks.Compats;

/// <summary>
/// Soft integration with randyknapp's EquipmentAndQuickSlots. Everything is resolved by reflection,
/// there is deliberately no assembly reference, so the mod builds and runs without EQS installed.
/// When EQS is present, backpacks get their own equipment slot instead of competing with capes.
/// The backpack prefab keeps ItemType.Shoulder either way, so the vanilla path still works.
/// </summary>
public static class EquipmentAndQuickSlotsCompat
{
    public const string PluginGuid = "randyknapp.mods.equipmentandquickslots";

    private const string SlotId = "Backpack";
    private const string SlotNameToken = "$vapok_mod_eqs_slot_backpack";
    private const int RequiredApiVersion = 2;

    private delegate bool TryGetSlotItemDelegate(string slotId, out ItemDrop.ItemData item);

    private static Func<string, string, string, Func<ItemDrop.ItemData, bool>, Func<bool>, bool> _addSlot;
    private static TryGetSlotItemDelegate _tryGetSlotItem;
    private static bool _registrationAttempted;

    /// <summary>True once EQS was found and its API resolved. Cached, never reflected per frame.</summary>
    public static bool IsAvailable => _tryGetSlotItem != null;

    public static void Initialize()
    {
        if (!Chainloader.PluginInfos.ContainsKey(PluginGuid))
            return;

        try
        {
            var apiType = AccessTools.TypeByName("EquipmentAndQuickSlots.API");
            if (apiType == null)
            {
                AdventureBackpacks.Log.Warning($"{PluginGuid} is loaded but its API type was not found. Falling back to the vanilla shoulder slot.");
                return;
            }

            var getApiVersion = AccessTools.DeclaredMethod(apiType, "GetApiVersion");
            var apiVersion = getApiVersion == null ? 0 : (int)getApiVersion.Invoke(null, null);

            if (apiVersion < RequiredApiVersion)
            {
                AdventureBackpacks.Log.Warning($"EquipmentAndQuickSlots API version {apiVersion} is older than the required {RequiredApiVersion}. Falling back to the vanilla shoulder slot.");
                return;
            }

            var addSlot = AccessTools.DeclaredMethod(apiType, "AddSlot");
            var tryGetSlotItem = AccessTools.DeclaredMethod(apiType, "TryGetSlotItem");

            if (addSlot == null || tryGetSlotItem == null)
            {
                AdventureBackpacks.Log.Warning("EquipmentAndQuickSlots API is missing AddSlot or TryGetSlotItem. Falling back to the vanilla shoulder slot.");
                return;
            }

            _addSlot = (Func<string, string, string, Func<ItemDrop.ItemData, bool>, Func<bool>, bool>)
                Delegate.CreateDelegate(typeof(Func<string, string, string, Func<ItemDrop.ItemData, bool>, Func<bool>, bool>), addSlot);
            _tryGetSlotItem = (TryGetSlotItemDelegate)Delegate.CreateDelegate(typeof(TryGetSlotItemDelegate), tryGetSlotItem);

            AdventureBackpacks.Log.Message($"EquipmentAndQuickSlots detected (API version {apiVersion}). A dedicated backpack slot will be registered.");
        }
        catch (Exception e)
        {
            _addSlot = null;
            _tryGetSlotItem = null;
            AdventureBackpacks.Log.Error($"Failed to bind the EquipmentAndQuickSlots API, falling back to the vanilla shoulder slot: {e.Message}");
        }
    }

    /// <summary>Registers the backpack slot once. Safe to call on every Player.Awake.</summary>
    public static void RegisterSlot()
    {
        if (_registrationAttempted || _addSlot == null)
            return;

        _registrationAttempted = true;

        try
        {
            // AddSlot returning false means the slot is already registered, which is not an error.
            var added = _addSlot(SlotId, "vapok.mods.adventurebackpacks", SlotNameToken, IsBackpackItem, () => true);
            AdventureBackpacks.Log.Message(added
                ? $"Registered the '{SlotId}' equipment slot with EquipmentAndQuickSlots."
                : $"The '{SlotId}' equipment slot was already registered with EquipmentAndQuickSlots.");
        }
        catch (Exception e)
        {
            _registrationAttempted = false;
            AdventureBackpacks.Log.Error($"Failed to register the '{SlotId}' slot with EquipmentAndQuickSlots: {e.Message}");
        }
    }

    /// <summary>The item currently sitting in the EQS backpack slot, or null.</summary>
    public static ItemDrop.ItemData GetSlotItem()
    {
        if (_tryGetSlotItem == null)
            return null;

        try
        {
            return _tryGetSlotItem(SlotId, out var item) ? item : null;
        }
        catch (Exception e)
        {
            AdventureBackpacks.Log.Error($"EquipmentAndQuickSlots TryGetSlotItem failed: {e.Message}");
            return null;
        }
    }

    // Must go through IsBackpack(), an m_itemType check would let every cape into the slot.
    private static bool IsBackpackItem(ItemDrop.ItemData item)
    {
        return item?.m_shared != null && item.IsBackpack();
    }
}
