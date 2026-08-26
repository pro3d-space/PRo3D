/// Section 20 - Boolean operations on annotations
///   Checkpoint 2 of plans/viewerIntegration.md: the UnionSelectedAnnotations message driven
///   through the real DrawingApp.update, exactly as the viewer dispatches it. No GL.
module PRo3D.Tests.Section20_BooleanOperations

open System

open Aardvark.Base
open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Tests

// ---------------------------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------------------------

let private run  = Draw.run Draw.refSystemFlat
let private rsys = Draw.refSystemFlat

/// Draw one closed polygon onto an existing model, the way the viewer does.
let private drawOn (points : List<V3d>) (m : DrawingModel) =
    let m = run m (DrawingAction.SetGeometry Geometry.Polygon)
    let m = run m DrawingAction.StartDrawing
    let m = points |> List.fold (fun m p -> Draw.click rsys p m) m
    run m DrawingAction.Finish

let private squareA = [ V3d(0.,0.,0.);  V3d(10.,0.,0.);  V3d(10.,10.,0.);  V3d(0.,10.,0.) ]
let private squareB = [ V3d(5.,5.,0.);  V3d(15.,5.,0.);  V3d(15.,15.,0.);  V3d(5.,15.,0.) ]
let private farC    = [ V3d(50.,50.,0.); V3d(60.,50.,0.); V3d(60.,60.,0.); V3d(50.,60.,0.) ]

/// U-shape and a bar capping its opening: the union encloses a hole and must be refused.
let private uShape = [ V3d(0.,0.,0.); V3d(25.,0.,0.); V3d(25.,25.,0.); V3d(17.,25.,0.); V3d(17.,8.,0.); V3d(8.,8.,0.); V3d(8.,25.,0.); V3d(0.,25.,0.) ]
let private bar    = [ V3d(-1.,23.,0.); V3d(26.,23.,0.); V3d(26.,30.,0.); V3d(-1.,30.,0.) ]

let private selectAll (m : DrawingModel) =
    Draw.annotations m
    |> List.fold (fun m a ->
        run m (DrawingAction.GroupsMessage (GroupsAppAction.AddLeafToSelection([], a.key, "")))) m

let private unionMsg = DrawingAction.UnionSelectedAnnotations (Some Draw.identityHit)

// ---------------------------------------------------------------------------------------------

