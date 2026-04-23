namespace PRo3D

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives

open System

open FSharp.Data.Adaptive
open Aardvark.Rendering
open PRo3D.Base
open PRo3D.Core
open PRo3D.Navigation2
open MapViewCameraController
open MapViewCameraController.MapViewController

module Navigation =
    
   // open Navigation.Sg


    type Action =
        | ArcBallAction            of ArcBallController.Message        
        //| FreeFlyAction            of CameraController.Message
        | FreeFlyAction            of FreeFlyController.Message
        | MapViewControllerAction of MapViewController.Message
        | SetNavigationMode        of NavigationMode

    type smallConfig<'a,'b> = 
        {
            navigationSensitivity : Lens<'a, float>
            up                    : Lens<'b, V3d>
            north                 : Lens<'b, V3d>
            frustum               : Lens<'a, Frustum>
            windowSize            : Lens<'a, V2i>
            planet                : Lens<'b, Planet>
        }

    let pickOrbitCenter (pickFunction : Option<unit->Option<V3d>>) (model : NavigationModel) = 
        let orbitCenter = 
            match pickFunction with
            | None -> None
            | Some f -> 
                Log.startTimed "pick new orbit"
                let point = f()
                Log.stop()
                point

        match orbitCenter with
        | None -> 
            Log.warn "could not get new orbit center, please center view to surface"

            let oldNavMode = 
                if model.navigationMode = NavigationMode.ArcBall then NavigationMode.FreeFly
                else model.navigationMode
                    

            { model with navigationMode = oldNavMode }, Some "could not pick new orbit center with center ray.\n Please center view to surface before changing to ArcBall or select explore center manually"
        | Some p -> 
            Log.line "new orbit implicitly set to center ray"
            { model with exploreCenter = p; navigationMode = NavigationMode.ArcBall }, Some("New orbit set with center ray")

    //find and distance to the next Surface in the center of the camera - save it in rotationFactor (only used in a LegacyCameraController)
    let findDistancetoSurface (pickFunction : Option<unit->Option<V3d>>) (model : NavigationModel) = 
        let centerPoint = 
            match pickFunction with
            | None -> None
            | Some f -> 
                Log.startTimed "pick Point on Surface to calculate Distance"
                let point = f()
                Log.stop()
                point

        match centerPoint with
        | None -> 
            Log.warn "could not find Point on Surface to get Distance, please center view to surface"

            let oldNavMode = 
                if model.navigationMode = NavigationMode.MapView then NavigationMode.FreeFly
                else model.navigationMode                    

            { model with navigationMode = oldNavMode }, Some "could not find Distance to Surface center with center ray.\n Please center view to surface before changing to MapViewCamera"
        | Some p -> 
            Log.line "Distance of Surface to Center found"

            let distance = Vec.Distance(V3d.OOO, p)

            let cam = { model.camera with rotationFactor = distance }

            { model with camera = cam; navigationMode = NavigationMode.MapView }, Some("Distance to Surface set with center ray")

    let update<'a,'b> (bigConfigA : 'a) (bigConfigB : 'b) (smallConfig : smallConfig<'a,'b>) (switchToArcball : bool) (pickFunction : Option<unit->Option<V3d>>) (model : NavigationModel) (act : Action) (ctrlFlag : bool) =
        match act with            
        | ArcBallAction a -> 
            let model, feedback =
                match a with 
                | ArcBallController.Message.Pick a when switchToArcball->
                    { model with navigationMode =  NavigationMode.ArcBall; exploreCenter = a }, None
                | _ ->                      
                    model, None
                    
            let (msg : ArcBallController.Message) =
                match a with
                | ArcBallController.Message.Down (button, pos) -> 
                    let mb = if (ctrlFlag && button = MouseButtons.Right) then MouseButtons.Left else button
                    ArcBallController.Message.Down (mb, pos)
                | ArcBallController.Message.Up button -> 
                    let mb = if (ctrlFlag && button = MouseButtons.Right) then MouseButtons.Left else button
                    ArcBallController.Message.Up mb
                | _ -> a
            let cam = ArcBallController.update model.camera msg
            let cam = { cam with sensitivity = smallConfig.navigationSensitivity.Get(bigConfigA); orbitCenter = Some model.exploreCenter } 
            match cam.orbitCenter with
            | Some oc -> { model with camera = cam; exploreCenter = oc}, feedback
            | None -> { model with camera = cam }, feedback
                  
        | FreeFlyAction a ->
            let cam' = FreeFlyController.update model.camera a
            let sensitivity = smallConfig.navigationSensitivity.Get(bigConfigA)          
            
            let config = { 
              cam'.freeFlyConfig with
                panMouseSensitivity       = exp(sensitivity) * 0.0025
                dollyMouseSensitivity     = exp(sensitivity) * 0.0025
                zoomMouseWheelSensitivity = exp(sensitivity) * 0.1
                moveSensitivity           = sensitivity
                lookAtMouseSensitivity    = 0.004
                lookAtDamping             = 50.0
                }
            
            { 
              model with camera = { cam' with freeFlyConfig = config }
            }, None
        | MapViewControllerAction a -> 
            let frustum = smallConfig.frustum.Get(bigConfigA)
            
            let angle = Math.Tanh(frustum.right / frustum.near) * 2.0

            let windowSize = smallConfig.windowSize.Get(bigConfigA)
            
            //let view = 
            //    model.camera.view 
            //    |> CameraView.withUp (smallConfig.north.Get(bigConfigB))
            //    |> setCameraViewCenter (smallConfig.north.Get(bigConfigB))
            
            let cam = { 
                model.camera with 
                    view = model.camera.view
                    sensitivity    = smallConfig.navigationSensitivity.Get(bigConfigA)
                    orbitCenter    = Some model.exploreCenter   
                    targetPhiTheta = V2d(windowSize.X, windowSize.Y)
                    panFactor      = angle
                }
            
            let cam = MapViewController.update cam a
                                    
            let cam = 
                let planet     = smallConfig.planet.Get(bigConfigB)

                if model.updatePerFrame then
                    match a with 
                    | KeyUp _ 
                    | Up _ 
                    | Move _
                    | StepTime ->
                        cam |> MapViewController.updateCameraForMapView planet                    
                    | _ -> cam
                else
                    match a with 
                    | KeyUp _ 
                    | Up _ -> 
                        cam |> MapViewController.updateCameraForMapView planet                    
                    | _ -> cam

            { model with camera = cam }, None

        | SetNavigationMode mode ->
            match mode with
            | NavigationMode.ArcBall ->
                let model, message = pickOrbitCenter pickFunction model
                { model with updatePerFrame = false }, message
            | NavigationMode.FreeFly ->
                let center = 
                    match model.camera.orbitCenter with
                    | Some x ->  x
                    | None   -> V3d.OOO
                
                let view' =
                    CameraView.lookAt model.camera.view.Location center (smallConfig.up.Get(bigConfigB))
                
                { model with camera = { model.camera with view = view'}; navigationMode = mode; updatePerFrame = false}, None
            | NavigationMode.MapView ->                
                let planet     = smallConfig.planet.Get(bigConfigB)
                
                let cam = model.camera |> switchToMapViewController planet
                                    
                let model = { model with camera = cam; exploreCenter = V3d.OOO; navigationMode = NavigationMode.MapView; updatePerFrame = true }

                findDistancetoSurface pickFunction model                
            | _ ->  { model with navigationMode = mode; updatePerFrame = false }, None
               
    module UI =        

        type smallConfig<'ma> = 
            {
                getNearPlane : 'ma -> aval<float>
                getFarPlane  : 'ma -> aval<float>
            }

        //let frustum near far =
        //    adaptive {
        //        let! near = near
        //        let! far = far
        //        return (Frustum.perspective 90.0 near far 1.0)
        //        }

        let renderControlAttributes (model : AdaptiveNavigationModel) near far =
            amap {
                let! state = model.navigationMode 
                match state with
                | NavigationMode.FreeFly -> yield! FreeFlyController.extractAttributes model.camera FreeFlyAction
                | NavigationMode.ArcBall -> yield! ArcBallController.extractAttributes model.camera ArcBallAction
                | NavigationMode.MapView -> yield! MapViewController.extractAttributes model.camera MapViewControllerAction
                | _ -> failwith "Invalid NavigationMode"
            } |> AttributeMap.ofAMap

        let viewNavigationModes  (model : AdaptiveNavigationModel) =
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large location arrow icon"] [] ]                
                Html.Layout.boxH [ Incremental.div (AttributeMap.empty) (
                    alist {
                        let navMode = model.navigationMode
                        Html.SemUi.dropDown navMode SetNavigationMode
                    })]
            ]

    module Sg =
        let view (model:AdaptiveNavigationModel)=
            let point = PRo3D.Base.Sg.dot (AVal.constant C4b.Magenta) (AVal.constant 3.0) model.exploreCenter 
            Sg.ofList [point] 
           
