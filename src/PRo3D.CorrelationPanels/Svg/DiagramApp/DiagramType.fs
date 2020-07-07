namespace Svgplus.DA
  
open Aardvark.Base
open Aardvark.Base.Incremental
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

[<DomainType>]
type DiagramAppModel = {
    items             : hmap<DiagramItemId, DiagramItem>
    order             : plist<DiagramItemId>
    correlations      : CorrelationsModel
    selectedRectangle : option<RectangleId>
    dataRange         : Range1d
    keyboard          : KeyboardTypes.Keyboard<DiagramAppModel>
    selectedBorders   : hmap<RectangleBorderId, BorderContactId>
    bordersTable      : hmap<RectangleBorderId, RectangleBorder>
    rectanglesTable   : hmap<RectangleId, Rectangle>
    yToSvg            : float
    yScaleValue       : float
}