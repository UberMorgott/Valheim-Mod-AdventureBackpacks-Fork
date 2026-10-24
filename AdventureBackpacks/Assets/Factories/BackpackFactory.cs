using System;
using System.Collections.Generic;
using System.Linq;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Items;
using AdventureBackpacks.Assets.Items.BackpackItems;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Features;
using BepInEx.Configuration;

namespace AdventureBackpacks.Assets.Factories;


internal class BackpackFactory : AssetFactory
{
    private static HashSet<BackpackItem> _backpackItems = new();
    private static bool _initialized;
    private static List<ABAPI.BackpackDefinition> _externalBackpacks = new();

    internal static IEnumerable<BackpackItem> BackpackItems => _backpackItems;


    internal BackpackFactory(ModLogger logger, ConfigRegistry configSync) : base(logger, configSync)
    {
        if (!_initialized)
        {
            BackpackItem.SetConfig(configSync);
            BackpackItem.SetLogger(logger);
            _initialized = true;
        }
    }

    public static void RegisterExternalBackpack(ABAPI.BackpackDefinition backpackDefinition)
    {
        _externalBackpacks.Add(backpackDefinition);
    }


    // Modern models ship as "backpack_x", the original ones as "classic_backpack_x". Read before the backpack
    // exists, because the bundle is loaded in its constructor; a change applies after a restart.
    private static string ResolveModelBundle(string baseBundleName, string itemName, out ConfigEntry<ModelStyle> styleEntry)
    {
        var configSection = $"Backpack: {itemName}";
        var englishSection = ConfigRegistry.ToEnglish(configSection);
        var localizedCategory = Localization.instance.Localize(configSection);

        ConfigEntry<ModelStyle> modelStyle = null;
        ConfigRegistry.BindLocal(englishSection, "Model Style", ModelStyle.Modern,
            new ConfigDescription("Select the visual model style for this backpack. Modern features detailed models. Classic uses the original mod model. (Requires Game Restart)",
                null,
                new ConfigurationManagerAttributes { Category = localizedCategory, Order = 20, DispName = "Model Style (Requires Game Restart)" }), ref modelStyle);

        modelStyle.SettingChanged += (_, _) =>
        {
            AdventureBackpacks.Log.Message($"Model Style changed for '{englishSection}' to {modelStyle.Value}. A game restart is required for changes to take effect.");
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Adventure Backpacks: Game restart required for model changes to take effect.");
        };

        styleEntry = modelStyle;
        return modelStyle.Value == ModelStyle.Classic && HasClassicModel(baseBundleName) ? $"classic_{baseBundleName}" : baseBundleName;
    }

    private static bool HasClassicModel(string baseBundleName)
    {
        var resource = $"{Utilities.EmbeddedFolder}.classic_{baseBundleName}";
        return typeof(BackpackFactory).Assembly.GetManifestResourceNames().Any(name => name.EndsWith(resource, StringComparison.Ordinal));
    }

    private static T CreateBackpack<T>(string baseBundleName, string prefabName, string itemName, Func<string, string, string, T> factory) where T : BackpackItem
    {
        var bundleName = ResolveModelBundle(baseBundleName, itemName, out var modelStyle);
        var backpack = factory(bundleName, prefabName, itemName);
        backpack.ModelStyle = modelStyle;
        return backpack;
    }

    internal override void CreateAssets()
    {
        _backpackItems.Add(CreateBackpack("backpack_meadows", "BackpackMeadows", "$adventurebackpacks_item_backpack_meadows",
            (bundle, prefab, item) => new BackpackMeadows(bundle, prefab, item)));
        _backpackItems.Add(CreateBackpack("backpack_black_forest", "BackpackBlackForest", "$adventurebackpacks_item_backpack_blackforest",
            (bundle, prefab, item) => new BackpackBlackForest(bundle, prefab, item)));
        _backpackItems.Add(CreateBackpack("backpack_swamp", "BackpackSwamp", "$adventurebackpacks_item_backpack_swamp",
            (bundle, prefab, item) => new BackpackSwamp(bundle, prefab, item)));
        _backpackItems.Add(CreateBackpack("backpack_mountains", "BackpackMountains", "$adventurebackpacks_item_backpack_mountains",
            (bundle, prefab, item) => new BackpackMountains(bundle, prefab, item)));
        _backpackItems.Add(CreateBackpack("backpack_plains", "BackpackPlains", "$adventurebackpacks_item_backpack_plains",
            (bundle, prefab, item) => new BackpackPlains(bundle, prefab, item)));
        _backpackItems.Add(CreateBackpack("backpack_mistlands", "BackpackMistlands", "$adventurebackpacks_item_backpack_mistlands",
            (bundle, prefab, item) => new BackpackMistlands(bundle, prefab, item)));
        _backpackItems.Add(new BackpackAshlands("backpack_ashlands", "BackpackAshlands", "$adventurebackpacks_item_backpack_ashlands"));
        _backpackItems.Add(new BackpackDeepNorth("backpack_deepnorth", "BackpackDeepNorth", "$adventurebackpacks_item_backpack_deepnorth"));
        _backpackItems.Add(new LegacyIronBackpack("adventurebackpacks", "CapeIronBackpack", "$adventurebackpacks_item_rugged_backpack"));
        _backpackItems.Add(new LegacySilverBackpack("adventurebackpacks", "CapeSilverBackpack", "$adventurebackpacks_item_arctic_backpack"));

        foreach (var backpackDefinition in _externalBackpacks)
        {
            if (_backpackItems.Any(x => x.ItemName.Equals(backpackDefinition.ItemName, StringComparison.Ordinal))) return;

            var newBackpack = backpackDefinition.BackPackGo != null ?
                new ExternalBackpack(backpackDefinition, backpackDefinition.BackPackGo) :
                new ExternalBackpack(backpackDefinition);

            _backpackItems.Add(newBackpack);

        }
    }

    internal static List<string> BackpackTypes()
    {
        return BackpackItems.Select(x => x.ItemName).ToList();
    }
}
