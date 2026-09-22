/// Properties of cut and merge, stated once and returned as violations so the example corpus,
/// the FsCheck generators and the exported fixtures all check the same definitions.
///
/// Same shape as FillInvariants: nothing here asserts, so a generator can reuse it.
module RegionInvariants

open Aardvark.Base
open PRo3D.Base.Geometry
open PRo3D.Base.Geometry.RegionOps

/// Relative tolerance for area comparisons. Boolean operations re-tessellate, so exact equality
/// is not available even for operations that are geometrically exact.
let areaTolerance = 0.01

let private relDiff (a : float) (b : float) =
    if abs b < 1e-12 then abs a else abs (a - b) / abs b

// ---------------------------------------------------------------------------------------------
// sampled equivalence
// ---------------------------------------------------------------------------------------------

/// Comparing regions geometrically is brittle - contour order, vertex counts and winding all
/// differ between equal regions. Comparing membership over a grid is robust and tolerant, and it
/// is the only equivalence that survives re-tessellation.
let sampledDisagreement (n : int) (a : Region) (b : Region) =
    let box = Box2d(bounds a)
    box.ExtendBy(bounds b)
    if box.IsInvalid then 0.0
    else
        let box = box.EnlargedBy(box.Size.Length * 0.1 + 1.0)
        let mutable disagree = 0
        let mutable total = 0
        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                let p =
                    V2d(box.Min.X + box.SizeX * (float i + 0.5) / float n,
                        box.Min.Y + box.SizeY * (float j + 0.5) / float n)
                total <- total + 1
                if contains p a <> contains p b then disagree <- disagree + 1
        float disagree / float total

/// A grid can only resolve features larger than a cell, so a small disagreement fraction near the
/// boundary is expected rather than a defect.
let sampledEquivalent (a : Region) (b : Region) = sampledDisagreement 40 a b <= 0.02

/// How much of `inner` sticks out of `outer`, as a fraction of inner's area. Zero means
/// contained.
///
/// Asked as `inner - outer` rather than `area (inner ∩ outer) = area inner`: both are exact
/// statements, but the difference is a single tessellation where intersect is two nested ones
/// (it is itself defined through difference), so this accumulates half the numerical error.
let escapedFraction (inner : Region) (outer : Region) =
    let a = area inner
    if a <= 0.0 then 0.0 else area (difference inner outer) / a

let containedIn (inner : Region) (outer : Region) = escapedFraction inner outer <= areaTolerance

// ---------------------------------------------------------------------------------------------
// cut
// ---------------------------------------------------------------------------------------------

let cutViolations (stroke : V2d[]) (r : Region) : string list =
    let pieces = cut stroke r
    let cutHappened = cutsThrough stroke r
    [
        if not cutHappened then
            // a stroke that does not cross must change nothing at all
            if pieces.Length <> 1 then
                yield sprintf "a non-cutting stroke produced %d pieces" pieces.Length
            elif relDiff (area pieces.Head) (area r) > 1e-9 then
                yield "a non-cutting stroke changed the region's area"
        else
            if pieces.Length < 2 then
                yield sprintf "a cutting stroke produced only %d piece(s)" pieces.Length

            // the cut neither creates nor destroys area
            let total = pieces |> List.sumBy area
            if relDiff total (area r) > areaTolerance then
                yield sprintf "pieces total %g but the region was %g" total (area r)

            // pieces must not overlap
            for i in 0 .. pieces.Length - 1 do
                for j in i + 1 .. pieces.Length - 1 do
                    let shared = intersect pieces.[i] pieces.[j] |> area
                    if shared > area r * 0.001 then
                        yield sprintf "pieces %d and %d overlap by %g" i j shared

            // every piece lies inside the original
            for k, piece in List.indexed pieces do
                if not (containedIn piece r) then
                    yield sprintf "piece %d is not contained in the original" k

            // re-cutting a piece with the same stroke must do nothing: it lies wholly on one side
            for k, piece in List.indexed pieces do
                if cutsThrough stroke piece then
                    yield sprintf "piece %d is still cut by the same stroke" k
    ]

// ---------------------------------------------------------------------------------------------
// merge
// ---------------------------------------------------------------------------------------------

let mergeViolations (a : Region) (b : Region) : string list =
    let m = merge a b
    [
        // Area of the union = area a plus the part of b lying outside a. Equivalent to
        // inclusion-exclusion, but built from a single difference rather than an intersection -
        // intersect is two nested tessellations, so the old form measured its accuracy as much
        // as merge's, and reported "merged area X, expected Y" for perfectly good unions.
        let expected = area a + area (difference b a)
        if relDiff (area m) expected > areaTolerance then
            // An independent estimate that uses no boolean operation at all: rasterise "in a or
            // in b" over the pair's bounds. It says which of the two operations drifted.
            let box = Box2d(bounds a)
            box.ExtendBy(bounds b)
            let n = 400
            let mutable hits = 0
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    let p =
                        V2d(box.Min.X + box.SizeX * (float i + 0.5) / float n,
                            box.Min.Y + box.SizeY * (float j + 0.5) / float n)
                    if contains p a || contains p b then hits <- hits + 1
            let rasterised = box.SizeX * box.SizeY * float hits / float (n * n)
            yield sprintf "merged area %g, expected %g (rasterised union %g; a %g, b %g, b-a %g)"
                    (area m) expected rasterised (area a) (area b) (area (difference b a))

        // order must not matter
        if not (sampledEquivalent m (merge b a)) then
            yield "merge is not commutative"

        // both operands must be contained in the result
        let escapedA = escapedFraction a m
        if escapedA > areaTolerance then
            yield sprintf "the merge lost %.3f%% of a (area %g)" (escapedA * 100.0) (area a)
        let escapedB = escapedFraction b m
        if escapedB > areaTolerance then
            yield sprintf "the merge lost %.3f%% of b (area %g)" (escapedB * 100.0) (area b)
    ]

let mergeIdempotenceViolations (a : Region) : string list =
    if not (sampledEquivalent (merge a a) a) then [ "merging a region with itself changed it" ] else []

// ---------------------------------------------------------------------------------------------
// the headline property
// ---------------------------------------------------------------------------------------------

/// Cut a region, merge the pieces back, and you must have what you started with. This checks both
/// operations against each other rather than against a hand-computed expectation, which is why it
/// is worth more than any single-operation property.
let roundTripViolations (stroke : V2d[]) (r : Region) : string list =
    match cut stroke r with
    | [] -> [ "cut produced nothing at all" ]
    | pieces ->
        let rejoined = pieces |> List.reduce merge
        [
            if relDiff (area rejoined) (area r) > areaTolerance then
                yield sprintf "rejoined area %g, original %g" (area rejoined) (area r)
            if not (sampledEquivalent rejoined r) then
                yield "rejoined region is not equivalent to the original"
        ]
