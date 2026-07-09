// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0
//
// Headless driver for the URP foreground-clip prototype setup (#57/#129).
//
// The interactive path is: open the project (URPSetupBootstrap runs on load) →
// Render Pipeline Converter (Built-in → URP material upgrade) → DisplayXR → Setup
// URP Foreground Clip. None of those run under -batchmode -quit (the bootstrap's
// delayCall never ticks, the converter and installer are menu-driven). This method
// performs all three synchronously so a Win64 Player can be built headlessly:
//
//   Unity.exe -batchmode -quit -projectPath <repo> \
//             -executeMethod ProtoAutomation.SetupAll -logFile <log>
//
// Safe to re-run (every step is idempotent).
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal;
using UnityEngine;

internal static class ProtoAutomation
{
    public static void SetupAll()
    {
        Debug.Log("[ProtoAutomation] === URP foreground-clip setup begin ===");

        // 1. URP pipeline assets + depth texture (normally URPSetupBootstrap's
        //    delayCall, which doesn't fire under -batchmode -quit).
        URPSetupBootstrap.EnsureSetup();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 2. Built-in → URP material upgrade (the crate uses the Standard shader;
        //    unconverted Built-in materials render magenta under URP).
        Debug.Log("[ProtoAutomation] running Built-in → URP material upgrade...");
        Converters.RunInBatchMode(
            ConverterContainerId.BuiltInToURP,
            new List<ConverterId> { ConverterId.Material },
            ConverterFilter.Inclusive);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 3. (Removed) The URP off-axis projection fix is no longer wired here.
        //    It was replaced in plugin v2.2.0 by the XR display provider handing
        //    Unity a full projection matrix, so
        //    DisplayXRUrpInstaller.InstallProjectionFix no longer exists. The
        //    committed URP-Renderer.asset still carries the opt-in Full Screen Pass
        //    foreground-clip feature + its material.

        Debug.Log("[ProtoAutomation] === setup complete ===");
    }
}
