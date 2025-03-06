﻿namespace PRo3D

open Aardvark.Service

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Primitives
open Aardvark.UI.Animation


open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.SequencedBookmarks
open PRo3D.Core.Drawing
open PRo3D.Navigation2
open PRo3D.Bookmarkings

open PRo3D.Viewer
open Aether
open Aether.Operators
open PRo3D.Core.Gis
open Aardvark.UI.Animation.Deprecated

module ViewerLenses =
    // surfaces
    let _surfacesModel   = Model.scene_  >-> Scene.surfacesModel_
    let _sgSurfaces      = _surfacesModel  >-> SurfaceModel.sgSurfaces_
    let _selectedSurface = _surfacesModel  >-> SurfaceModel.surfaces_  >-> GroupsModel.singleSelectLeaf_
       
    // navigation
    let _navigation = Model.navigation_
    let _camera     = _navigation >-> NavigationModel.camera_
    let _view       = _camera >-> CameraControllerState.view_

    // view config
    let _viewConfigModel = Model.scene_ >-> Scene.config_
    let _frustumModel = _viewConfigModel >-> ViewConfigModel.frustumModel_

    // drawing
    let _drawing         = Model.drawing_
    let _annotations     = _drawing >-> DrawingModel.annotations_
    let _dnsColorLegend  = _drawing >-> DrawingModel.dnsColorLegend_
    let _flat            = _annotations  >-> GroupsModel.flat_
    let _lookUp          = _annotations  >-> GroupsModel.groupsLookup_
    let _groups          = _annotations  >-> GroupsModel.rootGroup_ >-> Node.subNodes_

    // animation  
    let _animation = Model.animations_
    let _animationView = _animation >-> AnimationModel.cam_
    let _animator = Model.animator_

    //footprint
    let _footprint = Model.footPrint_

    // scale bars
    let _scaleBarsModel = Model.scene_  >->  Scene.scaleBars_
    let _scaleBars      = _scaleBarsModel >-> ScaleBarsModel.scaleBars_

    // traverses
    let _traversesModel = Model.scene_  >->  Scene.traverses_
    let _traverses      = _traversesModel >-> TraverseModel.traverses_

    // geologic surfaces
    let _geologicSurfacesModel = Model.scene_ >->  Scene.geologicSurfacesModel_
    let _geologicSurfaces      = _geologicSurfacesModel >-> GeologicSurfacesModel.geologicSurfaces_
        
    // sequenced bookmarks
    let _sequencedBookmarks = Model.scene_ >-> Scene.sequencedBookmarks_ 

    // GIS
    let _gisApp = Model.scene_ >-> Scene.gisApp_
    let _observationInfo = _gisApp >-> GisApp.defaultObservationInfo_

    // scene state for saving the state of the scene with sequenced bookmarks
    let _sceneState : ((Model -> SceneState) * (SceneState -> Model -> Model)) =
        let inline haveSameKeys (a : HashMap<'a, 'b>) (b : HashMap<'a, 'c>) =
            if a.Count <> b.Count then false
            else
                a.ToKeyList () |> List.sort = (b.ToKeyList () |> List.sort)

        (fun m -> 
            {
                version                = SceneState.currentVersion
                isValid                = true
                timestamp              = System.DateTime.Now
                stateAnnoatations      = m.drawing.annotations
                stateSurfaces          = m.scene.surfacesModel.surfaces
                stateSceneObjects      = m.scene.sceneObjectsModel
                stateScaleBars         = m.scene.scaleBars
                stateGeologicSurfaces  = m.scene.geologicSurfacesModel
                stateConfig            = SceneStateViewConfig.fromViewConfigModel 
                                            m.scene.config
                stateReferenceSystem   = SceneStateReferenceSystem.fromReferenceSystem 
                                            m.scene.referenceSystem
                stateTraverses         = Some m.scene.traverses
            }
        ), 
        (fun state m ->
            let state = // check surfaces
                if haveSameKeys state.stateSurfaces.flat
                                m.scene.surfacesModel.sgSurfaces then 
                                state
                else
                    Log.warn "[ViewerLenses] Surfaces have been added or removed making this scene state invalid. 
                                Not applying surfaces for this scene state. Update Scene State for this bookmark
                                or delete and create new bookmark."
                    let newState = {state with stateSurfaces = m.scene.surfacesModel.surfaces}
                    BookmarkUtils.getValidState newState
            let scaleBars = // check scale bars; using old segments for performance reasons
                if haveSameKeys state.stateScaleBars.scaleBars
                                m.scene.scaleBars.scaleBars then 
                    let inline update (newBar : ScaleVisualization) = 
                        let current = HashMap.tryFind newBar.guid m.scene.scaleBars.scaleBars
                        match current with
                        | Some current ->
                            { newBar with scSegments = current.scSegments}
                        | None ->
                            Log.line "[Viewer] Scale bar %s not present in current scene state." newBar.name
                            newBar
                    let updated = 
                        state.stateScaleBars.scaleBars
                        |> HashMap.map (fun id bar -> update bar)
                    {state.stateScaleBars with scaleBars = updated}        
                    
                else
                    Log.warn "[ViewerLenses] Scale Bars have been added or removed making this scene state invalid. 
                                Not applying scale bars for this scene state."
                    m.scene.scaleBars

            let traverses = 
                match state.stateTraverses with
                | Some t -> t
                | None -> m.scene.traverses

            let frustum =
                match m.startupArgs.isBatchRendering with
                | false ->
                    FrustumUtils.calculateFrustum
                        state.stateConfig.frustumModel.focal.value
                        state.stateConfig.nearPlane 
                        state.stateConfig.farPlane
                | true ->
                        state.stateConfig.frustumModel.frustum
            let m = 
                let refSysState = 
                    /// UPDATING REF SYSTEM HERE LEADS TO TRAVERSE CALCULATIONS BERING TRIGGERED, EVEN IF THE REF SYSTEM DOES NOT CHANGE!
                    /// so we check manually if the reference system has changed, and only assign it if there is a change
                    {m.scene.referenceSystem with
                        origin        = state.stateReferenceSystem.origin       
                        isVisible     = state.stateReferenceSystem.isVisible    
                        size          = 
                            {m.scene.referenceSystem.size with 
                                value = state.stateReferenceSystem.size}
                        selectedScale = state.stateReferenceSystem.selectedScale

                        textsize      = ReferenceSystem.text state.stateReferenceSystem.textsize
                        textcolor     = { c = state.stateReferenceSystem.textcolor }
                    }
                if m.scene.referenceSystem <> refSysState then
                    {m with scene   = 
                            {m.scene with
                                    referenceSystem = refSysState
                            }
                    }
                else
                    m

            let m = 
                {m with
                    drawing = {m.drawing with annotations = state.stateAnnoatations}
                    scene   = 
                        {m.scene with
                            surfacesModel           = {m.scene.surfacesModel with surfaces = state.stateSurfaces}
                            sceneObjectsModel       = state.stateSceneObjects
                            scaleBars               = scaleBars
                            geologicSurfacesModel   = state.stateGeologicSurfaces
                            config                  = 
                                {m.scene.config with
                                    nearPlane           = 
                                        {m.scene.config.nearPlane with value = state.stateConfig.nearPlane}
                                    farPlane            = 
                                        {m.scene.config.farPlane with value = state.stateConfig.farPlane}
                                    frustumModel        = state.stateConfig.frustumModel       
                                    arrowLength         = 
                                        {m.scene.config.arrowLength with value = state.stateConfig.arrowLength}
                                    arrowThickness      = 
                                        {m.scene.config.arrowLength with value = state.stateConfig.arrowThickness}
                                    dnsPlaneSize        = 
                                        {m.scene.config.dnsPlaneSize with value = state.stateConfig.dnsPlaneSize}
                                    lodColoring         = state.stateConfig.lodColoring        
                                    drawOrientationCube = state.stateConfig.drawOrientationCube
                                }
                            traverses               = traverses
                        
                        }
                    frustum = frustum
                }

            m
        )

    let _savedTimeSteps =
        Model.scene_ >-> Scene.sequencedBookmarks_ >-> SequencedBookmarks.savedTimeSteps_  

    let _selectedBookmark =
       Model.scene_ >-> Scene.sequencedBookmarks_ >-> SequencedBookmarks.selectedBookmark_  

    let inline appendToList element lst = lst@[element]

    /// for use with animations, getter returns selected bookmark or new bookmark
    /// setter applies bookmark state to model
    let _bookmark : ((Model -> SequencedBookmark) * (SequencedBookmark -> Model -> Model)) =
        (fun (m : Model) -> 
            Log.warn "[Viewer] Getting selected bookmark or new bookmark."
            let sel = BookmarkUtils.selected m.scene.sequencedBookmarks
            match sel with
            | Some sel -> SequencedBookmark.LoadedBookmark sel
            | None -> 
                SequencedBookmarkModel.init 
                      (Bookmarks.getNewBookmark (Optic.get _view m)
                                                m.scene.navigationMode
                                                m.scene.exploreCenter
                                                m.scene.bookmarks.flat.Count)
                    |> SequencedBookmark.LoadedBookmark
        ),
        (fun sb m ->
            let update (sb : SequencedBookmarkModel) = 

                // update the scene state if the bookmark contains one
                let inline updateSceneState sb m =
                    match sb.sceneState with
                    | Some state ->
                        Optic.set _sceneState state m
                    | None -> m
                let m = updateSceneState sb m            

                let m = 
                    match sb.observationInfo with
                    | Some info ->
                        match info.valuesIfComplete with
                        | Some (t, o, r) ->
                            // call spice function and transform surfaces here!
                            Log.line "[debug] call to spice function with 
                                        target: %s observer: %s reference frame:  %s 
                                        time: %s" 
                                     t.Value o.Value r.Value (string info.time.date)
                            let observationInfo =
                                {
                                    target         = Some t
                                    observer       = Some o
                                    time           = {m.scene.gisApp.defaultObservationInfo.time with
                                                            date = info.time.date}
                                    referenceFrame = Some r
                                }
                            let m = Optic.set _observationInfo observationInfo m
                            let c = GisApp.lookAtObserver' observationInfo
                            let m = 
                                match c with 
                                | Some c -> Optic.set _view c m
                                | _ -> m
                            m
                        | None ->
                            m
                    | None ->
                        //update camera to bookmark's camera
                        let m = Optic.set _view sb.cameraView m
                        m
                   
                m

            match sb with
            | SequencedBookmark.LoadedBookmark loaded ->
                update loaded
            | SequencedBookmark.NotYetLoaded notLoaded ->
                match SequencedBookmark.tryLoad sb with
                | Some loaded ->
                    update loaded
                | None ->
                    m
        )

    let bookmarkLenses =
        {
            navigationModel_  = _navigation
            sceneState_       = _sceneState
            setModel_         = _bookmark
            selectedBookmark_ = _selectedBookmark
            sequencedBookmarks_ = Model.scene_ >-> Scene.sequencedBookmarks_
            savedTimeSteps_   = _savedTimeSteps
            lastStart_        = Model.scene_ >-> Scene.sequencedBookmarks_ >-> SequencedBookmarks.lastStart_
            defaultObservationInfo_ = Model.scene_ 
                                      >-> Scene.gisApp_
                                      >-> GisApp.defaultObservationInfo_
        }

    let gisLenses : Gis.GisApp.GisLenses<Model> =
        {
            surfacesModel   = _surfacesModel 
            bookmarks       = _sequencedBookmarks
            scenePath       = Model.scene_ >-> Scene.scenePath_
            navigation      = Model.navigation_
            referenceSystem = Model.scene_ >-> Scene.referenceSystem_
        }


