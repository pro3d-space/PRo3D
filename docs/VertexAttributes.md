# Per-Vertex Attribute Layers

Newer OPC exports store their attribute layers twice: as **texture layers** under
`Images/<Layer>/` and, additionally, as **per-vertex `*.aara` grids** inside each patch
directory. PRo3D reads the per-vertex form wherever it exists — it is far cheaper than
decoding an image per layer per sample — and falls back to texture sampling otherwise.

Two features use this:

* **Profile extraction** — attribute values along an annotation, exported to CSV.
* **The 3D cursor readout** — the *Under Cursor* section of the **Config** panel, which
  shows the values under the mouse pointer live. This is also the testbed for the
  extraction itself.

## What the data looks like

A patch directory of such an OPC (HERA Dimorphos, `AARA_Textures` export):

```
Patches/0_0_2/
  XYZ_Local.aara              positions (V3f, 1032 x 1032)
  Earth8K_Coordinates.aara    texture coordinates (V2f, 1032 x 1032)
  Earth8K_Weights.aara        texture blend weights (float, 1032 x 1032)
  LonLatRad.aara              V3f, 1024 x 1024   <-- attribute layers
  Normal.aara                 V3f, 1024 x 1024
  Gravity.aara                V3f, 1024 x 1024
  Magnitude.aara              float, 1024 x 1024
  Potential.aara              float, 1024 x 1024
  Elevation.aara              float, 1024 x 1024
  Slope.aara                  float, 1024 x 1024
  patch.xml
```

`patch.xml` lists them in its `<Attributes>` element; the layer name is the file's base
name.

Layers hold whatever units the exporter wrote. `LonLatRad` is **not** in degrees: on the HERA
exports its first two channels are gradians -- longitude x 10/9 (0..400) and (latitude + 90)
x 10/9 (0..200, from the south pole) -- while the third channel, the radius, is in metres.
Established by comparing the layer against lat/lon/alt computed from the same intersection
points via `CooTransformation`; they agree to 1e-4 degrees once converted. Only layers whose resolution matches the geometry can be exported this way, so an
OPC may well ship a subset — or none at all.

### The attribute grid is smaller than the position grid

The position grid carries a skirt around the attribute grid, and its width shrinks with
the hierarchy level because it is a constant number of *source DEM* pixels:

| level | positions | attributes | offset |
|-------|-----------|------------|--------|
| 0     | 1032²     | 1024²      | 4      |
| 1     | 1028²     | 1024²      | 2      |
| 2     | 1026²     | 1024²      | 1      |

So a vertex at position grid `(x, y)` maps to attribute index

```
attributeIndex = (y - off) * attrWidth + (x - off),   off = (posSize - attrSize) / 2
```

This was established empirically, by comparing the third `LonLatRad` channel (the vertex
radius, in metres) against `|Local2Global · XYZ_Local|` for every patch of
`g_01960mm_spc_dtm_dimo_0000n00000_v003`. The centred offset agrees to float32 round-off
(median error 2·10⁻⁶ m); every other offset is off by at least 0.19 m. The same invariant
guards the code — see `per-vertex layers are physically consistent` in
`src/Tests/ProfileAttributeExtractionTest.fs`.

## How a value is sampled

1. Pick a surface: the KdTree hit gives a triangle index into the patch's triangle set.
2. `PatchTriangleGrid` maps that triangle back to its three position grid indices.
3. Barycentric weights of the hit point within the triangle.
4. For each attribute layer, read the three corner values and blend them.

Only three small random-access reads per layer are needed, so nothing has to be held in
memory. ZIP-backed OPCs (`*.opcz`) cannot seek, so their payloads are cached instead,
bounded at 512 MB.

Interpolation is component-wise. A layer that wraps — the longitude channel of
`LonLatRad` — is therefore interpolated *across* the 0/360 seam rather than around it, so
values near the seam can be misleading.

Vertices in the skirt have no attribute values. A hit whose triangle touches the skirt
yields no per-vertex value for that layer, and the texture fallback takes over.

## Texture sampling fallback

Layers the per-vertex data does not cover are read from the patch's attribute textures.
This costs a full image decode per layer per sample, so it is used for profile extraction
but **never** for the 3D cursor — a surface without per-vertex layers shows nothing in the
*Under Cursor* readout and says so.

