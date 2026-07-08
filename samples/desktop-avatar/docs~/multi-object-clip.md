# Multiple clickable objects in the transparent overlay

How to go from the single-tiger transparent demo to **multiple 3D objects**
(tiger + cube + …), all visible through the click-through overlay and each
independently clickable / drag-rotatable.

## Why a new object gets "clipped"

The transparent overlay's visible **and** clickable region is the **union of
the per-eye silhouettes of the renderers in
`DisplayXRTransparentOverlay.clickableRenderers`**. The plugin feeds that union
to Win32 `SetWindowRgn`, which is a hard window-shape clip — outside the region
the OS treats the window as if it didn't exist (desktop shows through, clicks
pass through).

So an object that is **not** in `clickableRenderers` is cut away (invisible
*and* unclickable) wherever it falls outside the listed objects' silhouettes.
This is *not* alpha/transparency clipping — it's the window-region mask. The fix
is simply to add the object to the list.

> No plugin change is ever needed: the mask already unions however many
> renderers you list. This is a test-app (C# + scene) change only.

## Steps

### 1. Put the object in the scene

- Make it **active** and ensure it has a `Renderer`
  (`MeshRenderer` for a cube, `SkinnedMeshRenderer` for an FBX).
- Position it within the **display-plane clip range** — at or in front of the
  display plane, near the tiger. `ClipAtDisplayPlane` culls all geometry past
  the plane per-eye, so an object pushed too far back disappears regardless of
  the mask.

### 2. Add its GameObject name to the target list

In `Assets/TransparentAutoSetup.cs`:

```csharp
string[] k_TargetNames =
{
    "cartoon tiger in witches hat_ rigged and animated",
    "Cube",   // <- add each new object's exact GameObject name here
};
```

Rebuild (`unity_build.bat`) and run. That's the whole change.

## What the setup script does for every name in the list (automatically)

- Collects each object's `Renderer` into `clickableRenderers` → its silhouette
  joins the window mask (visible + clickable).
- Ensures a collider for the raycast: skinned meshes use the plugin's per-frame
  baked collider; plain meshes get an auto `BoxCollider`
  (`AutoBoxColliderFromRenderer`).
- Adds a `DragRotateCube` so each object is independently left-drag-rotatable.
  Instances don't conflict — each only responds to its own renderer under the
  pointer (`OnDown` gates on `r != target`).

## Gotchas

- The name must match the **GameObject name exactly** (`GameObject.Find`), and
  the object must be **active** — `Find` skips inactive objects. (This is why
  the bundled `Cube` had to be flipped to `m_IsActive: 1`.)
- All listed objects are clipped at the display plane together (per-view, all
  geometry) — that's intentional; keep them in the foreground range.

## Reference

- Plugin mask implementation: `Runtime/DisplayXRTransparentOverlay.cs`
  (silhouette render → `displayxr_set_overlay_hit_mask` → `SetWindowRgn` in
  `native~/displayxr_win32.c`) in
  [`DisplayXR/displayxr-unity`](https://github.com/DisplayXR/displayxr-unity).
- Setup script: `Assets/TransparentAutoSetup.cs`.
