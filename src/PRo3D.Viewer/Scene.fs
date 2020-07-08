namespace PRo3D

open System
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.SceneGraph.Opc
open Aardvark.UI.Primitives

open PRo3D.Base
open PRo3D.Surfaces
open PRo3D.Viewer
open PRo3D.Surfaces.Surface
open PRo3D.Groups
open PRo3D.Navigation2

open Chiron

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Model =
    open PRo3D.Base

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
module Scene =        
    
    module Minerva =
        let defaultDumpFile =  @".\MinervaData\dump.csv"
        let defaultCacheFile = @".\MinervaData\dump.cache"

        
    let _surfaceModelLens = Model.Lens.scene |. Scene.Lens.surfacesModel
    let _flatSurfaces     = Scene.Lens.surfacesModel |. SurfaceModel.Lens.surfaces |. GroupsModel.Lens.flat
    let _camera           = Model.Lens.navigation |. NavigationModel.Lens.camera
    let _cameraView       = _camera |. CameraControllerState.Lens.view


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
                   selectedTexture = textures |> IndexList.tryHead
               }
           | None ->
               s
        )

    /// appends surfaces to existing surfaces
    let import' (runtime : IRuntime) (signature: IFramebufferSignature)(surfaces : IndexList<Surface>) (m : Model) =
            
        //handle semantic surfaces
        let surfaces = 
            surfaces 
            |> addLegacyTrafos
            |> addSurfaceAttributes
            |> IndexList.map( fun x -> { x with colorCorrection = PRo3D.Surfaces.Init.initColorCorrection}) 
              
        let existingSurfaces = 
            m.scene.surfacesModel.surfaces.flat
            |> Leaf.toSurfaces
            |> HashMap.toList 
            |> List.map snd 
            |> IndexList.ofList

        let sChildren = 
            surfaces 
            |> IndexList.map Leaf.Surfaces

        let m = 
            m.scene.surfacesModel.surfaces
            |> GroupsApp.addLeaves m.scene.surfacesModel.surfaces.activeGroup.path sChildren 
            |> Lenses.set' (_surfaceModelLens |. SurfaceModel.Lens.surfaces) m
        
        let surfaceMap = 
            (m.scene.surfacesModel.surfaces.flat |> Leaf.toSurfaces)

        let allSurfaces = existingSurfaces |> IndexList.append' surfaces //????
        //handle sg surfaces
        let m = 
            allSurfaces
            |> IndexList.filter (fun s -> s.surfaceType = SurfaceType.SurfaceOPC)
            |> Sg.createSgSurfaces runtime signature
            |> HashMap.union m.scene.surfacesModel.sgSurfaces
            |> Files.expandLazyKdTreePaths m.scene.scenePath surfaceMap
            |> Lenses.set' (_surfaceModelLens |. SurfaceModel.Lens.sgSurfaces) m                                               
         
        m.scene.surfacesModel 
        |> SurfaceModel.triggerSgGrouping 
        |> Lenses.set' _surfaceModelLens m

    let importObj (runtime : IRuntime) (signature: IFramebufferSignature)(surfaces : IndexList<Surface>) (m : Model) =
      
        let existingSurfaces = 
            m.scene.surfacesModel.surfaces.flat
            |> Leaf.toSurfaces
            |> HashMap.toList 
            |> List.map snd 
            |> IndexList.ofList

        let allSurfaces = existingSurfaces |> IndexList.append' surfaces //????
        let sChildren = surfaces |> IndexList.map Leaf.Surfaces

        let m = 
            m.scene.surfacesModel.surfaces 
            |> GroupsApp.addLeaves m.scene.surfacesModel.surfaces.activeGroup.path sChildren 
            |> Lenses.set' (_surfaceModelLens |. SurfaceModel.Lens.surfaces) m

        //handle sg surfaces
        let m = 
            allSurfaces
            |> IndexList.filter (fun s -> s.surfaceType = SurfaceType.SurfaceOBJ)
            |> SurfaceUtils.ObjectFiles.createSgObjects runtime signature
            |> HashMap.union m.scene.surfacesModel.sgSurfaces
            |> Lenses.set' (_surfaceModelLens |. SurfaceModel.Lens.sgSurfaces) m
                    
        m.scene.surfacesModel 
          |> SurfaceModel.triggerSgGrouping 
          |> Lenses.set' _surfaceModelLens m
             
    let prepareSurfaceModel (runtime : IRuntime) (signature: IFramebufferSignature) (scenePath : option<string>) (model : SurfaceModel) : SurfaceModel =
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
        |> Lenses.set' _camera m

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

            let m = { m with frustum = setFrustum m } |> Lenses.set _cameraView cameraView

            let sModel = 
                m.scene.surfacesModel 
                  |> prepareSurfaceModel runtime signature scene.scenePath

            _surfaceModelLens.Set(m, sModel)        
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
            let logBrush = Serialization.loadAs<option<PRo3D.Correlations.LogDrawingBrush>> path
            { m with correlationPlot = { m.correlationPlot with logBrush = logBrush } }
        | None -> m
