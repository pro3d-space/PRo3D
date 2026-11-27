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

/// #900: a single pick in 3D opens the folders down to the picked annotation.
let private expandToLeafTests =
    testList "expandToLeaf" [
        let leaf = System.Guid.NewGuid()
        let group name leaves subNodes =
            { GroupsApp.createEmptyGroup () with
                name = name; expanded = false
                leaves = IndexList.ofList leaves; subNodes = IndexList.ofList subNodes }
        let deep  = group "deep" [ leaf ] []
        let a     = group "a" [] [ deep ]
        let b     = group "b" [ System.Guid.NewGuid() ] []
        let model = { initGroups () with rootGroup = group "root" [] [ b; a ] }

        test "expands every group on the path to the leaf, nothing else" {
            let root = (GroupsApp.expandToLeaf leaf model).rootGroup
            let subs = root.subNodes |> IndexList.toList
            Expect.isTrue root.expanded "root"
            match subs with
            | [ b'; a' ] ->
                Expect.isFalse b'.expanded "b is off the path"
                Expect.equal b' b "b unchanged"
                Expect.isTrue a'.expanded "a"
                match a'.subNodes |> IndexList.toList with
                | [ deep' ] -> Expect.isTrue deep'.expanded "the group holding the leaf"
                | _ -> failtest "a lost its subgroup"
            | _ -> failtest "root lost its subgroups"
        }

        test "an unknown leaf changes nothing" {
            Expect.equal (GroupsApp.expandToLeaf (System.Guid.NewGuid()) model) model "model"
        }

        test "a leaf held by the root expands only the root" {
            let rootLeaf = System.Guid.NewGuid()
            let m = { initGroups () with rootGroup = group "root" [ rootLeaf ] [ a ] }
            let root = (GroupsApp.expandToLeaf rootLeaf m).rootGroup
            Expect.isTrue root.expanded "root"
            Expect.equal (root.subNodes |> IndexList.toList) [ a ] "subgroups untouched"
        }
    ]

/// #900: the rows a windowed list renders for a scroll position
let private virtualListWindowTests =
    testList "VirtualList.window" [
        let w top height n = VirtualList.window 42.0 top height n
        test "top of a long list: the visible rows and the overscan below" {
            Expect.equal (w 0.0 420.0 5000) (struct (0, 20)) "rows 0-9 visible, 10 overscan"
        }
        test "middle: overscan on both sides" {
            Expect.equal (w 4200.0 420.0 5000) (struct (90, 30)) "rows 100-109 visible"
        }
        test "end: clamped to the list" {
            Expect.equal (w (42.0 * 4990.0) 420.0 5000) (struct (4980, 20)) "the last rows"
        }
        test "empty list and hidden box" {
            Expect.equal (w 0.0 420.0 0) (struct (0, 0)) "no rows"
            Expect.equal (w 0.0 0.0 5000) (struct (0, 10)) "overscan only"
        }
        test "scrolled past the end (list shrank): no rows, no negative count" {
            Expect.equal (w 1.0e6 420.0 10) (struct (10, 0)) "nothing"
        }
    ]

/// Shift+click in the annotation list (docs/AnnotationList.md): everything between the anchor
/// (the last row clicked without Shift) and the clicked row
let private rangeSelectionTests =
    testList "SelectLeafRange" [
        // five leaves in the root, in list order
        let setup () =
            let leaves = List.init 5 (fun _ -> makeLeaf Geometry.Line Projection.Linear C4b.White)
            let g = leaves |> List.fold (fun g l -> addLeaf l g) (initGroups ())
            let ids = g.rootGroup.leaves |> IndexList.toArray
            g, ids
        let click (id : System.Guid) g = GroupsApp.update g (GroupsAppAction.PlainSelectLeaf(rootPath, id))
        let cube  (id : System.Guid) g = GroupsApp.update g (GroupsAppAction.AddLeafToSelection(rootPath, id, ""))
        let shift (id : System.Guid) g = GroupsApp.update g (GroupsAppAction.SelectLeafRange(rootPath, id))
        let selected (g : GroupsModel) = g.selectedLeaves |> HashSet.map (fun ts -> ts.id)

        test "click 2, Shift+click 4 selects 2..4, 4 green" {
            let g, ids = setup ()
            let g = g |> click ids.[1] |> shift ids.[3]
            Expect.equal (selected g) (HashSet.ofList [ ids.[1]; ids.[2]; ids.[3] ]) "range 2..4"
            Expect.equal g.singleSelectLeaf (Some ids.[3]) "clicked row is the single selection"
        }

        test "backwards: click 4, Shift+click 1 selects 1..4" {
            let g, ids = setup ()
            let g = g |> click ids.[3] |> shift ids.[0]
            Expect.equal (selected g) (HashSet.ofList [ ids.[0]; ids.[1]; ids.[2]; ids.[3] ]) "range 1..4"
        }

        test "the anchor stays: a second Shift+click re-spans and replaces" {
            let g, ids = setup ()
            let g = g |> click ids.[1] |> shift ids.[4] |> shift ids.[2]
            Expect.equal (selected g) (HashSet.ofList [ ids.[1]; ids.[2] ]) "range 2..3 only"
        }

        test "Shift+click on the anchor selects just it" {
            let g, ids = setup ()
            let g = g |> click ids.[2] |> shift ids.[2]
            Expect.equal (selected g) (HashSet.ofList [ ids.[2] ]) "only the anchor"
        }

        test "a leaf missing from flat is skipped" {
            let g, ids = setup ()
            let g = { g with flat = HashMap.remove ids.[2] g.flat }
            let g = g |> click ids.[1] |> shift ids.[3]
            Expect.equal (selected g) (HashSet.ofList [ ids.[1]; ids.[3] ]) "stale leaf left out"
        }

        test "no anchor in the group: toggles the clicked leaf only" {
            let g, ids = setup ()
            let g = g |> shift ids.[3]
            Expect.equal (selected g) (HashSet.ofList [ ids.[3] ]) "just the clicked leaf"
        }

        test "anchor in another group: toggles the clicked leaf only" {
            let g, ids = setup ()
            let g = { g with rangeAnchor = Some (System.Guid.NewGuid()) }
            let g = g |> shift ids.[0]
            Expect.equal (selected g) (HashSet.ofList [ ids.[0] ]) "just the clicked leaf"
        }

        test "no usable anchor: the green leaf stays selected beside the Shift+clicked one" {
            let g, ids = setup ()
            let g = g |> click ids.[0]
            let g = { g with rangeAnchor = Some (System.Guid.NewGuid()) }
            let g = g |> shift ids.[3]
            Expect.equal (selected g) (HashSet.ofList [ ids.[0]; ids.[3] ]) "old green leaf joins the multi-selection"
            Expect.equal g.singleSelectLeaf (Some ids.[3]) "the Shift+clicked leaf is green"
        }

        test "a Shift+click without an anchor becomes the anchor" {
            let g, ids = setup ()
            let g = g |> shift ids.[3] |> shift ids.[1]
            Expect.equal (selected g) (HashSet.ofList [ ids.[1]; ids.[2]; ids.[3] ]) "range 2..4 from the first Shift+click"
            let g, ids = setup ()
            let g = { g with rangeAnchor = Some (System.Guid.NewGuid()) }
            let g = g |> shift ids.[4] |> shift ids.[2]
            Expect.equal (selected g) (HashSet.ofList [ ids.[2]; ids.[3]; ids.[4] ]) "anchor in another group is replaced"
        }

        test "deleting the anchor leaf clears the anchor" {
            let g, ids = setup ()
            let g = g |> click ids.[1] |> GroupsApp.removeLeafById ids.[1]
            Expect.isNone g.rangeAnchor "removeLeafById"
            let g, ids = setup ()
            let g = g |> click ids.[1] |> GroupsApp.removeLeavesById (HashSet.single ids.[1])
            Expect.isNone g.rangeAnchor "removeLeavesById"
            let g, ids = setup ()
            let g = GroupsApp.update (g |> click ids.[1]) (GroupsAppAction.RemoveLeaf(ids.[1], rootPath))
            Expect.isNone g.rangeAnchor "RemoveLeaf"
            let g, ids = setup ()
            let g = GroupsApp.update (g |> click ids.[1]) (GroupsAppAction.ClearGroup rootPath)
            Expect.isNone g.rangeAnchor "ClearGroup"
        }

        test "deleting another leaf keeps the anchor" {
            let g, ids = setup ()
            let g = g |> click ids.[1] |> GroupsApp.removeLeafById ids.[3]
            Expect.equal g.rangeAnchor (Some ids.[1]) "anchor untouched"
        }

        test "a plain click after a range starts fresh" {
            let g, ids = setup ()
            let g = g |> click ids.[1] |> shift ids.[3] |> click ids.[4]
            Expect.isEmpty (selected g) "the range is cleared"
            Expect.equal g.singleSelectLeaf (Some ids.[4]) "only the clicked row is selected"
        }

        test "a cube-click deselect moves the anchor" {
            let g, ids = setup ()
            let g = g |> cube ids.[3] |> cube ids.[3] |> shift ids.[1]
            Expect.equal (selected g) (HashSet.ofList [ ids.[1]; ids.[2]; ids.[3] ]) "range 2..4 from the deselected row"
        }

        test "after a range the active child is the Shift+clicked leaf" {
            let g, ids = setup ()
            let g = g |> click ids.[1] |> shift ids.[3]
            Expect.equal g.activeChild.id ids.[3] "Remove acts on the green row"
            Expect.equal g.rangeAnchor (Some ids.[1]) "the anchor stays"
        }

        test "a cube click toggles off an entry stored with another path" {
            let g, ids = setup ()
            let g = { g with selectedLeaves = HashSet.single { id = ids.[2]; path = []; name = "picked" } }
            let g = GroupsApp.update g (GroupsAppAction.AddLeafToSelection([ Index.zero ], ids.[2], ""))
            Expect.isEmpty (selected g) "removed, not added a second time"
        }
    ]

let tests =
    testList "Section 10 — Grouping" [
        tc10Tests
        crudTests
        expandToLeafTests
        virtualListWindowTests
        rangeSelectionTests
    ]
