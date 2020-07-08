namespace PRo3D

module Navigation =
    open Aardvark.Base
    open Aardvark.Application
    open Aardvark.UI
    open Aardvark.UI.Primitives

    open System
    
    open FSharp.Data.Adaptive
    open Aardvark.Base.Rendering
    open Aardvark.Base.Camera
    open PRo3D.Navigation2
   // open Navigation.Sg

    type Action =
        | ArcBallAction         of ArcBallController.Message
        //| FreeFlyAction         of CameraController.Message
        | FreeFlyAction         of FreeFlyController.Message
        | SetNavigationMode     of NavigationMode

    type smallConfig<'a,'b> = 
        {
            navigationSensitivity : Lens<'a, float>
            up                    : Lens<'b, V3d>
        }

    let update<'a,'b> (bigConfigA : 'a) (bigConfigB : 'b) (smallConfig : smallConfig<'a,'b>) (switchToArcball : bool) (model : NavigationModel) (act : Action) =
      match act with            
        | ArcBallAction a -> 
          let model =
              match a with 
                  | ArcBallController.Message.Pick a when switchToArcball->
                      { model with navigationMode =  NavigationMode.ArcBall; exploreCenter = a }
                  | _ ->  { model with navigationMode =  NavigationMode.ArcBall } //model
          
          let cam = ArcBallController.update model.camera a
          let cam = { cam with sensitivity = smallConfig.navigationSensitivity.Get(bigConfigA); orbitCenter = Some model.exploreCenter } 
          match cam.orbitCenter with
              | Some oc -> { model with camera = cam; exploreCenter = oc}
              | None -> { model with camera = cam }
                
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
          }
        | SetNavigationMode mode ->
          match mode with
            | NavigationMode.FreeFly ->
              let center = 
                  match model.camera.orbitCenter with
                      | Some x ->  x
                      | None   -> V3d.OOO

              let view' =
                  CameraView.lookAt model.camera.view.Location center (smallConfig.up.Get(bigConfigB))
              
              { model with camera = { model.camera with view = view'}; navigationMode = mode} 
            | _ ->  { model with navigationMode = mode }
               
    module UI =
        open Aardvark.Base.Camera

        type smallConfig<'ma> = 
            {
                getNearPlane : 'ma -> aval<float>
                getFarPlane  : 'ma -> aval<float>
            }

        let frustum near far =
            adaptive {
                let! near = near
                let! far = far
                return (Frustum.perspective 90.0 near far 1.0)
                }

        let renderControlAttributes (model : MNavigationModel) near far =
            amap {
                let! state = model.navigationMode 
                match state with
                    | NavigationMode.FreeFly -> yield! FreeFlyController.extractAttributes model.camera FreeFlyAction
                    | NavigationMode.ArcBall -> yield! ArcBallController.extractAttributes model.camera ArcBallAction
                    | _ -> failwith "Invalid NavigationMode"
            } |> AttributeMap.ofAMap

        let viewNavigationModes  (model : MNavigationModel) =
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large location arrow icon"][] ]
                Html.Layout.boxH [ Html.SemUi.dropDown model.navigationMode SetNavigationMode ]                
            ]

    module Sg =
        let view (model:MNavigationModel) =
            let point = PRo3D.Base.Sg.dot (AVal.constant C4b.Magenta) (AVal.constant 3.0) model.exploreCenter 
            Sg.ofList [point]
           
