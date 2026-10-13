/// Geological maps (#843) ride along in the `.pro3d.ann` file under new keys: the map list
/// as `geologicalMaps` on the Annotations record, the label unit as `geoUnit` on annotations.
/// Older files have neither; older releases skip both.
module GeologicalMapPersistenceTests

open System
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive
open Chiron
open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

let private serialize (x : Annotations) =
    x |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty

let private labelPoint (code : string) =
    let a = Annotation.make Projection.Linear None Geometry.Point None { c = C4b.White } Annotation.Initial.thickness ""
    { a with points = IndexList.single (V3d(77.0, 0.0, 0.0)); geoUnit = Some code }

/// An annotation file with one group holding a label point, and a map over that group.
let private withMap () =
    let groups = GroupsApp.addGroupToRoot GroupsModel.initial "geology"
    let index, group =
        groups.rootGroup.subNodes |> IndexList.toSeqIndexed |> Seq.tryFind (fun (_, n) -> n.name = "geology")
        |> function Some g -> g | None -> failwith "group not created"
    let point = labelPoint "Cr"
    let groups = GroupsApp.addLeaves [ index ] (IndexList.single (Leaf.Annotations point)) groups
    let map =
        { GeologicalMap.make "units" group.key with
            units   = IndexList.ofList [ GeoUnit.make "Cr" C4b.Orange; GeoUnit.make "Np" C4b.Cyan ]
            opacity = { GeologicalMap.initialOpacity with value = 0.7 } }
    let file =
        {
            version         = Annotations.current
            annotations     = groups
            dnsColorLegend  = FalseColorsModel.initDnSLegend
            colorByCategory = ColorByCategoryModel.initial
            geologicalMaps  = { GeologicalMapsModel.initial with maps = IndexList.single map; active = Some map.key }
        }
    file, point, map

let tests () =
    testList "GeologicalMap persistence" [

        test "maps and label units round-trip through the .ann format" {
            let file, point, map = withMap ()
            let restored : Annotations = file |> serialize |> Json.parse |> Json.deserialize

            Expect.equal restored.geologicalMaps.active (Some map.key) "active map"
            match restored.geologicalMaps.maps |> IndexList.toList with
            | [ m ] ->
                Expect.equal m.name map.name "name"
                Expect.equal m.sourceGroup map.sourceGroup "group"
                Expect.equal m.opacity.value 0.7 "opacity"
                Expect.equal (m.units |> IndexList.toList |> List.map (fun u -> u.code, u.color.c))
                             [ "Cr", C4b.Orange; "Np", C4b.Cyan ] "legend"
            | ms -> failtestf "%d maps" ms.Length
            match HashMap.tryFind point.key restored.annotations.flat with
            | Some (Leaf.Annotations a) -> Expect.equal a.geoUnit (Some "Cr") "label unit"
            | _ -> failtest "label point lost"
        }

        test "a file without the new keys loads with no maps and no label units" {
            let file, point, _ = withMap ()
            // what an older release writes: neither key
            let json =
                match file |> serialize |> Json.parse with
                | Json.Object o ->
                    let strip (j : Json) =
                        match j with
                        | Json.Object a -> Json.Object (Map.remove "geoUnit" a)
                        | j -> j
                    let anns =
                        match Map.tryFind "annotations" o with
                        | Some (Json.Object g) ->
                            let flat =
                                match Map.tryFind "flat" g with
                                | Some (Json.Array leaves) ->
                                    leaves |> List.map (fun l ->
                                        match l with
                                        | Json.Object c -> Json.Object (c |> Map.map (fun _ v -> strip v))
                                        | l -> l) |> Json.Array
                                | _ -> Json.Array []
                            Json.Object (Map.add "flat" flat g)
                        | _ -> Json.Null ()
                    Json.Object (o |> Map.remove "geologicalMaps" |> Map.add "annotations" anns)
                | j -> j
            let restored : Annotations = json |> Json.format |> Json.parse |> Json.deserialize
            Expect.equal restored.geologicalMaps.maps.Count 0 "no maps"
            match HashMap.tryFind point.key restored.annotations.flat with
            | Some (Leaf.Annotations a) -> Expect.isNone a.geoUnit "no label unit"
            | _ -> failtest "label point lost"
        }

        test "maps a newer release wrote, or malformed ones, are skipped - the annotations still load" {
            let file, point, map = withMap ()
            let edit (f : Map<string, Json> -> Map<string, Json>) =
                match file |> serialize |> Json.parse with
                | Json.Object o -> Json.Object (f o)
                | j -> j
            let withMaps (maps : Json) =
                edit (fun o ->
                    match Map.tryFind "geologicalMaps" o with
                    | Some (Json.Object g) -> Map.add "geologicalMaps" (Json.Object (Map.add "maps" maps g)) o
                    | _ -> o)
            let good =
                match file.geologicalMaps.maps |> IndexList.toList with
                | [ m ] -> Json.serialize m
                | _ -> failwith "one map expected"
            let newer =
                match good with
                | Json.Object m -> Json.Object (Map.add "version" (Json.Number 9M) m)
                | j -> j
            let malformed =
                match good with
                | Json.Object m -> Json.Object (Map.add "key" (Json.String "not a guid") m)
                | j -> j
            let restored : Annotations = withMaps (Json.Array [ newer; good; malformed ]) |> Json.format |> Json.parse |> Json.deserialize
            Expect.equal (restored.geologicalMaps.maps |> IndexList.toList |> List.map (fun m -> m.key)) [ map.key ] "only the readable map"
            Expect.isTrue (HashMap.containsKey point.key restored.annotations.flat) "annotations loaded"

            let whole =
                edit (fun o ->
                    Map.add "geologicalMaps" (Json.Object (Map.ofList [ "version", Json.Number 7M; "maps", Json.Array [] ])) o)
            let restored : Annotations = whole |> Json.format |> Json.parse |> Json.deserialize
            Expect.equal restored.geologicalMaps.maps.Count 0 "a newer maps format: no maps"
            Expect.isTrue (HashMap.containsKey point.key restored.annotations.flat) "annotations still loaded"
        }

        test "a unit code already used in the map is refused" {
            let file, _, map = withMap ()
            let np = map.units |> IndexList.toArray |> Array.tryFind (fun u -> u.code = "Np") |> Option.defaultWith (fun () -> failwith "no Np")
            let renamed = GeologicalMapApp.update file.geologicalMaps (GeologicalMapAction.SetUnitCode(map.key, np.id, "Cr"))
            let codes = renamed.maps |> IndexList.toArray |> Array.collect (fun m -> m.units |> IndexList.toArray |> Array.map (fun u -> u.code))
            Expect.equal codes [| "Cr"; "Np" |] "unchanged"
        }

        test "the map follows its group through the group-key renewal on load" {
            let file, point, _ = withMap ()
            let loaded = file |> serialize |> DrawingUtilities.IO.loadAnnotationsFromJson
            match loaded.geologicalMaps.maps |> IndexList.toList with
            | [ m ] ->
                let ids = GeologicalMapApp.leafIdsOf loaded.annotations.rootGroup m.sourceGroup
                Expect.equal ids [| point.key |] "the map's group still holds the label point"
            | ms -> failtestf "%d maps" ms.Length
        }
    ]
