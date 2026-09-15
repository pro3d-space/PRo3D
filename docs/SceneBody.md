# Scene Body

A scene is expressed in one **scene body**: the planet, moon or asteroid at its origin. There
are two places to set it, and they are the same setting (issue
[#758](https://github.com/pro3d-space/PRo3D/issues/758)):

- the **planet** dropdown in the top bar (`ReferenceSystem.planet`), and
- the **Observed body** in the GIS view's *Current Observation Settings*.

Choosing one sets the other. For a single-body scene that is all the setup there is: pick
e.g. *Dimorphos*, load a SPICE kernel, add an image — image projection, sun lighting, MapView,
lat/lon and the measurements all work, without assigning anything per surface.

| File | Role |
|------|------|
| [`src/PRo3D.Base/GisModels.fs`](../src/PRo3D.Base/GisModels.fs) | `SceneBody`: planet ↔ SPICE body + fixed frame; `transformBody`'s identity shortcut |
| [`src/PRo3D.GIS/GisApp.fs`](../src/PRo3D.GIS/GisApp.fs) | surface inheritance (`getSpiceReferenceSystem`), `withScenePlanet`, `scenePlanet` |
| [`src/PRo3D.Core/GisApp/ObservationInfo.fs`](../src/PRo3D.Core/GisApp/ObservationInfo.fs) | observed body snaps the frame; the frame row of the GIS view |
| [`src/PRo3D.Viewer/SceneBodySync.fs`](../src/PRo3D.Viewer/SceneBodySync.fs) | the only writers of planet and observation; scene-load reconciliation |

## Why one setting

Everything the planet drives — up/north, lat/lon/altitude, bearing and dip/strike, the sky
projection, MapView's pole (+Z) and radius — reads **world coordinates as the planet's
body-fixed coordinates**. The GIS places each surface with
`transformBody(surface body, surface frame → observed body, observation frame)`, so world is
"the observed body's centre, in the observation frame's axes".

The two agree exactly when the GIS observes the body **in its own fixed frame**. Then a surface
of the scene body is placed at the identity, and every planet-based computation is correct as
written. With two independent settings they could disagree — e.g. a scene observed in `J2000`
rotated the surfaces into J2000 while the planet still read them as body-fixed, which showed as
a broken up vector and a wrongly oriented MapView. With one setting that cannot happen.

## Bodies and frames

| Planet | SPICE body | Fixed frame |
|--------|------------|-------------|
| Mars | `Mars` | `IAU_MARS` |
| Earth | `Earth` | `IAU_EARTH` |
| Moon | `Moon` | `IAU_MOON` |
| Phobos | `Phobos` | `IAU_PHOBOS` |
| Deimos | `deimos` | `IAU_DEIMOS` |
| Didymos | `Didymos` | `DIDYMOS_FIXED` |
| Dimorphos | `Dimorphos` | `DIMORPHOS_FIXED` |
| None, ENU, JPL | — | — (no body: the GIS observation is cleared) |

SPICE names are compared ignoring case (`DIMORPHOS` = `Dimorphos`).

## Behaviour

**Picking the planet** (top bar) sets the GIS observed body and its fixed frame. A planet that
is no body clears the GIS observation.

**Picking the observed body** (GIS view) sets the reference frame to that body's fixed frame and
the planet to that body. The frame is shown, not offered as a choice. Observing a body PRo3D has
no planet for (e.g. the HERA spacecraft) sets the planet to `None` and keeps the frame dropdown.

**Surfaces** without any GIS assignment belong to the scene body (the GIS dropdowns show
"Scene body (…)"). An explicit assignment still wins — that is how a Didymos surface is placed
in a Dimorphos scene. A half assignment (body without frame) stays unplaced, as before.

**Sequenced bookmarks** contribute their observation *time* and *camera source body* only. The
observed body and frame they carry are ignored, interactively and in `PRo3D.Snapshots` alike: a
tour cannot switch the scene body. A bookmark with a camera source looks from it at the scene
body; without one its own camera stands and only the time (sun) moves.

**First import** does not second-guess a body the GIS already observes, and the radius-based
inference no longer resets a body it cannot recognise (anything but Mars and Earth) to `None`.

**Loading a scene** whose GIS observation is body-fixed fills in the planet (scenes set up in the
GIS view only used to keep `None`, which disabled MapView). This recomputes the annotation
measurements, as any planet change does.

## Scenes in another frame

A scene saved with the observation in another frame — e.g. observed body Dimorphos, frame
`J2000` — **loads unchanged**: surfaces render where they did, unassigned surfaces are not
placed, and the planet stays as saved. The GIS view names the problem and offers a **Use
DIMORPHOS_FIXED** button. Switching rotates the world from J2000 into the body-fixed frame;
saved cameras, bookmarks and annotations are *not* converted, so they appear rotated by the
body's orientation at the scene time and need re-saving.

## Not yet: a freely chosen scene frame

For now a known body is always observed in its fixed frame. Letting the scene itself live in
another frame (J2000 for multi-body or fly-by scenes) is planned: the reference-frame field is
kept in the scene file for exactly that, and unlocking it needs no format change. What it needs
is one time-dependent body-fixed → world transform that every planet-based feature and every
stored point (annotations, reference system, pivots, bookmarks) goes through — see #758.

## Tests

`src/Tests/Features/Section12_GisView.fs`, TC-12.4.
