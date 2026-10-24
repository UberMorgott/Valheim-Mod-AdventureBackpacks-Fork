using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
#if ! API
using AdventureBackpacks.Assets.Factories;
using AdventureBackpacks.Extensions;
using AdventureBackpacks.Features;
using AdventureBackpacks.Patches;
#endif

namespace AdventureBackpacks.API;

/// <summary>
/// Adventure Backpacks API. Be sure to include the AdventureBackpacksAPI.dll as a dependency to your project.
/// </summary>
[PublicAPI]
// ReSharper disable once InconsistentNaming
public partial class ABAPI
{
    /// <summary>
    /// Notifies if the ABAPI is active or not.
    /// </summary>
    /// <returns>true of false</returns>
    public static bool IsLoaded()
    {
#if ! API
        return true;
#else
return false;
#endif
    }

    /// <summary>
    /// When provided with an ItemData object, will detect whether the Item is an Adventure Backpack or not.
    /// </summary>
    /// <param name="itemData">This is the ItemDrop.ItemData object of the item.</param>
    /// <returns>true or false</returns>
    public static bool IsBackpack(ItemDrop.ItemData itemData)
    {
#if ! API
        return itemData != null && itemData.IsBackpack();
#else
return false;
#endif
    }


    /// <summary>
    /// Determines if the Player provided is currently wearing a backpack.
    /// </summary>
    /// <param name="player">Player, usually Player.m_localPlayer</param>
    /// <returns>true or false</returns>
    public static bool IsBackpackEquipped(Player player)
    {
#if ! API
        return player != null && player.IsBackpackEquipped();
#else
return false;
#endif
    }

    /// <summary>
    /// Determines if the player is capable of currently opening the equipped backpack.
    /// </summary>
    /// <param name="player">Player, usually Player.m_localPlayer</param>
    /// <returns>true or false</returns>
    public static bool CanOpenBackpack(Player player)
    {
#if ! API
        return player != null && player.CanOpenBackpack();
#else
return false;
#endif
    }

    /// <summary>
    /// Determines if the player provided is wearing the item provided and that it's a backpack.
    /// </summary>
    /// <param name="player">Player, usually Player.m_localPlayer</param>
    /// <param name="itemData">Any ItemData</param>
    /// <returns>true or false. If item provided is not a backpack, will return false.</returns>
    public static bool IsThisBackpackEquipped(Player player, ItemDrop.ItemData itemData)
    {
#if ! API
        return player != null && player.IsThisBackpackEquipped(itemData);
#else
return false;
#endif
    }

    /// <summary>
    /// Returns a Backpack object if the provided Player is currently wearing a backpack.
    /// </summary>
    /// <param name="player">Player, usually Player.m_localPlayer</param>
    /// <returns>Nullable Backpack Object</returns>
    public static Backpack? GetEquippedBackpack(Player player)
    {
#if ! API
        var backpackComponent = player.GetEquippedBackpack();
        return ConvertBackpackItem(backpackComponent);
#else
return null;
#endif
    }

    /// <summary>
    /// Returns Backpack object of the provided itemData. Operates similarly to a TryGet but with a nullable type.
    /// </summary>
    /// <param name="itemData">ItemDrop.ItemData object</param>
    /// <returns>Nullable Backpack Object. Check HasValue.</returns>
    public static Backpack? GetBackpack(ItemDrop.ItemData itemData)
    {
#if ! API
        return ConvertBackpackItem(itemData);
#else
return null;
#endif
    }

    /// <summary>
    /// Retrieves the current Active Backpack StatusEffects running in the local players game.
    /// </summary>
    /// <returns>HashSet of Status Effects.</returns>
    public static HashSet<StatusEffect> GetActiveBackpackStatusEffects()
    {
#if ! API
        return EquipmentEffectCache.ActiveEffects;
#else
return null;
#endif
    }

    /// <summary>
    /// Retrieves all Status Effects Registered with Adventure Backpacks
    /// </summary>
    /// <returns>HashSet of Status Effects.</returns>
    public static HashSet<StatusEffect> GetRegisterdStatusEffects()
    {
#if ! API
        return EffectsFactory.GetRegisteredEffects();
#else
return null;
#endif
    }

