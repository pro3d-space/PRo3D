module VertexEditingTests

open System
open Expecto
open Aardvark.Base
open Aardvark.Rendering
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

// ---------------------------------------------------------------------------------------------
// closing edge of a Sky polygon
// ---------------------------------------------------------------------------------------------

let closingEdgeTests =
    // clicks 5 m above the ground, samples on it: a straight closing edge would stay at z = 5
    let drawSampled () =
        let refSystem = Draw.refSystemFlat
        let start =
            Draw.startTool refSystem Geometry.Polygon
            |> fun m -> Draw.run refSystem m (DrawingAction.SetProjection Projection.Sky)
        let click m p = Draw.run refSystem m (DrawingAction.AddPointAdv(p, flattenToGround, None, "test-surface", None))
        let drawn = [ V3d(0,0,5); V3d(10,0,5); V3d(10,10,5) ] |> List.fold click start
        Draw.run refSystem drawn DrawingAction.Finish |> theOnly "polygon"

    testList "closing edge" [
        test "is sampled onto the surface like the other edges" {
            let a = drawSampled ()
            let closing =
                match IndexList.tryLast a.segments with
                | Some s -> s
                | None -> failtest "no closing segment"
            Expect.equal (IndexList.count a.segments) 3 "closed ring"
            // still stored first -> last (#821), what older releases read
            Expect.equal closing.startPoint (V3d(0,0,5)) "starts at the first click"
            Expect.equal closing.endPoint (V3d(10,10,5)) "ends at the last click"
            Expect.isGreaterThan (IndexList.count closing.points) 10 "sampled every metre"
            for p in closing.points do
                Expect.floatClose Accuracy.high p.Z 0.0 "sample on the surface"
        }

        test "the flattened line walks it from the last point back to the first" {
            let a = drawSampled ()
            let pts = Annotation.retrievePoints a |> Array.ofList
            Expect.equal pts.[pts.Length - 1] (V3d(0,0,5)) "the ring ends where it started"
            // no straight jump back to the first point before the closing edge
            for i in 1 .. pts.Length - 1 do
                Expect.isLessThan (Vec.Distance(pts.[i - 1], pts.[i])) 5.5 (sprintf "step %d" i)
        }

        test "a closing segment already stored last -> first is not turned around" {
            let a = drawSampled ()
            let turned =
                match IndexList.tryLast a.segments with
                | Some s ->
                    let s' = { startPoint = s.endPoint; endPoint = s.startPoint; points = s.points |> IndexList.toList |> List.rev |> IndexList.ofList }
                    { a with segments = a.segments |> IndexList.setAt (IndexList.count a.segments - 1) s' }
                | None -> failtest "no closing segment"
            Expect.equal (Annotation.retrievePoints turned) (Annotation.retrievePoints a) "the same ring either way"
        }
    ]

// ---------------------------------------------------------------------------------------------
// three-point ellipses, edited by their axis ends (EllipseEdit)
// ---------------------------------------------------------------------------------------------

/// near the Mars surface, like TC-3.6, so the Sky frame is a real one. The three clicks
/// span the plane z = c.Z: major axis along x (semi 60 m), minor along y (35 m).
let private ellipseCenter = V3d(693177.21, -3147511.67, 1070879.15)

let private drawEllipse () =
    let c = ellipseCenter
    Draw.drawFull Draw.refSystemMars Geometry.AxisEllipse true
        [ c + V3d(60.0, 0.0, 0.0); c - V3d(60.0, 0.0, 0.0); c + V3d(0.0, 35.0, 0.0) ]

let private shapeOf (label : string) (m : DrawingModel) =
    match (theOnly label m).ellipticResults with
    | Some r -> r
    | None   -> failtestf "%s: no stored ellipse" label

/// grab handle `i` of the single annotation and drop it at `target`, on Mars
let private dropHandle (i : int) (target : V3d) (m : DrawingModel) =
    let refSystem = Draw.refSystemMars
    let a = theOnly "dropHandle" m
    m
    |> fun m -> Draw.run refSystem m DrawingAction.StopDrawing
    |> fun m -> Draw.run refSystem m DrawingAction.StartPicking
    |> fun m -> Draw.run refSystem m (DrawingAction.GrabVertex(a.key, i))
    |> fun m -> Draw.run refSystem m DrawingAction.ArmVertexGrab
    |> fun m -> Draw.run refSystem m (DrawingAction.MoveVertex(a.key, i, target, Draw.identityHit))

/// The handle at `p`. Which end of an axis is handle 0 or 1 (2 or 3) follows the sign of the
/// stored semi-axis, which the construction does not promise, so tests find handles by place.
let private handleAt (p : V3d) (m : DrawingModel) =
    match DrawingApp.editHandles (theOnly "handleAt" m) |> Array.tryFindIndex (fun h -> (h - p).Length < 1e-6) with
    | Some i -> i
    | None   -> failtestf "no handle at %A" p

let private close (label : string) (expected : V3d) (actual : V3d) =
    Expect.isLessThan (actual - expected).Length 1e-6 (sprintf "%s: expected %A, got %A" label expected actual)

let private ellipseTests =
    testList "editing a three-point ellipse" [

        test "it has four handles, at the ends of its axes" {
            let c = ellipseCenter
            let a = drawEllipse () |> theOnly "drawn"
            Expect.isTrue (DrawingApp.isEditable a) "a Sky three-point ellipse with a stored shape is editable"
            let h = DrawingApp.editHandles a
            Expect.equal h.Length 4 "two per axis"
            // 0/1 are the ends of the major axis, 2/3 those of the minor one
            close "major ends" V3d.Zero (h.[0] + h.[1] - 2.0 * c)
            Expect.floatClose Accuracy.medium (h.[0] - h.[1]).Length 120.0 "major axis, m"
            Expect.floatClose Accuracy.medium (abs (h.[0] - h.[1]).Normalized.X) 1.0 "major along x"
            close "minor ends" V3d.Zero (h.[2] + h.[3] - 2.0 * c)
            Expect.floatClose Accuracy.medium (h.[2] - h.[3]).Length 70.0 "minor axis, m"
        }

        test "dragging a major end keeps the opposite end and the minor length" {
            let c = ellipseCenter
            // off the plane by 5 m: the drop is projected onto the stored plane, not re-fitted
            let m = drawEllipse ()
            let r = m |> dropHandle (handleAt (c + V3d(60.0, 0.0, 0.0)) m) (c + V3d(80.0, 0.0, 5.0)) |> shapeOf "moved"
            close "centre midway between the fixed and the new end" (c + V3d(10.0, 0.0, 0.0)) r.center
            Expect.floatClose Accuracy.medium r.semiMajorAxis.Length 70.0 "semi-major, m"
            Expect.floatClose Accuracy.medium r.semiMinorAxis.Length 35.0 "semi-minor kept, m"
            Expect.floatClose Accuracy.medium (abs r.semiMajorAxis.Normalized.X) 1.0 "still along x"
        }

        test "dragging a major end sideways turns the axis in the plane" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let r = m |> dropHandle (handleAt (c + V3d(60.0, 0.0, 0.0)) m) (c + V3d(0.0, 120.0, 0.0)) |> shapeOf "turned"
            // fixed end c - 60x, new end c + 120y
            close "centre" (c + V3d(-30.0, 60.0, 0.0)) r.center
            Expect.floatClose Accuracy.medium r.semiMajorAxis.Length (V2d(60.0, 120.0).Length * 0.5) "semi-major, m"
            Expect.floatClose Accuracy.medium r.semiMinorAxis.Length 35.0 "semi-minor kept, m"
            Expect.isLessThan (abs r.semiMajorAxis.Z) 1e-6 "stays in the plane"
            Expect.isLessThan (abs r.semiMinorAxis.Z) 1e-6 "stays in the plane"
        }

        test "dragging a minor end changes only the minor length" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let r = m |> dropHandle (handleAt (c - V3d(0.0, 35.0, 0.0)) m) (c + V3d(20.0, -50.0, -3.0)) |> shapeOf "widened"
            close "centre kept" c r.center
            Expect.floatClose Accuracy.medium r.semiMajorAxis.Length 60.0 "semi-major kept, m"
            Expect.floatClose Accuracy.medium r.semiMinorAxis.Length 50.0 "semi-minor: distance from the major axis, m"
        }

        test "a minor end dragged past the major length makes it the major axis" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let r = m |> dropHandle (handleAt (c + V3d(0.0, 35.0, 0.0)) m) (c + V3d(0.0, 90.0, 0.0)) |> shapeOf "flipped"
            Expect.floatClose Accuracy.medium r.semiMajorAxis.Length 90.0 "the long axis is now along y"
            Expect.floatClose Accuracy.medium r.semiMinorAxis.Length 60.0 "the old major is now the minor"
        }

        test "dropping a handle where it already is changes nothing, however often" {
            let start = drawEllipse ()
            let r0 = shapeOf "start" start
            let repeated =
                [ 0 .. 9 ] |> List.fold (fun m k ->
                    let i = k % 4
                    match DrawingApp.editHandles (theOnly "repeat" m) |> Array.tryItem i with
                    | Some h -> dropHandle i h m
                    | None   -> failtestf "no handle %d" i) start
            let r = shapeOf "repeated" repeated
            close "centre" r0.center r.center
            Expect.floatClose Accuracy.high r.semiMajorAxis.Length r0.semiMajorAxis.Length "semi-major length"
            Expect.floatClose Accuracy.high r.semiMinorAxis.Length r0.semiMinorAxis.Length "semi-minor length"
            Expect.isGreaterThan (abs (Vec.dot r.semiMajorAxis.Normalized r0.semiMajorAxis.Normalized)) (1.0 - 1e-12) "axis direction"
        }

        test "the outline, dip and strike and the results follow the edit" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let before = theOnly "before" m
            let after = m |> dropHandle (handleAt (c + V3d(0.0, 35.0, 0.0)) m) (c + V3d(0.0, 50.0, 0.0)) |> theOnly "after"
            Expect.notEqual after.points before.points "the outline is re-sampled"
            Expect.isGreaterThan (IndexList.count after.points) 3 "and still an outline"
            let worst =
                after.points |> IndexList.toArray
                |> Array.map (fun p ->
                    let d = p - c
                    abs ((d.X / 60.0) ** 2.0 + (d.Y / 50.0) ** 2.0 - 1.0))
                |> Array.max
            Expect.isLessThan worst 1e-6 "every outline point is on the new ellipse"
            Expect.isSome after.dnsResults "dip and strike recomputed"
            match before.results, after.results with
            | Some b, Some a -> Expect.isGreaterThan a.area b.area "a wider ellipse has more area"
            | _ -> failtest "results missing"
        }

        test "one undo entry per drop, and undo restores the ellipse" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let r0 = shapeOf "start" m
            let undoBefore = List.length m.undoStack
            let moved = m |> dropHandle (handleAt (c - V3d(60.0, 0.0, 0.0)) m) (c - V3d(90.0, 0.0, 0.0))
            Expect.equal (List.length moved.undoStack) (undoBefore + 1) "one entry for the whole drop"
            let undone = Draw.run Draw.refSystemMars moved DrawingAction.Undo
            Expect.equal (shapeOf "undone" undone) r0 "the stored shape is back"
            Expect.equal (theOnly "undone" undone).points (theOnly "start" m).points "the outline is back"
        }

        test "a drop that would collapse an axis is refused without an undo entry" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let undoBefore = List.length m.undoStack
            // a minor end dropped onto the major axis would make the ellipse a line
            let refused = m |> dropHandle (handleAt (c + V3d(0.0, 35.0, 0.0)) m) (c + V3d(30.0, 0.0, 0.0))
            Expect.equal (shapeOf "refused" refused) (shapeOf "start" m) "shape unchanged"
            Expect.equal (List.length refused.undoStack) undoBefore "nothing to undo"
            Expect.isNone refused.vertexGrab "the grab is still released"
        }

        test "an ellipse that is not a Sky three-point ellipse has no handles" {
            let a = drawEllipse () |> theOnly "drawn"
            let linear = { a with projection = Projection.Linear }
            Expect.isFalse (DrawingApp.isEditable linear) "imported ellipses (Linear, undraped) are left alone"
            Expect.isEmpty (DrawingApp.editHandles linear) "no handles"
            let noShape = { a with ellipticResults = None }
            Expect.isFalse (DrawingApp.isEditable noShape) "saved before the shape was stored"
            let fourPoint = { a with geometry = Geometry.Axis4PEllipse }
            Expect.isFalse (DrawingApp.isEditable fourPoint) "four-point ellipses are out of scope"
        }
    ]

