# Multi-Image Projection — implementation plan

Requirements: [docs/MultiImageProjection.md](../docs/MultiImageProjection.md).
Target: project an **ordered stack of up to a few dozen same-instrument images**
onto OPC surfaces, painter's order (top wins), with a library/stack UI, hover
preview, hover footprints (surface outline + frustum wireframe), fly-to, and
curated metadata — on all platforms including macOS (GL 4.1, no SSBOs).

## Why this is an extension, not a rewrite

The single-image pipeline already contains every building block:

- **N projector matrices per patch already flow to the GPU.**
  `localImageProjections` (`src/PRo3D.GIS/ImageProjection.fs:115`) loops over
  `ProjectedImagesLocalTrafos` composed *per patch* in
  `ImageProjectionOpcExtensions.projectionUniformMap`
  (`src/PRo3D.Core/Surface/ImageProjectionOpcRendering.fs:15`). It only
  *counts* coverage — it never samples textures — and it uses a storage
  buffer, which is why it is gated off on macOS
  (`Config.limitedShaderCapabilities`, `src/PRo3D.Viewer/Program.fs:106`).
- **Single-image sampling already works** in `stableImageProjection`
  (`ImageProjection.fs:52`): NDC coverage test, back-face rejection via the
  geometric normal (`generateNormal`), colormap remap, border.
- **Per-image projector cameras are already abstracted**:
  `InstrumentObservation.projectorCamera` / `ResolvedImage`
  (`src/PRo3D.GIS/InstrumentObservation.fs`).
- **Footprint outline and animated fly-to have working precedents**:
  `footPrintF` (`src/PRo3D.Base/Utilities.fs:640`) and
  `createAnimation`/`addFlyToSurfaceAnimation`
  (`src/PRo3D.Viewer/Viewer/Viewer.fs:206,459`).

What's genuinely new: a texture array, a bounded-uniform-array replacement for
the storage buffer, stack/order state with stable ids, the hover-preview
plumbing, and the restructured UI.

## Design decisions

### D1 — GPU interface: `sampler2DArray` + fixed-size uniform matrix array

- `MAX_PROJECTED_IMAGES = 32` (compile-time constant). 32 × M44f = 2 KB — far
  under the 16 KB UBO minimum, works on GL 4.1/macOS. The storage buffer is
  **not** used; FShade emits SSBOs unconditionally (verified in FShade 5.7.9
  `Assembler.fs`) and macOS never got GL 4.3.
- One `sampler2dArray` (`ProjectedTextures`), layer *i* = stack entry *i*
  (bottom → top). Same instrument ⇒ same dimensions ⇒ array-compatible.
  `sampler2DArray` is core since GL 3.0.
- Uniform arrays (all `Arr<N32, _>` in FShade, filled per patch like today's
  storage buffer): projector matrices (`vp_i * modelTrafo * Local2Global`),
  per-layer `V2f` min/max; plus `ProjectedImagesCount : int`.
- Fragment loop **top-down** (`i = count-1 .. 0`): first layer that covers the
  fragment with a projector-facing normal wins → painter's "last wins" without
  any blending, and early-out keeps the common case cheap.
- **Decided: no storage buffer anywhere, no platform split.** The alternative —
  keep single-image projection on macOS and use SSBOs for multi-image
  elsewhere — was considered and rejected: the SSBO buys nothing at this scale
  (2–4 KB of matrices vs. unbounded/GPU-writable data, which is what SSBOs are
  for), while the split costs two shaders, two Sg wirings, a degraded Mac mode
  for the whole stack UI (ordering/hover-preview are meaningless with one
  image), and a spreading `limitedShaderCapabilities` flag. Uniform arrays are
  the older, better-trodden feature; the one real unknown — whether an
  `Arr<N32, M44f>` uniform flows through the per-patch uniform map into
  FShade's uniform block (the current array path hands over an `IBuffer`) —
  is platform-independent and was retired **first**, before any viewer
  integration — see below. This mirrors how `PackedRendering` solved the same
  problem: one no-SSBO path (`linesNoIndirect`), dead SSBO variant.

**D1 risk retired (verified in the Aardvark.Rendering sources, not assumed).**
`UniformWriters.tryGetWriter` matches a UBO field of kind `Array(len, elem,
stride)` against *three* source types — `ArrayOf` (a plain `'a[]`), `ArrOf`
(`Arr<N<n>,'a>`) and `SeqOf`
(`src/Aardvark.Rendering/Uniforms/UniformWriters.fs:708-735`). So the CPU side
does **not** have to build an `Arr`: the existing `aval<M44f[]>` that
`projectionUniformMap` already produces binds straight to an
`Arr<N<32>, M44f>`-declared uniform. `ArrayWriter.Write` (`:413`) does
`min value.Length count`, i.e. a **short array is written and the tail
zero-filled**, and an over-long one is truncated at the cap — exactly the
semantics the stack needs, with `ProjectedImagesCount` bounding the shader loop.
Precedent under test: `Tests/Rendering/Uniforms.fs:34,158` renders a shader
declaring `Arr<N<2>, V2f>` fed by a plain `V2f[]` via `Sg.uniform'`, as a
backend rendering test on GL *and* Vulkan; `Aardvark.GPGPU/Jpeg.fs:767` ships an
`Arr<64 N, V4i>` uniform in production.