    /// <summary>
    /// Method to activate the backpack on the local player's GUI and open it. Use in conjunction with CanOpenBackpack().
    /// Opens the inventory when it is closed and shows the backpack in its own panel (see GetBackpackPanel); an open
    /// chest stays open.
    /// </summary>
    /// <param name="player">Player, usually Player.m_localPlayer</param>
    /// <param name="gui">The instance of InventoryGui</param>
    public static void OpenBackpack(Player player, InventoryGui gui)
    {
#if ! API
        if (player != null)
            player.OpenBackpack(gui);
#endif
    }

    /// <summary>
    /// Root RectTransform of the backpack panel: the equipped backpack's grid in the inventory screen, shown whenever
    /// the inventory is open and a backpack is worn (with or without a chest). Null until the panel was first opened;
    /// use BackpackPanelCreated to get it as soon as it exists. A layout owner that docks it (RuneSkin
    /// Supports("panel.dock")) positions it; Adventure Backpacks then never moves it nor hides the crafting panel.
    /// </summary>
    /// <returns>The panel root, or null.</returns>
    public static RectTransform GetBackpackPanel()
    {
#if ! API
        return BackpackPanel.Root;
#else
        return null;
#endif
    }

    /// <summary>
    /// The backpack panel's InventoryGrid (a clone of the vanilla container grid, wired to the same InventoryGui
    /// handlers and gamepad group cycling). Null until the panel was first opened.
    /// </summary>
    /// <returns>The panel grid, or null.</returns>
    public static InventoryGrid GetBackpackGrid()
    {
#if ! API
        return BackpackPanel.Grid;
#else
        return null;
#endif
    }

    /// <summary>
    /// The backpack inventory the panel currently shows, null while the panel is closed. Changes made to it are saved
    /// into the backpack item by Adventure Backpacks (Inventory.Changed), so sorters can work on it directly.
    /// </summary>
    /// <returns>The shown backpack inventory, or null.</returns>
    public static Inventory GetBackpackPanelInventory()
    {
#if ! API
        return BackpackPanel.ShownInventory;
#else
        return null;
#endif
    }

    /// <summary>
    /// Whether the backpack panel is currently shown.
    /// </summary>
    /// <returns>true or false</returns>
    public static bool IsBackpackPanelOpen()
    {
#if ! API
        return BackpackPanel.IsOpen;
#else
        return false;
#endif
    }

    /// <summary>
    /// Raised once, on the Unity main thread, when the backpack panel is created (the first time it opens). A handler
    /// added after that is called at once with the existing panel, so subscribing late never misses it.
    /// </summary>
    public static event Action<RectTransform> BackpackPanelCreated
    {
#if ! API
        add => BackpackPanel.Created += value;
        remove => BackpackPanel.Created -= value;
#else
        add { }
        remove { }
#endif
    }

    /// <summary>
    /// Use this method in the Awake() of your mod to register a Status Effect that can be utilized on Adventure Backpacks
    /// </summary>
    /// <param name="effectDefinition">Create a new EffectDefinition that contains the overall parameters that are needed to register the new effect.</param>
    public static void RegisterEffect(EffectDefinition effectDefinition)
    {
#if ! API
        EffectsFactory.RegisterExternalEffect(effectDefinition);
#endif
    }

    /// <summary>
    /// Use this method in the Awake() of your mod to register a new Backpack that can be utilized on Adventure Backpacks.
    /// </summary>
    /// <param name="definition">Create a new BackpackDefinition that contains the overall parameters that are needed to register the new backpack.</param>
    public static void RegisterBackpack(BackpackDefinition definition)
    {
#if ! API
        BackpackFactory.RegisterExternalBackpack(definition);
#endif
    }

    /// <summary>
    /// Whether the backpack panel is currently shown. Same as <see cref="IsBackpackPanelOpen"/>.
    /// </summary>
    /// <returns>true or false</returns>
    public static bool IsBackpackOpen()
    {
        return IsBackpackPanelOpen();
    }

    /// <summary>
    /// Retrieves the Inventory of the equipped backpack on the specified player.
    /// </summary>
    /// <param name="player">Player, usually Player.m_localPlayer</param>
    /// <returns>Inventory object, or null if no backpack equipped</returns>
    public static Inventory GetEquippedBackpackInventory(Player player)
    {
#if ! API
        return player == null ? null : player.GetEquippedBackpack()?.GetInventory();
#else
        return null;
#endif
    }

