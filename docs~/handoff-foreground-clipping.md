# Handoff: foreground-only render (test-transparent#2)

GitHub: [DisplayXR/displayxr-unity-test-transparent#2 — Render only foreground content (in front of virtual display plane)](https://github.com/DisplayXR/displayxr-unity-test-transparent/issues/2)

## Goal

In transparent-overlay mode, render only the foreground portion of the scene — geometry between the eye and the virtual display plane (popping-out content). Geometry past the display plane (sunk-in content) should be visually clipped.

## Constraints

- **No plugin changes**, if possible. The user wants this resolved test-project-only.
- **Hit detection on the tiger must keep working.** The plugin's per-triangle SMR ray-tri uses `Camera.farClipPlane` as its initial `bestT`. Non-skinned clickables fall back to `Physics.Raycast(ray, info, Camera.farClipPlane)`. Anything that mutates `Camera.farClipPlane` must not break these.
- **The tiger straddles the display plane.** Its world-Z bounds are roughly `[-0.58, +0.74]`; with the display at world Z=0 and the eye at world Z≈0.6 looking at -Z, the tiger has triangles on both sides of the display plane. So the desired behavior involves **pixel-level** clipping (the front of the tiger fades to nothing at the plane). Bounds-level culling can't deliver this for a straddling renderer.

## Why the geometry is what it is

For `DisplayXRDisplay` rig:
- The parent transform IS the virtual display surface (per `DisplayXRDisplay.cs` class doc).
- The runtime's `xrLocateViews` returns eye poses in display-relative coords. `DisplayXRFeature.LeftEyePosition.z` / `RightEyePosition.z` are the raw values (display-relative), **stable** regardless of where the camera GameObject sits in world. For the typical Leia setup, eye.z is ~0.6 m.
- The eye looks at -Z (OpenXR convention). So the display plane is at eye-distance = `|eye.z|` in the eye-forward direction.

When the user presses W to push the camera, the world-Z eye position grows. But the camera-relative eye Z (= raw `LeftEyePosition.z`) stays at ~0.6 — it's how far the viewer's head is from the (also-moving) display.

So **for clipping at the display plane**: the far plane in eye coordinates must be `|raw eye.z|`. That's the number to clip at.

## Failed approaches (don't repeat)

1. **`Camera.farClipPlane = display-plane distance`** (computed from rig — for `DisplayXRDisplay`, `Vector3.Dot(parent.pos - cam.pos, cam.forward)`; for `DisplayXRCamera`, `1f / invConvergenceDistance`). Broke hit detection because the plugin's `Physics.Raycast` for non-skinned clickables uses `Camera.farClipPlane` as the ray max distance.
2. **`Camera.cullingMatrix` only**, multiple flavors:
   - From `cam.worldToCameraMatrix` × perspective: bounds-level, tiger straddles → not culled.
   - From per-eye view × perspective at world-eye-Z (`view.inverse.GetColumn(3)` extracts world-eye position which moves with camera): still bounds-level; tiger straddles whatever far we pick.
   - From per-eye view × perspective at raw eye.z: cull frustum geometrically correct, but Unity's auto-derived projection-based cull (from `Camera.farClipPlane`) seems to apply in parallel and drop geometry Kooima needs.
3. **`Camera.farClipPlane = raw eye.z` + permissive `Ortho` cullingMatrix**: tiger went **fully invisible**. Trace: Unity's transform.forward (+Z) projection at `farClipPlane=0.6` sees world Z `[+0.3, +0.6]`; Kooima at eye-forward (-Z) needs world Z `[-0.02, +0.28]`. Those don't overlap. Either Unity culled what Kooima needed, or Kooima clipped what Unity sent.
4. **`Camera.farClipPlane = raw eye.z` + per-eye cullingMatrix built from runtime eye view**: same outcome as (3) — tiger invisible.
5. **`SetStereoProjectionMatrix` per eye, rewriting `m22`/`m23` for new far while keeping near** (this is what's in the code right now): tiger renders ✓ but the user reports clipping doesn't visibly happen. Hit detection unaffected because `Camera.farClipPlane` is untouched. Suspected: the runtime overwrites our per-eye projection between `onPreCull` (where we set it) and the actual render submission.

## Current state — `ClipAtDisplayPlane`

Located near the bottom of `Assets/TransparentAutoSetup.cs`. Key points:

- Hooks `Camera.onPreCull` (static event, filters to its own camera). Computes `dist = |LeftEyePosition.z|` averaged with right eye.
- Calls `SetStereoProjectionMatrix(Left)` and `SetStereoProjectionMatrix(Right)` with a matrix derived from `GetStereoProjectionMatrix(eye)` by rewriting just `m22` / `m23`:
  ```csharp
  float n = proj.m23 / (proj.m22 - 1f);    // recover near from existing matrix
  proj.m22 = -(newFar + n) / (newFar - n);
  proj.m23 = -2f * newFar * n / (newFar - n);
  ```
- `Camera.farClipPlane` is NOT modified.
- `Camera.cullingMatrix` is NOT modified (relies on Unity's auto-derive from the modified stereo proj).
- `OnDisable` calls `cam.ResetStereoProjectionMatrices()` + `cam.ResetCullingMatrix()`.

## Next-session investigation steps

Try these in order; stop when something works.

### 1. Confirm the override actually reaches render

Hook `Camera.onPreRender` (fires after `onPreCull`, immediately before render submit). Inside, log `cam.GetStereoProjectionMatrix(eye).m22 / m23`. If the values revert to the runtime's defaults between `onPreCull` and `onPreRender`, the OpenXR XR plugin or the runtime is overwriting us — try setting in `onPreRender` instead, or set via a `CommandBuffer` attached to the camera.

### 2. Trace the tunables push path

`DisplayXRDisplay.LateUpdate` (and `DisplayXRCamera.LateUpdate`) push `tunables.farZ = m_Camera.farClipPlane` to native. The native `xrLocateViews` hook reads `tunables` to construct the Kooima projection. Read `native~/displayxr_kooima.cpp` and `native~/displayxr_hooks.cpp` to confirm — if `farZ` is the only knob the runtime uses for the depth clipping plane, then `Camera.farClipPlane` is the only test-project-side lever and we're stuck against constraint (2) above. That outcome → plugin-change-required.

### 3. Try `onPreRender` injection instead of `onPreCull`

If (1) shows the projection reverts between callbacks, `Camera.SetStereoProjectionMatrix` may need to happen later in the frame. `onPreRender` is the last per-camera callback before the actual draw.

### 4. Shader-level discard (test-project-only escape hatch)

If projection-matrix overrides won't stick, use `Camera.SetReplacementShader` with a custom shader that does `clip(IN.worldPos.z - displayZ)` (or whatever sign matches the rig). All scene materials get replaced for the camera's render pass. **BiRP only** — would not work in URP without rewriting against ScriptableRenderFeature.

### 5. Coarse per-renderer disable (compromise)

Walk all `Renderer`s each `LateUpdate`, disable those whose `bounds` are *entirely* past the display plane. Cheap and reliable but won't clip a straddling tiger. Acceptable if the demo's intent is "kill clearly-behind-display backgrounds" rather than pixel-perfect clipping.

### 6. Plugin change (if user authorizes)

Add a `tunables.foregroundOnly` boolean. The native Kooima projection clips at exactly the display plane in eye coords when set. Cleanest, but breaks the no-plugin-changes constraint — only attempt if the user explicitly opens that door.

## Critical files

- `Assets/TransparentAutoSetup.cs` (`ClipAtDisplayPlane` class) — the current attempt.
- `<plugin>/Runtime/DisplayXRTransparentOverlay.cs` — where the hit-test reads `farClipPlane` (the constraint).
- `<plugin>/Runtime/DisplayXRDisplay.cs` + `DisplayXRCamera.cs` — `LateUpdate` pushes `tunables.farZ` to native.
- `<plugin>/native~/displayxr_kooima.cpp` + `displayxr_hooks.cpp` — where `xrLocateViews` constructs the Kooima projection. Read before assuming where to inject.

## Verification once it works

1. Place a Quad behind the display plane (in eye-distance terms: further from the eye than `raw eye.z`) — should NOT render.
2. Move the Quad in front of the display plane (closer to the eye than `raw eye.z`) — should render.
3. Tiger remains fully clickable + draggable (left and right), animates normally.
4. Clickthrough still passes through transparent gaps.
5. W / S still pushes camera in/out; A / Q / D / E still no-ops.

## Session log

| Date | Outcome |
|------|---------|
| 2026-05-10 | Approaches 1–5 above tried; current state is approach 5 (stereo projection override) — tiger renders but doesn't visibly clip. Suspected runtime overwrite between `onPreCull` and render. Plan: start next session with investigation step 1 (confirm the override reaches render). |
