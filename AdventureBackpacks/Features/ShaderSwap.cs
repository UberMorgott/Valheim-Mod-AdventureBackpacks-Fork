using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdventureBackpacks.Features;

// Backpack materials ship with a stand-in shader (the modern models with AssetRipper's albedo-only, back-culled
// "DoubleSided/Standard/Standard DS" stub). They are switched to a game shader when the first world starts, when
// every game shader is loaded: the mod's own packs to "Custom/Creature" like the vanilla capes, API packs to "Custom/Piece".
internal static class ShaderSwap
{
    internal const string Piece = "Custom/Piece";
    internal const string Creature = "Custom/Creature";

    private static readonly List<(GameObject Prefab, string Shader)> Pending = new();

    internal static void Register(GameObject prefab, string shader)
    {
        Pending.Add((prefab, shader));
    }

    // Vanilla CapeDeerHide (SoftRef bundle c4210710, Custom/Creature): double-sided with two-sided normals,
    // wets in rain, low gloss, no metal. _MainTex, _BumpMap and _Color keep the backpack's own values.
    private static void UseCapeValues(Material material)
    {
        material.SetFloat("_Cull", 0f);
        material.SetFloat("_TwoSidedNormals", 1f);
        material.SetFloat("_AddRain", 1f);
        material.SetFloat("_Glossiness", 0.12f);
        material.SetFloat("_MetalGloss", 0f);
        material.SetFloat("_Metallic", 0f);
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

            foreach (var (prefab, shaderName) in Pending)
            {
                var shader = PrefabManager.Cache.GetPrefab<Shader>(shaderName);
                if (shader == null)
                {
                    AdventureBackpacks.Log.Warning($"Shader {shaderName} not found; {prefab.name} keeps its own shader.");
                    continue;
                }

                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    // The quality star particles keep their particle material.
                    if (renderer is ParticleSystemRenderer)
                        continue;
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (!material)
                            continue;
                        material.shader = shader;
                        if (shaderName == Creature)
                            UseCapeValues(material);
                    }
                }
            }

            Pending.Clear();
        }
    }
}