let tests =
    testList "Section 20 - boolean operations on annotations" [

        test "TC-20.1 union replaces two overlapping polygons with one carrying the union area" {
            let two = DrawingModel.initialdrawing |> drawOn squareA |> drawOn squareB |> selectAll
            let originalKeys = Draw.annotations two |> List.map (fun a -> a.key)

            let after = run two unionMsg
            let ann = after |> Draw.theAnnotation "union result"

            Expect.equal ann.geometry Geometry.Polygon "the result is a polygon"
            Expect.floatClose Accuracy.medium
                (Calculations.calculatePolygonArea ann.points) 175.0
                "inclusion-exclusion: 100 + 100 - 25"
            Expect.isFalse (originalKeys |> List.contains ann.key) "the result is a new annotation"
            Expect.isSome ann.results "measurements are recomputed, not copied"

            // segment-less annotations render as an open polyline between consecutive points, so
            // the ring must be stored closed like drawn polygons - regression: unionFail.pro3d
            let pts = ann.points |> IndexList.toArray
            Expect.equal pts.[0] pts.[pts.Length - 1] "the ring is stored closed (first = last)"
        }

        test "TC-20.2 union of disjoint polygons explodes into one annotation per component" {
            let two = DrawingModel.initialdrawing |> drawOn squareA |> drawOn farC |> selectAll
            let after = run two unionMsg
            let annos = Draw.annotations after
            Expect.equal annos.Length 2 "two components, two annotations"
            let total = annos |> List.sumBy (fun a -> Calculations.calculatePolygonArea a.points)
            Expect.floatClose Accuracy.medium total 200.0 "areas add"
        }

        test "TC-20.3 undo restores the originals in one step; redo re-applies" {
            let two = DrawingModel.initialdrawing |> drawOn squareA |> drawOn squareB |> selectAll
            let after = run two unionMsg
            Expect.equal (Draw.annotations after |> List.length) 1 "union happened"

            let undone = run after DrawingAction.Undo
            Expect.equal (Draw.annotations undone |> List.length) 2 "one undo brings both back"

            let redone = run undone DrawingAction.Redo
            Expect.equal (Draw.annotations redone |> List.length) 1 "one redo unions again"
        }

        test "TC-20.4 a union that would enclose a hole is refused and changes nothing" {
            let two = DrawingModel.initialdrawing |> drawOn uShape |> drawOn bar |> selectAll
            let undoDepth = two.undoStack.Length

            let after = run two unionMsg
            Expect.equal (Draw.annotations after |> List.length) 2 "both operands survive"
            Expect.equal after.undoStack.Length undoDepth "no undo step for a refused union"
        }

        // --- cutting -------------------------------------------------------------------------

        test "TC-20.6 a stroke across the selected polygon cuts it into two closed pieces" {
            let m = DrawingModel.initialdrawing |> drawOn squareA
            let target = m |> Draw.theAnnotation "setup"
            // the freshly drawn annotation is already single-selected by finishAndAppend
            Expect.equal m.annotations.singleSelectLeaf (Some target.key) "the drawn polygon is selected"

            let m = run m (DrawingAction.AddCutStrokePoint (V3d(-5.,5.,0.)))
            let m = run m (DrawingAction.AddCutStrokePoint (V3d(15.,5.,0.)))
            Expect.isSome m.cutStroke "the stroke is under construction"

            let after = run m (DrawingAction.ApplyCutStroke (Some Draw.identityHit))
            Expect.isNone after.cutStroke "the stroke is consumed by the cut"
            let pieces = Draw.annotations after
            Expect.equal pieces.Length 2 "two pieces"
            Expect.isFalse (pieces |> List.exists (fun a -> a.key = target.key)) "the original is gone"
            let total = pieces |> List.sumBy (fun a -> Calculations.calculatePolygonArea a.points)
            Expect.floatClose Accuracy.medium total 100.0 "areas sum to the original"
            for p in pieces do
                let pts = p.points |> IndexList.toArray
                Expect.equal pts.[0] pts.[pts.Length - 1] "each piece ring is stored closed"

            let undone = run after DrawingAction.Undo
            Expect.equal (Draw.annotations undone |> List.length) 1 "one undo restores the original"
        }

        test "TC-20.7 the dry-run colours the stroke by whether it would cut" {
            // the freshly drawn annotation is already single-selected by finishAndAppend
            let m = DrawingModel.initialdrawing |> drawOn squareA

            // a stroke passing beside the polygon: red
            let missing =
                run (run m (DrawingAction.AddCutStrokePoint (V3d(-5.,50.,0.)))) (DrawingAction.AddCutStrokePoint (V3d(15.,50.,0.)))
            match missing.cutStroke with
            | Some s -> Expect.equal s.color.c (C4b(230uy, 70uy, 60uy)) "a missing stroke shows red"
            | None -> failtest "stroke expected"

            // a stroke across it: green
            let cutting =
                run (run m (DrawingAction.AddCutStrokePoint (V3d(-5.,5.,0.)))) (DrawingAction.AddCutStrokePoint (V3d(15.,5.,0.)))
            match cutting.cutStroke with
            | Some s -> Expect.equal s.color.c (C4b(60uy, 200uy, 90uy)) "a cutting stroke shows green"
            | None -> failtest "stroke expected"
        }

        test "TC-20.8 a refused cut keeps the stroke and changes nothing" {
            // the freshly drawn annotation is already single-selected by finishAndAppend
            let m = DrawingModel.initialdrawing |> drawOn squareA
            let m = run m (DrawingAction.AddCutStrokePoint (V3d(-5.,50.,0.)))
            let m = run m (DrawingAction.AddCutStrokePoint (V3d(15.,50.,0.)))
            let undoDepth = m.undoStack.Length

            let after = run m (DrawingAction.ApplyCutStroke (Some Draw.identityHit))
            Expect.equal (Draw.annotations after |> List.length) 1 "the annotation survives"
            Expect.isSome after.cutStroke "the stroke stays for correction"
            Expect.equal after.undoStack.Length undoDepth "no undo step for a refused cut"
        }

        test "TC-20.9 cut stroke points without a selected annotation are refused" {
            let m = DrawingModel.initialdrawing |> drawOn squareA
            let target = m |> Draw.theAnnotation "setup"
            // finishAndAppend selected the drawn polygon; re-selecting it toggles the
            // selection OFF, leaving nothing selected
            let m = run m (DrawingAction.GroupsMessage (GroupsAppAction.SingleSelectLeaf([], target.key, "")))
            Expect.isNone m.annotations.singleSelectLeaf "nothing is selected"
            let after = run m (DrawingAction.AddCutStrokePoint (V3d(-5.,5.,0.)))
            Expect.isNone after.cutStroke "no stroke without a selection"
        }

        test "TC-20.5 fewer than two selected annotations refuses the union" {
            let two = DrawingModel.initialdrawing |> drawOn squareA |> drawOn squareB
            let one =
                match Draw.annotations two with
                | a :: _ -> run two (DrawingAction.GroupsMessage (GroupsAppAction.AddLeafToSelection([], a.key, "")))
                | [] -> failtest "setup produced no annotations"
            let after = run one unionMsg
            Expect.equal (Draw.annotations after |> List.length) 2 "nothing merged"
        }
    ]
