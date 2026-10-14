# Supporting profiles (spec draft)

Automatically generate **profiles** — `Line` annotations with `Projection.Sky` — from an
existing polyline or ellipse annotation.

Original idea (verbatim notes):

> (a) for polyline annotations, this feature automatically creates profiles perpendicular to the
> line directions of the polyline. given a custom "distance" each multiples of this distance pro3d
> automatically creates a line annotation with sky projection and the derived sampling distance
> from the original polyline.
> (b) this one extends ellipses. given a finished ellipse, it automatically creates profiles
> following primary directions on the ellipse: N–S, E–W, NE–SW, NW–SE. Two modes: major axis
> defines bearing, or real compass north (only with a planet selected).

## Common to both

- **Output**: plain `Geometry.Line` + `Projection.Sky` annotations, sampled like any hand-drawn
  sky line ("between two points the space is sampled by shooting rays along the scene's up").
- **Sampling distance**: the drawing tool's *current* sampling amount/unit at generation time
  (annotations do not store their own sampling distance — it is a `DrawingModel` setting).
  Shown and editable in the generate dialog.
- **"Horizontal" / "perpendicular"** is measured in the local tangent plane ⟂ to the sky
  direction (`ReferenceSystem.bodyAwareSky`: reference up on planets, +Z on small bodies).
- **Endpoints** are dropped onto the surface along the sky direction, exactly like picked points.
- **Profile link (phase 1)**: each generated line stores where it came from, fixed at generation
  (like `majorAxisAzimuth` is fixed at ellipse construction; editing the profile afterwards does
  not update it):
  ```fsharp
  type ProfileLink = {
      parent    : Guid           // key of the ellipse / polyline it was generated from
      index     : int            // 0, 1, 2, ...
      direction : string         // "N-S", "NE-SW", "E-W", "NW-SE" | "major", "major+45", "minor", "major+135" | "" (polyline)
      azimuth   : float          // deg, bearing from the profile's centre to its start end (N-S = 0, NW-SE = 315)
      station   : Option<float>  // m along the parent polyline; None for ellipses
  }
  // Annotation: profileOf : Option<ProfileLink>
  ```
  Persisted under a new JSON key `profileOf`, read with `Json.tryRead` -> `None` (no version
  bump; older releases skip it). A deleted parent leaves a dangling `parent`; harmless.
- **Start end**: every profile starts at its first-named end (N-S at N, NE-SW at NE, ...; major-axis
  mode: the end of the axis lying more to the north). Polyline profiles start on the left, looking
  along the polyline. So `distance` = 0 is always that end.
- **Target group**: the active group.
- **Naming**: `p<parentName>_<n>` (e.g. `pCraterA_03`), `n` counting from 0 along the line /
  through the 4 ellipse directions.
- **All or nothing**: if any profile endpoint misses the surface, generation fails as a whole —
  no profiles are created and the user gets an error saying which one failed.
- **Later (phase 2)**: reverse direction — show the profiles' metrics on the parent
  (ellipse / polyline) it was generated from. Regeneration after parent edits is out of scope.

## (a) Polyline → perpendicular profiles

Inputs: spacing `d`, total profile length `L` (symmetric: `L/2` to each side).

- Walk along the polyline; at `0, d, 2d, 3d, …` metres of 3D arc length insert a station, plus
  one at the polyline's end. (Start/end profiles are easy to drop again if users don't want them.)
  The distance carries straight through bends — vertices are not special.
- Each profile goes through its station, perpendicular to the segment the station lies on, in the
  horizontal plane: `t` = segment direction, `n = normalize(up × t)`, endpoints `station ± n·L/2`.
- Degenerate case only: a station landing exactly on a vertex uses the outgoing segment.

## (b) Ellipse → 4 profiles through the centre

Directions, 45° apart, through the ellipse centre:

| Mode | 0° | 45° | 90° | 135° |
|---|---|---|---|---|
| Compass (planet set) | N–S | NE–SW | E–W | NW–SE |
| Major axis | major | major+45° | minor | major+135° |

- Compass mode: north = +latitude in the ellipse's geographic frame (`geographicalEllipse`);
  disabled when no planet / reference system is available.
- **Length: exactly rim to rim** — the line through the centre is intersected with the ellipse
  outline, so this also works for asymmetric ellipses (`geographicalEllipseAssym`).
- Works for all ellipse geometries: `Ellipse`, `AxisEllipse`, `Axis4PEllipse`.

## Export

- New annotation field **`parentKey`** (GUID of the annotation a profile was generated from;
  empty for ordinary annotations), selectable in every preset's field list and ticked in
  *Profile*, so polyline-generated profiles export their link too.
- New preset **Craters**: one export writes **two CSV files**:
  - `<file>.csv` (the name picked in the save dialog) — the crater ellipses, exactly the *Boulders (ellipses)* rows and
    columns (semi-axes, azimuth, centre, ellipse statistics);
  - `<file>_profiles.csv` — every profile whose `parentKey` is one of those ellipses, in the
    *Profile* layout (one row per sampled point; `distance`, `alt`; `surface_<layer>` when
    *Surface properties in <file>_profiles.csv* is ticked, off by default as in *Profile*) plus
    `parentKey`. Join the files on `parentKey` = `key`.
  - Which ellipses: same scope/filter rules as *Boulders*; both files use the same longitude
    convention so they line up.

  - Profile columns (same on every row of one profile, from `ProfileLink`): `parentKey`,
    `profileIndex`, `profileDirection` (English compass labels), `profileAzimuth`,
    `profileStation`. `segmentIndex` keeps its documented meaning (always 0 for a two-point
    profile). Reconstruct: group by `parentKey` -> crater, then by `profileDirection` /
    `profileIndex` -> one profile, ordered by `pointIndex`.

