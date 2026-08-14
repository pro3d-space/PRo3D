namespace PRo3D.Base.Annotation

open Aardvark.Base
// the vendored copy rather than Aardvark.Geometry, so PolygonTessellator, Polygon2d<'a> and
// Triangle2d<'a> come from one place - see src/PRo3D.Base/Geometry/PolyRegion2d.fs
open PRo3D.Base.Geometry
open PRo3D.Base

/// Triangulates the interior of a closed annotation ring.
///
/// The ring is flattened into a SurfaceChart to decide *topology* only. Each vertex carries its
/// original world position through the tessellator as an attribute, so the resulting mesh is
/// stitched from the input points themselves rather than from their flattened images - the fill
/// boundary coincides exactly with the outline the user drew, terrain projection included. Only
/// triangle interiors chord across the surface.
module PolygonFill =

    type FillMesh =
        {
            /// Triangle list: three consecutive entries per triangle, world space. No index
            /// buffer, because the packed renderer concatenates many annotations into one draw.
            positions : V3d[]
            /// The chart the topology was decided in. Kept for diagnostics.
            chart     : SurfaceChart
        }

    /// World-space distance below which two consecutive ring points count as identical.
    /// Well above double-precision noise at planetary magnitudes, far below meaningful detail.
    [<Literal>]
    let DefaultEpsilon = 1e-6

    /// Collapses consecutive duplicates and strips the closing point, leaving an open ring.
    ///
    /// Stored annotation points genuinely contain duplicates, and no amount of fixing the
    /// producers changes what is already in users' project files: closePolyline appends the first
    /// point again in the Linear branch (Drawing-App.fs:68), computeEllipsePoints emits
    /// samples+1 points (EllipseConstruction.fs:69-78), and a user can pick the same spot twice.
    ///
    /// Runs on every fill, therefore, not only on freshly drawn annotations. Stored points are
    /// never rewritten - doing so would silently change calculatePolygonArea results and the
    /// rendered outline of existing projects.
    let normalize (eps : float) (ps : V3d[]) : V3d[] =
        if ps.Length = 0 then Array.empty
        else
            let res = System.Collections.Generic.List<V3d>(ps.Length)
            res.Add ps.[0]

            for i in 1 .. ps.Length - 1 do
                if Vec.Distance(res.[res.Count - 1], ps.[i]) > eps then
                    res.Add ps.[i]

            // the tessellator closes contours itself, so a ring closed by repetition would
            // contribute a zero-length edge
            while res.Count > 1 && Vec.Distance(res.[0], res.[res.Count - 1]) <= eps do
                res.RemoveAt(res.Count - 1)

            res.ToArray()

    /// Weighted blend of the world positions libtess combines when it invents a vertex.
    ///
    /// Only fires where the tessellator splits an edge - self-intersections, and later boolean
    /// ops. Vertices that coincide with an input point arrive with weight 1 on a single source
    /// and so come back bit-identical.
    let private interpolateWorld (weights : float[]) (values : V3d[]) : V3d =
        let mutable acc = V3d.Zero
        for i in 0 .. (min weights.Length values.Length) - 1 do
            acc <- acc + values.[i] * weights.[i]
        acc

    /// Triangulates a closed ring within the given chart. None when the ring is degenerate, or
    /// when the chart does not cover it.
    let tryComputeFill (chart : SurfaceChart) (points : V3d[]) : FillMesh option =
        let ring = normalize DefaultEpsilon points

        if ring.Length < 3 then None
        else

        let projected = ring |> Array.map chart.toChart

        if projected |> Array.exists Option.isNone then
            Log.warn "[PolygonFill] chart '%s' does not cover all %d ring points" chart.name ring.Length
            None
        else

        let chartPoints = projected |> Array.map Option.get

        // libtess hands the combine callback fixed-size slots; guard rather than let an
        // unexpected shape take down the whole render
        let triangles =
            try
                PolygonTessellator.Triangulate(
                    [ chartPoints, ring ],
                    TessellationRule.EvenOdd,
                    interpolateWorld)
            with e ->
                Log.warn "[PolygonFill] tessellation failed for a %d point ring: %s" ring.Length e.Message
                []

        match triangles with
        | [] -> None
        | ts ->
            let positions = Array.zeroCreate (List.length ts * 3)

            ts |> List.iteri (fun i (t : Triangle2d<V3d>) ->
                positions.[i * 3 + 0] <- t.A0
                positions.[i * 3 + 1] <- t.A1
                positions.[i * 3 + 2] <- t.A2)

            Some { positions = positions; chart = chart }
