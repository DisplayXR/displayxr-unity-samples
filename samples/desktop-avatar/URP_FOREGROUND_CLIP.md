# URP transparent overlay + per-eye foreground clip (prototype)

Branch: `feat/urp-transparent-clip` (off `feat/multi-object-clip`).

This is a **test-repo-local prototype** that ports the transparent overlay to URP
and validates a new approach to foreground-only clipping that works correctly
**per-eye** on URP — something the plugin's current URP path can't do.

## The problem it solves

URP builds each eye's projection from `views[i].fov` + `Camera.farClipPlane` and
**ignores** `Camera.SetStereoProjectionMatrix`. So the plugin's per-eye foreground
far (which BiRP injects straight into the projection matrix) can't reach URP that
way. The only existing lever is the single `Camera.farClipPlane`, shared by both
eyes — wrong for the off-eye, because in display-centric mode **each eye sees the
virtual display at a different depth** (its own `eye.z` after the m2v scaling +
ipd/parallax modifiers).

## The approach

Enforce the clip per-eye in **screen space** after the scene renders:

1. The native plugin already computes each eye's foreground far (`far_eff`) and
   embeds it in the per-view projection matrices returned by
   `DisplayXRFeature.GetStereoMatrices` (`leftProj`/`rightProj`). We recover it
   with `far = proj.m23 / (proj.m22 + 1)` — the exact `far_eff` Kooima used — for
   **both** eyes (URP's built-in path keeps only the left one).
2. `ForegroundClipURPDriver` publishes `[leftFar, rightFar]` to the
   `_DXRForegroundFar` global each frame, and forces the rig camera's far large so
   each eye renders the whole scene (defeating the rig's single-far clamp).
3. `DisplayXRForegroundClipURP.shader` (run via URP's built-in
   **FullScreenPassRendererFeature**, BeforeRenderingPostProcessing — URP 17 has no
   AfterRenderingTransparents injection point; this is the first slot after the
   transparent queue, and the opaque overlay is fully in the depth buffer by then)
   reconstructs each
   fragment's view-space eye Z from the depth texture and writes transparent black
   for anything farther than **that eye's** `far_eff`. Multipass → `unity_StereoEyeIndex`
   selects the eye. Color **and** alpha are zeroed (alpha-only was the #129 failure).

If this validates, the shader + feature + global push move into the plugin (behind
a URP-guarded assembly) so every app gets per-eye URP foreground clip for free.

## First-time setup in Unity

1. Open the project in **Unity 6000.4.0f1**. On load, `URPSetupBootstrap` creates
   `Assets/Settings/URP-Pipeline.asset` + `URP-Renderer.asset`, assigns them to
   Graphics + all quality levels, and turns the **camera depth texture on**
   (required by the clip pass). Check the Console for its log line.
2. **Upgrade materials to URP:** `Window → Rendering → Render Pipeline Converter`
   → *Built-in to URP* → tick **Material Upgrade** → Convert. (BiRP/Standard
   materials render magenta under URP until converted.)
3. Run **`DisplayXR → Setup URP Foreground Clip`** (menu). It creates the clip
   material and tries to add the Full Screen Pass feature to the renderer. If the
   dialog says auto-wiring didn't complete, do the 3 manual clicks it lists:
   - Select `Assets/Settings/URP-Renderer.asset`
   - **Add Renderer Feature → Full Screen Pass Renderer Feature**
   - Pass Material = `DXRForegroundClip`, Injection Point = **Before Rendering
     Post Processing** (URP 17 has no "After Rendering Transparents"; this is the
     first point after the transparent queue), Requirements = **Depth**

## Testing

- Open `Assets/CubeTest.unity`, **Window → DisplayXR → Preview Window → Start**
  (or build a Player). The tiger straddles the display plane; only the part poking
  **in front of** the screen should show; geometry behind the plane is cut away.
- The `ForegroundClipURPDriver` self-installs at scene load (only under URP).
  Press **C** to toggle the clip on/off — geometry behind the plane should appear
  (off) / disappear (on) cleanly, with no leftover color.
- Enable `diagnosticLog` on the driver to print `farL`/`farR` and their
  disagreement (Δmm). Off-axis head positions should show a non-zero Δ — that's
  exactly the per-eye difference the single-far path got wrong.

## Validation status (2026-06-14, Win64 / RTX 3080 / D3D12)

Headless build + run on this machine against the installed DisplayXR runtime
(active OpenXR runtime, advertises **`XR_EXT_view_rig` Version=2**):

- ✅ C# + shader compile clean; `DisplayXR/ForegroundClipURP` is built into the Player.
- ✅ `URPSetupBootstrap` creates the URP pipeline (depth texture on); Built-in→URP
  material upgrade runs; the **Full Screen Pass feature auto-wires** into
  `URP-Renderer.asset` (injection `BeforeRenderingPostProcessing`, requirements
  `Depth`, `fetchColorBuffer` on, material `DXRForegroundClip`).
- ✅ Driver installs at scene load; `_DXRForegroundFar` published every frame with
  **`enable=1`** and both per-eye fars recovered as real values
  (`farL=farR=11.41 m`, i.e. `eye.z`, not the 1000 m render override) — confirms
  `foregroundOnlyClip` is active and the `m23/(m22+1)` recovery works for both eyes.
- ✅ Clean run, **no exceptions** (fixed the legacy-`Input` crash — this project is
  Input System only; the C-toggle now uses `Keyboard.current`).

Δmm reads **0.0 on-axis** with no face at the eye-tracker (head centered →
each eye sees the plane at the same `eye.z` — correct baseline, not a bug).
**Still needs a human at the hardware:** the off-axis Δ growth, the visual stereo
result (tiger clips on both eyes, no residue), and the C-toggle visual. Enable the
log for those with `DXR_FGCLIP_DIAG=1` (env) or the driver's `diagnosticLog` field.

## Validation checklist

- [ ] Tiger clips at the display plane on **both** eyes (view it stereo / move head off-axis).
- [ ] No color residue behind the plane (full transparent cut, not just alpha).
- [ ] Toggling C cleanly shows/hides the behind-plane geometry.
- [ ] Click-through silhouette (multi-object mask) still aligns with the clipped image.
- [ ] Δmm in the diagnostic grows as the viewer moves off-axis (confirms per-eye fars differ).

## Files

| File | Role |
|------|------|
| `Packages/manifest.json` | adds `com.unity.render-pipelines.universal` 17.0.4 |
| `Assets/Editor/URPSetupBootstrap.cs` | creates/wires URP assets; forces depth texture on |
| `Assets/DisplayXRForegroundClipURP.shader` | per-eye depth clip fullscreen pass |
| `Assets/ForegroundClipURPDriver.cs` | publishes `[leftFar,rightFar]`; toggles; diag |
| `Assets/Editor/ForegroundClipURPInstaller.cs` | `DisplayXR → Setup URP Foreground Clip` menu |

## Known prototype limitations (fixed when promoted to the plugin)

- Per-eye selection relies on **multipass** (`unity_StereoEyeIndex`); the plugin
  forces multipass on the Kooima path, so this holds here.
- Reads `far_eff` from the matrices, which requires the rig's `foregroundOnlyClip`
  to be on — `ClipAtDisplayPlane` (in `TransparentAutoSetup`) already does that.
- Clips opaque geometry (depth-writing). The tiger/cube are opaque, so fine.
