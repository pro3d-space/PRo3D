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
        let inline haveSameKeys (a : HashMap<'a, 'b>) (b : HashMap<'a, 'c>) =
            if a.Count <> b.Count then false
            else
                a.ToKeyList () |> List.sort = (a.ToKeyList () |> List.sort)

        (fun m -> 
            {
                isValid                = true
                timestamp              = System.DateTime.Now
                stateAnnoatations      = m.drawing.annotations
                stateSurfaces          = m.scene.surfacesModel.surfaces
                stateSceneObjects      = m.scene.sceneObjectsModel
                stateScaleBars         = m.scene.scaleBars
                stateGeologicSurfaces  = m.scene.geologicSurfacesModel
                stateConfig            = m.scene.config
                stateReferenceSystem   = m.scene.referenceSystem
                stateTraverses         = Some m.scene.traverses
            }
        ), 
        (fun state m ->
            let state = // check surfaces
                if haveSameKeys state.stateSurfaces.flat
                                m.scene.surfacesModel.sgSurfaces then state
                else
                    Log.warn "[ViewerLenses] Surfaces have been added or removed making this scene state invalid. 
                                Not applying surfaces for this scene state."
                    {state with stateSurfaces = m.scene.surfacesModel.surfaces}

            let scaleBars = // check scale bars; using old segments for performance reasons
                if haveSameKeys state.stateScaleBars.scaleBars
                                m.scene.scaleBars.scaleBars then 
                    let inline update (newBar : ScaleBar) = 
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
                        traverses               = traverses
                    }
            }
        )

    let _savedTimeSteps =
        Model.scene_ >-> Scene.sequencedBookmarks_ >-> SequencedBookmarks.savedTimeSteps_  

    let _lastSavedBookmark =
        Model.scene_ >-> Scene.sequencedBookmarks_ >-> SequencedBookmarks.lastSavedBookmark_  

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
            | Some sel -> sel
            | None -> 
                SequencedBookmark.init 
                      (Bookmarks.getNewBookmark (Optic.get _view m)
                                                m.scene.navigationMode
                                                m.scene.exploreCenter
                                                m.scene.bookmarks.flat.Count)
        ),
        (fun sb m ->
            // update camera to bookmark's camera
            let m = Optic.set _view sb.cameraView m
            let lastBookmark = Optic.get _lastSavedBookmark m

            // update the scene state if the bookmark contains one
            let inline updateSceneState sb m =
                match sb.sceneState with
                | Some state ->
                    Optic.set _sceneState state m
                | None -> m

            // check whether animation is being recorded, and whether this frame constitutes a change to a new bookmark
            match lastBookmark with
            | Some key when key = sb.key ->
                // not recording, same bookmark
                m // nothing to do except update the view which we did above
            | Some key when key <> sb.key ->
                // new bookmark, so we need to update the scene state
                updateSceneState sb m
                |> Optic.set _lastSavedBookmark (Some sb.key)
            | None ->
                updateSceneState sb m
                |> Optic.set _lastSavedBookmark (Some sb.key)
            | _ -> 
                Log.line "[ViewerLenses] Impossible case."
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


