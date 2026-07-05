using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds AssetBundles (optional), copies them into the C# mod project, MSBuild Release,
/// and deploys the merged AdventureBackpacks.dll to Valheim BepInEx plugins.
/// </summary>
public static class BuildAndDeployAdventureBackpacks
{
    const string MenuRoot = "Tools/Adventure Backpacks/";
    const string PrefDeployDll = "AdventureBackpacks.DeployDllPath";

    const string DefaultDeployDll =
        @"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\plugins\Translations\AdventureBackpacks.dll";

    static readonly string[] DefaultBundles = { "vapokbackpacks", "backpack_black_forest" };

    [MenuItem(MenuRoot + "Build AssetBundles and Deploy Mod", false, 0)]
    static void BuildBundlesAndDeploy()
    {
        if (!EditorUtility.DisplayDialog(
                "Build and Deploy",
                "This will:\n" +
                "1. Build all AssetBundles → Assets/StreamingAssets/\n" +
                "2. Copy iron-forest bundles into AdventureBackpacks/Assets/Bundles/\n" +
                "3. MSBuild Release (AdventureBackpacks.sln)\n" +
                "4. Copy DLL to Valheim plugins\n\n" +
                "Close Valheim first. Continue?",
                "Build and Deploy",
                "Cancel"))
            return;

        try
        {
            EditorUtility.DisplayProgressBar("Adventure Backpacks", "Building AssetBundles…", 0.1f);
            BuildAllAssetBundles();
            DeployModInternal(includeBundleBuild: false);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem(MenuRoot + "Deploy Mod (Skip Bundle Build)", false, 1)]
    static void DeployOnly()
    {
        if (!EditorUtility.DisplayDialog(
                "Deploy Mod",
                "Copy latest StreamingAssets bundles, MSBuild Release, deploy DLL.\n\n" +
                "Use this after Assets → Build AssetBundles.\nClose Valheim first. Continue?",
                "Deploy",
                "Cancel"))
            return;

        try
        {
            DeployModInternal(includeBundleBuild: false);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem(MenuRoot + "Set Valheim Plugin DLL Path…", false, 20)]
    static void SetDeployPath()
    {
        var current = EditorPrefs.GetString(PrefDeployDll, DefaultDeployDll);
        var picked = EditorUtility.OpenFilePanel(
            "Select AdventureBackpacks.dll destination (merged plugin)",
            Path.GetDirectoryName(current) ?? "",
            "dll");
        if (string.IsNullOrEmpty(picked))
            return;

        EditorPrefs.SetString(PrefDeployDll, picked);
        Debug.Log($"Deploy path set to: {picked}");
    }

    static void DeployModInternal(bool includeBundleBuild)
    {
        if (includeBundleBuild)
            BuildAllAssetBundles();

        var hdRoot = GetAdventureBackpacksHdRoot();
        var streaming = Path.Combine(Application.dataPath, "StreamingAssets");
        var bundlesDst = Path.Combine(hdRoot, "AdventureBackpacks", "Assets", "Bundles");
        var solution = Path.Combine(hdRoot, "AdventureBackpacks.sln");
        var builtDll = Path.Combine(hdRoot, "AdventureBackpacks", "bin", "Release", "AdventureBackpacks.dll");
        var deployDll = EditorPrefs.GetString(PrefDeployDll, DefaultDeployDll);

        EditorUtility.DisplayProgressBar("Adventure Backpacks", "Copying bundles…", 0.25f);
        CopyBundles(streaming, bundlesDst, DefaultBundles);

        EditorUtility.DisplayProgressBar("Adventure Backpacks", "MSBuild Release…", 0.5f);
        RunMsBuildRelease(solution, builtDll);

        if (IsValheimRunning())
        {
            EditorUtility.DisplayDialog(
                "Deploy blocked",
                "Valheim is running and has the plugin DLL locked.\n\nClose the game and run Deploy again.",
                "OK");
            Debug.LogWarning("Deploy skipped: Valheim is running.");
            return;
        }

        EditorUtility.DisplayProgressBar("Adventure Backpacks", "Deploying DLL…", 0.85f);
        DeployDll(builtDll, deployDll);

        EditorUtility.DisplayDialog(
            "Deploy complete",
            $"DLL deployed to:\n{deployDll}\n\nLaunch Valheim and re-equip the backpack to test.",
            "OK");
    }

    static void BuildAllAssetBundles()
    {
        var dir = "Assets/StreamingAssets";
        if (!Directory.Exists(Path.Combine(Application.dataPath, "StreamingAssets")))
            Directory.CreateDirectory(dir);

        BuildPipeline.BuildAssetBundles(dir, BuildAssetBundleOptions.None, EditorUserBuildSettings.activeBuildTarget);
        AssetDatabase.Refresh();
        Debug.Log("AssetBundles built → Assets/StreamingAssets/");
    }

    static string GetAdventureBackpacksHdRoot()
    {
        // Assets → vapoksbackpacks → vapoksbackpacks → UnityProjects → AdventureBackpacksHD
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
    }

    static void CopyBundles(string streamingDir, string dstDir, string[] names)
    {
        if (!Directory.Exists(streamingDir))
            throw new InvalidOperationException($"Missing StreamingAssets: {streamingDir}\nBuild AssetBundles first.");

        Directory.CreateDirectory(dstDir);

        foreach (var name in names)
        {
            var src = Path.Combine(streamingDir, name);
            var dst = Path.Combine(dstDir, name);
            if (!File.Exists(src))
                throw new FileNotFoundException($"Bundle not found (build AssetBundles first): {src}");

            File.Copy(src, dst, overwrite: true);
            var info = new FileInfo(dst);
            Debug.Log($"Copied bundle {name} ({info.Length:N0} bytes, {info.LastWriteTime})");
        }
    }

    static void RunMsBuildRelease(string solutionPath, string expectedDll)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution not found: {solutionPath}");

        var msbuild = FindMsBuild();
        if (msbuild == null)
            throw new InvalidOperationException("MSBuild not found. Install Visual Studio 2022 with MSBuild.");

        var args = $"\"{solutionPath}\" /t:Build /p:Configuration=Release /p:Platform=\"Any CPU\" /v:minimal /nologo";
        var exit = RunProcess(msbuild, args, Path.GetDirectoryName(solutionPath));

        if (!File.Exists(expectedDll))
            throw new InvalidOperationException($"MSBuild did not produce:\n{expectedDll}\n(exit {exit})");

        if (exit != 0)
            Debug.LogWarning($"MSBuild exit {exit} (author post-build xcopy to X:\\ often fails; DLL is still valid).");

        var info = new FileInfo(expectedDll);
        Debug.Log($"Built {expectedDll} ({info.Length:N0} bytes, {info.LastWriteTime})");
    }

    static void DeployDll(string builtDll, string deployPath)
    {
        var destDir = Path.GetDirectoryName(deployPath);
        if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
            throw new DirectoryNotFoundException($"Plugin folder not found: {destDir}\nUse {MenuRoot}Set Valheim Plugin DLL Path…");

        File.Copy(builtDll, deployPath, overwrite: true);

        var a = FileHash(builtDll);
        var b = FileHash(deployPath);
        if (a != b)
            throw new IOException("Deploy hash mismatch — copy may have failed.");

        Debug.Log($"Deployed OK → {deployPath}");
    }

    static string FindMsBuild()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    static int RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? "",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException($"Failed to start: {fileName}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout))
            Debug.Log(stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr))
            Debug.Log(stderr.Trim());

        return process.ExitCode;
    }

    static bool IsValheimRunning()
    {
        return Process.GetProcessesByName("valheim").Length > 0;
    }

    static string FileHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
