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

    private static bool _initialized;

    // Deliberately NOT called from Awake: without a BepInDependency edge (which would close a
    // load-order cycle) EQS may not be in PluginInfos yet. Player.Awake is late enough for both.
    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var eqsInfo) || eqsInfo.Instance == null)
            return;

        try
        {
            // Resolve from the live plugin's own assembly, never AccessTools.TypeByName: EQS can be in the
            // AppDomain twice (APIManager byte-reload), and the stray copy's Slots.slots is never initialized,
            // so AddSlot NREs in TryAddCustomSlotAt.
            var apiType = eqsInfo.Instance.GetType().Assembly.GetType("EquipmentAndQuickSlots.API");
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

            // EQS's built-in Shoulder slot accepts every ItemType.Shoulder item (Slots.EquipmentSlotValidator), and
            // SlotValidation moves any equipped equipment-slot item out of a custom slot into the first free
            // equipment slot. Backpacks are Shoulder items, so without this they always ended up in Shoulder and the
            // Backpack slot stayed empty. The Shoulder slot now refuses backpacks while the Backpack slot is registered.
            var slotType = apiType.Assembly.GetType("EquipmentAndQuickSlots.Slots+Slot");
            _getSlotInfo = AccessTools.DeclaredMethod(apiType, "GetSlotInfoJson");
            _findSlot = AccessTools.DeclaredMethod(apiType.Assembly.GetType("EquipmentAndQuickSlots.Slots"), "FindSlot");
            var itemFits = slotType == null ? null : AccessTools.DeclaredMethod(slotType, "ItemFits");
            if (itemFits == null || _findSlot == null)
                AdventureBackpacks.Log.Warning("EquipmentAndQuickSlots Slot.ItemFits or Slots.FindSlot not found; backpacks may land in the Shoulder slot.");
            else
                new Harmony("vapok.mods.adventurebackpacks.eqs").Patch(itemFits,
                    postfix: new HarmonyMethod(typeof(EquipmentAndQuickSlotsCompat), nameof(ShoulderRefusesBackpack)));

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
        Initialize();

        if (_registrationAttempted || _addSlot == null)
            return;

        _registrationAttempted = true;

        try
        {
            // AddSlot returning false means the slot is already registered, which is not an error.
            var added = _addSlot(SlotId, "vapok.mods.adventurebackpacks", SlotNameToken, IsBackpackItem, () => true);
            _slotRegistered = true;
            AdventureBackpacks.Log.Message(added
                ? $"Registered the '{SlotId}' equipment slot with EquipmentAndQuickSlots."
                : $"The '{SlotId}' equipment slot was already registered with EquipmentAndQuickSlots.");
        }
        catch (Exception e)
        {
            _registrationAttempted = false;
            AdventureBackpacks.Log.Error($"Failed to register the '{SlotId}' slot with EquipmentAndQuickSlots: {e}");
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

    private static System.Reflection.MethodInfo _findSlot, _getSlotInfo;
    private static Vector2i? _slotCell;

    /// <summary>
    /// EQS moves equipped items only into its built-in equipment slots (SlotValidation), never into custom ones,
    /// so an equipped backpack stays in the main grid. Put it into the free Backpack slot cell the way EQS moves
    /// items itself (item.m_gridPos = slot.GridPosition, then Inventory.Changed). Cell from API.GetSlotInfoJson.
    /// </summary>
    public static void MoveToBackpackSlot(Player player, ItemDrop.ItemData item)
    {
        if (!_slotRegistered || _getSlotInfo == null || !IsBackpackItem(item))
            return;
        try
        {
            if (_slotCell == null)
            {
                var json = (string)_getSlotInfo.Invoke(null, new object[] { SlotId });
                var x = System.Text.RegularExpressions.Regex.Match(json ?? "", "\"gridX\":(\\d+)");
                var y = System.Text.RegularExpressions.Regex.Match(json ?? "", "\"gridY\":(\\d+)");
                if (!x.Success || !y.Success)
                    return;
                _slotCell = new Vector2i(int.Parse(x.Groups[1].Value), int.Parse(y.Groups[1].Value));
            }
            var cell = _slotCell.Value;
            var inventory = player.GetInventory();
            if (item.m_gridPos == cell || inventory.GetItemAt(cell.x, cell.y) != null)
                return;
            item.m_gridPos = cell;
            inventory.Changed();
        }
        catch (Exception e)
        {
            AdventureBackpacks.Log.Error($"Moving the backpack into the EquipmentAndQuickSlots slot failed: {e.Message}");
        }
    }
    private static object _shoulderSlot;
    private static bool _slotRegistered;

    // Postfix on EQS Slots.Slot.ItemFits(ItemData). The Shoulder slot object is looked up once (Slots.FindSlot).
    private static void ShoulderRefusesBackpack(object __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (!__result || !_slotRegistered || !IsBackpackItem(item))
            return;
        _shoulderSlot ??= _findSlot.Invoke(null, new object[] { "Shoulder" });
        if (ReferenceEquals(__instance, _shoulderSlot))
            __result = false;
    }

    // Must go through IsBackpack(), an m_itemType check would let every cape into the slot.
    private static bool IsBackpackItem(ItemDrop.ItemData item)
    {
        return item?.m_shared != null && item.IsBackpack();
    }
}
