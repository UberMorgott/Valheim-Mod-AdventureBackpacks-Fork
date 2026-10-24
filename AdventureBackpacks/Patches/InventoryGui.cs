using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using AdventureBackpacks.Assets;
using AdventureBackpacks.Components;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;
using AdventureBackpacks.Features;
using HarmonyLib;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace AdventureBackpacks.Patches;

internal static class InventoryGuiPatches
{
    public static bool BackpackEquipped = false;
    private static bool _showBackpack;

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    static class InventoryGuiDoCraftingPrefix
    {
        [UsedImplicitly]
        [HarmonyPriority(900)]
        static void Prefix(InventoryGui __instance)
        {
            AdventureBackpacks.Log.Debug($"########################################");
            AdventureBackpacks.Log.Debug($"####       DoCrafting.Prefix       #####");
            AdventureBackpacks.Log.Debug($"########################################");
            InventoryPatches.IsDoingCrafting = true;
            CraftingContext.Enter();
        }

        [UsedImplicitly]
        [HarmonyPriority(100)]
        static void Finalizer()
        {
            CraftingContext.Exit();
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

    // Called right after vanilla InventoryGui.Show(null) from the inventory key (InventoryGui.cs:571). The backpack
    // panel itself opens with the inventory (BackpackPanel's Update postfix, "Open with Inventory"); this only serves
    // an explicit backpack-hotkey request.
    public static void ShowBackpack(Player player, InventoryGui instance)
    {
        if (!_showBackpack)
            return;

        _showBackpack = false;
        BackpackPanel.Open(player, instance);
    }

    // Called right before vanilla Hide() from the close keys (InventoryGui.cs:556-560).
    public static void HideBackpack(InventoryGui instance)
    {
        BackpackPanel.Close(instance);
    }

    private static bool HoveringEquippedBackpack(InventoryGui instance)
    {
        var grids = instance.m_player.GetComponentsInChildren<InventoryGrid>();
        ItemDrop.ItemData hoveredItem = null;

        foreach (var grid in grids)
        {
            var hoveredElement = grid.GetHoveredElement();
            if (hoveredElement == null)
                continue;

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

        return hoveredItem != null && hoveredItem.IsBackpack() && hoveredItem.m_equipped;
    }

    // Backpack hotkey while the inventory is open. Returns true when the whole inventory must close (vanilla Hide).
    public static bool DetectInputToHide(Player player, InventoryGui instance)
    {
        if (CheckForTextInput())
            return false;

        var hotKeyDown = ZInput.GetButtonDown(ConfigRegistry.OpenBackpackButton.Name);
        var hotKeyDrop = ConfigRegistry.OutwardMode.Value && ZInput.GetButtonDown(ConfigRegistry.DropBackpackButton.Name);

        if (hotKeyDrop)
            player.QuickDropBackpack();

        if (!hotKeyDown)
            return hotKeyDrop;

        if (ConfigRegistry.OpenWithHoverInteract.Value)
        {
            // Hover mode: the hotkey over the equipped backpack toggles its panel.
            if (HoveringEquippedBackpack(instance))
                TogglePanel(player, instance);
            return hotKeyDrop;
        }

        // Without a chest, "Close Inventory" makes the hotkey close the whole inventory when the panel is up; with a
        // chest open it only toggles the backpack panel, the chest stays open.
        if (BackpackPanel.IsOpen && ConfigRegistry.CloseInventory.Value && !instance.IsContainerOpen())
            return true;

        TogglePanel(player, instance);
        return hotKeyDrop;
    }

    private static void TogglePanel(Player player, InventoryGui instance)
    {
        if (BackpackPanel.IsOpen)
            BackpackPanel.Close(instance, byUser: true);
        else if (player.CanOpenBackpack())
            BackpackPanel.Open(player, instance);
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
        if (hotKeyDown && !ConfigRegistry.OpenWithHoverInteract.Value && player.CanOpenBackpack() && !CheckForTextInput()
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

    // The requirement list counts the equipped backpack too (CraftingContext) and shows "available/required"
    // unless another mod already formatted the amount.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    static class InventoryGuiSetupRequirementPatch
    {
        [HarmonyPriority(900)]
        static void Prefix(Player player)
        {
            if (player != null && player == Player.m_localPlayer)
                CraftingContext.Enter();
        }

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, bool __result)
        {
            if (!__result || elementRoot == null || req?.m_resItem?.m_itemData?.m_shared == null || player == null || player != Player.m_localPlayer)
                return;

            if (CraftFromBackpack.DisplayTotalIngredientCount is { Value: false } || !CraftingContext.CanCraftFromBackpack)
                return;

            var text = elementRoot.Find("res_amount")?.GetComponent<TMP_Text>();
            if (text == null || string.IsNullOrEmpty(text.text))
                return;

            var current = text.text.Trim();
            if (CraftFromBackpack.IngredientRegex?.IsMatch(current) == true || !int.TryParse(current, out var required))
                return;

            var available = player.GetInventory().CountItems(req.m_resItem.m_itemData.m_shared.m_name);
            text.text = $"{available}/{required}";

            // Vanilla blinks red/white while short; settle on white once the backpack covers it.
            if (text.color != Color.white && available >= required)
                text.color = Color.white;
        }

        [HarmonyPriority(100)]
        static void Finalizer(Player player)
        {
            if (player != null && player == Player.m_localPlayer)
                CraftingContext.Exit();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList))]
    static class InventoryGuiUpdateRecipeListPatch
    {
        [HarmonyPriority(900)]
        static void Prefix() => CraftingContext.Enter();

        [HarmonyPriority(100)]
        static void Finalizer() => CraftingContext.Exit();
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
    static class InventoryGuiUpdateRecipePatch
    {
        [HarmonyPriority(900)]
        static void Prefix(Player player)
        {
            if (player != null && player == Player.m_localPlayer)
                CraftingContext.Enter();
        }

        [HarmonyPriority(100)]
        static void Finalizer(Player player)
        {
            if (player != null && player == Player.m_localPlayer)
                CraftingContext.Exit();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftPressed))]
    static class InventoryGuiOnCraftPressedPatch
    {
        [HarmonyPriority(900)]
        static void Prefix() => CraftingContext.Enter();

        [HarmonyPriority(100)]
        static void Finalizer() => CraftingContext.Exit();
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    static class InventoryGuiHidePatch
    {
        static void Postfix() => CraftingContext.Reset();
    }
}
