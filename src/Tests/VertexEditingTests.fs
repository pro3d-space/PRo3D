module VertexEditingTests

open System
open Expecto
open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

open PRo3D.Tests

// ---------------------------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------------------------

let private eps = 1e-9

/// A sampler that projects nothing - stands in for a ray that misses the surface everywhere.
let private missEverywhere : V3d -> Option<V3d> = fun _ -> None

/// A sampler that drops every sample onto z = 0, so a re-sampled segment is distinguishable from
/// the straight line between its end points.
let private flattenToGround : V3d -> Option<V3d> = fun p -> Some (V3d(p.X, p.Y, 0.0))

/// Draws a polyline whose segments are actually generated. `startTool` selects Projection.Linear
/// for every non-ellipse tool, and Linear produces no segments at all, so the projection has to be
/// set back after choosing the geometry.
let private drawProjected (geom : Geometry) (points : V3d list) =
    let refSystem = Draw.refSystemFlat
    let start =
        Draw.startTool refSystem geom
        |> fun m -> Draw.run refSystem m (DrawingAction.SetProjection Projection.Sky)
    let drawn = points |> List.fold (fun m p -> Draw.click refSystem p m) start
    Draw.run refSystem drawn DrawingAction.Finish

let private theOnly (label : string) (m : DrawingModel) = Draw.theAnnotation label m

let private pointAt i (a : Annotation) =
    match IndexList.tryAt i a.points with
    | Some p -> p
    | None   -> failtestf "no point at index %d" i

/// Puts the model into the state Interactions.EditAnnotation runs in: draw off, pick on. The Draw
/// harness leaves draw = true after startTool, and the vertex arms - like annotation selection -
/// dispatch on (draw = false, pick = true).
let private startEditing (m : DrawingModel) =
    let refSystem = Draw.refSystemFlat
    m
    |> fun m -> Draw.run refSystem m DrawingAction.StopDrawing
    |> fun m -> Draw.run refSystem m DrawingAction.StartPicking

/// grab control point `i` of the single annotation, arm the grab, then drop it at `target`
let private grabAndDrop (i : int) (target : V3d) (sampler : V3d -> Option<V3d>) (m : DrawingModel) =
    let refSystem = Draw.refSystemFlat
    let a = theOnly "grabAndDrop" m
    m
    |> startEditing
    |> fun m -> Draw.run refSystem m (DrawingAction.GrabVertex(a.key, i))
    |> fun m -> Draw.run refSystem m DrawingAction.ArmVertexGrab
    |> fun m -> Draw.run refSystem m (DrawingAction.MoveVertex(a.key, i, target, sampler))

// ---------------------------------------------------------------------------------------------
// pure index arithmetic
// ---------------------------------------------------------------------------------------------

let private touchedSegmentTests =
    testList "touchedSegments" [

        // an open chain of n points has n-1 segments; segment j spans points[j] -> points[j+1]
        test "interior point of an open chain touches the segments either side" {
            Expect.equal (DrawingApp.touchedSegments 4 3 1 |> List.sort) [0; 1] "point 1 sits between segments 0 and 1"
            Expect.equal (DrawingApp.touchedSegments 4 3 2 |> List.sort) [1; 2] "point 2 sits between segments 1 and 2"
        }

        test "first point of an open chain touches only the first segment" {
            Expect.equal (DrawingApp.touchedSegments 4 3 0) [0] "nothing precedes point 0"
        }

        test "last point of an open chain touches only the last segment" {
            Expect.equal (DrawingApp.touchedSegments 4 3 3) [2] "nothing follows the last point"
        }

        // a ring has one segment per point: the extra one closes last -> first
        test "first point of a ring also touches the closing segment" {
            Expect.equal (DrawingApp.touchedSegments 4 4 0 |> List.sort) [0; 3] "closing segment is the last one"
        }

        test "last point of a ring also touches the closing segment" {
            Expect.equal (DrawingApp.touchedSegments 4 4 3 |> List.sort) [2; 3] "segment 2 before it, closing segment after"
        }

        test "interior point of a ring is unaffected by the closure" {
            Expect.equal (DrawingApp.touchedSegments 4 4 1 |> List.sort) [0; 1] "same as the open case"
        }

        test "out of range and empty inputs yield nothing" {
            Expect.isEmpty (DrawingApp.touchedSegments 4 3 -1) "negative index"
            Expect.isEmpty (DrawingApp.touchedSegments 4 3 4) "index past the end"
            Expect.isEmpty (DrawingApp.touchedSegments 4 0 1) "no segments to touch"
        }

        test "a result never indexes outside the segment list" {
            // a two point line: one segment, and both ends must resolve to it alone
            Expect.equal (DrawingApp.touchedSegments 2 1 0) [0] "start of a single segment"
            Expect.equal (DrawingApp.touchedSegments 2 1 1) [0] "end of a single segment"
        }
    ]

