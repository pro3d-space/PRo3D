namespace Svgplus.RoseDiagramModel

open System
open Aardvark.Base
open FSharp.Data.Adaptive

type RoseDiagramId = RoseDiagramId of Guid

module RoseDiagramId =
    let createNew() = 
        Guid.NewGuid() |> RoseDiagramId
    let invalid =
        Guid.Empty |> RoseDiagramId

type Bin = 
   {
       number : int
       value : float
       colour : C4b
       startAngle : CorrelationDrawing.Math.Angle
       endAngle : CorrelationDrawing.Math.Angle
   }

[<ModelType>]
type RoseDiagram = {
    [<NonAdaptive>]
    id            : RoseDiagramId
    outerRadius   : float
    innerRadius   : float
    countPerBin   : IndexList<Bin>
    averageAngle  : CorrelationDrawing.Math.Angle
    pos           : V2d // left-center
    maxItemsInBin : int
    totalItems    : int
}