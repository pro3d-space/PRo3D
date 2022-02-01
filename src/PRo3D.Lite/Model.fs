namespace PRo3D.Lite

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Primitives
open Adaptify

type Message = 
    | CenterScene
    | ToggleBackground
    | SetCursor of V3d
    | SetMousePos of V2i
    | SetOrbitCenter
    | OrbitMessage of OrbitMessage
    | FreeFlyMessage of FreeFlyController.Message

type CameraMode = FreeFly | Orbit

[<ModelType>]
type Model = 
    {
        orbitState  : OrbitState
        cameraState : CameraControllerState
        background  : C4b

        cameraMode : CameraMode

        mousePos : Option<V2i>
        cursor : Option<V3d>

        state : State
    }