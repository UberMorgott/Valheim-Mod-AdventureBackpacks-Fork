# Adventure Backpacks — Morgott's personal fork

**Personal fork for Valheim 1.0.7**, maintained by **Morgott** ([UberMorgott](https://github.com/UberMorgott)).
Plugin version **1.9.13.4**, plugin GUID unchanged: `vapok.mods.adventurebackpacks`, so existing configs and
equipped backpacks keep working.

Credit where it is due:

- **Original mod and all gameplay design:** **Vapok** (Pete Navarra) — [github.com/Vapok/AdventureBackpacks](https://github.com/Vapok/AdventureBackpacks) · [Vapok's Mods Discord](https://discord.gg/5YAJkRFBXt)
- **HD backpack assets:** **psionprime**, from [AdventureBackpacksHD](https://github.com/psionprime/AdventureBackpacksHD)
- **This fork:** Valheim 1.0.7 compatibility, patch robustness, and EquipmentAndQuickSlots integration

Licensed under the MIT licence of the upstream project. `LICENSE.md` carries Vapok's copyright notice and is
preserved unchanged, as the licence requires.

This fork is not published to Thunderstore. `manifest.json` still reports the upstream package version `1.9.13`
because Thunderstore only accepts three-part versions; the runtime plugin version is `1.9.13.4`.

---

## What this fork changes on top of the HD fork (Sep 2026)

| Area | Summary |
|------|---------|
| **Valheim 1.0.7 targets** | Harmony patch targets resolved against the real 1.0.7 assembly: `Inventory.AddItem` 10-argument overload, `Inventory.IsTeleportable(bool)`, `InventoryElement.Position`. |
| **Patch isolation** | Harmony patches are applied class by class inside a try/catch. One stale target now logs its class name and is skipped instead of aborting `Awake` and silently disabling every other patch. |
| **Foreign exceptions** | The finalizer on `InventoryGui.OnSelectedItem` only swallows its own backpack NRE. Any other mod's exception is logged and rethrown untouched. |
| **Patch hygiene** | `EnvMan.IsCold`, `EnvMan.IsWet`, `ItemDrop.ItemData.GetIcon` and `ItemDrop.ItemData.GetWeight` are postfixes rather than skipping prefixes or transpilers. The remaining transpilers are anchored with `CodeMatcher` on real method calls and log an explicit error when the anchor is gone. |
| **Cheaper hot path** | The `SEMan.RemoveStatusEffect` prefix returns early for anything that is not the local player's `SEMan`. |
| **EquipmentAndQuickSlots** | Optional integration: with EQS installed, backpacks get their own `Backpack` equipment slot instead of competing with capes. Detected by reflection, no assembly reference, and the vanilla `ItemType.Shoulder` path still works without EQS. |

---

## What the HD fork changed (Jul 2026, psionprime)

Work lives under **`AdventureBackpacksHD/`** in this workspace. Detailed build/deploy notes: **[`advice.md`](../advice.md)** (workspace root).

| Area | Summary |
|------|---------|
| **HD Rugged (iron) mesh** | Replaced legacy 128×128 cape mesh with a Meshy AI model; preserved asset GUIDs, bind poses, and bone-weight transfer. Automated via `ReplaceIronBackpackFromMeshy.cs`. |
| **Equipped fit** | Tuned scale, rotation, and offset on the skinned mesh; preview in Unity without launching Valheim. |
| **Material** | `IronBackpack.mat` uses Unity **Standard** (Valheim triplanar/DS shaders broke visibility); lowered metallic/smoothness for in-game glare. |
| **Inventory icon** | Runtime **`IronBackpackIconFix`** embeds a 64×64 PNG in the DLL and patches **`BackpackBlackForest`** + **`CapeIronBackpack`** (in-game “Rugged Backpack” is the Black Forest item). Unity renders the icon with tuned camera/lighting. |
| **Drop particles** | Meadows-style cinders: **only on the ground** (`log` / `DropParticles` child), not when equipped; inverse scale compensates `log` ×100. |
| **Build / deploy** | Unity `Tools → Adventure Backpacks → Build AssetBundles and Deploy Mod`, or `.\build-adventure-backpacks.ps1` from the workspace root. |

**Scope note:** C# changes are limited to what was needed for icons and bundle loading. We did not alter backpack progression, API behavior, or config semantics. When merging upstream Vapok releases, prefer keeping their logic and re-applying asset + small fix layers from this fork.

---

## Original mod documentation (Vapok)

The sections below are **Vapok’s upstream README**, preserved as-is for install instructions, features, compatibility, and credits.

---

# Adventure Backpacks by Vapok

This Valheim mod seeks to introduce the concept of Backpacks throughout the Valheim progression. 
Starting as a wee Viking, rummaging through the tranquil fields of the Meadows, you'll happen upon materials 
that you think will eventually lead to a more meaningful destiny.  From Deer Hide capes and beyond, you'll soon 
learn how to make your very own, Adventure Backpacks!  Go forth and wander, ye wanderer of the wanders! 

---

## How to Use Adventure Backpacks
* Play Valheim as you would As you craft items and explore materials you will learn new recipes for Adventuring Backpacks
* The default hotkey is `I` to open the equipped backpack.
* Each backpack is completely different in form, function, and size.  Upgrading backpacks will unlock additional features depending on the progresion that backpack is intended to be used with.
* Check the Configuration for ALL the different ways that you can modify these packs.
* Keybindings and Actions are Controller Supported

## How To Install Adventure Backpacks
* AdventureBackpacks works best when installed using R2ModMan
  * JVL and YamlDotNet Mods are dependencies
* Install Adventure Backpacks into it's own FOLDER inside of the `BepInEx/plugins` folder.
  * Create a folder called `Translations` and ensure all Translation files are stored in there.
    * Translations files should be named `AdventureBackpacks.<language key>.json`
* Adventure Backpacks is a client-side **AND** server-side mod and should be installed on both.
  * If using on Dedicated Servers:
    * Configuration Lock and Sync is automatically enabled for configured Admins
      * All Syncable settings will be synced to connected clients, and server configs will be enforced.
      * Admins can change server configs using a Configuration Management mod or adjusting the config file directly on the server.
  * Network Compatibility Enforcement is enabled.
    * This is to prevent data loss on a dedicated server if one client is running the mod, but another client isn't. In particular, you could lose your backpacks and everything in them if someone opens a chest and doesn't have the mod.
---

## Gear Introduced In This Mod
* The 6 Original Adventure Backpacks are:
    * **Satchel** - _A small backpack capable of holding things._
    * **Rugged Backpack**  - _A rugged backpack, complete with buckles and fine leather straps._
    * **Bloodbag Wetpack** - _A durable backpack sealed using waterproof blood bags._
    * **Arctic Sherpa Pack** - _An arctic backpack, fit for long treks through the mountains._
    * **Lox Hide Knappsack** - _An adventuring backpack made from extremely durable lox hide._
    * **Explorers Wisppack** - _A finely crafted, mystical backpack. Complete with it's own Box of Holding. No one is quite sure how it works._

## Features of Backpacks
* Adventure Backpacks API Available
  * [Documentation](https://github.com/Vapok/AdventureBackpacks/blob/main/Docs/AdventureBackpacksAPI.md)
  * [Download ABAPI.DLL from GitHub](https://github.com/Vapok/AdventureBackpacks/releases)
    * API Features Include:
      * Registering Your Own Status Effects
      * Registering your own Backpacks (including models)
      * Getting Information about the Player worn backpack.
        * Is Item a Backpack
        * Is Backpack Equipped
        * Get Backpack Information (including Inventory on any backpack item, not just equipped)
        * Get Active Backpack Effects
      * View which effects are registered to Adventure Backpacks
* Each Backpack Biome can be fully configured for progression.
  * Configure Sizing
    * Each Quality Level of Backpack can have a different inventory grid size. Simply adjust the width and height in configuration for each quality level.
  * Configure Recipes
    * Default Recipes can be found in the configuration.
  * Configure Drops
    * Creatures and Drop Rates can be fully customized.
    * Drops are **DISABLED** by default. (as of version 1.6.3)
  * Configure Effects
    * Each Backpack Biome can be configured for any number of effects that are included in this mod.  There is nothing hardcoded about the effects.
  * Configure Carry Weight Maximum
    * Allows configuration for adjusting the additional carry weight allowed, per level of backpack.
  * Configure Speed Modification
    * Configure Speed Modification (slowness).
      * Upon each quality upgrade of backpack, speed modification is reduced (never eliminated).
  * Configure Opening of Backpack with Inventory
    * When enabled, opens backpack inventory with player inventory without additional interaction
    * Can also set Mouse, Keyboard, and Gamepad bindings.
  * Configure Opening of Backpack with Hover + Interaction
    * When enabled, will open backpack when hovered over in Player Inventory and the Open Hot Key is pressed.
    * This feature overrides Close with Inventory.
* Backpack Inventory Protection Guard
  * Every backpack inventory is specially handled by Thor himself and is monitored for any interactions that might otherwise harm the existence of items in your backpacks.
  * Backpacks in Backpacks is not allowed and the only feature that is not configurable. This is how the Allfather dreamt of it.
  * Current verified list of Compatible Inventory Mods:
    * Quick Stack Store
    * Fast Item Transfer (function is included in Backpacks)
    * Multi-User-Chest
* Backpack Monitoring System
  * Features complete support for Portal Technology to ensure no undesired items are hiding inside of backpacks in Player Inventory.
    * This feature will work with any Portal/Teleportation Mod that uses the `Inventory.IsTeleportable()` method.
      * Protip: Do not use `Humanoid.IsTeleportable()` as it won't respect backpack inventory.
      * Current List of verified Portal Compatibility:
        * Valheim Vanilla Portals
        * Advanced Portals
        * AnyPortal
        * XPortal
  * Keys stored in **Equipped Backpack** will active appropriate locked doors without having to move the key to Player inventory.
    * Swamp Key for Crypts
* Optional Right Click Quick Transfer (Fast Item Transfer)
  * Allows single right-click transfer of an item/stack of items between Player Inventory and any Open Container
  * This is the same functionality that's available as the stand-alone mod **Fast Item Transfer** which is disabled when installed with Adventure Backpacks
* Outward Run Away Mode
  * Pressing the Quick Drop keybind (default is `Y`), will immediately release the equipped backpack and drop it behind the player on the ground.
  * This feature is optional, and is disabled out of the box.
       

## Effects Used In This Mod
* This mod utilizes the following effects depending on backpack and quality level:
  * Carry Weight Modifications
  * Speed Modifications
  * Frost Resistance
  * Cold Resistance
  * Troll Armor Set
  * Waterproof
  * Slow Fall
  * Demister (Wisplight effect that clears mist in Mistlands)
    * Config Settings For Demister found in "Wisplight Client Settings"
      * Toggle Wisplight with Keybind 
        * Default: "L" key
      * Wisplight Biome Logic to automatically turn off Wisplight when not in Mistlands.
        * Default: Enabled

## Currently Available Translations
* Czech / čeština
* Chinese / 简体中文
* Chinese Traditional / 繁體中文
* English
* French / Français
* German / Deutsch
* Japanese / 日本
* Korean / 한국인
* Norwegian / norsk
* Polish / Polski
* Portuguese Brazilian / Português Brasileiro
* Russian / Русский
* Spanish / Español
* Swedish / svenska
* Turkish / Türkçe
* Ukrainian / українська
* *Don't see your language, I'm looking for submissions for additional languages. Please find me on Discord (see link below) or submit a Pull Request!*

## Current Patch Notes
[Adventure Backpack Patchnotes](https://github.com/Vapok/AdventureBackpacks/blob/main/CHANGELOG.md) 

## Compatible Mods (Verified)
* Epic Loot 0.9.3+
  * Check out our Discord to get Epic Loot Patches for Dropping backpacks as Epic Loot!
* Extra Slots
* Advanced Portals
* AnyPortal
* XPortal
* Quick Stack Store
* Auto Split Stack
* AzuCraftyBoxes
* Multi-User-Chests
* Fast Item Transfer
* Jewelcrafting
* Shield Me Bruh!
* Cheb's Necromancy
  * Spectral Shroud of Holding Backpack
    * Necromancy Armor Status Effect
    * Necromancy Skill Modifier
* _There's probably a ton of others. This mod is friendly to most mods. If you see a conflict though, let me know!_

## Incompatible Mods
* JotunnBackpacks
  * This will convert bags, but safe to revert back to JotunnBackpacks.

---

### About Vapok Gaming
![Vapok Gaming](https://avatars.githubusercontent.com/u/1264136?s=180&v=4)

Author: [Vapok](https://github.com/Vapok)

Source: [Github](https://github.com/Vapok/AdventureBackpacks)

Discord: [Vapok's Mod's Community](https://discord.gg/5YAJkRFBXt)

Patch notes: [Github Patchnotes](https://github.com/Vapok/AdventureBackpacks/blob/main/CHANGELOG.md)