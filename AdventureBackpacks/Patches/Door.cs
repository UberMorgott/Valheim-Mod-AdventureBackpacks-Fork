using AdventureBackpacks.Extensions;
using HarmonyLib;

namespace AdventureBackpacks.Patches;

public static class DoorPatches
{
    // Mirrors vanilla Door.HaveKey(Humanoid player, bool matchWorldLevel = true) (Door.cs:195-202): checks the
    // interacting humanoid, forwards matchWorldLevel to Inventory.HaveItem so the "key too low" path
    // (Door.cs:143, matchWorldLevel: false) and world-level key matching behave as in vanilla.
    [HarmonyPatch(typeof(Door), nameof(Door.HaveKey))]
    static class HaveDoorKeyPatch
    {
        static void Postfix(Door __instance, Humanoid player, bool matchWorldLevel, ref bool __result)
        {
            if (__result || __instance == null || __instance.m_keyItem == null)
                return;

            if (player is not Player owner || !owner.IsBackpackEquipped())
                return;

            var inventory = owner.GetEquippedBackpack()?.GetInventory();
            if (inventory != null && inventory.HaveItem(__instance.m_keyItem.m_itemData.m_shared.m_name, matchWorldLevel))
                __result = true;
        }
    }
}
