using HarmonyLib;

namespace AdventureBackpacks.Patches;

// Putting a full backpack on a stand, or taking it off, moves it whole: no yard sale.
public static class ArmorStandPatches
{
    [HarmonyPatch(typeof(ArmorStand), nameof(ArmorStand.UpdateAttach))]
    static class ArmorStandUpdateAttachPatch
    {
        static void Prefix()
        {
            AdventureBackpacks.PushBypassMoveProtection();
        }

        static void Finalizer()
        {
            AdventureBackpacks.PopBypassMoveProtection();
        }
    }

    [HarmonyPatch(typeof(ArmorStand), nameof(ArmorStand.UseItem))]
    static class ArmorStandUseItemPatch
    {
        static void Prefix()
        {
            AdventureBackpacks.PushBypassMoveProtection();
        }

        static void Finalizer()
        {
            AdventureBackpacks.PopBypassMoveProtection();
        }
    }
}
