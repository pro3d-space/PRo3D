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

open Aardvark.VRVis.Opc

open OpcViewer.Base
open OpcViewer.Base.Picking

open Aardvark.UI.Operators
open Aardvark.UI.Trafos  

open PRo3D
open PRo3D.Groups
open PRo3DCompability


module DebugKdTreesX = 
    open Aardvark.VRVis.Opc.KdTrees
    open MBrace.FsPickler
    open Aardvark.Geometry
    open OpcViewer.Base
   
    let getInvalidIndices3f (positions : V3f[]) =
        positions |> List.ofArray |> List.mapi (fun i x -> if x.AnyNaN then Some i else None) |> List.choose id    

    let getTriangleSet3f (vertices:V3f[]) =
      vertices 
        |> Seq.map(fun x -> x.ToV3d())
        |> Seq.chunkBySize 3
        |> Seq.filter(fun x -> x.Length = 3)
        |> Seq.map(fun x -> Triangle3d x)
        |> Seq.filter(fun x -> (IntersectionController.triangleIsNan x |> not)) |> Seq.toArray
        |> TriangleSet

    let getTriangleSet (indices : int[]) (vertices:V3d[]) = 
      indices 
        |> Seq.map(fun x -> vertices.[x])
        |> Seq.chunkBySize 3
        |> Seq.map(fun x -> Triangle3d(x))
        |> Seq.filter(fun x -> (IntersectionController.triangleIsNan x |> not)) |> Seq.toArray
        |> TriangleSet

    let loadTriangles (kd : LazyKdTree) =
        
        let positions = kd.objectSetPath |> Aara.fromFile<V3f>
                
        let invalidIndices = getInvalidIndices3f positions.Data |> List.toArray
        let size = positions.Size.XY.ToV2i()
        let indices = PRo3DCSharp.ComputeIndexArray(size, invalidIndices)
                  
       // Log.warn "num of inv_indices: %A" invalidIndices.Length
       // Log.warn "num of indices: %A" indices.Length
                       
        positions.Data 
          |> Array.map (fun x ->  x.ToV3d() |> kd.affine.Forward.TransformPos) 
          |> getTriangleSet indices

    let loadObjectSet (cache : HashMap<string, ConcreteKdIntersectionTree>) (lvl0Tree : Level0KdTree) =             
      match lvl0Tree with
        | InCoreKdTree kd -> 
          kd.kdTree, cache
        | LazyKdTree kd ->             
          let kdTree, cache =
            match kd.kdTree with
            | Some k -> k, cache
            | None -> 
              let key = (kd.boundingBox.ToString())
              let tree = cache |> HashMap.tryFind key
              match tree with
              | Some t ->                 
                t, cache
              | None ->                                     
                Log.line "cache miss %A- loading kdtree" kd.boundingBox
            
                let mutable tree = KdTrees.loadKdtree kd.kdtreePath      
                let triangles = kd |> loadTriangles
                
                tree.KdIntersectionTree.ObjectSet <- triangles                                                            
                tree, (HashMap.add key tree cache)
          kdTree, cache

    let getTriangle (set : TriangleSet) (index : int) : Triangle3d =
      let pi = index * 3
      let pl = set.Position3dList
      Triangle3d(pl.[pi], pl.[pi+1], pl.[pi + 2])

    let isNotOversized (size) (triangle:Triangle3d) =      
      ((Vec.Distance(triangle.P0, triangle.P1) < size) && 
       (Vec.Distance(triangle.P0, triangle.P2) < size) &&
       (Vec.Distance(triangle.P1, triangle.P2) < size))

    let intersectKdTrees bb (hitObject : PRo3D.Surfaces.Surface) (cache : HashMap<string, ConcreteKdIntersectionTree>) (ray : FastRay3d) (kdTreeMap: HashMap<Box3d, KdTrees.Level0KdTree>) = 

        let kdtree, c = kdTreeMap |> HashMap.find bb |> loadObjectSet cache

        let kdi = kdtree.KdIntersectionTree 
        let mutable hit = ObjectRayHit.MaxRange
                        
        try
          let hitFilter = //true means being omitted
            fun (a:IIntersectableObjectSet) (b:int) _ _ -> 
            let triangles = a :?> TriangleSet //TODO TO crashes if not encountering a triangleset
            b |> getTriangle triangles |> isNotOversized hitObject.triangleSize.value |> not // = tooBig            

          if kdi.Intersect(ray, null, Func<IIntersectableObjectSet,int,int, RayHit3d,bool>(hitFilter), 0.0, Double.MaxValue, &hit) then              
              Some (hit.RayHit.T, hitObject),c
          else            
              None,c
        with 
          | e -> 
            Log.error "[DebugKdtrees] error in kdtree intersection" 
            Log.error "%A" e
            None,c
  
