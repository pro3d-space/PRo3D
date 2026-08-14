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

    /// A rectangle covering the region, lying entirely on one side of the infinite line through
    /// p0..p1. Intersecting with it and subtracting it gives the two sides of the cut.
    let private halfPlaneQuad (p0 : V2d) (p1 : V2d) (r : Region) =
        let bb = bounds r
        let reach = (bb.Size.Length + Vec.Distance(p0, p1)) * 4.0 + 1.0
        let d = (p1 - p0).Normalized
        let n = V2d(-d.Y, d.X)
        // an origin that lies on the line but near the region, so the quad stays well-conditioned
        // even when the stroke was drawn far away
        let o = p0 + d * Vec.Dot(bb.Center - p0, d)
        let pts =
            [| o - d * reach
               o + d * reach
               o + d * reach + n * reach
               o - d * reach + n * reach |]
        let polygon = Polygon2d<V3d>(pts, pts |> Array.map toV3d)
        PolyRegion<V3d>(polygon, TessellationRule.EvenOdd, interpolate)

    /// The two sides of the infinite line through p0..p1.
    let private sides (p0 : V2d) (p1 : V2d) (r : Region) =
        let quad = halfPlaneQuad p0 p1 r
        PolyRegion<V3d>.Intersection(r, quad, interpolate),
        PolyRegion<V3d>.Difference(r, quad, interpolate)

    /// Does the drawn segment actually reach the region, as opposed to its infinite extension?
    ///
    /// Sampled rather than solved: a false negative is possible for a sliver thinner than the
    /// sample spacing, which is preferable to the fragility of exact segment/edge intersection at
    /// vertices - see the note on cutsThrough.
    let private segmentReaches (p0 : V2d) (p1 : V2d) (r : Region) =
        let steps = 64
        seq { 1 .. steps - 1 }
        |> Seq.exists (fun i -> contains (p0 + (p1 - p0) * (float i / float steps)) r)

    /// A stroke cuts a region only if it is drawn *across* it: both ends outside, the segment
    /// actually reaches the region, and the line leaves area on both sides.
    ///
    /// The area test rather than counting boundary crossings. Crossing counts cannot tell "the
    /// line passes through the interior" from "the line runs along a boundary an earlier cut
    /// created" - in both cases the two edges adjacent to the collinear stretch register a
    /// transition. That made re-cutting a piece with the same line report a cut, which the
    /// round-trip property caught. Vertex and collinear special cases do not fix it; the
    /// formulation was wrong.
    ///
    /// The endpoint test alone is also insufficient: a short stroke beside a region, whose
    /// infinite extension passes through it, has both ends outside and area on both sides.
    let cutsThrough (p0 : V2d) (p1 : V2d) (r : Region) =
        if r.IsEmpty || Vec.Distance(p0, p1) < 1e-9 then false
        elif contains p0 r || contains p1 r then false
        elif not (segmentReaches p0 p1 r) then false
        else
            let a, b = sides p0 p1 r
            let total = area r
            area a > total * 1e-6 && area b > total * 1e-6

    /// Splits a region along a line into its two sides. Returns the region unchanged when the
    /// stroke does not cut through it.
    ///
    /// Each side is one region, which for a concave shape may itself hold several contours - a
    /// side is not necessarily connected. Splitting into connected components is a separate
    /// concern (it needs holes paired to their outer contour) and is deliberately not done here.
    let cut (p0 : V2d) (p1 : V2d) (r : Region) : Region list =
        if not (cutsThrough p0 p1 r) then [ r ]
        else
            let a, b = sides p0 p1 r
            [ a; b ] |> List.filter (fun x -> not x.IsEmpty)

    // -----------------------------------------------------------------------------------------
    // merging
    // -----------------------------------------------------------------------------------------

    let merge (a : Region) (b : Region) : Region =
        PolyRegion<V3d>.Union(a, b, interpolate)

    let intersect (a : Region) (b : Region) : Region =
        PolyRegion<V3d>.Intersection(a, b, interpolate)
