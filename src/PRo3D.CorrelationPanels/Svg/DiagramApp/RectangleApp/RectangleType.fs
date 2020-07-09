namespace Svgplus.RectangleType

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open CorrelationDrawing

type RectangleId = RectangleId of Guid

module RectangleId =
    let createNew() = 
        Guid.NewGuid() |> RectangleId
    let invalid =
        Guid.Empty |> RectangleId
    let getValue (RectangleId id) =
        id

[<ModelType>]
type Rectangle = {
    [<NonAdaptive>]
    id             : RectangleId

    pos           : V2d
    dim           : Size2D
    faciesId      : Guid
    fixedWidth    : option<float>
    grainSize     : UIPlus.GrainSizeInfo
    worldHeight   : float
    fixedInfinityHeight : option<float>

    colour        : Aardvark.UI.ColorInput
    isUncertain   : bool    // visualized by dashed border-line

    isSelected    : bool
    isHovering    : bool
} with 
    member this.maxWidth =
        let labelWidth = 67.0 // MAGIC fixed width!!

        match this.fixedWidth with
        | Some w  -> w + labelWidth
        | None    -> this.dim.width + labelWidth

type RectangleBorderId = RectangleBorderId of Guid
type BorderContactId   = BorderContactId   of Guid

module BorderContactId =
    let create() =
        Guid.NewGuid() |> BorderContactId
    let getValue (BorderContactId a) =
        a

module RectangleBorderId =
    let create() =
        Guid.NewGuid() |> RectangleBorderId
    let getValue (RectangleBorderId a) =
        a

[<ModelType>]
type RectangleBorder = {
    [<NonAdaptive>]
    id             : RectangleBorderId
    [<NonAdaptive>]
    contactId      : BorderContactId

    color          : C4b
    weight         : float

    [<NonAdaptive>]
    upperRectangle : RectangleId
    [<NonAdaptive>]
    lowerRectangle : RectangleId
} with 
    member this.width (rectangles : HashMap<RectangleId,Rectangle>) =
        let rectangleA = rectangles |> HashMap.tryFind this.upperRectangle
        let rectangleB = rectangles |> HashMap.tryFind this.lowerRectangle

        rectangleB
        |> Option.map2 (fun a b -> max a.dim.width b.dim.width) rectangleA  
        |> Option.defaultValue 10.0

    member this.pos (rectangles : HashMap<RectangleId,Rectangle>) =
        rectangles 
        |> HashMap.tryFind this.lowerRectangle 
        |> Option.map(fun x -> x.pos) 
        |> Option.defaultValue V2d.Zero