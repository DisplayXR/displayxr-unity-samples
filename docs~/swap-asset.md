# Swapping the Tiger for Your Own Asset

Practical guide to replacing the Mixamo tiger in `CubeTest.unity` with a
different model while keeping the **transparent overlay + per-pixel
click-through** behavior intact.

The transparent overlay's click-through depends on a per-pixel silhouette
mask the plugin renders from `DisplayXRTransparentOverlay.clickableRenderers`
each frame. If your asset isn't wired through that array — with the right
import settings, materials, and collider setup — the silhouette doesn't
match the visible model and you'll see one of two failure modes:

| Symptom | Cause |
|---|---|
| Clicks "around" the asset land on the asset (overlay catches dead air) | Asset's silhouette is smaller than its bounding box and the clickable renderer is missing / mis-wired → falls back to AABB region |
| Clicks "on" the asset fall through to the desktop | Silhouette mask comes back empty → no region → window catches nothing |
| Foreground parts (hands, paws) get clipped at the edges | Asset has high disparity not covered by the per-eye union (rare on 2-view hardware now that plugin v1.7.0 unions both eyes) |
| Asset renders with a colored rectangular background | Material isn't writing alpha=0 to transparent pixels |

This guide walks the swap end-to-end and calls out each of these traps.

## TL;DR

1. Import the new asset under `Assets/` (FBX, GLB, prefab — anything Unity
   handles).
2. Drop it into `Assets/CubeTest.unity` at scene root, disable the tiger.
3. Edit one line in `Assets/TransparentAutoSetup.cs`:
   ```csharp
   const string k_TargetName = "your-new-asset-root-name";
   ```
