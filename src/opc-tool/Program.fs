open MBrace.FsPickler

open System
open System.IO
open Aardvark.Base
open Aardvark.SceneGraph.Opc
open Aardvark.GeoSpatial.Opc
open Aardvark.VRVis.Opc
open Aardvark.Rendering.SceneGraph.HierarchicalLoD
 
let serializer = FsPickler.CreateBinarySerializer()

let traverse (pathHierarchies : seq<string>) : unit =
    
    let _ =
        pathHierarchies |> Seq.toList |> List.map (fun basePath -> 
            let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
            //let t = PatchLod.toRoseTree h.tree
            let kdTrees = KdTrees.loadKdTrees' h Trafo3d.Identity true ViewerModality.XYZ serializer
            kdTrees
        ) 

    Log.line "Done."


[<EntryPoint>]
let main args =

    let hierarchies = 
        Directory.GetDirectories(@"I:\OPC\GardenCity - Kopie") 
        |> Seq.collect System.IO.Directory.GetDirectories

    traverse hierarchies

    0