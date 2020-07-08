namespace Svgplus.DiagramItemType
  
open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Svgplus.RectangleStackTypes
open Svgplus.RoseDiagramModel
open Svgplus

type DiagramItemId = private DiagramItemId of Guid

module DiagramItemId =
    let createFrom(id : Guid) =
        id |> DiagramItemId
    let getValue (DiagramItemId guid) =
        guid

[<ModelType>]
type RoseDiagramRelated = 
    {
        relatedRectangle : Svgplus.RectangleType.RectangleId
        roseDiagram : RoseDiagram
    }

[<ModelType>]
type DiagramItem = {
    id                  : DiagramItemId
                        
    pos                 : V2d
    header              : HeaderType.Header
                        
    primaryStack        : RectangleStack
    secondaryStack      : option<RectangleStack>

    itemSelected        : bool
    dimension           : V2d
                        
    itemDataRange       : Range1d
    contactPoint        : V3d

    elevationDifference : float         // in units
    flattenHorizon      : option<FlattenHorizonData> // in units - alternative mode with offset in regard to a reference correlation
    
    yToSvg              : float         // scale for elevationDifference and flattenOffset
    roseDiagrams        : HashMap<RoseDiagramId, RoseDiagramRelated>
}