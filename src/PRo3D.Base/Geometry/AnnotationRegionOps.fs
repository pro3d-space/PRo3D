namespace PRo3D.Base.Geometry

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Base.Geometry.RegionOps

/// Boolean operations at the Annotation level: the bridge between the viewer's annotations and
/// RegionOps. All geometry happens in 2D chart space; all identity stays in world space through
/// the region's attribute channel, so output ring vertices that survive from the inputs are
/// exactly the input world points. See plans/viewerIntegration.md.
///
/// Pure by construction: the terrain re-projection is a parameter, the GroupsModel stays out,
/// and everything here runs in CI without a renderer.
module AnnotationRegionOps =

    type Refusal =
        /// union needs at least two operands
        | TooFewAnnotations
        /// the common chart cannot cover this annotation's points
        | ChartProjectionFailed of annotationKey : Guid
        /// the merged region encloses holes, which a single-ring annotation cannot store -
        /// refused rather than silently dropped (decided in plans/booleanOperations.md)
        | ResultHasHoles of holeCount : int
        | DegenerateInput of reason : string
        /// the stroke does not cut the annotation: an end inside it, or it misses entirely
        | StrokeDoesNotCut

    let private worldPoints (a : Annotation) = a.points |> IndexList.toArray

    /// One chart every operand projects through (decided): a plane fitted over the concatenated
    /// points of all operands. tryOfPlane rejects degenerate fits (collinear points, NaN).
    let commonChart (annotations : seq<Annotation>) : Option<SurfaceChart> =
        let pts = annotations |> Seq.collect (fun a -> a.points) |> Seq.toArray
        if pts.Length < 3 then None
        else OpcViewer.Base.PlaneFitting.planeFit pts |> SurfaceChart.tryOfPlane

    /// Chart-project an annotation's ring, keeping each vertex's world position as the region
    /// attribute. None when the ring is degenerate or the chart does not cover a point.
    let toRegion (chart : SurfaceChart) (a : Annotation) : Option<Region> =
        let world = PolygonFill.normalize PolygonFill.DefaultEpsilon (worldPoints a)
        if world.Length < 3 then None
        else
            let projected = world |> Array.choose chart.toChart
            if projected.Length <> world.Length then None
            else
                let polygon = Polygon2d<V3d>(projected, world)
                let region = PolyRegion<V3d>(polygon, TessellationRule.EvenOdd, interpolate)
                if region.IsEmpty then None else Some region

    /// Invented vertices (edge crossings) carry chord-blended world positions; land them on the
    /// terrain through the given raycast (decided). Vertices matching an input point are kept
    /// verbatim - the attribute channel passes them through bitwise, so the tolerance only has
    /// to reject genuinely different vertices. A failed projection falls back to the blend for
    /// that vertex alone rather than failing the operation.
    let private reprojectInvented
        (projectToSurface : V3d -> Option<V3d>)
        (inputs           : V3d[])
        (ring             : V3d[]) =
        ring |> Array.map (fun v ->
            let survived = inputs |> Array.exists (fun p -> Vec.Distance(p, v) < 1e-9)
            if survived then v
            else projectToSurface v |> Option.defaultValue v)

    /// World-space rings of the union, one per output annotation (components explode, holes
    /// refuse - the decided policy).
    let union
        (projectToSurface : V3d -> Option<V3d>)
        (annotations      : list<Annotation>)
        : Result<list<V3d[]>, Refusal> =

        match annotations with
        | [] | [ _ ] -> Result.Error TooFewAnnotations
        | annotations ->
            match commonChart annotations with
            | None -> Result.Error (DegenerateInput "the operands' points admit no plane fit")
            | Some chart ->
                let regions = annotations |> List.map (fun a -> a, toRegion chart a)
                match regions |> List.tryPick (fun (a, r) -> if r.IsNone then Some a.key else None) with
                | Some key -> Result.Error (ChartProjectionFailed key)
                | None ->
                    let operandRegions = regions |> List.choose snd
                    let merged = operandRegions |> List.reduce merge

                    // Terrain rings routinely self-intersect once projected (a hand-drawn spike
                    // folding over itself), and EvenOdd faithfully resolves the fold into a tiny
                    // extra contour. Every *legitimate* union component contains at least one
                    // whole operand, so a component smaller than the smallest operand is
                    // definitionally such an artifact - dropped, costing area far below the
                    // invariant tolerances. Micro-holes from the same folds are filled on the
                    // same grounds; only substantial holes refuse.
                    let minOperandArea = operandRegions |> List.map area |> List.min
                    let contours = RegionOps.signedContours merged

                    let realHoles =
                        contours
                        |> List.filter (fun (sa, _) -> sa < 0.0 && abs sa >= minOperandArea * 1e-3)
                    match realHoles with
                    | [] ->
                        let inputs = annotations |> Seq.collect worldPoints |> Seq.toArray
                        contours
                        |> List.filter (fun (sa, _) -> sa > 0.0 && sa >= minOperandArea * 0.5)
                        |> List.map (fun (_, contour) ->
                            contour
                            |> Array.map snd
                            |> reprojectInvented projectToSurface inputs)
                        |> Result.Ok
                    | hs -> Result.Error (ResultHasHoles hs.Length)

    /// World-space rings of the pieces after cutting the annotation along a terrain-picked
    /// polyline stroke, one ring per output annotation (a side with several components
    /// explodes; a cut of a hole-free ring cannot create holes).
    let cut
        (projectToSurface : V3d -> Option<V3d>)
        (annotation       : Annotation)
        (stroke           : V3d[])
        : Result<list<V3d[]>, Refusal> =

        match commonChart [ annotation ] with
        | None -> Result.Error (DegenerateInput "the annotation's points admit no plane fit")
        | Some chart ->
            match toRegion chart annotation with
            | None -> Result.Error (ChartProjectionFailed annotation.key)
            | Some region ->
                let stroke2d = stroke |> Array.choose chart.toChart
                if stroke2d.Length <> stroke.Length || stroke2d.Length < 2 then
                    Result.Error (DegenerateInput "the cut stroke does not project into the annotation's chart")
                else
                    match RegionOps.cut stroke2d region with
                    | [] -> Result.Error (DegenerateInput "the cut produced nothing")
                    | [ _untouched ] -> Result.Error StrokeDoesNotCut
                    | pieces ->
                        // Unlike union, blended attributes are NOT trustworthy here: vertices on
                        // the stroke blend the region's world points with the synthetic side
                        // polygon, whose "world" attributes are chart coordinates - and such a
                        // garbage blend can even land near a real input point by accident, so
                        // attributes cannot identify survivors either. The chart positions are
                        // exact, so survivors are matched in 2D and everything else is lifted
                        // from its chart position and landed on the terrain.
                        let inputPairs =
                            PolygonFill.normalize PolygonFill.DefaultEpsilon (worldPoints annotation)
                            |> Array.choose (fun w -> chart.toChart w |> Option.map (fun c -> c, w))
                        let toWorldVertex (p2d : V2d, attr : V3d) =
                            match inputPairs |> Array.tryFind (fun (c, _) -> Vec.Distance(c, p2d) < 1e-9) with
                            | Some (_, w) -> w
                            | None ->
                                match chart.toWorld p2d with
                                | Some w -> projectToSurface w |> Option.defaultValue w
                                | None -> attr    // last resort; plane charts never take it
                        pieces
                        |> List.collect RegionOps.outerContours
                        |> List.map (Array.map toWorldVertex)
                        |> Result.Ok