module Files = 
       open System.IO
       open Aardvark.Prinziple

       type DiscoverFolder = 
          | OpcFolder of string
          | Directory of string  
       
       /// <summary>
       /// checks if "path" is a valid opc folder containing "images" or "Images", "patches" or "Patches", and patchhierarchy.xml
       /// </summary>
       let isOpcFolder (path : string) = 
           printfn "isOpc: %A" path
           let imagePath = Path.combine [path; "images"]
           let patchPath = Path.combine [path; "patches"]
           (Directory.Exists imagePath) &&
           (Directory.Exists patchPath) && 
            File.Exists(Path.combine [patchPath;"patchhierarchy.xml"])

       /// <summary>
       /// checks if "path" is a valid surface folder i.e. contains at least one opc folder
       /// </summary>        
       //let isSurfaceFolder (path : string) =
       //    Directory.GetDirectories(path) |> Seq.forall isOpcFolder
       let isSurfaceFolder (path : string) =
            let subdirs = Directory.GetDirectories(path)
            let containsOpcDir =
                match subdirs.IsEmptyOrNull() with
                | true -> false
                | _ -> subdirs |> Seq.map (fun (x : string) -> isOpcFolder x)
                               |> Seq.reduce (fun x y -> x || y)
            containsOpcDir

       /// returns all subdirectories of path for which function p returns true
       let discover (p : string -> bool) path : list<string> =
         if Directory.Exists path then
           Directory.EnumerateDirectories path                     
             |> Seq.filter p            
             |> Seq.toList
         else List.empty

       /// returns all valid surface folders in "path"   
       let discoverSurfaces path = 
         discover isSurfaceFolder path          

       let discoverOpcs path = 
         discover isOpcFolder path

       /// <summary>
       /// should open zip and call isOPCFolder
       /// </summary>        
       let isZippedOpcFolder (path : string) = 
         File.Exists(Path.ChangeExtension(path, "zip"))

       let whyEscaping (path : string) = 
         path
         //path.Replace(@"\", @"\\\\")


       let private retrieve (f :string -> string) (path : string) : list<string> =
           if path = String.Empty then failwith "encountered empty opc path"

           if isOpcFolder path then
               [whyEscaping path]
           elif isZippedOpcFolder path then                
               [(whyEscaping path).Replace(".zip", "")]
           elif Directory.Exists path then
               let opcDirs = 
                 Directory.GetDirectories(path)
                   |> Seq.map (fun a -> whyEscaping a)
                   |> Seq.filter isOpcFolder
                   |> Seq.map f
                   |> Seq.toList            

               let zipped =
                 Directory.GetFiles(path)
                   |> Seq.map (fun a -> whyEscaping a)
                   |> Seq.filter isZippedOpcFolder
                   |> Seq.map (fun a -> a.Replace(".zip", ""))
                   |> Seq.map f
                   |> Seq.toList            

               opcDirs @ zipped
           else []

       let toDiscoverFolder path = 
        if path |> isOpcFolder || path |> isZippedOpcFolder then
          OpcFolder path
        else
          Directory path

       let tryDirectoryExists path = 
        if Directory.Exists path then Some path else None

       let rec superDiscovery (input : string) :  string * list<string> =
        match input |> toDiscoverFolder with
        | Directory path -> 
          let opcs = 
            path 
              |> Directory.EnumerateDirectories 
              |> Seq.toList 
              |> List.map(fun x -> superDiscovery x |> snd) |> List.concat 
          input, opcs
        | OpcFolder p -> input,[p]

       let rec superDiscoveryMultipleSurfaceFolder (input : string) : list<string> =
        let isSurfFolder = input |> isSurfaceFolder
        let res = 
            match isSurfFolder with
                | true -> [input]
                | _-> 
                    let res = 
                        input 
                            |> Directory.EnumerateDirectories 
                            |> Seq.toList 
                            |> List.map(fun x -> superDiscoveryMultipleSurfaceFolder x ) |> List.concat
                    res
        res

       /// <summary>
       /// returns all valid OPCs in "path"
       /// </summary>
       let getOPCPaths path = 
         path |> retrieve (id)
           
       /// <summary>
       /// returns all valid OPCs in "path"
       /// </summary>
       let getOPCNames path = 
         path |> retrieve (Path.GetFileName)            

       let expandNamesToPaths (path : string) (names : List<string>) =
           printfn "names: %A" names
           names |> List.map(fun a -> whyEscaping (Path.combine [path; a])) // what is this???
           //names |> List.map(fun a -> (Path.combine [path; a]))
       
       let getSurfaceFolder (surface:PRo3D.Surfaces.Surface) (scenePath:option<string>) =
         if surface.relativePaths 
         then
           match scenePath with
             | Some sp -> 
                 let x = Path.Combine((Path.GetDirectoryName sp),"Surfaces")
                 let relPath = Path.Combine(x, surface.name)
                 if Directory.Exists relPath then Some relPath else None
             | None -> None
         else
           match surface.surfaceType with
             | SurfaceType.SurfaceOBJ -> 
               let p = Path.GetDirectoryName surface.importPath
               if Directory.Exists p then Some p else None
             |_-> 
               let p = surface.importPath
               if Directory.Exists p then Some p else None

       /// strips away parts of a file path until the remaining depth is reached
       /// [RNO] TODO CAN THROW ERRORS - REWRITE!!
       let relativePath (path : string) (remaining : int) = 
           let parts = 
               path.Split(Path.DirectorySeparatorChar) 
           //match parts.Length < remaining
           parts
               |> List.ofArray 
               |> List.rev 
               |> List.take remaining 
               |> List.rev 
               |> Path.combine

       let sceneRelativePath (path : string) = 
           relativePath path 5 

       let surfaceRelativePath (path : string) =
           relativePath path 4

       let expandLazyKdTreePaths (scenePath : option<string>) (surfaces : HashMap<Guid, PRo3D.Surfaces.Surface>) (sgSurfaces : HashMap<Guid, SgSurface>) =
         let expand surf tree =
             match tree with 
             | KdTrees.Level0KdTree.LazyKdTree lk when surf.relativePaths && scenePath.IsSome -> // scene is portable                                        
                 let path = Path.Combine((Path.GetDirectoryName scenePath.Value),"Surfaces")
                 let kdTreeSub   = lk.kdtreePath    |> sceneRelativePath
                 let triangleSub = lk.objectSetPath |> sceneRelativePath
                        
                 KdTrees.LazyKdTree { 
                     lk with 
                         kdtreePath    = Path.Combine(path, kdTreeSub)
                         objectSetPath = Path.Combine(path, triangleSub)
                 }
             | KdTrees.Level0KdTree.LazyKdTree lk -> // surfaces have absolute paths                  
                 let kdTreeSub   = lk.kdtreePath    |> surfaceRelativePath
                 let triangleSub = lk.objectSetPath |> surfaceRelativePath                                                                                
                        
                 KdTrees.LazyKdTree {
                     lk with 
                         kdtreePath    = Path.Combine(surf.importPath, kdTreeSub)
                         objectSetPath = Path.Combine(surf.importPath, triangleSub)
                 }
             | KdTrees.Level0KdTree.InCoreKdTree ik -> KdTrees.InCoreKdTree ik // kdtrees can be loaded as is
         
         sgSurfaces 
          |> HashMap.map (fun _ s -> 
            match s.picking with
              | Picking.KdTree ks ->
                let surf = surfaces |> HashMap.find s.surface
                match surf.surfaceType with 
                | SurfaceType.SurfaceOBJ -> s
                | _ -> 
                    let kd = 
                        ks |> HashMap.map (fun _ k -> expand surf k)
                    { s with picking = Picking.KdTree kd }
              | Picking.PickMesh ms -> s
          )                

       let makeSurfaceRelative guid (surfaceModel : SurfaceModel) (scenePath : option<string>) =
           match scenePath with
               | Some sp ->
                   let surf = SurfaceModel.getSurface surfaceModel guid
                   match surf with
                       | Some s ->
                           // create target dir
                           let targetSurfaceDir = Path.Combine((Path.GetDirectoryName sp),"Surfaces")
                           if not (Directory.Exists targetSurfaceDir) then
                               Directory.CreateDirectory(targetSurfaceDir) |> ignore

                           // copy data
                           match s with
                               |Leaf.Surfaces sf -> 
                                   let targetDir = Path.Combine(targetSurfaceDir, sf.name)
                                   Copy.copyAll sf.importPath targetDir true

                                   // update opc paths and scene
                                   let s' = { sf with opcPaths = expandNamesToPaths targetDir sf.opcNames; relativePaths = true }
                                   surfaceModel |> SurfaceModel.updateSingleSurface s'
                               | _ ->
                                   Log.error "surface %s not found" (guid.ToString())
                                   surfaceModel
                       | None ->
                           Log.error "surface %s not found" (guid.ToString())
                           surfaceModel
               | None -> 
                   Log.error "can't make surface relative. no valid scene path has not been saved yet."
                   surfaceModel

       