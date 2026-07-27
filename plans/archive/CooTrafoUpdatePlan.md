# CooTransformation cleanup — first-class LATREC, end failure-as-zero

## Problem

`src/PRo3D.Base/CooTransformation.fs` silently swallows native-library errors and exposes only the SPICE planetographic (PGRREC) path. This produces three concrete bugs and one design gap.

1. **Bogus GUI readouts.** Every wrapper seeds out-params with `let private init = 0.0` (line 161), calls the native function, logs once via `Log.line "cootrafo errorcode %A"` on a non-zero return, and returns the zero-initialised values *as if the call had succeeded*. On Dimorphos the status bar (`ViewerGUI.fs:141`) and the Reference System panel (`ReferenceSystem.fs:154`) show `lat=0°, lon=360°, alt=0 m`. `IsNaN` guards never fire because the values are zero, not NaN.

2. **100 GB `Aardvark.log`.** Per-frame redraw hits the wrapper repeatedly; six `Log.line` sites in `CooTransformation.fs` (lines 171, 196, 213, 234) plus two `printfn` sites in `SpiceInterfacing.fs` (`PRo3D.Base:44`, `PRo3D.GIS:43`) spam unconditionally at framerate.

3. **A "north" that only works by accident.** `MapViewCameraController.fs:83-93`:
   ```fsharp
   let up    = CooTransformation.getUpVector point planet |> Vec.Normalized
   let east  = V3d.OOI.Cross(up).Normalized
   let north = up.Cross(east).Normalized
   ```
   When `getUpVector` fails for Dimorphos, `getLatLonAlt → getXYZFromLatLonAlt` both silently return zero, so `up = (V3d.Zero − p).Normalized = −p.Normalized` — pointing inward. The camera "works" only because the body sits at world origin and the surrounding math is forgiving. The day someone moves the body off origin the orientation flips.

4. **Wrong conversion method for some bodies.** PGRREC is the planetographic transform, mathematically valid only for **spheroids** (oblate, rotationally symmetric). ESA's SPICE expert: Dimorphos is a tri-axial **ellipsoid** (radii `(89.5, 84.5, 57.5)` m) **and** has no stable rotation pole — post-DART tumbling makes a fixed PCK rotation model inaccurate, so ESA intentionally never defined POLE/PM for body `-658031`. PGRREC is the wrong tool for it. The community-standard convention for tri-axial small bodies is **planetocentric** (LATREC). It's a first-class method, not a fallback.

## SPICE coverage — what is and is not available

From ESA SPICE-team documentation and direct communication, plus the standalone probe `src/Tests/spice_coverage_tests.py`:

- **LATREC / RECLAT** is a pure-math coordinate transform of an xyz point. Takes ONE reference radius. Lat/lon are exact planetocentric polar coordinates of the point regardless of body shape (sphere, spheroid, tri-axial, …) as long as a body-fixed frame exists. **The JR.CooTransformation native wrapper does not expose LATREC**, so we implement it directly in F#.
- **PGRREC / RECPGR** is planetographic, oblate-spheroid math. Needs equatorial `re`, polar `rp`, and the body's `POLE_RA / POLE_DEC / PM` in the kernel pool. Mathematically valid only for spheroids — using it on a tri-axial body is incorrect even if the call succeeds. The JR.CooTransformation native wrapper exposes PGRREC via `Xyz2LatLonAlt` / `LatLonAlt2Xyz`. The native call fails on Dimorphos (returns error -3), consistent with the `SPICE(MISSINGDATA)` raised by direct `sp.pgrrec("DIMORPHOS", …)` calls in the Python probe: no Hera PCK defines POLE/PM for body `-658031`.
- **Tri-axial ellipsoidal ray-intersection.** Not in SPICE under that name; trivial to implement in F#. Same (lat, lon) as LATREC (always exact planetocentric polar coords); altitude referenced to the true tri-axial surface via ray-intersection from the body centre. Useful when the user wants altitude that reads 0 on the actual ellipsoid surface.
- **`IAU_<BODY>` frame** is auto-registered by name for every body but only evaluates when POLE_RA / POLE_DEC / PM exist. For Dimorphos this is intentional ESA design — the body tumbled after DART so a fixed PCK rotation model would misrepresent it; orientation is provided by `DIMORPHOS_FIXED` (dynamic two-vector frame in FK) or `DIMORPHOS_CK` (Flight-Dynamics attitude file).

## Decisions

