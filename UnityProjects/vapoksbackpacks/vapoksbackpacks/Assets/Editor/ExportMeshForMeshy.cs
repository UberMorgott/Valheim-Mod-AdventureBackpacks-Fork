using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Exports legacy Mesh assets to OBJ for external tools (e.g. Meshy AI texturing).
/// Valheim attachment meshes are authored at ~0.003m scale; export applies a scale factor.
/// </summary>
public static class ExportMeshForMeshy
{
    const float ExportScale = 1000f;

    /// <summary>Equipped-on-character meshes (attach_skin → IronBackpack + IronBackpack_Cloth).</summary>
    static readonly (string path, string objectName)[] IronBackpackEquippedMeshes =
    {
        ("Assets/Mesh/IronBackpack.asset", "IronBackpack"),
        ("Assets/Mesh/IronBackpack_Cloth.asset", "IronBackpack_Cloth"),
    };

    static readonly (string assetPath, string exportFileName)[] IronBackpackTextures =
    {
        ("Assets/Texture2D/IronBackpack_BaseMap.png", "IronBackpack_BaseMap.png"),
        ("Assets/Texture2D/IronBackpack_Normal.png", "IronBackpack_Normal.png"),
        ("Assets/Texture2D/IronBackpack_MaskMap.png", "IronBackpack_MaskMap.png"),
    };

    const string EquippedExportFolder = "IronBackpack_equipped_for_Meshy";
    const string EquippedObjName = "IronBackpack_equipped.obj";
    const string MtlName = "IronBackpack.mtl";
    const string MaterialName = "IronBackpack";

    [MenuItem("Tools/Export For Meshy/Iron Backpack — Equipped + Textures (for Meshy)")]
    static void ExportIronBackpackEquippedWithTextures()
    {
        var outDir = Path.Combine(Application.dataPath, "..", "Exports", EquippedExportFolder);
        Directory.CreateDirectory(outDir);

        CopyIronBackpackTextures(outDir);
        WriteMaterialFile(Path.Combine(outDir, MtlName));

        var objPath = Path.Combine(outDir, EquippedObjName);
        ExportMeshesToObj(IronBackpackEquippedMeshes, objPath, MtlName, MaterialName);

        EditorUtility.RevealInFinder(objPath);
        Debug.Log(
            "Exported equipped iron backpack (attach_skin meshes) with textures for Meshy:\n" +
            $"{objPath}\n" +
            "Upload the .obj to Meshy; optionally also upload IronBackpack_BaseMap.png as a reference image.");
    }

    [MenuItem("Tools/Export For Meshy/Iron Backpack (OBJ, mesh only)")]
    static void ExportIronBackpackMeshOnly()
    {
        var outDir = Path.Combine(Application.dataPath, "..", "Exports");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "IronBackpack_for_Meshy.obj");

