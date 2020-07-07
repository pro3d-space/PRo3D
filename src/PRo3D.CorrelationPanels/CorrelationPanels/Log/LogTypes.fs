namespace CorrelationDrawing.LogTypes

open Aardvark.Base
open Aardvark.Base.Incremental

open CorrelationDrawing.Types
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.LogNodeTypes

open Svgplus.RectangleStackTypes
open Svgplus.DiagramItemType
open System

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

[<DomainType>]
type GeologicalLog = {

    [<NonIncremental;PrimaryKey>]
    id              : Svgplus.RectangleStackTypes.RectangleStackId

    [<NonIncremental>]
    diagramRef      : LogDiagramReferences
    state           : State      
    defaultWidth    : float
    nodes           : plist<LogNode>
    annoPoints      : hmap<ContactId, V3d>
}

