using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace AdventureBackpacks.Patches;

public class GuiBarPatches
{
    // It fixes an issue where sometimes the durability bar is set to zero length in some
    // container slots if you have weapons/equipments in them.
    [HarmonyPatch(typeof(GuiBar), "Awake")]
    public static class GuiBarAwakePatch
    {
        //Dependency Strings
        public const string eaqsGUID = "randyknapp.mods.equipmentandquickslots";
        public const string augaGUID = "randyknapp.mods.auga";

        private static bool Prefix(GuiBar __instance)
        {
            // Since EAQS already includes this patch, we only want to include the following code if EAQS isn't installed
            // Auga sizes its own slot durability bars; forcing 54px breaks them
            if (!Chainloader.PluginInfos.ContainsKey(eaqsGUID) && !Chainloader.PluginInfos.ContainsKey(augaGUID) && __instance.name == "durability" && __instance.m_bar.sizeDelta.x != 54)
            {
                // Set the durability bar to normal length, if it isn't already normal length
                __instance.m_bar.sizeDelta = new Vector2(54, 0);
            }
            return true;
        }
    }
}