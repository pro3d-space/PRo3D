namespace PRo3D.Base.Annotation

open System.Collections.Generic
open Aardvark.Base
open PRo3D.Base

// Closed prisms for drawing annotation fills onto the terrain (#841).
//
// A flat fill mesh only touches the terrain at its rim: relief inside the outline cuts through
// it, which is the z-fighting of #841, and no depth bias hides metres of relief. Instead the
// renderer decides per terrain pixel whether it lies *inside* a closed volume around the
// outline - by counting the volume's faces behind the terrain, in colour against the surface
// depth (PackedRendering.DeferredFills) or, in the Snapshots fallback, in the stencil. Here is
// that volume: the PolygonFill triangulation as top and bottom caps, offset along the
// extrusion axis, plus a wall on every boundary edge.
//
// The volume must be closed and consistently oriented (outward), or the count is wrong.
// Walls are taken from the boundary edges of the triangulation rather than from the input ring,
// so vertices the tessellator invents at self-intersections - and holes, once rings carry
// them - are closed off by construction.

/// How far above and below its outline a fill may colour terrain - the user-facing control.
///
/// Too short and terrain inside the outline that rises above (or dips below) every rim point
/// stays uncoloured; too long and the volume reaches terrain that is not part of the mapped
/// area - the far side of a ridge, the back of a boulder, the far side of a small body.
[<RequireQualifiedAccess>]
type FillReach =
    /// Half the outline's size, capped by the body's maximum (FillReach.maxAuto)
    | Auto
    /// A fixed reach in metres
    | Metres of float

module FillReach =

    /// Auto reach as a fraction of the outline's size (its bounding-box diagonal).
    [<Literal>]
    let AutoFraction = 0.5

    /// Upper bound of the Auto reach per body, in metres. Hand-tuned: the reach must cover the
    /// relief a mapped unit encloses, but stay well below the body's own size - on Dimorphos
    /// (radius ~75 m) a 100 m reach would colour the far side.
    let maxAuto (planet : Planet) : float =
        match planet with
        | Planet.Mars | Planet.Earth | Planet.Moon -> 1000.0
        | Planet.Phobos                            -> 200.0
        | Planet.Deimos                            -> 100.0
        | Planet.Didymos                           -> 50.0
        | Planet.Dimorphos                         -> 15.0
        // local frames (rover/outcrop data without a body) and anything not listed above
        | _                                        -> 100.0

    /// The reach in effect, in metres, for an outline of the given size.
    let resolve (planet : Planet) (reach : FillReach) (outlineSize : float) : float =
        match reach with
        | FillReach.Auto     -> min (AutoFraction * outlineSize) (maxAuto planet)
        | FillReach.Metres m -> if m > 0.0 then m else 0.0

