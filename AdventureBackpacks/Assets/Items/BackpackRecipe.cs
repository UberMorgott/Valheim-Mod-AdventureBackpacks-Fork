using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using AdventureBackpacks.Configuration;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace AdventureBackpacks.Assets.Items;

// Member names are the values users write into "Crafting Station".
public enum CraftingTable
{
    Disabled,
    Inventory,
    Workbench,
    Cauldron,
    Forge,
    ArtisanTable,
    StoneCutter,
    MageTable,
    BlackForge,
    FoodPreparationTable,
    MeadKetill,
    Custom
}

// Member names are the values users write into "Require only one resource".
public enum Toggle
{
    Off,
    On
}

// Crafting recipe and creature drops of one backpack, both editable through the backpack's config section.
// The recipe is a vanilla Recipe registered with Jotunn (added to every ObjectDB); its fields are filled from
// the config once the world's ObjectDB holds every item (ItemManager.OnItemsRegistered) and again whenever a
// setting changes, including values the server pushes through Jotunn's config sync.
internal sealed class BackpackRecipe
{
    private struct Cost
    {
        public string Item;
        public int Amount;
        public int Quality;
    }

    private struct DropTarget
    {
        public string Creature;
        public float Chance;
        public int Min;
        public int Max;
        public bool LevelMultiplier;
    }

    private static readonly List<BackpackRecipe> All = new();
    private static readonly Dictionary<ItemDrop, BackpackRecipe> ByItem = new();

    // Upgrade costs written as "item:amount:quality" apply to that one quality level only.
    private static readonly ConditionalWeakTable<Piece.Requirement, object> QualityOnly = new();

    private static bool _registered;

    private readonly GameObject _prefab;
    private readonly ItemDrop _itemDrop;
    private readonly List<Cost> _craftCosts = new();
    private readonly List<Cost> _upgradeCosts = new();
    private readonly List<DropTarget> _drops = new();
    private readonly List<(CharacterDrop Owner, CharacterDrop.Drop Drop)> _addedDrops = new();

    private CraftingTable? _table;
    private string _customTable = "";
    private int _stationLevel = 1;

    private Recipe _recipe;
    private ConfigEntry<CraftingTable> _tableConfig;
    private ConfigEntry<string> _customTableConfig;
    private ConfigEntry<int> _stationLevelConfig;
    private ConfigEntry<int> _maxStationLevelConfig;
    private ConfigEntry<Toggle> _oneResourceConfig;
    private ConfigEntry<float> _qualityMultiplierConfig;
    private ConfigEntry<string> _craftCostsConfig;
    private ConfigEntry<string> _upgradeCostsConfig;
    private ConfigEntry<bool> _dropsEnabledConfig;
    private ConfigEntry<string> _dropsConfig;

    // Config section; its $tokens are resolved to English.
    public string SectionName = string.Empty;

    // Binds the recipe and drop settings in the config.
    public bool Configurable;

    // Highest crafting station level an upgrade or repair asks for; unset = station level + max quality - 1.
    public int MaximumRequiredStationLevel = int.MaxValue;

    public BackpackRecipe(GameObject prefab)
    {
        _prefab = prefab;
        _itemDrop = prefab.GetComponent<ItemDrop>();
        _itemDrop.m_itemData.m_dropPrefab = prefab;
        All.Add(this);
        ByItem[_itemDrop] = this;
    }

    public GameObject Prefab => _prefab;

    private int MaxQuality => _itemDrop.m_itemData.m_shared.m_maxQuality;

    public void SetStation(CraftingTable table, int level)
    {
        _table = table;
        _stationLevel = level;
    }

    public void SetCustomStation(string stationPrefab, int level)
    {
        _table = CraftingTable.Custom;
        _customTable = stationPrefab;
        _stationLevel = level;
    }

    public void AddCraftCost(string item, int amount)
    {
        _craftCosts.Add(new Cost { Item = item, Amount = amount });
    }

