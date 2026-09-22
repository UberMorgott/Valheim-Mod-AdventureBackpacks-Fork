using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using AdventureBackpacks.Assets.Factories;
using HarmonyLib;
using JetBrains.Annotations;

namespace AdventureBackpacks.Patches;

public class PlayerPatches
{
    [HarmonyPatch(typeof(Player), nameof(Player.Awake))]
    static class PlayerAwakePatch
    {
        static void Postfix(Player __instance)
        {
            __instance.gameObject.AddComponent<Container>();
        }
    }

    // Weather resistances act on the local player only: vanilla reads the global EnvMan.IsWet/IsCold here
    // (Player.cs:2217-2218) and also in Fire/Fireplace/Cinder/WearNTear, so the flag scopes
    // SEManPatches.AddStatusEffectPatch to the env effects this method adds for the local player.
    [HarmonyPatch(typeof(Player), nameof(Player.UpdateEnvStatusEffects))]
    internal static class PlayerUpdateEnvStatusEffectsPatch
    {
        internal static bool IsUpdatingEnvStatusEffects { get; private set; }

        [UsedImplicitly]
        private static void Prefix(Player __instance)
        {
            IsUpdatingEnvStatusEffects = __instance == Player.m_localPlayer;
        }

        [UsedImplicitly]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;

            foreach (var effect in EffectsFactory.EffectList.Values)
                effect.OnUpdateEnvStatusEffects(__instance);
        }

