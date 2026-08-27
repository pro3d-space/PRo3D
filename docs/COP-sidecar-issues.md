# HERA COP synthetic-image sidecars — issues for the data generators

Report on the `*.mbi.json` sidecars in the COP simulation delivery
(`COP/COP/<date>/HERA_AFC_*_COP.png` + `.mbi.json`, seen 2026-08-27, generator
comment "JR (c) 2026", metakernel note `hera_plan.tm 2026-08-19`). PRo3D now
tolerates all three issues via fallbacks (with log warnings), so the data works
as delivered — but the sidecars are out of spec and other MBI consumers will
reject them. Please forward to whoever generates these.

## 1. `DATE-OBS` does not contain the observation time

```json
"DATE-OBS": { "value": "AFC1-Synthetic", "comment": "Observation time UTC" }
```

`DATE-OBS` is the authoritative observation timestamp; here it carries a
product id. The actual (simulated) observation time is only in `DATE`
("File creation time UTC") and in the file name.

*PRo3D fallback:* when `DATE-OBS` doesn't parse as a date, the `DATE` header is
used instead.

*Fix:* write the simulated observation time into `DATE-OBS`
(`2027-02-05T01:00:00.000`-style, as `DATE` already does).

## 2. `bands[].file_path` is empty

```json
"bands": [ { "label": "", "description": "", "exposure": 0.0, "index": 0, "file_path": "" } ]
```

The band `file_path` is how an MBI sidecar declares which image file(s) it
describes — sidecar file naming is explicitly *not* a reliable association
(ASPECT exports share one sidecar across many differently-named band images).
With every `file_path` empty, the sidecar cannot be matched to its image by
content.

*PRo3D fallback:* when a sidecar declares no band file paths, the
`<image base>.mbi.json` naming convention is used.

*Fix:* set `file_path` to the image file name, e.g.
`"HERA_AFC_0001_20270205_010000_COP.png"`.

## 3. `SPICE_MK` value is empty — kernel name hides in the comment

```json
"SPICE_MK": { "value": "", "comment": "SPICE metakernel hera_plan.tm 2026-08-19" }
```

The metakernel the sidecar was generated against belongs in the *value*;
comments are not machine-readable. PRo3D uses `SPICE_MK` to verify/load the
matching kernel for an image ("Load Spice and Time").

*Fix:* `"value": "hera_plan.tm"` (or the fully qualified kernel id).

## 4. Position vectors are metres, but the headers say `[km]`

```json
"TRG_POSX": { "value": -11082.237, "comment": "Target position vector X [km]" },
"TRG_DIST": { "value": 14846.226,  "comment": "Target distance [AU]" }
```

Confirmed against the delivery's own `PRo3D.json` ground truth: the snapshot
camera sits at `[-11082, -6034, -7822]` **metres** (|r| = 14.8 km, a plausible
COP standoff), exactly the sidecar's `TRG_POS`. Likewise `SUN_POS` read as
metres is 1.09 AU (correct for Didymos, Feb 2027); read as km it would be
1090 AU. (`TRG_DIST`'s "[AU]" comment is a third unit claim on the same
number.) Real HERA/Mars sidecars use km here, so consumers that trust the
header mis-scale these by 1000×.

*PRo3D fallback:* the unit is auto-detected from the sun distance (as km it
must land near 1 AU) and all positions are normalized to km, with a warning.

*Fix:* write km as declared, or declare the actual unit.

## Minor observations (no action strictly needed)

- `OBJECT` ("Observation Target ID") is empty; `TARGET` is filled ("Didymos")
  and is what PRo3D uses for the projection target body.
- `EARTPOSX/Y/Z` are all `0.0` — accepted, but earth-position-derived
  displays will be meaningless.
- The PNGs are 8-bit RGB. Fine for projection testing; if physical values
  matter later, single-band float TIFF + the statistics sidecar (as in the
  Mars deliveries) is the richer format.
