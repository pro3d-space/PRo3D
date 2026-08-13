/// Section 19 — Undo/Redo and Group Default Color
///   TC-19.1 (Undo/Redo for Drawing), TC-19.2 (Group Default Color)
///
///   Exercises the real DrawingApp undo/redo delta helpers and the GroupsApp
///   default-color logic.
module PRo3D.Tests.Section19_UndoRedoGroupColor

open Aardvark.Base
open Aardvark.UI.Primitives              // ColorPicker

open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Tests

// TC-19.1 Undo/Redo for Drawing
let private undoRedoTests =
    testList "Undo/Redo for drawing" [

        test "TC-19.1 pushUndo grows the undo stack" {
            let leaf    = makeLeaf Geometry.Line Projection.Linear C4b.White
            let drawing = initDrawing ()
            let delta   = AnnotationsDelta.LeafAdded(leaf, rootPath)
            let drawing' = DrawingApp.pushUndo delta drawing
            Expect.equal (List.length drawing'.undoStack) 1
                "undo stack should contain one entry"
        }

        test "TC-19.1 pushUndo clears the redo stack" {
            let leaf    = makeLeaf Geometry.Line Projection.Linear C4b.White
            let drawing = initDrawing ()
            let withRedo = { drawing with redoStack = [ AnnotationsDelta.LeafAdded(leaf, rootPath) ] }
            let drawing' = DrawingApp.pushUndo (AnnotationsDelta.LeafAdded(leaf, rootPath)) withRedo
            Expect.isEmpty drawing'.redoStack "redo stack must be cleared after pushUndo"
        }

        test "TC-19.1 multiple pushUndo calls stack in LIFO order" {
            let leaf1 = makeLeaf Geometry.Line  Projection.Linear C4b.White
            let leaf2 = makeLeaf Geometry.Point Projection.Sky    C4b.Red
            let d1    = AnnotationsDelta.LeafAdded(leaf1, rootPath)
            let d2    = AnnotationsDelta.LeafAdded(leaf2, rootPath)
            let drawing' =
                initDrawing () |> DrawingApp.pushUndo d1 |> DrawingApp.pushUndo d2
            Expect.equal (List.length drawing'.undoStack) 2 "two entries in undo stack"
            match drawing'.undoStack.[0] with
            | AnnotationsDelta.LeafAdded(l, _) ->
                Expect.equal l.id leaf2.id "head of undo stack should be the most recent delta"
            | _ -> failtest "expected LeafAdded at head of undo stack"
        }

        // applyUndoDelta: LeafAdded

        test "TC-19.1 applyUndoDelta LeafAdded removes leaf from flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            Expect.equal (flatCount groups) 1 "leaf should be in flat map before undo"
            let groups' = DrawingApp.applyUndoDelta groups (AnnotationsDelta.LeafAdded(leaf, rootPath))
            Expect.equal (flatCount groups') 0 "leaf should be removed by undo"
        }

        test "TC-19.1 applyUndoDelta LeafAdded removes leaf from root group leaves" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            Expect.isTrue  (inRootLeaves leaf.id groups)  "leaf should be in root before undo"
            let groups' = DrawingApp.applyUndoDelta groups (AnnotationsDelta.LeafAdded(leaf, rootPath))
            Expect.isFalse (inRootLeaves leaf.id groups') "leaf should be removed from root by undo"
        }

        // applyUndoDelta: LeafRemoved

        test "TC-19.1 applyUndoDelta LeafRemoved restores leaf to flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups ()
            Expect.equal (flatCount groups) 0 "flat map should be empty"
            let groups' = DrawingApp.applyUndoDelta groups (AnnotationsDelta.LeafRemoved(leaf, rootPath))
            Expect.equal (flatCount groups') 1 "leaf should be restored by undo"
        }

        test "TC-19.1 applyUndoDelta LeafRemoved restores leaf to root group leaves" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups ()
            let groups' = DrawingApp.applyUndoDelta groups (AnnotationsDelta.LeafRemoved(leaf, rootPath))
            Expect.isTrue (inRootLeaves leaf.id groups')
                "leaf should appear in root leaves after undo of removal"
        }

        // applyUndoDelta: SnapshotDelta

        test "TC-19.1 applyUndoDelta SnapshotDelta restores before-state" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let before = initGroups ()
            let after  = before |> addLeaf leaf
            let groups' = DrawingApp.applyUndoDelta after (AnnotationsDelta.SnapshotDelta(before, after))
            Expect.equal (flatCount groups') 0 "undo of snapshot should restore empty before-state"
        }

        // applyRedoDelta: LeafAdded

        test "TC-19.1 applyRedoDelta LeafAdded re-adds leaf to flat map" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups ()
            let groups' = DrawingApp.applyRedoDelta groups (AnnotationsDelta.LeafAdded(leaf, rootPath))
            Expect.equal (flatCount groups') 1 "redo should add leaf back"
        }

        test "TC-19.1 applyRedoDelta LeafAdded re-adds leaf to root group leaves" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups ()
            let groups' = DrawingApp.applyRedoDelta groups (AnnotationsDelta.LeafAdded(leaf, rootPath))
            Expect.isTrue (inRootLeaves leaf.id groups')
                "leaf should reappear in root leaves after redo"
        }

        // applyRedoDelta: LeafRemoved

        test "TC-19.1 applyRedoDelta LeafRemoved removes leaf again" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let groups = initGroups () |> addLeaf leaf
            Expect.equal (flatCount groups) 1 "leaf present before redo"
            let groups' = DrawingApp.applyRedoDelta groups (AnnotationsDelta.LeafRemoved(leaf, rootPath))
            Expect.equal (flatCount groups') 0 "redo of removal should remove leaf"
        }

        // applyRedoDelta: SnapshotDelta

        test "TC-19.1 applyRedoDelta SnapshotDelta restores after-state" {
            let leaf   = makeLeaf Geometry.Line Projection.Linear C4b.White
            let before = initGroups ()
            let after  = before |> addLeaf leaf
            let groups' = DrawingApp.applyRedoDelta before (AnnotationsDelta.SnapshotDelta(before, after))
            Expect.equal (flatCount groups') 1 "redo of snapshot should restore after-state"
        }

        // Full undo-redo round-trip

        test "TC-19.1 add then undo then redo restores the leaf" {
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

        test "TC-19.1 undo of deletion restores the leaf" {
            let leaf   = makeLeaf Geometry.Point Projection.Sky C4b.Blue
            let groups = initGroups () |> addLeaf leaf
            let delta  = AnnotationsDelta.LeafRemoved(leaf, rootPath)
            let groupsAfterRemove = GroupsApp.removeLeaf groups leaf.id rootPath true
            Expect.equal (flatCount groupsAfterRemove) 0 "leaf removed"
            let groupsUndone = DrawingApp.applyUndoDelta groupsAfterRemove delta
            Expect.equal (flatCount groupsUndone) 1 "undo of removal restores leaf"
        }
    ]

// TC-19.2 Group Default Color
let private groupColorTests =
    testList "Group default color" [

        test "TC-19.2 initial default color of root group is white" {
            Expect.equal (initGroups ()).rootGroup.defaultColor.c C4b.White
                "root group default color should start as white"
        }

        test "TC-19.2 Node.initialDefaultColor is white" {
            Expect.equal Node.initialDefaultColor.c C4b.White
                "Node.initialDefaultColor should be white"
        }

        test "TC-19.2 SetGroupDefaultColor changes the root group color to red" {
            let groups  = initGroups ()
            let groups' = GroupsApp.update groups
                            (GroupsAppAction.SetGroupDefaultColor(rootPath, ColorPicker.Action.SetColor C4b.Red))
            Expect.equal groups'.rootGroup.defaultColor.c C4b.Red
                "root group default color should be red"
        }

        test "TC-19.2 SetGroupDefaultColor to blue then green produces green" {
            let setColor c g =
                GroupsApp.update g
                    (GroupsAppAction.SetGroupDefaultColor(rootPath, ColorPicker.Action.SetColor c))
            let groups' = initGroups () |> setColor C4b.Blue |> setColor C4b.Green
            Expect.equal groups'.rootGroup.defaultColor.c C4b.Green
                "last color set should be green"
        }

        test "TC-19.2 SetGroupDefaultColor on root does not affect subgroup default color" {
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

        test "TC-19.2 annotation color comes from the color passed to Annotation.make" {
            let leaf = makeLeaf Geometry.Line Projection.Linear C4b.Red
            match leaf with
            | Leaf.Annotations ann ->
                Expect.equal ann.color.c C4b.Red
                    "annotation should carry the color passed at construction"
            | _ -> failtest "expected Leaf.Annotations"
        }
    ]

let tests =
    testList "Section 19 — Undo/Redo and Group Default Color" [
        undoRedoTests
        groupColorTests
    ]
