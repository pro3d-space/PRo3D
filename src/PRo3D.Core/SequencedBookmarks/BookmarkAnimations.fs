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

/// Animations for sequenced bookmarks
module BookmarkAnimations =
    module AnimationSlot =
        let private getName (slot : string) (entity : V2i) =
            Sym.ofString <| sprintf "%A/%s" entity slot

        let camera = Sym.ofString "camera"
        let caption = Sym.ofString "caption"
        //let appearance = getName "appearance"

    module Primitives =
        /// Creates an animation that interpolates between two bookmarks
        let interpolateBm (src : SequencedBookmark) (dst : SequencedBookmark) = //IAnimation<'Model, SequencedBookmark> =
            let pause = 
                let dummyAnimation = Animation.create (fun _ -> dst.cameraView)
                // TODO RNO add other interpolations
                dummyAnimation
                    |> Animation.map (fun view -> {dst with bookmark = {dst.bookmark with cameraView = view}})
                    |> Animation.seconds src.delay.value
            
            let toNext = 
                let animCam = Animation.Camera.interpolate src.bookmark.cameraView dst.bookmark.cameraView
                // TODO RNO add other interpolations
                animCam
                    |> Animation.map (fun view -> {dst with bookmark = {dst.bookmark with cameraView = view}})
                    |> Animation.seconds dst.duration.value
            [pause; toNext]

        let inline slerpBm (src : SequencedBookmark) (dst : SequencedBookmark) : IAnimation<'Model, SequencedBookmark> =
            let slerped = Primitives.slerp (CameraView.orientation src.bookmark.cameraView)
                                           (CameraView.orientation dst.bookmark.cameraView)
            slerped
                |> Animation.map (fun ( x : Rot3d)  -> 
                                        {dst with bookmark = 
                                                     {dst.bookmark with cameraView = CameraView.withOrientation x dst.cameraView}} 
                                 ) 
                

    //let private addLocalAttributes (m : SequencedBookmarks)
    //                                (lenses : BookmarkLenses<'a>) 
    //                                (bm : SequencedBookmark)
    //                                (outerModel : 'a)
    //                                animation = 
    //    let animation = 
    //        animation
    //        |> Animation.onStart (fun name (x : SequencedBookmark) m -> 
    //                                    Log.line "selected bm %s" x.name
    //                                    Optic.set lenses.selectedBookmark_ (Some x.key) outerModel)

    //    let animation = 
    //        match m.animationSettings.useGlobalAnimation with
    //        | true ->
    //            animation
    //        | false ->
    //            animation
    //            |> Animation.seconds bm.duration.value

    //    animation

    /// <summary>
    /// Creates an array of animations that smoothly interpolate between the given bookmarks' camera views.
    /// The animations are scaled according to the distance between the camera view locations. Coinciding camera views are ignored.
    /// The accuracy of the parameterization depends on the given epsilon, where values closer to zero result in higher accuracy.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the sequence is empty.</exception>
    let smoothBookmarkPath (epsilon : float) (bookmarks : SequencedBookmark seq) 
                : IAnimation<'Model, SequencedBookmark>[] =
        let bookmarks = Array.ofSeq bookmarks

        if Seq.isEmpty bookmarks then
            raise <| System.ArgumentException("Camera path cannot be empty")

        let sky = bookmarks.[0].cameraView.Sky

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
            

    let private addGlobalAttributes (m : SequencedBookmarks)
                                    (lenses : BookmarkLenses<'a>) 
                                    (outerModel : 'a)
                                    (animation : IAnimation<'a,SequencedBookmark>) =
        let restoreState name bm outerModel =
            match m.originalSceneState with
            | Some state ->
                Log.line "[Animation] Restoring scene state."
                Optic.set lenses.sceneState_ state outerModel
            | None ->
                Log.line "[Animation] No scene state to restore."
                outerModel

        let animation = 
            match m.animationSettings.useGlobalAnimation with
            | true ->
                animation
                    |> Animation.seconds m.animationSettings.globalDuration.value
            | false ->
                animation

        let animation =
            if m.animationSettings.useEasing then
                animation
                |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
            else
                animation

        let animation =
            animation
                |> Animation.onFinalize restoreState
                |> Animation.onStop restoreState
                |> Animation.onProgress (fun name value model ->
                            Optic.set lenses.setModel_ value model 
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
        let bookmarks = orderedBookmarks m
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
        let m = {m with originalSceneState = Some (Optic.get lenses.sceneState_ outerModel)}
        outerModel, m
        
    let pathWithPausing (m : SequencedBookmarks)
                        (lenses : BookmarkLenses<'a>)
                        (outerModel      : 'a) =
        let bookmarks = orderedBookmarks m
        let animation =
            bookmarks
                |> List.pairwise 
                |> List.map (fun (a,b) -> Primitives.interpolateBm a b)
                |> List.concat
                |> List.map (Animation.onStart (fun name x m -> 
                                                        Log.line "[Bookmark Animation] Selected bookmark %s" x.name
                                                        Optic.set lenses.selectedBookmark_ (Some x.key) m
                                        ))
                |> Animation.path
        let animation = 
            animation
            |> addGlobalAttributes m lenses outerModel
                
        let outerModel =
            outerModel 
            |> Animator.createAndStart AnimationSlot.camera animation
        let m = {m with originalSceneState = Some (Optic.get lenses.sceneState_ outerModel)}
        outerModel, m

    let cameraOnly (m : SequencedBookmarks)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) =
        let views = 
            m.orderList
            |> List.map (fun id -> HashMap.find id m.bookmarks)
            |> List.map (fun (bm : SequencedBookmark) -> bm.cameraView)

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


    let toBookmark (m : SequencedBookmarks)
                   (outerModel      : 'a) 
                   (lenses : BookmarkLenses<'a>)
                   (f : SequencedBookmarks -> option<SequencedBookmark>)=
        let view_ = lenses.navigationModel_ 
                    >-> NavigationModel.camera_
                    >-> CameraControllerState.view_
        match selected m, f m with
        | Some selected, Some next -> 
            let outerModel = 
                let animation = 
                    let animCam =
                         AnimationCameraPrimitives.Animation.Camera.smoothPath
                            m.animationSettings.smoothingFactor.value 
                            [selected.bookmark.cameraView;next.bookmark.cameraView]
                    animCam
                        //|> Animation.map (fun view -> {selected with bookmark = {next.bookmark with cameraView = view}})
                        |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
                        |> Animation.seconds next.duration.value
                        |> Animation.onProgress (fun name value model ->
                            Optic.set view_ value model)
                //smoothCameraPath [selected.cameraView;next.cameraView] navigationModel outerModel
                outerModel
                    |> Animator.createAndStart AnimationSlot.camera animation
            outerModel, {m with selectedBookmark = Some next.key}
        | None ,_ -> 
            Log.line "[SequencedBookmarks] No bookmark selected."
            outerModel, m
        | Some _ , None -> 
            Log.line "[SequencedBookmarks] Could not find next bookmark."
            outerModel, m
