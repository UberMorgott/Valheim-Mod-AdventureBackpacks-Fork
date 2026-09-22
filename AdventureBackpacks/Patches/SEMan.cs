using System.Linq;
using AdventureBackpacks.Assets.Factories;
using AdventureBackpacks.Features;
using HarmonyLib;
using JetBrains.Annotations;

namespace AdventureBackpacks.Patches;

public static class SEManPatches
{
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), new[] { typeof(int), typeof(bool) })]
    public static class RemoveStatusEffects
    {
        [UsedImplicitly]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(SEMan __instance, int nameHash, ref bool __result)
        {
            // Backpack effects only ever belong to the local player. Bail out before the LINQ scan
            // so this does not run for every status effect removal of every character in the world.
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null || __instance != localPlayer.GetSEMan())
                return true;

            if (EquipmentEffectCache.ActiveEffects == null)
                return true;

            if (EquipmentEffectCache.ActiveEffects.Any(x => x.NameHash().Equals(nameHash)))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    // Blocks the Wet/Cold that vanilla Player.UpdateEnvStatusEffects adds from the weather (Player.cs:2233, :2270)
    // while the local player's backpack grants the matching resistance. Wet from swimming comes from another
    // path and still applies; the world itself (fires, rain wear) keeps reading the real EnvMan state.
    // Signature: SEMan.AddStatusEffect(int nameHash, bool resetTime, int itemLevel, float skillLevel, short variant), SEMan.cs:137.
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new[] { typeof(int), typeof(bool), typeof(int), typeof(float), typeof(short) })]
    public static class AddStatusEffectPatch
    {
        [UsedImplicitly]
        public static bool Prefix(SEMan __instance, int nameHash, ref StatusEffect __result)
        {
            if (!PlayerPatches.PlayerUpdateEnvStatusEffectsPatch.IsUpdatingEnvStatusEffects)
                return true;

            BackpackEffect resistance;
            if (nameHash == SEMan.s_statusEffectWet)
                resistance = BackpackEffect.WaterResistance;
            else if (nameHash == SEMan.s_statusEffectCold)
                resistance = BackpackEffect.ColdResistance;
            else
                return true;

            var player = Player.m_localPlayer;
            if (player == null || __instance != player.GetSEMan())
                return true;

            if (!EffectsFactory.EffectList.TryGetValue(resistance, out var effect) || !effect.IsEffectActive(player))
                return true;

            // Vanilla's own "not added" result (SEMan.cs:174/:199).
            __result = null;
            return false;
        }
    }
}
