namespace PRo3D.Core

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open Aardvark.Rendering
open Aardvark.UI.Anewmation
open Aardvark.UI.Anewmation.AnimationSplinePrimitives.Animation
open Aardvark.UI.Anewmation.AnimationPrimitives.Animation
open Aardvark.UI.Anewmation.AnimationCameraPrimitives.Animation

open PRo3D.Base
open PRo3D.Core.SequencedBookmarks
open PRo3D.Core.BookmarkUtils

open Aether
open Aether.Operators
open Chiron

/// Animations for sequenced bookmarks
module BookmarkAnimations =

    module AnimationSlot =
        let private getName (slot : string) (entity : V2i) =
            Sym.ofString <| sprintf "%A/%s" entity slot

        let camera = Sym.ofString "camera"
        let caption = Sym.ofString "caption"
        //let appearance = getName "appearance"

    module Primitives =
        let frustum_ = ViewConfigModel.frustumModel_ >-> FrustumModel.frustum_
        let focal_   = ViewConfigModel.frustumModel_ >-> FrustumModel.focal_ >-> NumericInput.value_

        let interpVcm (src : ViewConfigModel) (dst : ViewConfigModel)
                : IAnimation<'Model, ViewConfigModel> =
            //let animFocal = Primitives.lerp src.frustumModel.focal.value src.frustumModel.focal.value
            let animFocal = Animation.create (lerp src.frustumModel.focal.value dst.frustumModel.focal.value)
                            |> Animation.seconds 1
            animFocal
            |> Animation.map (fun focal -> 
                                let newFrustum = 
                                    FrustumUtils.calculateFrustum
                                        focal
                                        dst.nearPlane.value 
                                        dst.farPlane.value
                                        
                                dst 
                                |> Optic.set frustum_ newFrustum 
                                |> Optic.set focal_   focal
                                )


        /// Creates an animation that interpolates between two bookmarks
        let interpolateBm (settings : AnimationSettings) 
                          (setSeqbookmark : Lens<'a, SequencedBookmark>)
                          (src : SequencedBookmarkModel) (dst : SequencedBookmarkModel)
                          = //IAnimation<'Model, SequencedBookmark> =

            let pause = 
                if src.delay.value > 0.0 then
                    let dummyAnimation = Animation.create (fun _ -> src.cameraView)
                   
                    // TODO RNO add other interpolations
                    [
                        dummyAnimation
                        |> Animation.map (fun view -> src)
                        |> Animation.seconds src.delay.value
                    ]
                else 
                    []
            
            let toNext = 
                let animCam = Animation.Camera.interpolate src.bookmark.cameraView dst.bookmark.cameraView
                              |> Animation.map (fun view -> 
                                    {dst with bookmark = {dst.bookmark with cameraView = view}})
                match src.sceneState, dst.sceneState with
                | Some srcState, Some dstState ->
                    let _view = SequencedBookmarkModel._cameraView

                    let animFocal =
                        (interpVcm srcState.stateConfig dstState.stateConfig) 
                        |> Animation.map (fun vcm -> 
                            Optic.set SequencedBookmarkModel._stateConfig vcm dst) 
                    
                    Animation.map2 (fun c f -> f |> Optic.set _view (Optic.get _view c)) 
                                    animCam animFocal
                | _ -> 
                    animCam
                

            let toNext =
                if settings.useGlobalAnimation then
                    toNext
                else 
                    toNext
                    |> Animation.seconds dst.duration.value

            let toNext = 
                if settings.useEasing then
                    toNext
                        |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
                else 
                    toNext
            pause@[toNext]

        let inline slerpBm (src : SequencedBookmarkModel) (dst : SequencedBookmarkModel) 
                            : IAnimation<'Model, SequencedBookmarkModel> =
            let slerped = Primitives.slerp (CameraView.orientation src.bookmark.cameraView)
                                           (CameraView.orientation dst.bookmark.cameraView)
            slerped
            |> Animation.map (fun ( x : Rot3d)  -> 
                {dst with bookmark = 
                            {dst.bookmark with cameraView = CameraView.withOrientation x dst.cameraView}}) 

    let calculateCurrentFps (m                : SequencedBookmarks) =
        let nr = m.savedTimeSteps.Length
        if nr > 1 then
            let fps = 
                match m.lastStart with
                | Some lastStart ->
                    let now = System.DateTime.Now.TimeOfDay
                    let duration = (now - lastStart).TotalSeconds
                    let fps = (float nr) / duration
                    Some (int fps)
                | None -> 
                    None
            {m with currentFps = fps}
        else m

    /// <summary>
    /// Creates an array of animations that smoothly interpolate between the given bookmarks' camera views.
    /// The animations are scaled according to the distance between the camera view locations. Coinciding camera views are ignored.
    /// The accuracy of the parameterization depends on the given epsilon, where values closer to zero result in higher accuracy.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the sequence is empty.</exception>
    let smoothBookmarkPath (epsilon : float) (bookmarks : SequencedBookmarkModel seq) 
                : IAnimation<'Model, SequencedBookmarkModel>[] =
        let bookmarks = Array.ofSeq bookmarks

        if Seq.isEmpty bookmarks then
            raise <| System.ArgumentException("Camera path cannot be empty")

        let sky = bookmarks.[0].cameraView.Sky

        let ifSameRemove ((iA, a), (iB, b)) = 
            if a = b then 
                Log.warn "[Sequenced Bookmarks] Two Bookmarks have the same location! One of them will be ignored.
                         Please uncheck \"Global animation\" to use bookmarks with identical locations." 
                (iA, None) 
            else 
                (iA, Some a)

        // RNO TODO could also insert an animation only using orientation animation to solve this problem
        let bookmarks =
            bookmarks // we need to filter bookmarks because identical locations are ignored by Splines.catmullRom
            |> Array.append [| (Array.last bookmarks) |]
            |> Array.map (fun bm -> bm.cameraView |> CameraView.location)
            |> Array.indexed
            |> Array.pairwise 
            |> Array.map ifSameRemove
            |> Array.map (fun (i, x) -> if x.IsSome then Some bookmarks.[i] else None)
            |> Array.filter Option.isSome
            |> Array.map Option.get
                
        let locations =
            bookmarks
            |> Array.map (fun bm -> bm.cameraView |> CameraView.location)
            |> Primitives.smoothPath' Vec.distance epsilon

        let orientations =
            bookmarks
            |> Primitives.path' Primitives.slerpBm (fun _ _ -> 1.0)

        let animation = 
            (locations, orientations)
            ||> Array.map2 (fun l o ->
                let o = o |> Animation.duration l.Duration
                (l, o) ||> Animation.map2 (fun l o -> 
                                            let cameraView = CameraView.orient l (CameraView.orientation o.bookmark.cameraView) sky
                                            {o with bookmark = 
                                                        {o.bookmark with cameraView = cameraView}}
                                          ))
        
        animation

    //let restoreState name bm outerModel =
    //    match m.savedSceneState with
    //    | Some state ->
    //        Log.line "[Animation] Restoring scene state."
    //        Optic.set lenses.sceneState_ state outerModel
    //    | None ->
    //        Log.line "[Animation] No scene state to restore."
    //        outerModel
            

    let private addGlobalAttributes (m : SequencedBookmarks)
                                    (lenses : BookmarkLenses<'a>) 
                                    (outerModel : 'a)
                                    (animation : IAnimation<'a,SequencedBookmarkModel>) =

        let animation = 
            match m.animationSettings.useGlobalAnimation with
            | true ->
                animation
                    |> Animation.seconds m.animationSettings.globalDuration.value
            | false ->
                animation

        let animation =
            if m.animationSettings.useEasing && m.animationSettings.useGlobalAnimation then
                animation
                |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
            else
                animation

        let animation =
            animation
                //|> Animation.onFinalize restoreState
                //|> Animation.onStop restoreState
                |> Animation.onProgress (fun name value model ->
                    Optic.set lenses.setModel_ 
                                (SequencedBookmark.LoadedBookmark value) model 
                    )

        let animation =
            match m.animationSettings.loopMode with
            | AnimationLoopMode.Repeat ->
                animation
                    |> Animation.loop LoopMode.Repeat
            | AnimationLoopMode.Mirror ->
                animation
                    |> Animation.loop LoopMode.Mirror
            | AnimationLoopMode.NoLoop ->
                animation
            | _ ->
                animation

        let calcFps slot bm outerModel =
            let bms = calculateCurrentFps (Optic.get lenses.sequencedBookmarks_ outerModel)
            outerModel
            |> Optic.set lenses.sequencedBookmarks_ bms

        let setStartTime slot bm outerModel =
            Optic.set lenses.lastStart_ (Some System.DateTime.Now.TimeOfDay) outerModel

        let animation =
            match m.isRecording with
            | true ->
                animation
                |> Animation.onStart setStartTime
                |> Animation.onResume setStartTime
                |> Animation.onStop calcFps
                |> Animation.onPause calcFps
                |> Animation.onFinalize calcFps
            | false -> 
                animation
        animation

    //let pathAllBookmarks (m : SequencedBookmarks)
    //                     (lenses : BookmarkLenses<'a>)
    //                     (outerModel      : 'a) =
    //    let bookmarks = orderedBookmarks m
    //    let animation =
    //        bookmarks
    //        |> List.pairwise
    //        |> List.map (fun (a,b) -> Primitives.interpolateBm a b 
    //                                  |> addLocalAttributes m lenses b outerModel)
    //        |> Animation.path
    //    let animation = 
    //        animation
    //        |> addGlobalAttributes m lenses outerModel
                
    //    let outerModel =
    //        outerModel 
    //        |> Animator.createAndStart AnimationSlot.camera animation
    //    let m = {m with originalSceneState = Some (Optic.get lenses.sceneState_ outerModel)}
    //    outerModel, m

    let smoothPathAllBookmarks (m : SequencedBookmarks)
                               (lenses : BookmarkLenses<'a>)
                               (outerModel      : 'a) =
        let bookmarks = orderedLoadedBookmarks m
        let animation =
            bookmarks
            |> smoothBookmarkPath m.animationSettings.smoothingFactor.value
            |> List.ofArray
            |> List.map (Animation.onStart (fun name x m -> 
                                                    Log.line "selected bm %s" x.name
                                                    Optic.set lenses.selectedBookmark_ (Some x.key) m
                                    ))
                |> Animation.path
        let animation = 
            animation
            |> addGlobalAttributes m lenses outerModel
                
        let outerModel =
            outerModel 
            |> Animator.createAndStart AnimationSlot.camera animation
        //let m = {m with savedSceneState = Some (Optic.get lenses.sceneState_ outerModel)}
        outerModel, m
        
    let pathWithPausing (m : SequencedBookmarks)
                        (lenses : BookmarkLenses<'a>)
                        (outerModel      : 'a) =
        let aspectRatio = m.resolutionX.value / m.resolutionY.value
        let bookmarks = orderedLoadedBookmarks m
        let animations =
            bookmarks
            |> List.pairwise 
            |> List.map (fun (a,b) -> Primitives.interpolateBm m.animationSettings lenses.setModel_ a b)
            |> List.concat
            |> List.map (Animation.onStart (fun name x m -> 
                                                    Log.line "[Bookmark Animation] Selected bookmark %s" x.name
                                                    Optic.set lenses.selectedBookmark_ (Some x.key) m
                                    ))
        
        let animation =
            animations
            |> Animation.path

        let animation = 
            animation
            |> addGlobalAttributes m lenses outerModel
                
        let outerModel =
            outerModel 
            |> Animator.createAndStart AnimationSlot.camera animation
        //let m = {m with savedSceneState = Some (Optic.get lenses.sceneState_ outerModel)}
        outerModel, m

    let cameraOnly (m : SequencedBookmarks)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) =
        let views = 
            orderedLoadedBookmarks m
            |> List.map (fun (bm : SequencedBookmarkModel) -> bm.cameraView)

        let view_ = navigationModel 
                    >-> NavigationModel.camera_
                    >-> CameraControllerState.view_

        let animation = 
                AnimationCameraPrimitives.Animation.Camera.smoothPath
                    m.animationSettings.smoothingFactor.value views
                |> Animation.seconds m.animationSettings.globalDuration.value
                |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
                |> Animation.onProgress (fun name value model ->
                                            Optic.set view_ value model)
        outerModel
        |> Animator.createAndStart AnimationSlot.camera animation

    let toBookmarkFromView (m : SequencedBookmarks)
                           (outerModel      : 'a) 
                           (lenses : BookmarkLenses<'a>)
                           (f : SequencedBookmarks -> option<SequencedBookmarkModel>) =
        let view_ = lenses.navigationModel_ 
                    >-> NavigationModel.camera_
                    >-> CameraControllerState.view_
        let currentView = Optic.get view_ outerModel
        match f m with
        | Some next -> 
            let outerModel = 
                let animation = 
                    let animCam =
                         AnimationCameraPrimitives.Animation.Camera.smoothPath
                            m.animationSettings.smoothingFactor.value 
                            [currentView;next.bookmark.cameraView]
                    let animCam = 
                        if m.animationSettings.useEasing then
                            animCam
                            |> Animation.ease (Easing.InOut EasingFunction.Quadratic)        
                        else
                            animCam
                    animCam
                    |> Animation.seconds next.duration.value
                    |> Animation.onProgress (fun name value model ->
                                                Optic.set view_ value model)
                outerModel
                |> Animator.createAndStart AnimationSlot.camera animation
            outerModel, {m with selectedBookmark = Some next.key}
        | _ -> 
            Log.line "[SequencedBookmarks] No bookmark selected."
            outerModel, m

    //let toBookmarkFromSelected (m : SequencedBookmarks)
    //                           (outerModel      : 'a) 
    //                           (lenses : BookmarkLenses<'a>)
    //                           (f : SequencedBookmarks -> option<SequencedBookmark>)=
    //    let view_ = lenses.navigationModel_ 
    //                >-> NavigationModel.camera_
    //                >-> CameraControllerState.view_
    //    match selected m, f m with
    //    | Some selected, Some next -> 
    //        let outerModel = 
    //            let animation = 
    //                let animCam =
    //                     AnimationCameraPrimitives.Animation.Camera.smoothPath
    //                        m.animationSettings.smoothingFactor.value 
    //                        [selected.bookmark.cameraView;next.bookmark.cameraView]
    //                animCam
    //                    //|> Animation.map (fun view -> {selected with bookmark = {next.bookmark with cameraView = view}})
    //                    |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
    //                    |> Animation.seconds next.duration.value
    //                    |> Animation.onProgress (fun name value model ->
    //                        Optic.set view_ value model)
    //            //smoothCameraPath [selected.cameraView;next.cameraView] navigationModel outerModel
    //            outerModel
    //                |> Animator.createAndStart AnimationSlot.camera animation
    //        outerModel, {m with selectedBookmark = Some next.key}
    //    | None ,_ -> 
    //        Log.line "[SequencedBookmarks] No bookmark selected."
    //        outerModel, m
    //    | Some _ , None -> 
    //        Log.line "[SequencedBookmarks] Could not find next bookmark."
    //        outerModel, m