// ---------------------------------------------------------------------------------------------
// adding points (#639): Ctrl+click on a segment, or on a terrain sample
// ---------------------------------------------------------------------------------------------

/// A camera looking straight down at the z = 0 plane over (cx, cy) from `height`, on a square
/// 1000 px viewport: (view-projection, viewport size).
let private camera (cx : float) (cy : float) (height : float) =
    let view = CameraView.lookAt (V3d(cx, cy, height)) (V3d(cx, cy, 0.0)) V3d.OIO
    let proj = Frustum.perspective 60.0 0.1 10000.0 1.0
    CameraView.viewTrafo view * Frustum.projTrafo proj, V2d(1000.0, 1000.0)

/// Where `p` lands on screen, in normalized device coordinates, offset by `pixels`.
let private ndcOf (viewProj : Trafo3d) (size : V2d) (pixels : V2d) (p : V3d) =
    let c = viewProj.Forward * V4d(p, 1.0)
    V2d(c.X / c.W, c.Y / c.W) + V2d(2.0 * pixels.X / size.X, 2.0 * pixels.Y / size.Y)

/// Ctrl+click at world point `p` (shifted by `pixels` on screen) on the single annotation
let private clickLine (cam : Trafo3d * V2d) (pixels : V2d) (p : V3d) (m : DrawingModel) =
    let viewProj, size = cam
    let a = theOnly "clickLine" m
    Draw.run Draw.refSystemFlat (startEditing m) (DrawingAction.ClickSegment(a.key, viewProj, size, ndcOf viewProj size pixels p))

