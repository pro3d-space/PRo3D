namespace PRo3D.Base

open FSharp.Data.Adaptive
open Adaptify

open Aardvark.Base
open Aardvark.UI.Primitives

type NavigationMode = 
    | FreeFly = 0 
    | ArcBall = 1
    | Orbit   = 2

[<ModelType>]
type NavigationModel = {
    freeFlyCamera  : CameraControllerState    
    orbitCamera    : OrbitState
    navigationMode : NavigationMode
    exploreCenter  : V3d
} with 
    member this.view =
        match this.navigationMode with
        | NavigationMode.FreeFly
        | NavigationMode.ArcBall ->
            this.freeFlyCamera.view
        | NavigationMode.Orbit ->
            this.orbitCamera.view
        | _ ->
            this.freeFlyCamera.view

