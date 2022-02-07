namespace PRo3D.Lite

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Primitives
open Adaptify
open PRo3D.Base

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
        cameraMode  : CameraMode

        background  : C4b



        mousePos : Option<V2i>
        cursor   : Option<V3d>
        donutSizeInPixels : float

        state : State
    }