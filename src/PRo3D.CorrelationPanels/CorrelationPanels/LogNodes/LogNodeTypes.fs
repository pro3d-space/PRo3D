namespace CorrelationDrawing.LogNodeTypes

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify

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

[<ModelType>]
type Border = {
    [<NonAdaptive>]
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

    [<NonAdaptive>]
    borderType  : BorderType
}

[<ModelType>]
type LogNode = {
    [<NonAdaptive>]
    id            : LogNodeId
    [<NonAdaptive>]
    rectangleId   : RectangleId
  
    logId         : RectangleStackId

    //[<NonAdaptive>]
    nodeType           : LogNodeType

    level              : NodeLevel //TODO think about this; performance vs interaction
    lBorder            : option<Border>
    uBorder            : option<Border>
    annotation         : option<ContactId>

    children           : IndexList<LogNode>
  
    mainBody           : Rectangle
    //roseDiagram        : RoseDiagram
}