Consequences for the implementation:
- the change to `ImageProjection.fs` is dropping `?StorageBuffer?` from the
  declaration and giving it the `Arr<N<32>, M44f>` type — the uniform *name*
  and the whole CPU feed stay as they are;
- note the type is spelled `Arr<N<32>, M44f>` (or `Arr<32 N, _>`), not `N32`;
- `V2f` array elements are padded to 16 B by std140, so the per-layer min/max
  array costs 512 B rather than 256 B. Irrelevant against the 16 KB floor, and
  `ArrayWriter` takes the stride from the reflected layout, so it is correct
  without any CPU-side packing.

The remaining unknown is only that PRo3D pins Aardvark.Rendering **5.6.9** while
the verified sources are the 5.7.0 checkout; the writer dispatch is
long-standing, and the `TestViewer` run confirms it on the pinned version.
- `localImageProjections` (coverage-count debug view) is ported to the same
  arrays; the `limitedShaderCapabilities` gate disappears rather than spreads.
  Precedent for routing around SSBOs already exists:
  `PackedRendering.linesNoIndirect` is the live annotation path exactly because
  the SSBO variant (`lines__`) can't run on macOS.

### D2 — per-image display settings stay in the shader, colormap is global

Channel selection determines *which band gets uploaded* to a layer (upload is
per-image anyway). Min/max go into the per-layer uniform array so the sliders
stay live without re-uploads. The colormap/false-color toggle becomes **global
for the stack** (same instrument; one legend). Per-image false-color previews
in the edit panel are unaffected.

### D3 — model: stable ids, library order ≠ stack order

`ProjectedImageListModel` (`src/PRo3D.Core/ProjectedImageList-Model.fs:90`)
identifies images by `Index` into an `IndexList` that sorting **rewrites
destructively** — unusable as a stack reference. Changes:

```fsharp
ProjectedImageModel += id : Guid                      // stable identity
ProjectedImageListModel:
    images        : IndexList<ProjectedImageModel>    // library, sort/filter freely
    stack         : IndexList<Guid>                   // draw order, bottom → top
    hoveredImage  : Option<Guid>                      // library OR stack hover
    selectedImage : Option<Guid>                      // edit-panel target (was Option<Index>)
```

`IndexList<Guid>` for the stack: order is the point, N is tiny, and per-element
adaptive tracking matches how the Sg consumes it. Sorting then only permutes
`images` and stops remapping `selectedImage`/`editImages`
(`ProjectedImageListApp.fs:88-144` simplifies). New actions:
`AddToStack of Guid`, `RemoveFromStack of Guid`, `MoveInStack of Guid * int`,
`HoverImage of Option<Guid>`, `FlyToImage of Guid`.

### D4 — hover preview = "stack plus one"

The adaptive layer that feeds the GPU composes
`effectiveStack = stack ++ (hovered, if not already in stack)`. Hovering a
library image therefore previews it *on top* with zero special cases in the
shader; hovering an image already in the stack changes nothing in the GPU data
and instead drives the footprint highlight (D5) and the UI badge. Capped at
`MAX_PROJECTED_IMAGES` (stack full + hover ⇒ preview temporarily replaces the
top; log/hint in UI).

### D5 — footprints: one hovered image, two representations

- **Surface outline**: single uniform (`HoveredProjectionTrafo` + valid flag),
  border logic lifted from `footPrintF` — drawn for the hovered image only, so
  no per-layer border work in the main loop (and the green border currently
  hard-baked into `stableImageProjection` goes away).
- **Frustum wireframe**: line Sg of the 8 NDC-cube corners transformed by
  `(view * proj)⁻¹` from the image's `ProjectorCamera`, drawn in the surface's
  reference frame. Appears/disappears with `hoveredImage`.

### D6 — fly-to

`FlyToImage` is handled at the Viewer level (same pattern as
`SurfaceAppAction.FlyToSurface`, `Viewer.fs:650` → `addFlyToSurfaceAnimation`):
place the camera on the projector axis at a distance framing the frustum
footprint, forward = boresight, up from the projector view — then push a
`createAnimation` camera animation. Footprint center: boresight ∩ surface via
the existing picking/KdTree intersection, falling back to `mbi.targetPos`
distance along the boresight when no hit.

### D7 — textures: cache + async upload

Today the whole multi-band TIFF is re-read synchronously on every selection
change (`src/PRo3D.GIS/Visualization.fs:38-62`). With dozens of layers:

