/// Properties that must hold for *every* fill, stated once.
///
/// These return violations rather than asserting, so example-based tests and generated
/// sequences share one definition of correctness. When FsCheck starts producing random rings
/// and boolean-operation sequences, it checks these same functions - a property that only
/// exists inside an Expecto test cannot be reused by a generator.
module FillInvariants

open Aardvark.Base
open PRo3D.Base
open PRo3D.Base.Annotation

/// World-space slack. Above double-precision noise at planetary magnitudes, far below any
/// meaningful annotation detail.
let eps = 1e-6

// ---------------------------------------------------------------------------------------------
// normalize
// ---------------------------------------------------------------------------------------------

/// Holds for any input, including degenerate ones.
let normalizeViolations (input : V3d[]) : string list =
    let result = PolygonFill.normalize PolygonFill.DefaultEpsilon input
    [
        // consecutive duplicates are what the function exists to remove
        for i in 1 .. result.Length - 1 do
            if Vec.Distance(result.[i - 1], result.[i]) <= eps then
                yield sprintf "consecutive duplicate survived at index %d" i

        // a ring closed by repetition would contribute a zero-length edge
        if result.Length > 1 && Vec.Distance(result.[0], result.[result.Length - 1]) <= eps then
            yield "result is still closed by repetition"

        // it may only ever drop points, never invent or reorder them
        if result.Length > input.Length then
            yield sprintf "grew from %d to %d points" input.Length result.Length

        for p in result do
            if input |> Array.forall (fun q -> Vec.Distance(p, q) > eps) then
                yield sprintf "invented a point not present in the input: %A" p

        // idempotence: a second pass must be a no-op
        let twice = PolygonFill.normalize PolygonFill.DefaultEpsilon result
        if twice.Length <> result.Length then
            yield sprintf "not idempotent: %d then %d" result.Length twice.Length
    ]

// ---------------------------------------------------------------------------------------------
// fill meshes
// ---------------------------------------------------------------------------------------------

let private triangles (positions : V3d[]) =
    seq { for i in 0 .. 3 .. positions.Length - 3 -> positions.[i], positions.[i+1], positions.[i+2] }

let triangleArea (a : V3d, b : V3d, c : V3d) =
    0.5 * Vec.Length(Vec.cross (b - a) (c - a))

let meshArea (positions : V3d[]) =
    triangles positions |> Seq.sumBy triangleArea

/// Structural properties, true of any chart and any ring.
///
/// Note what is deliberately *not* here: "every vertex lies on the chart surface". Mesh vertices
/// are the original input points, which for a non-planar ring sit off the chart by design - that
/// is what keeps a fill's rim on the drawn outline rather than on the flattened datum. The first
/// run of these invariants failed on a tilted quad for exactly that reason.
let fillViolations (mesh : PolygonFill.FillMesh) : string list =
    [
        if mesh.positions.Length % 3 <> 0 then
            yield sprintf "%d positions is not a whole number of triangles" mesh.positions.Length

        if mesh.positions.Length = 0 then
            yield "a fill was produced with no triangles"

        for p in mesh.positions do
            if p.IsNaN then yield "mesh contains a NaN vertex"
    ]

/// True when every ring point lies on the chart's surface, i.e. projecting and lifting back is
/// the identity. Guards the area invariant below.
let isPlanarInChart (chart : SurfaceChart) (ring : V3d[]) =
    ring |> Array.forall (fun p ->
        match chart.toChart p |> Option.bind chart.toWorld with
        | Some q -> Vec.Distance(p, q) <= 1e-6
        | None   -> false)

/// Mesh area equals the ring's area - but only when the ring is planar *in this chart* and the
/// chart is isometric (a plane or up-vector chart preserves distance; geographic does not, since
/// degrees are not metres). Self-guards, so it can be applied uniformly across a corpus.
///
/// For a non-planar ring the mesh area is the true 3D area and the chart-space area is its
/// projection; both are correct and they legitimately differ.
let planarAreaViolations (chart : SurfaceChart) (ring : V3d[]) (mesh : PolygonFill.FillMesh) : string list =
    let normalized = PolygonFill.normalize PolygonFill.DefaultEpsilon ring
    if normalized.Length < 3 || not (isPlanarInChart chart normalized) then []
    else
        let projected = normalized |> Array.choose chart.toChart
        if projected.Length <> normalized.Length then []
        else
            let expected = Polygon2d(projected).ComputeArea() |> abs
            let actual = meshArea mesh.positions
            if expected <= 0.0 then []
            elif abs (actual - expected) / expected > 0.01 then
                [ sprintf "area %g differs from the ring's %g by more than 1%%" actual expected ]
            else []

/// For a simple (non-self-intersecting) ring the tessellator invents no vertices, so every mesh
/// vertex must be one of the input points - this is what keeps a fill's rim on the drawn
/// outline instead of on the flattened datum. Self-intersecting rings legitimately break it.
let verticesAreInputPointsViolations (ring : V3d[]) (mesh : PolygonFill.FillMesh) : string list =
    [ for p in mesh.positions do
        if ring |> Array.forall (fun q -> Vec.Distance(p, q) > 1e-9) then
            yield sprintf "mesh vertex %A is not an input point" p ]
