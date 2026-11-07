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

let private shapeOf' (a : Annotation) =
    match a.ellipticResults with
    | Some r -> r
    | None   -> failtestf "%s: no stored ellipse" a.text

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

        test "it has five handles, at the ends of its axes and its centre" {
            let c = ellipseCenter
            let a = drawEllipse () |> theOnly "drawn"
            Expect.isTrue (DrawingApp.isEditable a) "a Sky three-point ellipse with a stored shape is editable"
            let h = DrawingApp.editHandles a
            Expect.equal h.Length 5 "two per axis, and the centre"
            close "the centre handle" c h.[EllipseEdit.CentreHandle]
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
// moving a whole three-point ellipse by its centre, and the supporting profiles that follow
// ---------------------------------------------------------------------------------------------

/// the ellipse among its profiles
let private theEllipse (label : string) (m : DrawingModel) =
    match Draw.annotations m |> List.filter (fun a -> a.profileOf.IsNone) with
    | [ a ] -> a
    | xs -> failtestf "%s: expected one ellipse, got %d" label xs.Length

let private profilesOf (m : DrawingModel) =
    Draw.annotations m
    |> List.choose (fun a -> a.profileOf |> Option.map (fun l -> l.index, a))
    |> List.sortBy fst
    |> List.map snd

let private directionOf (a : Annotation) =
    a.profileOf |> Option.map (fun l -> l.direction) |> Option.defaultValue ""

let private azimuthOf (a : Annotation) =
    a.profileOf |> Option.map (fun l -> l.azimuth) |> Option.defaultValue nan

/// grab handle `i` of the ellipse and drop it at `target`
let private dropOn refSystem (i : int) (target : V3d) (m : DrawingModel) =
    let a = theEllipse "dropOn" m
    m
    |> fun m -> Draw.run refSystem m (DrawingAction.GrabVertex(a.key, i))
    |> fun m -> Draw.run refSystem m DrawingAction.ArmVertexGrab
    |> fun m -> Draw.run refSystem m (DrawingAction.MoveVertex(a.key, i, target, Draw.identityHit))

/// the ellipse with its four supporting profiles in `mode`, generated as the panel does
let private withProfiles (mode : EllipseProfileMode) =
    let m = drawEllipse ()
    let a = theEllipse "withProfiles" m
    let m = { m with annotations = { m.annotations with singleSelectLeaf = Some a.key } }
    let m = Draw.run Draw.refSystemMars m (DrawingAction.SetProfileEllipseMode mode)
    let m = Draw.run Draw.refSystemMars m (DrawingAction.GenerateSupportingProfiles (Some Draw.identityHit))
    Expect.equal (profilesOf m).Length 4 "four profiles to start with"
    m

let private endsOf (a : Annotation) =
    match IndexList.toList a.points with
    | [ s; e ] -> s, e
    | _ -> failtestf "profile %s has %d points" a.text (IndexList.count a.points)

/// how far `p` is from the infinite line through the two ends of profile `a`
let private offLine (p : V3d) (a : Annotation) =
    let s, e = endsOf a
    let d = (e - s).Normalized
    (p - s - d * Vec.dot (p - s) d).Length

/// grab handle `i` of the single ellipse and drop it at `target`, draped with `sampler`
let private dropWith (sampler : V3d -> Option<V3d>) (i : int) (target : V3d) (m : DrawingModel) =
    let refSystem = Draw.refSystemMars
    let a = theEllipse "dropWith" m
    m
    |> fun m -> Draw.run refSystem m (DrawingAction.GrabVertex(a.key, i))
    |> fun m -> Draw.run refSystem m DrawingAction.ArmVertexGrab
    |> fun m -> Draw.run refSystem m (DrawingAction.MoveVertex(a.key, i, target, sampler))

/// Terrain that is a plane through the ellipse centre, tilted 0.2 rad about y against the
/// ellipse's plane (z = const). Points are stamped onto it along the local up, as the Sky
/// projection does - not along its normal, which would hide a centre that slides sideways.
let private tiltedNormal = V3d(sin 0.2, 0.0, cos 0.2)
let private skyUp =
    let u, _ = Calculations.localFrame ReferenceSystem.initial.planet ReferenceSystem.initial.up.value ReferenceSystem.initial.north.value ellipseCenter
    u.Normalized
let private tiltedTerrain : V3d -> Option<V3d> =
    fun p -> Some (p - skyUp * (Vec.dot (p - ellipseCenter) tiltedNormal / Vec.dot skyUp tiltedNormal))

let private normalOf (r : EllipticAnnotationResult) = (Vec.cross r.semiMajorAxis r.semiMinorAxis).Normalized

let private ellipseMoveTests =
    testList "moving a three-point ellipse" [

        test "a move fits the plane to the terrain at the new place, axes kept" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let r0 = shapeOf "start" m
            let r = m |> dropWith tiltedTerrain EllipseEdit.CentreHandle (c + V3d(30.0, -20.0, 0.0)) |> shapeOf "moved"
            Expect.isGreaterThan (abs (Vec.dot (normalOf r) tiltedNormal)) (1.0 - 1e-9) "the ellipse lies in the terrain's plane now"
            match tiltedTerrain (c + V3d(30.0, -20.0, 0.0)) with
            | Some underCursor -> Expect.isLessThan (r.center - underCursor).Length 1e-3 "its centre on the terrain under the drop, not slid sideways"
            | None -> failtest "terrain"
            Expect.floatClose Accuracy.medium r.semiMajorAxis.Length r0.semiMajorAxis.Length "semi-major length kept"
            Expect.floatClose Accuracy.medium r.semiMinorAxis.Length r0.semiMinorAxis.Length "semi-minor length kept"
            Expect.isGreaterThan (Vec.dot r.semiMajorAxis.Normalized r0.semiMajorAxis.Normalized |> abs) 0.97 "the major axis turned only by the tilt"
        }

        test "dropping the centre where it is changes nothing, even on tilted terrain" {
            let start = drawEllipse ()
            let undoBefore = List.length start.undoStack
            let repeated =
                [ 0 .. 9 ] |> List.fold (fun m _ ->
                    match DrawingApp.editHandles (theEllipse "repeat" m) |> Array.tryItem EllipseEdit.CentreHandle with
                    | Some h -> dropWith tiltedTerrain EllipseEdit.CentreHandle h m
                    | None   -> failtest "no centre handle") start
            Expect.equal (shapeOf "repeated" repeated) (shapeOf "start" start) "not re-fitted, not moved"
            Expect.equal (List.length repeated.undoStack) undoBefore "nothing to undo"
        }

        test "a refused move says why" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let missing : V3d -> Option<V3d> = fun _ -> None
            let refused = m |> dropWith missing EllipseEdit.CentreHandle (c + V3d(30.0, -20.0, 0.0))
            Expect.equal (shapeOf "refused" refused) (shapeOf "start" m) "the ellipse stayed"
            Expect.equal (List.length refused.undoStack) (List.length m.undoStack) "nothing to undo"
            Expect.stringContains (refused.supportingProfiles.message |> Option.defaultValue "") "refused" "says so"
        }

        test "an axis-end drag keeps the plane, whatever the terrain" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let r0 = shapeOf "start" m
            let i = handleAt (c + V3d(0.0, 35.0, 0.0)) m
            let r = m |> dropWith tiltedTerrain i (c + V3d(0.0, 45.0, 0.0)) |> shapeOf "widened"
            Expect.isGreaterThan (abs (Vec.dot (normalOf r) (normalOf r0))) (1.0 - 1e-12) "not tilted"
        }

        test "the centre handle sits in the middle of the draped outline" {
            let c = ellipseCenter
            // after a tilted move the outline is on the terrain; the handle is its middle
            let moved = drawEllipse () |> dropWith tiltedTerrain EllipseEdit.CentreHandle (c + V3d(30.0, -20.0, 0.0))
            let a = theEllipse "moved" moved
            let outline = IndexList.toArray a.points
            let ring = if outline.Length > 1 && outline.[0] = outline.[outline.Length - 1] then outline.[.. outline.Length - 2] else outline
            let middle = (ring |> Array.fold (+) V3d.Zero) / float ring.Length
            match DrawingApp.editHandles a |> Array.tryItem EllipseEdit.CentreHandle with
            | Some h -> close "the centre handle" middle h
            | None -> failtest "no centre handle"
        }

        test "dragging the centre moves the whole ellipse in its plane, axes and orientation kept" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let r0 = shapeOf "start" m
            // off the plane by 7 m: projected onto it, like the axis ends
            let r = m |> dropHandle EllipseEdit.CentreHandle (c + V3d(30.0, -20.0, 7.0)) |> shapeOf "moved"
            close "centre at the drop, in the plane" (c + V3d(30.0, -20.0, 0.0)) r.center
            close "semi-major axis kept" r0.semiMajorAxis r.semiMajorAxis
            close "semi-minor axis kept" r0.semiMinorAxis r.semiMinorAxis
            // the axis keeps its direction in space; its azimuth is against local north, which
            // turns a hair over 36 m on Mars (1.2e-4 deg measured)
            Expect.isLessThan (abs (r.majorAxisAzimuth - r0.majorAxisAzimuth)) 1e-3 "azimuth kept, deg"
        }

        test "the moved outline is the old one, shifted, at the same size" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let before = theOnly "before" m
            let after = m |> dropHandle EllipseEdit.CentreHandle (c + V3d(30.0, -20.0, 0.0)) |> theOnly "after"
            let worst =
                after.points |> IndexList.toArray
                |> Array.map (fun p ->
                    let d = p - (c + V3d(30.0, -20.0, 0.0))
                    abs ((d.X / 60.0) ** 2.0 + (d.Y / 35.0) ** 2.0 - 1.0))
                |> Array.max
            Expect.isLessThan worst 1e-6 "every outline point is on the moved ellipse"
            Expect.isSome after.dnsResults "dip and strike recomputed"
            match before.results, after.results with
            | Some b, Some a -> Expect.floatClose Accuracy.medium a.area b.area "the same size"
            | _ -> failtest "results missing"
        }

        test "dropping the centre where it is, ten times, changes nothing" {
            let start = drawEllipse ()
            let r0 = shapeOf "start" start
            let repeated =
                [ 0 .. 9 ] |> List.fold (fun m _ ->
                    match DrawingApp.editHandles (theOnly "repeat" m) |> Array.tryItem EllipseEdit.CentreHandle with
                    | Some h -> dropHandle EllipseEdit.CentreHandle h m
                    | None   -> failtest "no centre handle") start
            let r = shapeOf "repeated" repeated
            close "centre" r0.center r.center
            close "semi-major" r0.semiMajorAxis r.semiMajorAxis
            close "semi-minor" r0.semiMinorAxis r.semiMinorAxis
        }

        test "a move is one undo entry, and undo puts the ellipse back" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let undoBefore = List.length m.undoStack
            let moved = m |> dropHandle EllipseEdit.CentreHandle (c + V3d(40.0, 0.0, 0.0))
            Expect.equal (List.length moved.undoStack) (undoBefore + 1) "one entry"
            let undone = Draw.run Draw.refSystemMars moved DrawingAction.Undo
            Expect.equal (shapeOf "undone" undone) (shapeOf "start" m) "the stored shape is back"
            Expect.equal (theOnly "undone" undone).points (theOnly "start" m).points "the outline is back"
        }

        test "compass profiles follow a move: the same profiles, through the new centre, still compass" {
            let c = ellipseCenter
            let shift = V3d(30.0, -20.0, 0.0)
            let m = withProfiles EllipseProfileMode.Compass
            let before = profilesOf m
            let moved = m |> dropOn Draw.refSystemMars EllipseEdit.CentreHandle (c + shift)
            let after = profilesOf moved
            let centre = (shapeOf' (theEllipse "moved" moved)).center
            close "the ellipse moved" (c + shift) centre
            Expect.equal (after |> List.map (fun a -> a.key)) (before |> List.map (fun a -> a.key)) "the same profiles, regenerated in place"
            Expect.equal (after |> List.map (fun a -> a.text)) (before |> List.map (fun a -> a.text)) "names kept"
            Expect.equal (after |> List.map directionOf) [ "N-S"; "NE-SW"; "E-W"; "NW-SE" ] "still compass"
            for (b, a) in List.zip before after do
                Expect.isLessThan (offLine centre a) 1e-6 (sprintf "%s runs through the new centre" a.text)
                Expect.floatClose Accuracy.medium (azimuthOf a) (azimuthOf b) (sprintf "%s keeps its bearing" a.text)
                Expect.isLessThan ((fst (endsOf a) - fst (endsOf b)) - shift).Length 1e-3 (sprintf "%s moved with the ellipse" a.text)
            Expect.stringContains (moved.supportingProfiles.message |> Option.defaultValue "") "4 profiles" "says so"
        }

        test "axis profiles follow a turned major axis" {
            let c = ellipseCenter
            let m = withProfiles EllipseProfileMode.MajorAxis
            let handles = DrawingApp.editHandles (theEllipse "start" m)
            match handles |> Array.tryFindIndex (fun h -> (h - (c + V3d(60.0, 0.0, 0.0))).Length < 1e-6) with
            | None -> failtest "no handle on the +x major end"
            | Some i ->
                let turned = m |> dropOn Draw.refSystemMars i (c + V3d(0.0, 120.0, 0.0))
                let r = shapeOf' (theEllipse "turned" turned)
                match profilesOf turned |> List.tryFind (fun a -> directionOf a = "major") with
                | None -> failtest "no major profile"
                | Some major ->
                    let s, e = endsOf major
                    Expect.isGreaterThan (abs (Vec.dot (e - s).Normalized r.semiMajorAxis.Normalized)) (1.0 - 1e-9) "the major profile lies along the new major axis"
                    Expect.isLessThan (offLine r.center major) 1e-6 "through the new centre"
        }

        test "undo takes the ellipse and its profiles back in one step" {
            let c = ellipseCenter
            let m = withProfiles EllipseProfileMode.Compass
            let moved = m |> dropOn Draw.refSystemMars EllipseEdit.CentreHandle (c + V3d(30.0, -20.0, 0.0))
            Expect.equal (List.length moved.undoStack) (List.length m.undoStack + 1) "one entry for ellipse and profiles"
            let undone = Draw.run Draw.refSystemMars moved DrawingAction.Undo
            Expect.equal (theEllipse "undone" undone).points (theEllipse "start" m).points "the ellipse is back"
            Expect.equal (profilesOf undone |> List.map (fun a -> a.points)) (profilesOf m |> List.map (fun a -> a.points)) "and its profiles"
        }

        test "an edit its profiles cannot follow is refused, and says why" {
            let c = ellipseCenter
            let m = withProfiles EllipseProfileMode.Compass
            // compass profiles need a reference body; without one they cannot be rebuilt
            let refused = m |> dropOn Draw.refSystemFlat EllipseEdit.CentreHandle (c + V3d(30.0, -20.0, 0.0))
            Expect.equal (theEllipse "refused" refused).points (theEllipse "start" m).points "the ellipse stayed"
            Expect.equal (profilesOf refused |> List.map (fun a -> a.points)) (profilesOf m |> List.map (fun a -> a.points)) "and its profiles"
            Expect.equal (List.length refused.undoStack) (List.length m.undoStack) "nothing to undo"
            Expect.isNone refused.vertexGrab "the grab is released"
            Expect.stringContains (refused.supportingProfiles.message |> Option.defaultValue "") "refused" "says so"
        }

        test "an ellipse without profiles moves without a profile message" {
            let c = ellipseCenter
            let m = drawEllipse ()
            let moved = m |> dropHandle EllipseEdit.CentreHandle (c + V3d(10.0, 0.0, 0.0))
            Expect.isNone moved.supportingProfiles.message "nothing to report"
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
        ellipseMoveTests
    ]
