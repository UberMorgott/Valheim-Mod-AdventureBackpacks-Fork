/* Adventure Backpacks by Vapok */

using System;
using System.Reflection;
using AdventureBackpacks.Assets;
using AdventureBackpacks.Assets.Factories;
using AdventureBackpacks.Assets.Items;
using AdventureBackpacks.Compats;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;
using AdventureBackpacks.Features;
using AdventureBackpacks.Patches;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using JetBrains.Annotations;
using Jotunn.Managers;
using Jotunn.Utils;

namespace AdventureBackpacks
{
    [BepInPlugin(_pluginId, _displayName, _version)]
    [BepInIncompatibility("JotunnBackpacks")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency("com.ValheimModding.YamlDotNetDetector")]
    [BepInDependency("com.chebgonaz.ChebsNecromancy", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.maxsch.valheim.contentswithin", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    public class AdventureBackpacks : BaseUnityPlugin
    {
        //Module Constants
        private const string _pluginId = "vapok.mods.adventurebackpacks";
        private const string _displayName = "Adventure Backpacks";
        private const string _version = "1.9.14.6";


        //Class Properties
        public static ModLogger Log => _log;
        public static bool PerformYardSale = false;
        public static bool QuickDropping = false;
        public static bool BypassMoveProtection = false;
        public static Waiting Waiter;
        public static ConfigRegistry ActiveConfig => _config;

        //Class Privates
        private static AdventureBackpacks _instance;
        private static ConfigRegistry _config;
        private static ModLogger _log;
        private Harmony _harmony;


        [UsedImplicitly]
        // This the main function of the mod. BepInEx will call this.
        private void Awake()
        {
            //I'm awake!
            _instance = this;

            //Mods built against the AdventureBackpacksAPI stub assembly bind to this plugin instead.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveApiAssembly;

            //Waiting For Startup
            Waiter = new Waiting();

            //Jotunn Localization: English is embedded; other languages are side-loaded by Jotunn
            //from Translations/<Language>/AdventureBackpacks.json next to the plugin.
            var localization = LocalizationManager.Instance.GetLocalization();
            localization.AddJsonFile("English", AssetUtils.LoadTextFromResources("Translations.English.json", typeof(AdventureBackpacks).Assembly));

            //Register Logger
            _log = new ModLogger(_pluginId);

            //Register Configuration Settings (English config section names are looked up in this localization)
            _config = new ConfigRegistry(_instance, _log, localization);

            //Patch Harmony one class at a time: a single stale target must not abort Awake and
            //silently disable every other patch in the mod.
            _harmony = new Harmony(Info.Metadata.GUID);
            foreach (var patchClass in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                try
                {
                    _harmony.CreateClassProcessor(patchClass).Patch();
                }
                catch (Exception e)
                {
                    _log.LogError($"Harmony patch class {patchClass.FullName} failed to apply, skipping it: {e.Message}");
                }
            }

            //Backpacks need vanilla prefabs (recipe items, crafting stations) and loaded translations.
            PrefabManager.OnVanillaPrefabsAvailable += InitializeBackpacks;
            //Every menu ObjectDB with our items in it: effects resolve their vanilla status effects.
            Jotunn.Managers.ItemManager.OnItemsRegisteredFejd += () => Waiter.ValheimIsAwake(true);

            //Compatibilities
            if (Chainloader.PluginInfos.ContainsKey("com.chebgonaz.ChebsNecromancy"))
            {
                ChebsNecromancy.SetupNecromancyBackpackUsingApi();
            }

            if (Chainloader.PluginInfos.ContainsKey("com.maxsch.valheim.contentswithin"))
            {
                ContentsWithin.Awake(_harmony, "com.maxsch.valheim.contentswithin");
            }

            // EQS is detected lazily on the first Player.Awake: a BepInDependency edge here would
            // close a cycle (EQS -> EpicLoot -> AdventureBackpacks) and kill the whole chainloader.

            //???

            //Profit
        }


        private void Start()
        {
            //Initialized Features
            QuickTransfer.FeatureInitialized = true;
        }

        private void Update()
        {
            if (!Player.m_localPlayer || !ZNetScene.instance)
                return;

            if (PerformYardSale)
            {
                var backpack = Player.m_localPlayer.GetEquippedBackpack();
                if (backpack != null)
                    Backpacks.PerformYardSale(Player.m_localPlayer, backpack.Item);
            }

            if ((ZInput.GetButton("Forward") || ZInput.GetButton("Backward") || ZInput.GetButton("Left") || ZInput.GetButton("Right"))
                && ZInput.GetButtonDown(ConfigRegistry.DropBackpackButton.Name) && ConfigRegistry.OutwardMode.Value)
            {
                Player.m_localPlayer.QuickDropBackpack();
            }

            EffectsFactory.Instance.ToggleEffects();

            InventoryPatches.ProcessItemsAddedQueue();
        }

        private void InitializeBackpacks()
        {
            //Once: the event fires on every menu start.
            PrefabManager.OnVanillaPrefabsAvailable -= InitializeBackpacks;

            //Register Effects
            var effectsFactory = new EffectsFactory(_log, _config);
            effectsFactory.RegisterEffects();

            //Register Assets
            var backpackFactory = new BackpackFactory(_log, _config);
            backpackFactory.CreateAssets();

            //Setup Backpack Types
            Backpacks.LoadBackpackTypes(BackpackFactory.BackpackTypes());


            ConfigRegistry.Waiter.ConfigurationComplete(true);

            //Recipe/drop configs and the Jotunn recipes of every backpack
            BackpackRecipe.RegisterAll();
        }

        private static Assembly ResolveApiAssembly(object sender, ResolveEventArgs args)
        {
            return new AssemblyName(args.Name).Name == "AdventureBackpacksAPI" ? typeof(AdventureBackpacks).Assembly : null;
        }

        private void OnDestroy()
        {
            _instance = null;
        }

        public class Waiting
        {
            public void ValheimIsAwake(bool awakeFlag)
            {
                if (awakeFlag)
                    StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            public event EventHandler StatusChanged;
        }
    }
}
