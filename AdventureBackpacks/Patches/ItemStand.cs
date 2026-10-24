using HarmonyLib;

namespace AdventureBackpacks.Patches;

// Putting a full backpack on a stand, or taking it off, moves it whole: no yard sale.
public static class ItemStandStandPatches
{
    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UpdateAttach))]
    static class ItemStandUpdateAttachPatch
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

    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem))]
    static class ItemStandUseItemPatch
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
