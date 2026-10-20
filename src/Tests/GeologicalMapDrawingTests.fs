/// Geological map actions the drawing app handles (#843): importing an annotation file that
/// carries maps, and giving units to selected label points.
module GeologicalMapDrawingTests

open System
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open Chiron
open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

open PRo3D.Tests

let private point (p : V3d) =
    let a = Annotation.make Projection.Linear None Geometry.Point None { c = C4b.White } Annotation.Initial.thickness ""
    { a with points = IndexList.single p }

/// A drawing model with group "geology" holding `inside`, `outside` in the root group, and an
/// open (enabled) map over "geology" with units Cr and Np, Cr active.
let private model () =
    let groups = GroupsApp.addGroupToRoot GroupsModel.initial "geology"
    let index, group =
        groups.rootGroup.subNodes |> IndexList.toSeqIndexed |> Seq.tryFind (fun (_, n) -> n.name = "geology")
        |> function Some g -> g | None -> failwith "group not created"
    let inside = point (V3d(77.0, 0.0, 0.0))
    let outside = point (V3d(0.0, 77.0, 0.0))
    let groups = GroupsApp.addLeaves [ index ] (IndexList.single (Leaf.Annotations inside)) groups
    let groups = GroupsApp.addLeaves [] (IndexList.single (Leaf.Annotations outside)) groups
    let cr = GeoUnit.make "Cr" C4b.Orange
    let map = { GeologicalMap.make "units" group.key with units = IndexList.ofList [ cr; GeoUnit.make "Np" C4b.Cyan ] }
    let maps = { GeologicalMapsModel.initial with maps = IndexList.single map; active = Some map.key; activeUnit = Some cr.id; enabled = true }
    { DrawingModel.initialdrawing with annotations = groups; geologicalMaps = maps }, inside, outside, map

let private unitOf (m : DrawingModel) (key : Guid) =
    match HashMap.tryFind key m.annotations.flat with
    | Some (Leaf.Annotations a) -> a.geoUnit
    | _ -> failtestf "no annotation %A" key

let private assign (selected : Guid) (m : DrawingModel) =
    let m = { m with annotations = { m.annotations with singleSelectLeaf = Some selected } }
    Draw.run Draw.refSystemFlat m (DrawingAction.GeologicalMapMessage GeologicalMapAction.AssignToSelected)

let tests () =
    testList "GeologicalMap drawing actions" [

        test "Assign to selected labels a point of the active map's group" {
            let m, inside, _, _ = model ()
            Expect.equal (unitOf (assign inside.key m) inside.key) (Some "Cr") "labelled"
        }

        test "Assign to selected leaves points outside the map's group alone" {
            let m, _, outside, _ = model ()
            Expect.equal (unitOf (assign outside.key m) outside.key) None "not labelled"
        }

        test "a new unit of an inactive map does not become the active unit" {
            let m, _, _, _ = model ()
            let other = GeologicalMap.make "other" (Guid.NewGuid())
            let maps = { m.geologicalMaps with maps = IndexList.add other m.geologicalMaps.maps }
            let after = GeologicalMapApp.update maps (GeologicalMapAction.AddUnit other.key)
            Expect.equal after.activeUnit maps.activeUnit "unchanged"
        }

        test "importing an annotation file brings its maps, on their renewed groups" {
            let m, inside, _, map = model ()
            let file : Annotations =
                { version         = Annotations.current
                  annotations     = m.annotations
                  dnsColorLegend  = FalseColorsModel.initDnSLegend
                  colorByCategory = ColorByCategoryModel.initial
                  geologicalMaps  = m.geologicalMaps }
            let path = Path.Combine(Path.GetTempPath(), sprintf "geomap-import-%s.pro3d.ann" (Guid.NewGuid().ToString("N")))
            try
                File.WriteAllText(path, file |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty)
                let start = { DrawingModel.initialdrawing with geologicalMaps = { GeologicalMapsModel.initial with enabled = true } }
                let once = Draw.run Draw.refSystemFlat start (DrawingAction.AddAnnotations [ path ])
                let twice = Draw.run Draw.refSystemFlat once (DrawingAction.AddAnnotations [ path ])
                match once.geologicalMaps.maps |> IndexList.toList with
                | [ imported ] ->
                    Expect.equal imported.name map.name "the map"
                    Expect.equal imported.units.Count 2 "its legend"
                    let fileUnits = map.units |> IndexList.toList |> List.map (fun u -> u.id)
                    Expect.isFalse (imported.units |> IndexList.toList |> List.exists (fun u -> List.contains u.id fileUnits)) "legend entries get new ids"
                    let ids = GeologicalMapApp.leafIdsOf once.annotations.rootGroup imported.sourceGroup
                    Expect.equal ids [| inside.key |] "its group, renewed on load, holds the point"
                | ms -> failtestf "%d maps after one import" ms.Length
                let keys = twice.geologicalMaps.maps |> IndexList.toList |> List.map (fun x -> x.key)
                Expect.equal (List.distinct keys).Length 2 "the same file twice: two maps, two keys"
            finally
                File.Delete path
        }
    ]
