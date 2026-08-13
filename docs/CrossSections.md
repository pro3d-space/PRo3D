# Synopsis

![alt text](images/crossSectionTeaser.png)

The cross section feature allows you to cut  surfaces and map images onto the cutting plane.

# Approach

## 1. Create a polyline annotation. 

Use sky projection and adequate subsampling if needed. Watch out not to generate insane segment counts. E.g. for long cross-sections increase sampling distance.

![alt text](images/crossSectionAnno.png)

## 2. Profile Export

![alt text](images/exportProfile.png)

this will look like this:
```
"distance","elevation"
"0","-2540.3019554105754"
"96.56728104292469","-2532.4515562465303"
"146.95083969889563","-2529.7198536160195"
"178.2109028662038","-2525.732550567427"
"238.47366569537462","-2515.188256619444"
"332.84560816666226","-2514.8733450224286"
```

The profile tool is described in [Profile Drawing](../profileDrawing/README.md).

now use the draw profile tool to create an svg/png of which shows the profile
```
python draw_profile.py --profile testProfile2.csv --curtain-height 100 --min-altitude -2200 --vertical-px 2000 --overlay --x-interval 20 --y-interval 20 --x-grid 25 --y-grid 1 --output profile.svg --grid-opacity 1.0 --grid-width 3.0
```

- choose curtain height to match the full vertical span you need
- min-alititude should be where the profile ends (lowest point of profile)
- curtain height is height in meters
- all this needs to be later matched up in (6)
- the vertical-px parameter is used as height of the image. the width is computed automatically given the profile length. For long profiles, you might to lower the resolution as later in pro3d the width shall not exceed 16384 pixels.

Look at the profile or the annotation nfo in pro3d to find good parameters (e.g. vertical span).

Use approximate curtain height and min altitude to specify the 2d viewport of the cross section.

## 3. do your interpretation & tweaking of the cross section

Next convert it to a png and remember the file name.

## 5. Creating the cross section

Next go to annotation properties and move the caemera far away from the cross section.
All between the annotation and the point when creating the cross section will be clipped away.
![alt text](images/createCrossSection.png)

This will leave you with the data clipped:
![alt text](images/clippedCrossSection.png)

## 6. Setting curtain details

Next choose curtain settings and set up the curtain:

![alt text](images/curtainProperties.png)

## 7. Inspection of cross section & curtain

![alt text](images/crossSectionAndCurtain.png)

## How clipping is implemented

`Surface.Sg` writes a **signed distance to the cross-section polygon** into a per-vertex
`InsideOutsideV4` attribute (negative inside), interpolated across triangles so the clip
edge is smooth rather than snapping to mesh-edge midpoints. The `crossSectionClip`
fragment stage in `ViewerUtils.surfaceEffect` discards fragments whose interpolated
distance is negative.

Two flags gate it, and they are not redundant:

- **`CrossSectionClippingEnabled`** — the user's Clipping checkbox. Defaults to **true**
  (`CrossSection-Model.fs`), so it only means "clip if there is something to clip against".
- **`CrossSectionDefined`** — whether a cross-section actually exists.

### Why `CrossSectionDefined` exists

Without it, the clip shader ran on every scene from the first frame, before any
cross-section was defined. In that state `Surface.Sg` binds `InsideOutsideV4` as a
*constant* vertex attribute (`SingleValueBuffer`) — and **on Apple Silicon that value does
not arrive**. Whole patches read back garbage instead of the bound zero; roughly half of
it is negative, so the shader discarded a mesh-grid lattice of fragments across the
terrain.

Substituting a real zero-filled `ArrayBuffer` makes the same machine read exact zeros,
which pins the fault to the constant-attribute path specifically rather than to this
attribute or shader. **What is wrong with that path has not been established.** Two
candidates, neither verified: Apple's GL driver, or how Aardvark's GL backend drives it
(`glVertexAttrib*f` sets *context* state rather than VAO state, and only takes effect
while the attribute array is disabled — either could leave a per-draw value stale or let
another buffer be read instead). Do not repeat either as fact without testing it; if it
turns out to be the backend, it belongs upstream in Aardvark.Rendering.

The real buffer is not used as the fix because it would cost an extra `Patch.load` per
patch on the no-cross-section path, which is almost always. Guarding the shader costs
nothing.

That was the Apple-Silicon-only "regular dark quads" artefact. It never reproduced on
Windows or Linux, and never in the headless harness, which does not bind the attribute at
all. It was found by bisecting `surfaceEffect` one stage at a time — see
[OpcViewer-Screenshot-Harness.md](OpcViewer-Screenshot-Harness.md#bisecting-the-viewers-surface-effect).

**If you touch this path:** do not make the shader read `InsideOutsideV4` when no
cross-section is defined. `PRO3D_SURFACE_EFFECT_ADD=crossSectionDebug` paints the
attribute instead of discarding on it (green = 0, red = negative, blue = positive); with
no cross-section it must be uniformly green.

`SingleValueBuffer` is used in exactly one place in the repo (this attribute). Treat any
new use as suspect on Apple Silicon.

## Future work

This might be interesting: https://www.youtube.com/watch?v=RKH1gKXCD6A