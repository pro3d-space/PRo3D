/// Automatic tests covering the model/logic layer of features described in
/// docs/Test_Protocol/PRo3D_TestProtocol.tex.
///
/// TC references use the hierarchical numbering printed by \testcase{} in the LaTeX
/// document: TC-<section>.<subsection-within-section>.
///
/// Scope: pure model computation — no renderer, no window, no IO.
module FeatureTests

open System
open Aardvark.Base
open Aardvark.UI              // ColorInput
open Aardvark.UI.Primitives   // NumericInput, ColorPicker

open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

// ---------------------------------------------------------------------------
// Private helpers
// ---------------------------------------------------------------------------

/// Root-group path (empty list = root node in updateNodeAt).
let private rootPath : list<Index> = []

/// Minimal Annotation leaf for use in GroupsModel tests.
let private makeLeaf (geometry : Geometry) (projection : Projection) (color : C4b) : Leaf =
    let ann =
        Annotation.make
            projection
            None
            geometry
            None
            ({ c = color } : ColorInput)
            Annotation.Initial.thickness
            "test-surface"
    Leaf.Annotations ann

let private initGroups  () = GroupsModel.initial
let private initDrawing () = DrawingModel.initialdrawing

let private addLeaf (leaf : Leaf) (groups : GroupsModel) =
    GroupsApp.addLeafToActiveGroup leaf false groups

let private flatCount (groups : GroupsModel) = groups.flat |> HashMap.count

let private inRootLeaves (id : Guid) (groups : GroupsModel) =
    groups.rootGroup.leaves |> IndexList.toList |> List.contains id

// ---------------------------------------------------------------------------
// 1. DrawingModel initial state
//    Preconditions verified for TC-3.1–TC-3.5 and TC-19.1
// ---------------------------------------------------------------------------

let drawingModelInitTests =
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

// ---------------------------------------------------------------------------
// 2. Sampling-distance unit conversion
//    TC-3.1 – TC-3.5 (Draw Point/Line/Polyline/Polygon/DnS Annotation)
//    The sampling distance governs annotation point spacing on OPC surfaces.
// ---------------------------------------------------------------------------

let samplingDistTests =
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

// ---------------------------------------------------------------------------
// 3. Undo/Redo logic for drawing
//    TC-19.1 (Undo/Redo for Drawing)
// ---------------------------------------------------------------------------

