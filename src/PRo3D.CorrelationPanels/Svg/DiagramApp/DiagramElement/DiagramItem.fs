namespace Svgplus
  
open Aardvark.Base
open FSharp.Data.Adaptive
open Svgplus

open Svgplus.RectangleType
open Svgplus.RectangleStackTypes
open Svgplus
open Svgplus.HeaderType
open UIPlus
open RoseDiagram
open CorrelationDrawing
open Svgplus.DiagramItemType
open Svgplus.RoseDiagramModel
open Aardvark.UI
open Adaptify.FSharp.Core

type DiagramItemAction =
    | RectangleStackMessage of RectangleStackId * RectangleStackAction
    | HeaderMessage         of HeaderAction
    | RoseDiagramMessage    of RoseDiagramId * RoseDiagramAction
    | ChangeLabel           of TextInput.Action
    | Delete
    | UpdateStack           of RectangleStackTypes.RectangleStack
    | AddSecondaryStack     of RectangleStackTypes.RectangleStack
    | MoveLeft              of RectangleStackTypes.RectangleStackId
    | MoveRight             of RectangleStackTypes.RectangleStackId
    | UpdateColour          of (Rectangle -> Rectangle) //(ColourMap * CMItemId)
    | UpdateRectangle       of RectangleId * RectangleAction
    | SetYScaling           of float
    | UpdateXSizes          of (float -> float)
    | UpdateElevationDifference of float
    | SetItemSelected       of bool
    | SetFlattenHorizonOffset of option<FlattenHorizonData>

module DiagramItem =
    open System
    
    let updateDimension (model : DiagramItem) = 
        let width = 
            let primWidth = model.primaryStack.maxWidth 
            let secondaryWidth = 
                model.secondaryStack
                |> Option.map (fun _ -> 15.0) // magic width of secondary
                |> Option.defaultValue 0.0

            // all same size
            let roseWidth = 
                model.roseDiagrams
                |> HashMap.values 
                |> Seq.first
                |> Option.map (fun x -> x.roseDiagram.outerRadius * 2.0)
                |> Option.defaultValue 0.0
                
            primWidth + secondaryWidth + roseWidth

        let height = 
            let stackHeight = model.primaryStack.stackDimensions.Y // prim = sec
            let headerHeight = model.header.dim.height
            let offset = 
                match model.flattenHorizon with
                | Some offsetData -> offsetData.offsetGlobal * model.yToSvg
                | None -> model.elevationDifference * model.yToSvg
            stackHeight + headerHeight + offset

        { model with dimension = V2d(width, height) }

    let resetPosition (model : DiagramItem) (v : V2d) =
        
        let pri = RectangleStackApp.resetPosition model.primaryStack v
        let sec = model.secondaryStack |> Option.map (fun s -> RectangleStackApp.resetPosition s v)
        
        let header = HeaderApp.Lens.pos.Set (model.header, v)
        { 
            model with 
              pos             = v
              primaryStack    = pri
              secondaryStack  = sec
              header          = header
        }
        |> updateDimension
                  
    module Lens =
        let pos =
            { new Lens<DiagramItem, Aardvark.Base.V2d>() with
                override x.Get(item) = item.pos
                override x.Set(item,v) =
                    
                    let itemHelper offset (stack : RectangleStack)  = 
                        let _x = v.X + stack.pos.X + offset 
                        let yOffset = 
                            match item.flattenHorizon with
                            | Some offsetData -> offsetData.offsetGlobal * item.yToSvg
                            | None -> item.elevationDifference * item.yToSvg // INCLUDES Y-Shift for alignment! (same scale level)
                        
                        let _y = v.Y + stack.pos.Y + item.header.dim.height + yOffset 
                        let _v = V2d (_x, _y)
                        RectangleStackApp.Lens.pos.Set (stack, _v)

                    let offset = 
                        item.secondaryStack 
                        |> Option.map (fun x -> 15.0) // MAGIC width for secondary...
                        |> Option.defaultValue 0.0
                    let _pri = item.primaryStack |> (itemHelper offset)
                    let _sec = item.secondaryStack |> Option.map (itemHelper 0.0)
                    
                    let rosePosX = _pri.pos.X + _pri.maxWidth
                    let _roses = 
                        item.roseDiagrams
                        |> HashMap.map (fun k d ->
                            let rosePosY = 
                                _sec
                                |> Option.bind (fun x -> x.rectangles |> HashMap.tryFind d.relatedRectangle)
                                |> Option.map (fun r -> r.pos.Y + (r.dim.height / 2.0)) // centered
                                |> Option.defaultValue 0.0

                            let rosePos = V2d(rosePosX, rosePosY)
                            { d with roseDiagram = RoseDiagram.update d.roseDiagram (RoseDiagramAction.UpdatePosition rosePos) })

                    let _header = HeaderApp.Lens.pos.Set (item.header, v) 
                    
                    {
                        item with  
                            primaryStack    = _pri
                            secondaryStack  = _sec
                            header          = _header
                            roseDiagrams     = _roses
                            pos             = v
                    }
                    |> updateDimension
            }
    
    let tryFindStackFromId (model : DiagramItem) (stackId : RectangleStackId) =
        match model.primaryStack.id = stackId, model.secondaryStack with
        | true, _ -> Some model.primaryStack
        | _, Some s when s.id = stackId -> Some s
        | _ -> None
        
    let createDiagramItem (prim: RectangleStack) (sec: Option<RectangleStack>) roses yToSvg (id : DiagramItemId) (contactPoint: V3d) : DiagramItem =
        {
            id                  = id
            pos                 = V2d(0.0)
            header              = HeaderApp.init
            primaryStack        = prim
            secondaryStack      = sec         
            itemDataRange       = prim.stackRange
            elevationDifference = 0.0
            yToSvg              = yToSvg
            roseDiagrams        = roses
            itemSelected        = false
            dimension           = V2d.Zero // directly fixed afterwards with updateDimension
            flattenHorizon      = None
            contactPoint        = contactPoint
        }
        |> updateDimension
    