/// arm the grab a ClickSegment started, then drop the new point at `target`
let private dropInserted (target : V3d) (sampler : V3d -> Option<V3d>) (m : DrawingModel) =
    let refSystem = Draw.refSystemFlat
    match m.vertexGrab with
    | Some g when g.inserting ->
        m
        |> fun m -> Draw.run refSystem m DrawingAction.ArmVertexGrab
        |> fun m -> Draw.run refSystem m (DrawingAction.InsertVertex(g.annotation, g.pointIndex, target, sampler))
    | other -> failtestf "expected a point being added in hand, got %A" other

/// The drawn line with the repeated end/start point of consecutive segments removed.
let private drawnLine (a : Annotation) =
    Annotation.retrievePoints a
    |> List.fold (fun (acc : list<V3d>) (p : V3d) -> match acc with q :: _ when (q - p).Length < 1e-12 -> acc | _ -> p :: acc) []
    |> List.rev

/// Every segment runs between the control points it is indexed by (closing: first -> last).
let private expectChained (a : Annotation) =
    let segs = IndexList.toArray a.segments
    for i in 0 .. segs.Length - 1 do
        match DrawingApp.segmentEndpoints a.points segs.Length i with
        | Some (s, e) ->
            Expect.equal segs.[i].startPoint s (sprintf "segment %d starts at its point" i)
            Expect.equal segs.[i].endPoint e (sprintf "segment %d ends at its point" i)
        | None -> failtestf "segment %d has no end points" i

