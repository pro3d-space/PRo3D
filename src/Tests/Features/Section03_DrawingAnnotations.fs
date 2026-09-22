/// Section 3 — Drawing and Managing Annotations
///   TC-3.1 Point, TC-3.2 Line, TC-3.3 Polyline, TC-3.4 Polygon, TC-3.5 DnS,
///   TC-3.6 AxisEllipse, TC-3.7 Axis4PEllipse, TC-3.8 Projection Modes,
///   TC-3.9/3.10 Pick Annotation (list / 3D view),
///   TC-3.11/3.12 Pick Surface (list / 3D view)
///
///   Annotations are built exactly as the viewer does: a geometry is chosen,
///   drawing is started, and each Ctrl+click is fed to the real DrawingApp.update
///   as an AddPointAdv — the same call matchPickingInteraction makes.
module PRo3D.Tests.Section03_DrawingAnnotations

open System

open Aardvark.Base
open Aardvark.UI                         // ColorInput
open Aardvark.UI.Primitives              // NumericInput

open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.Tests

let private drawingE2ETests =
    testList "Drawing annotations" [

        // TC-3.1 Draw Point

        test "TC-3.1 a Ctrl+click in Point mode creates one point annotation" {
            let started = Draw.startTool Draw.refSystemFlat Geometry.Point
            Expect.isEmpty (Draw.annotations started) "nothing is drawn before the first click"
            let m   = Draw.click Draw.refSystemFlat (V3d(1.0, 2.0, 3.0)) started
            let ann = m |> Draw.theAnnotation "point"
            Expect.equal ann.geometry Geometry.Point "geometry should be Point"
            Expect.equal (ann.points |> IndexList.count) 1 "a point annotation has a single vertex"
        }

        // TC-3.2 Draw Line

        test "TC-3.2 two clicks in Line mode make a two-point line with a length" {
            let ann = Draw.drawFull Draw.refSystemFlat Geometry.Line true [ V3d(0.0, 0.0, 0.0); V3d(3.0, 4.0, 0.0) ]
                      |> Draw.theAnnotation "line"
            Expect.equal ann.geometry Geometry.Line "geometry should be Line"
            Expect.equal (ann.points |> IndexList.count) 2 "a line has two points"
            match ann.results with
            | Some r -> Expect.floatClose Accuracy.medium r.length 5.0 "length should be the 3-4-5 distance"
            | None   -> failtest "a line should carry measurement results"
        }

        test "TC-3.2 a line auto-finishes after the second point" {
            let afterOne = Draw.click Draw.refSystemFlat (V3d(0.0, 0.0, 0.0)) (Draw.startTool Draw.refSystemFlat Geometry.Line)
            Expect.isEmpty (Draw.annotations afterOne) "one point is not enough to finish a line"
            let afterTwo = Draw.click Draw.refSystemFlat (V3d(1.0, 0.0, 0.0)) afterOne
            Expect.equal (Draw.annotations afterTwo |> List.length) 1 "the second point finishes the line"
        }

        // TC-3.3 Draw Polyline

        test "TC-3.3 a polyline collects points until Enter finishes it" {
            let pts     = [ V3d(0.0, 0.0, 0.0); V3d(1.0, 0.0, 0.0); V3d(2.0, 1.0, 0.0) ]
            let working = pts |> List.fold (fun m p -> Draw.click Draw.refSystemFlat p m)
                                           (Draw.startTool Draw.refSystemFlat Geometry.Polyline)
            Expect.isEmpty (Draw.annotations working) "an unfinished polyline is not in the list yet"
            Expect.equal (working.working |> Option.map (fun w -> w.points.Count)) (Some 3)
                "the working polyline holds every clicked point"
            let ann = Draw.run Draw.refSystemFlat working DrawingAction.Finish |> Draw.theAnnotation "polyline"
            Expect.equal ann.geometry Geometry.Polyline "geometry should be Polyline"
            Expect.equal (ann.points |> IndexList.count) 3 "the finished polyline keeps its three points"
        }

        // TC-3.4 Draw Polygon

        test "TC-3.4 a polygon closes back to its first point on finish" {
            let pts = [ V3d(0.0, 0.0, 0.0); V3d(2.0, 0.0, 0.0); V3d(2.0, 2.0, 0.0) ]
            let ann = Draw.drawFull Draw.refSystemFlat Geometry.Polygon false pts |> Draw.theAnnotation "polygon"
            Expect.equal ann.geometry Geometry.Polygon "geometry should be Polygon"
            // closePolyline repeats the first point so the ring is closed
            Expect.equal (ann.points |> IndexList.count) 4 "closing the polygon repeats the first point"
            Expect.equal (ann.points |> IndexList.toList |> List.last) (List.head pts)
                "the ring closes back to the first point"
        }

        // TC-3.5 Draw Dip and Strike

        test "TC-3.5 a DnS annotation fits a plane and computes dip/strike" {
            // three points spanning a tilted layer
            let pts = [ V3d(0.0, 0.0, 0.0); V3d(4.0, 0.0, 1.0); V3d(0.0, 4.0, 1.0) ]
            let ann = Draw.drawFull Draw.refSystemFlat Geometry.DnS false pts |> Draw.theAnnotation "dns"
            Expect.equal ann.geometry Geometry.DnS "geometry should be DnS"
            Expect.isTrue ann.showDns "a DnS annotation shows its dip/strike plane"
            Expect.isSome ann.dnsResults "dip and strike should be computed on finish"
        }

        // TC-3.6 Draw AxisEllipse
        //
        // What is checked is the *drawing* outcome: the axis points are turned into a
        // sampled ellipse outline. `ellipticResults` — the geographical (lat/lon) ellipse
        // used by the GeoJSON export — is deliberately not asserted: getFinishedAnnotation
        // currently takes the plane-based branch (`let geo = false`), which computes the
        // geographical ellipse and then discards it, setting `ellipticResults = None`
        // (Drawing-App.fs). The geographical branch above it does populate the field.

        test "TC-3.6 three clicks in AxisEllipse mode fit an ellipse" {
            // points near the Mars surface so the geographical projection is valid
            let c  = V3d(693177.21, -3147511.67, 1070879.15)
            let m  = Draw.drawFull Draw.refSystemMars Geometry.AxisEllipse true
                        [ c + V3d(60.0, 0.0, 0.0); c - V3d(60.0, 0.0, 0.0); c + V3d(0.0, 35.0, 0.0) ]
            let ann = m |> Draw.theAnnotation "axis-ellipse"
            Expect.equal ann.geometry Geometry.AxisEllipse "geometry should be AxisEllipse"
            Expect.equal ann.projection Projection.Sky "ellipses default to the Sky projection"
            Expect.isGreaterThan (ann.points |> IndexList.count) 3 "the ellipse outline is sampled into many points"
        }

        // TC-3.7 Draw Axis4PEllipse
        //
        // The first two clicks are the ends of the major axis, the last two give the
        // semi-minor length on either side of it, so the outline is stitched together
        // from two half-ellipses that share the major axis.

        test "TC-3.7 four clicks in Axis4PEllipse mode fit an ellipse" {
            let c  = V3d(693177.21, -3147511.67, 1070879.15)
            let m  = Draw.drawFull Draw.refSystemMars Geometry.Axis4PEllipse true
                        [ c + V3d(60.0, 0.0, 0.0); c - V3d(60.0, 0.0, 0.0)
                          c + V3d(0.0, 35.0, 0.0); c - V3d(0.0, 25.0, 0.0) ]
            let ann = m |> Draw.theAnnotation "axis4p-ellipse"
            Expect.equal ann.geometry Geometry.Axis4PEllipse "geometry should be Axis4PEllipse"
            Expect.isGreaterThan (ann.points |> IndexList.count) 4 "the ellipse outline is sampled into many points"
        }

        // TC-3.8 Projection Modes

        test "TC-3.8 SetProjection switches the active projection" {
            let m = Draw.run Draw.refSystemFlat DrawingModel.initialdrawing (DrawingAction.SetProjection Projection.Sky)
            Expect.equal m.projection Projection.Sky "the active projection should switch to Sky"
        }

        test "TC-3.8 a line records the projection it was drawn with" {
            let drawWith proj =
                let started =
                    Draw.run Draw.refSystemFlat (Draw.startTool Draw.refSystemFlat Geometry.Line)
                        (DrawingAction.SetProjection proj)
                [ V3d(0.0, 0.0, 0.0); V3d(2.0, 0.0, 0.0) ]
                |> List.fold (fun m p -> Draw.click Draw.refSystemFlat p m) started
            for proj in [ Projection.Linear; Projection.Viewpoint; Projection.Sky ] do
                let ann = drawWith proj |> Draw.theAnnotation (sprintf "line-%A" proj)
                Expect.equal ann.projection proj (sprintf "annotation should carry the %A projection" proj)
        }

        // TC-3.9 Pick Annotation (via UI list).
        //   A freshly drawn annotation is auto-selected, so to test a list click we
        //   draw two (the second becomes selected) and click the first, which is the
        //   clean "not yet selected" case.

        test "TC-3.9 selecting an annotation in the list highlights it" {
            let first = Draw.drawFull Draw.refSystemFlat Geometry.Point true [ V3d(0.0, 0.0, 0.0) ]
            let both  = Draw.click Draw.refSystemFlat (V3d(5.0, 0.0, 0.0)) first
            let target =
                Draw.annotations both
                |> List.find (fun a -> Some a.key <> both.annotations.singleSelectLeaf)
            let clicked =
                Draw.run Draw.refSystemFlat both
                    (DrawingAction.GroupsMessage (GroupsAppAction.SingleSelectLeaf([], target.key, target.text)))
            Expect.equal clicked.annotations.singleSelectLeaf (Some target.key)
                "the clicked annotation becomes the single selection"
            Expect.isTrue (DrawingApp.isSelected clicked target)
                "the selected annotation reports as selected (turns green)"
        }

        // TC-3.10 Pick Annotation (via 3D view).
        //   The 3D-view pick resolves the ray to one annotation's id and selects it
        //   through the same SingleSelectLeaf the list uses; the ray -> id
        //   intersection itself is the renderer's job.

        test "TC-3.10 picking one annotation in the 3D view selects only that one" {
            let first  = Draw.drawFull Draw.refSystemFlat Geometry.Point true [ V3d(0.0, 0.0, 0.0) ]
            let both   = Draw.click Draw.refSystemFlat (V3d(5.0, 0.0, 0.0)) first
            let anns   = Draw.annotations both
            Expect.equal (List.length anns) 2 "there should be two annotations to choose between"
            let target = anns |> List.find (fun a -> Some a.key <> both.annotations.singleSelectLeaf)
            let other  = anns |> List.find (fun a -> a.key <> target.key)
            let picked =
                Draw.run Draw.refSystemFlat both
                    (DrawingAction.GroupsMessage (GroupsAppAction.SingleSelectLeaf([], target.key, "")))
            Expect.equal picked.annotations.singleSelectLeaf (Some target.key)
                "only the picked annotation should be selected"
            Expect.isFalse (DrawingApp.isSelected picked other)
                "the other annotation should not be selected"
        }

        // TC-3.11 Pick Surface (via UI list)

        test "TC-3.11 selecting a surface in the list marks it selected" {
            let surf = makeSurface "test-surface-dir"
            let sm =
                { SurfaceModel.initial with
                    surfaces = SurfaceModel.initial.surfaces
                               |> GroupsApp.addLeafToActiveGroup (Leaf.Surfaces surf) false }
            let selected =
                SurfaceApp.update sm
                    (SurfaceAppAction.GroupsMessage (GroupsAppAction.SingleSelectLeaf([], surf.guid, surf.name)))
                    None Draw.view Draw.refSystemFlat
            Expect.equal selected.surfaces.singleSelectLeaf (Some surf.guid)
                "the clicked surface becomes the single selection"
        }

        // TC-3.12 Pick Surface (via 3D view)

        test "TC-3.12 picking one surface in the 3D view selects only that one" {
            // Same selection the list uses; the 3D pick just supplies the surface id.
            let s1, s2 = makeSurface "surface-a", makeSurface "surface-b"
            let sm =
                { SurfaceModel.initial with
                    surfaces =
                        SurfaceModel.initial.surfaces
                        |> GroupsApp.addLeafToActiveGroup (Leaf.Surfaces s1) false
                        |> GroupsApp.addLeafToActiveGroup (Leaf.Surfaces s2) false }
            let selected =
                SurfaceApp.update sm
                    (SurfaceAppAction.GroupsMessage (GroupsAppAction.SingleSelectLeaf([], s2.guid, s2.name)))
                    None Draw.view Draw.refSystemFlat
            Expect.equal selected.surfaces.singleSelectLeaf (Some s2.guid)
                "the picked surface should be the single selection"
        }
    ]

