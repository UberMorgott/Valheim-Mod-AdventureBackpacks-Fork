using AdventureBackpacks.Assets;
using AdventureBackpacks.Extensions;
using HarmonyLib;

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
}
