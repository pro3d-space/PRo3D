### Traverse Import (GeoJSON)

Traverses are imported from a GeoJSON `FeatureCollection` (*Import → Traverse*). The collection's
`properties.type` selects the parser:

| `type` | Geometry | Mandatory feature properties |
| --- | --- | --- |
| `waypoints` | `Point` | `sol` (int), `site` (int), `RMC` (string), `yaw`, `pitch`, `roll` (numbers); `elev_geoid` for 2D points |
| `rover` | `LineString` / `MultiLineString` | `sol` (int), `fromRMC`, `toRMC` (strings), `length`, `SCLK_START`, `SCLK_END` (numbers) |
| `rimfax` | `LineString` / `MultiLineString` | see [RIMFAXTraverse.md](RIMFAXTraverse.md) |

If `properties` is present, all of `type`, `generatedBy`, `segmentCount` (int) and `timestamp` are
required, otherwise the file fails to load. Without `properties` the file only loads if the first
feature is a `Point`; it is then read as a `waypoints` traverse. A feature with a missing or
mistyped mandatory property is skipped with a warning.

Optional `waypoints` properties: `tilt`, `dist_m`, `dist_total_m` (or `dist_total`), `Note` (or
`note`). `yaw` is the heading clockwise from north in degrees.

### Coordinates and planet

Coordinates are GeoJSON `[lon, lat]` or `[lon, lat, alt]` in degrees, longitude **east-positive**.
2D waypoints take their altitude from `elev_geoid`; 2D rover/RIMFAX lines are placed at altitude 0.

Lat/lon is converted on the **scene's planet** (`referenceSystem.planet`):

- Mars, Phobos, Deimos: the longitude is flipped to SPICE's west-positive planetographic longitude.
- Earth, Moon (and other bodies): the longitude is passed through east-positive.
- Scenes without a geographic frame (`None` / `JPL` / `ENU`) fall back to Mars, the historical
  behaviour for M2020 data.

The positions are computed with the same native conversion used for the scene's geographic
readouts, so the altitude must be in the same height reference as the surfaces. For Earth scenes
built from orthometric DEMs, use orthometric heights (not GNSS ellipsoidal heights).

Imported positions are stored as cartesian coordinates in the scene, so reloading a saved scene
does not re-run the conversion.

Code: `src/PRo3D.Viewer/TraverseApp.fs` (`parseTraverse`), `src/PRo3D.Viewer/Traverse/`
(`TraverseUtilities.tryGeoJsonToXyz`, per-type parsers).
