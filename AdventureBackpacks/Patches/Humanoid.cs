using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using AdventureBackpacks.Assets;
using AdventureBackpacks.Components;
using AdventureBackpacks.Extensions;
using AdventureBackpacks.Features;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace AdventureBackpacks.Patches;

public class HumanoidPatches
{
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipmentStatusEffects))]
    static class HumanoidUpdateEquipmentStatusEffectsPatch
    {
        // The "HashSet<StatusEffect> other" the method builds is stored right after its constructor call
        // (local 0 in vanilla). Anchored on the real HashSet<StatusEffect> constructor followed by any stloc
        // form, and the insert reuses that local, so a shifted local slot does not break the patch.
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var hashSetCtor = AccessTools.DeclaredConstructor(typeof(HashSet<StatusEffect>), new Type[0]);

            var matcher = new CodeMatcher(instructions)
                .MatchStartForward(new CodeMatch(OpCodes.Newobj, hashSetCtor), new CodeMatch(i => i.IsStloc()));

            if (matcher.IsInvalid)
            {
                AdventureBackpacks.Log.LogError($"{nameof(Humanoid.UpdateEquipmentStatusEffects)} transpiler: anchor new HashSet<StatusEffect>() not found. Backpack equipment effects will not be applied.");
                return instructions;
            }

            var otherStore = matcher.Advance(1).Instruction;

            return matcher.Advance(1).Insert(
                    PlayerPatches.LoadLocal(otherStore),                         // HashSet<StatusEffect> other
                    new CodeInstruction(OpCodes.Ldarg_0),                        // Humanoid this, to filter players from creatures
                    new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(EquipmentEffectCache), nameof(EquipmentEffectCache.AddActiveBackpackEffects))),
                    new CodeInstruction(otherStore.opcode, otherStore.operand))
                .Instructions();
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    static class HumanoidUnequipItemPatch
    {
        // The "__instance" here is a Humanoid type, but we want the ItemData argument, so we use "__0" instead.
        // "__0" fetches the argument passed into the first parameter of the original method, which in this case is an ItemData object.
        static void Prefix(ItemDrop.ItemData __0)
        {
            if (__0 is null) return;

            if (Player.m_localPlayer == null)
                return;

            if (SceneManager.GetActiveScene().name.Equals("start", StringComparison.Ordinal))
                return;

            var player = Player.m_localPlayer;

            var item = __0;

            // Check if the item being unequipped is a backpack, and see if it is the same backpack the player is wearing
            if (player.IsThisBackpackEquipped(item))
            {
                var backpackInventory = player.GetEquippedBackpack();
                if (backpackInventory is null) return;

                //Save Backpack
                backpackInventory.Save();

                var inventoryGui = InventoryGui.instance;

                // Close the backpack inventory if it's currently open
                if (inventoryGui != null && inventoryGui.IsContainerOpen())
                {
                    inventoryGui.CloseContainer();
                    InventoryGuiPatches.BackpackIsOpen = false;
                }

                var backpackContainer = player.gameObject.GetComponent<Container>();
                if (backpackContainer == null)
                    return;

                backpackContainer.m_inventory = new Inventory("Empty", null, 1, 1);
                InventoryGuiPatches.BackpackEquipped = false;
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    static class HumanoidEquipItemPatch
    {
        static void Postfix(Humanoid __instance, ItemDrop.ItemData __0, bool __result)
        {
            AdventureBackpacks.Log.Debug($"##########   EquipItem Start");
            if (__0 is null) return;

            if (!__result)
                return;

            if (Player.m_localPlayer == null)
                return;

            if (SceneManager.GetActiveScene().name.Equals("start", StringComparison.Ordinal))
                return;

            var player = Player.m_localPlayer;
            var item = __0;

            if (item.IsBackpack() && item.TryGetBackpackItem(out var backpack))
            {
                InventoryGuiPatches.BackpackEquipped = true;

                var backpackItem = item.GetOrCreateBackpackComponent();

                if (!backpackItem.IsEmptyingBackpack)
                {
                    var size = backpack.GetInventorySize(backpackItem.Item.m_quality);
                    if (backpackItem.InventoryNeedsValidating(size))
                    {
                        Backpacks.ValidateBackpackInventorySizing(player, backpackItem.Item);
                    }
                    else
                    {
                        var backpackContainer = player.gameObject.GetComponent<Container>();
                        if (backpackContainer != null)
                            backpackItem.UpdateContainerSizing(ref backpackContainer);
                    }
                }
            }
            AdventureBackpacks.Log.Debug($"##########   EquipItem End");
        }
    }
}