    public void AddUpgradeCost(string item, int amount)
    {
        _upgradeCosts.Add(new Cost { Item = item, Amount = amount });
    }

    public void AddDrop(string creature, float chance, int min = 1, int? max = null)
    {
        _drops.Add(new DropTarget { Creature = creature, Chance = chance, Min = min, Max = max ?? min, LevelMultiplier = true });
    }

    // Binds the settings and registers the recipes of every backpack created so far. Call once, after all
    // backpacks exist and the translations are loaded.
    public static void RegisterAll()
    {
        if (_registered)
            return;
        _registered = true;

        foreach (var recipe in All)
        {
            if (recipe.Configurable)
                recipe.BindConfig();
            recipe.CreateRecipe();
        }

        ItemManager.OnItemsRegistered += ApplyAllRecipes;
        PrefabManager.OnPrefabsRegistered += ApplyAllDrops;
    }

    private void BindConfig()
    {
        _table ??= CraftingTable.Disabled;

        var english = ConfigRegistry.ToEnglish(SectionName).Replace("'", "").Replace("[", "").Replace("\"", "").Replace("]", "").Trim();
        var category = Localization.instance.Localize(SectionName).Trim();
        var order = 0;

        ConfigurationManagerAttributes Attributes() => new() { Order = --order, Category = category };

        ConfigRegistry.BindSynced(english, "Crafting Station", _table.Value,
            new ConfigDescription($"Crafting station where {english} is available.", null, Attributes()), ref _tableConfig);
        ConfigRegistry.BindSynced(english, "Custom Crafting Station", _customTable ?? "",
            new ConfigDescription("", null, Attributes()), ref _customTableConfig);
        ConfigRegistry.BindSynced(english, "Crafting Station Level", _stationLevel,
            new ConfigDescription($"Required crafting station level to craft {english}.", null, Attributes()), ref _stationLevelConfig);

        if (MaxQuality > 1)
        {
            var maxLevel = MaximumRequiredStationLevel == int.MaxValue ? _stationLevel + MaxQuality - 1 : MaximumRequiredStationLevel;
            ConfigRegistry.BindSynced(english, "Maximum Crafting Station Level", maxLevel,
                new ConfigDescription($"Maximum crafting station level to upgrade and repair {english}.", null, Attributes()), ref _maxStationLevelConfig);
        }

        ConfigRegistry.BindSynced(english, "Require only one resource", Toggle.Off,
            new ConfigDescription($"Whether only one of the ingredients is needed to craft {english}", null, Attributes()), ref _oneResourceConfig);
        ConfigRegistry.BindSynced(english, "Quality Multiplier", 1f,
            new ConfigDescription($"Multiplies the crafted amount based on the quality of the resources when crafting {english}. Only works, if Require Only One Resource is true.", null, Attributes()), ref _qualityMultiplierConfig);

        ConfigRegistry.BindSynced(english, "Crafting Costs", FormatCosts(_craftCosts),
            new ConfigDescription($"Item costs to craft {english}", null, Attributes()), ref _craftCostsConfig);
        if (MaxQuality > 1)
            ConfigRegistry.BindSynced(english, "Upgrading Costs", FormatCosts(_upgradeCosts),
                new ConfigDescription($"Item costs per level to upgrade {english}", null, Attributes()), ref _upgradeCostsConfig);

        ConfigRegistry.BindSynced(english, "Drops Enabled", false,
            new ConfigDescription($"Enables {english} drops", null, new ConfigurationManagerAttributes { Category = category }), ref _dropsEnabledConfig);
        ConfigRegistry.BindSynced(english, "Drops from", FormatDrops(_drops),
            new ConfigDescription($"Creatures {english} drops from", null, new ConfigurationManagerAttributes { Category = category }), ref _dropsConfig);

        OnChange(_tableConfig, OnRecipeSettingChanged);
        OnChange(_customTableConfig, OnRecipeSettingChanged);
        OnChange(_stationLevelConfig, OnRecipeSettingChanged);
        OnChange(_maxStationLevelConfig, OnRecipeSettingChanged);
        OnChange(_oneResourceConfig, OnRecipeSettingChanged);
        OnChange(_qualityMultiplierConfig, OnRecipeSettingChanged);
        OnChange(_craftCostsConfig, OnRecipeSettingChanged);
        OnChange(_upgradeCostsConfig, OnRecipeSettingChanged);
        OnChange(_dropsEnabledConfig, OnDropSettingChanged);
        OnChange(_dropsConfig, OnDropSettingChanged);

        UpdateVisibility();
    }

