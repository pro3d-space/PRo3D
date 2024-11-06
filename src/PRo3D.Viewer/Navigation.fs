namespace PRo3D

open Aether

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives

open System

open FSharp.Data.Adaptive
open Aardvark.Rendering
open PRo3D.Base
open PRo3D.Core



module Navigation =
    
   // open Navigation.Sg


    type Action =
        | ArcBallAction         of ArcBallController.Message
        | OrbitAction           of OrbitMessage
        //| FreeFlyAction         of CameraController.Message
        | FreeFlyAction         of FreeFlyController.Message
        | SetNavigationMode     of NavigationMode

    type smallConfig<'a,'b> = 
        {
            navigationSensitivity : Lens<'a, float>
            up                    : Lens<'b, V3d>
        }           

    let stopPanning (model : NavigationModel) = 
        {model with 
            freeFlyCamera = {model.freeFlyCamera with pan = false }
            orbitCamera   = (snd OrbitState.panning_) false model.orbitCamera
        }

    let setExploreCenter (model : NavigationModel) (center : V3d) =
        let model =  { model with exploreCenter  = center }
        let state = ArcBallController.update model.freeFlyCamera (ArcBallController.Message.Pick center)
        let orbitState = snd OrbitState.targetCenter_ model.exploreCenter model.orbitCamera
        {model with orbitCamera    = orbitState
                    freeFlyCamera  = state
                    exploreCenter  = center
                    navigationMode = NavigationMode.Orbit}

    let setSensitivity (model : NavigationModel) (sensitivity : float) =
        
        //let zoomMouseWheelSensitivity = exp(sensitivity) * 0.1 
        //let orbitCamera = snd OrbitState.moveSensitivity_ sensitivity model.orbitCamera
        //let orbitCamera = snd OrbitState.zoomSensitivity_ zoomMouseWheelSensitivity orbitCamera
        //let orbitCamera = snd OrbitState.targetCenter_ model.exploreCenter orbitCamera

        let config = 
            {model.freeFlyCamera.freeFlyConfig with
              panMouseSensitivity       = exp(sensitivity) * 0.0025
              dollyMouseSensitivity     = exp(sensitivity) * 0.0025
              zoomMouseWheelSensitivity = exp(sensitivity) * 0.1
              moveSensitivity           = sensitivity
              lookAtMouseSensitivity    = 0.004
              lookAtDamping             = 50.0
            }

        let freeFlyCamera = {model.freeFlyCamera with freeFlyConfig = config }
        let orbitRadius = model.freeFlyCamera.view.Location.Distance model.exploreCenter
        { 
            model with freeFlyCamera = freeFlyCamera
                       orbitCamera   = OrbitState.ofFreeFly orbitRadius freeFlyCamera
        }

    let toOrbitState (radius : float) (newFreeFly : CameraControllerState) =
        let view = newFreeFly.view
        let forward = view.Forward.Normalized // - c |> Vec.normalize
        let center = view.Location + forward * radius
        let sky = newFreeFly.view.Sky
        let right = newFreeFly.view.Right
        let left = newFreeFly.view.Left
        let basis = M44d.FromBasis(right, Vec.cross sky right.Normalized, sky, V3d.Zero)
        Log.line "BASIS: %A" basis
        //Log.line "RIGHT NORM %A" right.Normalized

        let sphereForward = basis.TransposedTransformDir -forward
        let phiTheta = sphereForward.SphericalFromCartesian()

        OrbitState.create' newFreeFly.view.Right newFreeFly.view.Sky center phiTheta.X phiTheta.Y radius


    let update<'a,'b> (viewConfigModel : 'a) (referenceSystem : 'b) 
                      (lenses : smallConfig<'a,'b>) // removed switchToArcball because it was hardcoded true
                      (model : NavigationModel) (act : Action) =
        match act with            
        | ArcBallAction arcBallMessage -> 
            let model =
                match arcBallMessage with 
                | ArcBallController.Message.Pick newCenter ->
                    { model with navigationMode =  NavigationMode.ArcBall
                                 exploreCenter  = newCenter }
                | _ ->  
                    { model with navigationMode =  NavigationMode.ArcBall }
            
            let cam = ArcBallController.update model.freeFlyCamera arcBallMessage
            let cam = { cam with sensitivity = lenses.navigationSensitivity.Get viewConfigModel
                                 orbitCenter = Some model.exploreCenter } 
            let orbitRadius = model.freeFlyCamera.view.Location.Distance model.exploreCenter
            let model = 
                { model with freeFlyCamera = cam
                             orbitCamera   = OrbitState.ofFreeFly orbitRadius cam
                }
            model

        | OrbitAction orbitMsg -> 
            let camera = OrbitController.update model.orbitCamera orbitMsg
            // update the explore center in model if it is changed
            let model =
                match orbitMsg with 
                | OrbitMessage.SetTargetCenter targetCenter ->
                    { model with exploreCenter  = targetCenter}        
                | _ ->  
                    model
            
            // update move sensitivity and target center to current values in model - why is this necessary here? Should this not be done whenever changes occur in model?
            let sensitivity = (lenses.navigationSensitivity.Get(viewConfigModel))

            let zoomMouseWheelSensitivity = exp(sensitivity) * 0.1 // TODO RNO check where that calculation comes from
            let camera = snd OrbitState.moveSensitivity_ sensitivity camera
            let camera = snd OrbitState.zoomSensitivity_ zoomMouseWheelSensitivity camera
            let camera = snd OrbitState.targetCenter_ model.exploreCenter camera
            { model with orbitCamera   = camera
                         //freeFlyCamera = OrbitState.toFreeFly model.freeFlyCamera.moveSpeed camera // RNO is moveSpeed right here?
            }     
        | FreeFlyAction a ->
            let camera = FreeFlyController.update model.freeFlyCamera a
            let sensitivity = lenses.navigationSensitivity.Get(viewConfigModel)          
            let model = {model with freeFlyCamera = camera}
            setSensitivity model sensitivity
        | SetNavigationMode mode ->
            match mode with
            | NavigationMode.FreeFly ->
                let center = 
                    match model.freeFlyCamera.orbitCenter with
                    | Some x -> x
                    | None   -> V3d.OOO
                
                let view' =
                    CameraView.lookAt model.freeFlyCamera.view.Location center (lenses.up.Get (referenceSystem))
                
                { model with freeFlyCamera = { model.freeFlyCamera with view = view'}; navigationMode = mode} 
            | NavigationMode.Orbit ->  
                let orbitRadius = model.freeFlyCamera.view.Location.Distance model.exploreCenter

                //Log.line "camer view = %A" model.camera.view
                let orbitCamera = toOrbitState orbitRadius model.freeFlyCamera //OrbitState.ofFreeFly orbitRadius model.camera
               
                //Log.line "orbit view = %A" orbitCamera
                { model with navigationMode = mode
                             orbitCamera    = orbitCamera}
            | _ ->
               { model with navigationMode = mode }


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
                | NavigationMode.FreeFly -> yield! FreeFlyController.extractAttributes model.freeFlyCamera FreeFlyAction
                | NavigationMode.ArcBall -> yield! ArcBallController.extractAttributes model.freeFlyCamera ArcBallAction
                | _ -> failwith "Invalid NavigationMode"
            } |> AttributeMap.ofAMap

        let viewNavigationModes  (model : AdaptiveNavigationModel) =
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large location arrow icon"] [] ]
                Html.Layout.boxH [ Html.SemUi.dropDown model.navigationMode SetNavigationMode ]                
            ]

    module Sg =
        let view (model:AdaptiveNavigationModel)=
            let point = PRo3D.Base.Sg.dot (AVal.constant C4b.Magenta) (AVal.constant 3.0) model.exploreCenter 
            Sg.ofList [point] 
           
