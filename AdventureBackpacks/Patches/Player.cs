using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AdventureBackpacks.Assets.Factories;
using AdventureBackpacks.Features;
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

    // Dying moves the inventory into the tombstone, a full backpack included.
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    static class PlayerOnDeathPatch
    {
        static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                AdventureBackpacks.PushBypassMoveProtection();
        }

        static void Finalizer(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                AdventureBackpacks.PopBypassMoveProtection();
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

    // Turns a stloc into the matching ldloc; local slots differ per method and between game versions, so
    // transpilers read them out of the real IL instead of hardcoding indices.
    internal static CodeInstruction LoadLocal(CodeInstruction store)
    {
        if (store.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
        if (store.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
        if (store.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
        if (store.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
        return new CodeInstruction(store.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S : OpCodes.Ldloc, store.operand);
    }

    // Kept for mods that call them by reflection; the crafting patches below use CraftingContext instead.
    [Obsolete("Use CraftingContext and CraftFromBackpack.")]
    public static int AdjustCountIfEquipped(int itemCount, Player player, Piece.Requirement resource)
    {
        var itemName = resource?.m_resItem?.m_itemData?.m_shared?.m_name;
        if (player == null || string.IsNullOrEmpty(itemName))
            return itemCount;

        var num = itemCount;
        if (num > 0 && resource.m_resItem.m_itemData.IsEquipable())
        {
            var equippedItems = player.GetInventory()?.GetEquippedItems();
            if (equippedItems != null && equippedItems.Any(x => x?.m_shared != null && x.m_shared.m_name == itemName))
                num -= 1;
        }

        return num + CraftFromBackpack.GetBackpackItemCount(player, itemName);
    }

    [Obsolete("Use CraftingContext and CraftFromBackpack.")]
    public static int AdjustCountIfEquipped(Player player, Piece.Requirement resource, int itemCount)
    {
        return AdjustCountIfEquipped(itemCount, player, resource);
    }

    [Obsolete("Use CraftingContext and CraftFromBackpack.")]
    public static int ConsumeUnEquippedItems(int amount, Player player, Piece.Requirement resource)
    {
        var itemName = resource?.m_resItem?.m_itemData?.m_shared?.m_name;
        if (amount < 1 || player == null || string.IsNullOrEmpty(itemName))
            return amount;

        return CraftFromBackpack.ConsumeCraftingItem(player, itemName, amount);
    }

    [Obsolete("Use CraftingContext and CraftFromBackpack.")]
    public static int ConsumeUnEquippedItems(Player player, Piece.Requirement resource, int amount)
    {
        return ConsumeUnEquippedItems(amount, player, resource);
    }

    // Requirement checks and payments of the local player run inside a CraftingContext, see InventoryPatches:
    // its counts leave out equipped items and add the equipped backpack. Piece placement pays inside
    // UpdatePlacement, crafting inside InventoryGui.DoCrafting.
    private static void EnterFor(Player player)
    {
        if (player != null && player == Player.m_localPlayer)
            CraftingContext.Enter();
    }

    private static void ExitFor(Player player)
    {
        if (player != null && player == Player.m_localPlayer)
            CraftingContext.Exit();
    }

    // Every overload: their parameter lists change between game versions.
    [HarmonyPatch]
    static class PlayerHaveRequirementsPatch
    {
        [UsedImplicitly]
        static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(Player).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name is nameof(Player.HaveRequirements) or nameof(Player.HaveRequirementItems));
        }

        [HarmonyPriority(900)]
        static void Prefix(Player __instance) => EnterFor(__instance);

        [HarmonyPriority(100)]
        static void Finalizer(Player __instance) => ExitFor(__instance);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
    static class PlayerUpdatePlacementPatch
    {
        [HarmonyPriority(900)]
        static void Prefix(Player __instance) => EnterFor(__instance);

        [HarmonyPriority(100)]
        static void Finalizer(Player __instance) => ExitFor(__instance);
    }

    // "Requires only one ingredient" recipes pick the ingredient here; vanilla only looks at the inventory.
    [HarmonyPatch(typeof(Player), nameof(Player.GetFirstRequiredItem))]
    static class PlayerGetFirstRequiredItemPatch
    {
        static void Postfix(Player __instance, Inventory inventory, Recipe recipe, int qualityLevel, ref int amount, ref int extraAmount, int craftMultiplier, ref ItemDrop.ItemData __result)
        {
            if (__result != null || recipe?.m_resources == null || !CraftFromBackpack.CanCraftFromBackpack(__instance, out var backpackInventory))
                return;

            var station = __instance.GetCurrentCraftingStation();
            foreach (var requirement in recipe.m_resources)
            {
                if ((station != null && station.m_upgrader != requirement.m_upgraderResource) || (station == null && requirement.m_upgraderResource) ||
                    !requirement.m_resItem || requirement.m_resItem.m_itemData?.m_shared == null)
                    continue;

                var needed = requirement.GetAmount(qualityLevel) * craftMultiplier;
                if (needed <= 0)
                    continue;

                var name = requirement.m_resItem.m_itemData.m_shared.m_name;
                for (var quality = 1; quality <= requirement.m_resItem.m_itemData.m_shared.m_maxQuality; quality++)
                {
                    var count = inventory.CountItems(name, quality) + CraftFromBackpack.UsableCount(backpackInventory.CountItems(name, quality));
                    if (count < needed)
                        continue;

                    var items = backpackInventory.GetAllItems();
                    var match = items.FirstOrDefault(x => x?.m_shared != null && x.m_shared.m_name == name && x.m_quality == quality && x.m_stack >= needed)
                                ?? items.FirstOrDefault(x => x?.m_shared != null && x.m_shared.m_name == name && x.m_quality == quality);
                    if (match == null)
                        continue;

                    amount = needed;
                    extraAmount = requirement.m_extraAmountOnlyOneIngredient;
                    __result = match;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDestroy))]
    static class PlayerOnDestroyPatch
    {
        static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                CraftingContext.Reset();
        }
    }
}
