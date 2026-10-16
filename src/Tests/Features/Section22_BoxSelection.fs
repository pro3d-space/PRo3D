/// Section 22 - Box selection of annotations (docs/AnnotationBoxSelection.md)
///   The screen-rectangle hit test (DrawingApp.annotationsTouchingRect) against fixed cameras,
///   and the SelectAnnotations message driven through the real DrawingApp.update. No GL: the
///   viewer only turns the dragged pixels into NDC and the camera into view * projection.
module PRo3D.Tests.Section22_BoxSelection

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI                          // ColorInput
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

/// looking straight down -z from (0,0,10), y up on screen
let private view = CameraView.lookAt (V3d(0.0, 0.0, 10.0)) V3d.Zero V3d.OIO

/// orthographic: world x/y in -10..10 map to NDC -1..1, so NDC = world / 10
let private orthoViewProj =
    CameraView.viewTrafo view * Frustum.projTrafo (Frustum.ortho (Box3d(V3d(-10.0, -10.0, 0.1), V3d(10.0, 10.0, 100.0))))

let private perspectiveViewProj =
    CameraView.viewTrafo view * Frustum.projTrafo (Frustum.perspective 60.0 0.1 100.0 1.0)

let private ndc (x0 : float) (y0 : float) (x1 : float) (y1 : float) = Box2d(V2d(x0, y0), V2d(x1, y1))

let private fullScreen = ndc -1.0 -1.0 1.0 1.0

let private anno (geometry : Geometry) (points : list<V3d>) : Annotation =
    let a =
        Annotation.make Projection.Linear None geometry None ({ c = C4b.White } : ColorInput)
            Annotation.Initial.thickness "test-surface"
    { a with points = IndexList.ofList points }

let private hits viewProj rect (annotations : list<Annotation>) =
    DrawingApp.annotationsTouchingRect viewProj rect annotations |> Set.ofList

let private run = Draw.run Draw.refSystemFlat

let private line x =
    fun (m : DrawingModel) ->
        let m = run m (DrawingAction.SetGeometry Geometry.Line)
        m |> Draw.click Draw.refSystemFlat (V3d(x, 0.0, 0.0)) |> Draw.click Draw.refSystemFlat (V3d(x, 5.0, 0.0))

/// finishing a drawing single-selects the new annotation; start from no selection
let private deselect (m : DrawingModel) =
    { m with annotations = { m.annotations with singleSelectLeaf = None; selectedLeaves = HashSet.empty } }

let private select mode ids (m : DrawingModel) = run m (DrawingAction.SelectAnnotations(mode, ids))

let private selectedIds (m : DrawingModel) =
    m.annotations.selectedLeaves |> HashSet.toList |> List.map (fun ts -> ts.id) |> List.sort

/// three lines, keys in drawing order, nothing selected
let private threeLines () =
    let m = DrawingModel.initialdrawing |> line 0.0 |> line 10.0 |> line 20.0 |> deselect
    let keys =
        Draw.annotations m
        |> List.sortBy (fun a -> a.points |> IndexList.tryFirst |> Option.map (fun p -> p.X) |> Option.defaultValue 0.0)
        |> List.map (fun a -> a.key)
    m, keys

// ---------------------------------------------------------------------------------------------

