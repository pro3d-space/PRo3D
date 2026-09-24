# Multi-attribute profiles

A **profile** is an annotation exported as one row per point, carrying how far along the
line each point is and how high it sits. A **multi-attribute** profile adds, for every one
of those points, the values of the OPC's own data layers underneath it — elevation,
gravity, slope, potential, whatever the dataset ships — so the whole cross-section arrives
in a single table ready to plot.

There is no *multi-attribute profile* menu command. It is a combination of three settings
in one window, and this page walks the whole thing through on the HERA Dimorphos dataset.
For the reference description of every control, see [Annotation Export](AnnotationExport.md).

The screenshots and the excerpt at the bottom come from a real run against the HERA
Dimorphos OPC. The Playwright spec that performs exactly these steps and regenerates them
(`tests-ui/tests/annotation-profile-export.spec.ts`, run with `PRO3D_DOC_SHOTS=1`) lands
separately in PR #771.

---

## 1. Draw the line

Pick **Draw annotation** (the pencil) in the tool strip down the right edge of the render
view, then set *Annotation* to **Line** and the projection to **Sky**.

![The annotation toolbar: Line, Sky](images/multiAttributeProfile-2-toolbar.png)

**The projection is the setting that matters here.** With `Linear`, the two picked points
*are* the whole geometry: the annotation is a straight chord through space and a profile of
it has exactly two rows. `Sky` samples the span between them by shooting rays along the
scene's up-vector into the surface, so the line is draped over the terrain it crosses — and
those draped points are what the profile is made of. `Viewpoint` drapes it too, but samples
along rays from the camera, so the result depends on where you were standing.

Now **ctrl+click** two points on the surface. A `Line` completes itself on the second
point; there is nothing to confirm.

![A sky-projected line annotation across Dimorphos](images/multiAttributeProfile-3-annotation.png)

> Each pick ray-casts the surface's KdTrees, and the first pick on a patch loads its tree
> from disk — seconds, during which PRo3D does not react. That is loading, not a hang.
> If picking does nothing at all, the surface has no KdTrees; build them with `opc-tool`.

The annotation stays selected after drawing, which is what the *Selected* scope below picks
up.

## 2. Export it

*Annotations → Export…*, then choose the **Profile** preset. It sets everything a profile
needs in one go: CSV, **one record per point**, scope *Selected*, sampled segment points
on, and every point attribute ticked.

![The export window under the Profile preset](images/multiAttributeProfile-4-export-window.png)

Then open **Point attributes** and tick **Surface properties**. This is the step that makes
the profile multi-attribute, and no preset sets it, because it is the expensive one.

![Surface properties, at the bottom of the point attributes](images/multiAttributeProfile-5-surface-properties.png)

Ticking it switches *Preset* back to **Custom** — expected, and shown in the screenshots
above. A preset only pre-fills the controls; nothing is locked, and changing anything
afterwards moves the label to *Custom* without undoing what the preset set.

Press **Export…** and choose a file.

> **It re-picks every exported point.** Reading the layers means casting a ray per point
> and reading each per-vertex layer there, so expect seconds rather than milliseconds, and PRo3D
> is unresponsive while it runs. Cost is dominated by how many *patches* the line crosses,
> not how many points it has.

## 3. What comes out

One header row, then one row per sampled point, in the order the annotation runs. For the
Dimorphos line above: **44 rows, 27 columns.**

```csv
key,text,surfaceName,pointIndex,segmentIndex,x,y,z,lat,lon,alt,body,latLonAltSource,stepLength,segmentLength,distance,groundDistance,surface_DRACO_1,surface_DRACO_2,surface_Earth,surface_Elevation,surface_Gravity,surface_LonLatRad,surface_Magnitude,surface_Normal,surface_Potential,surface_Slope
2a6a5cb6…,,Dimorphos,0,0,-17.86673793028778,-83.17453539188264,8.064056598761464,5.414950318728199,102.12348507179354,85.4532191947919,Dimorphos,spice_reclat,0,43.38934354414176,0,0,157.96401345288248,155.21539467936208;155.21539467936208;155.21539467936208,0.00392156862745098;0.37254901960784315;0.6,62.971735173434816,8.794816374747565E-06;4.441696625325292E-05;-7.098137259917534E-06,257.87650697733926;5.414937103912553;85.45338172285355,4.5847124014892136E-05,-0.3470082115851766;-0.8844589321447753;0.2752987988112416,-0.0030084808771983686,13.786625029890022
2a6a5cb6…,,Dimorphos,1,0,-16.963069905911592,-83.63249126391267,8.112347803657125,5.430458860470855,101.46568665380471,85.72018153578988,Dimorphos,spice_reclat,1.0142344499557387,43.38934354414176,1.0142344499557387,0,174.88669041037033,176.41363975150682;176.41363975150682;176.41363975150682,0.00392156862745098;0.37254901960784315;0.6,62.67786205413004,8.334434013093306E-06;4.44882343295229E-05;-6.99067052039317E-06,258.534295085002;5.430451137958959;85.72032311675297,4.581414467630833E-05,-0.3297068615419235;-0.8955739519503707;0.2456696414044665,-0.0030240257145326766,12.60507874090586
2a6a5cb6…,,Dimorphos,2,0,-16.01470911299567,-83.91583556914533,8.143737714305146,5.445328409204593,100.80455051589888,85.81759045324624,Dimorphos,spice_reclat,0.9902815333723957,43.38934354414176,2.0045159833281345,0,211.30923702864794,211.13944617644577;211.13944617644577;211.13944617644577,0.00392156862745098;0.37254901960784315;0.6,62.18989492465736,7.790576081251199E-06;4.4597331585857994E-05;-6.9919025941747054E-06,259.1954417980158;5.445327680502811;85.81772472765563,4.582797199340736E-05,-0.23379989165803897;-0.9131304815661341;0.23711076760287453,-0.00304556615429828,12.643903232664744
```

