

namespace PlaneExtrude

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI.Trafos

[<DomainType>]
type PlaneModel =
    {
        v0    : V3d
        v1    : V3d
        v2    : V3d
        v3    : V3d
        group : int
        above : int
        below : int

        [<NonIncremental>]
        id : string

        local2Global : Trafo3d
    }

type LineSide =
    | LEFT
    | RIGHT

[<DomainType>]
type LineModel =
    {
        startPlane : PlaneModel
        endPlane   : PlaneModel
        group      : int
        side       : LineSide

        local2Global : Trafo3d
    }
    
[<DomainType>]
type Model =
    {
        pointsModel : Utils.PickPointsModel
        planeModels : plist<PlaneModel>
        lineModels  : plist<LineModel>

        addMode     : bool
        extrudeMode : bool
        selected    : option<string>
        trafo       : Transformation
        maxGroupId  : int

        [<NonIncremental>]
        id : string
    }