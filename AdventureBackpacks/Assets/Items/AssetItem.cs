using System;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Features;
using Jotunn.Entities;
using UnityEngine;

namespace AdventureBackpacks.Assets.Items;

internal interface IAssetItem
{
    string PrefabName { get; }
    string ItemName { get; }
    BackpackRecipe Recipe { get; }
}
internal abstract class AssetItem : IAssetItem
{

    private readonly BackpackRecipe _recipe;
    private readonly GameObject _prefab;

    public string AssetName { get; }
    public string PrefabName { get; }

    public string ItemName { get; }

    public BackpackRecipe Recipe => _recipe;

    public CustomItem CustomItem { get; }

    protected AssetItem(GameObject goItem, string itemName)
    {
        _prefab = goItem;
        PrefabName = goItem.name;
        ItemName = itemName;

        _recipe = new BackpackRecipe(goItem);

        SetPersistence();
        ResetPrefabArmor();

        // Jotunn owns the prefab: ZNetScene on every ZNetScene.Awake, ObjectDB on every menu/world ObjectDB.
        // The recipe is created later from the config values (BackpackRecipe.RegisterAll).
        CustomItem = new CustomItem(goItem, fixReference: false);
        Jotunn.Managers.ItemManager.Instance.AddItem(CustomItem);
    }

    protected AssetItem(AssetBundle bundle, string prefabName, string itemName)
        : this(bundle.LoadAsset<GameObject>(prefabName), itemName)
    {
    }

    protected AssetItem(string assetName, string prefabName, string itemName)
        : this(Utilities.LoadAssetBundle(assetName), prefabName, itemName)
    {
        AssetName = assetName;
    }

    internal void AssignCraftingTable(CraftingTable craftingTable, int stationLevel)
    {
        _recipe.SetStation(craftingTable, stationLevel);
    }

    internal void AssignCraftingTable(string craftingTable, int stationLevel)
    {
        if (Enum.TryParse<CraftingTable>(craftingTable, true, out var tableEnum))
            _recipe.SetStation(tableEnum, stationLevel);
        else
            _recipe.SetCustomStation(craftingTable, stationLevel);
    }

    internal void AddRecipeIngredient(string prefabName, int quantity)
    {
        _recipe.AddCraftCost(prefabName, quantity);
    }

    internal void AddUpgradeIngredient(string prefabName, int quantity)
    {
        _recipe.AddUpgradeCost(prefabName, quantity);
    }

    internal ItemDrop GetItemDrop()
    {
        return _prefab.GetComponent<ItemDrop>();
    }

    internal void RegisterShaderSwap()
    {
        if (!ConfigRegistry.ReplaceShader.Value)
            return;

        PieceShaderSwap.Register(_prefab);
    }

    internal void SetPersistence()
    {
        _prefab.GetComponent<ZNetView>().m_persistent = true;
    }

    internal void ResetPrefabArmor()
    {
        var itemDrop = GetItemDrop();
        var itemData = itemDrop.m_itemData;
        if (itemData != null)
        {
            itemDrop.m_autoPickup = true;
            itemData.m_shared.m_armor = itemData.m_shared.m_armorPerLevel;
            itemDrop.Save();
        }
    }
}
