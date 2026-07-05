using System;
using AdventureBackpacks.Assets;
using AdventureBackpacks.Assets.Factories;
using AdventureBackpacks.Features;
using Jotunn.Utils;
using UnityEngine;

namespace AdventureBackpacks.Assets.Items;

/// <summary>
/// Iron HD backpack inventory icon (Rugged / Black Forest / legacy CapeIron): embedded 64×64 PNG,
/// patch prefab SharedData, and override GetIcon at runtime.
/// </summary>
internal static class IronBackpackIconFix
{
    internal const string IconTextureName = "IronBackpack_Icon";
    const string EmbeddedPngSuffix = "Icons.IronBackpack_Icon.png";

    /// <summary>In-game "Rugged Backpack" is Black Forest; legacy item is "Old Rugged Backpack".</summary>
    static readonly string[] TargetPrefabNames =
    {
        "BackpackBlackForest",
        "CapeIronBackpack",
    };

    static readonly string[] ItemNameTokens =
    {
        "vapok_mod_item_backpack_blackforest",
        "vapok_mod_item_rugged_backpack",
    };

    static Sprite _cachedIcon;
    static bool _loggedFailure;
    static bool _loggedGetIcon;

    internal static bool IsIronHdBackpack(ItemDrop.ItemData.SharedData shared)
    {
        if (shared?.m_name == null)
            return false;

        foreach (var token in ItemNameTokens)
        {
            if (shared.m_name.Contains(token))
                return true;
        }

        return false;
    }

    internal static bool IsIronHdBackpack(ItemDrop.ItemData item)
    {
        if (item?.m_shared == null)
            return false;

        if (IsIronHdBackpack(item.m_shared))
            return true;

        if (item.TryGetBackpackItem(out var backpack) &&
            IsTargetPrefab(backpack.PrefabName))
            return true;

        if (ObjectDB.instance == null)
            return false;

        foreach (var prefabName in TargetPrefabNames)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            var drop = prefab?.GetComponent<ItemDrop>();
            if (drop != null && ReferenceEquals(item.m_shared, drop.m_itemData.m_shared))
                return true;
        }

        return false;
    }

    static bool IsTargetPrefab(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName))
            return false;

        foreach (var target in TargetPrefabNames)
        {
            if (string.Equals(prefabName, target, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static Sprite GetOrCreateIcon()
    {
        if (_cachedIcon != null)
            return _cachedIcon;

        _cachedIcon = LoadEmbeddedPngIcon();
        if (_cachedIcon != null)
        {
            AdventureBackpacks.Log.Message(
                $"[IronBackpack] Icon loaded from embedded PNG ({_cachedIcon.texture.width}x{_cachedIcon.texture.height}).");
            return _cachedIcon;
        }

        foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (bundle == null)
                continue;

            if (!TryCreateIconFromBundleTexture(bundle, out _cachedIcon))
                continue;

            AdventureBackpacks.Log.Message(
                $"[IronBackpack] Icon built from bundle texture '{bundle.name}' ({_cachedIcon.texture.width}x{_cachedIcon.texture.height}).");
            return _cachedIcon;
        }

        LogOnce("Could not load IronBackpack_Icon (embedded PNG or bundle texture).");
        return null;
    }

    static Sprite LoadEmbeddedPngIcon()
    {
        var assembly = typeof(IronBackpackIconFix).Assembly;
        string resourceName = null;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith(EmbeddedPngSuffix, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("IronBackpack_Icon.png", StringComparison.OrdinalIgnoreCase))
            {
                resourceName = name;
                break;
            }
        }

        if (resourceName == null)
        {
            AdventureBackpacks.Log.Warning("[IronBackpack] Embedded PNG resource not found in mod assembly.");
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        var bytes = new byte[stream.Length];
        _ = stream.Read(bytes, 0, bytes.Length);
        var tex = AssetUtils.LoadImage(bytes);
        if (tex == null)
            return null;

        var sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
        sprite.name = IconTextureName;
        return sprite;
    }

    static bool TryCreateIconFromBundleTexture(AssetBundle bundle, out Sprite icon)
    {
        icon = null;
        if (bundle == null)
            return false;

        Texture2D texture = null;
        foreach (var tex in bundle.LoadAllAssets<Texture2D>())
        {
            if (tex != null && tex.name.IndexOf(IconTextureName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                texture = tex;
                break;
            }
        }

        texture ??= bundle.LoadAsset<Texture2D>(IconTextureName);
        texture ??= bundle.LoadAsset<Texture2D>($"Assets/Texture2D/{IconTextureName}.png");

        if (texture == null)
            return false;

        var readable = DuplicateReadableTexture(texture);
        if (readable == null)
            return false;

        icon = Sprite.Create(
            readable,
            new Rect(0f, 0f, readable.width, readable.height),
            new Vector2(0.5f, 0.5f),
            100f);
        icon.name = IconTextureName;
        return true;
    }

    static Texture2D DuplicateReadableTexture(Texture2D source)
    {
        if (source == null)
            return null;

        try
        {
            return AssetUtils.DuplicateTexture(source);
        }
        catch (Exception ex)
        {
            AdventureBackpacks.Log.Warning($"[IronBackpack] DuplicateTexture failed: {ex.Message}");
            return null;
        }
    }

    internal static void Apply()
    {
        var icon = GetOrCreateIcon();
        if (icon == null)
            return;

        foreach (var prefabName in TargetPrefabNames)
        {
            foreach (var backpack in BackpackFactory.BackpackItems)
            {
                if (!string.Equals(backpack.PrefabName, prefabName, StringComparison.Ordinal))
                    continue;

                ApplyToItemDrop(backpack.Item?.Prefab?.GetComponent<ItemDrop>(), icon);
            }

            if (ZNetScene.instance != null)
            {
                var prefab = ZNetScene.instance.GetPrefab(prefabName);
                ApplyToItemDrop(prefab?.GetComponent<ItemDrop>(), icon);
            }

            if (ObjectDB.instance != null)
            {
                var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                ApplyToItemDrop(prefab?.GetComponent<ItemDrop>(), icon);
            }
        }

        AdventureBackpacks.Log.Message("[IronBackpack] Prefab icon arrays patched (BackpackBlackForest, CapeIronBackpack).");
    }

    internal static void NotifyGetIconOverride(ItemDrop.ItemData item)
    {
        if (_loggedGetIcon)
            return;

        _loggedGetIcon = true;
        var name = item?.m_shared?.m_name ?? "(unknown)";
        AdventureBackpacks.Log.Message($"[IronBackpack] GetIcon override active for '{name}'.");
    }

    static void ApplyToItemDrop(ItemDrop drop, Sprite icon)
    {
        if (drop?.m_itemData?.m_shared == null)
            return;

        var variantCount = Math.Max(1, drop.m_itemData.m_shared.m_variants);
        var icons = new Sprite[variantCount];
        for (var i = 0; i < icons.Length; i++)
            icons[i] = icon;

        drop.m_itemData.m_shared.m_icons = icons;
        drop.Save();
    }

    static void LogOnce(string message)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        AdventureBackpacks.Log.Warning($"[IronBackpack] Inventory icon fix failed: {message}");
    }
}