Attribute textures store each layer **normalised into the layer's `ChannelsDefinedRange`**
from the `*.opcx` (EXR layers hold `[0,1]` floats; 8/16 bit images are normalised on read).
Per-vertex layers hold physical values. Texture samples are therefore mapped back onto the
layer's range so both sources are in the same units:

```
physical = definedRange.Min + sample * definedRange.Size
```

Without a declared range the raw sample is returned unchanged. Samples carrying the
export's nodata sentinel (values `<= -9999`) are dropped rather than reported.

Because texture sampling is nearest-texel while the per-vertex path interpolates, the two
agree to a few percent of a layer's range for smooth layers (elevation, potential,
gravity magnitude) and can differ substantially for a gradient layer such as slope.

## The Under Cursor readout

`Config → Show Preview Cursor` drives both the 3D pointer and this readout; it is **on by
default**. The readout is the *Under Cursor* accordion in the **Config** panel, below
*Screenshots*, and shows the surface, the patch and every per-vertex layer under the mouse.

The readout only updates while surface picking is active — hold `CTRL` and move the mouse
over a surface. Picking and attribute extraction run on the background picking thread, so
neither blocks the UI.

Per-patch data (attribute layer headers, triangle-to-grid mappings) is cached by path.
Triangle mappings dominate — about 4 MB per patch — so that cache is bounded to 32
patches, and all caches are dropped when a surface is removed.

## Multi-channel `*.opcx` attribute layers

For a multi-channel `Map` layer, ExportGpc writes one range per channel:

```xml
<AttributeLayer version="0" num="12">
  <Type>Map</Type>
  <Label>Gravity</Label>
  <ChannelsDefinedRange>[[-0.000039896, 0.000040985], [-0.000046144, 0.000046183], [-0.000050788, 0.000050736]]</ChannelsDefinedRange>
  ...
```

PRo3D parsed this with `Range1d.Parse`, which only understands `[min, max]`, so such OPCs
failed to import until the `*.opcx` was hand-patched. Both forms are now accepted.

