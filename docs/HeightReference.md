# Heights: what PRo3D's "altitude" is

PRo3D shows a height next to every latitude/longitude: in the camera overlay, the
*Coordinate System* panel, the *Under Cursor* readout, the annotation results, Color by
Category, the cross-section curtain and the annotation export. Until #866 all of these were
labelled "Altitude", although the number means different things on different bodies, and
none of them is an elevation in the geodetic sense.

## PRo3D computes no gravity potential

Every height PRo3D derives is **geometric**: measured against a reference spheroid or
against the body centre. PRo3D has no gravity model, no density, no rotation term, and no
equipotential surface (geoid / areoid). So:

- PRo3D's value is **not** "elevation above an equipotential surface", and the UI does not
  call it that.
- Potential-based values come **only from the data**: OPC attribute layers such as
  `Elevation`, `Potential`, `Gravity`, `Magnitude` and `Slope`. For HERA these are derived
  from the DART shape models. PRo3D only interpolates them (see
  [VertexAttributes.md](VertexAttributes.md)). Which gravity model, density, rotation and
  level 0 they use is defined by the producer of those shape models, not by PRo3D.

## What the value is, per body

The convention comes from `CooTransformation.getConvention`. The UI labels and tooltips
come from `CooTransformation.HeightReference`, and the text there matches this table.

| Body | Routine | Label in the UI | Meaning |
|---|---|---|---|
| Mars, Earth, Moon, Phobos, Deimos, Didymos | SPICE `recpgr` (planetographic) | **Height above spheroid** | Height above the reference spheroid of the body, measured along the spheroid normal. The spheroid is the equatorial and polar radius from the loaded SPICE PCK (by default `pck00010.tpc`; Mars: 3396.19 / 3376.20 km). 0 m is on the spheroid, not on the terrain. On Mars this is **not** MOLA areoid height. Tri-axial bodies are approximated by a spheroid. |
| Dimorphos | SPICE `reclat` (planetocentric) | **Radius from centre** | Distance from the body centre. Not a height above any surface: on the ground it is the local radius (about 77 m). Dimorphos has no PCK rotation model and is tri-axial, so `recpgr` is invalid there (see the comment on `getConvention`). |
| (none, available) | ray to tri-axial ellipsoid | **Height above ellipsoid** | Height above a tri-axial ellipsoid, measured radially. Implemented, but no body uses it today. |
| No body (`Planet.None`, `JPL`, `ENU`) | – | **Height along up** | The coordinate along the reference system's up vector, from the scene origin. |

Derived annotation values use the same per-point quantity:

| UI label | Export column | Meaning |
|---|---|---|
| Max - min height (*radius* on Dimorphos) / Height range | `height` | largest minus smallest per-point value |
| Last - first height / Height delta | `heightDelta` | \|last - first\| |
| Avg height (*radius*) | `avgAltitude` | mean of the per-point values |

The export column ids and the persisted `avgAltitude` key keep their old names. Renaming
them would break existing readers and scenes.

### "Vertical" is the reference system's up

Slope, dip and strike, vertical/horizontal distance and true/vertical thickness take the
reference system's up (radial, or the spheroid normal) as vertical, **not** the local
direction of gravity. On Mars the two differ by arcseconds. On small, fast-rotating bodies
(Didymos, 2.26 h) they can differ a lot, so an annotation's `slope` and the OPC `Slope`
layer can disagree on the same spot. A gravity-based up is tracked in #867.

## Exports

- **`alt`** (CSV, GeoJSON): its meaning depends on the per-row `latLonAltSource`:
  - `spice_recpgr`: height above spheroid
  - `spice_reclat`: radius from centre
  - `aara_file`: the radius stored in the OPC's `LonLatRad` layer, **on every body**,
    including Didymos, where SPICE would give a spheroid height
- **GeoJSON** exports carry a collection-level `properties.altDefinition` that says this in
  words, next to `planet`. The remote API's annotation GeoJSON carries the same.
- **`surface_Elevation`** and the other `surface_*` columns are the data's own values (see
  above). They are the potential-based ones.
- **`pro3d-tool unproject`**: `alt_m` follows the SPICE row of the table above;
  `Elevation` / `LonLatRad_z` are the shape model's values.

## Local frames

Before #866, the height in a local frame was computed as the component-wise product
`p * up`. The overlay read its `.Z`, using the **camera's** up, so the value changed when the
view tilted. The annotation results read its `.Length`, which folded negative heights onto
positive ones. Both now use `p · up` with the reference system's up.
