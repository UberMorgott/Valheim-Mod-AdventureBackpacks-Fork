using System;
using Jotunn.Utils;
using UnityEngine;

namespace AdventureBackpacks.Features;

public static class Utilities
{
    const string EmbeddedFolder = "Assets.Bundles";

    /// <summary>
    /// Returns an embedded AssetBundle, reusing an already-loaded instance when ItemManager got there first.
    /// </summary>
    public static AssetBundle LoadAssetBundle(string assetBundleFileName, string folderName = EmbeddedFolder)
    {
        var existing = FindLoadedAssetBundle(assetBundleFileName);
        if (existing != null)
            return existing;

        var bundleName = $"{folderName}.{assetBundleFileName}";
        return AssetUtils.LoadAssetBundleFromResources(bundleName, typeof(Utilities).Assembly);
    }

    /// <summary>
    /// Finds a bundle already in memory (avoids "same files already loaded" when loading twice).
    /// </summary>
    public static AssetBundle FindLoadedAssetBundle(string bundleFileName)
    {
        foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (bundle == null)
                continue;

            if (!string.IsNullOrEmpty(bundle.name) &&
                bundle.name.IndexOf(bundleFileName, StringComparison.OrdinalIgnoreCase) >= 0)
                return bundle;
        }

        var probeAsset = GetProbePrefabName(bundleFileName);
        if (probeAsset == null)
            return null;

        foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            // LoadAsset throws on streamed scene bundles (other mods' location/scene bundles).
            if (bundle == null || bundle.isStreamedSceneAssetBundle)
                continue;

            if (ContainsAsset<GameObject>(bundle, probeAsset))
                return bundle;
        }

        return null;
    }

    static string GetProbePrefabName(string bundleFileName) =>
        bundleFileName switch
        {
            "vapokbackpacks" => "CapeIronBackpack",
            "backpack_meadows" => "BackpackMeadows",
            "backpack_black_forest" => "BackpackBlackForest",
            "backpack_swamp" => "BackpackSwamp",
            "backpack_mountains" => "BackpackMountains",
            "backpack_plains" => "BackpackPlains",
            "backpack_mistlands" => "BackpackMistlands",
            _ => null
        };

    static bool ContainsAsset<T>(AssetBundle bundle, string assetName) where T : UnityEngine.Object
    {
        if (bundle.LoadAsset<T>(assetName) != null)
            return true;

        return bundle.LoadAsset<T>($"Assets/vapok/Prefabs/{assetName}.prefab") != null;
    }
}
