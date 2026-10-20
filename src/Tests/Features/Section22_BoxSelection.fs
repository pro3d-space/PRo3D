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

/// a 1000 x 1000 px view, for the sample spacing along segments
let private viewport = V2d(1000.0, 1000.0)

/// the hit test with occlusion ignored
let private hits viewProj rect (annotations : list<Annotation>) =
    DrawingApp.annotationsTouchingRect viewProj viewport rect (fun _ -> true) annotations |> Set.ofList

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

        test "TC-22.2 Replace with nothing clears both selection kinds" {
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
                    viewport
                    (ndc -0.1 -0.1 0.4 0.2)
                    (fun _ -> true)
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

// ---------------------------------------------------------------------------------------------
// occlusion: a flat "terrain" at z = 0 seen from above, as the KdTree ray cast would report it

/// distance along a ray to the plane z = 0, the way Picking.castToSurfaces answers
let private terrainPlane (ray : Ray3d) : Option<float> =
    if ray.Direction.Z >= 0.0 then None
    else
        let t = -ray.Origin.Z / ray.Direction.Z
        if t > 0.0 then Some t else None

/// the depth offset annotations are drawn with, as the viewer passes it
let private depthOffsetClip =
    PRo3D.Base.Shader.DepthOffset.toClip PRo3D.Base.Shader.DepthOffset.annotationWorld

let private visibleOver (castRay : Ray3d -> Option<float>) viewProj =
    DrawingApp.pointVisible viewProj depthOffsetClip castRay

let private occludedHits viewProj rect (annotations : list<Annotation>) =
    DrawingApp.annotationsTouchingRect viewProj viewport rect (visibleOver terrainPlane viewProj) annotations
    |> Set.ofList

/// runs from (-5,0,1), above the terrain, to (5,0,-1), below it: hidden for x > 0
let private halfBuried () = anno Geometry.Line [ V3d(-5.0, 0.0, 1.0); V3d(5.0, 0.0, -1.0) ]

let private occlusionTests =
    testList "Occlusion" [

        test "TC-22.4 a point on the terrain is visible, one below it hidden, one above it visible" {
            let visible = visibleOver terrainPlane orthoViewProj
            Expect.isTrue  (visible (V3d(1.0, 1.0, 0.0)))  "on the surface: the depth offset keeps it in front, as on screen"
            Expect.isFalse (visible (V3d(1.0, 1.0, -1.0))) "a metre below the surface"
            Expect.isTrue  (visible (V3d(1.0, 1.0, 1.0)))  "a metre above it"
            Expect.isFalse (visibleOver terrainPlane perspectiveViewProj (V3d(1.0, 1.0, -1.0))) "also through a perspective camera"
        }

        test "TC-22.4 a box over a fully buried annotation selects nothing" {
            let buried = anno Geometry.Polyline [ V3d(-2.0, 0.0, -1.0); V3d(0.0, 2.0, -1.0); V3d(2.0, 0.0, -1.0) ]
            Expect.isEmpty (occludedHits orthoViewProj fullScreen [ buried ]) "hidden everywhere"
            Expect.equal (hits orthoViewProj fullScreen [ buried ]) (Set.singleton buried.key)
                "projection alone would have selected it"
        }

        test "TC-22.4 a partly buried annotation is selected only where the box covers its visible part" {
            let l = halfBuried ()
            Expect.equal (occludedHits orthoViewProj (ndc -0.4 -0.1 -0.2 0.1) [ l ]) (Set.singleton l.key) "box over the visible end"
            Expect.isEmpty (occludedHits orthoViewProj (ndc 0.2 -0.1 0.4 0.1) [ l ]) "box over the buried end only"
        }

        test "TC-22.4 a fully hidden annotation costs at most the ray cap" {
            let mutable rays = 0
            let counting (ray : Ray3d) = rays <- rays + 1; terrainPlane ray
            // a long, densely sampled polyline buried along its whole length
            let buried = anno Geometry.Polyline [ for i in 0 .. 50 -> V3d(-9.0 + 0.36 * float i, 0.0, -1.0) ]
            let selected =
                DrawingApp.annotationsTouchingRect orthoViewProj viewport fullScreen
                    (DrawingApp.pointVisible orthoViewProj depthOffsetClip counting) [ buried ]
            Expect.isEmpty selected "hidden"
            Expect.isLessThanOrEqual rays DrawingApp.maxVisibilityRaysPerAnnotation "bounded by the cap"
            Expect.isGreaterThan rays 1 "but it did look at more than one sample"
        }

        test "TC-22.4 a long hidden stretch does not use up the ray cap before the visible part" {
            // buried and densely sampled for its first half - far more samples than the cap - then
            // above the terrain: the rays must be spread over the whole path to reach the visible half
            let l =
                anno Geometry.Polyline [
                    for i in 0 .. 50 -> V3d(-9.0 + 0.18 * float i, 0.0, -1.0)
                    for i in 1 .. 10 -> V3d(0.9 * float i, 0.0, 1.0)
                ]
            Expect.equal (occludedHits orthoViewProj fullScreen [ l ]) (Set.singleton l.key) "the visible half selects it"
        }

        test "TC-22.5 a click on the visible part of a partly buried annotation finds a visible point" {
            let l = halfBuried ()
            let visible = visibleOver terrainPlane orthoViewProj
            match DrawingApp.annotationPointUnderCursor orthoViewProj viewport (V2d(-0.3, 0.0)) l,
                  DrawingApp.annotationPointUnderCursor orthoViewProj viewport (V2d( 0.3, 0.0)) l with
            | Some onVisible, Some onBuried ->
                Expect.floatClose Accuracy.medium onVisible.X -3.0 "the clicked point, at world x = -3"
                Expect.isTrue  (visible onVisible) "the visible part: the click selects"
                Expect.isFalse (visible onBuried)  "the buried part: the click is ignored"
            | _ -> failtest "the line is in front of the camera"
        }

        test "TC-22.5 the point under the cursor is perspective-correct" {
            // a line receding from the camera: its screen midpoint is not its world midpoint
            let l  = anno Geometry.Line [ V3d(-2.0, 0.0, 5.0); V3d(4.0, 0.0, -20.0) ]
            let vp = perspectiveViewProj
            let project (p : V3d) = vp.Forward.TransformPosProj p |> fun v -> V2d(v.X, v.Y)
            let a, b   = project (V3d(-2.0, 0.0, 5.0)), project (V3d(4.0, 0.0, -20.0))
            let cursor = (a + b) * 0.5
            match DrawingApp.annotationPointUnderCursor vp viewport cursor l with
            | Some p ->
                Expect.isLessThan ((project p - cursor).Length) 1e-9 "re-projects onto the cursor"
                Expect.isGreaterThan ((p - V3d(1.0, 0.0, -7.5)).Length) 0.5 "and is not the world midpoint"
            | None -> failtest "the line is in front of the camera"
        }

        test "TC-22.6 against real terrain: on the MSL surface visible, 1 m into it hidden" {
            let opc = Render.opcSurfaceDir
            if not (System.IO.Directory.Exists opc) then
                skiptest (sprintf "no MSL OPC at %s -- run: git submodule update --init src/Tests/resources" opc)
            PRo3D.Core.Surface.HeadlessPicking.init ()
            let kdTrees =
                PRo3D.Core.Surface.HeadlessPicking.loadHierarchies opc
                |> PRo3D.Core.Surface.HeadlessPicking.loadKdTreeMap
            if kdTrees.IsEmpty then skiptest (sprintf "no kd-trees under %s" opc)

            let mutable cache = HashMap.empty
            let castRay (ray : Ray3d) =
                let hit, c = PRo3D.Core.Surface.HeadlessPicking.intersectAll kdTrees cache (FastRay3d ray)
                cache <- c
                hit |> Option.map (fun h -> h.hit.RayHit.T)

            // look down onto the middle of the surface from above, along the local up. The MSL
            // outcrop is only metres across, so keep the camera at least 20 m away: the points
            // tested 1 m above the ground must stay in front of it.
            let bb     = kdTrees |> HashMap.toList |> List.map fst |> Box3d
            let centre = bb.Center
            let up     = if centre.Length > 1.0e5 then centre.Normalized else V3d.OOI
            let eye    = centre + up * max 20.0 (2.0 * bb.Size.Length)
            let toward = (centre - eye).Normalized
            match castRay (Ray3d(eye, toward)) with
            | None -> skiptest "the look-down ray misses the surface"
            | Some t ->
                let ground   = eye + toward * t
                let side     = if abs (Vec.dot up V3d.OOI) > 0.9 then V3d.IOO else V3d.OOI
                let view     = CameraView.lookAt eye ground (Vec.cross (Vec.cross toward side) toward)
                let viewProj = CameraView.viewTrafo view * Frustum.projTrafo (Frustum.perspective 60.0 1.0 (t * 4.0) 1.0)
                let visible  = DrawingApp.pointVisible viewProj depthOffsetClip castRay
                Expect.isTrue  (visible ground) "a point on the terrain"
                Expect.isFalse (visible (ground + toward * 1.0)) "the same point 1 m into the terrain"
                Expect.isTrue  (visible (ground - toward * 1.0)) "and 1 m above it"
        }
    ]

let tests =
    testList "Section 22 - Box selection of annotations" [
        hitTests
        selectionTests
        occlusionTests
    ]