        // Clears the flag even when vanilla or another patch throws.
        [UsedImplicitly]
        private static void Finalizer()
        {
            IsUpdatingEnvStatusEffects = false;
        }
    }

    // itemCount comes first so the transpilers can call this with the CountItems result already on the stack.
    public static int AdjustCountIfEquipped(int itemCount, Player player, Piece.Requirement resource)
    {
        var num = itemCount;

        if (num < 1 || resource == null || resource.m_resItem == null || resource.m_resItem.m_itemData == null || !resource.m_resItem.m_itemData.IsEquipable())
            return num;

        var inventory = player?.GetInventory();
        if (inventory == null)
            return num;

        var itemName = resource.m_resItem.m_itemData.m_shared?.m_name;
        if (string.IsNullOrEmpty(itemName))
            return num;

        var equippedItems = inventory.GetEquippedItems();

        if (equippedItems != null && equippedItems.Any(x => x.m_shared != null && x.m_shared.m_name.Equals(itemName, StringComparison.Ordinal)))
        {
            num -= 1;
        }

        return num;
    }

    public static int AdjustCountIfEquipped(Player player, Piece.Requirement resource, int itemCount)
    {
        return AdjustCountIfEquipped(itemCount, player, resource);
    }

    // amount comes first so the transpiler can call this with the GetAmount * multiplier result already on the stack.
    public static int ConsumeUnEquippedItems(int amount, Player player, Piece.Requirement resource)
    {
        var num = amount;

        if (num < 1 || resource == null || resource.m_resItem == null || resource.m_resItem.m_itemData == null || !resource.m_resItem.m_itemData.IsEquipable())
            return num;

        var itemName = resource.m_resItem.m_itemData.m_shared?.m_name;
        if (string.IsNullOrEmpty(itemName))
            return num;

        var allItems = player?.m_inventory?.GetAllItems();
        if (allItems == null)
            return num;

        var resourceItems = allItems.Where(x => x.m_shared != null && x.m_shared.m_name.Equals(itemName, StringComparison.Ordinal)).ToList();

        var removedCounter = 0;
        for (int i = 0; i < num; i++)
        {
            foreach (var item in resourceItems)
            {
                if (item.m_equipped)
                    continue;

                if (removedCounter < amount)
                {
                    player.m_inventory.RemoveItem(item, 1);
                    removedCounter++;
                }
            }
        }

        return num - removedCounter;
    }

    public static int ConsumeUnEquippedItems(Player player, Piece.Requirement resource, int amount)
    {
        return ConsumeUnEquippedItems(amount, player, resource);
    }

    // Local slots differ per method and shift between game versions, so the transpilers below read
    // them out of the real IL instead of hardcoding indices. In 1.0.7 the loop variable is loc 3,
    // not loc 2, and the amount is stloc.s 4, not stloc.3 — those stale indices are exactly what
    // made ConsumeResources fail to match and HaveRequirementItems fail to compile.
    internal static CodeInstruction LoadLocal(CodeInstruction store)
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

        // Anchored on the real call to Inventory.CountItems(string,int,bool). The adjustment is inserted directly
        // after the call and works on the stack value, so it does not depend on what the caller (or another
        // mod's transpiler) does with the result next.
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var countItemsMethod = AccessTools.DeclaredMethod(typeof(Inventory), nameof(Inventory.CountItems), new[] { typeof(string), typeof(int), typeof(bool) });
            var adjustMethod = AccessTools.DeclaredMethod(typeof(PlayerPatches), nameof(AdjustCountIfEquipped), new[] { typeof(int), typeof(Player), typeof(Piece.Requirement) });
            var code = instructions.ToList();

            var patched = false;
            for (var i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(countItemsMethod))
                    continue;

                var requirement = LoadRequirement(code, i);
                if (requirement == null)
                {
                    AdventureBackpacks.Log.LogError($"{nameof(Player.HaveRequirementItems)} transpiler: Piece.Requirement loop local not found. Crafting requirements will not account for an equipped backpack.");
                    return instructions;
                }

                // [int itemCount] is on the stack: push Player this, Piece.Requirement, call -> adjusted int.
                code.InsertRange(i + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    requirement,
                    new CodeInstruction(OpCodes.Call, adjustMethod)
                });
                i += 3;
                patched = true;
            }

            if (!patched)
                AdventureBackpacks.Log.LogError($"{nameof(Player.HaveRequirementItems)} transpiler: anchor Inventory.CountItems(string,int,bool) not found. Crafting requirements will not account for an equipped backpack.");

            return code;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    static class PlayerConsumeResourcesPatch
    {

        // Anchored on the Mul that follows the real call to Piece.Requirement.GetAmount(int)
        // (`GetAmount(qualityLevel) * multiplier`). The adjustment is inserted directly after the Mul and works on
        // the stack value, so it does not depend on how the result is stored.
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getAmountMethod = AccessTools.DeclaredMethod(typeof(Piece.Requirement), "GetAmount", new[] { typeof(int) });
            var consumeMethod = AccessTools.DeclaredMethod(typeof(PlayerPatches), nameof(ConsumeUnEquippedItems), new[] { typeof(int), typeof(Player), typeof(Piece.Requirement) });
            var code = instructions.ToList();

            var patched = false;
            for (var i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Mul)
                    continue;

                var getAmountIndex = -1;
                for (var j = i - 1; j >= Math.Max(0, i - 5); j--)
                {
                    if (code[j].Calls(getAmountMethod))
                    {
                        getAmountIndex = j;
                        break;
                    }
                }

                if (getAmountIndex < 0)
                    continue;

                var requirement = LoadRequirement(code, getAmountIndex);
                if (requirement == null)
                {
                    AdventureBackpacks.Log.LogError($"{nameof(Player.ConsumeResources)} transpiler: Piece.Requirement loop local not found. Crafting may consume an equipped backpack.");
                    return instructions;
                }

                // [int amount] is on the stack: push Player this, Piece.Requirement, call -> adjusted amount.
                code.InsertRange(i + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    requirement,
                    new CodeInstruction(OpCodes.Call, consumeMethod)
                });
                i += 3;
                patched = true;
            }

            if (!patched)
                AdventureBackpacks.Log.LogError($"{nameof(Player.ConsumeResources)} transpiler: anchor Piece.Requirement.GetAmount(int) not found. Crafting may consume an equipped backpack.");

            return code;
        }
    }
}