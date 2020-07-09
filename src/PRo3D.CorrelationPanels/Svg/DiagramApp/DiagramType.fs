namespace Svgplus.DA
  
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Svgplus
open Svgplus.RectangleType
open UIPlus
open Svgplus.DiagramItemType
open Svgplus.Correlations2

type DiagramAppAction =
    | DiagramItemMessage    of (DiagramItemId * DiagramItemAction)
    | CorrelationsMessage   of CorrelationsAction
    | MouseMove             of V2d    
    | AddItem               of DiagramItem
    | UpdateItem            of DiagramItem
    | DeleteStack           of DiagramItemId
    | MoveLeft              of DiagramItemId
    | MoveRight             of DiagramItemId
    | UpdateColour          of (Rectangle -> Rectangle) //(ColourMap * CMItemId)
    | UpdateRectangle       of (RectangleId * RectangleAction)  // bypasses regular way of media -> stack -> rect
    | SetYScaling           of float
    | UpdateXSizes          of (float -> float)
    | KeyboardMessage       of Keyboard.Action

[<ModelType>]
type DiagramAppModel = {
    items             : HashMap<DiagramItemId, DiagramItem>
    order             : IndexList<DiagramItemId>
    correlations      : CorrelationsModel
    selectedRectangle : option<RectangleId>
    dataRange         : Range1d
    keyboard          : KeyboardTypes.Keyboard<DiagramAppModel>
    selectedBorders   : HashMap<RectangleBorderId, BorderContactId>
    bordersTable      : HashMap<RectangleBorderId, RectangleBorder>
    rectanglesTable   : HashMap<RectangleId, Rectangle>
    yToSvg            : float
    yScaleValue       : float
}