namespace CorrelationDrawing.LogTypes

open Aardvark.Base
open FSharp.Data.Adaptive

open CorrelationDrawing.Types
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.LogNodeTypes

open Svgplus.RectangleStackTypes
open Svgplus.DiagramItemType
open System

open Adaptify

type LogId = LogId of Guid

module LogId =
    let createNew() = 
        Guid.NewGuid() |> LogId
    let value id =
        let (LogId v) = id
        v
    let fromDiagramItemId (id : DiagramItemId) =
        id |> DiagramItemId.getValue |> LogId

type LogDiagramReferences = {
    itemId         : DiagramItemId
    mainLog        : RectangleStackId
    secondaryLog   : option<RectangleStackId>
}

[<ModelType>]
type GeologicalLog = {

    [<NonIncremental;PrimaryKey>]
    id              : Svgplus.RectangleStackTypes.RectangleStackId

    [<NonAdaptive>]
    diagramRef      : LogDiagramReferences
    state           : State      
    defaultWidth    : float
    nodes           : IndexList<LogNode>
    annoPoints      : HashMap<ContactId, V3d>
}

