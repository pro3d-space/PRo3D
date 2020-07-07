namespace Svgplus.RectangleStackTypes

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Svgplus.RectangleType

type RectangleStackId = RectangleStackId of Guid

module RectangleStackId =
    let createNew() = 
        Guid.NewGuid() |> RectangleStackId

type StackType = 
    | Primary
    | Secondary

type FlattenHorizonData = 
    {
        offsetGlobal : float
        offsetFromStackTop : float
    }

[<DomainType>]
type RectangleStack = {
    [<NonIncremental>]
    id              : RectangleStackId    
    [<NonIncremental>]
    stackType       : StackType

    rectangles      : hmap<RectangleId, Rectangle>
    order           : list<RectangleId>
    borders         : hmap<RectangleBorderId, RectangleBorder>
    selectedBorder  : option<RectangleBorderId>
    pos             : V2d
    yAxisMargin     : float
    stackDimensions : V2d
    stackRange      : Range1d   // set by init!
    yToSvg          : float
} with
    member this.maxWidth = 
        let maxRectangleWidth =
            this.rectangles 
            |> DS.HMap.values
            |> List.map (fun r -> r.maxWidth)
            |> List.max
        maxRectangleWidth