    private static void OnChange<T>(ConfigEntry<T> entry, Action handler)
    {
        if (entry != null)
            entry.SettingChanged += (_, _) => handler();
    }

    // ConfigurationManager lists only the settings that matter for the chosen station.
    private void UpdateVisibility()
    {
        var table = _tableConfig.Value;
        SetBrowsable(_customTableConfig, table == CraftingTable.Custom);
        foreach (var entry in new ConfigEntryBase[] { _stationLevelConfig, _maxStationLevelConfig, _oneResourceConfig, _craftCostsConfig, _upgradeCostsConfig })
            SetBrowsable(entry, table != CraftingTable.Disabled);
        SetBrowsable(_qualityMultiplierConfig, table != CraftingTable.Disabled && _oneResourceConfig.Value == Toggle.On);
    }

    private static void SetBrowsable(ConfigEntryBase entry, bool browsable)
    {
        var attributes = entry?.Description.Tags.OfType<ConfigurationManagerAttributes>().FirstOrDefault();
        if (attributes != null)
            attributes.Browsable = browsable;
    }

    private void CreateRecipe()
    {
        if (_table == null)
            return;

        _recipe = ScriptableObject.CreateInstance<Recipe>();
        _recipe.name = $"{_prefab.name}_Recipe_{_table.Value}";
        _recipe.m_item = _itemDrop;
        _recipe.m_enabled = false;
        ItemManager.Instance.AddRecipe(new CustomRecipe(_recipe, fixReference: false, fixRequirementReferences: false));
    }

    private void OnRecipeSettingChanged()
    {
        UpdateVisibility();
        if (ObjectDB.instance && ZNetScene.instance)
            ApplyRecipe(ObjectDB.instance);
    }

    private void OnDropSettingChanged()
    {
        if (ZNetScene.instance)
            ApplyDrops(ZNetScene.instance);
    }

    private static void ApplyAllRecipes()
    {
        foreach (var recipe in All)
            recipe.ApplyRecipe(ObjectDB.instance);
    }

    private static void ApplyAllDrops()
    {
        foreach (var recipe in All)
            recipe.ApplyDrops(ZNetScene.instance);
    }

    private CraftingTable Table => _tableConfig?.Value ?? _table ?? CraftingTable.Disabled;

    private int MaxStationLevel => _maxStationLevelConfig?.Value ?? MaximumRequiredStationLevel;

    private void ApplyRecipe(ObjectDB objectDB)
    {
        if (_recipe == null || !objectDB)
            return;

        var table = Table;
        _recipe.m_amount = 1;
        _recipe.m_enabled = table != CraftingTable.Disabled;
        _recipe.m_craftingStation = FindStation(table, _customTableConfig?.Value ?? _customTable);
        _recipe.m_minStationLevel = _stationLevelConfig?.Value ?? _stationLevel;
        _recipe.m_requireOnlyOneIngredient = _oneResourceConfig?.Value == Toggle.On;
        _recipe.m_qualityResultAmountMultiplier = _qualityMultiplierConfig?.Value ?? 1f;

        var craft = _craftCostsConfig != null ? ParseCosts(_craftCostsConfig.Value) : _craftCosts;
        var upgrade = _upgradeCostsConfig != null ? ParseCosts(_upgradeCostsConfig.Value) : _upgradeCosts;
        _recipe.m_resources = BuildRequirements(objectDB, craft, upgrade);
    }

