// Temporary batchmode build entry point for end-to-end capture testing.
// (Not part of the shipped test app; safe to delete.)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

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

        // Derive the output name from the project's productName so the build
        // output, BIN_DIR, and installer never drift on a rename.
        var product = PlayerSettings.productName;
        var outDir = "Builds/Win64/" + product;
        Directory.CreateDirectory(outDir);
        var exe = Path.Combine(outDir, product + ".exe");

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