        ExportMeshesToObj(IronBackpackEquippedMeshes, outPath, null, null);
        EditorUtility.RevealInFinder(outPath);
        Debug.Log($"Exported iron backpack mesh only: {outPath}");
    }

    [MenuItem("Tools/Export For Meshy/Selected Mesh Assets (OBJ)")]
    static void ExportSelectedMeshes()
    {
        var meshes = Selection.objects;
        if (meshes == null || meshes.Length == 0)
        {
            EditorUtility.DisplayDialog("Export For Meshy", "Select one or more Mesh assets in the Project window.", "OK");
            return;
        }

        var entries = new System.Collections.Generic.List<(string path, string objectName)>();
        foreach (var obj in meshes)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (obj is Mesh)
                entries.Add((path, obj.name));
        }

        if (entries.Count == 0)
        {
            EditorUtility.DisplayDialog("Export For Meshy", "Selection contains no Mesh assets.", "OK");
            return;
        }

        var outDir = Path.Combine(Application.dataPath, "..", "Exports");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, $"{entries[0].objectName}_for_Meshy.obj");
        ExportMeshesToObj(entries.ToArray(), outPath, null, null);
        EditorUtility.RevealInFinder(outPath);
        Debug.Log($"Exported {entries.Count} mesh(es) for Meshy: {outPath}");
    }

    [MenuItem("Tools/Export For Meshy/Selected Mesh Assets (OBJ)", true)]
    static bool ExportSelectedMeshesValidate() =>
        Selection.objects != null && Selection.objects.Length > 0;

    static void CopyIronBackpackTextures(string outDir)
    {
        foreach (var (assetPath, exportFileName) in IronBackpackTextures)
        {
            var src = Path.GetFullPath(assetPath);
            if (!File.Exists(src))
            {
                Debug.LogWarning($"Texture not found, skipped: {assetPath}");
                continue;
            }

            File.Copy(src, Path.Combine(outDir, exportFileName), overwrite: true);
        }
    }

    static void WriteMaterialFile(string mtlPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# IronBackpack.mat → Texture2D maps");
        sb.AppendLine($"newmtl {MaterialName}");
        sb.AppendLine("Ka 1.000 1.000 1.000");
        sb.AppendLine("Kd 1.000 1.000 1.000");
        sb.AppendLine("Ks 0.200 0.200 0.200");
        sb.AppendLine("Ns 32.000");
        sb.AppendLine("d 1.0");
        sb.AppendLine("illum 2");
        sb.AppendLine("map_Kd IronBackpack_BaseMap.png");
        sb.AppendLine("map_Bump IronBackpack_Normal.png");
        sb.AppendLine("# MaskMap (metallic/smoothness): IronBackpack_MaskMap.png — upload separately in Meshy if needed");
        File.WriteAllText(mtlPath, sb.ToString());
    }

    static void ExportMeshesToObj(
        (string path, string objectName)[] meshes,
        string outPath,
        string mtlFileName,
        string materialName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Exported from AdventureBackpacks Unity project for Meshy");
        sb.AppendLine("# Meshes: attach_skin equipped parts (IronBackpack + IronBackpack_Cloth)");
        sb.AppendLine($"# Scale factor: {ExportScale.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrEmpty(mtlFileName))
            sb.AppendLine($"mtllib {mtlFileName}");

        var vertexOffset = 1;
        var culture = CultureInfo.InvariantCulture;

        foreach (var (path, objectName) in meshes)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                Debug.LogWarning($"Skipping missing mesh: {path}");
                continue;
            }

            sb.AppendLine($"o {objectName}");
            if (!string.IsNullOrEmpty(materialName))
                sb.AppendLine($"usemtl {materialName}");

            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var hasNormals = normals != null && normals.Length == vertices.Length;
            var hasUvs = uvs != null && uvs.Length == vertices.Length;

            for (var i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i] * ExportScale;
                sb.AppendLine(string.Format(culture, "v {0} {1} {2}", v.x, v.y, v.z));
            }

            if (hasNormals)
            {
                for (var i = 0; i < normals.Length; i++)
                {
                    var n = normals[i];
                    sb.AppendLine(string.Format(culture, "vn {0} {1} {2}", n.x, n.y, n.z));
                }
            }

            if (hasUvs)
            {
                for (var i = 0; i < uvs.Length; i++)
                {
                    var uv = uvs[i];
                    sb.AppendLine(string.Format(culture, "vt {0} {1}", uv.x, uv.y));
                }
            }

            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetIndices(sub);
                for (var t = 0; t < indices.Length; t += 3)
                {
                    var i0 = indices[t] + vertexOffset;
                    var i1 = indices[t + 1] + vertexOffset;
                    var i2 = indices[t + 2] + vertexOffset;

                    if (hasUvs && hasNormals)
                        sb.AppendLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
                    else if (hasUvs)
                        sb.AppendLine($"f {i0}/{i0} {i1}/{i1} {i2}/{i2}");
                    else if (hasNormals)
                        sb.AppendLine($"f {i0}//{i0} {i1}//{i1} {i2}//{i2}");
                    else
                        sb.AppendLine($"f {i0} {i1} {i2}");
                }
            }

            vertexOffset += vertices.Length;
        }

        File.WriteAllText(outPath, sb.ToString());
    }
}