    private static CraftingStation FindStation(CraftingTable table, string customTable)
    {
        var prefabName = table switch
        {
            CraftingTable.Workbench => "piece_workbench",
            CraftingTable.Cauldron => "piece_cauldron",
            CraftingTable.Forge => "forge",
            CraftingTable.ArtisanTable => "piece_artisanstation",
            CraftingTable.StoneCutter => "piece_stonecutter",
            CraftingTable.MageTable => "piece_magetable",
            CraftingTable.BlackForge => "blackforge",
            CraftingTable.FoodPreparationTable => "piece_preptable",
            CraftingTable.MeadKetill => "piece_MeadCauldron",
            CraftingTable.Custom => customTable,
            _ => null
        };

        if (string.IsNullOrEmpty(prefabName))
            return null;

        var station = PrefabManager.Instance.GetPrefab(prefabName)?.GetComponent<CraftingStation>();
        if (station == null)
            AdventureBackpacks.Log.Warning($"The crafting station '{prefabName}' does not exist.");
        return station;
    }

    // Craft costs are paid once (m_amount); upgrade costs per level (m_amountPerLevel, Piece.cs:102-110).
    // An item listed in both gets one requirement carrying both amounts.
    private static Piece.Requirement[] BuildRequirements(ObjectDB objectDB, List<Cost> craft, List<Cost> upgrade)
    {
        var result = new List<Piece.Requirement>();
        var byItem = new Dictionary<string, Piece.Requirement>();

        foreach (var cost in craft)
        {
            if (byItem.ContainsKey(cost.Item) || !TryFindItem(objectDB, cost.Item, out var itemDrop))
                continue;

            var requirement = new Piece.Requirement { m_resItem = itemDrop, m_amount = cost.Amount, m_amountPerLevel = 0 };
            byItem[cost.Item] = requirement;
            result.Add(requirement);
        }

        foreach (var cost in upgrade)
        {
            if (cost.Quality > 0)
            {
                if (!TryFindItem(objectDB, cost.Item, out var qualityItem))
                    continue;

                var qualityRequirement = new Piece.Requirement { m_resItem = qualityItem, m_amount = 0, m_amountPerLevel = cost.Amount };
                QualityOnly.Add(qualityRequirement, cost.Quality);
                result.Add(qualityRequirement);
                continue;
            }

            if (!byItem.TryGetValue(cost.Item, out var requirement))
            {
                if (!TryFindItem(objectDB, cost.Item, out var itemDrop))
                    continue;

                requirement = new Piece.Requirement { m_resItem = itemDrop, m_amount = 0 };
                byItem[cost.Item] = requirement;
                result.Add(requirement);
            }
            requirement.m_amountPerLevel = cost.Amount;
        }

        return result.ToArray();
    }

    private static bool TryFindItem(ObjectDB objectDB, string name, out ItemDrop itemDrop)
    {
        itemDrop = objectDB.GetItemPrefab(name)?.GetComponent<ItemDrop>();
        if (itemDrop == null)
            AdventureBackpacks.Log.Warning($"The required item '{name}' does not exist.");
        return itemDrop != null;
    }

    // "item:amount[:quality],..." - amount defaults to 1, quality to 0 (every level).
    private static List<Cost> ParseCosts(string text)
    {
        var costs = new List<Cost>();
        foreach (var part in text.Split(','))
        {
            var fields = part.Split(':');
            var item = fields[0].Trim();
            if (item.Length == 0)
                continue;

            costs.Add(new Cost
            {
                Item = item,
                Amount = fields.Length > 1 && int.TryParse(fields[1], out var amount) ? amount : 1,
                Quality = fields.Length > 2 && int.TryParse(fields[2], out var quality) ? quality : 0
            });
        }
        return costs;
    }

    private static string FormatCosts(List<Cost> costs)
    {
        return string.Join(",", costs.Select(c => $"{c.Item}:{c.Amount}"));
    }

