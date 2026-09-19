using AdventureBackpacks.Assets.Factories;
using HarmonyLib;
using JetBrains.Annotations;

namespace AdventureBackpacks.Patches;

public class EnvManPatches
{
    // Both of these used to be skipping prefixes. They are postfixes now: vanilla still decides,
    // we only clear the result when the equipped backpack grants the matching resistance.
    static bool BackpackEffectActive(BackpackEffect effect)
    {
        if (Player.m_localPlayer == null)
            return false;

        // Effect registry can be empty if Awake() aborted; degrade quietly instead of throwing every frame.
        if (!EffectsFactory.EffectList.TryGetValue(effect, out var registered))
            return false;

        return registered.IsEffectActive(Player.m_localPlayer);
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.IsCold))]
    public static class EnvManIsCold
    {
        [UsedImplicitly]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(ref bool __result)
        {
            if (__result && BackpackEffectActive(BackpackEffect.ColdResistance))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.IsWet))]
    public static class EnvManIsWet
    {
        [UsedImplicitly]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(ref bool __result)
        {
            if (__result && BackpackEffectActive(BackpackEffect.WaterResistance))
                __result = false;
        }
    }
}