- Decode cache keyed `(path, channel)` (LRU, size-bounded); decode off the UI
  thread; checkerboard layer until ready.
- Budget: AFC 1024² × R32f = 4 MB/layer → 128 MB at the 32-image cap.
  Acceptable; the cap and eviction make it bounded.
- The texture array reallocates only when the *set* of layers grows past its
  current capacity; reorder = updating the matrix/minmax arrays, not re-upload.

### D8 — projector matrices computed once per image

SPICE is single-threaded (`spiceCallLock`,
`src/PRo3D.Base/InstrumentProjection.fs:96`) and matrices currently recompute
inside `AVal.custom` on every observation change
(`ProjectedImagesListHelpers.fs:119-142`). Each image's projector depends only
on its own obs time + projection method + boresight adjustment ⇒ memoize per
`(imageId, method, boresight)`.

### Also fixed on the way

- Body uniform hardcoded to `"MARS"` (`src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:1145`).
- `stableImageProjection` + single `ProjectedTexture` path is **subsumed**: a
  stack of one behaves identically (minus the green border). The old shader and
  `imageProjection : aval<Option<Trafo3d>>` field are removed at the end of
  Phase 2, not kept in parallel.

## Phases

### Phase 1 — model + adaptive plumbing
`ProjectedImageList-Model.fs` changes (D3) + `adapt.cmd`; stack actions in
`ProjectedImageListApp.update`; `getProjectedImageData`
(`ProjectedImagesListHelpers.fs:88`) produces
`aval<array<Trafo3d> * array<V2f> * count>` from `effectiveStack` (D4, D8).
Existing single-image rendering still consumes the top entry — app stays
functional mid-way.

### Phase 2 — shader + texture array
New `stableImageProjectionStack` fragment shader (D1, D2); texture-array
building with cache (D7); `projectionUniformMap` fills the bounded arrays;
effect stack swap in `Viewer-Utils.fs:958-1007`; remove single-image path; fix
`"MARS"`. Validate on the `TestViewer`/`ProjectionTestbed` scenes (the
constant-array binding in `TestViewer.fs:270` is the existing N-projector
prototype). **Last step of the phase:** port `localImageProjections`
(`RelativeCount` coverage view) off its storage buffer onto the same arrays and
drop the `limitedShaderCapabilities` gate.

### Phase 3 — footprints + fly-to
Hovered outline uniform + shader branch, frustum wireframe Sg (D5); `FlyToImage`
through the Viewer animation path (D6).

### Phase 4 — UI: library + stack, GIS tab restructure
Restructure the GIS tab view (`src/PRo3D.GIS/GisApp.fs:701-790`) to cut
accordion nesting; projection-stack panel (reorder via ↑↓ buttons, remove, opacity
slider, count/cap indicator); library table: add-to-stack, fly-to, in-stack
badge, hover (`onmouseenter/leave` → `HoverImage`, throttled), curated header
fields (obs date, distance, instrument/camera); edit panel kept.

### Phase 5 — docs + tests
Update `docs/GisView.md` (also stale re: persistence claims) +
`docs/MultiImageProjection.md`; extend
`src/Tests/Features/Section12_GisView.fs`: stack ordering/`effectiveStack`
composition, sort-does-not-touch-stack, hover-in-stack no-duplicate, matrix
memoization, fly-to pushes an animation (pattern of
`Section01_StartingPRo3D.fs:89`).

## Resolved (was: open questions)

- **Base branch** — `origin/develop`. `features/pro3d-tool` is fully merged into
  it, so `InstrumentObservation.fs`, `SunAngles.fs` and the `Tiff.fs`/`Viewer.fs`
  changes are already in the base and there is no merge conflict to plan around.
  (The earlier "22 commits ahead" note compared against a local `main` that was
  605 commits stale.) PR goes to `develop`.
- **Cap** — `MAX_PROJECTED_IMAGES = 32`. 2 KB of matrices, ~128 MB worst-case
  texture budget; raising it later is one constant plus the `Arr` size type.
- **Stack reorder** — ↑↓ buttons first. `MoveInStack of Guid * int` takes a
  target index either way, so drag & drop can be layered on later without
  touching the model.
- **Coverage-count view** (`RelativeCount`) — kept, but ported **last**: the
  sampling stack lands first and `localImageProjections` stays on its current
  storage-buffer path until the end of Phase 2. `limitedShaderCapabilities`
  therefore survives until that final step, then goes.
- **Curated header fields** — observation date (`Mbi.obs_date`), distance to
  target (`ProjectedImageModel.distance`), instrument/camera (`Mbi.instrument`,
  `ImageMetadata.camera_system`/`mission_name`). Sun position is *not* in the
  header (it stays available for the future illumination work).

## Future work (explicitly out of scope)

Persistence of stack + per-image settings in the scene (additive
`Json.tryRead` field on `GisApp.ToJson`, no version bump needed); persistent
footprint toggles; projector-side occlusion/self-shadowing.