4. Verify the asset has **Read/Write Enabled** in its import settings
   (Inspector → Model tab) so `BakeMesh` works for the per-triangle
   raycast. For multi-renderer assets, see [Multi-renderer assets](#multi-renderer-assets) below.
5. Build standalone, run, click in the gaps between limbs to confirm the
   desktop receives the click. Open Notepad behind the asset to test
   click-through.

If anything misbehaves, work through [Common pitfalls](#common-pitfalls) below.

## What the auto-setup wires

`Assets/TransparentAutoSetup.cs` runs at `AfterSceneLoad` and does:

1. Finds the target by name (`k_TargetName`).
2. Picks the **first** `Renderer` (or `SkinnedMeshRenderer`) under that
   root (`GetComponentInChildren<Renderer>`).
3. Adds it to every DisplayXR rig camera's `DisplayXRTransparentOverlay.clickableRenderers` array.
4. For plain `MeshRenderer`: attaches `AutoBoxColliderFromRenderer` if no
   collider exists.
5. For `SkinnedMeshRenderer`: does **not** add a collider — the plugin
   manages a per-frame `BakeMesh` + per-triangle raycast itself.
6. Adds `DragRotateCube` to the asset root so left-drag rotates the
   whole hierarchy.
7. Adds `ClipAtDisplayPlane` + `LockToForwardAxis` per rig (foreground-clip
   + WASD constraints, see existing repo docs).

**The single line you almost always need to change is the target name.**
Everything else is wired through that one entry point.

## Step-by-step

### 1. Drop the asset into the scene

- Import the asset under `Assets/`. For FBX, leave default importer
  settings but check the **Model** tab:
  - **Read/Write** = ON (required for `BakeMesh` on skinned meshes; harmless on static).
  - **Mesh Compression** = Off (or Low) for accurate silhouette edges.
  - If animated, **Rig** → Animation Type = Generic / Humanoid as
    appropriate. The existing tiger uses Generic.
- Open `Assets/CubeTest.unity`.
- Drag the asset prefab (or FBX) into the **scene root** (top-level, not
  nested under another GameObject).
- Disable the existing tiger (`cartoon tiger in witches hat_ rigged and
  animated`) so both don't render at once. The cube and tiger are kept
  in the scene for fallback testing.
- Position / scale the asset so its visible silhouette fits in the
  display's stereo comfort zone (roughly within the display plane;
  large positive Z = behind the screen, large negative Z = popped
  forward, expensive on the eyes).

### 2. Edit the target name

Open `Assets/TransparentAutoSetup.cs` and change:

```csharp
const string k_TargetName = "cartoon tiger in witches hat_ rigged and animated";
```

to whatever the **root GameObject name** of your new asset is. The
match is exact (case-sensitive). If your asset is named
`MyCharacter` in the hierarchy, set it to `"MyCharacter"`.

> **Why not auto-detect?** The auto-setup script is intentionally
> simple. Hardcoding the name keeps the scene contract explicit and
> debuggable — if the lookup fails, the script logs a clear "no
> '<name>' found" warning so you know exactly what to fix.

### 3. Verify materials write alpha correctly

The transparent overlay relies on the camera output having **alpha=1
on opaque pixels and alpha=0 on the rest**. This happens automatically
for almost every standard shader (Built-in Standard, URP/Lit, Unlit
variants) when the camera's clear color is `(0, 0, 0, 0)` — which
`DisplayXRTransparentOverlay` sets up for you.

Where things go wrong:

- **Custom shaders** that explicitly set `o.Alpha = 1` everywhere or
  use `BlendOp Add` with hardcoded alpha will produce alpha=1 in the
  full screen rect. You'll see a colored rectangle, not just the
  silhouette.
- **Skybox / Procedural sky** in the camera clear flags. Must be
  `Solid Color` with `(0,0,0,0)`. The auto-setup sets this on every
  DisplayXR rig camera, but if your scene has other cameras that
  render after the rig, they can stomp the alpha back to 1.
- **Post-processing** that writes opaque output (bloom, tonemap)
  often forces alpha=1 in the final blit. URP's render features
  in particular often need explicit alpha handling.

**Quick verification:** in Play mode in the editor, set the Game
View background to something visually obvious (e.g., bright magenta
via Game View → Stats area, or render to a RenderTexture and inspect
in a preview). If you see your asset on a black/colored *rectangle*,
the alpha isn't 0 outside the silhouette → fix the material/post
chain first. If you see the asset on a checkerboard-like transparent
preview, you're good.

### 4. Check animator culling (skinned mesh only)

If your asset is a `SkinnedMeshRenderer` with an Animator driving
bones, the Animator's culling mode matters for two reasons:

- The plugin calls `BakeMesh` per-frame to get the current animated
  silhouette for the per-pixel mask and the per-triangle raycast.
  If the Animator stops updating bones because Unity thinks the
  renderer is offscreen (which it isn't — it's always "onscreen"
  from the user's POV but Unity's culling math is based on Camera
  frustum vs. world bounds), the baked silhouette and the visible
  silhouette diverge.
- The plugin sets `SkinnedMeshRenderer.updateWhenOffscreen = true`
  and `Animator.cullingMode = AlwaysAnimate` **automatically** the
  first time it touches the SMR via `UpdateBakedHitColliders`.

> **You usually don't need to do anything here.** The plugin handles
> it. But if you see "stale pose" symptoms (clicks land on
> last-frame's silhouette, hands wobble outside the mask), confirm
> the Animator's `cullingMode` is `AlwaysAnimate` in the Inspector.

### 5. Multi-renderer assets

The default auto-setup wires only the **first** renderer it finds
(`GetComponentInChildren<Renderer>(true)`). For assets with multiple
renderable parts — character + accessories, head + body + clothing
— you need every visible mesh in `clickableRenderers` so the
silhouette mask covers all of them.

Change line ~48 of `TransparentAutoSetup.cs` from:

```csharp
hit = new[] { targetRenderer };
```

to:

```csharp
hit = targetRoot.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy)
                .ToArray();
```

(Requires `using System.Linq;` at the top of the file.)

This includes every active, enabled renderer under the target root.
Each one contributes to the per-pixel silhouette mask and gets
queried by the per-triangle raycast.

**Caveats for multi-renderer setups:**

- Each `SkinnedMeshRenderer` adds one `BakeMesh` per frame (~1–3 ms
  for a typical character mesh). Adding 8 renderers on one rig =
  8–24 ms/frame, which can eat your frame budget.
- For `MeshRenderer` children, `AutoBoxColliderFromRenderer` is
  only added by the auto-setup for the **target root**. If a
  multi-renderer child has no collider and uses the AABB-fallback
  raycast path, clicks may miss. Add a `BoxCollider` (or
  `MeshCollider`) manually on each `MeshRenderer` child.
- Disabled children are excluded by the filter above — keep that
  if you toggle visibility at runtime, drop it if you want every
  child wired regardless of state.

### 6. Build and test

```
File → Build Settings → Build (Windows Standalone, x86_64)
```

Output the build folder somewhere outside `Assets/`. Run the `.exe`
on a Leia SR machine with the DisplayXR runtime installed.

## Verification checklist

After running the standalone build:

- [ ] Asset renders **without a rectangular background**. Anti-aliased
      edges blend cleanly into the desktop.
- [ ] Open Notepad behind the asset; click in **transparent regions
      around** the asset → Notepad activates and takes input.
- [ ] Click in **concavities of the asset** (gaps between limbs, hat
      tips, anywhere the silhouette is non-convex) → desktop window
      underneath takes the click.
- [ ] Hover effects fire on background windows when the cursor is in
      transparent areas (button highlights, taskbar previews).
- [ ] Cursor adapts to background windows' hit-test zones (e.g.,
      changes to resize cursor on a background window's edge).
- [ ] Click **on** the asset's visible silhouette → `onPointerClick`
      logs to the console with the renderer name.
- [ ] Stereo: the asset pops in 3D convincingly. **Foreground parts
      (hands extended toward camera) are not clipped** at the
      silhouette boundary.
- [ ] Drag-rotate works: left-click on the asset, drag horizontally
      → the asset yaws.
- [ ] `displayxr.log` (next to the `.exe`) shows `[DisplayXR]
      hit_mask: mask=NxM rects=K dst=WxH` lines once per second — the
      per-pixel silhouette mask is active. If you see only
      `hit_region:` lines, the silhouette path didn't engage and
      you're using the AABB fallback.

## Common pitfalls

### "The whole rectangular bounding box catches clicks, not the silhouette"

The asset's `clickableRenderers` wireup didn't take. Confirm:

- The asset's root GameObject name matches `k_TargetName` exactly
  (case-sensitive).
- The asset is **active** in the scene at `AfterSceneLoad` time.
  Auto-setup runs once and only catches active GameObjects.
- The first `Renderer` found under the root is the visible one.
  If your asset has an invisible LOD0 renderer as the first child
  and the visible mesh deeper down, the auto-setup picks the
  invisible one. Reorder the hierarchy or extend to multi-renderer
  (see [Multi-renderer assets](#multi-renderer-assets)).

### "Asset renders on a colored rectangle"

Materials or post-processing forcing alpha=1 in the camera output.
See [Step 3](#3-verify-materials-write-alpha-correctly).

### "Asset disappears entirely after first frame"

The silhouette mask came back empty → `SetWindowRgn` clipped the
window to 0×0. Almost always:

- The asset has no enabled `Renderer` at the point the auto-setup
  ran (loaded async after scene load, or a script disables it
  immediately).
- The renderer is on a layer the camera doesn't render. The
  silhouette pass uses the same cameras, so anything the rig
  doesn't render is invisible to the mask.

### "Foreground parts get clipped at the silhouette edge"

Should not happen with plugin v1.7.0 on 2-view hardware (the
per-eye union covers stereo disparity). If it does:

- Confirm you're running plugin v1.7.0 or later (check
  `Packages/packages-lock.json` for `hash:
  3c314d9358fa23fe493147d50cd186655cfe5505` or newer).
- For N-view quilt displays (4+ views) the union is currently
  only left+right; high-disparity geometry can extend beyond.
  Tracked in the plugin repo — file an issue against
  [DisplayXR/displayxr-unity](https://github.com/DisplayXR/displayxr-unity/issues).

### "Asset's animated pose visibly lags the silhouette"

Animator culling is dropping bone updates. Plugin should
auto-fix on first touch; if it doesn't, set
`Animator.cullingMode = AlwaysAnimate` and
`SkinnedMeshRenderer.updateWhenOffscreen = true` manually in
the Inspector and rebuild.

### "Clicks on the asset get eaten but don't fire onPointerClick"

The asset is in `clickableRenderers` and the silhouette covers
the click pixel, but the per-triangle raycast doesn't intersect
the visible mesh. Common causes:

- The asset's mesh has Read/Write disabled in import settings —
  `BakeMesh` returns an empty mesh. Toggle Read/Write on and
  reimport.
- For `MeshRenderer` (non-skinned) children that lack a
  `BoxCollider` / `MeshCollider` and aren't the auto-wired root —
  add a collider manually.

### "Performance drops"

Each `SkinnedMeshRenderer` in `clickableRenderers` triggers a
`BakeMesh` per frame (1–3 ms on typical character meshes). Total
silhouette render cost = ~0.5 ms (RT clear + 2 small DrawRenderer
passes + dilation Blit). The `SetWindowRgn` itself is cheap (a
few hundred microseconds for typical region complexity).

If you're animating many separate skinned meshes, reduce the
`clickableRenderers` array to the single most-visible mesh (the
one users actually click on) — the other meshes still render
normally, they just don't contribute to the silhouette / raycast.

## Architecture references

- Plugin docs:
  [`docs~/architecture/preview-session.md`](https://github.com/DisplayXR/displayxr-unity/blob/main/docs~/architecture/preview-session.md),
  [`docs~/architecture/hook-chain.md`](https://github.com/DisplayXR/displayxr-unity/blob/main/docs~/architecture/hook-chain.md).
- Per-pixel silhouette mask: `Runtime/DisplayXRTransparentOverlay.cs`
  in the plugin repo —
  `RenderHitMaskAndRequestReadback`, `AppendSilhouettePass`,
  `OnHitMaskReadback`.
- Native region application:
  `native~/displayxr_win32.c::displayxr_set_overlay_hit_mask`.
- Issue history:
  [DisplayXR/displayxr-unity#57](https://github.com/DisplayXR/displayxr-unity/issues/57)
  (the transparent overlay umbrella issue + click-through evolution).