let private hitTests =
    testList "Hit test" [

        test "TC-22.1 a vertex inside the rectangle selects the annotation" {
            let p = anno Geometry.Point [ V3d(2.0, 2.0, 0.0) ]
            Expect.equal (hits orthoViewProj (ndc 0.1 0.1 0.3 0.3) [ p ]) (Set.singleton p.key) "point at NDC (0.2, 0.2)"
            Expect.isEmpty (hits orthoViewProj (ndc -0.5 -0.5 -0.1 -0.1) [ p ]) "rectangle elsewhere"
        }

        test "TC-22.1 a segment crossing the rectangle selects it even with no vertex inside" {
            let l = anno Geometry.Line [ V3d(-5.0, 0.0, 0.0); V3d(5.0, 0.0, 0.0) ]
            Expect.equal (hits orthoViewProj (ndc -0.1 -0.1 0.1 0.1) [ l ]) (Set.singleton l.key) "the middle of the line"
            Expect.isEmpty (hits orthoViewProj (ndc -0.1 0.2 0.1 0.4) [ l ]) "a rectangle above the line"
        }

        test "TC-22.1 a polyline is tested along every segment, not just its ends" {
            // a U: both ends far away at the top, the bend through the rectangle
            let u = anno Geometry.Polyline [ V3d(-5.0, 8.0, 0.0); V3d(-5.0, -5.0, 0.0); V3d(5.0, -5.0, 0.0); V3d(5.0, 8.0, 0.0) ]
            Expect.equal (hits orthoViewProj (ndc -0.1 -0.6 0.1 -0.4) [ u ]) (Set.singleton u.key) "the bottom edge"
            Expect.isEmpty (hits orthoViewProj (ndc -0.1 -0.1 0.1 0.1) [ u ]) "inside the U but touching nothing"
        }

        test "TC-22.1 hidden annotations are not selected" {
            let p = { anno Geometry.Point [ V3d.Zero ] with visible = false }
            Expect.isEmpty (hits orthoViewProj fullScreen [ p ]) "invisible"
        }

        test "TC-22.1 geometry behind the camera never lands on screen" {
            // the camera sits at z = 10 looking down -z: z = 20 is behind it
            let behind = anno Geometry.Point [ V3d(0.0, 0.0, 20.0) ]
            Expect.isEmpty (hits perspectiveViewProj fullScreen [ behind ]) "a point behind the camera"

            // a line from behind the camera to a point in front: only its visible part counts
            let through = anno Geometry.Line [ V3d(0.0, 0.0, 20.0); V3d(1.0, 1.0, 0.0) ]
            Expect.equal (hits perspectiveViewProj fullScreen [ through ]) (Set.singleton through.key) "visible part on screen"
            Expect.isEmpty (hits perspectiveViewProj (ndc -1.0 -1.0 -0.5 -0.5) [ through ])
                "the clipped part is not smeared across the opposite corner"
        }

        test "TC-22.1 draped segments are what is tested, not the straight control-point chord" {
            // control points at both ends; the draped path bulges up through y = 5
            let a = anno Geometry.Line [ V3d(-5.0, 0.0, 0.0); V3d(5.0, 0.0, 0.0) ]
            let draped =
                { a with
                    segments = IndexList.single
                        { startPoint = V3d(-5.0, 0.0, 0.0); endPoint = V3d(5.0, 0.0, 0.0)
                          points = IndexList.ofList [ V3d(0.0, 5.0, 0.0) ] } }
            Expect.equal (hits orthoViewProj (ndc -0.1 0.4 0.1 0.6) [ draped ]) (Set.singleton a.key) "the bulge"
            Expect.isEmpty (hits orthoViewProj (ndc -0.1 -0.1 0.1 0.1) [ draped ]) "the chord is not the path"
        }

        test "TC-22.1 every annotation geometry can be box-selected" {
            let all =
                [ Geometry.Point; Geometry.Line; Geometry.Polyline; Geometry.Polygon; Geometry.DnS
                  Geometry.TT; Geometry.Ellipse; Geometry.AxisEllipse; Geometry.Axis4PEllipse ]
                |> List.mapi (fun i g ->
                    let x = float i - 4.0
                    anno g [ V3d(x, 0.0, 0.0); V3d(x, 1.0, 0.0); V3d(x + 0.5, 1.0, 0.0) ])
            Expect.equal (hits orthoViewProj fullScreen all) (all |> List.map (fun a -> a.key) |> Set.ofList)
                "all nine geometries"
        }
    ]

