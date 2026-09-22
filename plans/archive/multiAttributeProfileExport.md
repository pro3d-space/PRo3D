# Multi-Attribute Profile Export - Integration Plan

## Goal

Integrate the multi-attribute profile sampling (prototyped in `src/OpcViewer/MultiTexturingViewer.fs`) into the real PRo3D viewer. Given a drawn annotation line, export a CSV with distance, elevation, and all available texture/scalar attributes sampled along the profile.

## Prior Work (completed, in order)

1. **kdtreepicking.md** - KdTree-based picking in MultiTexturingViewer (verified KdTree hits match depth-buffer picks)
2. **profilePicking.md** - Line drawing with depth-buffer readback and segment sampling
3. **samplingProfiles.md** - Shooting rays along drawn lines to sample surface points, computing normals from triangle hits
4. **layeredDataExtraction.md** - Extracting per-patch texture attributes (gravity, altitude, etc.) at hit points using UV interpolation

## Architecture Overview

### What the prototype does (MultiTexturingViewer.fs)

The full pipeline works in the standalone viewer:
1. Loads `PatchHierarchy` per OPC, builds `patchInfoLookup: Dictionary<objectSetPath, (PatchFileInfo, OpcPaths)>`
2. Loads KdTrees per patch hierarchy
3. On KdTree hit, extracts `LazyKdTree.objectSetPath` to find the patch
4. Builds triangle-to-grid mapping (indices array -> triangle index -> grid vertex indices)
5. Computes barycentric coordinates at hit point -> interpolates UV
6. Loads textures per patch via `Patch.extractTexturePath` and samples at UV
7. Reports all attribute values

### What PRo3D already has

- **KdTree intersection**: `SurfaceIntersection.doKdTreeIntersection` in `src/PRo3D.Core/Surface.fs`
  - Iterates all active surfaces, applies transforms, intersects KdTrees
  - Returns `Option<ObjectRayHit * Surface>` + updated cache
- **Profile export**: `ExportAsProfileCsv` in `src/PRo3D.Core/Drawing/Drawing-App.fs:588`
  - Gets selected annotation points, converts to distance/elevation pairs, writes CSV
- **Surface data**: Each `SgSurface` has:
  - `picking: Picking.KdTree of HashMap<Box3d, Level0KdTree>` (per-patch kdtrees)
  - `dataSource: DataSource.OpcHierarchy of PatchHierarchy[]` (patch hierarchies with PatchFileInfo)

### The gap

`doKdTreeIntersection` returns `ObjectRayHit * Surface` but discards **which specific `Level0KdTree`** (i.e., which patch) was hit. The bounding box key used to look up the Level0KdTree is lost in `Surface.fs:250-271`. We need it to:
- Get `LazyKdTree.objectSetPath` -> look up `PatchFileInfo`
- Get `LazyKdTree.coordinatesPath` -> load texture coordinates
- Get `LazyKdTree.affine` -> transform positions for triangle mapping

## Implementation Plan

### Step 1: Extend `doKdTreeIntersection` to return hit patch info

In `src/PRo3D.Core/Surface.fs`:

- Modify `intersectKdTrees` (line 160) to also return the `Box3d` key
- Create a new function `doKdTreeIntersectionWithPatchInfo` (or extend the existing one) that returns `Option<ObjectRayHit * Surface * Box3d>` where the `Box3d` identifies which Level0KdTree was hit
- With the `Box3d` key, the caller can look up `Level0KdTree` from `SgSurface.picking` to get `objectSetPath`, `coordinatesPath`, `affine`

### Step 2: Create `ProfileAttributeExtraction` module

New file: `src/PRo3D.Core/ProfileAttributeExtraction.fs`

Port from MultiTexturingViewer.fs:
- `computeBarycentric` (lines 107-122)
- `buildTriangleToGridMapping` (lines 195-215) - with caching
- `getUVAtHit` (lines 219-230)
- `extractAttributesAtUV` (lines 233-299)
- `buildPatchInfoLookup` - builds `Dictionary<objectSetPath, (PatchFileInfo, OpcPaths)>` from `PatchHierarchy[]`