let undoRedoTests =
    testList "Undo/Redo for drawing" [

        test "pushUndo grows the undo stack" {
            let leaf    = makeLeaf Geometry.Line Projection.Linear C4b.White
            let drawing = initDrawing ()
            let delta   = AnnotationsDelta.LeafAdded(leaf, rootPath)
            let drawing' = DrawingApp.pushUndo delta drawing
            Expect.equal (List.length drawing'.undoStack) 1
                "undo stack should contain one entry"
        }

        test "pushUndo clears the redo stack" {
            let leaf    = makeLeaf Geometry.Line Projection.Linear C4b.White
            let drawing = initDrawing ()
            let withRedo = { drawing with redoStack = [ AnnotationsDelta.LeafAdded(leaf, rootPath) ] }
            let drawing' = DrawingApp.pushUndo (AnnotationsDelta.LeafAdded(leaf, rootPath)) withRedo
            Expect.isEmpty drawing'.redoStack "redo stack must be cleared after pushUndo"
        }

        test "multiple pushUndo calls stack in LIFO order" {
            let leaf1 = makeLeaf Geometry.Line  Projection.Linear C4b.White
            let leaf2 = makeLeaf Geometry.Point Projection.Sky    C4b.Red
            let d1    = AnnotationsDelta.LeafAdded(leaf1, rootPath)
            let d2    = AnnotationsDelta.LeafAdded(leaf2, rootPath)
            let drawing' =
                initDrawing () |> DrawingApp.pushUndo d1 |> DrawingApp.pushUndo d2
            Expect.equal (List.length drawing'.undoStack) 2 "two entries in undo stack"
            // Compare by leaf id to avoid deep equality on IndexList/HashMap fields
            match drawing'.undoStack.[0] with
            | AnnotationsDelta.LeafAdded(l, _) ->
                Expect.equal l.id leaf2.id "head of undo stack should be the most recent delta"
            | _ -> failtest "expected LeafAdded at head of undo stack"
        }

        // applyUndoDelta: LeafAdded

        test "applyUndoDelta LeafAdded removes leaf from flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            Expect.equal (flatCount groups) 1 "leaf should be in flat map before undo"
            let groups' = DrawingApp.applyUndoDelta groups (AnnotationsDelta.LeafAdded(leaf, rootPath))
            Expect.equal (flatCount groups') 0 "leaf should be removed by undo"
        }

        test "applyUndoDelta LeafAdded removes leaf from root group leaves" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            Expect.isTrue  (inRootLeaves leaf.id groups)  "leaf should be in root before undo"
            let groups' = DrawingApp.applyUndoDelta groups (AnnotationsDelta.LeafAdded(leaf, rootPath))
            Expect.isFalse (inRootLeaves leaf.id groups') "leaf should be removed from root by undo"
        }

        // applyUndoDelta: LeafRemoved

        test "applyUndoDelta LeafRemoved restores leaf to flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups ()
            Expect.equal (flatCount groups) 0 "flat map should be empty"
            let groups' = DrawingApp.applyUndoDelta groups (AnnotationsDelta.LeafRemoved(leaf, rootPath))
            Expect.equal (flatCount groups') 1 "leaf should be restored by undo"
        }

        test "applyUndoDelta LeafRemoved restores leaf to root group leaves" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups ()
            let groups' = DrawingApp.applyUndoDelta groups (AnnotationsDelta.LeafRemoved(leaf, rootPath))
            Expect.isTrue (inRootLeaves leaf.id groups')
                "leaf should appear in root leaves after undo of removal"
        }

        // applyUndoDelta: SnapshotDelta

        test "applyUndoDelta SnapshotDelta restores before-state" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let before = initGroups ()
            let after  = before |> addLeaf leaf
            let groups' = DrawingApp.applyUndoDelta after (AnnotationsDelta.SnapshotDelta(before, after))
            Expect.equal (flatCount groups') 0 "undo of snapshot should restore empty before-state"
        }

        // applyRedoDelta: LeafAdded

        test "applyRedoDelta LeafAdded re-adds leaf to flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups ()
            let groups' = DrawingApp.applyRedoDelta groups (AnnotationsDelta.LeafAdded(leaf, rootPath))
            Expect.equal (flatCount groups') 1 "redo should add leaf back"
        }

        test "applyRedoDelta LeafAdded re-adds leaf to root group leaves" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups ()
            let groups' = DrawingApp.applyRedoDelta groups (AnnotationsDelta.LeafAdded(leaf, rootPath))
            Expect.isTrue (inRootLeaves leaf.id groups')
                "leaf should reappear in root leaves after redo"
        }

        // applyRedoDelta: LeafRemoved

        test "applyRedoDelta LeafRemoved removes leaf again" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            Expect.equal (flatCount groups) 1 "leaf present before redo"
            let groups' = DrawingApp.applyRedoDelta groups (AnnotationsDelta.LeafRemoved(leaf, rootPath))
            Expect.equal (flatCount groups') 0 "redo of removal should remove leaf"
        }

        // applyRedoDelta: SnapshotDelta

        test "applyRedoDelta SnapshotDelta restores after-state" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let before = initGroups ()
            let after  = before |> addLeaf leaf
            let groups' = DrawingApp.applyRedoDelta before (AnnotationsDelta.SnapshotDelta(before, after))
            Expect.equal (flatCount groups') 1 "redo of snapshot should restore after-state"
        }

        // Full undo-redo round-trip

        test "add then undo then redo restores the leaf" {
            let leaf    = makeLeaf Geometry.Polyline Projection.Viewpoint C4b.Green
            let groups0 = initGroups ()
            let groups1 = groups0 |> addLeaf leaf
            let delta   = AnnotationsDelta.LeafAdded(leaf, rootPath)
            let groups2 = DrawingApp.applyUndoDelta groups1 delta
            let groups3 = DrawingApp.applyRedoDelta groups2 delta
            Expect.equal (flatCount groups3) 1 "leaf should be present after undo+redo"
            Expect.isTrue (inRootLeaves leaf.id groups3)
                "leaf should be in root after undo+redo"
        }

        test "undo of deletion restores the leaf" {
            let leaf   = makeLeaf Geometry.Point Projection.Sky C4b.Blue
            let groups = initGroups () |> addLeaf leaf
            let delta  = AnnotationsDelta.LeafRemoved(leaf, rootPath)
            let groupsAfterRemove = GroupsApp.removeLeaf groups leaf.id rootPath true
            Expect.equal (flatCount groupsAfterRemove) 0 "leaf removed"
            let groupsUndone = DrawingApp.applyUndoDelta groupsAfterRemove delta
            Expect.equal (flatCount groupsUndone) 1 "undo of removal restores leaf"
        }
    ]

// ---------------------------------------------------------------------------
// 4. Group default color
//    TC-19.2 (Group Default Color)
// ---------------------------------------------------------------------------

let groupColorTests =
    testList "Group default color" [

        test "initial default color of root group is white" {
            Expect.equal (initGroups ()).rootGroup.defaultColor.c C4b.White
                "root group default color should start as white"
        }

        test "Node.initialDefaultColor is white" {
            Expect.equal Node.initialDefaultColor.c C4b.White
                "Node.initialDefaultColor should be white"
        }

        test "SetGroupDefaultColor changes the root group color to red" {
            let groups  = initGroups ()
            let groups' = GroupsApp.update groups
                            (GroupsAppAction.SetGroupDefaultColor(rootPath, ColorPicker.Action.SetColor C4b.Red))
            Expect.equal groups'.rootGroup.defaultColor.c C4b.Red
                "root group default color should be red"
        }

        test "SetGroupDefaultColor to blue then green produces green" {
            let setColor c g =
                GroupsApp.update g
                    (GroupsAppAction.SetGroupDefaultColor(rootPath, ColorPicker.Action.SetColor c))
            let groups' = initGroups () |> setColor C4b.Blue |> setColor C4b.Green
            Expect.equal groups'.rootGroup.defaultColor.c C4b.Green
                "last color set should be green"
        }

        test "SetGroupDefaultColor on root does not affect subgroup default color" {
            let groups0 = initGroups ()
            let groups1 = GroupsApp.update groups0 (GroupsAppAction.AddGroup rootPath)
            let groups2 =
                GroupsApp.update groups1
                    (GroupsAppAction.SetGroupDefaultColor(rootPath, ColorPicker.Action.SetColor C4b.Red))
            match groups2.rootGroup.subNodes |> IndexList.tryFirst with
            | Some sub ->
                Expect.equal sub.defaultColor.c C4b.White
                    "subgroup should keep its initial default color when root is changed"
            | None ->
                failtest "no subgroup was created by AddGroup"
        }

        test "annotation color comes from the color passed to Annotation.make" {
            let leaf = makeLeaf Geometry.Line Projection.Linear C4b.Red
            match leaf with
            | Leaf.Annotations ann ->
                Expect.equal ann.color.c C4b.Red
                    "annotation should carry the color passed at construction"
            | _ -> failtest "expected Leaf.Annotations"
        }
    ]

// ---------------------------------------------------------------------------
// 5. GroupsApp CRUD operations
//    TC-10.1 (Grouping -- Create Group), TC-10.2 (Grouping -- Move Leafs)
// ---------------------------------------------------------------------------

let groupsTests =
    testList "GroupsApp operations" [

        test "addLeafToActiveGroup adds leaf to flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            Expect.equal (flatCount groups) 1 "flat map should contain 1 leaf"
            Expect.isTrue (groups.flat |> HashMap.containsKey leaf.id)
                "flat map should contain the leaf's id"
        }

        test "addLeafToActiveGroup adds leaf id to root group leaves" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            Expect.isTrue (inRootLeaves leaf.id groups)
                "root group leaves should contain the leaf id"
        }

        test "adding two leaves grows flat map to 2" {
            let leaf1  = makeLeaf Geometry.Line  Projection.Linear C4b.White
            let leaf2  = makeLeaf Geometry.Point Projection.Sky    C4b.Red
            let groups = initGroups () |> addLeaf leaf1 |> addLeaf leaf2
            Expect.equal (flatCount groups) 2 "flat map should contain 2 leaves"
        }

        test "removeLeaf removes leaf from flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            let groups' = GroupsApp.removeLeaf groups leaf.id rootPath true
            Expect.equal (flatCount groups') 0 "flat map should be empty after remove"
        }

        test "removeLeaf removes leaf id from root group leaves" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            let groups' = GroupsApp.removeLeaf groups leaf.id rootPath true
            Expect.isFalse (inRootLeaves leaf.id groups')
                "root group leaves should not contain the leaf id after remove"
        }

        test "removeLeaf with removeFromFlat=false keeps leaf in flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            let groups' = GroupsApp.removeLeaf groups leaf.id rootPath false
            Expect.equal (flatCount groups') 1
                "flat map should still contain leaf when removeFromFlat=false"
            Expect.isFalse (inRootLeaves leaf.id groups')
                "leaf should be removed from group structure"
        }

        test "AddGroup action adds a subgroup to root" {
            let groups  = initGroups ()
            let groups' = GroupsApp.update groups (GroupsAppAction.AddGroup rootPath)
            Expect.equal (groups'.rootGroup.subNodes |> IndexList.count) 1
                "root should have one subgroup after AddGroup"
        }

        test "ClearGroup empties root group leaves list" {
            let leaf1  = makeLeaf Geometry.Line  Projection.Linear C4b.White
            let leaf2  = makeLeaf Geometry.Point Projection.Sky    C4b.Red
            let groups = initGroups () |> addLeaf leaf1 |> addLeaf leaf2
            let groups' = GroupsApp.update groups (GroupsAppAction.ClearGroup rootPath)
            Expect.isEmpty (groups'.rootGroup.leaves |> IndexList.toList)
                "root group leaves should be empty after ClearGroup"
        }

        test "ClearGroup removes leaves from flat map" {
            let leaf1  = makeLeaf Geometry.Line  Projection.Linear C4b.White
            let leaf2  = makeLeaf Geometry.Point Projection.Sky    C4b.Red
            let groups = initGroups () |> addLeaf leaf1 |> addLeaf leaf2
            let groups' = GroupsApp.update groups (GroupsAppAction.ClearGroup rootPath)
            Expect.equal (flatCount groups') 0 "flat map should be empty after ClearGroup"
        }

        test "SetGroupName renames the active group (root)" {
            let groups  = initGroups ()
            let groups' = GroupsApp.update groups (GroupsAppAction.SetGroupName "MyGroup")
            Expect.equal groups'.rootGroup.name "MyGroup"
                "root group name should be updated (initial active group = root)"
        }
    ]

// ---------------------------------------------------------------------------
// 6. Annotation creation
//    TC-3.1 (Draw Point), TC-3.2 (Draw Line), TC-3.3 (Draw Polyline),
//    TC-3.4 (Draw Polygon), TC-3.5 (Draw DnS),
//    TC-3.6 (Draw AxisEllipse), TC-3.7 (Draw Axis4PEllipse),
//    TC-3.8 (Projection Modes)
// ---------------------------------------------------------------------------

let annotationTests =
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

// ---------------------------------------------------------------------------
// 7. Annotation property mutations
//    TC-5.1 (Annotation Properties Panel), TC-5.3 (Annotation Text Note)
// ---------------------------------------------------------------------------

let annotationPropertiesTests =
    testList "Annotation property mutations" [

        let refSystem = ReferenceSystem.initial

        let baseAnn () =
            Annotation.make Projection.Linear None Geometry.Line None
                ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"

        test "ToggleVisible hides a visible annotation" {
            let ann  = baseAnn ()
            Expect.isTrue ann.visible "pre: annotation should be visible"
            let ann' = AnnotationProperties.update refSystem ann AnnotationProperties.Action.ToggleVisible
            Expect.isFalse ann'.visible "annotation should be hidden"
        }

        test "ToggleVisible twice restores visibility" {
            let ann  = baseAnn ()
            let ann' =
                ann
                |> fun a -> AnnotationProperties.update refSystem a AnnotationProperties.Action.ToggleVisible
                |> fun a -> AnnotationProperties.update refSystem a AnnotationProperties.Action.ToggleVisible
            Expect.isTrue ann'.visible "double toggle should restore visibility"
        }

        test "SetText sets the annotation text" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann (AnnotationProperties.Action.SetText "hello")
            Expect.equal ann'.text "hello" "text should be updated"
        }

        test "SetText to empty string clears text" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann (AnnotationProperties.Action.SetText "")
            Expect.equal ann'.text "" "text should be cleared"
        }

        test "ChangeColor changes annotation color" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann
                           (AnnotationProperties.Action.ChangeColor (ColorPicker.Action.SetColor C4b.Green))
            Expect.equal ann'.color.c C4b.Green "color should be green"
        }

        test "ToggleShowDns flips showDns on DnS annotation" {
            let ann =
                Annotation.make Projection.Linear None Geometry.DnS None
                    ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.isTrue ann.showDns "DnS annotation should start with showDns = true"
            let ann' = AnnotationProperties.update refSystem ann AnnotationProperties.Action.ToggleShowDns
            Expect.isFalse ann'.showDns "showDns should be toggled to false"
        }

        test "ToggleShowText flips showText" {
            let ann  = baseAnn ()
            Expect.isTrue ann.showText "annotation should start with showText = true"
            let ann' = AnnotationProperties.update refSystem ann AnnotationProperties.Action.ToggleShowText
            Expect.isFalse ann'.showText "showText should be toggled"
        }

        test "SetGeometry changes the geometry" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann
                           (AnnotationProperties.Action.SetGeometry Geometry.Polyline)
            Expect.equal ann'.geometry Geometry.Polyline "geometry should be changed to Polyline"
        }

        test "SetProjection changes the projection" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann
                           (AnnotationProperties.Action.SetProjection Projection.Sky)
            Expect.equal ann'.projection Projection.Sky "projection should be Sky"
        }
    ]

