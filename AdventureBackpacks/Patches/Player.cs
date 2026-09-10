using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using AdventureBackpacks.Compats;
using HarmonyLib;

namespace AdventureBackpacks.Patches;

public class PlayerPatches
{
    [HarmonyPatch(typeof(Player), nameof(Player.Awake))]
    static class PlayerAwakePatch
    {
        static void Postfix(Player __instance)
        {
            __instance.gameObject.AddComponent<Container>();

            // First Player.Awake is the earliest point where EquipmentAndQuickSlots' Slots exist.
            // The call is a no-op after the first successful registration.
            EquipmentAndQuickSlotsCompat.RegisterSlot();
        }
    }

    public static int AdjustCountIfEquipped(Player player, Piece.Requirement resource, int itemCount)
    {
        var num = itemCount;

        if (num < 1 || !resource.m_resItem.m_itemData.IsEquipable())
            return num;

        var inventory = player?.GetInventory();
        if (inventory == null)
            return num;
            
        var itemName = resource.m_resItem.m_itemData.m_shared.m_name;
        var equippedItems = inventory.GetEquippedItems();

        if (equippedItems.Any(x => x.m_shared.m_name.Equals(itemName)))
        {
            num -= 1;
        }

        return num;
    }

    public static int ConsumeUnEquippedItems(Player player, Piece.Requirement resource, int amount)
    {
        var num = amount;

        if (num < 1 || !resource.m_resItem.m_itemData.IsEquipable())
            return num;
            
        var itemName = resource.m_resItem.m_itemData.m_shared.m_name;
        var resourceItems = player.m_inventory.GetAllItems().Where(x => x.m_shared.m_name.Equals(itemName)).ToList();

        var removedCounter = 0;
        for (int i = 0; i < num; i++)
        {
            foreach (var item in resourceItems)
            {
                if (item.m_equipped)
                    continue;

                if (removedCounter < amount)
                {
                    player.m_inventory.RemoveItem(item,1);
                    removedCounter++;
                }
            }
        }

        return num - removedCounter;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirementItems))]
    static class PlayerHaveRequirementItemsPatch
    {
        
        // Anchored on the real call to Inventory.CountItems(string,int,bool).
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var countItemsMethod = AccessTools.DeclaredMethod(typeof(Inventory), nameof(Inventory.CountItems), new[] { typeof(string), typeof(int), typeof(bool) });

            var matcher = new CodeMatcher(instructions)
                .MatchStartForward(new CodeMatch(OpCodes.Callvirt, countItemsMethod), new CodeMatch(OpCodes.Stloc_S));

            if (matcher.IsInvalid)
            {
                AdventureBackpacks.Log.Error($"{nameof(Player.HaveRequirementItems)} transpiler: anchor Inventory.CountItems(string,int,bool) not found. Crafting requirements will not account for an equipped backpack.");
                return instructions;
            }

            matcher.Advance(1);
            var numLocal = matcher.Operand;

            return matcher.Advance(1).Insert(
                    new CodeInstruction(OpCodes.Ldarg_0),                        // Player this
                    new CodeInstruction(OpCodes.Ldloc_2),                        // Piece.Requirement resource
                    new CodeInstruction(OpCodes.Ldloc_S, numLocal),              // int num
                    new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(PlayerPatches), nameof(AdjustCountIfEquipped))),
                    new CodeInstruction(OpCodes.Stloc_S, numLocal))
                .Instructions();
        }
    }
    
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    static class PlayerConsumeResourcesPatch
    {
        
        // Anchored on the real call to Piece.Requirement.GetAmount(int).
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getAmountMethod = AccessTools.DeclaredMethod(typeof(Piece.Requirement), "GetAmount", new[] { typeof(int) });

            var matcher = new CodeMatcher(instructions)
                .MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, getAmountMethod),
                    new CodeMatch(OpCodes.Ldarg_S),
                    new CodeMatch(OpCodes.Mul),
                    new CodeMatch(OpCodes.Stloc_3));

            if (matcher.IsInvalid)
            {
                AdventureBackpacks.Log.Error($"{nameof(Player.ConsumeResources)} transpiler: anchor Piece.Requirement.GetAmount(int) not found. Crafting may consume an equipped backpack.");
                return instructions;
            }

            return matcher.Advance(4).Insert(
                    new CodeInstruction(OpCodes.Ldarg_0),                        // Player this
                    new CodeInstruction(OpCodes.Ldloc_2),                        // Piece.Requirement resource
                    new CodeInstruction(OpCodes.Ldloc_3),                        // int amount
                    new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(PlayerPatches), nameof(ConsumeUnEquippedItems))),
                    new CodeInstruction(OpCodes.Stloc_3))
                .Instructions();
        }
    }
}