New function: `extractProfileWithAttributes`
- Input: annotation points, SurfaceModel (for kdtrees), ReferenceSystem, planet
- For each annotation point:
  1. Compute surface normal (from KdTree hit triangle)
  2. Shoot ray into surface along normal
  3. From hit, identify patch via Box3d key -> Level0KdTree -> objectSetPath -> PatchFileInfo
  4. Compute UV via triangle-to-grid mapping + barycentric interpolation
  5. Sample all texture layers at UV
- Also sample interior points between annotation vertices (using sampleSegment approach)
- Output: list of `(distance, elevation, Map<attributeName, float[]>)`

### Step 3: Add `ViewerAction` case and handler

In `src/PRo3D.Viewer/Viewer-Model.fs`:
- Add `ExportMultiAttributeProfile of string` to `ViewerAction`

In `src/PRo3D.Viewer/Viewer/Viewer.fs` (`updateViewer`):
- Handle `ExportMultiAttributeProfile path`:
  1. Get selected annotation from `m.drawing.annotations`
  2. Get annotation points
  3. Find the relevant SgSurface(s) from `m.scene.surfacesModel.sgSurfaces`
  4. Build patchInfoLookup from SgSurface.dataSource (PatchHierarchy[])
  5. Call `ProfileAttributeExtraction.extractProfileWithAttributes`
  6. Write CSV

This is a top-level `ViewerAction` (not `DrawingAction`) because it needs access to both `m.drawing` (annotation) and `m.scene.surfacesModel` (kdtrees, patches).

### Step 4: Add GUI entry

In `src/PRo3D.Viewer/Viewer/ViewerGUI.fs`:
- Add menu item alongside existing "selected as profile (*.csv)" (line 507)
- New item: "selected as multi-attribute profile (*.csv)"
- Triggers `ExportMultiAttributeProfile` ViewerAction

## Key Data Flow

```
Annotation points
    |
    v
For each point: shoot ray into surface
    |
    v
doKdTreeIntersectionWithPatchInfo
    -> ObjectRayHit (triangle index, ray T)
    -> Surface (which surface was hit)
    -> Box3d (which patch bounding box was hit)
    |
    v
SgSurface.picking[Box3d] -> Level0KdTree.LazyKdTree
    -> objectSetPath  (positions .aara file)
    -> coordinatesPath (texture coordinates)
    -> affine          (local-to-global transform)
    |
    v
patchInfoLookup[objectSetPath] -> PatchFileInfo
    -> Textures list (texture file names per patch)
    |
    v
buildTriangleToGridMapping(affine, objectSetPath)
    -> triangle index -> grid vertex indices
    |
    v
getUVAtHit(coordinatesPath, gridMapping, triIdx, triangle, hitPoint)
    -> UV coordinates via barycentric interpolation
    |
    v
extractAttributesAtUV(uv, patchInfo, opcPaths)
    -> Dictionary<textureName, float[]> (all attribute values)
```

## Files to Modify

| File | Change |
|------|--------|
| `src/PRo3D.Core/Surface.fs` | Extend intersection to return hit Box3d key |
| `src/PRo3D.Core/ProfileAttributeExtraction.fs` | **New** - extraction logic ported from MultiTexturingViewer |
| `src/PRo3D.Viewer/Viewer-Model.fs` | Add `ExportMultiAttributeProfile` to ViewerAction |
| `src/PRo3D.Viewer/Viewer/Viewer.fs` | Handle new action in updateViewer |
| `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` | Add menu item |

## Open Questions

- Should interior sampling (between annotation vertices) be configurable (sampling distance)?
  - PRo3D already has `samplingAmount` and `samplingUnit` in DrawingModel
- Should the export also include the original distance/elevation columns for backward compatibility?
- How to handle multiple surfaces being hit along the profile (different patches from different surfaces)?
