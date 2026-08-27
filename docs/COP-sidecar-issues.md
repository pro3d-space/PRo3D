# HERA COP synthetic-image sidecars — issues for the data generators

Report on the `*.mbi.json` sidecars in the COP simulation delivery
(`COP/COP/<date>/HERA_AFC_*_COP.png` + `.mbi.json`, seen 2026-08-27, generator
comment "JR (c) 2026", metakernel note `hera_plan.tm 2026-08-19`). Please
forward to whoever generates these.

**Issues 1–4 are tolerated** by PRo3D via fallbacks (with log warnings), so the
images import and display — but the sidecars are out of spec and other MBI
consumers will reject them.

**Issues 5–7 are not tolerable and must be fixed in the data.** They are the
reason the projected images do not sit on the terrain: together they describe a
camera roughly 100° away from where the picture was taken, so the projection
lands nowhere near the body. There is no safe fallback — the sidecar's fields
are self-consistent under the wrong convention, so nothing in the file reveals
which reading was meant. See "Pointing" below for the evidence and for a
correctly written reference sidecar.

## 1. `DATE-OBS` does not contain the observation time

```json
"DATE-OBS": { "value": "AFC1-Synthetic", "comment": "Observation time UTC" }
```

`DATE-OBS` is the authoritative observation timestamp; here it carries a
product id. Worse, `DATE` is **the same value in every sidecar of the whole
delivery** (`2027-02-05T01:00:00.000`, the sequence start — verified across
dates), so it cannot stand in for the observation time either. The only
per-image time in the delivery is the **file-name timestamp**
(`HERA_AFC_2317_20270301_040000_COP` → 2027-03-01T04:00:00Z).

*PRo3D fallback:* when `DATE-OBS` doesn't parse as a date, the file-name
timestamp (`_yyyyMMdd_HHmmss_`) is used; `DATE` is the last resort.

*Fix:* write each image's simulated observation time into `DATE-OBS`
(ISO style, as `DATE` already is), and make `DATE` the actual per-file
creation time rather than a constant.

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

## Pointing — issues 5, 6 and 7

These three are why the projected images do not land on the body. They were
found by comparing the sidecars against the delivery's **own** ground truth
(`COP/PRo3D.json`, which carries a per-image `location` / `forward` / `up`) and
against the three real HERA sidecars in PRo3D's test fixtures (AFC2 and HSH from
the Mars flyby, ASPECT at Didymos).

The convention PRo3D reads — and which all three real deliveries follow — is:

| field | meaning |
|---|---|
| `SC_QUAT0..3` | `(w, x, y, z)` of the quaternion whose rotation matrix takes vectors from the **spacecraft frame to J2000** |
| `TRG_POSX/Y/Z` | **target minus spacecraft**: a vector *from* the camera *to* the body being observed, in km, J2000 axes, centred on **that body** |

One invariant follows, and it is a good self-check for a generator: transforming
`TRG_POS` into the spacecraft frame (i.e. multiplying by the transpose of the
quaternion's matrix) must give roughly `(0, 0, +1)` — the target is what the
camera is looking at, and the instrument boresight is +Z.

Measured on the real deliveries:

| fixture | `TRG_POS` in the spacecraft frame |
|---|---|
| AFC2, Mars flyby | `( 0.00085, -0.00005, 0.9999996)` |
| HSH, Mars flyby | `( 0.00008, -0.00001, 0.99999999)` |
| ASPECT, Didymos | `( 0.000005, -0.000004, 0.99999999)` |

The COP delivery gives `(-0.0023, -0.0012, -1.0)` instead — the target on **−Z**.
That is not a small calibration offset; it is a different convention.

### 5. `SC_QUAT` is the conjugate of the convention

The delivery's quaternion matrix maps **J2000 → spacecraft**, the inverse of what
the real deliveries and PRo3D use.

*Fix:* write the spacecraft → J2000 quaternion, i.e. conjugate the current value
(negate `SC_QUAT1/2/3`, keep `SC_QUAT0`).

### 6. `TRG_POS` is the camera location, not target-minus-spacecraft

`TRG_POSX/Y/Z` is **bit-identical** to the `location` of the corresponding
snapshot in the delivery's own `PRo3D.json` — it is where the camera is, not
where the target is relative to it. The sign is therefore inverted with respect
to the header's own description ("Target position vector").

*Fix:* write `target − spacecraft`, i.e. negate the current value.

### 7. `TRG_POS` is centred on Didymos, not on `TARGET`

Even negated, the vector is measured from **Didymos**, while `TARGET` says
`Dimorphos`. Dimorphos sits ~1.05 km from Didymos, and the COP standoff is
~8.2 km, so the boresight implied by `TRG_POS` is **7.0° away** from where the
camera actually points — more than the AFC's whole 5.53° field of view. Fixing
issues 5 and 6 alone therefore still misses the body.

For image `2027-03-01/HERA_AFC_2317_20270301_040000_COP` the delivery's own
ground truth gives:

```
camera location (Didymos-centred)  [ -199.24, -7850.73,  2467.94] m
Dimorphos position                 [-1050.99,   468.81,    22.64] m
camera relative to Dimorphos       [  851.75, -8319.54,  2445.29] m   (|r| = 8713 m)
angle(forward, Dimorphos)          0.15°
angle(forward, Didymos)            7.01°
```

*Fix:* measure `TRG_POS` from the body named in `TARGET`.

### Verification

Run against the raw delivery, PRo3D's own unprojection misses the shape model
outright; with all three corrected it lands on it:

```
pro3d-tool unproject --opc <Dimorphos OPC> --images <folder> --method mbi
                     --body DIMORPHOS --frame DIMORPHOS_FIXED --observer HERA
```

| sidecar | centre pixel (510, 510) |
|---|---|
| as delivered | `no-hit` |
| 5 + 6 corrected | `no-hit` |
| 5 + 6 + 7 corrected | hit at 8561.9 m range, 0.15° from the ground-truth boresight |

### A reference sidecar

`pro3d-tool simulate-image --write-mbi` writes a sidecar in exactly this
convention next to the image it renders, and then reads it back through PRo3D's
own projection path and reports the residual — so it is a working example rather
than a description. See
[Pro3DTool-SimulateImage.md](Pro3DTool-SimulateImage.md#writing-an-mbi-sidecar).

The convention itself is pinned by tests in `src/Tests/MbiSidecarTest.fs`, which
assert the `(0, 0, +1)` invariant above against the three real fixtures.

## Minor observations (no action strictly needed)

- `OBJECT` ("Observation Target ID") is empty; `TARGET` is filled ("Didymos")
  and is what PRo3D uses for the projection target body.
- `EARTPOSX/Y/Z` are all `0.0` — accepted, but earth-position-derived
  displays will be meaningless.
- The PNGs are 8-bit RGB. Fine for projection testing; if physical values
  matter later, single-band float TIFF + the statistics sidecar (as in the
  Mars deliveries) is the richer format.
