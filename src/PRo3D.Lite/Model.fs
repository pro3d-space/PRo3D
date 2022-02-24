namespace PRo3D.Lite

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Primitives
open Adaptify
open PRo3D.Base

type Message = 
    | ToggleBackground
    
    | SetCursor of V3d
    | SetMousePos of V2i

    | SetOrbitCenter
    | OrbitMessage   of OrbitMessage
    | FreeFlyMessage of FreeFlyController.Message
    | CenterScene

type CameraMode = FreeFly | Orbit

[<ModelType>]
type Model = 
    {
        // camera
        orbitState  : OrbitState
        cameraState : CameraControllerState
        cameraMode  : CameraMode


        // cursor and picking
        mousePos : Option<V2i>
        cursor   : Option<V3d>
        cursorWorldSphereSize : float

        // scene state
        state : State


        // view state
        background  : C4b
    }