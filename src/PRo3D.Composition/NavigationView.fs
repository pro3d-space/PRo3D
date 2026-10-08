namespace PRo3D.Composition

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Navigation2
open MapViewCameraController

/// The view-side wiring of the navigation stack: which camera controller listens to the
/// render control, and which controller threads run. Total over `NavigationMode` - an
/// unknown mode (the enum is persisted) falls back to FreeFly instead of throwing.
module NavigationView =

    /// The active controller's render-control attributes, or none while `cameraLive` is false
    /// (a tool owns the mouse).
    let controllerAttributes (cameraLive : aval<bool>) (model : AdaptiveNavigationModel) : AttributeMap<Navigation.Action> =
        amap {
            let! mode = model.navigationMode
            let! live = cameraLive
            if live then
                match mode with
                | NavigationMode.ArcBall ->
                    yield! ArcBallController.extractAttributes model.camera Navigation.Action.ArcBallAction
                | NavigationMode.MapView ->
                    yield! MapViewController.extractAttributes model.camera Navigation.Action.MapViewControllerAction
                | _ ->
                    yield! FreeFlyController.extractAttributes model.camera Navigation.Action.FreeFlyAction
        }
        |> AttributeMap.ofAMap

    /// The active controller's animation threads (inertia, MapView's per-frame update).
    let threads (model : NavigationModel) : ThreadPool<Navigation.Action> =
        match model.navigationMode with
        | NavigationMode.ArcBall ->
            ArcBallController.threads model.camera |> ThreadPool.map Navigation.ArcBallAction
        | NavigationMode.MapView ->
            MapViewController.threads model.camera |> ThreadPool.map Navigation.MapViewControllerAction
        | _ ->
            FreeFlyController.threads model.camera |> ThreadPool.map Navigation.FreeFlyAction
