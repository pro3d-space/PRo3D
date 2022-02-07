namespace PRo3D.Base

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open Aardvark.Application
open Adaptify

[<ModelType>]
type OrbitState =
    {
        sky     : V3d
        right   : V3d

        center  : V3d
        phi     : float
        theta   : float
        radius  : float

        targetPhi : float
        targetTheta : float
        targetRadius : float
        targetCenter : V3d
        
        dragStart : Option<V2i>
        [<NonAdaptive>]
        lastRender : Option<MicroTime>

        view : CameraView
        
        radiusRange : Range1d
        thetaRange : Range1d
        moveSensitivity : float
        zoomSensitivity : float
        speed : float

    }