- **LATREC is a first-class method, not a fallback.** Surfaced as the right convention for the bodies that need it.
- **Up-vector failure** → fall back to `p.Normalized` (radial). A direction, not a coordinate; geometrically sound regardless of frame.
- **Migration scope** → full sweep of the 22 caller files. No `[<Obsolete>]` shim.
- **Logging** → wrappers are silent. Callers decide whether to surface a `None`.
- **`Planet.None / .JPL / .ENU`** keep their existing short-circuits — they are deliberate non-planetary conventions, not fallbacks. `Planet.ENU` is *not* equivalent to the radial fallback (it pins `up = V3d.ZAxis`, world-Z everywhere; radial varies per point).
- **Per-body convention** lives in a hardcoded `bodyConvention : Planet → ConventionKind` table in `CooTransformation.fs`. One literal per body, easy to audit.
- **Two F# convention helpers ship together:** `Spherical of meanRadius` (LATREC, one radius) and `Ellipsoidal of radii` (tri-axial ray-intersection, three radii). Both compute identical exact planetocentric (lat, lon); only altitude reference differs. Per-body choice is a single line in `getConvention`.
- **Default for Dimorphos is Spherical** with mean radius `(89.5 + 84.5 + 57.5) / 3 = 77.17 m`, matching ESA SPICE-team guidance. Switching to Ellipsoidal is a one-line change if precise altitude-above-true-surface is preferred later.
- **Radii** are hardcoded F# constants per body, sourced from the loaded PCK at design time. Updated by hand if ESA revises a PCK.

## Design

### Convention enum and dispatch

Three options. Two F# paths (Spherical, Ellipsoidal) cover what the native wrapper cannot:

```fsharp
type ConventionKind =
    | Planetographic                       // PGRREC via JR.CooTransformation native
    | Spherical   of meanRadius:double     // F# LATREC, single radius
    | Ellipsoidal of radii:V3d             // F# tri-axial ray-intersection
    | NonPlanetary                         // Planet.None / .JPL / .ENU

let getConvention = function
    | Planet.Mars    | Planet.Earth | Planet.Moon
    | Planet.Phobos  | Planet.Deimos
    | Planet.Didymos                          -> Planetographic
    | Planet.Dimorphos                        -> Spherical 77.1666666666667   // (89.5+84.5+57.5)/3 m
    | Planet.None    | Planet.JPL | Planet.ENU -> NonPlanetary
```

Radii in metres to match the native wrapper's xyz convention.

**Default for Dimorphos is `Spherical`** (LATREC convention, single mean radius) — matches ESA SPICE-team guidance and the SPICE community's standard for small bodies. Switching to `Ellipsoidal (V3d(89.5, 84.5, 57.5))` is a one-line change in `getConvention` if altitude referenced to the true tri-axial surface is preferred later. Both helpers ship together so no further code change is needed.

The (lat, lon) values are identical under Spherical and Ellipsoidal — both are exact planetocentric polar coordinates. Only altitude differs:

| Convention | Altitude reference | Reads 0 at … |
|---|---|---|
| Spherical 77.17 m | Distance from a sphere of mean radius | The mean-radius sphere (~±12 m off the true surface for Dimorphos) |
| Ellipsoidal | Distance from the true tri-axial surface | The actual ellipsoid surface |

### Why LATREC works for non-spheroidal bodies

LATREC is *not* a shape model. It is a coordinate transform of a point:
```
lat = asin(z / r)    lon = atan2(y, x)    r = |p|
```
It never claims a surface; it reports the spherical coordinates of the *given* xyz point. The body's shape only enters through the **altitude reference**, which is a separate modelling choice.

### Spherical helper (LATREC)

```fsharp
let private latLonAltOnSphere (radius : double) (p : V3d) : SphericalCoo option =
    let r = p.Length
    if r = 0.0 then None
    else
        let n = p / r
        Some {
            latitude  = (asin n.Z)      * Constant.DegreesPerRadian
            longitude = (atan2 n.Y n.X) * Constant.DegreesPerRadian
            altitude  = r - radius
            radian    = 0.0
        }
```

Inverse: `xyz = (radius + alt) · (cos lat·cos lon, cos lat·sin lon, sin lat)`. Round-trips to numerical precision.

### Ellipsoidal helper (tri-axial)

Same (lat, lon); altitude via ray–ellipsoid intersection from the origin:
```
surface point at  t·p̂  with  (t·n̂.X)²/a² + (t·n̂.Y)²/b² + (t·n̂.Z)²/c² = 1
  → t_surface = 1 / sqrt( (n̂.X/a)² + (n̂.Y/b)² + (n̂.Z/c)² )
alt = |p| − t_surface
```

Inverse computes `rSurface` along the (lat, lon) direction the same way, then places the point at `rSurface + alt` in that direction. Round-trips cleanly.

