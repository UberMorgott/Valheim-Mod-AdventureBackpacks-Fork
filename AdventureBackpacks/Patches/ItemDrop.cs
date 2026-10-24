using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets;
using AdventureBackpacks.Assets.Effects;
using AdventureBackpacks.Assets.Factories;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;
using HarmonyLib;
using JetBrains.Annotations;

namespace AdventureBackpacks.Patches;

public class ItemDropPatches
{
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight))]
    static class ItemDataGetWeightPatch
    {
        // Vanilla returns item weight scaled by stack and quality. A backpack additionally carries the
        // weight of its own inventory, so we only need the result - no IL rewriting required.
        static void Postfix(ItemDrop.ItemData __instance, ref float __result)
        {
            if (__instance?.m_shared == null || string.IsNullOrEmpty(__instance.m_shared.m_name))
                return;

            if (!__instance.TryGetBackpackItem(out var backpack))
                return;

            var backpackItem = __instance.GetOrCreateBackpackComponent();

            var size = backpack.GetInventorySize(backpackItem.Item.m_quality);

            if (!backpackItem.IsEmptyingBackpack && backpackItem.InventoryNeedsValidating(size))
            {
                AdventureBackpacks.Log.Debug($"[GetWeight() - Item Name: {__instance.m_shared.m_name}");
                AdventureBackpacks.Log.Debug($"[GetWeight() - Backpack: {backpack.ItemName}");
                Backpacks.ValidateBackpackInventorySizing(Player.m_localPlayer, backpackItem.Item);
            }

            // GetTotalWeight() just returns the cached m_totalWeight, which Inventory keeps up to date.
            var inventoryWeight = backpackItem.GetInventory()?.GetTotalWeight() ?? 0;

            __result += inventoryWeight * backpack.WeightMultiplier.Value;
        }
    }

    // Backpack tooltip: armor line, the damage modifiers its status effect grants, and on the worn pack the
    // open/quickdrop keys.
    [HarmonyPatch]
    internal static class ItemDataGetTooltipPatch
    {
        // The static overload with the most parameters; its signature grows between game versions.
        [UsedImplicitly]
        private static MethodBase TargetMethod()
        {
            return typeof(ItemDrop.ItemData).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == nameof(ItemDrop.ItemData.GetTooltip) && m.ReturnType == typeof(string))
                .Where(m => m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(ItemDrop.ItemData))
                .OrderByDescending(m => m.GetParameters().Length)
                .First();
        }

        [UsedImplicitly]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(ItemDrop.ItemData item, int qualityLevel, float worldLevel, ref string __result)
        {
            if (item == null || string.IsNullOrEmpty(__result) || !item.TryGetBackpackItem(out var backpack))
                return;

            if (!__result.Contains("$item_armor"))
                __result = InsertAfterLine(__result, $"\n$item_armor: <color=orange>{item.GetArmor(qualityLevel, worldLevel)}</color>", "$item_quality", "$item_weight");

            var damageMods = item.m_shared.m_damageModifiers;
            if ((damageMods == null || damageMods.Count == 0) && item.m_shared.m_equipStatusEffect is SE_Stats { m_mods.Count: > 0 } stats)
                damageMods = stats.m_mods;

            // A pack not worn yet has no status effect: show the frost resistance its level would give.
            if ((damageMods == null || damageMods.Count == 0) && FrostResistanceQuality(backpack) is var requiredQuality && requiredQuality > 0 && qualityLevel >= requiredQuality)
                damageMods = new List<HitData.DamageModPair> { FrostResistance.EffectMod };

            if (damageMods is { Count: > 0 })
            {
                var modifiers = SE_Stats.GetDamageModifiersTooltipString(damageMods);
                if (!string.IsNullOrEmpty(modifiers) && !__result.Contains(modifiers))
                    __result = InsertAfterLine(__result, modifiers, "$item_armor");
            }

            if (!item.m_equipped)
                return;

            var actionKey = BackpackPanel.IsOpen ? "$adventurebackpacks_close_backpack" : "$adventurebackpacks_open_backpack";
            __result += $"\n[<color=yellow><b>{ConfigRegistry.HotKeyOpen.Value}</b></color>] {Localization.instance.Localize(actionKey)}";

            if (ConfigRegistry.OutwardMode.Value && ConfigRegistry.HotKeyDrop.Value != UnityEngine.KeyCode.None)
                __result += $"\n[<color=yellow><b>{ConfigRegistry.HotKeyDrop.Value}</b></color>] {Localization.instance.Localize("$adventurebackpacks_quickdrop_backpack")}";
        }

        private static int FrostResistanceQuality(Assets.Items.BackpackItem backpack)
        {
            if (backpack.BackpackBiome == null || !EffectsFactory.EffectList.TryGetValue(BackpackEffect.FrostResistance, out var frost) ||
                frost?.EnabledEffect == null || !frost.EnabledEffect.Value || frost.BiomeQualityLevels == null)
                return 0;

            var required = 0;
            foreach (var entry in frost.BiomeQualityLevels)
            {
                if ((backpack.BackpackBiome.Value & entry.Key) != BackpackBiomes.None && entry.Value != null)
                    required = Math.Max(required, entry.Value.Value);
            }

            return required;
        }

        // Inserts text at the end of the first line holding one of the anchors, else appends it.
        private static string InsertAfterLine(string text, string insert, params string[] anchors)
        {
            foreach (var anchor in anchors)
            {
                var index = text.IndexOf(anchor, StringComparison.Ordinal);
                if (index < 0)
                    continue;

                var endOfLine = text.IndexOf('\n', index);
                return endOfLine >= 0 ? text.Insert(endOfLine, insert) : text + insert;
            }

            return text + insert;
        }
    }
}