module DiagramItemApp =

    let update (model : DiagramItem) (action : DiagramItemAction) =

        match action with
        | RectangleStackMessage (id, a) ->
            let updatedModel =
                if model.primaryStack.id = id then
                    { model with primaryStack = (RectangleStackApp.update model.primaryStack a )}
                else 
                    match model.secondaryStack with
                    | Some x when x.id = id ->
                        { model with secondaryStack = Some (RectangleStackApp.update x a )}
                    | _ -> model
            updatedModel
        | HeaderMessage m ->
            { model with header = HeaderApp.update model.header m }
        | ChangeLabel msg ->
            let header = HeaderApp.update model.header (HeaderAction.TextMessage (Text.ChangeLabel msg))
            { model with header = header }
        | Delete -> // WHY should a diagram item delete itself? this should be handled by the Diagram itself!
            { model with header = { model.header with visible = false } }     
        | AddSecondaryStack stack ->
            { model with secondaryStack = Some stack }
        | UpdateColour rectFun  ->
            let pri = RectangleStackApp.update model.primaryStack (RectangleStackAction.UpdateColour rectFun)
            let sec = model.secondaryStack |> Option.map (fun s -> RectangleStackApp.update s (RectangleStackAction.UpdateColour rectFun))
            { model with primaryStack = pri; secondaryStack = sec }
        | UpdateRectangle (id, a) ->
            let stackMessage = RectangleStackAction.RectangleMessage (id, a)

            let model = 
                match model.primaryStack.rectangles |> HashMap.tryFind(id) with
                | Some _ -> { model with primaryStack = RectangleStackApp.update model.primaryStack stackMessage }
                | None -> model

            let model =
                match model.secondaryStack |> Option.bind (fun s -> s.rectangles |> HashMap.tryFind(id)) with
                | Some _ -> { model with secondaryStack = Some (RectangleStackApp.update model.secondaryStack.Value stackMessage) }
                | None -> model
            model
        | SetYScaling scale ->
            let model = { model with yToSvg = scale}
            
            let model =
                let pri = RectangleStackApp.update model.primaryStack (RectangleStackAction.SetYScaling scale)
                { model with primaryStack = pri } 

            let model =
                let sec = model.secondaryStack |> Option.map (fun s -> RectangleStackApp.update s (RectangleStackAction.SetYScaling scale))
                { model with secondaryStack = sec }
            model |> DiagramItem.updateDimension
        | UpdateXSizes f ->
            let model =
                let pri = RectangleStackApp.update model.primaryStack (RectangleStackAction.UpdateXSizes f)
                { model with primaryStack = pri }

            let model =
                let sec = model.secondaryStack |> Option.map (fun s -> RectangleStackApp.update s (RectangleStackAction.UpdateXSizes f))
                { model with secondaryStack = sec }

            model |> DiagramItem.updateDimension
        | UpdateElevationDifference v -> 
            { model with elevationDifference = v } |> DiagramItem.updateDimension
        | RoseDiagramMessage (roseID, msg) -> 
            let updatedRoses = 
                model.roseDiagrams 
                |> HashMap.alter roseID (Option.map (fun r -> { r with roseDiagram = RoseDiagram.update r.roseDiagram msg }))   

            { model with roseDiagrams = updatedRoses }
        | SetItemSelected selectionState -> 
            { model with itemSelected = selectionState }      
        | SetFlattenHorizonOffset offset ->
            { model with flattenHorizon = offset }
        | _ -> 
            action |> sprintf "[DiagramItem] %A not implemented" |> failwith
        
    let view (stacksMaxMinRange : aval<Range1d>) (model : AdaptiveDiagramItem) =

        let stacks = 
            alist {
                // drawing order in svg is relevant! (draw first secondary than primary)
                let! se = 
                    model.secondaryStack
                    |> AVal.map (fun sec -> 
                    sec
                        |> Adaptivy.FSharp.Core.Missing.AdaptiveOption.toOption |> Option.map (fun s -> 
                            RectangleStackApp.view stacksMaxMinRange model.flattenHorizon s
                            |> UI.map (fun x -> DiagramItemAction.RectangleStackMessage(s.id, x)))
                    )

                if se.IsSome then
                    yield se.Value

                yield 
                    RectangleStackApp.view stacksMaxMinRange model.flattenHorizon model.primaryStack
                    |> UI.map (fun x -> DiagramItemAction.RectangleStackMessage(model.primaryStack.id, x))
            }

        let header =
            HeaderApp.view model.header 
            |> UIMapping.mapAList HeaderMessage

        let roses = 
            model.roseDiagrams 
            |> AMap.map (fun _ d -> RoseDiagram.view d.roseDiagram |> UI.map RoseDiagramMessage)
            |> AMap.toASet
            |> ASet.toAList
            |> AList.map snd

        let selectionRectAttr = 
            amap{
                let offset = V2d(25.0, 25.0)    // magic
                
                let! dim = model.dimension
                let yAxisTextAndBorder = V2d(10.0, 45.0)  // magic
                let extendedDim = dim + offset + yAxisTextAndBorder
                yield attribute "width" (sprintf "%fpx" extendedDim.X)
                yield attribute "height" (sprintf "%fpx" extendedDim.Y)

                let! pos = model.pos
                let shiftedPos = pos-offset
                yield attribute "x" (sprintf "%fpx" shiftedPos.X)
                yield attribute "y" (sprintf "%fpx" shiftedPos.Y)

                let! isSelected = model.itemSelected
                match isSelected with
                | true -> yield style "stroke:rgb(178, 217, 2); stroke-width:2; stroke-opacity:1.0; fill-opacity:0.0;  fill:white"
                | false -> yield style "stroke-opacity:0.0; fill-opacity:0.0; fill:white"
            } |> AttributeMap.ofAMap

        let selectionRectangle = Incremental.Svg.rect selectionRectAttr AList.empty |> AList.single
        
        // order is important for events
        let lst = 
            stacks
            |> AList.append header
            |> AList.append roses
            |> AList.append selectionRectangle 

        Incremental.Svg.g AttributeMap.empty lst