### New API

All total `get*` wrappers become `tryGet*` returning `Option`:

```fsharp
val tryGetLatLonAlt        : planet:Planet -> p:V3d -> SphericalCoo option
val tryGetXYZFromLatLonAlt : sc:SphericalCoo -> planet:Planet -> V3d option
val tryGetXYZFromLatLonAlt': coordinate:V3d -> planet:Planet -> V3d option
val tryGetLatLonRad        : p:V3d -> SphericalCoo option
val tryGetHeight           : p:V3d -> up:V3d -> planet:Planet -> double option
val tryGetAltitude         : p:V3d -> up:V3d -> planet:Planet -> double option
val tryGetElevation        : planet:Planet -> p:V3d -> double option

val getUpVector            : p:V3d -> planet:Planet -> V3d        // total; radial fallback
val getConvention          : planet:Planet -> ConventionKind      // for GUI display
```

Internal dispatch:

```fsharp
let tryGetLatLonAlt (planet : Planet) (p : V3d) : SphericalCoo option =
    match getConvention planet with
    | NonPlanetary       -> None
    | Spherical r        -> latLonAltOnSphere r p
    | Ellipsoidal radii  -> latLonAltOnEllipsoid radii p
    | Planetographic     -> tryPgrrec planet p     // native, nan-seeded, Option on err
```

### Per-call failure detection

The native PGRREC call returns a non-zero error code → `tryPgrrec` returns `None`. No body whitelist, no startup probe. Mutables seed with `nan` instead of `0.0`. If ESA later ships POLE/PM for Dimorphos, switching the `bodyConvention` line to `Planetographic` is the only edit needed.

### Up-vector becomes total

```fsharp
let getUpVector (p : V3d) (planet : Planet) : V3d =
    match planet with
    | Planet.None ->  V3d.ZAxis
    | Planet.JPL  -> -V3d.ZAxis
    | Planet.ENU  ->  V3d.ZAxis
    | _ ->
        match tryGetLatLonAlt planet p with
        | None -> if p.LengthSquared > 0.0 then p.Normalized else V3d.ZAxis
        | Some sc ->
            match tryGetXYZFromLatLonAlt { sc with altitude = sc.altitude + 100.0 } planet with
            | Some v2 -> (v2 - p).Normalized
            | None    -> if p.LengthSquared > 0.0 then p.Normalized else V3d.ZAxis
```

For Dimorphos the spherical path succeeds, so `up` is the +100 m radial difference, equal to `p.Normalized` to numerical precision. For Mars, PGRREC succeeds, so `up` matches the geodetic normal (slightly off-radial near the poles due to flattening — correct).

### Removing the bogus-value pipeline

- Delete `let private init = 0.0` (line 161). Out-params seed with `nan`.
- Delete all six `Log.line "cootrafo errorcode"` sites in `CooTransformation.fs`.
- Delete `printfn "could not get rot trafo for frame: %s"` in `src/PRo3D.Base/SpiceInterfacing.fs:44` and `src/PRo3D.GIS/SpiceInterfacing.fs:43`.
- Delete the existing nine total `get*` wrappers (`getLatLonAlt`, `getXYZFromLatLonAlt`, `getXYZFromLatLonAltPlanet`, `getLatLonAltPlanet`, `getLatLonRad`, `getXYZFromLatLonAlt'`, `getHeight`, `getAltitude`, `getElevation'`). No Obsolete shim. Callers migrate to `tryGet*`.

## Files to modify

### Wrapper

- `src/PRo3D.Base/CooTransformation.fs`
  - Add `ConventionKind`, `bodyConvention`, radii constants.
  - Add `latLonAltOnSphere` / `xyzFromLatLonAltOnSphere` (LATREC, one radius).
  - Add `latLonAltOnEllipsoid` / `xyzFromLatLonAltOnEllipsoid` (tri-axial, three radii).
  - Add `tryGet*` family dispatching via `bodyConvention`.
  - Rewrite `getUpVector` as above.
  - Add `getConvention`.
  - Delete `init = 0.0`, six `Log.line` sites, the nine total wrappers.

- `src/PRo3D.Base/SpiceInterfacing.fs:44` and `src/PRo3D.GIS/SpiceInterfacing.fs:43` — drop `printfn` warnings.

### `getUpVector` call-sites (7 files)

`getUpVector` stays total; most sites compile unchanged. Verify each does not rely on the prior failure-as-`-p.Normalized` quirk:

