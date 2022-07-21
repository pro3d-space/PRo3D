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

        
    /// Creates an animation that interpolates between two bookmarks
    let inline interpolateBm (src : SequencedBookmark) (dst : SequencedBookmark) : IAnimation<'Model, SequencedBookmark> =
        let animCam = Animation.Camera.interpolate src.bookmark.cameraView dst.bookmark.cameraView
        // TODO add other interpolations
        animCam
            |> Animation.map (fun view -> {dst with bookmark = {dst.bookmark with cameraView = view}})

    let inline slerpBm (src : SequencedBookmark) (dst : SequencedBookmark) : IAnimation<'Model, SequencedBookmark> =
        let slerped = Primitives.slerp (CameraView.orientation src.bookmark.cameraView)
                                       (CameraView.orientation dst.bookmark.cameraView)
        slerped
            |> Animation.map (fun ( x : Rot3d)  -> 
                                    {dst with bookmark = 
                                                 {dst.bookmark with cameraView = CameraView.withOrientation x dst.cameraView}} 
                             ) 

    /// <summary>
    /// Creates an array of animations that smoothly interpolate between the given camera views.
    /// The animations are scaled according to the distance between the camera view locations. Coinciding camera views are ignored.
    /// The accuracy of the parameterization depends on the given epsilon, where values closer to zero result in higher accuracy.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the sequence is empty.</exception>
    let smoothBookmarkPath (epsilon : float) (points : SequencedBookmark seq) : IAnimation<'Model, SequencedBookmark>[] =
        let points = Array.ofSeq points

        if Seq.isEmpty points then
            raise <| System.ArgumentException("Camera path cannot be empty")

        let sky = points.[0].cameraView.Sky

        let locations =
            points
            |> Array.map (fun bm -> bm.cameraView |> CameraView.location)
            |> Primitives.smoothPath' Vec.distance epsilon

        let orientations =
            points
            //|> Array.map (fun bm -> bm.cameraView |> CameraView.orientation)
            |> Primitives.path' slerpBm (fun _ _ -> 1.0)

        (locations, orientations)
        ||> Array.map2 (fun l o ->
            let o = o |> Animation.duration l.Duration
            (l, o) ||> Animation.map2 (fun l o -> {o with bookmark = 
                                                            {o.bookmark with cameraView = CameraView.orient l (CameraView.orientation o.bookmark.cameraView) sky}})
        )



    let private execute (setter_ : Lens<'a,'b>) 
                        (outerModel      : 'a) 
                animation =
        let durationInSeconds = 10
        let animation = 
            animation
                |> Animation.seconds durationInSeconds
                |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
                |> Animation.onFinalize (fun name _ m -> 
                                                        Log.line "[Animation] Finished animation."
                                                        m
                                        )
                |> Animation.onProgress (fun name value model ->
                            Optic.set setter_ value model 
                    )
        outerModel
        |> Animator.createAndStart AnimationSlot.camera animation



    let private execute' (sbs : SequencedBookmarks)
                         (lenses : BookmarkLenses<'a>) 
                         (outerModel      : 'a) 
                         animation =
        let restoreState name bm m =
            match sbs.originalSceneState with
            | Some state ->
                Log.line "[Animation] Restoring scene state."
                Optic.set lenses.sceneState_ state m
            | None ->
                Log.line "[Animation] No scene state to restore."
                m
        let animation = 
            animation
                |> Animation.seconds sbs.animationSettings.globalDuration.value

        let animation =
            if sbs.animationSettings.useEasing then
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
            match sbs.animationSettings.loopMode with
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

        outerModel
        |> Animator.createAndStart AnimationSlot.camera animation

    let pathAllBookmarks (m : SequencedBookmarks)
                         (lenses : BookmarkLenses<'a>)
                         (outerModel      : 'a) =
        let bookmarks = orderedBookmarks m
        let animation =
            bookmarks
                |> List.pairwise
                |> List.map (fun (a,b) -> interpolateBm a b 
                                            |> Animation.onStart (fun name x m -> 
                                                        Log.line "selected bm %s" x.name
                                                        Optic.set lenses.selectedBookmark_ (Some x.key) m
                                        ))
                |> Animation.path
        let outerModel = 
            animation
                |> (execute' m lenses outerModel)
        let m = {m with originalSceneState = Some (Optic.get lenses.sceneState_ outerModel)}
        outerModel, m

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
        let outerModel = 
            animation
                |> (execute' m lenses outerModel)
        let m = {m with originalSceneState = Some (Optic.get lenses.sceneState_ outerModel)}
        outerModel, m
        

    let smoothCameraPath (views : seq<CameraView>)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) =
        let view_ = navigationModel 
                        >-> NavigationModel.camera_
                        >-> CameraControllerState.view_

        AnimationCameraPrimitives.Animation.Camera.smoothPath 0.1 views
            |> execute view_ outerModel 

    let cameraOnly (m : SequencedBookmarks)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) =
        let views = 
            m.orderList
                |> List.map (fun id -> HashMap.find id m.bookmarks)
                |> List.map (fun (bm : SequencedBookmark) -> bm.cameraView)
        smoothCameraPath views navigationModel outerModel

    let toBookmark (m : SequencedBookmarks)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) 
                   (f : SequencedBookmarks -> option<SequencedBookmark>)=
        match selected m, f m with
        | Some selected, Some next -> 
            let outerModel = smoothCameraPath [selected.cameraView;next.cameraView] navigationModel outerModel
            outerModel, {m with selectedBookmark = Some next.key}
        | None ,_ -> 
            Log.line "[SequencedBookmarks] No bookmark selected."
            outerModel, m
        | Some _ , None -> 
            Log.line "[SequencedBookmarks] Could not find next bookmark."
            outerModel, m
