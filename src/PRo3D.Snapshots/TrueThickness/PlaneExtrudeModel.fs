

namespace PlaneExtrude

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Trafos

[<ModelType>]
type PlaneModel =
    {
        v0    : V3d
        v1    : V3d
        v2    : V3d
        v3    : V3d
        group : int
        above : int
        below : int

        [<NonAdaptive>]
        id : string

        local2Global : Trafo3d
    }

type LineSide =
    | LEFT
    | RIGHT

[<ModelType>]
type LineModel =
    {
        startPlane : PlaneModel
        endPlane   : PlaneModel
        group      : int
        side       : LineSide

        local2Global : Trafo3d
    }
    
[<ModelType>]
type Model =
    {
        pointsModel : Utils.PickPointsModel
        planeModels : IndexList<PlaneModel>
        lineModels  : IndexList<LineModel>

        addMode     : bool
        extrudeMode : bool
        selected    : option<string>
        trafo       : Transformation
        maxGroupId  : int

        [<NonAdaptive>]
        id : string
    }