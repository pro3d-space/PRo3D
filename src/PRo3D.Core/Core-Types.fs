namespace PRo3D.Core

open FSharp.Data.Adaptive
open Adaptify

open Aardvark.Base
open Aardvark.UI.Primitives

type NavigationMode = 
    | FreeFly = 0 
    | ArcBall = 1

[<ModelType>]
type NavigationModel = {
    camera         : CameraControllerState    
    navigationMode : NavigationMode
    exploreCenter  : V3d
}

type TTAlignment =
    | Top    = 0
    | Right  = 1
    | Bottom = 2
    | Left   = 3