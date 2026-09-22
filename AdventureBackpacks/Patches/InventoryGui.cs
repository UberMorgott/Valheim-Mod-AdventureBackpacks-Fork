using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using AdventureBackpacks.Assets;
using AdventureBackpacks.Components;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace AdventureBackpacks.Patches;

internal static class InventoryGuiPatches
{
    public static bool BackpackIsOpen;
    public static bool BackpackEquipped = false;
    private static bool _showBackpack;

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    static class InventoryGuiDoCraftingPrefix
    {
        [UsedImplicitly]
        static void Prefix(InventoryGui __instance)
        {
            AdventureBackpacks.Log.Debug($"########################################");
            AdventureBackpacks.Log.Debug($"####       DoCrafting.Prefix       #####");
            AdventureBackpacks.Log.Debug($"########################################");
            InventoryPatches.IsDoingCrafting = true;

        }
        static void Postfix(InventoryGui __instance)
        {
            AdventureBackpacks.Log.Debug($"########################################");
            AdventureBackpacks.Log.Debug($"####       DoCrafting.Postfix      #####");
            AdventureBackpacks.Log.Debug($"########################################");

            InventoryPatches.IsDoingCrafting = false;
            if (Player.m_localPlayer == null)
                return;
            var player = Player.m_localPlayer;

            if (__instance.m_craftUpgradeItem != null && __instance.m_craftUpgradeItem.IsBackpack())
            {
                AdventureBackpacks.Log.Debug($"Item: {__instance.m_craftUpgradeItem.m_shared.m_name} ");

                var backpack = __instance.m_craftUpgradeItem.GetBackpackComponent();
                AdventureBackpacks.Log.Debug($"Backpack: {__instance.m_craftUpgradeItem.m_shared.m_name} ");
                if (backpack == null)
                    return;

                backpack?.Load();

                if (player.IsThisBackpackEquipped(backpack.Item))
                {
                    var backpackContainer = player.gameObject.GetComponent<Container>();
                    backpack?.UpdateContainerSizing(ref backpackContainer);
                }


                player.UpdateEquipmentStatusEffects();
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
    static class InventoryGuiOnSelectedItem
    {
        [UsedImplicitly]
        static Exception Finalizer(Exception __exception, InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod, InventoryGui __instance)
        {

            if (__exception != null)
            {
                if (__exception is NullReferenceException)
                {
                    if (__instance != null && __instance.m_currentContainer == null && grid != null && grid.GetInventory() != null && Player.m_localPlayer != null
                        && item != null && item.m_shared != null
                        && Backpacks.BackpackTypes.Contains(item.m_shared.m_name))
                    {
                        Player.m_localPlayer.DropItem(Player.m_localPlayer.GetInventory(), item, 1);
                        BackpackIsOpen = false;
                        __instance.Hide();
                        Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "$adventurebackpacks_you_dropped_bag");

                        return null;
                    }
                }
                // Not our case: log it, then hand the exception back untouched. Swallowing it here
                // would hide other mods' failures and leave them in an inconsistent state.
                AdventureBackpacks.Log.Warning($"Adventure Backpacks saw an exception in InventoryGui.OnSelectedItem that is not its own. Rethrowing it:");
                AdventureBackpacks.Log.LogError($"External Mod Error Message: {__exception.Message}");
                AdventureBackpacks.Log.LogError($"External Mod Error Source: {__exception.Source}");
                AdventureBackpacks.Log.LogError($"External Mod Error Stack Trace: {__exception.StackTrace}");
            }
            return __exception;
        }
    }

    public static bool CheckForTextInput()
    {
        /*var textInputVisible = false;
        var textInputPanel = GameObject.Find("_GameMain/LoadingGUI/PixelFix/IngameGui(Clone)/TextInput/panel");
        
        if (textInputPanel != null)
        {
            if (textInputPanel.activeInHierarchy)
                textInputVisible = true;
        }*/

        return TextInput.IsVisible();
    }

    public static void ShowBackpack(Player player, InventoryGui instance)
    {
        if (ConfigRegistry.OpenWithInventory.Value && !BackpackIsOpen && player.CanOpenBackpack())
        {
            _showBackpack = true;
        }

        if (_showBackpack)
        {
            if (!BackpackIsOpen && instance.m_currentContainer != null)
            {
                instance.m_currentContainer.SetInUse(false);
                instance.m_currentContainer = null;
            }

            _showBackpack = false;
            player.OpenBackpack(instance);
        }
    }

    public static void HideBackpack(InventoryGui instance)
    {
        if (BackpackIsOpen)
        {
            instance.CloseContainer();
            BackpackIsOpen = false;

            if (ConfigRegistry.CloseInventory.Value && !ConfigRegistry.OpenWithHoverInteract.Value)
                instance.Hide();
        }
    }

    public static bool DetectInputToHide(Player player, InventoryGui instance)
    {
        var hotKeyDown = ZInput.GetButtonDown(ConfigRegistry.OpenBackpackButton.Name);
        var hotKeyDownOnClose = ConfigRegistry.CloseInventory.Value && hotKeyDown && !ConfigRegistry.OpenWithHoverInteract.Value;
        var hotKeyDrop = ConfigRegistry.OutwardMode.Value && ZInput.GetButtonDown(ConfigRegistry.DropBackpackButton.Name);

        var openBackpack = hotKeyDown && !BackpackIsOpen && player.CanOpenBackpack() && !ConfigRegistry.OpenWithHoverInteract.Value;

        var grids = new List<InventoryGrid>();
        grids.AddRange(instance.m_player.GetComponentsInChildren<InventoryGrid>());

        if (hotKeyDown && !BackpackIsOpen && ConfigRegistry.OpenWithHoverInteract.Value && !CheckForTextInput())
        {
            ItemDrop.ItemData hoveredItem = null;

            foreach (var grid in grids)
            {
                if (grid.GetHoveredElement() == null)
                    continue;

                var hoveredElement = grid.GetHoveredElement();
                hoveredItem = grid.GetInventory().GetItemAt(hoveredElement.Position.x, hoveredElement.Position.y);
            }

            if (ZInput.IsGamepadActive() && hoveredItem == null)
            {
                foreach (var grid in grids)
                {
                    if (grid.GetGamepadSelectedItem() == null)
                        continue;
                    hoveredItem = grid.GetGamepadSelectedItem();
                }
            }

            if (hoveredItem != null && hoveredItem.IsBackpack() && hoveredItem.m_equipped && !BackpackIsOpen &&
                player.CanOpenBackpack())
            {
                openBackpack = true;
            }
        }

        // Hotkey while the extra backpack panel is up closes only that panel, the chest stays open.
        if (hotKeyDown && BackpackPanel.IsOpen && !CheckForTextInput())
        {
            BackpackPanel.Close(instance);
            return false;
        }

        if (openBackpack & !CheckForTextInput())
        {
            // A real container is open (not the player's own backpack container): show the backpack as a
            // third panel next to it instead of replacing it.
            // The mod's own backpack container is the Container component on the player object
            // (PlayerExtensions.OpenBackpack); anything else is a real container in the world.
            if (instance.m_currentContainer != null && instance.m_currentContainer != player.gameObject.GetComponent<Container>())
            {
                BackpackPanel.Open(player, instance);
                return false;
            }

            if (instance.m_currentContainer != null)
            {
                instance.m_currentContainer.SetInUse(false);
                instance.m_currentContainer = null;
            }
            player.OpenBackpack(instance);
            return false;
        }

        if (hotKeyDown && BackpackIsOpen && (!hotKeyDownOnClose || ConfigRegistry.OpenWithHoverInteract.Value) && !CheckForTextInput())
        {
            bool closeBackpack = false;

            if (ConfigRegistry.OpenWithHoverInteract.Value)
            {
                ItemDrop.ItemData hoveredItem = null;

                foreach (var grid in grids)
                {
                    if (grid.GetHoveredElement() == null)
                        continue;

                    var hoveredElement = grid.GetHoveredElement();
                    hoveredItem = grid.GetInventory().GetItemAt(hoveredElement.Position.x, hoveredElement.Position.y);
                }

                if (ZInput.IsGamepadActive() && hoveredItem == null)
                {
                    foreach (var grid in grids)
                    {
                        if (grid.GetGamepadSelectedItem() == null)
                            continue;
                        hoveredItem = grid.GetGamepadSelectedItem();
                    }
                }

                if (hoveredItem != null && hoveredItem.IsBackpack() && hoveredItem.m_equipped && BackpackIsOpen)
                {
                    closeBackpack = true;
                }
            }
            else
            {
                closeBackpack = true;
            }

            if (closeBackpack)
            {
                instance.CloseContainer();
                BackpackIsOpen = false;
                return false;
            }
        }

        if (hotKeyDrop && !CheckForTextInput())
        {
            player.QuickDropBackpack();
        }

        return ((hotKeyDownOnClose) || hotKeyDrop) && !CheckForTextInput();
    }

    public static bool DetectInputToShow(Player player, InventoryGui instance)
    {
        var hotKeyDown = ZInput.GetButtonDown(ConfigRegistry.OpenBackpackButton.Name);
        var hotKeyDrop = ConfigRegistry.OutwardMode.Value && ZInput.GetButtonDown(ConfigRegistry.DropBackpackButton.Name);

        if (hotKeyDrop && !CheckForTextInput())
        {
            player.QuickDropBackpack();
        }

        // Same radial/build-menu gate vanilla puts on the "Inventory" button (InventoryGui.cs:566), so the request
        // is not latched in _showBackpack while vanilla refuses to show the inventory.
        if (hotKeyDown && !ConfigRegistry.OpenWithHoverInteract.Value && !BackpackIsOpen && player.CanOpenBackpack() && !CheckForTextInput()
            && !Hud.InRadial() && !Hud.InBuildUi())
        {
            _showBackpack = true;
        }

        return _showBackpack && !CheckForTextInput();
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
    static class InventoryGuiUpdateTranspiler
    {
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
        {

            var instrs = instructions.ToList();
            var counter = 0;

            var patchedHideBackpackMethod = false;
            var patchedShowBackpackMethod = false;
            var patchedDetectInputHideMethod = false;
            var patchedDetectInputShowMethod = false;

            CodeInstruction LogMessage(CodeInstruction instruction)
            {
                AdventureBackpacks.Log.Debug($"IL_{counter}: Opcode: {instruction.opcode} Operand: {instruction.operand}");
                return instruction;
            }

            CodeInstruction FindInstructionWithLabel(List<CodeInstruction> codeInstructions, int index, Label label)
            {
                if (index >= codeInstructions.Count)
                    return null;

                if (codeInstructions[index].labels.Contains(label))
                    return codeInstructions[index];

                return FindInstructionWithLabel(codeInstructions, index + 1, label);
            }

            CodeInstruction CreateLdlocFromStloc(CodeInstruction stloc)
            {
                if (stloc.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
                if (stloc.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
                if (stloc.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
                if (stloc.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
                if (stloc.opcode == OpCodes.Stloc_S) return new CodeInstruction(OpCodes.Ldloc_S, stloc.operand);
                return new CodeInstruction(OpCodes.Ldloc, stloc.operand);
            }

            CodeInstruction CreateStlocFromStloc(CodeInstruction stloc)
            {
                return new CodeInstruction(stloc.opcode, stloc.operand);
            }

            var resetButtonStatus = AccessTools.DeclaredMethod(typeof(ZInput), nameof(ZInput.ResetButtonStatus));
            var menuVisibleMethod = AccessTools.DeclaredMethod(typeof(Menu), nameof(Menu.IsVisible));
            var hideMethod = AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.Hide));
            var showMethod = AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.Show));
            var tutorialMethod = AccessTools.DeclaredMethod(typeof(Player), nameof(Player.ShowTutorial));
            var zInputKeyDown = AccessTools.DeclaredMethod(typeof(ZInput), nameof(ZInput.GetKeyDown), new[] { typeof(KeyCode), typeof(bool) });
            var zInputButtonDown = AccessTools.DeclaredMethod(typeof(ZInput), nameof(ZInput.GetButtonDown), new[] { typeof(string) });
            var hiddenFramesField = AccessTools.DeclaredField(typeof(InventoryGui), nameof(InventoryGui.m_hiddenFrames));

            for (int i = 0; i < instrs.Count; ++i)
            {
                if (i > 6 && instrs[i].opcode == OpCodes.Call && instrs[i].operand.Equals(resetButtonStatus) &&
                    instrs[i + 1].opcode == OpCodes.Ldarg_0 && instrs[i + 2].opcode == OpCodes.Call &&
                    instrs[i + 2].operand.Equals(hideMethod))
                {
                    //Call to Hide Backpack
                    var ldArgInstruction = new CodeInstruction(OpCodes.Ldarg_0);
                    //Move Any Labels from the instruction position being patched to new instruction.
                    if (instrs[i].labels.Count > 0)
                        instrs[i].MoveLabelsTo(ldArgInstruction);

                    //Output current Operation
                    yield return LogMessage(instrs[i]);
                    counter++;

                    //Patch ldarg_0 this is instance of InventoryGui.
                    yield return LogMessage(ldArgInstruction);
                    counter++;

                    //Patch Call Method for Hiding.
                    yield return LogMessage(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(InventoryGuiPatches), nameof(HideBackpack))));
                    counter++;

                    patchedHideBackpackMethod = true;

                }
                else if (i > 6 && (instrs[i].opcode == OpCodes.Call && instrs[i].operand.Equals(showMethod) &&
                           instrs[i - 1].opcode == OpCodes.Ldc_I4_1 && instrs[i - 2].opcode == OpCodes.Ldnull &&
                           instrs[i - 3].opcode == OpCodes.Ldarg_0 || instrs[i - 4].opcode == OpCodes.Callvirt && instrs[i - 4].operand.Equals(tutorialMethod)))
                {
                    // instrs[i - 4].opcode == OpCodes.Callvirt && instrs[i - 4].operand.Equals(tutorialMethod)
                    // ZenUI mod is removing the vanilla showMethod.  Making an update th at if i can't find showMethod, try to find another method further up, but at a distance.
                    // Not ideal, but does work.

                    //Call to Show Backpack
                    //Get localPlayer at ldloc.1
                    var localPlayerInstruction = new CodeInstruction(OpCodes.Ldloc_1);
                    //Move Any Labels from the instruction position being patched to new instruction.
                    if (instrs[i].labels.Count > 0)
                        instrs[i].MoveLabelsTo(localPlayerInstruction);

                    //Output current Operation
                    yield return LogMessage(instrs[i]);
                    counter++;

                    //Patch ldloc_1 this is localPlayer.
                    yield return LogMessage(localPlayerInstruction);
                    counter++;

                    //InventoryGui Argument.
                    yield return LogMessage(new CodeInstruction(OpCodes.Ldarg_0));
                    counter++;

                    //Patch Call Method for Hiding.
                    yield return LogMessage(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(InventoryGuiPatches), nameof(ShowBackpack))));
                    counter++;

                    patchedShowBackpackMethod = true;
                }
                else if (i > 6 && instrs[i].opcode == OpCodes.Call
                                 && instrs[i].operand.Equals(zInputKeyDown)
                                 && instrs[i - 1].opcode == OpCodes.Ldc_I4_1
                                 && instrs[i - 2].opcode == OpCodes.Ldc_I4_S
                                 && instrs[i - 2].operand.Equals((sbyte)KeyCode.Escape)
                                 && instrs[i + 2].opcode == OpCodes.Ldstr
                                 && instrs[i + 2].operand.Equals("Use"))
                {

                    //1. Output current spot.
                    yield return LogMessage(instrs[i]);
                    counter++;

                    //2. Output i + 1 (this is the brtrue).
                    yield return LogMessage(instrs[i + 1]);
                    counter++;

                    //3. Grab label from brtrue.
                    Label originalLabel = (Label)instrs[i + 1].operand;

                    //4. Look ahead and find instruction with label.
                    var instWithLabel = FindInstructionWithLabel(instrs, i + 2, originalLabel);

                    if (instWithLabel == null)
                    {
                        AdventureBackpacks.Log.LogError($"Can't Find Instruction with Label {originalLabel.GetHashCode()}");
                        continue;
                    }

                    i++;

                    //5. Generate new label.
                    var detectHideLabel = ilGenerator.DefineLabel();

                    //6. Save Label to instruction ahead.
                    instWithLabel.labels.Add(detectHideLabel);

                    //7. Write Player Var
                    yield return LogMessage(new CodeInstruction(OpCodes.Ldloc_1));
                    counter++;

                    //8. Write LdArg Var
                    yield return LogMessage(new CodeInstruction(OpCodes.Ldarg_0));
                    counter++;

                    //9. Write Call instruction
                    yield return LogMessage(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(InventoryGuiPatches), nameof(DetectInputToHide))));
                    counter++;

                    //10. Write Brture instruction with new label
                    yield return LogMessage(new CodeInstruction(OpCodes.Brtrue, detectHideLabel));
                    counter++;

                    patchedDetectInputHideMethod = true;

                }
                else if (i > 6 && (instrs[i].opcode == OpCodes.Stloc_3 || instrs[i].opcode == OpCodes.Stloc_S || instrs[i].opcode == OpCodes.Stloc || instrs[i].opcode == OpCodes.Stloc_0 || instrs[i].opcode == OpCodes.Stloc_1 || instrs[i].opcode == OpCodes.Stloc_2)
                           && instrs[i + 1].opcode == OpCodes.Ldarg_0
                           && instrs[i + 2].opcode == OpCodes.Ldfld && instrs[i + 2].operand.Equals(hiddenFramesField)
                           && instrs.GetRange(Math.Max(0, i - 10), Math.Min(10, i)).Any(inst => inst.opcode == OpCodes.Ldstr && "JoyButtonY".Equals(inst.operand)))
                {
                    // 1. Output current stloc instruction (stores the vanilla / ModLib flag result)
                    yield return LogMessage(instrs[i]);
                    counter++;

                    // 2. Define skip label
                    var skipLabel = ilGenerator.DefineLabel();

                    // 3. Load flag (same local variable as instrs[i])
                    yield return LogMessage(CreateLdlocFromStloc(instrs[i]));
                    counter++;

                    // 4. Branch to skip if flag is already true
                    yield return LogMessage(new CodeInstruction(OpCodes.Brtrue, skipLabel));
                    counter++;

                    // 5. Load Player (ldloc.1)
                    yield return LogMessage(new CodeInstruction(OpCodes.Ldloc_1));
                    counter++;

                    // 6. Load InventoryGui (ldarg.0)
                    yield return LogMessage(new CodeInstruction(OpCodes.Ldarg_0));
                    counter++;

                    // 7. Call DetectInputToShow
                    yield return LogMessage(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(InventoryGuiPatches), nameof(DetectInputToShow))));
                    counter++;

                    // 8. Store result back into the flag local variable
                    yield return LogMessage(CreateStlocFromStloc(instrs[i]));
                    counter++;

                    // 9. Attach the skip label to the next instruction (ldarg.0)
                    instrs[i + 1].labels.Add(skipLabel);

                    patchedDetectInputShowMethod = true;
                }
                else
                {
                    yield return LogMessage(instrs[i]);
                    counter++;
                }
            }

            if (!patchedHideBackpackMethod || !patchedShowBackpackMethod || !patchedDetectInputHideMethod ||
                !patchedDetectInputShowMethod)
            {
                AdventureBackpacks.Log.LogError($"InventoryGui.Update Transpiler Failed To Patch");
                AdventureBackpacks.Log.Warning($" patchedHideBackpackMethod {patchedHideBackpackMethod}");
                AdventureBackpacks.Log.Warning($" patchedShowBackpackMethod {patchedShowBackpackMethod}");
                AdventureBackpacks.Log.Warning($" patchedDetectInputHideMethod {patchedDetectInputHideMethod}");
                AdventureBackpacks.Log.Warning($" patchedDetectInputShowMethod {patchedDetectInputShowMethod}");
                AdventureBackpacks.Log.LogError($"Please inform Mod Author.");
                Thread.Sleep(5000);
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    static class InventoryGuiSetupRequirementPatch
    {
        // Anchored on the real call to Inventory.CountItems(string,int,bool) instead of raw opcode
        // index arithmetic, so a shifted method body fails loudly rather than silently no-opping.
        // The adjustment is inserted directly after the call and works on the stack value, so it does not
        // depend on how the result is stored.
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var countItemsMethod = AccessTools.DeclaredMethod(typeof(Inventory), nameof(Inventory.CountItems), new[] { typeof(string), typeof(int), typeof(bool) });
            var adjustMethod = AccessTools.DeclaredMethod(typeof(PlayerPatches), nameof(PlayerPatches.AdjustCountIfEquipped), new[] { typeof(int), typeof(Player), typeof(Piece.Requirement) });

            var code = instructions.ToList();

            var patched = false;
            for (var i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(countItemsMethod))
                    continue;

                // SetupRequirement(Transform, Piece.Requirement req, Player player, ...): [int itemCount] is on the stack.
                code.InsertRange(i + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_2),                        // Player player
                    new CodeInstruction(OpCodes.Ldarg_1),                        // Piece.Requirement req
                    new CodeInstruction(OpCodes.Call, adjustMethod)              // -> adjusted int
                });
                i += 3;
                patched = true;
            }

            if (!patched)
                AdventureBackpacks.Log.LogError("InventoryGui.SetupRequirement transpiler: anchor Inventory.CountItems(string,int,bool) not found. Crafting requirements will not account for an equipped backpack.");

            return code;
        }
    }
}
