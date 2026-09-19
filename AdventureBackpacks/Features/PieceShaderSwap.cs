using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdventureBackpacks.Features;

// Backpacks added through the API may ship materials with a stand-in shader. Their materials are switched
// to the game's "Custom/Piece" shader when the first world starts, when every game shader is loaded.
internal static class PieceShaderSwap
{
    private const string ShaderName = "Custom/Piece";

    private static readonly List<GameObject> Pending = new();

    internal static void Register(GameObject prefab)
    {
        Pending.Add(prefab);
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    private static class ZoneSystemStartPatch
    {
        [UsedImplicitly]
        private static void Postfix()
        {
            // A dedicated server renders nothing.
            if (Pending.Count == 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            var shader = PrefabManager.Cache.GetPrefab<Shader>(ShaderName);
            if (shader == null)
            {
                AdventureBackpacks.Log.Warning($"Shader {ShaderName} not found; API backpack materials keep their own shader.");
                Pending.Clear();
                return;
            }

            foreach (var prefab in Pending)
            {
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material)
                            material.shader = shader;
                    }
                }
            }

            Pending.Clear();
        }
    }
}