// ---------------------------------------------------------------------------
// 8. Geometric measurement calculations
//    TC-5.2 (Annotation Measurements)
// ---------------------------------------------------------------------------

let measurementTests =
    testList "Geometric measurement calculations" [

        let up = V3d.OOI

        // verticalDelta

        test "verticalDelta with single point returns 0" {
            Expect.floatClose Accuracy.high
                (Calculations.verticalDelta [ V3d(1.0, 2.0, 3.0) ] up) 0.0
                "single-point vertical delta should be 0"
        }

        test "verticalDelta same height returns 0" {
            Expect.floatClose Accuracy.high
                (Calculations.verticalDelta [ V3d(0.0, 0.0, 5.0); V3d(3.0, 4.0, 5.0) ] up) 0.0
                "same-height points: vertical delta should be 0"
        }

        test "verticalDelta ascending returns positive" {
            Expect.floatClose Accuracy.high
                (Calculations.verticalDelta [ V3d.OOO; V3d(0.0, 0.0, 10.0) ] up) 10.0
                "ascending by 10 m: vertical delta should be 10"
        }

        test "verticalDelta descending returns negative" {
            Expect.floatClose Accuracy.high
                (Calculations.verticalDelta [ V3d(0.0, 0.0, 10.0); V3d.OOO ] up) -10.0
                "descending by 10 m: vertical delta should be -10"
        }

        // horizontalDelta

        test "horizontalDelta with single point returns 0" {
            Expect.floatClose Accuracy.high
                (Calculations.horizontalDelta [ V3d(1.0, 2.0, 3.0) ] up) 0.0
                "single-point horizontal delta should be 0"
        }

        test "horizontalDelta 3-4-5 triangle in horizontal plane" {
            Expect.floatClose Accuracy.high
                (Calculations.horizontalDelta [ V3d.OOO; V3d(3.0, 4.0, 0.0) ] up) 5.0
                "3-4-5 triangle: horizontal distance should be 5"
        }

        test "horizontalDelta for points differing only in Z returns 0" {
            Expect.floatClose Accuracy.high
                (Calculations.horizontalDelta [ V3d.OOO; V3d(0.0, 0.0, 10.0) ] up) 0.0
                "vertical-only movement: horizontal delta should be 0"
        }

        // getDistance

        test "getDistance for two coincident points is 0" {
            let p = V3d(1.0, 2.0, 3.0)
            Expect.floatClose Accuracy.high (Calculations.getDistance [p; p]) 0.0
                "coincident points: distance should be 0"
        }

        test "getDistance for a 3-4-5 triangle leg is 5" {
            Expect.floatClose Accuracy.high
                (Calculations.getDistance [ V3d.OOO; V3d(3.0, 4.0, 0.0) ]) 5.0
                "3-4-5: distance should be 5"
        }

        test "getDistance accumulates over multiple segments" {
            Expect.floatClose Accuracy.high
                (Calculations.getDistance [ V3d.OOO; V3d(1.0, 0.0, 0.0); V3d(2.0, 0.0, 0.0) ]) 2.0
                "two 1-unit segments: total distance should be 2"
        }

        // pitch

        test "pitch of a horizontal vector is 0 degrees" {
            Expect.floatClose Accuracy.high (Calculations.pitch up V3d.IOO) 0.0
                "horizontal direction: pitch should be 0"
        }

        test "pitch of the up vector is 90 degrees" {
            Expect.floatClose Accuracy.high (Calculations.pitch up up) 90.0
                "up direction: pitch should be 90"
        }

        test "pitch of a downward vector is -90 degrees" {
            Expect.floatClose Accuracy.high (Calculations.pitch up (-up)) -90.0
                "down direction: pitch should be -90"
        }

        // computeAzimuth

        test "computeAzimuth returns a finite number" {
            let az = Calculations.computeAzimuth V3d.IOO V3d.OIO up
            Expect.isTrue (Double.IsFinite az) "azimuth should be finite"
        }
    ]

// ---------------------------------------------------------------------------
// 9. Leaf type helpers
//    TC-3.9 (Pick Annotation via UI List), TC-3.10 (Pick Annotation via 3D View),
//    TC-3.11 (Pick Surface via UI List),   TC-3.12 (Pick Surface via 3D View)
// ---------------------------------------------------------------------------

let leafTests =
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

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

let tests () =
    testList "PRo3D feature tests" [
        drawingModelInitTests
        samplingDistTests
        undoRedoTests
        groupColorTests
        groupsTests
        annotationTests
        annotationPropertiesTests
        measurementTests
        leafTests
    ]
