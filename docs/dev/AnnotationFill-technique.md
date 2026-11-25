# How annotation fills are drawn (#841)

For developers. The user-facing page is [docs/AnnotationFill.md](../AnnotationFill.md); the terse agent
notes are in [ai/RENDERING.md](../../ai/RENDERING.md#annotation-fills--against-the-surface-depth-841).
Code: `FillVolume` (`src/PRo3D.Base/Annotation/FillVolume.fs`), `PackedRendering.DeferredFills`
(`src/PRo3D.Core/Drawing/PackedRendering.fs`), `OffscreenSurfaces` (`src/PRo3D.Core/OffscreenSurfaces.fs`).

## The short version

**The prism is never visible.** Each fill has a closed prism around it (its outline, pushed out
up and down along the outline's plane normal). The prism's job is to answer one yes/no question
per pixel: *is the terrain point seen at this pixel inside this prism?* Its faces write nothing
you see. A separate full-screen pass then paints the fill colour onto exactly those terrain
pixels where the answer was "yes".

So "colour is only counted behind the terrain" does not mean the colour ends up behind the
terrain. The *prism faces* behind the terrain are counted, and the count says whether the
terrain point in front of them is inside. The colour is put on the terrain in the second pass.

## One pixel, one ray

Follow the ray from the eye through one pixel. It hits the terrain at depth `T`, which we know
because the surfaces were rendered first into a depth texture.

```
 terrain point inside the prism -> filled

   eye ----------[ front face ]-----------X-----------[ back face ]------->
                    z < T                 T              z > T
                  discarded          terrain point      counted +1      sum = +1  (inside)


 prism entirely behind the terrain point -> not filled

   eye ----------------X--------[ front face ]---------[ back face ]------->
                       T            z > T                  z > T
                               counted -1              counted +1      sum =  0  (outside)


 prism entirely in front of the terrain point -> not filled

   eye ---[ front face ]-------[ back face ]-----------X------------------->
              z < T                z < T                T
            discarded            discarded                             sum =  0  (outside)
```

For every prism face fragment on that pixel:

- face depth `z <= T` (in front of the terrain): discarded, contributes nothing;
- face depth `z > T` (behind the terrain): back face +1, front face −1.

The sum is the number of prism surfaces the ray crosses *after* the terrain point, counted with
orientation. For a closed, consistently oriented volume that is its winding number at the terrain
point: 1 inside, 0 outside. It holds for non-convex prisms too (a ray that enters and leaves an
L-shaped prism twice behind the terrain gets −1 +1 −1 +1 = 0). This is why `FillVolume` insists
the prism is closed and outward-oriented: a missing wall or a flipped triangle breaks the count.

This is the shadow-volume *z-fail* test ("Carmack's reverse"), just with the terrain as the
receiver and a colour instead of a shadow.

## Why count behind the terrain (z-fail) and not in front (z-pass)

Counting the faces *in front* of the terrain (front +1, back −1) gives the same answer, as long
as every face in front of the terrain is actually rasterised. That fails in exactly the cases a
user runs into:

- **The camera is inside a prism.** Common when you fly low over a large mapped area, especially
  with a big fill reach. The front face is behind the camera and never rasterised, so z-pass
  counts 0 for terrain that is inside. z-fail does not need that face: the back face behind the
  terrain still gives +1.
- **The near plane cuts a prism.** Same problem: the clipped part of the front face is missing.
  z-fail does not care what happens in front of the terrain.
- **The far plane.** z-fail does need the faces *behind* the terrain, and those can lie beyond the
  far plane. That is what **depth clamp** is for: with it, faces beyond near/far are not clipped
  but rasterised at the clamped depth. The shader also clamps `gl_FragCoord.z` to [0, 1] itself,
  because it arrives unclamped. A back face beyond the far plane therefore sits at depth 1, which
  is still `> T` wherever there is terrain, and is counted. (Pixels with no surface at all,
  `T = 1`, are skipped.)

The `FillRenderTest` GPU tests cover each case: camera inside a prism, a far plane cutting a
prism.

## No sorting, and why the sum is the fill's colour

All prisms of all fills go into **one draw** into an RGBA32F accumulation texture, with depth
test off, culling off, and **additive blending** (`One, One`). Each fragment that survives the
test adds

```
w * (r*a, g*a, b*a, a)        w = +1 back face, −1 front face
```

of its fill's colour `(r, g, b)` and alpha `a`. Addition is commutative, so the order in which
fragments arrive does not matter: no sorting, no per-fill pass.

For one pixel:

- a prism that does **not** contain the terrain point contributes its faces in +1/−1 pairs (see
  the second diagram), and both faces of a pair carry the *same* colour, so it adds exactly 0;
- the prism that **does** contain it leaves exactly one uncancelled `+(rgb*a, a)`.

The composite pass (full-screen, drawn before outlines so they stay on top) reads the sum `s`
and turns it back into straight-alpha colour:

```
alpha  = min(1, |s.a|)
colour = |s.rgb| / |s.a|
```

and blends that over the terrain with ordinary alpha blending. For a single fill this gives back
exactly `(r, g, b)` and `a`.

**Where the sign comes from, and why `abs`:** "front" and "back" come from the triangle winding
as seen on screen (`gl_FrontFacing`). Whether an inside point ends up at +1 or −1 depends only on
the winding convention (prisms are outward-oriented, but the API's notion of front-facing is
CCW or CW by configuration). That convention is the same for every prism, so every inside sum in
the frame has the same sign. `abs` in the composite makes the result independent of it.

(Floating-point: `x − x` is exactly 0, but sums of many terms in a different order can leave tiny
residues; the composite discards `|s.a| < 0.0001`.)

## Is this cheap order-independent transparency? Not quite

Where two translucent fills overlap on the same terrain point, both prisms contain it and the
sum is `(Σ rgbᵢ·aᵢ, Σ aᵢ)`. The composite then gives

```
colour = Σ rgbᵢ·aᵢ / Σ aᵢ        (average weighted by alpha)
alpha  = min(1, Σ aᵢ)
```

That is **not** "over" compositing. Two 50 % fills on top of each other give alpha 1.0 here, not
the 0.75 that layering them would give, and the colour is a symmetric mix rather than "the top
one shows more".

It is the right kind of answer only because of where the layers are: every fill that contributes
at a pixel colours *the same terrain point*, at the same depth. There is no front or back between
them, so no "correct" order to violate; a symmetric, order-independent mix is a reasonable
definition. The general technique this resembles is **Weighted Blended OIT** (McGuire & Bavoil,
JCGT 2013), which uses the same normalised `Σ c·a / Σ a` but approximates layers at *different*
depths with a depth weight, and computes coverage as `1 − Π(1 − aᵢ)` (revealage) instead of a
clamped sum. We have no depth differences to weight, and we use the clamped sum for coverage;
switching to revealage would make overlap opacity match "over" (0.75 for two 50 % fills) and is
a possible refinement, not something the current code does.

## Why not the stencil buffer

The textbook way to do z-fail is the stencil buffer: increment/decrement per face, then draw where
the stencil is non-zero. That answers "is this terrain point inside *some* prism", not "*which*
one", and the colour depends on which.

- **A shared stencil for all fills** (one count for everything): the colour pass then has to
  paint each pixel from some prism's geometry, and it was whichever prism face reached the pixel
  first, e.g. a near prism's wall painting over a far fill's terrain. In oblique views this gave
  straight cuts through fills.
- **One stencil bit per fill**: correct. Eight fills per 8-bit stencil batch, each batch a
  stencil pass plus a colour pass, ordered. At 10 240 fills that is about 10–23 ms per frame
  (varies between runs); one render command per fill was about 54 ms.

The accumulation in colour carries the fill's identity (its colour) in the count itself, which is
why one draw is enough.

| method (10 240 fills, RTX 500 Ada laptop, moving camera) | cost per frame |
|---|---|
| deferred accumulation + composite (current) | ≈ 0.06–0.15 ms |
| offscreen surfaces + copy back (needed for the depth texture) | ≈ 0.05 ms |
| stencil, one bit per fill, batches of 8 (Snapshots fallback) | ≈ 10–23 ms |
| one render command per fill | ≈ 54 ms |

The stencil renderer is still in the code as the fallback for PRo3D.Snapshots, which renders
surfaces directly and has no surface depth texture.

## Why the surfaces render offscreen

The test needs the terrain depth `T` as a texture while the prisms are drawn, and it must be the
depth of the *surfaces only* (not annotations, lines, labels). A pass cannot read the depth buffer
it is writing, so `OffscreenSurfaces` renders the surface priority groups into a multisampled
colour + depth target, copies both back per sample into the view's framebuffer (so everything
after depth-tests as before), and keeps the depth as a texture for the fills.

## Known limits of the technique

- The accumulation is single-sampled (sample 0 of the depth is read), so fill edges at terrain
  silhouettes are not antialiased.
- The fill reach decides how far up/down a prism extends. Too small, and a tall boulder pokes out
  of the prism (uncoloured top); too large, and terrain that is not part of the area (the far
  side of a ridge, the back of a small body) is inside it. See the user docs.
- Overlapping translucent fills: weighted average with clamped alpha, as above.
