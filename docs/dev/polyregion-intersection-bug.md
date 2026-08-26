# `PolyRegion.Intersection` returns the union when boundaries coincide

Found by the FsCheck merge property on CI (`StdGen (627528932, 297661573)`), which reported
"the merge lost part of b". The union was correct; the *containment check* was wrong, because it
asked the question through `intersect`.

## Symptom

For two overlapping rings `a`, `b` and `m = merge a b` (areas: a 296.65, b 239.00, a∩b 17.77,
m 517.88 — inclusion–exclusion exact, so the union is right):

```
PolyRegion.Intersection(b, m)  ->  517.88   // the whole union, expected 239.00 (= b)
PolyRegion.Intersection(a, m)  ->  296.65   // correct
PolyRegion.Intersection(a, b)  ->  17.77    // correct
```

Symmetric (`Intersection(m, b)` is equally wrong), and all contours are positively oriented, so
it is neither argument order nor winding of the inputs.

## Cause

`Intersection` tessellates both contour sets together under the **`AbsGeqTwo`** winding rule
(`PolyRegion2d.fs:596`): keep what the combined contours wind at least twice. That assumes the
overlap is enclosed by *two separate* boundaries. Where the two boundaries **coincide** — which
is exactly what a merge result and one of its operands look like, since `m`'s outline runs along
`b`'s wherever `b` protrudes — the coincident edges do not contribute the second turn, the whole
outer region reads as winding ≥ 2, and the call returns the union.

`Union` and `Difference` use positive winding (`:579`, `:588`) and are unaffected.

## Fix

`RegionOps.intersect` computes `A ∩ B` as `A - (A - B)`, using only positive-winding
`Difference`. Verified to agree with the old path on ordinary overlaps (17.77 for `a ∩ b`) and to
fix the coincident-boundary case (239.00 for `b ∩ m`).

The vendored `PolyRegion2d.fs` is deliberately **not** patched — it is kept byte-identical to
upstream apart from its namespace so the eventual switch back to the released
`Aardvark.Geometry` stays a clean revert (see `plans/booleanOperations.md`).

## The one place that must keep `AbsGeqTwo`

`RegionOps.sides`, which splits a region along a cut stroke. Its side polygon **self-overlaps**
whenever the stroke bends back on itself (a V-shaped cut), and there the two formulations
disagree the other way round: `AbsGeqTwo` paired with positive-winding `Difference` partitions
the region exactly, while `A - (A - B)` leaves the doubly-wound wedge in *both* sides, so the
pieces sum to more than the original (107.5 instead of 100 in "a V-shaped stroke carves a
wedge").

So the two call sites use different formulations because their inputs are degenerate in
different ways — coincident boundaries on one side, self-overlap on the other. Both are pinned
by tests:

- `RegionOpsTests` → "a small ring beside a much larger one is not reported as lost"
- `RegionOpsTests` → "a V-shaped stroke carves a wedge"

## Worth reporting upstream

The behaviour is in aardvark.base's `PolyRegion` (both the legacy and the attributed type use
`AbsGeqTwo` for intersection). A minimal report needs only two rings where one contains the
other with a shared boundary stretch.