    // "creature:chance:min:max[:0],..." - max is left empty when it equals min; a trailing ":0" turns off
    // the creature level multiplier.
    private static List<DropTarget> ParseDrops(string text)
    {
        var drops = new List<DropTarget>();
        foreach (var part in text.Split(','))
        {
            var fields = part.Split(':');
            var creature = fields[0].Trim();
            if (creature.Length == 0)
                continue;

            var min = fields.Length > 2 && int.TryParse(fields[2], out var parsedMin) ? parsedMin : 1;
            drops.Add(new DropTarget
            {
                Creature = creature,
                Chance = fields.Length > 1 && float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var chance) ? chance : 1f,
                Min = min,
                Max = fields.Length > 3 && int.TryParse(fields[3], out var parsedMax) ? parsedMax : min,
                LevelMultiplier = fields.Length <= 4 || fields[4] != "0"
            });
        }
        return drops;
    }

    private static string FormatDrops(List<DropTarget> drops)
    {
        return string.Join(",", drops.Select(d =>
            $"{d.Creature}:{d.Chance.ToString(CultureInfo.InvariantCulture)}:{d.Min}:{(d.Min == d.Max ? "" : d.Max.ToString())}{(d.LevelMultiplier ? "" : ":0")}"));
    }

    // Creature prefabs live in ZNetScene for the whole session, so the drops this backpack added before
    // are taken out first and the configured ones put in again.
    private void ApplyDrops(ZNetScene netScene)
    {
        foreach (var (owner, drop) in _addedDrops)
        {
            if (owner)
                owner.m_drops.Remove(drop);
        }
        _addedDrops.Clear();

        var enabled = _dropsEnabledConfig?.Value ?? false;
        if (!enabled || !netScene)
            return;

        var targets = _dropsConfig != null ? ParseDrops(_dropsConfig.Value) : _drops;
        foreach (var target in targets)
        {
            var creature = netScene.GetPrefab(target.Creature);
            if (!creature || !creature.GetComponent<Character>())
            {
                AdventureBackpacks.Log.Debug($"The drop target character '{target.Creature}' does not exist.");
                continue;
            }

            var characterDrop = creature.GetComponent<CharacterDrop>();
            if (!characterDrop)
                characterDrop = creature.AddComponent<CharacterDrop>();
            var drop = new CharacterDrop.Drop
            {
                m_prefab = _prefab,
                m_amountMin = target.Min,
                m_amountMax = target.Max,
                m_chance = target.Chance,
                m_levelMultiplier = target.LevelMultiplier
            };
            characterDrop.m_drops.Add(drop);
            _addedDrops.Add((characterDrop, drop));
            AdventureBackpacks.Log.Debug($"{_prefab.name} drops from {target.Creature}: chance {target.Chance}, amount {target.Min}-{target.Max}");
        }
    }

    // Vanilla asks for one more station level per quality (Recipe.cs:29-32); the config caps it.
    [HarmonyPatch(typeof(Recipe), nameof(Recipe.GetRequiredStationLevel))]
    private static class RecipeGetRequiredStationLevelPatch
    {
        [UsedImplicitly]
        private static void Postfix(Recipe __instance, ref int __result)
        {
            if (__instance.m_item != null && ByItem.TryGetValue(__instance.m_item, out var recipe))
                __result = Mathf.Min(__result, recipe.MaxStationLevel);
        }
    }

    // A quality-bound upgrade cost is due only when upgrading to that quality.
    [HarmonyPatch(typeof(Piece.Requirement), nameof(Piece.Requirement.GetAmount))]
    private static class RequirementGetAmountPatch
    {
        [UsedImplicitly]
        private static bool Prefix(Piece.Requirement __instance, int qualityLevel, ref int __result)
        {
            if (!QualityOnly.TryGetValue(__instance, out var quality))
                return true;

            __result = (int)quality == qualityLevel ? __instance.m_amountPerLevel : 0;
            return false;
        }
    }
}
