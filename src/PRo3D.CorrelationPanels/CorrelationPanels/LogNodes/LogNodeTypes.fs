namespace CorrelationDrawing.LogNodeTypes

open System

open Aardvark.Base
open Aardvark.Base.Incremental

open Svgplus.RectangleStackTypes
open Svgplus.RectangleType

open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.Types

type LogNodeId = LogNodeId of Guid  

module LogNodeId =
    let createNew () =
        Guid.NewGuid() |> LogNodeId
    let invalid =
        Guid.Empty |> LogNodeId
    let getValue (LogNodeId id) =
        id

type BorderId  = BorderId  of Guid

type BorderType  = PositiveInfinity | NegativeInfinity | Normal | Invalid

[<DomainType>]
type Border = {
    [<NonIncremental>]
    id            : BorderId
    nodeId        : LogNodeId
    logId         : RectangleStackId
    isSelected    : bool
    correlation   : Option<BorderId>
    contactId     : ContactId
    point         : V3d
    color         : C4b
    weight        : double
    svgPosition   : V2d

    [<NonIncremental>]
    borderType  : BorderType
}

[<DomainType>]
type LogNode = {
    [<NonIncremental>]
    id            : LogNodeId
    [<NonIncremental>]
    rectangleId   : RectangleId
  
    logId         : RectangleStackId

    //[<NonIncremental>]
    nodeType           : LogNodeType

    level              : NodeLevel //TODO think about this; performance vs interaction
    lBorder            : option<Border>
    uBorder            : option<Border>
    annotation         : option<ContactId>

    children           : plist<LogNode>
  
    mainBody           : Rectangle
    //roseDiagram        : RoseDiagram
}