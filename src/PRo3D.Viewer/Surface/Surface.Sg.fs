namespace PRo3D.Surfaces

open System
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.SceneGraph.IO
open Aardvark.SceneGraph.Opc
open Aardvark.Prinziple
open Aardvark.VRVis.Opc

open Aardvark.GeoSpatial.Opc

open Aardvark.UI.Operators
open Aardvark.UI.Trafos  

open PRo3D
open PRo3D.Base
open PRo3D.Groups

module Sg =

    type SgHelper = {
        surf   : PRo3D.Surfaces.Surface
        bb     : Box3d
        sg     : ISg        
        kdtree : HashMap<Box3d,KdTrees.Level0KdTree>
    }

    let mutable hackRunner : Option<Load.Runner> = None

    let mars 
        (preTrafo    : Trafo3d)
        (self        : AdaptiveToken) 
        (viewTrafo   : aval<Trafo3d>)
        (_projection : aval<Trafo3d>) 
        (p           : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) 
        (lodParams   : aval<LodParameters>)
        =

        let lodParams = lodParams.GetValue self
       
        let campPos = (viewTrafo.GetValue self).Backward.C3.XYZ
        let bb      = p.info.GlobalBoundingBox.Transformed(lodParams.trafo * preTrafo ) //* preTrafo)
        let closest = bb.GetClosestPointOn(campPos)
        let dist    = (closest - campPos).Length

        let unitPxSize = lodParams.frustum.right / (float lodParams.size.X / 2.0)
        let px = (lodParams.frustum.near * p.triangleSize) / dist
                
    //    Log.warn "%f to %f - avgSize: %f" px (unitPxSize * lodParams.factor) p.triangleSize
        px > unitPxSize * (exp lodParams.factor)

    let createPlainSceneGraph (runtime : IRuntime) (signature : IFramebufferSignature) (scene : OpcScene) (createKdTrees)
        : (ISg * list<PatchHierarchy> * HashMap<Box3d, KdTrees.Level0KdTree>) =

        let runner = 
            match hackRunner with
            | None -> 
                printfn "create runner"
                //let  r = runtime.CreateLoadRunner 2
                //hackRunner <- Some (r)
                failwith ""
            | Some h -> h
        let preTransform = scene.preTransform
    
        let patchHierarchies = 
            scene.patchHierarchies
            |> Seq.map Prinziple.registerIfZipped
            |> Seq.map (fun x -> 
                PatchHierarchy.load Serialization.binarySerializer.Pickle Serialization.binarySerializer.UnPickle (OpcPaths x)
            )
            |> Seq.toList
                            
        let kdTreesPerHierarchy =
            [| 
                for h in patchHierarchies do
                    if createKdTrees then   
                        yield KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ Serialization.binarySerializer                    
                    else 
                        yield HashMap.empty
            |]

        let totalKdTrees = kdTreesPerHierarchy.Length
        Log.line "creating %d kdTrees" totalKdTrees

        let kdTrees = 
            kdTreesPerHierarchy                     
            |> Array.Parallel.mapi (fun i e ->
                Log.start "creating kdtree #%d of %d" i totalKdTrees
                let r = e
                Log.stop()
                r
            )
            |> Array.fold (fun a b -> HashMap.union a b) HashMap.empty
        
        let mars = mars (scene.preTransform)

        // create level of detail hierarchy (Sg)
        let g = 
            patchHierarchies 
            |> List.map (fun h ->                                  
                Sg.patchLod 
                    signature
                    runner 
                    h.opcPaths.Opc_DirAbsPath
                    mars //scene.lodDecider 
                    scene.useCompressedTextures
                    ViewerModality.XYZ
                    (PatchLod.toRoseTree h.tree)
            )
            |> Sg.ofList                
                                                
        g, patchHierarchies, kdTrees
    
    let assertInvalidBB (bb:Box3d) = 
        if(bb.IsInvalid) then 
            failwith (sprintf "invalid bounding box %s" (bb.ToString()))
        else bb
    
    let combineLeafBBs (hierarchies : list<PatchHierarchy>) =
      hierarchies
        |> List.map(fun d -> d.tree |> QTree.getLeaves)
        |> Seq.map (fun p -> p |> Seq.map(fun d -> assertInvalidBB d.info.GlobalBoundingBox)) 
        |> Seq.concat
        |> Box3d.ofSeq
    
    let combineRootBBs (hierarchies : list<PatchHierarchy>) =
      hierarchies
        |> List.map(fun d -> d.tree |> QTree.getRoot)                  
        |> Seq.map (fun p -> assertInvalidBB p.info.GlobalBoundingBox)
        |> Box3d.ofSeq
       
    let createSgSurface (s:PRo3D.Surfaces.Surface) sg (bb:Box3d) (kd:HashMap<Box3d,KdTrees.Level0KdTree>) = 
    
        let pose = Pose.translate V3d.Zero // bb.Center
        let trafo = { TrafoController.initial with pose = pose; previewTrafo = Pose.toTrafo pose; mode = TrafoMode.Local }
    
        let sgSurface = {
                surface    = s.guid
                sceneGraph = sg
                globalBB   = bb
                picking    = Picking.KdTree kd
                trafo      = trafo
                //transformation = Init.Transformations
            }
        sgSurface
    
    let transformBox (trafo:Trafo3d) (bb:Box3d) = bb.Transformed(trafo)
              
    let createSgSurfaces runtime signature (surfaces:IndexList<PRo3D.Surfaces.Surface>) =
      
        let surfaces = 
          surfaces
            |> IndexList.toList
            |> List.filter(fun s ->
                let dirExists = Directory.Exists s.importPath
                if dirExists |> not then 
                    Log.error "[Surface.Sg] could not find %s" s.importPath
                dirExists
            )
        
        let sghs =
            surfaces          
            |> List.map (fun d -> { d with opcPaths = d.opcNames |> Files.expandNamesToPaths d.importPath })
            |> List.map (fun d -> 
                { 
                   Configurations.Empty.mars() with
                       patchHierarchies      = d.opcPaths
                       preTransform          = d.preTransform
                       useCompressedTextures = false
                }
            )
            |> List.map (fun d -> createPlainSceneGraph runtime signature d true)
            |> List.zip surfaces
            |> List.map(fun (surf, (sg, hierarchies, kdtree)) -> 
                let bb = 
                    hierarchies 
                    |> combineLeafBBs 
                    |> transformBox surf.preTransform

                { surf = surf; bb = bb; sg = sg; kdtree = kdtree })
        
        let sgSurfaces =
          sghs 
          |> List.map (fun d -> createSgSurface d.surf d.sg d.bb d.kdtree)
          |> List.map (fun d -> (d.surface, d))
          |> HashMap.ofList       
        
        sgSurfaces
    
    let viewHomePosition (model:AdaptiveSurfaceModel) =
        let point = 
            aset{
                let! guid = model.surfaces.singleSelectLeaf
                let fail = Aardvark.UI.Extensions.Sg.empty

                match guid with
                    | Some i -> 
                        let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                        if exists then
                          let leaf = model.surfaces.flat |> AMap.find i 
                          let! surf = leaf 
                          let x = match surf with | AdaptiveSurfaces s -> s | _ -> surf |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith
                          let! hpos = x.homePosition
                          match hpos with
                              | Some p -> yield Sg.dot (AVal.constant C4b.Yellow) (AVal.constant 3.0) (AVal.constant p.Location)
                              | None -> yield fail
                        else
                          yield fail
                    | None -> yield fail
            }|> Aardvark.UI.``F# Sg``.Sg.set
        Aardvark.UI.``F# Sg``.Sg.ofList [point]
        
            
                                  

