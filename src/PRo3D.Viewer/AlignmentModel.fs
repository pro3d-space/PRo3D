namespace PRo3D.Align

open System

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI.Primitives
open Aardvark.UI
open IPWrappers
open Aardvark.Base.CameraView

type PickSurfacePair = {
    point       : V3d
    surfaceName : Guid
    }

[<DomainType>]
type Alignment = {    
    red         : Guid
    blue        : Guid
    redPoints   : plist<V3d>
    bluePoints  : plist<V3d>    
}

[<DomainType>]
type AlignmentModel = {
    pickedPoints : plist<PickSurfacePair>
    alignment    : option<Alignment>
    resultTrafo  : option<Trafo3d>
}

