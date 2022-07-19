namespace PRo3D.Core

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open Aardvark.Rendering
open Aardvark.UI.Anewmation

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
    let interpolate (src : SequencedBookmark) (dst : SequencedBookmark) : IAnimation<'Model, SequencedBookmark> =
        let animCam = Animation.Camera.interpolate src.bookmark.cameraView dst.bookmark.cameraView
        // TODO add other interpolations
        animCam
            |> Animation.map (fun view -> {dst with bookmark = {dst.bookmark with cameraView = view}})

    let pathAllBookmarks (m : SequencedBookmarks) =
        let bookmarks = orderedBookmarks m
        let animation =
            bookmarks
                |> List.pairwise
                |> List.map (fun (a,b) -> interpolate a b)
                |> Animation.path
        animation
    
    let smoothPath (views : seq<CameraView>)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) =
        let view_       = navigationModel 
                            >-> NavigationModel.camera_
                            >-> CameraControllerState.view_

        let animate views =
            let durationInSeconds = 5
        
            AnimationCameraPrimitives.Animation.Camera.smoothPath 0.1 views
                    |> Animation.seconds durationInSeconds
                    |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
                    |> Animation.onFinalize (fun name _ m -> 
                                                            Log.line "[Animation] Finished animation."
                                                            m
                                            )
                    |> Animation.onProgress (fun name value model ->
                             Optic.set view_ value model 
                        )

        let animation =
            animate views 

        outerModel
        |> Animator.createAndStart AnimationSlot.camera animation

    let smoothPathAllBookmarks (m : SequencedBookmarks)
                               (navigationModel : Lens<'a,NavigationModel>) 
                               (outerModel      : 'a) =
        let views = 
            m.orderList
                |> List.map (fun id -> HashMap.find id m.bookmarks)
                |> List.map (fun (bm : SequencedBookmark) -> bm.cameraView)

        smoothPath views navigationModel outerModel

    let toBookmark (m : SequencedBookmarks)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) 
                   (f : SequencedBookmarks -> option<SequencedBookmark>)=
        match selected m, f m with
        | Some selected, Some next -> 
            let outerModel = smoothPath [selected.cameraView;next.cameraView] navigationModel outerModel
            outerModel, {m with selectedBookmark = Some next.key}
        | None ,_ -> 
            Log.line "[SequencedBookmarks] No bookmark selected."
            outerModel, m
        | Some _ , None -> 
            Log.line "[SequencedBookmarks] Could not find next bookmark."
            outerModel, m
                        

    /////// ANEWMATIONS ///////////    