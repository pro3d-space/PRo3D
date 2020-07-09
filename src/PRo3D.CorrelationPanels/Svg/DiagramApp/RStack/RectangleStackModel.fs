namespace Svgplus.RectangleStackTypes

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
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

[<ModelType>]
type RectangleStack = {
    [<NonAdaptive>]
    id              : RectangleStackId    
    [<NonAdaptive>]
    stackType       : StackType

    rectangles      : HashMap<RectangleId, Rectangle>
    order           : list<RectangleId>
    borders         : HashMap<RectangleBorderId, RectangleBorder>
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