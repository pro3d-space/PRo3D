open MBrace.FsPickler

open System
open System.IO
open Aardvark.Base
open Aardvark.SceneGraph.Opc
open Aardvark.GeoSpatial.Opc
open Aardvark.VRVis.Opc
open Aardvark.Rendering.SceneGraph.HierarchicalLoD


let traverse (pathHierarchies: seq<string>) : unit =
    
    let serializer = PRo3D.Base.Serialization.binarySerializer

    let _ =
        pathHierarchies
        |> Seq.toList
        |> List.map (fun basePath ->
            let h =
                PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
            //let t = PatchLod.toRoseTree h.tree
            let kdTrees =
                KdTrees.loadKdTrees' h Trafo3d.Identity true ViewerModality.XYZ serializer true

            kdTrees)

    Log.line "Done."


[<EntryPoint>]
let main args =

    PRo3D.Base.Serialization.init()
  
    PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
    PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> PRo3D.Core.Surface.Init.incorePickler)

    let hierarchies =
        Directory.GetDirectories(@"F:\pro3d\data\OpcHera")

    traverse hierarchies

    0
