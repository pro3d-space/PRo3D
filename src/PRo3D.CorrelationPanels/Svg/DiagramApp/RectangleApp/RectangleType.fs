namespace Svgplus.RectangleType

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open CorrelationDrawing

type RectangleId = RectangleId of Guid

module RectangleId =
    let createNew() = 
        Guid.NewGuid() |> RectangleId
    let invalid =
        Guid.Empty |> RectangleId
    let getValue (RectangleId id) =
        id

[<DomainType>]
type Rectangle = {
    [<NonIncremental>]
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

[<DomainType>]
type RectangleBorder = {
    [<NonIncremental>]
    id             : RectangleBorderId
    [<NonIncremental>]
    contactId      : BorderContactId

    color          : C4b
    weight         : float

    [<NonIncremental>]
    upperRectangle : RectangleId
    [<NonIncremental>]
    lowerRectangle : RectangleId
} with 
    member this.width (rectangles : hmap<RectangleId,Rectangle>) =
        let rectangleA = rectangles |> HMap.tryFind this.upperRectangle
        let rectangleB = rectangles |> HMap.tryFind this.lowerRectangle

        rectangleB
        |> Option.map2 (fun a b -> max a.dim.width b.dim.width) rectangleA  
        |> Option.defaultValue 10.0

    member this.pos (rectangles : hmap<RectangleId,Rectangle>) =
        rectangles 
        |> HMap.tryFind this.lowerRectangle 
        |> Option.map(fun x -> x.pos) 
        |> Option.defaultValue V2d.Zero