let private segmentEndpointTests =
    testList "segmentEndpoints" [

        let points = IndexList.ofList [ V3d.Zero; V3d.IOO; V3d.OIO; V3d.OOI ]

        test "a regular segment spans consecutive control points" {
            Expect.equal (DrawingApp.segmentEndpoints points 3 0) (Some (V3d.Zero, V3d.IOO)) "segment 0"
            Expect.equal (DrawingApp.segmentEndpoints points 3 2) (Some (V3d.OIO, V3d.OOI)) "segment 2"
        }

        test "the closing segment of a ring runs first to last" {
            // closePolyline builds it as { startPoint = first; endPoint = last }
            Expect.equal (DrawingApp.segmentEndpoints points 4 3) (Some (V3d.Zero, V3d.OOI)) "closing segment"
        }

        test "an out of range index yields nothing" {
            Expect.isNone (DrawingApp.segmentEndpoints points 3 3) "past the last open segment"
            Expect.isNone (DrawingApp.segmentEndpoints points 3 -1) "negative"
        }
    ]

// ---------------------------------------------------------------------------------------------
// resampleSegment
// ---------------------------------------------------------------------------------------------

let private resampleTests =
    testList "resampleSegment" [

        test "keeps the end points it was given" {
            let a = V3d(0.0, 0.0, 5.0)
            let b = V3d(10.0, 0.0, 5.0)
            let s = DrawingApp.resampleSegment 1.0 flattenToGround a b
            Expect.equal s.startPoint a "start"
            Expect.equal s.endPoint b "end"
        }

        test "interior samples are projected, not interpolated" {
            // the straight line sits at z = 5; the sampler drops everything to z = 0, so a sample
            // still at z = 5 would mean the projection was skipped
            let s = DrawingApp.resampleSegment 1.0 flattenToGround (V3d(0.0, 0.0, 5.0)) (V3d(10.0, 0.0, 5.0))
            Expect.isGreaterThan (IndexList.count s.points) 0 "some interior samples"
            for p in s.points do
                Expect.floatClose Accuracy.high p.Z 0.0 "sample was projected onto the ground"
        }

        test "sample count follows the sampling distance" {
            // 10 units at 1 unit steps: s = 1..10, and the step at s = 10 lands exactly on the end
            let s = DrawingApp.resampleSegment 1.0 flattenToGround V3d.Zero (V3d(10.0, 0.0, 0.0))
            Expect.equal (IndexList.count s.points) 10 "one sample per step"

            let coarse = DrawingApp.resampleSegment 5.0 flattenToGround V3d.Zero (V3d(10.0, 0.0, 0.0))
            Expect.equal (IndexList.count coarse.points) 2 "two steps at distance 5"
        }

        test "samples that miss the surface are dropped rather than interpolated" {
            let s = DrawingApp.resampleSegment 1.0 missEverywhere V3d.Zero (V3d(10.0, 0.0, 0.0))
            Expect.isEmpty s.points "nothing projected"
            Expect.equal s.startPoint V3d.Zero "start survives"
            Expect.equal s.endPoint (V3d(10.0, 0.0, 0.0)) "end survives"
        }

        test "a zero length segment produces no samples instead of NaN" {
            // reachable from vertex editing: dropping a control point onto its neighbour. The
            // direction is undefined here, and normalising it would fill points with NaN.
            let s = DrawingApp.resampleSegment 1.0 flattenToGround V3d.Zero V3d.Zero
            Expect.isEmpty s.points "no direction to walk along"
        }

        test "a non-positive sampling distance produces no samples instead of hanging" {
            let s = DrawingApp.resampleSegment 0.0 flattenToGround V3d.Zero (V3d(10.0, 0.0, 0.0))
            Expect.isEmpty s.points "would otherwise ask for infinitely many steps"
        }
    ]

// ---------------------------------------------------------------------------------------------
// moveVertex, through the real DrawingApp.update
// ---------------------------------------------------------------------------------------------

