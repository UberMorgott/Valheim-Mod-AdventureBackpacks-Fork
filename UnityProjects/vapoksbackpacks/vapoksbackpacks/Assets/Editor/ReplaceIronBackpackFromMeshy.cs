using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Replaces legacy IronBackpack mesh assets (keeping names/GUIDs) with a Meshy FBX model,
/// scales for Valheim prefab convention (×100 on child transform), applies root-bone skinning,
/// copies PBR textures, and configures IronBackpack.mat with Standard (same as Material.001).
/// </summary>
public static class ReplaceIronBackpackFromMeshy
{
    const string DefaultMeshyFbx =
        "Assets/MeshyImports/Meshy_Model_20260704_090837/Meshy_AI_This_is_a_valheim_gam_0704130829_texture.fbx";

    const string EquippedMeshPath = "Assets/Mesh/IronBackpack.asset";
    const string PickupMeshPath = "Assets/Mesh/IronBackpack_Pickup.asset";
    const string MaterialPath = "Assets/Material/IronBackpack.mat";
    const string IconTexturePath = "Assets/Texture2D/IronBackpack_Icon.png";
    const string IconSpritePath = "Assets/Sprite/IronBackpack_Icon.asset";
    const string IconAssetBundle = "vapokbackpacks";
    const string CapeIronBackpackPrefabPath = "Assets/vapok/Prefabs/CapeIronBackpack.prefab";

    /// <summary>Valheim inventory icons match legacy mod convention: 64×64 PNG (Silver/Meadows use 64).</summary>
    const int IconPixelSize = 64;
    const int IconRenderScale = 2;

    // ---- Inventory icon render tuning (Tools → Refresh Inventory Icon) ----
    /// <summary>Stand pack upright; pitch +180 vs prior view so the outside panel faces the camera right-side up.</summary>
    static readonly Vector3 IconMeshEuler = new Vector3(94f, 155f, -12f);
    /// <summary>Orthographic half-extent multiplier; lower = larger icon in the 64×64 frame.</summary>
    const float IconFrameFill = 0.66f;
    const float IconAmbientIntensity = 1.0f;
    const float IconKeyLightIntensity = 1.55f;
    const float IconFillLightIntensity = 1.05f;
    static readonly Vector3 IconCameraViewDir = new Vector3(0.2f, 0.34f, -1f).normalized;
    const float IconBrightnessLift = 1.28f;
    const float IconGamma = 0.82f;
    const bool IconRotateOutput180 = false;

    /// <summary>Embedded PNG copied into AdventureBackpacks DLL (runtime GetIcon source).</summary>
    const string EmbeddedModIconFileName = "IronBackpack_Icon.png";




    /// <summary>Meshy looked correct in scene at this transform scale.</summary>
    const float MeshySceneScale = 25f;

    /// <summary>IronBackpack child transform scale inside attach_skin prefab.</summary>
    const float PrefabChildScale = 100f;

    /// <summary>Extra vertex scale when baking mesh (prefab uses 130 for +30% size).</summary>
    const float MeshScaleMultiplier = 1f;

    /// <summary>
    /// Extra rotation baked into the equipped skinned mesh vertices, around the mesh's bounds center.
    /// The bind pose bakes a -90° X, so world-vertical maps to the mesh's local Z axis; 180° about Z
    /// flips a "facing backward" pack to face outward correctly.
    /// </summary>
    static readonly Vector3 EquippedSkinnedCorrectionEuler = new Vector3(0f, 0f, 180f);

    // ---- Equipped fit fine-tuning (baked into IronBackpack.asset; iterate via "Re-tune Equipped Mesh Only") ----
    // Mesh local axes after the facing flip: +Z = height (up the back), +X = width (shoulder line), +Y = depth (into/out from spine).
    // Rotations are applied X → Y → Z around the mesh bounds center. Offsets are fractions of the current bounds size per axis.

    /// <summary>Equipped size relative to the original equipped bounds. &lt;1 shrinks the pack on the back.</summary>
    const float EquippedExtraScale = 0.75f;

    /// <summary>Pitch (degrees, mesh +X). Top toward (+) or away from (−) the spine.</summary>
    const float EquippedPitchDeg = 0f;

    /// <summary>Yaw (degrees, mesh +Y). Spins the pack on the back; + = clockwise when viewed from behind (flip sign if wrong).</summary>
    const float EquippedYawDeg = 0f;

    /// <summary>Roll (degrees, mesh +Z). Tips the pack toward one shoulder; + = toward character's right (flip sign if wrong).</summary>
    const float EquippedRollDeg = 0f;

    /// <summary>
    /// Position nudge as a fraction of pack size along mesh local X/Y/Z (width / depth / height).
    /// Example: Z = 0.22 raises the pack ~22% of its height; Y = −0.05 pulls it closer to the spine.
    /// </summary>
    static readonly Vector3 EquippedOffsetFrac = new Vector3(0f, 0.07f, 0.07f);

    // ---- Material (Standard shader). Mask-map channels are multiplied by these scalars. ----
    /// <summary>0 = fabric/leather (no mirror). Raise slightly (e.g. 0.2) if buckles should catch light.</summary>
    const float MaterialMetallic = 0f;
    /// <summary>Surface smoothness / gloss. 0 = matte, 0.5 = Meadows default, 1 = mirror (too shiny in Valheim sun).</summary>
    const float MaterialSmoothness = 0.15f;

    static readonly (string src, string dst)[] TextureCopies =
    {
        ("meshy_basecolor.png", "Assets/Texture2D/IronBackpack_BaseMap.png"),
        ("meshy_normal.png", "Assets/Texture2D/IronBackpack_Normal.png"),
        ("meshy_metallic_smoothness.png", "Assets/Texture2D/IronBackpack_MaskMap.png"),
    };

    static readonly string[] PrefabPaths =
    {
        "Assets/vapok/Prefabs/BackpackBlackForest.prefab",
        "Assets/vapok/Prefabs/CapeIronBackpack.prefab",
    };

    [MenuItem("Tools/Replace Iron Backpack/From Default Meshy Model (20260704_090837)")]
    static void ReplaceFromDefault() => ReplaceFromFbx(DefaultMeshyFbx);

    [MenuItem("Tools/Replace Iron Backpack/From Selected FBX")]
    static void ReplaceFromSelected()
    {
        var path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(
                "Replace Iron Backpack",
                "Select a Meshy .fbx in the Project window, then run this menu item again.",
                "OK");
            return;
        }

