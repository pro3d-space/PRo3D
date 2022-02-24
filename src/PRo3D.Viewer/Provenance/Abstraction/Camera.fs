namespace PRo3D.Provenance.Abstraction

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive

open Adaptify

type OCameraView = CameraView

// We need to redefine this because we want structural equality but CameraView is a class :/
[<ModelType>]
type CameraView = {
    sky         : V3d
    location    : V3d
    forward     : V3d
    up          : V3d
    right       : V3d
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CameraView =
    
    let create (v : OCameraView) : CameraView = {
        sky      = v.Sky
        location = v.Location
        forward  = v.Forward
        up       = v.Up
        right    = v.Right
    }

    let restore (v : CameraView) : OCameraView =
        OCameraView (v.sky, v.location, v.forward, v.up, v.right)