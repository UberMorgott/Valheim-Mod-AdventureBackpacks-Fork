using AdventureBackpacks.Features;
using HarmonyLib;

namespace AdventureBackpacks.Patches;

internal static class HudPatches
{
    // The build menu's piece info counts the equipped backpack's materials too.
    [HarmonyPatch(typeof(Hud), nameof(Hud.SetupPieceInfo))]
    static class HudSetupPieceInfoPatch
    {
        [HarmonyPriority(900)]
        static void Prefix() => CraftingContext.Enter();

        [HarmonyPriority(100)]
        static void Finalizer() => CraftingContext.Exit();
    }
}