- `src/PRo3D.Viewer/Viewer/Viewer.fs`
- `src/PRo3D.Viewer/MapViewCameraController.fs`
- `src/PRo3D.Core/TransformationApp.fs`
- `src/PRo3D.Core/ScaleBarsApp.fs`
- `src/PRo3D.Lite/API.fs`
- `src/PRo3D.CorrelationPanels/CorrelationPanels/Log/GeologicalLogNuevo.fs`
- `src/PRo3D.Core/ReferenceSystem.fs`

### `getLatLonAlt / getXYZFromLatLonAlt / getAltitude / getElevation'` call-sites (16 files)

Each site now handles `Option`. On `None`:

| File | Handling on `None` |
|------|--------------------|
| `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` (~139–165) | Show `"conversion failed"`. |
| `src/PRo3D.Core/ReferenceSystem.fs` (~154) | Same. |
| `src/PRo3D.Core/Drawing/{Drawing-App,Drawing-Properties,DrawingUtilities,EllipseAnnotation}.fs` | Drawing suspended for this point; one log per session, not per frame. |
| `src/PRo3D.Viewer/Bookmarks.fs` | Skip lat/lon enrichment; xyz still stored. |
| `src/PRo3D.Viewer/Traverse/{WayPoints,Rover,RIMFAX}Traverse.fs` | Skip the relevant feature with a clear log. |
| `src/PRo3D.Base/Annotation/Exporters/GeoJSON.Export.fs` | Refuse export; single-line warning. |
| `src/PRo3D.Base/Annotation/AnnotationHelpers.fs` | Decide per helper. |
| `src/PRo3D.Viewer/RemoteApi.fs` | API returns failure instead of `(0, 360, 0)`. |
| `src/PRo3D.Core/Validator/HeightValidator-Model.fs` | Validator records the point as failed. |
| `src/PRo3D.CorrelationPanels/.../GeologicalLogNuevo.fs` | Skip the log entry. |

For Dimorphos specifically, most sites will *succeed* now (they used to fall through to zeros because of PGRREC). The `None` branches are for genuinely unsupported bodies (`NonPlanetary` strategy) or unexpected native errors.

## Verification

End-to-end:
1. `dotnet build src/PRo3D.Viewer/PRo3D.Viewer.fsproj` — compiles across all 22 caller files.
2. PRo3D with a Mars scene → status bar lat/lon/alt unchanged from current behaviour (PGRREC unchanged).
3. PRo3D with a Dimorphos scene:
   - Status bar shows real lat/lon and an altitude referenced to the mean-radius sphere (with `Spherical` default) or the true ellipsoid surface (if Dimorphos is switched to `Ellipsoidal`). **No** more `0 / 360 / 0 m`.
   - Optionally annotates `"spherical"` / `"ellipsoidal"` / `"planetographic"` next to the lat/lon readout.
   - `Aardvark.log` does not grow by more than a few kB per minute (was multiple GB / hour).
4. MapView on Dimorphos: camera orientation stable as you orbit — `up` is radial, `north` constructed from world-Z, numerically sound.
5. Reference System panel: works on Mars, works on Dimorphos, shows `"conversion failed"` for `NonPlanetary`.
6. Re-run `python src/Tests/spice_coverage_tests.py` — output unchanged (script bypasses PRo3D).

Unit tests (`src/Tests/`):
- Existing `HeraSpiceTests.fs` Didymos / Phobos tests remain green.
- The current `"latlonalt to xyz for dimorphos"` test (red today) is **rewritten** to exercise `CooTransformation.tryGetXYZFromLatLonAlt Planet.Dimorphos …` and assert `Some`.
- Add round-trip tests for both spherical and ellipsoidal helpers: any xyz → latlon → xyz returns the original xyz within 1e-9. For Ellipsoidal, additionally take a point on the Dimorphos tri-axial surface and expect altitude ≈ 0.

## Out of scope

- **Native log level.** Production `Init(..., 1, 2)` stays. The 100 GB log was `Aardvark.log` (F# `Log.line` output), fixed by removing those sites.
- **Synthesising POLE/PM for Dimorphos.** ESA explicitly designed Dimorphos to use the dynamic frame, not a PCK, because of post-DART tumbling. We honour that.
- **Loading a Dimorphos relative-orbit SPK** to unblock `DIMORPHOS_FIXED` / `DIMORPHOS_CK`. ESA's distribution decision.
- **`DIMORPHOS_CK` integration.** The CK-frame exists but requires a Flight-Dynamics attitude file we don't currently load. Separate item.
- **Sky-projection on bodies without IAU pole** (`Viewer.fs:1188`). Tracked separately.
- **The `getRotationTrafo` plumbing.** Already returns `Option`; only change is dropping the `printfn` warning.
