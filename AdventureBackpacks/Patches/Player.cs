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

    // Local slots differ per method and shift between game versions, so the transpilers below read
    // them out of the real IL instead of hardcoding indices. In 1.0.7 the loop variable is loc 3,
    // not loc 2, and the amount is stloc.s 4, not stloc.3 — those stale indices are exactly what
    // made ConsumeResources fail to match and HaveRequirementItems fail to compile.
    private static CodeInstruction LoadLocal(CodeInstruction store)
    {
        if (store.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
        if (store.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
        if (store.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
        if (store.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
        return new CodeInstruction(store.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S : OpCodes.Ldloc, store.operand);
    }

    // `foreach (Piece.Requirement r in resources)` stores the current element right after ldelem.ref.
    private static CodeInstruction LoadRequirement(List<CodeInstruction> code, int anchor)
    {
        for (var i = anchor; i > 0; i--)
        {
            if (code[i - 1].opcode == OpCodes.Ldelem_Ref && code[i].IsStloc())
                return LoadLocal(code[i]);
        }

        return null;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirementItems))]
    static class PlayerHaveRequirementItemsPatch
    {

        // Anchored on the real call to Inventory.CountItems(string,int,bool).
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var countItemsMethod = AccessTools.DeclaredMethod(typeof(Inventory), nameof(Inventory.CountItems), new[] { typeof(string), typeof(int), typeof(bool) });
            var code = instructions.ToList();

            var matcher = new CodeMatcher(code)
                .MatchStartForward(new CodeMatch(OpCodes.Callvirt, countItemsMethod), new CodeMatch(i => i.IsStloc()));

            if (matcher.IsInvalid)
            {
                AdventureBackpacks.Log.Error($"{nameof(Player.HaveRequirementItems)} transpiler: anchor Inventory.CountItems(string,int,bool) not found. Crafting requirements will not account for an equipped backpack.");
                return code;
            }

            var requirement = LoadRequirement(code, matcher.Pos);
            if (requirement == null)
            {
                AdventureBackpacks.Log.Error($"{nameof(Player.HaveRequirementItems)} transpiler: Piece.Requirement loop local not found. Crafting requirements will not account for an equipped backpack.");
                return code;
            }

            var countStore = matcher.Advance(1).Instruction;

            return matcher.Advance(1).Insert(
                    new CodeInstruction(OpCodes.Ldarg_0),                        // Player this
                    requirement,                                                 // Piece.Requirement resource
                    LoadLocal(countStore),                                       // int num
                    new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(PlayerPatches), nameof(AdjustCountIfEquipped))),
                    new CodeInstruction(countStore.opcode, countStore.operand))
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
            var code = instructions.ToList();

            var matcher = new CodeMatcher(code)
                .MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, getAmountMethod),
                    new CodeMatch(OpCodes.Ldarg_S),
                    new CodeMatch(OpCodes.Mul),
                    new CodeMatch(i => i.IsStloc()));

            if (matcher.IsInvalid)
            {
                AdventureBackpacks.Log.Error($"{nameof(Player.ConsumeResources)} transpiler: anchor Piece.Requirement.GetAmount(int) not found. Crafting may consume an equipped backpack.");
                return code;
            }

            var requirement = LoadRequirement(code, matcher.Pos);
            if (requirement == null)
            {
                AdventureBackpacks.Log.Error($"{nameof(Player.ConsumeResources)} transpiler: Piece.Requirement loop local not found. Crafting may consume an equipped backpack.");
                return code;
            }

            var amountStore = matcher.Advance(3).Instruction;

            return matcher.Advance(1).Insert(
                    new CodeInstruction(OpCodes.Ldarg_0),                        // Player this
                    requirement,                                                 // Piece.Requirement resource
                    LoadLocal(amountStore),                                      // int amount
                    new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(PlayerPatches), nameof(ConsumeUnEquippedItems))),
                    new CodeInstruction(amountStore.opcode, amountStore.operand))
                .Instructions();
        }
    }
}