using System.Collections.Generic;
using Jotunn.Utils;
using UnityEngine;

namespace AdventureBackpacks.Features;

public static class Utilities
{
    const string EmbeddedFolder = "Assets.Bundles";

    // Bundles this mod loaded. Several backpacks share one bundle (vapokbackpacks); Unity refuses to load
    // the same bundle twice, so each is loaded once and reused.
    private static readonly Dictionary<string, AssetBundle> Loaded = new();

    /// <summary>
    /// Returns an embedded AssetBundle, loading it once through Jotunn's AssetUtils.
    /// </summary>
    public static AssetBundle LoadAssetBundle(string assetBundleFileName, string folderName = EmbeddedFolder)
    {
        var bundleName = $"{folderName}.{assetBundleFileName}";
        if (!Loaded.TryGetValue(bundleName, out var bundle) || !bundle)
            Loaded[bundleName] = bundle = AssetUtils.LoadAssetBundleFromResources(bundleName, typeof(Utilities).Assembly);
        return bundle;
    }
}