```csv
# <file>.csv
key,text,surfaceName,groupPath,semiMajorAxis,semiMinorAxis,majorAxisAzimuth,x,y,z,lat,lon,alt,…
88f3…,CraterA,…

# <file>_profiles.csv
key,text,parentKey,profileIndex,profileDirection,profileAzimuth,profileStation,pointIndex,segmentIndex,x,y,z,lat,lon,alt,…,distance,groundDistance
1a2b…,pCraterA_0,88f3…,0,N-S,0,,0,0,…
1a2b…,pCraterA_0,88f3…,0,N-S,0,,1,0,…
3c4d…,pCraterA_1,88f3…,1,NE-SW,45,,0,0,…
```

- New preset **Lineaments** (added later, same concept for polylines): `<file>.csv` with one row
  per line/polyline (type filter *lines only*, which excludes generated profiles; `key`, `text`,
  `surfaceName`, `length`, `wayLength`, `bearing`, `groupPath`) and `<file>_profiles.csv` as for
  Craters.

- **Placement** (added later): BySpacing (every *d* m) or ByCount (*N* equally spaced over the
  whole polyline), each with *At start* / *At end*; with ByCount an excluded end still counts as
  a station, so gaps stay equal (table in docs/SupportingProfiles.md).
- **Toolbar** (added later): *Lineament profiles* (Polyline) and *Crater profiles* (ellipses)
  checkboxes in the drawing toolbar generate the profiles as soon as the annotation is finished,
  with the panel's settings (shown next to the ticked checkbox). Separate undo step; outcome as
  a short notice.

## Success criteria

1. **Polyline**: a Sky polyline of length `S` with spacing `d` yields `floor(S/d) + 1` profiles
   (plus one at the end unless `S` is a multiple of `d`). Each is a two-point Sky `Line`, centred on
   the polyline within sampling tolerance, of horizontal length `L`, perpendicular (horizontal
   angle 90° ± 1°) to the segment its station lies on.
2. **Ellipse**: 4 profiles through the stored centre, each ending on the outline (endpoints
   are outline points within one outline sample spacing). Compass mode: azimuths 0/45/90/135 ± 1°,
   start ends at N/NE/E/NW. Axis mode: profile 0 along the major axis.
3. **All or nothing**: if any endpoint misses the surface, the annotation tree is unchanged
   and an error is shown.
4. **Link**: every profile carries `profileOf` with the parent's key; it survives save → load,
   and a scene without the key loads with `None`.
5. **Craters export**: one export writes `<file>.csv` (= the Boulders rows) and
   `<file stem>_profiles.csv`; every profile row has `parentKey` equal to a `key` of the craters
   file, the 4 `profileDirection` labels per crater, `distance` starting at 0 at the first-named end.
6. Undo removes all generated profiles in one step.

## Testing

- **Expecto** (pure geometry, synthetic flat/tilted surface sampler): station placement along
  arc length incl. through bends, perpendicularity, ellipse rim intersection incl. asymmetric
  outline, compass vs axis directions, all-or-nothing on a sampler with a hole, JSON round trip
  of `profileOf` and loading without it, Craters export record building (`parentKey` join,
  profile columns).
- **Playwright** (`tests-ui/tests/supporting-profiles.spec.ts`, Dimorphos OPC, Dimorphos as the
  reference body): draw an AxisEllipse and a Sky polyline through the UI, generate profiles from
  each via the annotation properties, check counts on the annotations page, export with the
  *Craters* preset and check both CSVs (rows, `parentKey` join, directions, `distance` from 0,
  values varying along each profile). Screenshots to `tests-ui/artifacts/supporting-profiles/`.

## Code

| File | Role |
|---|---|
| `src/PRo3D.Core/Drawing/SupportingProfiles.fs` | the geometry: stations, perpendiculars, compass and axis directions, rim intersection |
| `src/PRo3D.Core/Drawing/Drawing-App.fs` (`GenerateSupportingProfiles`, `autoProfileParent`) | turns the profiles into annotations, all or nothing, one undo step; which finished annotation the toolbar generates for |
| `src/PRo3D.Viewer/Viewer/Viewer.fs` (`skyProjection`, `withAutoProfiles`) | the sky ray, and generating right after a polyline or ellipse is finished |
| `src/PRo3D.Core/Drawing/Drawing.UI.fs` (`viewSupportingProfiles`, toolbar) | the panel and the toolbar checkboxes |
| `src/PRo3D.Base/Annotation/Annotation-Model.fs` (`ProfileLink`) | the stored link, key `profileOf` |
| `src/PRo3D.Base/Annotation/Exporters/` | profile columns, *Craters* and *Lineaments* presets, the *lines only* filter, the profiles file |
| `src/PRo3D.Viewer/Viewer/AnnotationExportViewer.fs` | writing the second file |
| `src/Tests/Features/Section22_SupportingProfiles.fs` | exact geometry on a synthetic plane, the drawing app, persistence, export |
| `tests-ui/tests/supporting-profiles.spec.ts` | the whole workflow on the Dimorphos OPC through the UI, both exports, the toolbar; produces the doc's screenshots |
