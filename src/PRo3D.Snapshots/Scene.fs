namespace PRo3D

open System
open System.IO
open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open Aardvark.SceneGraph.Opc
open Aardvark.UI.Primitives

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.Navigation2

open Chiron

open Aether
open Aether.Operators

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Scene =
    let toModSurface (leaf : AdaptiveLeafCase) = 
          adaptive {
             let c = leaf
             match c with 
                 | AdaptiveSurfaces s -> return s
                 | _ -> return c |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith
             }
      
    let lookUp guid (scene : AdaptiveScene) =
        let surfaces = scene.surfacesModel.surfaces.flat
        let entry = surfaces |> AMap.find guid
        entry |> AVal.bind(fun x -> x |> toModSurface)

    let completeTrafo guid (scene : AdaptiveScene) = 
        let surf = lookUp guid scene
        adaptive {
            let! fullTrafo = SurfaceTransformations.fullTrafo surf scene.referenceSystem
            let! s = surf
            let! sc = s.scaling.value
            let! t = s.preTransform
            return Trafo3d.Scale(sc) * (t * fullTrafo )
        }

    let isVisibleSurfaceObj guid (scene : AdaptiveScene) =
        let surfaces = scene.surfacesModel.surfaces.flat 
        adaptive {
            let! exists = surfaces |> AMap.keys |> ASet.contains guid
            let isObj =
                match exists with
                | false -> AVal.constant false
                | true  ->
                    let surf = lookUp guid scene
                    let surfType = AVal.map (fun (s : AdaptiveSurface) -> s.surfaceType) surf
                    let isObj = 
                        surfType |> AVal.map (fun st ->
                                                match st with
                                                | SurfaceType.SurfaceOBJ -> true
                                                | SurfaceType.SurfaceOPC -> false
                                                | _ -> true
                                            )
                    let isVisible = surf |> AVal.bind (fun s -> s.isVisible)
                    AVal.map2 (fun a b -> a && b) isObj isVisible
            return! isObj        
        }

    let calculateSceneBoundingBox (scene : AdaptiveScene) (noOpcs : bool) =
        let surfaces = scene.surfacesModel.surfaces.flat 
        let noOpcs guid =
            match noOpcs with
            | true -> isVisibleSurfaceObj guid scene
            | false -> true |> AVal.constant

        let sgSurfaces = scene.surfacesModel.sgSurfaces
                            |> AMap.filterA (fun g sg -> noOpcs g)

        let combine b1 b2 =
            adaptive {
                let! b1 = b1
                let! b2 = b2
                return Box3d.ofSeq [b1;b2]
            }
  
        let calcSbb () =
            adaptive {
                let trafos =
                    sgSurfaces
                        |> AMap.map (fun guid surf -> completeTrafo guid scene)
                let transformBB (sgSurf : AdaptiveSgSurface) =
                    adaptive {
                        let! bb = sgSurf.globalBB
                        let! trafo = AMap.find sgSurf.surface trafos
                        let! trafo = trafo
                        return bb.Transformed trafo
                    }
                let transformedBBs =
                    sgSurfaces
                        |> AMap.map (fun guid sg -> sg |> transformBB)
                let bbList =
                    transformedBBs
                        |> AMap.toASet
                        |> ASet.toAList
                        |> AList.map snd
   
                let! plst = AList.toAVal bbList
                let s = plst |> IndexList.toList
                let sceneBb = List.fold combine s.Head s
                let! sbb = sceneBb
                return! sceneBb
            }   
  
        sgSurfaces 
          |> AMap.keys  
          |> ASet.count 
          |> AVal.bind (fun c -> 
                        match c > 0 with
                        | true -> calcSbb ()
                        | false -> Box3d.Unit |> AVal.constant
                      )

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Model =    

    let withScene (s:Scene) (m:Model) =
        { m with scene = s}

    let withScene' (m:Model) (s:Scene) =
        { m with scene = s}

    let private stashAndSaveRecent' (s : SceneHandle) (model : Model) =
        let scenePaths = 
          model.recent.recentScenes                 
            |> List.filter (fun x -> x.path <> s.path)   
            |> List.append [s]
        
        let recent = { model.recent with recentScenes = scenePaths }

        Serialization.save "./recent" recent |> ignore

        { model with recent = recent }

    let stashAndSaveRecent (path : string) (model : Model) =
        let handle = {
            name = Path.GetFileName(path)
            path = path
            writeDate = DateTime.Now
        }
        stashAndSaveRecent' handle model

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SceneLoader =        
    
    module Minerva =
        let defaultDumpFile =  @".\MinervaData\dump.csv"
        let defaultCacheFile = @".\MinervaData\dump.cache"

        
    let _surfaceModelLens = Model.scene_ >-> Scene.surfacesModel_
    let _flatSurfaces     = Scene.surfacesModel_ >-> SurfaceModel.surfaces_ >-> GroupsModel.flat_
    let _camera           = Model.navigation_ >-> NavigationModel.camera_
    let _cameraView       = _camera >-> CameraControllerState.view_


    let expandRelativePaths (m:Scene) =               
        match m.scenePath with
        | Some p ->         
            let p' = Path.Combine((Path.GetDirectoryName p), "Surfaces")
            let flat' = 
                (m.surfacesModel.surfaces.flat |> Leaf.toSurfaces)
                |> HashMap.map(fun _ x ->                            
                    if x.relativePaths then
                        let p'' = Path.Combine(p', x.name)
                        { x with opcPaths = Files.expandNamesToPaths p'' x.opcNames }
                    else x
                )
                |> HashMap.map(fun _ x -> Leaf.Surfaces x)
            
            let sm = { m.surfacesModel.surfaces with flat = flat' }                
            
            { m with surfacesModel = { m.surfacesModel with surfaces = sm }}
        | None -> m        

    let private readLine (filePath:string) =
      use sr = new System.IO.StreamReader (filePath)
      sr.ReadLine ()       

    let addLegacyTrafos surfaces = 
        surfaces 
        |> IndexList.map(fun s -> s,Path.ChangeExtension(s.importPath, ".trafo"))
        |> IndexList.map(fun(s,p) ->                     
           match (Serialization.fileExists p) with
           | Some path->
               let t = readLine path
               Log.line "[TRAFO] Importing trafo: %s" (t.ToString ())
               { s with preTransform = Trafo3d.Parse(t) }
           | None -> s
        )

    let getOPCxPath (surfacePath : string) = 
        let name = Path.GetFileName surfacePath
        Path.ChangeExtension(Path.Combine(surfacePath, name), ".opcx")

    let addSurfaceAttributes surfaces = 
        surfaces 
        |> IndexList.map(fun s -> s, s.importPath |> getOPCxPath)
        |> IndexList.map(fun(s,p) -> 
           match (Serialization.fileExists p) with
           | Some path->        
               let layers = SurfaceUtils.SurfaceAttributes.read path
               let textures = layers |> SurfaceProperties.getTextures
        
               { s with 
                   scalarLayers  = layers |> SurfaceProperties.getScalarsHmap //SurfaceProperties.getScalars
                   textureLayers = textures
                   selectedTexture = textures |> IndexList.tryFirst
               }
           | None ->
               s
        )

    /// appends surfaces to existing surfaces
    let import' (runtime : IRuntime) (signature: IFramebufferSignature)(surfaces : IndexList<Surface>) (model : Model) =
            
        //handle semantic surfaces
        let surfaces = 
            surfaces 
            |> addLegacyTrafos
            |> addSurfaceAttributes
            |> IndexList.map( fun x -> { x with colorCorrection = Init.initColorCorrection}) 
              
        let existingSurfaces = 
            model.scene.surfacesModel.surfaces.flat
            |> Leaf.toSurfaces
            |> HashMap.toList 
            |> List.map snd 
            |> IndexList.ofList

        let sChildren = 
            surfaces 
            |> IndexList.map Leaf.Surfaces

        let m = 
            model.scene.surfacesModel.surfaces
            |> GroupsApp.addLeaves model.scene.surfacesModel.surfaces.activeGroup.path sChildren 
            |> (flip <| Optic.set (_surfaceModelLens >-> SurfaceModel.surfaces_)) model
        
        let surfaceMap = 
            (m.scene.surfacesModel.surfaces.flat |> Leaf.toSurfaces)

        let allSurfaces = existingSurfaces |> IndexList.append surfaces //????
        //handle sg surfaces
        let m = 
            allSurfaces
            |> IndexList.filter (fun s -> s.surfaceType = SurfaceType.SurfaceOPC)
            |> Sg.createSgSurfaces runtime signature
            |> HashMap.union m.scene.surfacesModel.sgSurfaces
            |> Files.expandLazyKdTreePaths m.scene.scenePath surfaceMap
            |> (flip <| Optic.set (_surfaceModelLens >-> SurfaceModel.sgSurfaces_)) m                                               
         
        m.scene.surfacesModel 
        |> SurfaceModel.triggerSgGrouping 
        |> (flip <| Optic.set _surfaceModelLens) m

    let importObj (runtime : IRuntime) (signature: IFramebufferSignature)(surfaces : IndexList<Surface>) (m : Model) =
      
        let existingSurfaces = 
            m.scene.surfacesModel.surfaces.flat
            |> Leaf.toSurfaces
            |> HashMap.toList 
            |> List.map snd 
            |> IndexList.ofList

        let allSurfaces = existingSurfaces |> IndexList.append surfaces //????
        let sChildren = surfaces |> IndexList.map Leaf.Surfaces

        let m = 
            m.scene.surfacesModel.surfaces 
            |> GroupsApp.addLeaves m.scene.surfacesModel.surfaces.activeGroup.path sChildren 
            |> (flip <| Optic.set (_surfaceModelLens >-> SurfaceModel.surfaces_)) m

        //handle sg surfaces
        let m = 
            allSurfaces
            |> IndexList.filter (fun s -> s.surfaceType = SurfaceType.SurfaceOBJ)
            |> SurfaceUtils.ObjectFiles.createSgObjects runtime signature
            |> HashMap.union m.scene.surfacesModel.sgSurfaces
            |> (flip <| Optic.set (_surfaceModelLens >-> SurfaceModel.sgSurfaces_)) m
                    
        m.scene.surfacesModel 
          |> SurfaceModel.triggerSgGrouping 
          |> (flip <| Optic.set _surfaceModelLens) m
             
    let prepareSurfaceModel 
        (runtime   : IRuntime) 
        (signature : IFramebufferSignature) 
        (scenePath : option<string>) 
        (model     : SurfaceModel) : SurfaceModel =
        
        let surfaces = model.surfaces.flat |> Leaf.toSurfaces 

        let surfacesList =
            surfaces
            |> HashMap.toList 
            |> List.map snd 
            |> IndexList.ofList

        let opcSurfs = 
            surfacesList 
            |> IndexList.filter ( fun x -> x.surfaceType = SurfaceType.SurfaceOPC)

        let sgSurfaces = 
          Sg.createSgSurfaces runtime signature opcSurfs |> Files.expandLazyKdTreePaths scenePath surfaces        

        let objSurfs = 
            surfacesList 
            |> IndexList.filter ( fun x -> x.surfaceType = SurfaceType.SurfaceOBJ)

        let sgSurfaceObj = 
            SurfaceUtils.ObjectFiles.createSgObjects runtime signature objSurfs //|> Files.expandLazyKdTreePaths scenePath surfaces      
            
        let sgs = sgSurfaces |> HashMap.union sgSurfaceObj

        model           
        |> SurfaceModel.withSgSurfaces sgs
        |> SurfaceModel.triggerSgGrouping    
          
    let setFrustum (m:Model) =
       let near = m.scene.config.nearPlane.value
       let far = m.scene.config.farPlane.value
       Frustum.perspective 60.0 near far 1.0
           
    let resetControllerState (m : Model) = 
      
        let state = m.navigation.camera
        { 
            state with 
              forward  = false
              backward = false
              left     = false
              right    = false
              isWheel  = false
              zoom     = false
              pan      = false 
              look     = false
              moveVec  = V3d.Zero
        }
        |> (flip <| Optic.set _camera) m

    let loadScene path m runtime signature =
        //try            
        //    //let s = Serialization.loadAs<Scene> path

        let scene = 
            path
            |> Serialization.Chiron.readFromFile 
            |> Json.parse 
            |> Json.deserialize

        let scene = 
            { scene  with scenePath = Some path }
            |> expandRelativePaths
           
        let m = 
            m 
            |> Model.withScene scene
            |> resetControllerState

        let cameraView = m.scene.cameraView

        let m = { m with frustum = setFrustum m } |> Optic.set _cameraView cameraView

        let sModel = 
            m.scene.surfacesModel 
            |> prepareSurfaceModel runtime signature scene.scenePath

        Optic.set _surfaceModelLens sModel m  
 
        //with e ->            
        //    Log.error "Could not load selected scenefile %A. It is either outdated or not a valid scene" path
        //    Log.error "exact error %A" e
        //    m
    
    let loadLastScene runtime signature m =
        match Serialization.fileExists "./recent" with
        | Some path  ->
            try 
                let m = { m with recent = Serialization.loadAs<Recent> path}
                match m.recent.recentScenes |> List.sortByDescending (fun v -> v.writeDate) |> List.tryHead with
                | Some (scenePath) ->
                    try
                        loadScene scenePath.path m runtime signature                       
                    with e ->
                        Log.warn "[Scene] Error parsing last scene: %s. loading empty" scenePath.path
                        Log.error "[Scene] %A" e.Message
                        m
                | None -> 
                    Log.warn "[Scene] No recent scene found"
                    m            
            with e ->
                Log.warn "[Scene] %A. deleting recent." e
                File.Delete path
                m
        | _ -> 
            Log.warn "[Scene] recent scene file does not exist (yet)"
            m

    ///for debugging
    let loadLogBrush (m : Model) =
        match Serialization.fileExists "./logbrush" with
        | Some path ->
            //let logBrush = Serialization.loadAs<option<PRo3D.Correlations.LogDrawingBrush>> path
            //{ m with correlationPlot = { m.correlationPlot with logBrush = logBrush } }
            m
        | None -> m