    /// <summary>
    /// Tries to retrieve the Inventory of the equipped backpack on the specified player.
    /// </summary>
    /// <param name="player">Player, usually Player.m_localPlayer</param>
    /// <param name="inventory">Out parameter for the backpack inventory</param>
    /// <returns>true if backpack equipped and inventory exists, otherwise false</returns>
    public static bool TryGetEquippedBackpackInventory(Player player, out Inventory inventory)
    {
        inventory = GetEquippedBackpackInventory(player);
        return inventory != null;
    }

    /// <summary>
    /// Retrieves the Inventory of a backpack from an ItemDrop.ItemData object.
    /// </summary>
    /// <param name="itemData">ItemDrop.ItemData object</param>
    /// <returns>Inventory object, or null if not a backpack or uninitialized</returns>
    public static Inventory GetBackpackInventory(ItemDrop.ItemData itemData)
    {
        return itemData == null ? null : GetBackpack(itemData)?.Inventory;
    }

    /// <summary>
    /// Tries to retrieve the Inventory of a backpack from an ItemDrop.ItemData object.
    /// </summary>
    /// <param name="itemData">ItemDrop.ItemData object</param>
    /// <param name="inventory">Out parameter for the backpack inventory</param>
    /// <returns>true if item is a backpack and inventory exists, otherwise false</returns>
    public static bool TryGetBackpackInventory(ItemDrop.ItemData itemData, out Inventory inventory)
    {
        inventory = GetBackpackInventory(itemData);
        return inventory != null;
    }

    /// <summary>
    /// Retrieves all distinct backpack inventories of the player: the equipped one and those carried in the inventory.
    /// </summary>
    /// <param name="player">The player to inspect, usually Player.m_localPlayer.</param>
    /// <returns>A list of unique backpack Inventory instances.</returns>
    public static List<Inventory> GetAllBackpackInventories(Player player)
    {
        var inventories = new List<Inventory>();
        if (player == null)
            return inventories;

        var seen = new HashSet<Inventory>();
        var equipped = GetEquippedBackpackInventory(player);
        if (equipped != null && seen.Add(equipped))
            inventories.Add(equipped);

        var playerInventory = player.GetInventory();
        if (playerInventory == null)
            return inventories;

        foreach (var item in playerInventory.GetAllItems())
        {
            if (TryGetBackpackInventory(item, out var inventory) && seen.Add(inventory))
                inventories.Add(inventory);
        }

        return inventories;
    }

    /// <summary>
    /// Whether crafting and building may use the materials in the equipped backpack (local setting).
    /// </summary>
    /// <returns>true or false</returns>
    public static bool IsCraftFromBackpackEnabled()
    {
#if ! API
        return CraftFromBackpack.EnableCraftFromBackpack?.Value ?? false;
#else
        return false;
#endif
    }

    /// <summary>
    /// Whether a requirement check or payment of the local player is running, during which Inventory.CountItems,
    /// HaveItem and RemoveItem(name, ...) of the player's inventory include the equipped backpack.
    /// </summary>
    /// <returns>true or false</returns>
    public static bool IsCraftingContextActive()
    {
#if ! API
        return CraftingContext.IsActive;
#else
        return false;
#endif
    }

    /// <summary>
    /// Switches crafting from the backpack off until <see cref="UnsuppressNativeCrafting"/> is called with the same
    /// identifier, for a mod that pays recipe costs itself.
    /// </summary>
    /// <param name="modIdentifier">Your mod's GUID.</param>
    public static void SuppressNativeCrafting(string modIdentifier)
    {
#if ! API
        CraftingContext.Suppress(modIdentifier);
#endif
    }

    /// <summary>
    /// Ends a <see cref="SuppressNativeCrafting"/> of the same identifier.
    /// </summary>
    /// <param name="modIdentifier">Your mod's GUID.</param>
    public static void UnsuppressNativeCrafting(string modIdentifier)
    {
#if ! API
        CraftingContext.Unsuppress(modIdentifier);
#endif
    }

    /// <summary>
    /// Whether a picked up item would go into the equipped backpack (auto store or inventory overflow).
    /// </summary>
    /// <param name="player">Player, usually Player.m_localPlayer</param>
    /// <param name="item">ItemDrop.ItemData candidate for pickup</param>
    /// <returns>true or false</returns>
    public static bool WouldStorePickup(Player player, ItemDrop.ItemData item)
    {
#if ! API
        return StoreToBackpack.ShouldStoreToBackpack(player, item, out _);
#else
        return false;
#endif
    }
}