let private moveTests =
    testList "moving a control point" [

        test "the moved point takes the new position and the others do not budge" {
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Polyline false [ V3d.Zero; V3d(1,0,0); V3d(2,0,0) ]
            let target = V3d(1.0, 5.0, 0.0)
            let moved = m |> grabAndDrop 1 target Draw.identityHit |> theOnly "moved"

            Expect.equal (pointAt 1 moved) target "point 1 moved"
            Expect.equal (pointAt 0 moved) V3d.Zero "point 0 untouched"
            Expect.equal (pointAt 2 moved) (V3d(2,0,0)) "point 2 untouched"
        }

        test "the grab is released by the drop" {
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Polyline false [ V3d.Zero; V3d(1,0,0); V3d(2,0,0) ]
            let after = m |> grabAndDrop 1 (V3d(1.0, 5.0, 0.0)) Draw.identityHit
            Expect.isNone after.vertexGrab "nothing left in hand"
        }

        test "cancelling leaves the annotation exactly as it was" {
            let refSystem = Draw.refSystemFlat
            let m = Draw.drawFull refSystem Geometry.Polyline false [ V3d.Zero; V3d(1,0,0); V3d(2,0,0) ]
            let before = theOnly "before" m
            let a = before

            let cancelled =
                m
                |> startEditing
                |> fun m -> Draw.run refSystem m (DrawingAction.GrabVertex(a.key, 1))
                |> fun m -> Draw.run refSystem m DrawingAction.ArmVertexGrab
                |> fun m -> Draw.run refSystem m DrawingAction.CancelVertexEdit

            Expect.isNone cancelled.vertexGrab "grab released"
            Expect.equal (theOnly "after" cancelled).points before.points "points identical - the grab never mutated anything"
        }

        test "a drop pushes exactly one undo entry, and undo restores the old position" {
            let refSystem = Draw.refSystemFlat
            let m = Draw.drawFull refSystem Geometry.Polyline false [ V3d.Zero; V3d(1,0,0); V3d(2,0,0) ]
            let original = pointAt 1 (theOnly "original" m)
            let undoBefore = List.length m.undoStack

            let moved = m |> grabAndDrop 1 (V3d(1.0, 5.0, 0.0)) Draw.identityHit
            Expect.equal (List.length moved.undoStack) (undoBefore + 1) "one entry for the whole drop"

            let undone = Draw.run refSystem moved DrawingAction.Undo
            Expect.equal (pointAt 1 (theOnly "undone" undone)) original "back where it started"
        }

        test "measurements are recomputed on the drop" {
            // a 1-unit line stretched to 5 units: if results were left stale the length would not move
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Line true [ V3d.Zero; V3d(1,0,0) ]
            let before = theOnly "before" m
            let moved = m |> grabAndDrop 1 (V3d(5.0, 0.0, 0.0)) Draw.identityHit |> theOnly "moved"

            match before.results, moved.results with
            | Some b, Some a ->
                Expect.floatClose Accuracy.medium b.length 1.0 "drawn length"
                Expect.floatClose Accuracy.medium a.length 5.0 "length after the move"
            | _ -> failtest "annotation carries no results"
        }

        test "an out of range control point index changes nothing" {
            let refSystem = Draw.refSystemFlat
            let m = Draw.drawFull refSystem Geometry.Polyline false [ V3d.Zero; V3d(1,0,0); V3d(2,0,0) ]
            let a = theOnly "before" m
            let after =
                m
                |> startEditing
                |> fun m -> Draw.run refSystem m (DrawingAction.MoveVertex(a.key, 17, V3d(9,9,9), Draw.identityHit))
            Expect.equal (theOnly "after" after).points a.points "untouched"
        }
    ]

let private segmentUpdateTests =
    testList "segments after a move" [

        test "an annotation drawn without segments does not grow any" {
            // the default projection for every non-ellipse tool is Linear, which generates none;
            // an edit must not silently upgrade the annotation to a terrain-following one
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Polyline false [ V3d.Zero; V3d(1,0,0); V3d(2,0,0) ]
            Expect.isTrue (IndexList.isEmpty (theOnly "drawn" m).segments) "precondition: no segments"

            let moved = m |> grabAndDrop 1 (V3d(1.0, 5.0, 0.0)) flattenToGround |> theOnly "moved"
            Expect.isTrue (IndexList.isEmpty moved.segments) "still none"
        }

        test "only the segments either side of the moved point are re-sampled" {
            let m = drawProjected Geometry.Polyline [ V3d.Zero; V3d(10,0,0); V3d(20,0,0); V3d(30,0,0) ]
            let before = theOnly "drawn" m
            Expect.equal (IndexList.count before.segments) 3 "precondition: three segments"

            let moved = m |> grabAndDrop 1 (V3d(10.0, 10.0, 0.0)) flattenToGround |> theOnly "moved"

            let segAt i (a : Annotation) =
                match IndexList.tryAt i a.segments with
                | Some s -> s
                | None -> failtestf "no segment %d" i

            Expect.equal (segAt 0 moved).endPoint (V3d(10.0, 10.0, 0.0)) "segment 0 follows the point"
            Expect.equal (segAt 1 moved).startPoint (V3d(10.0, 10.0, 0.0)) "segment 1 follows the point"
            // the far segment must be untouched, contents and all
            Expect.equal (segAt 2 moved) (segAt 2 before) "segment 2 was not rebuilt"
        }

        test "moving the first point of a ring re-samples the closing segment" {
            let m = drawProjected Geometry.Polygon [ V3d.Zero; V3d(10,0,0); V3d(10,10,0) ]
            let before = theOnly "drawn" m
            // closePolyline appended the ring's closing segment, so segments = points
            Expect.equal (IndexList.count before.segments) (IndexList.count before.points) "precondition: closed ring"

            let target = V3d(-5.0, -5.0, 0.0)
            let moved = m |> grabAndDrop 0 target flattenToGround |> theOnly "moved"

            let closing =
                match IndexList.tryLast moved.segments with
                | Some s -> s
                | None -> failtest "no closing segment"

            // closePolyline builds the closing segment as first -> last
            Expect.equal closing.startPoint target "the closing segment tracks point 0"
        }
    ]

let tests () =
    testList "vertex editing" [
        touchedSegmentTests
        segmentEndpointTests
        resampleTests
        moveTests
        segmentUpdateTests
    ]