let private selectionTests =
    testList "SelectAnnotations" [

        test "TC-22.2 Replace makes the batch the whole selection and drops the green one" {
            let m, keys = threeLines ()
            match keys with
            | [ a; b; c ] ->
                let m = run m (DrawingAction.PickDirectly c)
                let m = m |> select SelectionMode.Replace [ a; b ]
                Expect.equal (selectedIds m) (List.sort [ a; b ]) "a and b"
                Expect.isNone m.annotations.singleSelectLeaf "c is no longer selected"
            | _ -> failtest "expected three lines"
        }

        test "TC-22.2 Replace with nothing (click on empty space) clears both selection kinds" {
            let m, keys = threeLines ()
            match keys with
            | [ a; b; _ ] ->
                let m = m |> select SelectionMode.Add [ a ] |> fun m -> run m (DrawingAction.PickDirectly b)
                let m = m |> select SelectionMode.Replace []
                Expect.isEmpty (selectedIds m) "no multi-selection"
                Expect.isNone m.annotations.singleSelectLeaf "no single selection"
            | _ -> failtest "expected three lines"
        }

        test "TC-22.2 Add extends the selection without duplicating a viewport pick" {
            let m, keys = threeLines ()
            match keys with
            | [ a; b; _ ] ->
                // a viewport Shift+pick stores the root path
                let m = run m (DrawingAction.GroupsMessage (GroupsAppAction.AddLeafToSelection([], a, "")))
                let m = m |> select SelectionMode.Add [ a; b ]
                Expect.equal (selectedIds m) (List.sort [ a; b ]) "a once, b added"
            | _ -> failtest "expected three lines"
        }

        test "TC-22.2 Toggle flips each annotation, the green one included" {
            let m, keys = threeLines ()
            match keys with
            | [ a; b; c ] ->
                let m = m |> select SelectionMode.Add [ a ] |> fun m -> run m (DrawingAction.PickDirectly c)
                Expect.equal m.annotations.singleSelectLeaf (Some c) "c is green"
                let m = m |> select SelectionMode.Toggle [ a; b; c ]
                Expect.equal (selectedIds m) [ b ] "a off, b on, c off"
                Expect.isNone m.annotations.singleSelectLeaf "c was toggled off"
            | _ -> failtest "expected three lines"
        }

        test "TC-22.2 a selected leaf in a sub-group gets its real group path" {
            let m0 = DrawingModel.initialdrawing
            let g1 = GroupsApp.update m0.annotations (GroupsAppAction.AddGroup rootPath)
            match g1.rootGroup.subNodes.TryGetIndex 0, g1.rootGroup.subNodes |> IndexList.tryFirst with
            | Some subIdx, Some subNode ->
                let g2 = { g1 with activeGroup = { id = subNode.key; path = [ subIdx ]; name = subNode.name } }
                let m  = { m0 with annotations = g2 } |> line 0.0 |> deselect
                let a  = Draw.theAnnotation "one line in the sub-group" m
                let m  = m |> select SelectionMode.Replace [ a.key ]
                match m.annotations.selectedLeaves |> HashSet.toList with
                | [ ts ] -> Expect.equal ts.path [ subIdx ] "the sub-group's path, not the root"
                | other  -> failtestf "expected one selected leaf, got %d" other.Length
            | _ -> failtest "AddGroup did not create an addressable subgroup"
        }

        test "TC-22.2 unknown ids are ignored" {
            let m, _ = threeLines ()
            let m = m |> select SelectionMode.Replace [ Guid.NewGuid() ]
            Expect.isEmpty (selectedIds m) "nothing selected"
        }

        test "TC-22.3 box-select, Delete, one Undo restores everything" {
            let m, keys = threeLines ()
            // the drawn lines run from (x,0) to (x,5), x = 0, 10, 20: the box spans the first two
            let boxed =
                DrawingApp.annotationsTouchingRect
                    (CameraView.viewTrafo view * Frustum.projTrafo (Frustum.ortho (Box3d(V3d(-30.0, -30.0, 0.1), V3d(30.0, 30.0, 100.0)))))
                    (ndc -0.1 -0.1 0.4 0.2)
                    (Draw.annotations m)
            match keys with
            | [ a; b; c ] ->
                Expect.equal (List.sort boxed) (List.sort [ a; b ]) "the box touches the first two lines"
                let selected = m |> select SelectionMode.Replace boxed
                let deleted  = run selected DrawingAction.DeleteSelectedAnnotations
                Expect.equal (Draw.annotations deleted |> List.map (fun x -> x.key)) [ c ] "only the third line is left"
                let undone = run deleted DrawingAction.Undo
                Expect.equal (Draw.annotations undone |> List.length) 3 "one undo brings both back"
            | _ -> failtest "expected three lines"
        }
    ]

let tests =
    testList "Section 22 - Box selection of annotations" [
        hitTests
        selectionTests
    ]