        ReplaceFromFbx(path);
    }

    [MenuItem("Tools/Replace Iron Backpack/From Selected FBX", true)]
    static bool ReplaceFromSelectedValidate() =>
        Selection.activeObject != null &&
        AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

    [MenuItem("Tools/Replace Iron Backpack/Rotate Equipped Mesh (correction euler)")]
    static void FlipEquippedMeshUpright()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(EquippedMeshPath);
        if (mesh == null)
        {
            EditorUtility.DisplayDialog("Replace Iron Backpack", $"Missing {EquippedMeshPath}", "OK");
            return;
        }

        ApplyRotationAroundCenter(mesh, Quaternion.Euler(EquippedSkinnedCorrectionEuler), mesh.bounds.center);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();
        Debug.Log($"Rotated {EquippedMeshPath} by {EquippedSkinnedCorrectionEuler} (skinned orientation fix).");
    }

    // ---- Fast tuning loop: adjust the Equipped* constants, run Re-tune, watch the scene Preview. ----

    /// <summary>
    /// Re-bakes ONLY the equipped mesh (IronBackpack.asset) from the default Meshy FBX using the current
    /// Equipped* constants. Skips textures/material/prefab/icon so tuning is fast; no bundle/DLL/game needed
    /// to see the result via "Preview Equipped In Scene".
    /// </summary>
    [MenuItem("Tools/Replace Iron Backpack/Re-tune Equipped Mesh Only (fast)")]
    static void RetuneEquippedMeshOnly()
    {
        RestoreOriginalMeshAssetsFromGit();

        var equippedAsset = AssetDatabase.LoadAssetAtPath<Mesh>(EquippedMeshPath);
        if (equippedAsset == null)
        {
            EditorUtility.DisplayDialog("Replace Iron Backpack", $"Missing {EquippedMeshPath}", "OK");
            return;
        }

        var originalBindposes = equippedAsset.bindposes;
        var equippedBounds = equippedAsset.bounds;
        var originalVertices = equippedAsset.vertices;
        var originalWeights = equippedAsset.boneWeights;
        if (originalBindposes == null || originalBindposes.Length == 0)
        {
            EditorUtility.DisplayDialog("Replace Iron Backpack",
                "Equipped mesh has no bind poses (git restore may have failed). Restore IronBackpack.asset from git, then retry.", "OK");
            return;
        }

        var sourceMesh = LoadFirstMeshFromFbx(DefaultMeshyFbx);
        if (sourceMesh == null)
        {
            EditorUtility.DisplayDialog("Replace Iron Backpack", $"No Mesh found in {DefaultMeshyFbx}", "OK");
            return;
        }

        var equippedMesh = Object.Instantiate(sourceMesh);
        equippedMesh.name = sourceMesh.name + "_equipped";
        FitMeshToBounds(equippedMesh, equippedBounds.center, equippedBounds.extents * EquippedExtraScale);
        if (EquippedSkinnedCorrectionEuler.sqrMagnitude > 0.01f)
            ApplyRotationAroundCenter(equippedMesh, Quaternion.Euler(EquippedSkinnedCorrectionEuler), equippedBounds.center);
        ApplyEquippedFineTune(equippedMesh);

        WriteSkinnedMesh(equippedAsset, equippedMesh, originalBindposes, originalVertices, originalWeights);
        Object.DestroyImmediate(equippedMesh);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Re-tuned equipped mesh → scale {EquippedExtraScale}, " +
                  $"rot pitch/yaw/roll ({EquippedPitchDeg}, {EquippedYawDeg}, {EquippedRollDeg})°, " +
                  $"offset frac {EquippedOffsetFrac}. Use Preview to check.");
    }

    static GameObject _previewInstance;
    static Transform _previewArmature;

    /// <summary>
    /// Instantiates CapeIronBackpack into the open scene and draws its Armature as reference lines so the
    /// equipped mesh's placement on the skeleton (spine/hips/head) is visible without launching the game.
    /// </summary>
    [MenuItem("Tools/Replace Iron Backpack/Preview Equipped In Scene")]
    static void PreviewEquippedInScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CapeIronBackpackPrefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Replace Iron Backpack", $"Missing {CapeIronBackpackPrefabPath}", "OK");
            return;
        }

        if (_previewInstance != null)
            Object.DestroyImmediate(_previewInstance);

        _previewInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        _previewInstance.name = "PREVIEW_CapeIronBackpack (delete when done)";
        _previewInstance.transform.position = Vector3.zero;
        _previewInstance.transform.rotation = Quaternion.identity;

        // Hide everything except the equipped (skinned) branch + its Armature so the dropped "log" mesh,
        // particles, etc. don't clutter the view. The equipped SMR is what we're tuning.
        for (var i = 0; i < _previewInstance.transform.childCount; i++)
        {
            var child = _previewInstance.transform.GetChild(i);
            var keep = child.name.StartsWith("attach_skin") || child.name == "Armature";
            child.gameObject.SetActive(keep);
        }

        _previewArmature = FindDeepChild(_previewInstance.transform, "Armature") ?? _previewInstance.transform;
        BuildBodyProxy(_previewArmature);

        var attach = FindDeepChild(_previewInstance.transform, "attach_skin");
        Selection.activeGameObject = attach != null ? attach.gameObject : _previewInstance;

        SceneView.duringSceneGui -= DrawSkeletonGizmos;
        SceneView.duringSceneGui += DrawSkeletonGizmos;

        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
            SceneView.lastActiveSceneView.Repaint();
        }

        Debug.Log("Preview spawned with translucent body proxy. Re-run 'Re-tune Equipped Mesh Only' to refresh.");
    }

    /// <summary>
    /// Builds a translucent humanoid silhouette from the rig's bone world positions (torso, head, arms, legs)
    /// so the equipped pack can be judged against a real body shape. Parented under the preview instance.
    /// </summary>
    static void BuildBodyProxy(Transform armature)
    {
        var hips = FindDeepChild(armature, "Hips");
        var head = FindDeepChild(armature, "Head");
        if (hips == null || head == null)
        {
            Debug.LogWarning("BuildBodyProxy: could not find Hips/Head bones; skipping body silhouette.");
            return;
        }

        var unit = Mathf.Max(0.001f, Vector3.Distance(hips.position, head.position));
        var mat = MakeTranslucentMaterial(new Color(0.75f, 0.72f, 0.68f, 0.35f));
        var root = new GameObject("BODY_PROXY").transform;
        root.SetParent(_previewInstance.transform, worldPositionStays: true);

        void Seg(string a, string b, float radiusFrac)
        {
            var ta = FindDeepChild(armature, a);
            var tb = FindDeepChild(armature, b);
            if (ta != null && tb != null)
                CreateCapsuleBetween(root, ta.position, tb.position, unit * radiusFrac, mat);
        }

        // torso + neck
        Seg("Hips", "Spine", 0.20f);
        Seg("Spine", "Spine1", 0.20f);
        Seg("Spine1", "Spine2", 0.20f);
        Seg("Spine2", "Neck", 0.17f);
        Seg("Neck", "Head", 0.09f);
        // arms
        Seg("LeftShoulder", "LeftArm", 0.08f);
        Seg("LeftArm", "LeftForeArm", 0.07f);
        Seg("LeftForeArm", "LeftHand", 0.06f);
        Seg("RightShoulder", "RightArm", 0.08f);
        Seg("RightArm", "RightForeArm", 0.07f);
        Seg("RightForeArm", "RightHand", 0.06f);
        // legs
        Seg("Hips", "LeftUpLeg", 0.10f);
        Seg("LeftUpLeg", "LeftLeg", 0.09f);
        Seg("LeftLeg", "LeftFoot", 0.07f);
        Seg("Hips", "RightUpLeg", 0.10f);
        Seg("RightUpLeg", "RightLeg", 0.09f);
        Seg("RightLeg", "RightFoot", 0.07f);
        // head
        CreateSphereAt(root, head.position, unit * 0.17f, mat);
    }

    static void CreateCapsuleBetween(Transform parent, Vector3 a, Vector3 b, float radius, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "proxy";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.transform.SetParent(parent, worldPositionStays: true);

        var dir = b - a;
        var len = Mathf.Max(0.001f, dir.magnitude);
        go.transform.position = (a + b) * 0.5f;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        // Capsule primitive spans 2 units along Y at scale 1; half-length = len/2.
        go.transform.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
    }

    static void CreateSphereAt(Transform parent, Vector3 pos, float radius, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "proxy_head";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.transform.SetParent(parent, worldPositionStays: true);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * radius * 2f;
    }

    static Material MakeTranslucentMaterial(Color color)
    {
        var mat = new Material(Shader.Find("Standard"));
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = color;
        return mat;
    }

    [MenuItem("Tools/Replace Iron Backpack/Clear Preview")]
    static void ClearPreview()
    {
        SceneView.duringSceneGui -= DrawSkeletonGizmos;
        if (_previewInstance != null)
            Object.DestroyImmediate(_previewInstance);
        _previewInstance = null;
        _previewArmature = null;
    }

    static void DrawSkeletonGizmos(SceneView sv)
    {
        if (_previewArmature == null)
        {
            SceneView.duringSceneGui -= DrawSkeletonGizmos;
            return;
        }

        var hips = FindDeepChild(_previewArmature, "Hips");
        var head = FindDeepChild(_previewArmature, "Head");
        Handles.color = Color.yellow;
        if (hips != null) Handles.Label(hips.position, "Hips");
        if (head != null) Handles.Label(head.position, "Head (up)");
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (var i = 0; i < parent.childCount; i++)
        {
            var found = FindDeepChild(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    [MenuItem("Tools/Replace Iron Backpack/Fix IronBackpack Material Only (Standard)")]
    static void FixMaterialOnly()
    {
        CopyMeshyTextures(AssetPathToAbsoluteFolder(DefaultMeshyFbx));
        ConfigureIronBackpackMaterial();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("IronBackpack.mat → built-in Standard with Meshy UV textures.");
    }

    [MenuItem("Tools/Replace Iron Backpack/Refresh Inventory Icon From Meshy")]
    static void RefreshInventoryIcon() => RefreshInventoryIconFromMeshy(DefaultMeshyFbx);

    [MenuItem("Tools/Replace Iron Backpack/Fix Inventory Icon Only (sprite bundle + prefab)")]
    static void FixInventoryIconOnly()
    {
        RefreshInventoryIconFromMeshy(DefaultMeshyFbx);
        FixIronBackpackPrefabsAfterMeshReplace();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Iron backpack inventory icon fixed.\n" +
            $"- {IconTexturePath} imported as Sprite (Meadows-style type-3 prefab ref)\n" +
            $"- Embedded mod copy → {ResolveEmbeddedModIconPath()}\n" +
            "- Backpack prefab icon references refreshed\n\n" +
            "Rebuild vapokbackpacks + backpack_black_forest, then deploy DLL.");
    }

    /// <summary>Unity batchmode entry: ReplaceIronBackpackFromMeshy.BatchFixInventoryIcon</summary>
    public static void BatchFixInventoryIcon()
    {
        FixInventoryIconOnly();
        EditorApplication.Exit(0);
    }

    [MenuItem("Tools/Replace Iron Backpack/Fix Drop-Only Particles (Meadows-style)")]
    static void FixDropOnlyParticlesMenu()
    {
        foreach (var prefabPath in PrefabPaths)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
                continue;

            try
            {
                FixDropOnlyParticles(root);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"Drop-only particles fixed on {prefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Drop-only particles: moved root emitters to log, removed attach_skin CinderParticles.");
    }

    /// <summary>Unity batchmode: ReplaceIronBackpackFromMeshy.BatchFixDropOnlyParticles</summary>
    public static void BatchFixDropOnlyParticles()
    {
        FixDropOnlyParticlesMenu();
        EditorApplication.Exit(0);
    }

    [MenuItem("Tools/Replace Iron Backpack/Fix In-Game Presentation (material, icon, collider)")]
    static void FixInGamePresentation()
    {
        // NOTE: does NOT touch bind poses. Equipped bind poses are set only by the full FBX replace,
        // which reuses the original player-rig bind poses. Recomputing them here breaks equipped rendering.
        ConfigureIronBackpackMaterial();
        DisableIronBackpackClothOnPrefabs();
        RefreshInventoryIconFromMeshy(DefaultMeshyFbx);
        FixIronBackpackPrefabsAfterMeshReplace();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Iron backpack presentation fixes applied.\n" +
            "- IronBackpack.mat → built-in Standard, inventory icon refreshed\n" +
            "- Pickup collider synced to mesh bounds\n\n" +
            "Bind poses are untouched (set by the full FBX replace).\n" +
            "Rebuild vapokbackpacks + backpack_black_forest bundles, then redeploy DLL.");
    }

    static void RefreshInventoryIconFromMeshy(string fbxPath)
    {
        var projectRoot = Path.GetDirectoryName(Application.dataPath)!;
        var dst = Path.Combine(projectRoot, IconTexturePath);

        // Render the actual backpack model to a transparent PNG. A crop of the Meshy UV atlas is
        // unrecognizable, and Valheim shows its round-shield fallback whenever the sprite is bad.
        if (!RenderModelIcon(dst, IconPixelSize))
        {
            Debug.LogWarning("Icon render failed; keeping previous icon texture.");
            return;
        }

        // Import as Sprite (single) — same pattern as Meadows biome icons (type-3 texture sub-sprite ref).
        ConfigureIconTextureImporter();

        EnsureIconAssetsInBundle();
        CopyIconToEmbeddedModResource();
        Debug.Log($"Rendered inventory icon → {IconTexturePath} (Sprite import, bundle {IconAssetBundle}).");
    }

    static string ResolveEmbeddedModIconPath()
    {
        var dir = Application.dataPath;
        while (!string.IsNullOrEmpty(dir))
        {
            var direct = Path.Combine(dir, "AdventureBackpacks", "AdventureBackpacks.csproj");
            if (File.Exists(direct))
            {
                return Path.Combine(dir, "AdventureBackpacks", "Assets", "Icons", EmbeddedModIconFileName);
            }

            var hd = Path.Combine(dir, "AdventureBackpacksHD", "AdventureBackpacks", "AdventureBackpacks.csproj");
            if (File.Exists(hd))
            {
                return Path.Combine(dir, "AdventureBackpacksHD", "AdventureBackpacks", "Assets", "Icons", EmbeddedModIconFileName);
            }

            dir = Path.GetDirectoryName(dir);
        }

        Debug.LogWarning("ResolveEmbeddedModIconPath: could not find AdventureBackpacks.csproj walking up from Assets.");
        return Path.Combine(Application.dataPath, "Icons", EmbeddedModIconFileName);
    }

    static void CopyIconToEmbeddedModResource()
    {
        var src = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, IconTexturePath);
        var dst = Path.GetFullPath(ResolveEmbeddedModIconPath());
        if (!File.Exists(src))
        {
            Debug.LogWarning($"CopyIconToEmbeddedModResource: missing {src}");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, true);
        Debug.Log($"Copied inventory icon for mod DLL embed → {dst}");
    }

    static void ConfigureIconTextureImporter()
    {
        var importer = AssetImporter.GetAtPath(IconTexturePath) as TextureImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(IconTexturePath, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(IconTexturePath) as TextureImporter;
        }

        if (importer == null)
        {
            Debug.LogWarning($"ConfigureIconTextureImporter: no importer for {IconTexturePath}.");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsToUnits = 100f;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.isReadable = false;
        importer.spritePivot = new Vector2(0.5f, 0.5f);
        importer.SaveAndReimport();
    }

    static Texture2D RotateTexture180(Texture2D source)
    {
        var w = source.width;
        var h = source.height;
        var rotated = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = source.GetPixels();
        var dst = new Color[pixels.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
                dst[(h - 1 - y) * w + (w - 1 - x)] = pixels[y * w + x];
        }

        rotated.SetPixels(dst);
        rotated.Apply();
        return rotated;
    }

    static Sprite LoadIconSpriteFromTextureAsset()
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(IconTexturePath))
        {
            if (obj is Sprite sprite)
                return sprite;
        }

        return null;
    }

    /// <summary>
    /// Renders the pickup mesh with its material to a transparent PNG for use as the inventory icon.
    /// Uses PreviewRenderUtility so batchmode renders reliably (plain Camera.Render fails with -nographics).
    /// </summary>
    static bool RenderModelIcon(string destinationPath, int size)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(PickupMeshPath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mesh == null || mat == null)
        {
            Debug.LogWarning("RenderModelIcon: missing pickup mesh or material.");
            return false;
        }

        var renderSize = size * IconRenderScale;
        var preview = new PreviewRenderUtility();
        try
        {
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            preview.camera.orthographic = true;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 1000f;
            preview.ambientColor = new Color(IconAmbientIntensity, IconAmbientIntensity, IconAmbientIntensity);

            if (preview.lights is { Length: > 0 })
            {
                preview.lights[0].intensity = IconKeyLightIntensity;
                preview.lights[0].transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            }

            if (preview.lights is { Length: > 1 })
            {
                preview.lights[1].intensity = IconFillLightIntensity;
                preview.lights[1].transform.rotation = Quaternion.Euler(20f, 160f, 0f);
            }

            var rotation = Quaternion.Euler(IconMeshEuler);
            var bounds = mesh.bounds;
            var matrix = Matrix4x4.TRS(-(rotation * bounds.center), rotation, Vector3.one);

            var radius = Mathf.Max(0.0001f, bounds.extents.magnitude);

            var prevAmbMode = RenderSettings.ambientMode;
            var prevAmb = RenderSettings.ambientLight;
            var prevFog = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = preview.ambientColor;
            RenderSettings.fog = false;

            preview.BeginPreview(new Rect(0f, 0f, renderSize, renderSize), GUIStyle.none);
            preview.camera.orthographicSize = radius * IconFrameFill;
            preview.camera.transform.position = -IconCameraViewDir * (radius * 5f);
            preview.camera.transform.LookAt(Vector3.zero, Vector3.up);
            preview.DrawMesh(mesh, matrix, mat, 0);
            preview.camera.Render();
            var previewImage = preview.EndPreview();

            RenderSettings.ambientMode = prevAmbMode;
            RenderSettings.ambientLight = prevAmb;
            RenderSettings.fog = prevFog;

            if (previewImage == null)
            {
                Debug.LogWarning("RenderModelIcon: PreviewRenderUtility returned null.");
                return false;
            }

            var tex = CopyPreviewTextureToReadable(previewImage, renderSize);
            if (tex == null)
            {
                Debug.LogWarning("RenderModelIcon: could not read preview texture.");
                return false;
            }

            ApplyIconColorGrade(tex);

            Texture2D output = tex;
            if (IconRotateOutput180)
            {
                var rotated = RotateTexture180(tex);
                Object.DestroyImmediate(tex);
                output = rotated;
            }

            if (output.width != size)
            {
                var scaled = ScaleTexture(output, size, size);
                Object.DestroyImmediate(output);
                output = scaled;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);
            return true;
        }
        finally
        {
            preview.Cleanup();
        }
    }

    static Texture2D CopyPreviewTextureToReadable(Texture source, int size)
    {
        var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
            tex.Apply();
            return tex;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    static void ApplyIconColorGrade(Texture2D tex)
    {
        var pixels = tex.GetPixels();
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            if (c.a < 0.005f)
                continue;

            c.r = Mathf.Clamp01(Mathf.Pow(c.r, IconGamma) * IconBrightnessLift);
            c.g = Mathf.Clamp01(Mathf.Pow(c.g, IconGamma) * IconBrightnessLift);
            c.b = Mathf.Clamp01(Mathf.Pow(c.b, IconGamma) * IconBrightnessLift);
            pixels[i] = c;
        }

        tex.SetPixels(pixels);
        tex.Apply();
    }

    /// <summary>
    /// Legacy: standalone sprite asset path. Prefab now uses the texture sub-sprite (type 3) like Meadows.
    /// </summary>
    static void RebuildStandaloneSprite()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconTexturePath);
        if (tex == null)
        {
            Debug.LogWarning($"RebuildStandaloneSprite: no texture at {IconTexturePath}.");
            return;
        }

        var sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "IronBackpack_Icon";

        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, IconSpritePath))!);
        if (AssetDatabase.LoadAssetAtPath<Sprite>(IconSpritePath) != null)
            AssetDatabase.DeleteAsset(IconSpritePath);
        AssetDatabase.CreateAsset(sprite, IconSpritePath);
    }

    /// <summary>
    /// The icon texture must live in vapokbackpacks. Prefab references its sub-sprite (type 3).
    /// </summary>
    static void EnsureIconAssetsInBundle()
    {
        var importer = AssetImporter.GetAtPath(IconTexturePath);
        if (importer == null)
        {
            Debug.LogWarning($"EnsureIconAssetsInBundle: missing asset at {IconTexturePath}.");
            return;
        }

        if (importer.assetBundleName == IconAssetBundle)
            return;

        importer.assetBundleName = IconAssetBundle;
        importer.SaveAndReimport();
        Debug.Log($"Assigned {IconTexturePath} → asset bundle '{IconAssetBundle}'.");
    }

    static void ReplaceFromFbx(string fbxPath)
    {
        if (!EditorUtility.DisplayDialog(
                "Replace Iron Backpack",
                "This overwrites IronBackpack + IronBackpack_Pickup meshes, IronBackpack textures, " +
                "IronBackpack.mat (Standard shader), and disables IronBackpack_Cloth on both prefabs.\n\n" +
                $"Mesh scale: Meshy scene scale {MeshySceneScale} → prefab child scale {PrefabChildScale} " +
                $"(factor {(MeshySceneScale / PrefabChildScale) * MeshScaleMultiplier:F2}).\n\n" +
                $"Source FBX:\n{fbxPath}\n\nContinue?",
                "Replace",
                "Cancel"))
            return;

        var sourceMesh = LoadFirstMeshFromFbx(fbxPath);
        if (sourceMesh == null)
        {
            EditorUtility.DisplayDialog("Replace Iron Backpack", $"No Mesh found in {fbxPath}", "OK");
            return;
        }

        // Always start from the pristine (git HEAD) mesh assets so the captured "original" bounds and bind
        // poses are correct. Without this, re-running compounds the fit scale / correction rotation.
        RestoreOriginalMeshAssetsFromGit();

        var equippedAsset = AssetDatabase.LoadAssetAtPath<Mesh>(EquippedMeshPath);
        var pickupAsset = AssetDatabase.LoadAssetAtPath<Mesh>(PickupMeshPath);
        if (equippedAsset == null || pickupAsset == null)
        {
            EditorUtility.DisplayDialog("Replace Iron Backpack", "Missing equipped or pickup mesh asset.", "OK");
            return;
        }

        // Capture the ORIGINAL equipped bind poses + bounds BEFORE overwriting. These bind poses were
        // authored for the live player skeleton (via the 95x Armature) and are the only ones that render
        // correctly in-game; recomputing them from the prefab bakes the wrong scale and makes it invisible.
        var originalBindposes = equippedAsset.bindposes;
        var equippedBounds = equippedAsset.bounds;
        var pickupBounds = pickupAsset.bounds;
        // Original bone weights (multi-bone: spine/shoulders) are transferred onto the new geometry so
        // the pack follows character animations instead of riding rigidly on the hips.
        var originalVertices = equippedAsset.vertices;
        var originalWeights = equippedAsset.boneWeights;

        if (originalBindposes == null || originalBindposes.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Replace Iron Backpack",
                "The equipped mesh has no bind poses. Restore the original IronBackpack.asset from git first,\n" +
                "then run this again so the original (player-rig) bind poses can be reused.",
                "OK");
            return;
        }

        var working = Object.Instantiate(sourceMesh);
        working.name = sourceMesh.name;

        // Pickup (static, ground): fit Meshy geometry into the ORIGINAL pickup mesh bounds so it renders
        // at the proven size/position under the log transform (scale 100, -90° X).
        var pickupMesh = Object.Instantiate(working);
        pickupMesh.name = working.name + "_pickup";
        FitMeshToBounds(pickupMesh, pickupBounds.center, pickupBounds.extents);

        // Equipped (skinned): fit Meshy geometry into the ORIGINAL equipped mesh bounds so the original
        // bind poses place it correctly on the character's back.
        var equippedMesh = Object.Instantiate(working);
        equippedMesh.name = working.name + "_equipped";
        FitMeshToBounds(equippedMesh, equippedBounds.center, equippedBounds.extents * EquippedExtraScale);
        if (EquippedSkinnedCorrectionEuler.sqrMagnitude > 0.01f)
            ApplyRotationAroundCenter(equippedMesh, Quaternion.Euler(EquippedSkinnedCorrectionEuler), equippedBounds.center);
        ApplyEquippedFineTune(equippedMesh);

        WriteStaticMesh(pickupAsset, pickupMesh);
        WriteSkinnedMesh(equippedAsset, equippedMesh, originalBindposes, originalVertices, originalWeights);

        CopyMeshyTextures(AssetPathToAbsoluteFolder(fbxPath));
        ConfigureIronBackpackMaterial();
        DisableIronBackpackClothOnPrefabs();
        // Import the icon PNG as a Sprite BEFORE wiring it into the prefab, otherwise the prefab
        // icon fix finds no Sprite and Valheim falls back to the round shield placeholder.
        RefreshInventoryIconFromMeshy(fbxPath);
        FixIronBackpackPrefabsAfterMeshReplace();

        Object.DestroyImmediate(working);
        Object.DestroyImmediate(pickupMesh);
        Object.DestroyImmediate(equippedMesh);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "Iron backpack replaced from Meshy.\n" +
            $"- Equipped mesh: {EquippedMeshPath} (original bind poses reused, {originalBindposes.Length} bones)\n" +
            $"- Pickup mesh: {PickupMeshPath}\n" +
            "- Textures → Assets/Texture2D/IronBackpack_*.png\n" +
            "- IronBackpack.mat → built-in Standard\n" +
            "- IronBackpack_Cloth disabled, inventory icon refreshed\n\n" +
            "Build AssetBundles, copy bundles, rebuild DLL.");
    }

    /// <summary>
    /// Uniformly scales and recenters a mesh so its bounds match a target center/extent
    /// (keeps Meshy proportions while matching the original mesh footprint).
    /// </summary>
    static void FitMeshToBounds(Mesh mesh, Vector3 targetCenter, Vector3 targetExtent)
    {
        var b = mesh.bounds;
        var srcMag = b.extents.magnitude;
        var dstMag = targetExtent.magnitude;
        var scale = srcMag > 1e-9f ? dstMag / srcMag : 1f;

        var verts = mesh.vertices;
        var c = b.center;
        for (var i = 0; i < verts.Length; i++)
            verts[i] = (verts[i] - c) * scale + targetCenter;

        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        Debug.Log($"Fit Meshy mesh to bounds (scale {scale:F3}) → size {mesh.bounds.size}, center {mesh.bounds.center}");
    }

    /// <summary>
    /// Applies equipped rotation (pitch/yaw/roll) + mesh-local offset to an already-fitted, already-flipped mesh.
    /// </summary>
    static void ApplyEquippedFineTune(Mesh mesh)
    {
        var c = mesh.bounds.center;

        if (Mathf.Abs(EquippedPitchDeg) > 0.01f)
            ApplyRotationAroundCenter(mesh, Quaternion.AngleAxis(EquippedPitchDeg, Vector3.right), c);
        if (Mathf.Abs(EquippedYawDeg) > 0.01f)
            ApplyRotationAroundCenter(mesh, Quaternion.AngleAxis(EquippedYawDeg, Vector3.up), c);
        if (Mathf.Abs(EquippedRollDeg) > 0.01f)
            ApplyRotationAroundCenter(mesh, Quaternion.AngleAxis(EquippedRollDeg, Vector3.forward), c);

        var ext = mesh.bounds.extents;
        var offset = new Vector3(
            EquippedOffsetFrac.x * ext.x * 2f,
            EquippedOffsetFrac.y * ext.y * 2f,
            EquippedOffsetFrac.z * ext.z * 2f);
        if (offset.sqrMagnitude > 1e-8f)
            TranslateMesh(mesh, offset);
    }

    /// <summary>
    /// Restores IronBackpack.asset + IronBackpack_Pickup.asset (and their .meta) from git HEAD so each
    /// replace/re-tune starts from the pristine original bounds and bind poses.
    /// </summary>
    static void RestoreOriginalMeshAssetsFromGit()
    {
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        var targets = new[]
        {
            EquippedMeshPath, EquippedMeshPath + ".meta",
            PickupMeshPath, PickupMeshPath + ".meta",
        };

        foreach (var rel in targets)
        {
            var abs = Path.GetFullPath(Path.Combine(projectRoot, rel));
            if (!File.Exists(abs))
                continue;
            RunGit(projectRoot, $"checkout HEAD -- \"{abs}\"");
        }

        AssetDatabase.Refresh();
        Debug.Log("Restored IronBackpack mesh assets from git HEAD (pristine bounds/bind poses).");
    }

    static void RunGit(string workingDir, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null)
            {
                Debug.LogWarning($"git not launched for: {args}");
                return;
            }
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                Debug.LogWarning($"git {args} exited {p.ExitCode}: {err}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"git restore failed ({args}): {e.Message}. Restore the mesh assets manually if the fit looks wrong.");
        }
    }

    static void TranslateMesh(Mesh mesh, Vector3 offset)
    {
        var verts = mesh.vertices;
        for (var i = 0; i < verts.Length; i++)
            verts[i] += offset;
        mesh.vertices = verts;
        mesh.RecalculateBounds();
    }

    static void ApplyRotationAroundCenter(Mesh mesh, Quaternion rotation, Vector3 center)
    {
        var matrix = Matrix4x4.Rotate(rotation);
        var verts = mesh.vertices;
        var norms = mesh.normals;
        var tangents = mesh.tangents;
        for (var i = 0; i < verts.Length; i++)
        {
            verts[i] = matrix.MultiplyPoint3x4(verts[i] - center) + center;
            if (norms != null && norms.Length == verts.Length)
                norms[i] = matrix.MultiplyVector(norms[i]).normalized;
            if (tangents != null && tangents.Length == verts.Length)
            {
                var t = tangents[i];
                var rotated = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                tangents[i] = new Vector4(rotated.x, rotated.y, rotated.z, t.w);
            }
        }

        mesh.vertices = verts;
        if (norms != null && norms.Length == verts.Length)
            mesh.normals = norms;
        if (tangents != null && tangents.Length == verts.Length)
            mesh.tangents = tangents;
        mesh.RecalculateBounds();
    }

    static Mesh LoadFirstMeshFromFbx(string fbxPath)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (asset is Mesh mesh && !string.IsNullOrEmpty(mesh.name))
                return mesh;
        }

        return null;
    }

    static void ApplyRotation(Mesh mesh, Quaternion rotation)
    {
        var matrix = Matrix4x4.Rotate(rotation);
        var verts = mesh.vertices;
        var norms = mesh.normals;
        var tangents = mesh.tangents;
        for (var i = 0; i < verts.Length; i++)
        {
            verts[i] = matrix.MultiplyPoint3x4(verts[i]);
            if (norms != null && norms.Length == verts.Length)
                norms[i] = matrix.MultiplyVector(norms[i]).normalized;
            if (tangents != null && tangents.Length == verts.Length)
            {
                var t = tangents[i];
                var rotated = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                tangents[i] = new Vector4(rotated.x, rotated.y, rotated.z, t.w);
            }
        }

        mesh.vertices = verts;
        if (norms != null && norms.Length == verts.Length)
            mesh.normals = norms;
        if (tangents != null && tangents.Length == verts.Length)
            mesh.tangents = tangents;
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Valheim prefab child uses scale 100; Meshy preview used scale 25. Bake 25/100 into mesh vertices.
    /// </summary>
    static void ScaleMeshForValheimPrefab(Mesh mesh, Vector3 attachmentCenter, bool alreadyScaled = false)
    {
        if (alreadyScaled)
        {
            var shifted = mesh.vertices;
            var center = mesh.bounds.center;
            for (var i = 0; i < shifted.Length; i++)
                shifted[i] += attachmentCenter - center;
            mesh.vertices = shifted;
            mesh.RecalculateBounds();
            return;
        }

        var factor = (MeshySceneScale / PrefabChildScale) * MeshScaleMultiplier;
        var srcCenter = mesh.bounds.center;
        var verts = mesh.vertices;
        for (var i = 0; i < verts.Length; i++)
            verts[i] = (verts[i] - srcCenter) * factor + attachmentCenter;

        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        Debug.Log(
            $"Scaled Meshy mesh for Valheim prefab (factor {factor:F3}). " +
            $"Result bounds {mesh.bounds.size}, center {mesh.bounds.center}");
    }

    static void WriteStaticMesh(Mesh targetAsset, Mesh source)
    {
        targetAsset.Clear(false);
        CopyMeshSurface(targetAsset, source);
        targetAsset.UploadMeshData(markNoLongerReadable: false);
        EditorUtility.SetDirty(targetAsset);
    }

    static void WriteSkinnedMesh(
        Mesh targetAsset, Mesh source, Matrix4x4[] bindposes,
        Vector3[] originalVertices = null, BoneWeight[] originalWeights = null)
    {
        targetAsset.Clear(false);
        CopyMeshSurface(targetAsset, source);
        // Transfer the ORIGINAL mesh's multi-bone weights (spine/shoulders) onto the new geometry via
        // nearest-vertex lookup so the pack deforms with animations. Both meshes share the same bounds
        // after FitMeshToBounds. Falls back to rigid Hips-only weights if no original weights exist.
        var transferred = TransferBoneWeights(source.vertices, originalVertices, originalWeights);
        targetAsset.boneWeights = transferred ?? CreateRootBoneWeights(source.vertexCount);
        targetAsset.bindposes = bindposes;
        targetAsset.UploadMeshData(markNoLongerReadable: false);
        EditorUtility.SetDirty(targetAsset);
        Debug.Log(transferred != null
            ? $"Skinned mesh written with transferred bone weights ({CountDistinctBones(transferred)} bones used)."
            : "Skinned mesh written with rigid root-bone weights (no original weights available).");
    }

    /// <summary>
    /// Nearest-vertex bone-weight transfer from the original mesh onto new geometry.
    /// Returns null when the original data is missing or degenerate.
    /// </summary>
    static BoneWeight[] TransferBoneWeights(Vector3[] targetVerts, Vector3[] sourceVerts, BoneWeight[] sourceWeights)
    {
        if (targetVerts == null || sourceVerts == null || sourceWeights == null ||
            sourceVerts.Length == 0 || sourceWeights.Length != sourceVerts.Length)
            return null;

        var result = new BoneWeight[targetVerts.Length];
        for (var i = 0; i < targetVerts.Length; i++)
        {
            var p = targetVerts[i];
            var best = 0;
            var bestSq = float.MaxValue;
            for (var j = 0; j < sourceVerts.Length; j++)
            {
                var dSq = (sourceVerts[j] - p).sqrMagnitude;
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    best = j;
                }
            }

            result[i] = sourceWeights[best];
        }

        return result;
    }

    static int CountDistinctBones(BoneWeight[] weights)
    {
        var bones = new System.Collections.Generic.HashSet<int>();
        foreach (var w in weights)
        {
            if (w.weight0 > 0f) bones.Add(w.boneIndex0);
            if (w.weight1 > 0f) bones.Add(w.boneIndex1);
            if (w.weight2 > 0f) bones.Add(w.boneIndex2);
            if (w.weight3 > 0f) bones.Add(w.boneIndex3);
        }

        return bones.Count;
    }

    /// <summary>
    /// Shift pickup mesh so its lowest point sits on the ground plane used by the log child (-90° X).
    /// </summary>
    static void AlignPickupMeshBottomToGround(Mesh mesh)
    {
        var bounds = mesh.bounds;
        var verts = mesh.vertices;
        var offset = new Vector3(0f, -bounds.min.y, 0f);
        for (var i = 0; i < verts.Length; i++)
            verts[i] += offset;

        mesh.vertices = verts;
        mesh.RecalculateBounds();
    }

    static void RecomputeSkinnedBindPoseFromPrefabs()
    {
        var meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(EquippedMeshPath);
        if (meshAsset == null)
        {
            Debug.LogWarning($"Missing equipped mesh: {EquippedMeshPath}");
            return;
        }

        foreach (var prefabPath in PrefabPaths)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
                continue;

            try
            {
                var attachSkin = FindChildByName(root.transform, "attach_skin");
                var ironBackpack = attachSkin != null ? FindChildByName(attachSkin, "IronBackpack") : null;
                var log = FindChildByName(root.transform, "log");
                if (attachSkin == null || ironBackpack == null)
                {
                    Debug.LogWarning($"Could not find attach_skin/IronBackpack in {prefabPath}");
                    continue;
                }

                if (log != null)
                    log.gameObject.SetActive(false);
                attachSkin.gameObject.SetActive(true);

                var smr = ironBackpack.GetComponent<SkinnedMeshRenderer>();
                if (smr == null || smr.sharedMesh == null || smr.bones == null || smr.bones.Length == 0)
                {
                    Debug.LogWarning($"IronBackpack SkinnedMeshRenderer missing bones in {prefabPath}");
                    continue;
                }

                var meshTransform = smr.transform;
                var bindPoses = new Matrix4x4[smr.bones.Length];
                for (var i = 0; i < smr.bones.Length; i++)
                {
                    if (smr.bones[i] == null)
                        continue;
                    bindPoses[i] = smr.bones[i].worldToLocalMatrix * meshTransform.localToWorldMatrix;
                }

                meshAsset.bindposes = bindPoses;
                meshAsset.boneWeights = CreateRootBoneWeights(meshAsset.vertexCount);
                EditorUtility.SetDirty(meshAsset);

                smr.updateWhenOffscreen = true;
                EditorUtility.SetDirty(smr);

                Debug.Log($"Recomputed skinned bind pose from {prefabPath} ({bindPoses.Length} bones).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    static void FixIronBackpackPrefabsAfterMeshReplace()
    {
        var pickupMesh = AssetDatabase.LoadAssetAtPath<Mesh>(PickupMeshPath);
        foreach (var prefabPath in PrefabPaths)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
                continue;

            try
            {
                var log = FindChildByName(root.transform, "log");
                if (log != null)
                    SyncLogColliderToPickupMesh(log, pickupMesh);

                FixDropOnlyParticles(root);
                FixBackpackItemIcon(root);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"Fixed presentation on {prefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    const string DropParticlesChildName = "DropParticles";

    static void FixDropOnlyParticles(GameObject root)
    {
        var logTransform = FindChildByName(root.transform, "log");
        if (logTransform != null)
            MoveDropParticlesToLog(root, logTransform.gameObject);

        var attachSkin = FindChildByName(root.transform, "attach_skin");
        if (attachSkin != null)
            RemoveEquippedParticles(attachSkin);
    }

    static void RemoveEquippedParticles(Transform attachSkin)
    {
        for (var i = attachSkin.childCount - 1; i >= 0; i--)
        {
            var child = attachSkin.GetChild(i);
            if (child.GetComponent<ParticleSystem>() == null &&
                !string.Equals(child.name, "CinderParticles", System.StringComparison.Ordinal))
                continue;

            var name = child.name;
            Object.DestroyImmediate(child.gameObject);
            Debug.Log($"Removed equipped particle '{name}' from attach_skin on {attachSkin.root.name}.");
        }
    }

    static void MoveDropParticlesToLog(GameObject root, GameObject log)
    {
        var logTransform = log.transform;
        var existingHost = logTransform.Find(DropParticlesChildName);
        if (existingHost != null && existingHost.GetComponent<ParticleSystem>() != null)
        {
            EnsureDropParticleHostScale(existingHost, logTransform);
            return;
        }

        var psOnLog = log.GetComponent<ParticleSystem>();
        var rendererOnLog = log.GetComponent<ParticleSystemRenderer>();
        if (psOnLog != null)
        {
            RelocateParticleSystemToDropHost(psOnLog, rendererOnLog, logTransform);
            return;
        }

        var particleSystem = root.GetComponent<ParticleSystem>();
        var particleRenderer = root.GetComponent<ParticleSystemRenderer>();
        if (particleSystem == null)
            return;

        try
        {
            var host = CreateDropParticleHost(logTransform);
            UnityEditorInternal.ComponentUtility.CopyComponent(particleSystem);
            if (!UnityEditorInternal.ComponentUtility.PasteComponentAsNew(host.gameObject))
            {
                Debug.LogWarning("Could not paste ParticleSystem onto log DropParticles child; leaving particles on root.");
                Object.DestroyImmediate(host.gameObject);
                return;
            }

            if (particleRenderer != null)
            {
                var hostRenderer = host.GetComponent<ParticleSystemRenderer>();
                if (hostRenderer != null)
                {
                    UnityEditorInternal.ComponentUtility.CopyComponent(particleRenderer);
                    UnityEditorInternal.ComponentUtility.PasteComponentValues(hostRenderer);
                }
            }

            Object.DestroyImmediate(particleRenderer, true);
            Object.DestroyImmediate(particleSystem, true);
            Debug.Log($"Moved drop ParticleSystem from {root.name} to {log.name}/{DropParticlesChildName} (log scale compensated).");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Skipped moving drop particles ({ex.GetType().Name}: {ex.Message}); leaving them on root.");
        }
    }

    static Transform CreateDropParticleHost(Transform log)
    {
        var existing = log.Find(DropParticlesChildName);
        if (existing != null)
        {
            EnsureDropParticleHostScale(existing, log);
            return existing;
        }

        var hostGo = new GameObject(DropParticlesChildName);
        hostGo.transform.SetParent(log, false);
        hostGo.transform.localPosition = Vector3.zero;
        hostGo.transform.localRotation = Quaternion.identity;
        EnsureDropParticleHostScale(hostGo.transform, log);
        return hostGo.transform;
    }

    static void EnsureDropParticleHostScale(Transform host, Transform log)
    {
        var s = log.localScale;
        host.localScale = new Vector3(
            Mathf.Approximately(s.x, 0f) ? 1f : 1f / s.x,
            Mathf.Approximately(s.y, 0f) ? 1f : 1f / s.y,
            Mathf.Approximately(s.z, 0f) ? 1f : 1f / s.z);
    }

    static void RelocateParticleSystemToDropHost(ParticleSystem ps, ParticleSystemRenderer psr, Transform log)
    {
        if (ps == null)
            return;

        try
        {
            var host = CreateDropParticleHost(log);
            UnityEditorInternal.ComponentUtility.CopyComponent(ps);
            if (!UnityEditorInternal.ComponentUtility.PasteComponentAsNew(host.gameObject))
            {
                Debug.LogWarning($"Could not relocate ParticleSystem from {log.name} to {DropParticlesChildName}.");
                return;
            }

            if (psr != null)
            {
                var hostRenderer = host.GetComponent<ParticleSystemRenderer>();
                if (hostRenderer != null)
                {
                    UnityEditorInternal.ComponentUtility.CopyComponent(psr);
                    UnityEditorInternal.ComponentUtility.PasteComponentValues(hostRenderer);
                }
            }

            Object.DestroyImmediate(psr, true);
            Object.DestroyImmediate(ps, true);
            Debug.Log($"Relocated ParticleSystem from {log.name} to {DropParticlesChildName} (inverse log scale).");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Failed relocating log ParticleSystem ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    static void SyncLogColliderToPickupMesh(Transform log, Mesh pickupMesh)
    {
        if (pickupMesh == null)
            return;

        var box = log.GetComponent<BoxCollider>();
        if (box == null)
            return;

        var bounds = pickupMesh.bounds;
        box.center = bounds.center;
        box.size = bounds.size;
    }

    static void FixBackpackItemIcon(GameObject root)
    {
        // Meadows-style: reference the sub-sprite embedded in IronBackpack_Icon.png (type 3).
        var icon = LoadIconSpriteFromTextureAsset();
        if (icon == null)
        {
            Debug.LogWarning($"No sprite on {IconTexturePath}. Run icon refresh (imports as Sprite type).");
            return;
        }

        foreach (var component in root.GetComponents<Component>())
        {
            if (component == null)
                continue;

            var serialized = new SerializedObject(component);
            var icons = serialized.FindProperty("m_itemData.m_shared.m_icons");
            if (icons == null || !icons.isArray)
                continue;

            icons.arraySize = 1;
            icons.GetArrayElementAtIndex(0).objectReferenceValue = icon;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return;
        }

        Debug.LogWarning($"Could not find ItemDrop icon array on {root.name}.");
    }

    static bool CropCenterSquarePng(string sourcePath, string destinationPath, int size)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!source.LoadImage(bytes))
            return false;

        var side = Mathf.Min(source.width, source.height);
        var x = (source.width - side) / 2;
        var y = (source.height - side) / 2;
        var pixels = source.GetPixels(x, y, side, side);

        var cropped = new Texture2D(side, side, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply();

        var output = side == size ? cropped : ScaleTexture(cropped, size, size);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllBytes(destinationPath, output.EncodeToPNG());

        Object.DestroyImmediate(source);
        Object.DestroyImmediate(cropped);
        if (!ReferenceEquals(output, cropped))
            Object.DestroyImmediate(output);

        return true;
    }

    static Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    static void CopyMeshSurface(Mesh target, Mesh source)
    {
        target.indexFormat = source.indexFormat;
        target.vertices = source.vertices;
        target.normals = source.normals;
        target.tangents = source.tangents;
        target.uv = source.uv;
        if (source.uv2 != null && source.uv2.Length > 0)
            target.uv2 = source.uv2;
        target.subMeshCount = source.subMeshCount;
        for (var sub = 0; sub < source.subMeshCount; sub++)
            target.SetTriangles(source.GetTriangles(sub), sub);
        target.RecalculateBounds();
    }

    static BoneWeight[] CreateRootBoneWeights(int vertexCount)
    {
        var weights = new BoneWeight[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            weights[i].boneIndex0 = 0;
            weights[i].weight0 = 1f;
        }

        return weights;
    }

    static string AssetPathToAbsoluteFolder(string assetPath) =>
        Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath)));

    static void CopyMeshyTextures(string meshyFolder)
    {
        if (string.IsNullOrEmpty(meshyFolder) || !Directory.Exists(meshyFolder))
        {
            Debug.LogWarning($"Could not resolve Meshy folder for texture copy: {meshyFolder}");
            return;
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        foreach (var (srcName, dstAssetPath) in TextureCopies)
        {
            var src = Path.Combine(meshyFolder, srcName);
            var dst = Path.Combine(projectRoot, dstAssetPath);
            if (!File.Exists(src))
            {
                Debug.LogWarning($"Meshy texture missing, skipped: {src}");
                continue;
            }

            File.Copy(src, dst, overwrite: true);
            Debug.Log($"Copied {srcName} → {dstAssetPath}");
        }
    }

    static void ConfigureIronBackpackMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        // Built-in Standard renders reliably in-game (same as the working Meadows backpack).
        // The Valheim DS shader clips via alpha-cutout and rendered invisible in-game.
        var shader = Shader.Find("Standard");
        if (mat == null || shader == null)
        {
            Debug.LogWarning($"Could not load {MaterialPath} or built-in Standard shader");
            return;
        }

        mat.shader = shader;

        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Texture2D/IronBackpack_BaseMap.png");
        var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Texture2D/IronBackpack_Normal.png");
        var maskMap = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Texture2D/IronBackpack_MaskMap.png");

        if (baseMap != null)
            mat.SetTexture("_MainTex", baseMap);
        if (normalMap != null)
            mat.SetTexture("_BumpMap", normalMap);
        if (maskMap != null)
            mat.SetTexture("_MetallicGlossMap", maskMap);

        mat.SetColor("_Color", Color.white);
        mat.SetFloat("_Metallic", MaterialMetallic);
        mat.SetFloat("_Glossiness", MaterialSmoothness);
        mat.SetFloat("_GlossMapScale", MaterialSmoothness);
        mat.SetFloat("_Mode", 0f);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ENABLETRIPLANARPROJECTION_ON");
        mat.DisableKeyword("_TRIPLANARSPACEPROJECTION_OBJECTSPACE");
        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_METALLICGLOSSMAP");
        mat.renderQueue = -1;
        EditorUtility.SetDirty(mat);
    }

    static void DisableIronBackpackClothOnPrefabs()
    {
        foreach (var prefabPath in PrefabPaths)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogWarning($"Could not load prefab: {prefabPath}");
                continue;
            }

            var cloth = FindChildByName(root.transform, "IronBackpack_Cloth");
            if (cloth != null)
            {
                // DELETE (not just deactivate) the cloth. BoneReorder iterates every
                // SkinnedMeshRenderer under attach_skin (including inactive ones) and indexes the
                // attached instance's renderers 1:1. A leftover inactive cloth renderer makes the
                // counts differ → IndexOutOfRangeException → bones never rebind → mesh renders wrong.
                Object.DestroyImmediate(cloth.gameObject);
                Debug.Log($"Removed IronBackpack_Cloth from {prefabPath}");
            }
            else
                Debug.Log($"IronBackpack_Cloth already absent in {prefabPath}");

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindChildByName(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindChildByName(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