// Unit-level checks on the drawing defaults, Annotation.make and Leaf helpers that
// back the drawing tests above.

let private drawingModelInitTests =
    testList "DrawingModel initial state" [

        test "undo stack is empty on init" {
            Expect.isEmpty (initDrawing ()).undoStack "undoStack should be empty"
        }

        test "redo stack is empty on init" {
            Expect.isEmpty (initDrawing ()).redoStack "redoStack should be empty"
        }

        test "annotations flat map is empty on init" {
            Expect.equal (flatCount (initDrawing ()).annotations) 0
                "annotations flat map should be empty"
        }

        test "initial geometry is Line" {
            Expect.equal (initDrawing ()).geometry Geometry.Line "default geometry should be Line"
        }

        test "initial projection is Linear" {
            Expect.equal (initDrawing ()).projection Projection.Linear
                "default projection should be Linear"
        }
    ]

let private samplingDistTests =
    testList "Sampling distance unit conversion" [

        let makeAmount (v : float) : NumericInput =
            { value = v; min = 0.001; max = 1000.0; step = 0.001; format = "{0:0.000}" }

        test "1 km = 1000 m" {
            let r = DrawingModel.calculateSamplingDistance (makeAmount 1.0) SamplingUnit.km
            Expect.floatClose Accuracy.high r 1000.0 "1 km should be 1000 m"
        }

        test "1 m = 1 m" {
            let r = DrawingModel.calculateSamplingDistance (makeAmount 1.0) SamplingUnit.m
            Expect.floatClose Accuracy.high r 1.0 "1 m should be 1 m"
        }

        test "1 cm = 0.01 m" {
            let r = DrawingModel.calculateSamplingDistance (makeAmount 1.0) SamplingUnit.cm
            Expect.floatClose Accuracy.high r 0.01 "1 cm should be 0.01 m"
        }

        test "1 mm = 0.001 m" {
            let r = DrawingModel.calculateSamplingDistance (makeAmount 1.0) SamplingUnit.mm
            Expect.floatClose Accuracy.high r 0.001 "1 mm should be 0.001 m"
        }

        test "2.5 m stays 2.5 m" {
            let r = DrawingModel.calculateSamplingDistance (makeAmount 2.5) SamplingUnit.m
            Expect.floatClose Accuracy.high r 2.5 "2.5 m should be 2.5 m"
        }

        test "0.5 km = 500 m" {
            let r = DrawingModel.calculateSamplingDistance (makeAmount 0.5) SamplingUnit.km
            Expect.floatClose Accuracy.high r 500.0 "0.5 km should be 500 m"
        }
    ]

