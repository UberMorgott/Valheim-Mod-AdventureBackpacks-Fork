using System.Linq;
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

}