*(`key` shortened for width. The real file carries the full GUID on every row, and it is a
fresh one per annotation, so re-running this will not reproduce that column.)*

### The columns

| Column | What it holds here |
|---|---|
| `key`, `text`, `surfaceName` | the annotation's identity, repeated on every one of its rows |
| `pointIndex`, `segmentIndex` | position along the annotation, and which picked-to-picked stretch it belongs to |
| `x, y, z` | body-fixed metres |
| `lat, lon, alt` | `Coordinates: Both` writes both sets. On Dimorphos `alt` is a **radial distance from the body centre** (~85 m), not a height above a spheroid — see below |
| `body`, `latLonAltSource` | provenance: `Dimorphos` / `spice_reclat`, recorded per row |
| | *Note:* the Profile preset leaves the longitude convention on **Flipped** (`360 − lon`), so `lon` and the raw `surface_LonLatRad` layer disagree by design — 102.12 against 257.88 in the excerpt above. Neither is broken. |
| `stepLength` | distance to the previous point (~1 m here — the sampling amount) |
| `segmentLength` | total length of the segment, repeated on each of its rows (43.39 m) |
| `distance` | running length from the first point, through 3D space — **the x-axis of the profile** |
| `groundDistance` | running length with the height removed. **0 on Dimorphos — see the warning below** |
| `surface_<layer>` | one per OPC layer sampled under the point, alphabetically |

The ten `surface_` columns are the whole point: `surface_Elevation`, `surface_Gravity`,
`surface_Slope`, `surface_Potential`, `surface_Magnitude`, `surface_LonLatRad`,
`surface_Normal`, `surface_DRACO_1`, `surface_DRACO_2` and `surface_Earth` — every layer
this OPC ships, sampled at each point of the line.

Multi-channel layers stay in **one** cell, semicolon-separated. In this dataset
`surface_DRACO_2`, `surface_Gravity`, `surface_LonLatRad` and
`surface_Normal` have three components, while `surface_DRACO_1`, `surface_Elevation`,
`surface_Magnitude`, `surface_Potential` and `surface_Slope` are single values. Splitting
them would make the column count depend on which layer a point landed on.

### ⚠ `groundDistance` is 0 on Dimorphos

Every row reads `groundDistance = 0`, and that is wrong rather than merely absent — a
plausible-looking number that silently collapses the x-axis if you plot against it. **Use
`distance` on this body.**

The cause is a mismatch of what `alt` means. Removing the height works by setting the
altitude to 0 and transforming back to cartesian. That is right for a **planetographic**
body, where altitude is a height above the spheroid. Dimorphos is tri-axial with no PCK
rotation pole, so PRo3D gives it the **spherical** convention, where altitude is the
*radial distance from the body centre* — and altitude 0 is therefore the body centre
itself. Every point flattens onto the same spot, consecutive flattened points coincide, and
nothing accumulates.

This affects every spherical-convention body. On the planetographic ones — Mars, Earth,
Moon, Phobos, Deimos, Didymos — `groundDistance` measures a real horizontal run, and the
difference from `distance` is the vertical climb.

`annotation-profile-export.spec.ts` asserts this zero as the current behaviour, so fixing
it will trip the test and bring you back to this page.

## Plotting it

`distance` against `surface_<layer>` is the usual topographic-profile shape: the
cross-section of the line, coloured by whatever the OPC measured there.

```python
import pandas as pd
df = pd.read_csv("dimorphos-profile.csv")
df.plot(x="distance", y=["surface_Elevation", "surface_Slope"])
```

The single-valued layers — `surface_Elevation`, `surface_Slope`, `surface_Potential`,
`surface_Magnitude` and `surface_DRACO_1` — read straight into a plot. The rest are
semicolon-separated vectors and need splitting first; `surface_Gravity` is one of them.

## If something is missing

| Symptom | Cause |
|---|---|
| Only two rows | The annotation is `Linear` — it has no draped points. Redraw with *Sky*. |
| No `surface_` columns at all | Nothing was sampled: *Surface properties* is off, the granularity is per-annotation, or the surface is hidden/inactive — only visible, active OPC surfaces are picked, and mesh surfaces have no layers. The schema only contains columns some row produced. |
| `surface_` columns present but empty on **some** rows | Those points missed the surface — the line runs off it, or onto a patch without that layer. |
| `lat`/`lon`/`alt` empty | No reference body: the scene is `None`, `JPL` or `ENU`. Set it under *Coordinate System*. |
| Nothing written, window stays open with a warning | The scope matched no annotation — *Selected* needs one selected. The warning says which. |

## See also

- [Annotation Export](AnnotationExport.md) — every control of the export window
- [Per-Vertex Attribute Layers](VertexAttributes.md) — what the `.aara` layers are
- [KdTrees](KdTrees.md) — the picking structures the sampling depends on
