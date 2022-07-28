namespace PRo3D

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

module ViewerLenses =
    // surfaces
    let _surfacesModel   = Model.scene_  >-> Scene.surfacesModel_
    let _sgSurfaces      = _surfacesModel  >-> SurfaceModel.sgSurfaces_
    let _selectedSurface = _surfacesModel  >-> SurfaceModel.surfaces_  >-> GroupsModel.singleSelectLeaf_
       
    // navigation
    let _navigation = Model.navigation_
    let _camera     = _navigation >-> NavigationModel.camera_
    let _view       = _camera >-> CameraControllerState.view_

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
       
    let _sceneState : ((Model -> SceneState) * (SceneState -> Model -> Model)) =
        (fun m -> 
            {
                stateAnnoatations      = m.drawing.annotations
                stateSurfaces          = m.scene.surfacesModel.surfaces
                stateSceneObjects      = m.scene.sceneObjectsModel
                stateScaleBars         = m.scene.scaleBars
                stateGeologicSurfaces  = m.scene.geologicSurfacesModel
                stateConfig            = m.scene.config
                stateReferenceSystem   = m.scene.referenceSystem
            }
        ), 
        (fun state m ->
            let scaleBars = 
                let inline update (bar : ScaleBar) = 
                    let current = HashMap.tryFind bar.guid m.scene.scaleBars.scaleBars
                    match current with
                    | Some current ->
                        { current with scSegments = current.scSegments}
                    | None ->
                        Log.line "[Viewer] Scale bar %s not present in current scene state." bar.name
                        bar
                let updated = 
                    state.stateScaleBars.scaleBars
                                |> HashMap.map (fun id bar -> update bar)
                {state.stateScaleBars with scaleBars = updated}

            {m with
                drawing = {m.drawing with annotations = state.stateAnnoatations}
                scene   = 
                    {m.scene with
                        surfacesModel           = {m.scene.surfacesModel with surfaces = state.stateSurfaces}
                        sceneObjectsModel       = state.stateSceneObjects
                        scaleBars               = scaleBars
                        geologicSurfacesModel   = state.stateGeologicSurfaces
                        config                  = state.stateConfig
                        referenceSystem         = state.stateReferenceSystem
                    }
            }
        )

    let _savedTimeSteps =
        Model.scene_ >-> Scene.sequencedBookmarks_ >-> SequencedBookmarks.savedTimeSteps_  

    let _lastSavedBookmark =
        Model.scene_ >-> Scene.sequencedBookmarks_ >-> SequencedBookmarks.lastSavedBookmark_  

    let _selectedBookmark =
       Model.scene_ >-> Scene.sequencedBookmarks_ >-> SequencedBookmarks.selectedBookmark_  
    
    /// for use with animations, getter returns selected bookmark or new bookmark
    /// setter applies bookmark state to model
    let _bookmark : ((Model -> SequencedBookmark) * (SequencedBookmark -> Model -> Model)) =
        (fun (m : Model) -> 
            Log.warn "[Viewer] Getting selected bookmark or new bookmark."
            let sel = BookmarkUtils.selected m.scene.sequencedBookmarks
            match sel with
            | Some sel -> sel
            | None -> 
                SequencedBookmark.init 
                      (Bookmarks.getNewBookmark (Optic.get _view m)
                                                m.scene.navigationMode
                                                m.scene.exploreCenter
                                                m.scene.bookmarks.flat.Count)
        ),
        (fun sb m ->
            let m = Optic.set _view sb.cameraView m
            let m = 
                match sb.sceneState with
                | Some state ->
                    Optic.set _sceneState state m
                | None -> m
            if m.scene.sequencedBookmarks.isRecording then
                let lastBookmark = Optic.get _lastSavedBookmark m
                let addNewTimeStep newStep steps = steps@[newStep]
                let index = (Optic.get _savedTimeSteps m).Length
                let filename = sprintf "%06i_%s" index sb.name
                let addBookmarkStep () =
                    let newStep = {
                        filename = filename
                        content  = AnimationTimeStepContent.Bookmark sb}
                    (Optic.map _savedTimeSteps (addNewTimeStep newStep) m)
                        |> (Optic.set _lastSavedBookmark (Some sb.bookmark.key))
                match lastBookmark with
                | Some key ->
                    if key = sb.key then
                        let newStep = {
                            filename = filename
                            content  = AnimationTimeStepContent.Camera sb.cameraView}
                        (Optic.map _savedTimeSteps (addNewTimeStep newStep) m)
                    else
                        addBookmarkStep ()
                | None -> 
                    addBookmarkStep ()
            else
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
        }



