using AdventureBackpacks.Assets.Items;
using HarmonyLib;

namespace AdventureBackpacks.Patches;

public class FejdStartupPatches
{
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
    [HarmonyAfter("org.bepinex.helpers.LocalizationManager")]
    [HarmonyBefore("org.bepinex.helpers.ItemManager")]
    public static class FejdStartupAwakePatch
    {
        static void Postfix()
        {
            AdventureBackpacks.Waiter.ValheimIsAwake(true);
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
    [HarmonyAfter("org.bepinex.helpers.ItemManager")]
    static class FejdStartupAfterItemManagerIconFix
    {
        static void Postfix() => IronBackpackIconFix.Apply();
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    static class ObjectDBAwakeIronIconFix
    {
        static void Postfix() => IronBackpackIconFix.Apply();
    }
}