/// What an edit may change: geometry, points, segments.
let private shapeOfLine (a : Annotation) = a.geometry, a.points, a.segments

let private straightSegments = [ V3d.Zero; V3d(10,0,0); V3d(20,0,0); V3d(30,0,0) ]

let private sampleOf (segment : int) (sample : int) (a : Annotation) =
    match IndexList.tryAt segment a.segments |> Option.bind (fun s -> IndexList.tryAt sample s.points) with
    | Some p -> p
    | None -> failtestf "no sample %d on segment %d" sample segment

let private insertTests =
    // From 20 m up the 1 m samples are ~43 px apart: far enough apart to snap to, and a click
    // between two of them is a segment click. From 100 m they are ~8.7 px apart.
    let near = camera 15.0 0.0 20.0
    let far  = camera 15.0 0.0 100.0

    testList "adding a point on a segment" [

        test "a click on an edge of a Linear polyline picks up a new point there; nothing is written yet" {
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Polyline false [ V3d.Zero; V3d(10,0,0); V3d(20,0,0) ]
            let before = theOnly "drawn" m
            let clicked = m |> clickLine near V2d.Zero (V3d(5.0, 0.0, 0.0))
            match clicked.vertexGrab with
            | Some g ->
                Expect.isTrue g.inserting "a point being added"
                Expect.equal g.pointIndex 1 "goes in between points 0 and 1"
                Expect.isLessThan (g.original - V3d(5.0, 0.0, 0.0)).Length 1e-6 "where the line was clicked"
            | None -> failtest "nothing in hand"
            Expect.equal (shapeOfLine (theOnly "after click" clicked)) (shapeOfLine before) "the annotation is untouched until the drop"
        }

        test "the drop inserts it, pushes one undo entry, and undo removes it" {
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Polyline false [ V3d.Zero; V3d(10,0,0); V3d(20,0,0) ]
            let undoBefore = List.length m.undoStack
            let target = V3d(5.0, 3.0, 0.0)
            let dropped = m |> clickLine near V2d.Zero (V3d(5.0, 0.0, 0.0)) |> dropInserted target Draw.identityHit
            let a = theOnly "dropped" dropped
            Expect.equal (IndexList.toList a.points) [ V3d.Zero; target; V3d(10,0,0); V3d(20,0,0) ] "inserted at index 1"
            Expect.isTrue a.segments.IsEmpty "a Linear annotation grows no segments"
            Expect.isNone dropped.vertexGrab "released"
            Expect.equal (List.length dropped.undoStack) (undoBefore + 1) "one entry"
            match a.results with
            | Some r -> Expect.floatClose Accuracy.medium r.wayLength (2.0 * sqrt 34.0 + 10.0) "length along the line recomputed"
            | None -> failtest "no results"
            let undone = Draw.run Draw.refSystemFlat dropped DrawingAction.Undo
            Expect.equal (IndexList.count (theOnly "undone" undone).points) 3 "back to three points"
        }

        test "ESC after the click leaves the annotation exactly as it was" {
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Polyline false [ V3d.Zero; V3d(10,0,0); V3d(20,0,0) ]
            let before = theOnly "drawn" m
            let cancelled =
                m |> clickLine near V2d.Zero (V3d(15.0, 0.0, 0.0))
                |> fun m -> Draw.run Draw.refSystemFlat m DrawingAction.ArmVertexGrab
                |> fun m -> Draw.run Draw.refSystemFlat m DrawingAction.CancelVertexEdit
            Expect.isNone cancelled.vertexGrab "released"
            Expect.equal (shapeOfLine (theOnly "after" cancelled)) (shapeOfLine before) "identical"
            Expect.equal (List.length cancelled.undoStack) (List.length m.undoStack) "no undo entry"
        }

        test "a line becomes a polyline" {
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Line true [ V3d.Zero; V3d(20,0,0) ]
            let a = m |> clickLine near V2d.Zero (V3d(10.0, 0.0, 0.0)) |> dropInserted (V3d(10.0, 2.0, 0.0)) Draw.identityHit |> theOnly "dropped"
            Expect.equal a.geometry Geometry.Polyline "three points are a polyline"
            Expect.equal (IndexList.count a.points) 3 "three points"
        }

        test "a click inside a polygon, away from its edges, adds nothing" {
            // a filled polygon is picked by its fill with the annotation's id too
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Polygon false [ V3d.Zero; V3d(20,0,0); V3d(20,20,0); V3d(0,20,0) ]
            let clicked = m |> clickLine (camera 10.0 10.0 20.0) V2d.Zero (V3d(10.0, 10.0, 0.0))
            Expect.isNone clicked.vertexGrab "nothing picked up in the middle"
        }

        test "a point annotation gets nothing" {
            let m = Draw.drawFull Draw.refSystemFlat Geometry.Point true [ V3d(10,0,0) ]
            let clicked = m |> clickLine near V2d.Zero (V3d(10.0, 0.0, 0.0))
            Expect.isNone clicked.vertexGrab "no point to add to a point"
        }

        test "on a draped polyline the clicked segment is split and both halves re-sampled; the others stay" {
            let m = drawProjected Geometry.Polyline straightSegments
            let before = theOnly "drawn" m
            // halfway between samples 4 and 5 of segment 1 (10 -> 20)
            let dropped =
                m |> clickLine near V2d.Zero (V3d(14.5, 0.0, 0.0))
                |> dropInserted (V3d(14.5, 4.0, 0.0)) flattenToGround
            let a = theOnly "dropped" dropped
            Expect.equal (IndexList.count a.points) 5 "one more point"
            Expect.equal (IndexList.count a.segments) 4 "one more segment"
            expectChained a
            Expect.equal (IndexList.tryAt 0 a.segments) (IndexList.tryAt 0 before.segments) "segment 0 untouched"
            Expect.equal (IndexList.tryAt 3 a.segments) (IndexList.tryAt 2 before.segments) "the last segment untouched"
            for i in [1; 2] do
                match IndexList.tryAt i a.segments with
                | Some s -> Expect.isGreaterThan (IndexList.count s.points) 2 (sprintf "half %d re-sampled" i)
                | None -> failtest "missing half"
        }

        test "on a draped ring the closing segment can be split too" {
            let m = drawProjected Geometry.Polygon [ V3d.Zero; V3d(20,0,0); V3d(20,20,0) ]
            // the closing edge runs (20,20) -> (0,0); click it between two samples
            let cam = camera 10.0 10.0 20.0
            let clicked = m |> clickLine cam V2d.Zero (V3d(10.25, 10.25, 0.0))
            match clicked.vertexGrab with
            | Some g -> Expect.equal g.pointIndex 3 "appended after the last point"
            | None -> failtest "nothing in hand"
            let target = V3d(8.0, 12.0, 0.0)
            let a = clicked |> dropInserted target flattenToGround |> theOnly "dropped"
            Expect.equal (IndexList.count a.points) 4 "four points"
            Expect.equal (IndexList.count a.segments) 4 "still a closed ring"
            expectChained a
            let line = drawnLine a
            Expect.equal (List.tryLast line) (Some V3d.Zero) "the ring still closes on the first point"
            Expect.isTrue (line |> List.contains target) "and runs through the new one"
        }

        test "where the samples run together on screen, a click on one adds a point rather than converting it" {
            let m = drawProjected Geometry.Polyline straightSegments
            let before = theOnly "drawn" m
            // from 100 m up the 1 m samples are 8.7 px apart, under sampleMinSpacingPixels
            let clicked = m |> clickLine far V2d.Zero (sampleOf 1 4 before)
            match clicked.vertexGrab with
            | Some g -> Expect.isTrue g.inserting "a point being added"
            | None -> failtest "nothing picked up"
            Expect.equal (shapeOfLine (theOnly "after" clicked)) (shapeOfLine before) "nothing converted"
        }
    ]

