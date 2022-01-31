namespace PRo3D.Lite

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Primitives
open Adaptify

type Message = 
    | Camera of FreeFlyController.Message
    | CenterScene
    | ToggleBackground

[<ModelType>]
type Model = 
    {
        //orbitState  : OrbitState
        cameraState : CameraControllerState
        background  : C4b



        state : State
    }