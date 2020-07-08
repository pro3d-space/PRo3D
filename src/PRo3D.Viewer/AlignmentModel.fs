namespace PRo3D.Align

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Primitives
open Aardvark.UI
open IPWrappers
open Aardvark.Base.CameraView

type PickSurfacePair = {
    point       : V3d
    surfaceName : Guid
    }

[<ModelType>]
type Alignment = {    
    red         : Guid
    blue        : Guid
    redPoints   : IndexList<V3d>
    bluePoints  : IndexList<V3d>    
}

[<ModelType>]
type AlignmentModel = {
    pickedPoints : IndexList<PickSurfacePair>
    alignment    : option<Alignment>
    resultTrafo  : option<Trafo3d>
}

