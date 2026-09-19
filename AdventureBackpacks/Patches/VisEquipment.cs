using System.Collections.Generic;
using AdventureBackpacks.Extensions;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace AdventureBackpacks.Patches;

internal static class VisEquipmentPatches
{
    // VisEquipment.AttachArmor (VisEquipment.cs) binds every "attach_skin" mesh to the body skeleton by
    // index: renderer.bones = m_bodyModel.bones. The backpack meshes list their bones in a different
    // order, so each backpack skin renderer gets the body bones that carry the names its own prefab
    // renderer lists, in that order.
    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachArmor))]
    private static class AttachArmorPatch
    {
        [UsedImplicitly]
        private static void Postfix(VisEquipment __instance, int itemHash, List<GameObject> __result)
        {
            if (__result == null || !__instance.m_bodyModel || !ObjectDB.instance)
                return;

            var itemPrefab = ObjectDB.instance.GetItemPrefab(itemHash);
            var itemDrop = itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (!itemDrop || !itemDrop.m_itemData.IsBackpack())
                return;

            var skeleton = BonesByName(__instance.m_bodyModel);
            var prefabSkins = new List<Transform>();
            foreach (Transform child in itemPrefab.transform)
            {
                if (child.name == "attach_skin")
                    prefabSkins.Add(child);
            }

            // AttachArmor clones the "attach_skin" children in prefab order; pair them in the same order.
            var skinIndex = 0;
            foreach (var instance in __result)
            {
                if (!instance || !instance.name.StartsWith("attach_skin") || skinIndex >= prefabSkins.Count)
                    continue;

                var source = prefabSkins[skinIndex++].GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var target = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (var i = 0; i < source.Length && i < target.Length; i++)
                    Rebind(target[i], source[i], skeleton);
            }
        }
    }

    private static Dictionary<string, Transform> BonesByName(SkinnedMeshRenderer body)
    {
        var bones = new Dictionary<string, Transform>();
        var root = body.rootBone ? body.rootBone : body.transform;
        foreach (var bone in root.GetComponentsInChildren<Transform>(true))
        {
            if (!bones.ContainsKey(bone.name))
                bones.Add(bone.name, bone);
        }
        return bones;
    }

    private static void Rebind(SkinnedMeshRenderer target, SkinnedMeshRenderer source, Dictionary<string, Transform> skeleton)
    {
        var mesh = source.sharedMesh;
        var names = source.bones;

        // The prefab's bone list must describe its mesh (one bone per bindpose) to be remapped.
        if (!mesh || names.Length != mesh.bindposes.Length)
            return;

        var bones = new Transform[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            if (!names[i] || !skeleton.TryGetValue(names[i].name, out bones[i]))
            {
                AdventureBackpacks.Log.Debug($"Bone {(names[i] ? names[i].name : "<none>")} of {source.name} not found in the body skeleton; keeping the vanilla binding.");
                return;
            }
        }

        target.bones = bones;
    }
}