let private annotationMakeTests =
    testList "Annotation creation" [

        test "make creates annotation with correct geometry" {
            let ann = Annotation.make Projection.Linear None Geometry.Polyline None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.equal ann.geometry Geometry.Polyline "geometry should be Polyline"
        }

        test "make creates annotation with correct projection" {
            let ann = Annotation.make Projection.Viewpoint None Geometry.Line None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.equal ann.projection Projection.Viewpoint "projection should be Viewpoint"
        }

        test "make creates annotation with correct color" {
            let ann = Annotation.make Projection.Linear None Geometry.Line None
                          ({ c = C4b.Red } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.equal ann.color.c C4b.Red "color should be red"
        }

        test "make creates annotation with correct thickness" {
            let t : NumericInput = { value = 5.0; min = 1.0; max = 8.0; step = 1.0; format = "{0:0}" }
            let ann = Annotation.make Projection.Linear None Geometry.Line None
                          ({ c = C4b.White } : ColorInput) t "surf"
            Expect.equal ann.thickness.value 5.0 "thickness value should match"
        }

        test "DnS geometry sets showDns = true" {
            let ann = Annotation.make Projection.Linear None Geometry.DnS None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.isTrue ann.showDns "DnS annotation should have showDns = true"
        }

        test "Line geometry sets showDns = false" {
            let ann = Annotation.make Projection.Linear None Geometry.Line None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.isFalse ann.showDns "Line annotation should have showDns = false"
        }

        test "Point geometry sets showDns = false" {
            let ann = Annotation.make Projection.Linear None Geometry.Point None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.isFalse ann.showDns "Point annotation should have showDns = false"
        }

        test "make creates annotation with empty points list" {
            let ann = Annotation.make Projection.Linear None Geometry.Line None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.isEmpty (ann.points |> IndexList.toList) "new annotation should have no points"
        }

        test "make creates annotation with empty text" {
            let ann = Annotation.make Projection.Linear None Geometry.Line None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.equal ann.text "" "new annotation should have empty text"
        }

        test "make creates visible annotation" {
            let ann = Annotation.make Projection.Linear None Geometry.Line None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.isTrue ann.visible "new annotation should be visible"
        }

        test "make sets correct surface name" {
            let ann = Annotation.make Projection.Linear None Geometry.Line None
                          ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "MySurface"
            Expect.equal ann.surfaceName "MySurface" "surface name should be passed through"
        }

        test "each new annotation gets a unique id" {
            let leaf1 = makeLeaf Geometry.Line Projection.Linear C4b.White
            let leaf2 = makeLeaf Geometry.Line Projection.Linear C4b.White
            Expect.notEqual leaf1.id leaf2.id "each annotation should get a unique id"
        }
    ]

let private leafTests =
    testList "Leaf type helpers" [

        test "Leaf.Annotations wraps annotation key correctly" {
            let ann  = Annotation.make Projection.Linear None Geometry.Line None
                           ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            let leaf = Leaf.Annotations ann
            match leaf with
            | Leaf.Annotations a -> Expect.equal a.key ann.key "annotation key should be preserved"
            | _ -> failtest "expected Leaf.Annotations"
        }

        test "Leaf.toggleVisibility on Annotations flips visible" {
            let ann  = Annotation.make Projection.Linear None Geometry.Line None
                           ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.isTrue ann.visible "annotation starts visible"
            let leaf' = Leaf.toggleVisibility (Leaf.Annotations ann)
            match leaf' with
            | Leaf.Annotations a -> Expect.isFalse a.visible "visibility should be toggled"
            | _ -> failtest "expected Leaf.Annotations"
        }

        test "leaf id is unique per annotation created" {
            let leaf1 = makeLeaf Geometry.Line Projection.Linear C4b.White
            let leaf2 = makeLeaf Geometry.Line Projection.Linear C4b.White
            Expect.notEqual leaf1.id leaf2.id "each annotation should get a unique id"
        }

        test "leaf id matches the wrapped annotation key" {
            let leaf = makeLeaf Geometry.Line Projection.Linear C4b.White
            match leaf with
            | Leaf.Annotations a -> Expect.equal leaf.id a.key "leaf.id should equal annotation.key"
            | _ -> failtest "expected Leaf.Annotations"
        }
    ]

let tests =
    testList "Section 3 — Drawing and Managing Annotations" [
        drawingE2ETests
        drawingModelInitTests
        samplingDistTests
        annotationMakeTests
        leafTests
    ]
