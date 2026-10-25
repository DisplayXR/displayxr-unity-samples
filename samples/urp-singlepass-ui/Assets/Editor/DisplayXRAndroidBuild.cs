// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0
//
// Android build for this sample (Vulkan, ARM64, IL2CPP) — the headless
// counterpart of File > Build Profiles > Android > Build.
//
//   Unity -batchmode -quit -projectPath <this sample> \
//         -executeMethod DisplayXRAndroidBuild.Build [-dxrVersion 2.4.0] \
//         -logFile <somewhere outside the repo>
//
// Output: Builds/Android/<productName>.apk. Unity signs it with its debug key;
// a release APK is re-signed afterwards with apksigner (see README.md).
//
// Only Android-specific settings are written (graphics APIs, scripting backend,
// architectures, min SDK and application id for the Android target, and the
// DisplayXR loader on XR Plug-in Management's Android tab), so the Windows build
// is unaffected. The active build target is switched only inside Build(); run it
// in batchmode against a clean checkout (or switch back to Windows afterwards in
// an interactive editor).

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

public static class DisplayXRAndroidBuild
{
    // Stable from the first signed release on: the release keystore binds every
    // future update to this id. Do not change it.
    public const string ApplicationId = "com.displayxr.samples.urp_singlepass_ui";

    const string LoaderType = "DisplayXR.DisplayXRDisplayLoader";

    [MenuItem("DisplayXR/Android/Configure Android Settings")]
    public static void Configure()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
        PlayerSettings.Android.forceInternetPermission = false;

        // XR Plug-in Management: the DisplayXR display provider on the Android tab.
        // (Unity's OpenXR loader stays unassigned on every tab — the provider drives
        // OpenXR itself.)
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                                               out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
            throw new InvalidOperationException("[DisplayXRAndroidBuild] no XRGeneralSettingsPerBuildTarget");
        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        var gs = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        gs.InitManagerOnStart = true;
        bool assigned = XRPackageMetadataStore.AssignLoader(gs.Manager, LoaderType, BuildTargetGroup.Android);
        Debug.Log($"[DisplayXRAndroidBuild] AssignLoader({LoaderType}, Android) = {assigned}; " +
                  $"loaders={gs.Manager.activeLoaders.Count}");
        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(gs);
        EditorUtility.SetDirty(gs.Manager);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("DisplayXR/Android/Build Android APK")]
    public static void Build()
    {
        int exitCode = 1;
        string savedVersion = PlayerSettings.bundleVersion;
        int savedCode = PlayerSettings.Android.bundleVersionCode;
        try
        {
            Configure();

            // Optional -dxrVersion X.Y.Z: applied for this build only (bundleVersion is
            // shared with the Windows player), versionCode = X*10000 + Y*100 + Z.
            string version = GetArg("-dxrVersion");
            if (!string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
                PlayerSettings.Android.bundleVersionCode = VersionCode(version);
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);
            if (scenes.Count == 0)
                throw new InvalidOperationException("[DisplayXRAndroidBuild] no enabled scenes in Build Settings");

            string outDir = Path.Combine("Builds", "Android");
            Directory.CreateDirectory(outDir);
            string apk = Path.Combine(outDir, PlayerSettings.productName + ".apk");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            var sum = report.summary;
            Debug.Log($"[DisplayXRAndroidBuild] result={sum.result} errors={sum.totalErrors} " +
                      $"out={sum.outputPath} size={sum.totalSize} id={ApplicationId} " +
                      $"version={PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})");
            exitCode = sum.result == BuildResult.Succeeded ? 0 : 1;
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
        finally
        {
            PlayerSettings.bundleVersion = savedVersion;
            PlayerSettings.Android.bundleVersionCode = savedCode;
            AssetDatabase.SaveAssets();
        }
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }

    static int VersionCode(string v)
    {
        var p = v.Split('.');
        int Part(int i) => i < p.Length && int.TryParse(p[i], out int n) ? n : 0;
        return Part(0) * 10000 + Part(1) * 100 + Part(2);
    }

    static string GetArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
