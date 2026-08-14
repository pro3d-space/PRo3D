/// Section 10 — Grouping
///   TC-10.1 (Grouping -- Create Group), TC-10.2 (Grouping -- Move Leafs)
///
///   Exercises the real GroupsApp.update / GroupsApp CRUD helpers.
module PRo3D.Tests.Section10_Grouping

open Aardvark.Base
open Aardvark.UI.Primitives              // ColorPicker

open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Tests

let private crudTests =
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

let private tc10Tests =
    testList "Grouping create/move" [

        // TC-10.1 Create Group

        test "TC-10.1 AddGroup adds a subgroup to root" {
            let groups  = initGroups ()
            let groups' = GroupsApp.update groups (GroupsAppAction.AddGroup rootPath)
            Expect.equal (groups'.rootGroup.subNodes |> IndexList.count) 1
                "root should have one subgroup after AddGroup"
        }

        // TC-10.2 Move Leafs

        test "TC-10.2 MoveLeaves relocates a selected leaf into the active group" {
            let leaf = makeLeaf Geometry.Line Projection.Linear C4b.White
            let g0 = initGroups () |> addLeaf leaf                       // leaf in root
            let g1 = GroupsApp.update g0 (GroupsAppAction.AddGroup rootPath)
            match g1.rootGroup.subNodes.TryGetIndex 0 with
            | Some subIdx ->
                let subNode = g1.rootGroup.subNodes |> IndexList.toList |> List.head
                // make the subgroup the active (destination) group
                let g2 = { g1 with activeGroup = { id = subNode.key; path = [ subIdx ]; name = subNode.name } }
                // select the leaf where it currently lives (root)
                let g3 = GroupsApp.update g2 (GroupsAppAction.AddLeafToSelection([], leaf.id, ""))
                let g4 = GroupsApp.update g3 GroupsAppAction.MoveLeaves
                Expect.isFalse (inRootLeaves leaf.id g4) "leaf should have left the root group"
                let subAfter = g4.rootGroup.subNodes |> IndexList.toList |> List.head
                Expect.isTrue (subAfter.leaves |> IndexList.toList |> List.contains leaf.id)
                    "leaf should now live in the subgroup"
                Expect.isTrue (g4.flat |> HashMap.containsKey leaf.id)
                    "moving a leaf keeps it in the flat map"
            | None -> failtest "AddGroup did not create an addressable subgroup"
        }
    ]

let tests =
    testList "Section 10 — Grouping" [
        tc10Tests
        crudTests
    ]
