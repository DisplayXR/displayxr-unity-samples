// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Idempotent URP pipeline setup. Runs on every editor load and self-heals:
/// - If no URP asset is assigned (fresh clone, deleted Assets/Settings,
///   etc.), creates one with XR-friendly defaults and wires it into
///   Project Settings → Graphics + Quality.
/// - If a URP asset is already assigned, only patches UpscalingFilter to
///   Auto (if it drifted) and ensures the camera depth texture is on. No-op
///   otherwise.
///
/// Copied from displayxr-unity-test-2d-ui (the canonical URP template) and
/// extended for the transparent-overlay foreground-clip prototype: the depth-
/// based clip pass (DisplayXRForegroundClipURP) samples _CameraDepthTexture,
/// so the pipeline asset MUST generate the depth texture. We force
/// m_RequireDepthTexture = true here regardless of the path taken.
///
/// Why this exists: Unity 6's URP-converter wizard creates the URP asset in
/// the Editor but its default UpscalingFilter sometimes lands on FSR/STP,
/// which trips an OpenXR project-validator warning. Pinning to Auto keeps
/// OpenXR happy. Gating purely on observable state
/// (`GraphicsSettings.defaultRenderPipeline`) makes the bootstrap survive
/// re-clones, plugin reinstalls, and Library wipes.
/// </summary>
[InitializeOnLoad]
internal static class URPSetupBootstrap
{
    private const string kAssetDir = "Assets/Settings";
    private const string kPipelineAssetPath = "Assets/Settings/URP-Pipeline.asset";
    private const string kRendererAssetPath = "Assets/Settings/URP-Renderer.asset";

    static URPSetupBootstrap()
    {
        // Defer until the editor has finished loading — AssetDatabase.CreateAsset
        // can't run during the static ctor on first import.
        EditorApplication.delayCall += TrySetup;
    }

    /// <summary>
    /// Synchronous entry point for headless automation (-executeMethod). In
    /// -batchmode -quit the editor exits before the delayCall fires, so the
    /// automation driver calls this directly to guarantee the URP assets exist.
    /// </summary>
    internal static void EnsureSetup() => TrySetup();

    private static void TrySetup()
    {
        EditorApplication.delayCall -= TrySetup;
        if (GraphicsSettings.defaultRenderPipeline != null)
        {
            // URP asset already assigned — patch upscaling filter + depth texture.
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset existing)
            {
                bool dirty = false;
                if (existing.upscalingFilter != UpscalingFilterSelection.Auto)
                {
                    existing.upscalingFilter = UpscalingFilterSelection.Auto;
                    dirty = true;
                }
                if (EnsureDepthTexture(existing)) dirty = true;
                if (dirty)
                {
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[DisplayXRTest] URP pipeline patched (UpscalingFilter=Auto, depth texture on).");
                }
            }
            return;
        }

        Directory.CreateDirectory(kAssetDir);

        // Renderer first — pipeline asset references it.
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        renderer.name = "URP-Renderer";
        AssetDatabase.CreateAsset(renderer, kRendererAssetPath);

        var pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
        pipeline.name = "URP-Pipeline";
        AssetDatabase.CreateAsset(pipeline, kPipelineAssetPath);

        // Wire the renderer into the pipeline asset via SerializedObject (the
        // public API doesn't expose the renderer list mutator before play).
        var so = new SerializedObject(pipeline);
        var rendererList = so.FindProperty("m_RendererDataList");
        if (rendererList != null)
        {
            rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // XR-friendly defaults.
        pipeline.upscalingFilter = UpscalingFilterSelection.Auto;
        pipeline.msaaSampleCount = 1; // disable MSAA — not needed for our Kooima path
        EnsureDepthTexture(pipeline); // foreground-clip pass needs _CameraDepthTexture

        EditorUtility.SetDirty(pipeline);
        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssets();

        // Assign to GraphicsSettings + every quality level.
        GraphicsSettings.defaultRenderPipeline = pipeline;
        for (int i = 0; i < QualitySettings.count; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[DisplayXRTest] URP pipeline asset created at " + kPipelineAssetPath +
                  " with UpscalingFilter=Auto, depth texture on, assigned to all quality levels.");
    }

    /// <summary>
    /// Force the camera depth texture on (serialized field m_RequireDepthTexture).
    /// The foreground-clip fullscreen pass reconstructs view-space eye Z from
    /// _CameraDepthTexture, so it must be generated. Returns true if changed.
    /// </summary>
    private static bool EnsureDepthTexture(UniversalRenderPipelineAsset asset)
    {
        var so = new SerializedObject(asset);
        var prop = so.FindProperty("m_RequireDepthTexture");
        if (prop != null && !prop.boolValue)
        {
            prop.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
        return false;
    }
}
