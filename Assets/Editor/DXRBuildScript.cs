// Temporary batchmode build entry point for end-to-end capture testing.
// (Not part of the shipped test app; safe to delete.)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildScript
{
    public static void BuildWindows64()
    {
        var scenes = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) scenes.Add(s.path);
        if (scenes.Count == 0)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.StartsWith("Assets/")) scenes.Add(p);
            }
        }

        // Force D3D12 — the supported API for the DisplayXR Unity plugin.
        // (A previous version of this script forced D3D11, which silently
        // reverted the project's D3D12 setting on every batch build and has
        // known geometry corruption + wrong-aspect capture: runtime#431.)
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D12 });
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);

        const string outDir = "Builds/Win64/DisplayXR-test";
        Directory.CreateDirectory(outDir);
        var exe = Path.Combine(outDir, "DisplayXR-test.exe");

        var opts = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        Debug.Log($"[DXRBuild] building {scenes.Count} scene(s) -> {exe}");
        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;
        Debug.Log($"[DXRBuild] result={summary.result} errors={summary.totalErrors} out={summary.outputPath}");
        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