The scalar layer model carries exactly one range, and it keeps the **first channel's**.
That is the right choice because both consumers look at channel 0 — the false-colour legend
and the de-normalisation of texture samples, which are read through
`ChannelReference.ChannelWithIndex 0`. Unioning the channels would widen the range (by 25%
for Dimorphos's `Gravity` layer) and skew every de-normalised value. `parseChannelRanges`
still returns all channels for callers that need them.

An unparsable range logs a warning and falls back to `[0, 1]` instead of failing the import.

The parser is PRo3D's own copy, `SurfaceUtils.SurfaceAttributes` in
`src/PRo3D.Core/Surface/SurfaceApp.fs` — reached from `SceneLoader.addSurfaceAttributes`.
`OPCViewer.Base` ships a near-identical copy that PRo3D's import path does not use, so no
package update is involved.

## `*.opc.json` sidecar

ExportGpc writes a `*.opc.json` next to the `*.opcx`. PRo3D parses it at surface import and
logs a summary — it is not persisted into the scene:

* `product_information` — product type/state, creator, creation time
* `input_products` — the `*.gpc.json` the OPC was exported from
* `DemModel` — the DEM reference model. **Its shape depends on `ModelType`:**
  * `DemSphere` (Deimos BDS export) declares `Center` and a single rotation `Axis`.
  * `DemEllipsoid` (Dimorphos export) declares `Center`, a full frame
    `AxisX`/`AxisY`/`AxisZ`, and the three semi-axis lengths `Radii`
    (`[89.5, 84.5, 57.5]` m for Dimorphos) — and *no* `Axis`.

  Both are read; fields absent for a given model type stay `None`. A key that is present
  but not a numeric triple logs a warning rather than being reported as missing.
* `DskBrief` — only present for OPCs derived from a SPICE DSK (`*.bds`) shape model; the
  Dimorphos export has none. Kept verbatim in `dskBrief`, and parsed into `dskSummary`:
  body, reference frame, coordinate system, longitude/latitude/radius ranges and
  vertex/plate counts. Every field is optional — DSKBRIEF output is free text.

Example output for the Deimos BDS export:

```
[OpcMetadata] Deimos: deimos_k005_tho_v02.opc.json
[OpcMetadata]   product: opc (initial), created 2026-08-04T08:10:14.619015+0000 by ExportGpc
[OpcMetadata]   input: ...\4_ModifyGpc\Deimos\deimos_k005_tho_v02.gpc.json
[OpcMetadata]   DEM model: DemSphere center=[39.2324, -75.5169, 339.0206] axis=[0.0301, 0.9982, -0.0526]
[OpcMetadata]   DSK body: 402 (DEIMOS), frame IAU_DEIMOS, Planetocentric Latitudinal
[OpcMetadata]   DSK radius 3.58220 .. 8.70680 km, longitude -180.0 .. 180.0 deg, latitude -90.0 .. 90.0 deg
[OpcMetadata]   DSK plates: 5040, vertices: 2522
```

## Code map

| File | Contents |
|------|----------|
| `src/PRo3D.Core/VertexAttributes.fs` | `*.aara` header reading, layer discovery, grid offset, barycentric sampling |
| `src/PRo3D.Core/ProfileAttributeExtraction.fs` | triangle-to-grid mapping, texture fallback, profile walk, CSV export |
| `src/PRo3D.Core/TriangleSet.fs` | `computeValidQuadStarts` — the compact triangle-to-grid form |
| `src/PRo3D.Core/OpcMetadata.fs` | `*.opc.json` / DSKBRIEF parsing |
| `src/PRo3D.Core/Surface/SurfaceApp.fs` | `SurfaceUtils.SurfaceAttributes` — `*.opcx` attribute layer parsing |
| `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` | `Gui.Config.underCursor` — the *Under Cursor* readout |
| `src/PRo3D.Viewer/Viewer/Picking.fs` | `pickRayInfo`, which keeps the hit info the extraction needs |

## Tests

`src/Tests/ProfileAttributeExtractionTest.fs` and `src/Tests/OpcSidecarTests.fs`.

All fixtures come from a checkout of
[PRo3D.Resources.TestData](https://github.com/pro3d-space/PRo3D.Resources.TestData).
Point **`PRO3D_TEST_DATA`** at it and the data-backed cases run; leave it unset and
every one of them skips rather than fails. Paths are resolved relative to that root:

| Fixture | Path under the checkout | Used for |
|---|---|---|
| Dimorphos DRACO1 OPC | `Dimorphos_DRACO1/Dimorphos_DRACO1` | grid mapping, aara header, kd-tree intersection |
| test annotation | `Dimorphos_DRACO1/testAnnotatation.pro3d.ann` | end-to-end profile extraction |
| HERA Dimorphos AARA export | `HERA/Dimorphos` | per-vertex layers, texture fallback, attribute coverage |

```
set PRO3D_TEST_DATA=C:\Users\<you>\Desktop\pro3d\PRo3D.Resources.TestData
dotnet run --project src/Tests -- --filter "all.profile tests.ProfileAttributeExtraction"
```

The suite-wide `--testdatasource` is still honoured as a fallback root, so
`run-tests.cmd` keeps working. Two narrower overrides remain for exports kept
outside the checkout: `PRO3D_AARA_OPC` for an OPC with per-vertex attribute
layers, and `PRO3D_BDS_OPC` for one with a `*.opc.json` carrying a `DskBrief`.

CSV exports the tests produce are written to `outputs/ProfileExtraction/` under the
same root.

*profile export covers every declared attribute layer* is the regression test for
exports that lost attributes. It reads each hit patch's `<Attributes>` and requires
every layer named there to reach the CSV — with all of its components, and with one
column per attribute and no empty cells. It also requires the texture fallback to be
able to reach each layer on its own, which the per-vertex path would otherwise mask.
Two defects it pins down, both fixed:

* per-vertex `*.aara` layers not being read at all, which left the scalar layers
  (`Elevation`, `Magnitude`, `Potential`, `Slope`) without a source, and
* deriving the attribute texture indices from `patchInfo.Textures.Length / 2`, which
  assumes the list is `[textures; weights]`. `patch.xml` interleaves them
  (`DiffuseColorNTexture`, `DiffuseColorNWeights`), so that walk landed on the
  `*.aara` weights entries and reached only `LonLatRad`, `Normal` and `Gravity` —
  three of seven layers, as raw normalised samples.
