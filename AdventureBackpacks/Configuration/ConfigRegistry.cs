using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace AdventureBackpacks.Configuration
{
    // Owns the plugin's ConfigFile. Entries bound through BindSynced carry Jotunn's
    // ConfigurationManagerAttributes.IsAdminOnly: Jotunn's SynchronizationManager sends their server values
    // to every client and locks them for non-admins. BindLocal entries stay per client.
    public class ConfigRegistry
    {
        private const string SyncedSuffix = " [Synced with Server]";
        private const string LocalSuffix = " [Not Synced with Server]";

        // BepInEx rejects these characters in section and key names.
        private static readonly char[] ForbiddenNameChars = { '\n', '\t', '"', '\'', '[', ']', '=', '\\' };

        private static readonly Regex LocalizationToken = new Regex(@"\$[\w_]+", RegexOptions.Compiled);

        private static ConfigFile _file;
        private static CustomLocalization _localization;

        //Configuration Entry Privates
        internal static ConfigEntry<bool> LoggingEnabled;
        internal static ConfigEntry<LogLevels> LogLevel;
        internal static ConfigEntry<KeyCode> HotKeyOpen;
        internal static ConfigEntry<KeyCode> HotKeyDrop;
        internal static ConfigEntry<bool> OpenWithInventory;
        internal static ConfigEntry<bool> OpenWithHoverInteract;
        internal static ConfigEntry<bool> CloseInventory;
        internal static ConfigEntry<bool> OutwardMode;
        internal static ConfigEntry<bool> ReplaceShader;

        //Buttons registered with Jotunn: polled by name, rebindable through the config entry.
        internal static ButtonConfig OpenBackpackButton;
        internal static ButtonConfig DropBackpackButton;
        private static string _modGuid;

        public static Waiting Waiter;

        public ConfigRegistry(BaseUnityPlugin plugin, ModLogger log, CustomLocalization localization)
        {
            //Waiting For Startup
            Waiter = new Waiting();

            _file = plugin.Config;
            _file.SaveOnConfigSet = true;
            _localization = localization;
            _modGuid = plugin.Info.Metadata.GUID;

            LoggingEnabled = _file.Bind("Log Output Configuration", "Logging Enabled", true,
                new ConfigDescription("Toggles Log Output", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            LogLevel = _file.Bind("Log Output Configuration", "Log Level", LogLevels.Warning,
                new ConfigDescription("Minimum Log Level to Output", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            log.UseFilter(LoggingEnabled, LogLevel);

            WatchConfigFile(Path.GetFileName(_file.ConfigFilePath), log);

            InitializeConfigurationSettings();

            //Polled only from InventoryGui.Update, inside vanilla's own chat/console/menu gate (InventoryGui.cs:518),
            //like vanilla's "Inventory" button, which ZInput does not gate on Player.TakeInput either.
            OpenBackpackButton = AddButton("AdventureBackpacks_OpenBackpack", HotKeyOpen, activeInGui: true);
            DropBackpackButton = AddButton("AdventureBackpacks_QuickdropBackpack", HotKeyDrop);
        }

        // Binds an entry whose value the server dictates.
        internal static void BindSynced<T>(string section, string key, T defaultValue, ConfigDescription description, ref ConfigEntry<T> entry)
        {
            entry = Bind(section, key, defaultValue, description, true);
        }

        // Binds an entry every client keeps for itself.
        internal static void BindLocal<T>(string section, string key, T defaultValue, ConfigDescription description, ref ConfigEntry<T> entry)
        {
            entry = Bind(section, key, defaultValue, description, false);
        }

        private static ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description, bool synced)
        {
            var attributes = description.Tags?.OfType<ConfigurationManagerAttributes>().FirstOrDefault() ?? new ConfigurationManagerAttributes();
            attributes.IsAdminOnly = synced;

            var text = description.Description + (synced ? SyncedSuffix : LocalSuffix);
            return _file.Bind(CleanName(section), CleanName(key), defaultValue,
                new ConfigDescription(text, description.AcceptableValues, attributes));
        }

        private static string CleanName(string name)
        {
            return new string(name.Where(c => Array.IndexOf(ForbiddenNameChars, c) < 0).ToArray());
        }

        // Config section names are written in English whatever the game language: resolves the
        // $tokens of a section name from the mod's English translation.
        internal static string ToEnglish(string text)
        {
            if (string.IsNullOrEmpty(text) || _localization == null)
                return text;

            var english = _localization.GetTranslations("English");
            return LocalizationToken.Replace(text, token =>
                english.TryGetValue(token.Value.Substring(1), out var translated) ? translated : token.Value);
        }

        // Edits made to the .cfg file while the game runs are loaded without a restart.
        private static void WatchConfigFile(string fileName, ModLogger log)
        {
            var watcher = new FileSystemWatcher(Paths.ConfigPath, fileName)
            {
                IncludeSubdirectories = true,
                SynchronizingObject = ThreadingHelper.SynchronizingObject
            };

            void Reload(object sender, FileSystemEventArgs args)
            {
                try
                {
                    _file.Reload();
                }
                catch (Exception e)
                {
                    log.LogError($"Could not reload {fileName}: {e.Message}");
                }
            }

            watcher.Changed += Reload;
            watcher.Deleted += Reload;
            watcher.Renamed += Reload;
            watcher.EnableRaisingEvents = true;
        }

        internal static ButtonConfig AddButton(string name, ConfigEntry<KeyCode> key, bool activeInGui = false)
        {
            //AddButton appends the mod GUID to the name, so poll by ButtonConfig.Name.
            //Config (KeyCode), not ShortcutConfig: Jotunn binds it through ZInput.KeyCodeToPath, which is the
            //physical key. ShortcutConfig additionally gates on UnityEngine.Input, which follows the keyboard layout.
            //activeInGui: without it Jotunn answers GetButtonDown with Player.TakeInput(), which is false while
            //InventoryGui is visible (Player.cs:2670), so a button polled from InventoryGui.Update never fires there.
            var button = new ButtonConfig { Name = name, Config = key, ActiveInGUI = activeInGui };
            InputManager.Instance.AddButton(_modGuid, button);
            return button;
        }

        private static void InitializeConfigurationSettings()
        {
            //User Configs
            BindLocal("Local Config", "Open Backpack", KeyCode.I,
                new ConfigDescription("Hotkey to open backpack.", null, new ConfigurationManagerAttributes { Order = 3 }), ref HotKeyOpen);

            BindLocal("Local Config", "Quickdrop Backpack", KeyCode.Y,
                new ConfigDescription("Hotkey to quickly drop backpack while on the run.",
                    null,
                    new ConfigurationManagerAttributes { Order = 1 }), ref HotKeyDrop);

            BindLocal("Local Config", "Open with Inventory", true,
                new ConfigDescription("If enabled, both backpack and inventory will open when Inventory is opened.",
                    null, new ConfigurationManagerAttributes { Order = 3 }), ref OpenWithInventory);

            BindLocal("Local Config", "Open with Interactive Hover", false,
                new ConfigDescription("If enabled, backpack will only open while hovering over equipped backpack and pressing hotkey.  This option overrides Open with Inventory.",
                    null, new ConfigurationManagerAttributes { Order = 3 }), ref OpenWithHoverInteract);

            BindLocal("Local Config", "Close Inventory", true,
                new ConfigDescription("If enabled, both backpack and inventory will close with Open Backpack keybind is pressed while Inventory is open.",
                    null, new ConfigurationManagerAttributes { Order = 2 }), ref CloseInventory);

            BindLocal("Local Config", "Outward Mode", false,
                new ConfigDescription("You can use a hotkey to quickly drop your equipped backpack in order to run faster away from danger.",
                    null, new ConfigurationManagerAttributes { Order = 1 }), ref OutwardMode);

            BindLocal("Local Config", "Replace Shader", true,
                new ConfigDescription("Toggle To use the Material Shader Replacer (Requires Game Restart)",
                    null, new ConfigurationManagerAttributes { Order = 1 }), ref ReplaceShader);
        }
    }

    public class Waiting
    {
        public void ConfigurationComplete(bool configDone)
        {
            if (configDone)
                StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler StatusChanged;
    }

}