let private ringWithSampleOnClosingEdge (turn : bool) =
    let m = drawProjected Geometry.Polygon [ V3d.Zero; V3d(20,0,0); V3d(20,20,0) ]
    let m =
        if not turn then m
        else
            // a closing segment stored last -> first, as some older scenes have it
            let a = theOnly "drawn" m
            let k = IndexList.count a.segments - 1
            let turned =
                match IndexList.tryAt k a.segments with
                | Some s -> { startPoint = s.endPoint; endPoint = s.startPoint; points = s.points |> IndexList.toList |> List.rev |> IndexList.ofList }
                | None -> failtest "no closing segment"
            let a' = { a with segments = a.segments |> IndexList.setAt k turned }
            { m with annotations = m.annotations |> Groups.updateLeaf a.key (fun _ -> Leaf.Annotations a') }
    let a = theOnly "ring" m
    m, a, sampleOf (IndexList.count a.segments - 1) 5 a

let private promoteTests =
    let cam = camera 15.0 0.0 20.0

    testList "turning a terrain sample into a control point" [

        test "the sample becomes a control point where it is, and the drawn line does not move" {
            let m = drawProjected Geometry.Polyline straightSegments
            let before = theOnly "drawn" m
            let sample = sampleOf 1 3 before
            // 3 px off the sample: inside the snap radius
            let promoted = m |> clickLine cam (V2d(3.0, 0.0)) sample
            let a = theOnly "promoted" promoted
            Expect.isNone promoted.vertexGrab "nothing picked up"
            Expect.equal (IndexList.tryAt 2 a.points) (Some sample) "the sample is control point 2"
            Expect.equal (IndexList.count a.segments) 4 "segment 1 split in two"
            expectChained a
            Expect.equal (drawnLine a) (drawnLine before) "not a single sample moved"
            Expect.equal (List.length promoted.undoStack) (List.length m.undoStack + 1) "one undo entry"
            let undone = Draw.run Draw.refSystemFlat promoted DrawingAction.Undo
            Expect.equal (theOnly "undone" undone).points before.points "undo takes it back"
        }

        test "a line with a sample converted becomes a polyline" {
            let m = drawProjected Geometry.Line [ V3d.Zero; V3d(20,0,0) ]
            let before = theOnly "drawn" m
            let a = m |> clickLine (camera 10.0 0.0 20.0) V2d.Zero (sampleOf 0 7 before) |> theOnly "promoted"
            Expect.equal a.geometry Geometry.Polyline "three points are a polyline"
            Expect.equal (drawnLine a) (drawnLine before) "same line"
        }

        test "a sample on the closing segment (stored first -> last) is appended, and the ring is unchanged" {
            let m, before, sample = ringWithSampleOnClosingEdge false
            let a = m |> clickLine (camera sample.X sample.Y 20.0) V2d.Zero sample |> theOnly "promoted"
            Expect.equal (IndexList.tryLast a.points) (Some sample) "the new last point"
            Expect.equal (IndexList.count a.segments) 4 "still a closed ring"
            expectChained a
            Expect.equal (drawnLine a) (drawnLine before) "the ring runs through the same points"
        }

        test "a sample on a closing segment stored last -> first is appended, and the ring is unchanged" {
            let m, before, sample = ringWithSampleOnClosingEdge true
            let a = m |> clickLine (camera sample.X sample.Y 20.0) V2d.Zero sample |> theOnly "promoted"
            Expect.equal (IndexList.tryLast a.points) (Some sample) "the new last point"
            Expect.equal (IndexList.count a.segments) 4 "still a closed ring"
            Expect.equal (drawnLine a) (drawnLine before) "the ring runs through the same points"
        }
    ]

let tests () =
    testList "vertex editing" [
        touchedSegmentTests
        segmentEndpointTests
        resampleTests
        moveTests
        segmentUpdateTests
        closingEdgeTests
        ellipseTests
        insertTests
        promoteTests
    ]
