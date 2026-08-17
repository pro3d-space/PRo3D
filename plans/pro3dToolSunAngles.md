# `pro3d-tool` — a public CLI, and sun-angle rasters from ASPECT imagery

## Goal

Ship PRo3D's command-line functionality as **one public dotnet tool, `pro3d-tool`**,
carrying two verbs:

- `kdtree` — KdTree generation for OPC hierarchies, migrated from the existing
  `opc-tool` (clean break; see [Migration](#migration-from-opc-tool)).
- `sun-angles` — for each ASPECT instrument image, emit per-pixel **illumination
  geometry as float32 rasters** (incidence, emission, phase, slope, azimuth),
  pixel-aligned to the source image, plus a screenshot for eyeballing.

`PRo3D.ProjectionTestbed` **stays exactly where it is**: a separate, unpublished
development instrument. Its diagnostic flags (`--flip-sweep`, `--flip-normals`,
`--model-offset-px`, `--time-offset-sec`, the `DebugNormal`/`DebugOutward`/
`DebugModelTrafo` shade modes) are investigation tooling, not a supported contract,
and must not become public CLI surface. Two tools, two audiences.

## Why this is mostly plumbing, not new science

The angle computation already exists and is already validated by the testbed:

- `PRo3D.ProjectionTestbed/Shading.fs:83` computes `(incidence, emission, phase)`
  in radians, in view space.
- `PRo3D.ProjectionTestbed/Program.fs:368` already renders and writes them
  (`angle_incidence.png`, …).
- `Setup.fs:130` (`sunDirection`) resolves the Sun direction in the body-fixed
  frame via SPICE; `Setup.fs:112` handles the relative-state plumbing.
- The `FromInstrument` camera mode combined with `width`/`height` defaulting to the
  source image's native size **already yields the 1:1 pixel alignment** this feature
  requires. That correspondence is the whole reason the testbed renders that way.

What is missing is precision and packaging, in three concrete places:

1. **`Program.fs:360` quantises to 8-bit PNG**, scaling incidence/emission to 0..90°
   and phase to 0..180°. A float raster must carry radians (or degrees) unquantised.
2. **`Offscreen.fs:29` allocates `TextureFormat.Rgba8`.** Float output needs float
   render targets.
3. **`PRo3D.GIS/Tiff.fs` is read-only** — every `Tiff.Open` call passes `"r"`. It
   already decodes `SampleFormat.IEEEFP` at 32 bits (`Tiff.fs:159`, `Tiff.fs:210`),
   so the format knowledge is there, but there is no write path.

Slope and azimuth are genuinely net-new — no occurrence anywhere in
`PRo3D.ProjectionTestbed` or `PRo3D.GIS`.

## Phase 1 — `pro3d-tool` skeleton and the `kdtree` verb

New project `src/PRo3D.Tool/PRo3D.Tool.fsproj`, modelled on
`src/opc-tool/opc-tool.fsproj`:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>pro3d-tool</ToolCommandName>
<PackageId>PRo3D.Tool</PackageId>
```

**Verb dispatch.** `opc-tool` today uses a single flat options record with
`[<Option>]` plus `[<Value(0)>]` (`src/opc-tool/Program.fs:191`). Move to
`CommandLineParser.FSharp` verbs:

```fsharp
[<Verb("kdtree", HelpText = "Validate OPC directories and generate KdTrees.")>]
type KdTreeOptions = { ... }        // carries over opc-tool's options verbatim

[<Verb("sun-angles", HelpText = "Render per-pixel illumination geometry for instrument images.")>]
type SunAnglesOptions = { ... }

Parser.Default.ParseArguments<KdTreeOptions, SunAnglesOptions>(argv)
```

Each verb gets its own `--help` for free.

**Lazy GPU initialisation is mandatory, not a nicety.** `kdtree` today needs only
`Aardvark.Init()` + `PixImageDevil.InitDevil()` (`src/opc-tool/Program.fs:228`) and
therefore runs on machines with no GPU or display — that is how it gets used in
data-prep pipelines. `sun-angles` needs a real GL/Vulkan runtime. **The runtime must
be created inside the `sun-angles` branch only**, never before verb dispatch.
Otherwise a driverless box fails at startup and KdTree generation dies for a reason
that has nothing to do with what it was asked to do. This is the single most likely
way to regress the existing tool.

**Slim the dependency closure.** `src/PRo3D.GIS/paket.references` lists **`Aardium`**
— the Electron shell — but no `src/PRo3D.GIS/*.fs` references it. It is a stray.
Removing it keeps an Electron dependency out of the CLI's closure entirely. Audit
`Newtonsoft.Json` and `Chiron` there at the same time.

## Phase 2 — the `sun-angles` verb

### Output

Per input image, five single-band float32 rasters plus a screenshot:

| Raster | Definition | Units |
|---|---|---|
| incidence | surface normal ↔ direction to Sun | radians |
| emission | surface normal ↔ direction to observer | radians |
| phase | Sun ↔ surface ↔ observer | radians |
| slope | local surface slope against the body reference (see open question) | radians |
| azimuth | slope azimuth in the body-fixed frame | radians |

All are **pixel-aligned to the source `.tif`**, so they overlay the ASPECT frame 1:1.

### Rendering

Extend `Offscreen.createTarget` to accept a format, and add a float variant:
five `TextureFormat.R32f` colour attachments in **one MRT pass**, so the geometry is
rasterised once rather than five times. `Shading.fs` already produces incidence,
emission and phase in the fragment stage — they get written to attachments instead of
being tonemapped to RGB.

**Clear to NaN, not zero.** Pixels where the ray misses the body, or where the
front-facing test rejects the fragment, carry no meaningful angle. Zero is a
perfectly plausible incidence angle, so clearing to zero silently fabricates
data. NaN is the nodata value, and it must survive readback.

### Writing float32 TIFF

New write path alongside the existing reader in `PRo3D.GIS/Tiff.fs`, using
`BitMiracle.LibTiff.NET` (already a dependency — and now correctly credited, see
PR #690): `SampleFormat.IEEEFP`, `BitsPerSample = 32`, `SamplesPerPixel = 1`,
`Photometric.MINISBLACK`, written by scanline.

Plain TIFF — no georeferencing tags. The rasters live in the instrument image
frame, so there is no map projection to encode; they are read as unreferenced
float rasters by GDAL/QGIS and anything else that speaks TIFF.

Write a small JSON sidecar per image recording units, nodata sentinel, source image,
epoch, body, frame, observer, and the SPICE kernel actually used — otherwise the
rasters are unprovenanced floats.

### Batch is the primary mode

Point the verb at a folder of `.tif` + `.mbi.json` pairs and process all of them.

- Initialise SPICE **once** and the GL runtime **once**, reuse across every image.
  This is the main reason batch belongs inside the tool rather than in a shell loop.
- **Per-image failure isolation**: one bad image logs and continues; the run reports
  a summary and exits non-zero if anything failed. A batch that dies on image 3 of
  400 is useless.
- Deterministic output naming: `<imageBaseName>_incidence.tif`, `_emission.tif`, …
  under `--out`.
- `--image` becomes a filter that selects a single frame from the folder.

### Interactive mode

Retained, as requested — `--interactive` opens a window for eyeballing a projection
without launching PRo3D. Note this is the one path in the public tool that requires a
display, which is why it must stay opt-in and off the batch path.

## Phase 3 — test script and test data

The tool ships with `scripts/run-pro3d-tool-examples.ps1`, a runnable demonstration
of every verb against public test data — modelled on the existing
`scripts/run-projection-testbed.ps1` (same `param()` + `dotnet run --project` shape,
so it works from a source checkout before anything is published).

Test data lives in a **separate repository** so a plain PRo3D clone stays small:

```
git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
.\scripts\run-pro3d-tool-examples.ps1 -TestData <path-to-clone>
```

`-TestData` is a plain path, not a submodule — documented that way in
`docs/Pro3DTool.md` so users clone it wherever they like. (A submodule wiring at
`src/Tests/data/opc` exists in history for the test suite; the tool examples
deliberately do not depend on it.)

### What the test data covers

| Verb | Fixture | Status |
|---|---|---|
| `kdtree` | `1087_004779_MSLMST_0011/` — MSL/Stimson OPC | **runs end-to-end** |
| `sun-angles` | `HERA/Instrument Data/ASP_000000_270323T060000_2B_NIR1_0.tif` + `.mbi.json` | **cannot run end-to-end** |

`sun-angles` needs three inputs — instrument image, body OPC, and SPICE kernels —
and the repository currently holds only the first. There is no Didymos/Dimorphos OPC
and no kernel of any kind (`.bsp`/`.tm`/`.tf`/`.tpc`) in the clone. So either:

- **add a small Didymos OPC and a minimal metakernel to
  `PRo3D.Resources.TestData`** (preferred — makes the example self-contained and
  gives the verb a regression fixture), or
- have the script skip the `sun-angles` example with an explicit message when
  `-Opc`/`-KernelRoot` are not supplied, exactly as the test suite skips OPC tests
  when the submodule is absent.

Skipping loudly is the fallback, not the goal: an example that cannot be run is not
an example. Decide before Phase 2 lands, because it determines whether `sun-angles`
gets an automated regression test at all.

## Phase 4 — packaging and release

- **Decouple the tool version from the product version.** `Build.fs:764` currently
  packs with `notes.NugetVersion`, welding the tool to PRo3D's release cadence, so a
  tool-only fix forces a product version bump. Drive it from its own release-notes
  file instead.
- Replace the `opc-tool` pack step (`Build.fs:760`) with `PRo3D.Tool`.
- `Publish` (`Build.fs:448`) is untouched — it publishes only `PRo3D.Viewer`, so the
  tool stays out of the product payload exactly as `opc-tool` does today.
- `docs/Pro3DTool.md` per the repo rule that every feature gets a docs page. It
  documents both verbs, and opens with the `git clone` of
  `PRo3D.Resources.TestData` plus a worked `-TestData <path>` example, so a reader
  can run something before reading anything else. `docs/OpcTool.md` is superseded
  and should say so.

## Migration from `opc-tool`

Clean break, as decided:

- `pro3d-tool kdtree <dir>` replaces `opc-tool <dir>`. Every existing invocation and
  script breaks — including the examples in `docs/OpcTool.md`
  (`opc-tool --forcekdtreerebuild "F:\pro3d\data\dimorphos"`).
- Deprecate `opc-tool` on nuget.org with a pointer to `PRo3D.Tool`. NuGet
  deprecation keeps existing installs working while flagging new ones.
- Delete `src/opc-tool` once `kdtree` is verified at parity.
- Announce in the release notes, since the break is user-visible.

## Open questions

1. **Slope relative to what?** On an irregular small body like Didymos, slope needs a
   local reference "up", and radial-from-centre and local gravity differ
   substantially — enough to change the numbers materially. Radial is trivial to
   compute; a gravity-based reference needs a shape/density model. **This must be
   decided before implementing slope/azimuth**, and whichever is chosen must be
   recorded in the sidecar, because the two are not interconvertible after the fact.
2. **Angle units** — radians or degrees in the raster? Radians proposed above;
   degrees are friendlier to analysts reading pixel values directly.
3. **Emission beyond 90°** — `Shading.fs:83` notes emission may exceed 90°. Clamp,
   or preserve and let downstream mask? Preserving is proposed; it is information.
4. **Does `PRo3D.Resources.TestData` gain a Didymos OPC and a metakernel?** Without
   them the shipped `sun-angles` example cannot be executed by a user who follows
   the documentation, and the verb has no regression fixture. See
   [Phase 3](#phase-3--test-script-and-test-data). Decide before Phase 2 lands.

## Out of scope

- `project-screen` (screen-space → body projection) — deferred until the verb
  infrastructure is proven. Net-new; no prototype exists.
- Absorbing or refactoring `PRo3D.ProjectionTestbed`.
- Map-projected (body-fixed lat/lon grid) output.
