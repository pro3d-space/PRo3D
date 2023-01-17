namespace PRo3D.Core.Surface

open System
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.SceneGraph.IO
open Aardvark.SceneGraph.Opc

open Aardvark.VRVis.Opc

open OpcViewer.Base
open OpcViewer.Base.Picking

open Aardvark.UI.Operators
open Aardvark.UI.Trafos  

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3DCompability


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
           printfn "[Surface.Files] checking potential opc path: %A" path
           let imagesProbingPaths = 
                OpcPaths.Images_DirNames |> List.map (fun imageSuffix -> Path.combine [path; imageSuffix])
           let patchesProbingPaths =
                OpcPaths.Patches_DirNames|> List.map (fun patchSuffix -> Path.combine [path; patchSuffix])

           let imagesFound = imagesProbingPaths |> List.exists Directory.Exists
           let patchesFound = imagesProbingPaths |> List.exists Directory.Exists
           let patchHierarchyProbingPath = Path.Combine(path, OpcPaths.PatchHierarchy_FileName)

           imagesFound && patchesFound && Directory.Exists patchHierarchyProbingPath

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
       
       let getSurfaceFolder (surface : Surface) (scenePath : option<string>) =
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
             | SurfaceType.Mesh -> 
               let p = Path.GetDirectoryName surface.importPath
               if Directory.Exists p then Some p else None
             |_-> 
               let p = surface.importPath
               if Directory.Exists p then Some p else None


        // glub/gah/gugu/ghzu/aaa
        //ghzu/aaaa

        // aaa/ghzu/gugu/gah/glub


       /// strips away parts of a file path until the remaining depth is reached
       let private regex = System.Text.RegularExpressions.Regex("\\\\|/")
       let relativePath (path : string) (remaining : int) = 
           let parts = regex.Split(path)
           if parts.Length > remaining then 
                Array.skip (parts.Length - remaining) parts 
                |> Path.combine
                |> Some
           else 
                None

       let sceneRelativePath (path : string) = 
           relativePath path 5 

       let surfaceRelativePath (path : string) =
           relativePath path 4

       let expandLazyKdTreePaths (scenePath : option<string>) (surfaces : HashMap<Guid, Surface>) (sgSurfaces : HashMap<Guid, SgSurface>) =
         let expand surf tree =
             match tree with 
             | KdTrees.Level0KdTree.LazyKdTree lk when surf.relativePaths && scenePath.IsSome -> // scene is portable   
             
                 let path = Path.Combine((Path.GetDirectoryName scenePath.Value),"Surfaces")

                 match sceneRelativePath lk.kdtreePath, sceneRelativePath lk.objectSetPath with
                 | Some kdTreeSub, Some triangleSub -> 
                     KdTrees.LazyKdTree { 
                         lk with 
                             kdtreePath    = Path.Combine(path, kdTreeSub)
                             objectSetPath = Path.Combine(path, triangleSub)
                     } |> Some
                 | _ -> 
                     Log.warn "[expandLazyKdTreePaths] could not create relative paths for %A" (lk.kdtreePath, lk.objectSetPath)
                     None
             | KdTrees.Level0KdTree.LazyKdTree lk -> // surfaces have absolute paths      

                 match surfaceRelativePath lk.kdtreePath, surfaceRelativePath  lk.objectSetPath with
                 | Some kdTreeSub, Some triangleSub ->
                     KdTrees.LazyKdTree {
                         lk with 
                             kdtreePath    = Path.Combine(surf.importPath, kdTreeSub)
                             objectSetPath = Path.Combine(surf.importPath, triangleSub)
                     } |> Some
                 | _ -> 
                     Log.warn "[expandLazyKdTreePaths] could not create relative paths for %A" (lk.kdtreePath, lk.objectSetPath)
                     None
             | KdTrees.Level0KdTree.InCoreKdTree ik -> 

                 KdTrees.InCoreKdTree ik |> Some // kdtrees can be loaded as is
         
         sgSurfaces 
          |> HashMap.choose (fun _ s -> 
            match s.picking with
              | Picking.NoPicking -> None
              | Picking.KdTree ks ->
                match surfaces |> HashMap.tryFind s.surface with
                | Some surf ->
                    match surf.surfaceType with 
                    | SurfaceType.Mesh -> Some s
                    | _ -> 
                        let kd = 
                            ks 
                            |> HashMap.choose (fun box k -> 
                                    // here we skip failed kd-trees if necessary
                                    expand surf k
                               )

                        { s with picking = Picking.KdTree kd } |> Some
                | _ -> 
                    // note: for robustness we skip unknown surfaces
                    Log.warn "[expandLazyKdTreePaths] surface not found. Cannot pick"
                    None
              | Picking.PickMesh ms -> 
                s |> Some
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
                                   PRo3D.Base.Copy.copyAll sf.importPath targetDir true

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
