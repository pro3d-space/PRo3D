namespace PRo3D

open System
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Data.Opc
open Aardvark.UI.Primitives
open Aardvark.Rendering

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.Navigation2

open Chiron

open Aether
open Aether.Operators
open PRo3D.Core.Gis

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

        try Serialization.save "./recent" recent |> ignore with e -> Log.warn "could not save recent: %A" e.Message

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

        
    let _surfaceModelLens    = Model.scene_ >-> Scene.surfacesModel_
    let _referenceSystemLens = Model.scene_ >-> Scene.referenceSystem_
    let _scaleBarsModelLens  = Model.scene_ >-> Scene.scaleBars_
    let _scaleBarsLens       = _scaleBarsModelLens >-> ScaleBarsModel.scaleBars_
    let _sceneObjects        = Model.scene_ >-> Scene.sceneObjectsModel_ 

    let _camera              = Model.navigation_ >-> NavigationModel.camera_
    let _cameraView          = _camera >-> CameraControllerState.view_
    let _flatSurfaces        = Scene.surfacesModel_ >-> SurfaceModel.surfaces_ >-> GroupsModel.flat_
    let _geologicSurfaceLens = Model.scene_ >-> Scene.geologicSurfacesModel_ >-> GeologicSurfacesModel.geologicSurfaces_

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
            let sequencedBookmarks = 
                let basePath = PRo3D.Core.BookmarkUtils.basePathFromScenePath p
                BookmarkUtils.updatePaths basePath m.sequencedBookmarks
            { m with surfacesModel      = { m.surfacesModel with surfaces = sm }
                     sequencedBookmarks = sequencedBookmarks
            }
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
           let loadOpcX (path : string) =
               let layers = SurfaceUtils.SurfaceAttributes.read path
               let textures = layers |> SurfaceProperties.getTextures
               
               { s with 
                   scalarLayers  = layers |> SurfaceProperties.getScalarsHmap //SurfaceProperties.getScalars
                   textureLayers = textures
                   primaryTexture = textures |> IndexList.tryFirst
                   opcxPath = Some path
               }
           match Serialization.fileExists p with
           | Some path->        
               loadOpcX path
           | None ->
               match Directory.EnumerateFiles(s.importPath, "*.opcx") |> Seq.toList with
               | [singleOpcX] -> loadOpcX singleOpcX
               | _ -> s
        )

    let addGeologicSurfaces (m:Model) = 
        m.scene.geologicSurfacesModel.geologicSurfaces
        |> HashMap.map( fun id surf -> 
                let triangles = GeologicSurfacesUtils.getTrianglesForMesh 
                                    surf.points1 
                                    surf.points2    
                                    surf.color.c 
                                    surf.transparency.value
                { surf with sgGeoSurface = triangles})
        |> (flip <| Optic.set _geologicSurfaceLens) m

    /// appends surfaces to existing surfaces
    let import' (runtime : IRuntime) (signature: IFramebufferSignature)(surfaces : IndexList<Surface>) (model : Model) =
            
        //handle semantic surfaces
        let surfaces = 
            surfaces 
            |> addLegacyTrafos
            |> addSurfaceAttributes
            |> IndexList.map( fun x -> { x with colorCorrection = Init.initColorCorrection}) 
            |> IndexList.map( fun x -> { x with radiometry = Init.initRadiometry})
              
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

    let importObj (loaderType : MeshLoaderType) (surfaces : IndexList<Surface>) (m : Model) =
      
        let leafs = surfaces |> IndexList.map Leaf.Surfaces

        let m = 
            m.scene.surfacesModel.surfaces 
            |> GroupsApp.addLeaves m.scene.surfacesModel.surfaces.activeGroup.path leafs 
            |> (flip <| Optic.set (_surfaceModelLens >-> SurfaceModel.surfaces_)) m

        let loadedSgSurfaces =
            match loaderType with
            | MeshLoaderType.Assimp ->
                SurfaceUtils.ObjectFiles.AssimpLoader.createSgObjects surfaces
            | MeshLoaderType.GlTf -> 
                failwith "not implemented"
            | MeshLoaderType.Wavefront -> 
                SurfaceUtils.ObjectFiles.CustomWavefrontLoader.createSgObjectsWavefront surfaces
            | _ -> 
                SurfaceUtils.ObjectFiles.AssimpLoader.createSgObjects surfaces 

        let m = 
            HashMap.union m.scene.surfacesModel.sgSurfaces loadedSgSurfaces
            |> (flip <| Optic.set (_surfaceModelLens >-> SurfaceModel.sgSurfaces_)) m
                    
        m.scene.surfacesModel 
          |> SurfaceModel.triggerSgGrouping 
          |> (flip <| Optic.set _surfaceModelLens) m


    let importSceneObj (sceneObjs : IndexList<SceneObject>) (m : Model) =
        
        let test =
            sceneObjs |> IndexList.toList |> List.map(fun x -> (x.guid,x))|> HashMap.ofList
        let m = 
            m.scene.sceneObjectsModel.sceneObjects 
            |> HashMap.union test
            |> (flip <| Optic.set (_sceneObjects >-> SceneObjectsModel.sceneObjects_)) m
    
        let m = 
            sceneObjs
            |> SceneObjectsUtils.createSgSceneObjects 
            |> HashMap.union m.scene.sceneObjectsModel.sgSceneObjects
            |> (flip <| Optic.set (_sceneObjects >-> SceneObjectsModel.sgSceneObjects_)) m
                      
        m
             
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

        // TODO hs: should how to handle multiple loaders here?
        let objSurfs = 
            surfacesList 
            |> IndexList.filter ( fun x -> x.surfaceType = SurfaceType.Mesh)

        let sgSurfaceObj = 
            SurfaceUtils.ObjectFiles.CustomWavefrontLoader.createSgObjectsWavefront objSurfs //|> Files.expandLazyKdTreePaths scenePath surfaces      
            
        let sgs = sgSurfaces |> HashMap.union sgSurfaceObj

        model           
        |> SurfaceModel.withSgSurfaces sgs
        |> SurfaceModel.triggerSgGrouping    

    let addScaleBarSegments (m:Model) = 
        m.scene.scaleBars.scaleBars
        |> HashMap.map( fun id sb -> 
                let segments = ScaleBarUtils.updateSegments sb m.scene.referenceSystem.planet
                { sb with scSegments = segments})
        |> (flip <| Optic.set _scaleBarsLens) m

    let loadSceneSpiceKernel (m : Model) =
        let gisApp = GisApp.loadSpiceKernel' m.scene.gisApp
        {m with scene = {m.scene with gisApp = gisApp}}

    let prepareSceneObjectsModel
        (model     : SceneObjectsModel) : SceneObjectsModel =

        let sOList =
            model.sceneObjects
            |> HashMap.toList 
            |> List.map snd 
            |> IndexList.ofList

        let sgSurfaces = 
             SceneObjectsUtils.createSgSceneObjects sOList

        { model with sgSceneObjects = sgSurfaces }

    let setFrustum (m:Model) =
        FrustumUtils.calculateFrustum' 
            m.scene.config.frustumModel.focal.value
            m.scene.config.nearPlane.value
            m.scene.config.farPlane.value
            m.aspect
           
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

    let updateNavigation (m : Model) =
        let navigation' = 
            { 
                m.navigation with 
                    camera          = { m.navigation.camera with view = m.scene.cameraView }
                    exploreCenter   = m.scene.exploreCenter;
                    navigationMode  = m.scene.navigationMode 
            }
        { m with navigation = navigation' }
     
    let updateCameraUp (m: Model) =
        let cam = m.navigation.camera
        let view' = 
            CameraView.lookAt 
                cam.view.Location 
                (cam.view.Location + cam.view.Forward) 
                m.scene.referenceSystem.up.value

        let cam' = { cam with view = view' }
        Optic.set _camera cam' m


    let private applyScene (scene : Scene) (m : Model) (runtime : IRuntime) (signature : IFramebufferSignature)=
        let m = 
            m 
            |> Model.withScene scene
            |> resetControllerState
            |> updateNavigation


        let m = { m with frustum = setFrustum m } 


        let surfaceModel = 
            m.scene.surfacesModel 
            |> prepareSurfaceModel runtime signature scene.scenePath

        let m = m |> Optic.set _surfaceModelLens surfaceModel

        let fullBoundingBox = 
            surfaceModel.sgSurfaces             
            |> HashMap.toSeq 
            |> Seq.map(fun (_,sgSurface) -> sgSurface.globalBB) 
            |> Box3d        

        let centerPoint = 
            if (fullBoundingBox.IsEmpty || fullBoundingBox.IsInvalid) then V3d.Zero else fullBoundingBox.Center

        ////inferring coordinate system on scene load
        //let suggestedSystem = 
        //    Planet.suggestedSystem centerPoint m.scene.referenceSystem.planet
        
        //let (suggestedReferenceSystem,_) = 
        //    suggestedSystem
        //    |> SetPlanet
        //    |> ReferenceSystemApp.update 
        //        m.scene.config 
        //        LenseConfigs.referenceSystemConfig 
        //        m.scene.referenceSystem
                
        let m = 
            m
            |> Optic.set _referenceSystemLens m.scene.referenceSystem //suggestedReferenceSystem
            |> updateCameraUp

        // add sg scene objects
        let sOModel = 
            m.scene.sceneObjectsModel 
            |> prepareSceneObjectsModel 

        Optic.set _sceneObjects sOModel m  

    let loadSceneFromJson (jsonScene : string) (m : Model) (runtime : IRuntime) (signature : IFramebufferSignature) =
        let scene : Scene = 
            jsonScene
            |> Json.parse 
            |> Json.deserialize

        applyScene scene m runtime signature

    let loadSceneFromFile path m runtime signature =

        let scene = 
            path
            |> Serialization.Chiron.readFromFile 
            |> Json.parse 
            |> Json.deserialize

        let scene = 
            { scene  with scenePath = Some path }
            |> expandRelativePaths
           
        applyScene scene m runtime signature

    
    let loadLastScene runtime signature m =
        match Serialization.fileExists "./recent" with
        | Some path  ->
            try 
                let m = { m with recent = Serialization.loadAs<Recent> path}
                match m.recent.recentScenes |> List.sortByDescending (fun v -> v.writeDate) |> List.tryHead with
                | Some (scenePath) ->
                    try
                        loadSceneFromFile scenePath.path m runtime signature
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