module FillVolume =

    /// Bounding-box diagonal of a ring: the "size" FillReach.Auto scales with.
    let outlineSize (ring : V3d[]) : float =
        if ring.Length = 0 then 0.0
        else Box3d(ring).Size.Length

    /// Half the prism height along `axis` (normalized): the spread of the ring along it, so a
    /// tilted or rough outline is always enclosed, plus the reach.
    let halfHeight (reach : float) (axis : V3d) (ring : V3d[]) : float =
        if ring.Length = 0 then 0.0
        else
            let mutable lo = Vec.Dot(ring.[0], axis)
            let mutable hi = lo
            for p in ring do
                let d = Vec.Dot(p, axis)
                if d < lo then lo <- d
                if d > hi then hi <- d
            (hi - lo) + reach

    /// Outward-oriented triangle list (three entries per triangle) of the prism around a fill
    /// mesh, extruded `halfHeight` both ways along `axis` (normalized). Each cap vertex column
    /// is offset from its own rim point, so the caps follow the outline rather than a plane.
    ///
    /// The sign of `axis` is irrelevant - the prism is symmetric - which is what lets the caller
    /// take a fitted-plane normal without orienting it.
    let ofFill (axis : V3d) (halfHeight : float) (mesh : PolygonFill.FillMesh) : V3d[] =
        let tris = mesh.positions
        let triCount = tris.Length / 3

        // libtess emits every triangle with the same winding, so the cap is consistently
        // oriented as it comes. Do not "correct" triangles one by one: a sliver triangle
        // (collinear points around an invented crossing vertex) has a winding sign that is pure
        // rounding noise, and flipping it breaks the cap's consistency - edges then appear twice
        // in the same direction and the prism is no longer closed (FsCheck found exactly that).
        let caps = tris
        let mutable area = V3d.Zero
        for t in 0 .. triCount - 1 do
            let a = caps.[3 * t]
            let b = caps.[3 * t + 1]
            let c = caps.[3 * t + 2]
            area <- area + Vec.Cross(b - a, c - a)

        // the chart's handedness relative to axis decides which cap faces +axis. Summed vector
        // area, not a single triangle: individual triangles of a rough ring may stand steep
        // against the axis, the whole cap does not
        let towardsAxis = Vec.Dot(area, axis) >= 0.0

        let up = axis * halfHeight
        let result = List<V3d>(triCount * 6 + 64)

        for t in 0 .. triCount - 1 do
            let a = caps.[3 * t]
            let b = caps.[3 * t + 1]
            let c = caps.[3 * t + 2]
            // top cap faces +axis, bottom cap -axis
            let b, c = if towardsAxis then b, c else c, b
            result.Add(a + up); result.Add(b + up); result.Add(c + up)
            result.Add(a - up); result.Add(c - up); result.Add(b - up)

        // directed edges of the (now +axis facing) top cap. An interior edge appears once in
        // each direction; a boundary edge only once, and in the direction that keeps the cap on
        // its left when viewed from +axis
        let edges = HashSet<struct(V3d * V3d)>()
        for t in 0 .. triCount - 1 do
            let a = caps.[3 * t]
            let b = caps.[3 * t + 1]
            let c = caps.[3 * t + 2]
            let b, c = if towardsAxis then b, c else c, b
            edges.Add(struct(a, b)) |> ignore
            edges.Add(struct(b, c)) |> ignore
            edges.Add(struct(c, a)) |> ignore

        // a wall per boundary edge a -> b, facing outward (to the right of a -> b seen from
        // +axis): bottom a, bottom b, top b / bottom a, top b, top a
        for struct(a, b) in edges do
            if not (edges.Contains(struct(b, a))) then
                let at = a + up
                let bt = b + up
                let ab = a - up
                let bb = b - up
                result.Add ab; result.Add bb; result.Add bt
                result.Add ab; result.Add bt; result.Add at

        result.ToArray()

    type Volume =
        {
            /// The flat fill triangulation (PolygonFill), triangle list. The pick pass has no
            /// terrain to stencil against, so it still draws this.
            cap   : V3d[]
            /// The closed, outward-oriented prism around it, triangle list.
            prism : V3d[]
        }

    /// Prism around the fill of a closed ring: triangulated in `chart`, extruded along `axis`.
    /// `reachOf` maps the outline's size to the reach in metres (FillReach.resolve, partially
    /// applied). None when the ring cannot be filled (degenerate, or the chart does not cover
    /// it) or the axis is unusable.
    let tryCompute (reachOf : float -> float) (chart : SurfaceChart) (axis : V3d) (points : V3d[]) : Option<Volume> =
        let len = axis.Length
        if not (len > 1e-12) || axis.AnyNaN then None
        else
            let axis = axis / len
            match PolygonFill.tryComputeFill chart points with
            | None -> None
            | Some mesh ->
                let ring = PolygonFill.normalize PolygonFill.DefaultEpsilon points
                let h = halfHeight (reachOf (outlineSize ring)) axis ring
                if not (h > 0.0) then None
                else Some { cap = mesh.positions; prism = ofFill axis h mesh }
