namespace PRo3D.Base.Geometry

open Aardvark.Base
open PRo3D.Base.Annotation

/// Boolean operations on annotation regions: cutting one along a line, and merging two.
///
/// Deliberately free of any UI. The 2D lab drives these; so do the property tests. Geometry that
/// only exists behind a GUI cannot be checked automatically, which is the whole point of the
/// staging in plans/testingStrategy.md.
module RegionOps =

    /// A 2D region whose vertices carry their source world position as an attribute. In the plane
    /// the attribute is the same point with z = 0, but keeping the attributed type means the lab
    /// exercises exactly the code path PRo3D uses, where the attribute is a terrain-projected
    /// point and re-flattening it would be visible.
    type Region = PolyRegion<V3d>

    /// The one interpolation used everywhere. Boolean operations invent vertices where edges
    /// cross, and this decides their world position.
    let interpolate = PolygonFill.interpolateWorld

    let private toV2d (p : V3d) = V2d(p.X, p.Y)
    let private toV3d (p : V2d) = V3d(p.X, p.Y, 0.0)

    /// Positive for counter-clockwise contours, negative for clockwise ones. LibTess emits holes
    /// clockwise, so the sign is what distinguishes an outer contour from a hole.
    let private signedArea (pts : V2d[]) =
        let mutable acc = 0.0
        for i in 0 .. pts.Length - 1 do
            let p = pts.[i]
            let q = pts.[(i + 1) % pts.Length]
            acc <- acc + (p.X * q.Y - q.X * p.Y)
        acc * 0.5

    // -----------------------------------------------------------------------------------------
    // construction and inspection
    // -----------------------------------------------------------------------------------------

    /// None when the ring has fewer than three distinct points, or encloses nothing.
    let ofRing (ring : V3d[]) : Region option =
        let cleaned = PolygonFill.normalize PolygonFill.DefaultEpsilon ring
        if cleaned.Length < 3 then None
        else
            let polygon = Polygon2d<V3d>(cleaned |> Array.map toV2d, cleaned)
            let region = PolyRegion<V3d>(polygon, TessellationRule.EvenOdd, interpolate)
            if region.IsEmpty then None else Some region

    let ofRing2d (ring : V2d[]) = ofRing (ring |> Array.map toV3d)

    /// Outer contours, counter-clockwise, carrying their world positions.
    let outerRings (r : Region) =
        r.Polygons |> List.filter (fun p -> signedArea p.Points > 0.0) |> List.map (fun p -> p.Attributes)

    /// Outer contours with the chart position alongside each attribute. For callers that must
    /// reconstruct positions of invented vertices: after a cut, attributes of vertices on the
    /// stroke are blends involving the synthetic side polygon (whose "world" attributes are
    /// chart coordinates), so only the 2D position is trustworthy there - see
    /// AnnotationRegionOps.cut.
    let outerContours (r : Region) : list<(V2d * V3d)[]> =
        r.Polygons
        |> List.filter (fun p -> signedArea p.Points > 0.0)
        |> List.map (fun p -> Array.zip p.Points p.Attributes)

    /// Every contour with its signed chart-space area: positive contours are outer rings,
    /// negative ones holes. For callers that classify contours by size - e.g. separating
    /// legitimate union components from self-intersection slivers.
    let signedContours (r : Region) : list<float * (V2d * V3d)[]> =
        r.Polygons
        |> List.map (fun p -> signedArea p.Points, Array.zip p.Points p.Attributes)

    /// Hole contours, clockwise. Non-empty means the region cannot be stored as a single-ring
    /// annotation - see the viewer-integration note in the plan.
    let holes (r : Region) =
        r.Polygons |> List.filter (fun p -> signedArea p.Points < 0.0) |> List.map (fun p -> p.Attributes)

    let isEmpty (r : Region) = r.IsEmpty

    /// Net area: outer contours minus holes, which falls out of the signed areas directly.
    let area (r : Region) =
        r.Polygons |> List.sumBy (fun p -> signedArea p.Points) |> abs

    let bounds (r : Region) =
        if r.IsEmpty then Box2d.Invalid
        else Box2d(r.Polygons |> Seq.collect (fun p -> p.Points))

    /// Even-odd across *all* contours, so a point inside a hole is correctly outside the region.
    ///
    /// Not PolyRegion.Contains: that one is `Seq.exists` over the contours, so it answers true for
    /// a point sitting in a hole.
    let contains (p : V2d) (r : Region) =
        let mutable inside = false
        for poly in r.Polygons do
            let pts = poly.Points
            let n = pts.Length
            for i in 0 .. n - 1 do
                let a = pts.[i]
                let b = pts.[(i + 1) % n]
                if (a.Y > p.Y) <> (b.Y > p.Y) then
                    let t = (p.Y - a.Y) / (b.Y - a.Y)
                    if p.X < a.X + t * (b.X - a.X) then inside <- not inside
        inside

    // -----------------------------------------------------------------------------------------
    // cutting
    // -----------------------------------------------------------------------------------------

    // A cut stroke is a polyline of at least two points; a straight cut is the two-point case.
    // Only the *ends* must lie outside the region - interior stroke points may dip inside, which
    // is what lets a cut follow a feature instead of a chord.

    /// Consecutive duplicates removed, so segment directions are well-defined.
    let private dedupStroke (stroke : V2d[]) =
        if stroke.Length = 0 then stroke
        else
            let out = ResizeArray<V2d>(stroke.Length)
            out.Add stroke.[0]
            for i in 1 .. stroke.Length - 1 do
                if Vec.Distance(stroke.[i], out.[out.Count - 1]) > 1e-9 then out.Add stroke.[i]
            out.ToArray()

    let private strokeLength (stroke : V2d[]) =
        let mutable acc = 0.0
        for i in 0 .. stroke.Length - 2 do
            acc <- acc + Vec.Distance(stroke.[i], stroke.[i + 1])
        acc

    /// A stroke segment passing *exactly* through region vertices makes LibTess drop tangent
    /// slivers, losing area from the pieces. Users produce this case routinely - shapes drawn on
    /// round coordinates put vertices on nice diagonals, and a bounds-centred stroke hits them
    /// (found by a lab-exported fixture: the 135° stroke through a U-shape's centre grazed three
    /// of its corners and lost 14% of its area). Nudge the whole stroke sideways until it clears
    /// every vertex; the shift is orders of magnitude below the invariant tolerances, and it
    /// keeps re-cutting a piece along the same stroke a no-op (the sliver it leaves is far under
    /// the area ratio cutsThrough requires).
    let private clearOfVertices (stroke : V2d[]) (r : Region) =
        let bb = bounds r
        let eps = (bb.Size.Length + 1.0) * 1e-9
        let d0 = (stroke.[1] - stroke.[0]).Normalized
        let n0 = V2d(-d0.Y, d0.X)
        let distToSegment (p : V2d) (a : V2d) (b : V2d) =
            let ab = b - a
            let len2 = ab.LengthSquared
            if len2 < 1e-18 then Vec.Distance(p, a)
            else
                let t = clamp 0.0 1.0 (Vec.Dot(p - a, ab) / len2)
                Vec.Distance(p, a + ab * t)
        let touches (shift : V2d) =
            r.Polygons |> List.exists (fun poly ->
                poly.Points |> Seq.exists (fun v ->
                    seq { 0 .. stroke.Length - 2 }
                    |> Seq.exists (fun i ->
                        distToSegment v (stroke.[i] + shift) (stroke.[i + 1] + shift) < eps)))
        let mutable shift = V2d.Zero
        let mutable step = eps * 4.0
        let mutable iter = 0
        // capped: a vertex sliding along a segment parallel to the shift direction could resist
        // forever, and a residual graze merely costs a sliver the tolerances absorb
        while iter < 60 && touches shift do
            shift <- shift + n0 * step
            step <- step * 2.0
            iter <- iter + 1
        stroke |> Array.map (fun p -> p + shift)

    /// One side of the stroke, as a region: the stroke extended well past the region at both
    /// ends, then closed through an arc so far out that the closure cannot interfere with
    /// anything near the region. Which of the two sides the polygon covers depends on the arc
    /// direction and does not matter - the cut takes both the intersection and the difference,
    /// and they are complementary within the region either way.
    let private sidePolygon (stroke : V2d[]) (r : Region) : Region =
        let bb = bounds r
        let c = bb.Center
        let reach = (bb.Size.Length + strokeLength stroke + Vec.Distance(c, stroke.[0])) * 4.0 + 1.0
        let last = stroke.Length - 1
        let d0 = (stroke.[1] - stroke.[0]).Normalized
        let dn = (stroke.[last] - stroke.[last - 1]).Normalized
        let ext =
            Array.concat [
                [| stroke.[0] - d0 * reach |]
                stroke
                [| stroke.[last] + dn * reach |] ]
        let ext = clearOfVertices ext r
        let a = ext.[0]
        let b = ext.[ext.Length - 1]
        // radial connectors out to a far circle, then an arc between them: every closure edge
        // stays at several times the region's diameter, so within the region the polygon's
        // even-odd membership is decided by the stroke alone
        let rBig = reach * 4.0
        let angA = atan2 (a - c).Y (a - c).X
        let angB = atan2 (b - c).Y (b - c).X
        let sweep =
            let d = angA - angB
            if d <= 0.0 then d + Constant.PiTimesTwo else d
        let steps = 16
        let arc =
            [| for i in 0 .. steps ->
                 let ang = angB + sweep * (float i / float steps)
                 c + V2d(cos ang, sin ang) * rBig |]
        let pts = Array.concat [ ext; arc ]
        let polygon = Polygon2d<V3d>(pts, pts |> Array.map toV3d)
        PolyRegion<V3d>(polygon, TessellationRule.EvenOdd, interpolate)

    /// The two sides of the (extended) stroke.
    let private sides (stroke : V2d[]) (r : Region) =
        let p = sidePolygon stroke r
        PolyRegion<V3d>.Intersection(r, p, interpolate),
        PolyRegion<V3d>.Difference(r, p, interpolate)

    /// Does the drawn stroke actually reach the region, as opposed to its infinite extension?
    ///
    /// Sampled rather than solved: a false negative is possible for a sliver thinner than the
    /// sample spacing, which is preferable to the fragility of exact segment/edge intersection at
    /// vertices - see the note on cutsThrough.
    let private strokeReaches (stroke : V2d[]) (r : Region) =
        let steps = 64
        seq {
            for i in 0 .. stroke.Length - 2 do
                for j in 1 .. steps - 1 ->
                    stroke.[i] + (stroke.[i + 1] - stroke.[i]) * (float j / float steps) }
        |> Seq.exists (fun p -> contains p r)

    /// A stroke cuts a region only if it is drawn *across* it: both ends outside, the stroke
    /// actually reaches the region, and it leaves area on both sides.
    ///
    /// The area test rather than counting boundary crossings. Crossing counts cannot tell "the
    /// stroke passes through the interior" from "the stroke runs along a boundary an earlier cut
    /// created" - in both cases the two edges adjacent to the collinear stretch register a
    /// transition. That made re-cutting a piece with the same stroke report a cut, which the
    /// round-trip property caught. Vertex and collinear special cases do not fix it; the
    /// formulation was wrong.
    ///
    /// The endpoint test alone is also insufficient: a short stroke beside a region, whose
    /// infinite extension passes through it, has both ends outside and area on both sides.
    let cutsThrough (stroke : V2d[]) (r : Region) =
        let stroke = dedupStroke stroke
        if r.IsEmpty || stroke.Length < 2 then false
        elif contains stroke.[0] r || contains stroke.[stroke.Length - 1] r then false
        elif not (strokeReaches stroke r) then false
        else
            let a, b = sides stroke r
            let total = area r
            area a > total * 1e-6 && area b > total * 1e-6

    /// Splits a region along a polyline stroke into its two sides. Returns the region unchanged
    /// when the stroke does not cut through it.
    ///
    /// Each side is one region, which for a concave shape or a zigzag stroke may itself hold
    /// several contours - a side is not necessarily connected. Splitting into connected
    /// components is a separate concern (it needs holes paired to their outer contour) and is
    /// deliberately not done here.
    let cut (stroke : V2d[]) (r : Region) : Region list =
        let stroke = dedupStroke stroke
        if not (cutsThrough stroke r) then [ r ]
        else
            let a, b = sides stroke r
            [ a; b ] |> List.filter (fun x -> not x.IsEmpty)

    // -----------------------------------------------------------------------------------------
    // merging
    // -----------------------------------------------------------------------------------------

    let merge (a : Region) (b : Region) : Region =
        PolyRegion<V3d>.Union(a, b, interpolate)

    let intersect (a : Region) (b : Region) : Region =
        PolyRegion<V3d>.Intersection(a, b, interpolate)
