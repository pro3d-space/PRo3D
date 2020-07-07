namespace Svgplus

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Base.Incremental.Operators
open Aardvark.UI
open Svgplus.RectangleType
open Svgplus.DA
open UIPlus
open Svgplus.DiagramItemType

type UnpackAction =
    | MouseMessage      of MouseAction
    | RectangleMessage  of RectangleAction
    | LeftArrowMessage  of Arrow.Action
    | RightArrowMessage of Arrow.Action
   
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module LeanDiagramAction =
    let OnLeftClick =
        UnpackAction.MouseMessage MouseAction.OnLeftClick
    let SelectRectangle =
        UnpackAction.RectangleMessage (RectangleAction.Select (RectangleId Guid.Empty))    
        
    let do124 (fromAction : DiagramAppAction) (toAction : UnpackAction) f (def : 'model) =
        match fromAction with 
        | DiagramItemMessage (itemId, (DiagramItemAction.HeaderMessage (HeaderAction.TextMessage (Text.MouseMessage a)))) -> 
            match a, toAction with 
            | MouseAction.OnLeftClick, (MouseMessage MouseAction.OnLeftClick) -> 
                f itemId (RectangleId Guid.Empty)
            | _ -> 
                def
        | DiagramItemMessage (itemId, (DiagramItemAction.RectangleStackMessage (stackId,ra))) -> 
            match ra with
            //| RectangleStackAction.HeaderMessage hm ->
            //    match hm with
            //    | HeaderAction.TextMessage (Text.MouseMessage MouseAction.OnLeftClick) ->
            //        match toAction with
            //        | _ -> def
            //    | HeaderAction.LeftArrowMessage a ->
            //        def
            //    | HeaderAction.RightArrowMessage a ->
            //        def
            //    | _ ->
            //        def
            | RectangleStackAction.RectangleMessage (rid, rm) ->
                def
            | _ -> 
                def  
        | _ -> 
            def

    let unpack (fromAction : DiagramAppAction) (toAction : UnpackAction) f (def : 'model) =
        match fromAction, toAction with
        | DiagramItemMessage (itemId, im), _ ->
            match im, toAction with
            | DiagramItemAction.HeaderMessage hm, _ ->
                match hm, toAction with
                | HeaderAction.TextMessage tm, toAction ->
                    match tm, toAction with 
                    | Text.MouseMessage mm, MouseMessage mm2 ->
                        match mm, mm2 with
                        | MouseAction.OnLeftClick, MouseAction.OnLeftClick -> 
                            f itemId (RectangleId Guid.Empty)                      
                        | MouseAction.OnMouseEnter, MouseAction.OnMouseEnter ->
                            def
                        | _ -> def
                    | _ -> def
                | _,_ -> def
            | DiagramItemAction.RectangleStackMessage (stackId,ra), _ -> 
                match ra, toAction with 
                //| RectangleStackAction.HeaderMessage sm, toAction ->
                //    match sm, toAction with
                //    | HeaderAction.TextMessage tm, toAction ->
                //        match tm, toAction with 
                //        | Text.MouseMessage mm, MouseMessage mm2 ->
                //            match mm, mm2 with 
                //            | MouseAction.OnLeftClick, MouseAction.OnLeftClick -> 
                //                f itemId (RectangleId Guid.Empty)
                //            | MouseAction.OnMouseEnter, MouseAction.OnMouseEnter ->
                //                def
                //            | _ -> def
                //        | _ -> def
                //    | HeaderAction.LeftArrowMessage sm, LeftArrowMessage _lam ->
                //        match sm = _lam with 
                //        | true -> f itemId (RectangleId Guid.Empty)
                //        | false -> def
                //    | HeaderAction.RightArrowMessage sm, RightArrowMessage _ram ->
                //        match sm = _ram with 
                //        | true -> f itemId (RectangleId Guid.Empty)
                //        | false -> def
                //    | _,_ -> def
                | RectangleStackAction.RectangleMessage (rid, rm), RectangleMessage rm2 -> 
                    match rm, rm2 with
                    | RectangleAction.Select rid,  RectangleAction.Select dummy ->
                        f itemId rid                   
                    | _,_ -> def
                | _ -> def       
            | _ -> def
        | _ -> def

 //let view (model : MConnection) =
 //  let actions = MouseActions.init ()
 //  alist {
 //    let! fr = model.bFrom
 //    let! t  = model.bTo
 //    let! dotted = model.dotted
 //    let dNode = 
 //      match dotted with
 //        | true ->
 //          (Incremental.drawDottedLine fr t model.colour model.weight model.dashLength model.dashDist actions)
 //        | false ->
 //          (Incremental.drawLine fr t model.colour model.weight actions)                           
   
 //    yield dNode |> UI.map MouseMessage
 //  }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DiagramApp =         
    open Svgplus.Correlations2
       
    let layoutDiagramItems (model : DiagramAppModel) =

        let setFirstItemPosition (optItem : option<DiagramItem>)  =
            Svgplus.DiagramItem.Lens.pos.Set (optItem.Value, V2d (50.0)) // margin top-left = 50,50 -> so it starts not in corner....
          
        let cleanItems items =
            items
            |> HMap.map (fun id s -> DiagramItem.resetPosition s (V2d 0.0))
            |> HMap.update 
                (model.order.Item 0) (fun opts -> setFirstItemPosition opts) //hack
                
        let f (prev : DiagramItem) (curr : DiagramItem) =
            let pos =
                let cx = 40.0 + prev.pos.X + prev.dimension.X // model.itemGap = 40
                V2d (cx, 50.0)  // margin-top

            Svgplus.DiagramItem.Lens.pos.Set (curr, pos)
        
        let items = 
            match model.order.Count > 0 with
            | true ->
                let items = cleanItems model.items                
                DS.PList.mapPrev' model.order items None f                 
            | false -> model.items
        
        { model with items = items }

    let tryFindRectangle (model: DiagramAppModel) (id: RectangleId) =

        model.items.Values 
        |> Seq.collect(fun x -> 
            seq{
                yield x.primaryStack
                if x.secondaryStack.IsSome then
                    yield x.secondaryStack.Value
            
            })
        |> Seq.map(fun x -> x.rectangles)
        |> Seq.fold(fun acc x -> x |> HMap.union acc) HMap.empty
        |> HMap.tryFind id

    let tryGetIdPair (model: DiagramAppModel) (id: RectangleId) =
        model.items.Values 
        |> Seq.collect(fun x -> 
            seq{
                yield x.primaryStack
                if x.secondaryStack.IsSome then
                    yield x.secondaryStack.Value
            } |> Seq.map (fun y -> x.id, y))
        |> Seq.map(fun (k,v) -> v.rectangles  |> HMap.map(fun _ r -> (k, v.id)))
        |> Seq.fold(fun acc x -> x |> HMap.union acc) HMap.empty
        |> HMap.tryFind id

    let updateItemFromId (model: DiagramAppModel) (id: DiagramItemId) (a: DiagramItemAction) =

        let items =
            model.items
            |> HMap.alter id (fun optm -> 
                match optm with
                | Some r -> (DiagramItemApp.update r a) |> Some
                | None -> None                
            )
        { model with items = items }

    let updateAllRectangles (a: RectangleAction) (model: DiagramAppModel) =

        let items =
            model.items
            |> HMap.map (fun _ d -> 
                { 
                    d with
                        primaryStack = { 
                            d.primaryStack with 
                                rectangles = 
                                    d.primaryStack.rectangles 
                                    |> HMap.map(fun _ r -> Rectangle.update r a)
                        }
                        secondaryStack = 
                            match d.secondaryStack with
                            | None -> d.secondaryStack
                            | Some s -> Some { s with rectangles = s.rectangles |> HMap.map(fun _ r -> Rectangle.update r a) }
                })  
        { model with items = items }

    let updateSelectedRectangle (diagram : DiagramAppModel) (itemId  : DiagramItemId) (action  : DiagramItemAction) =
        
        match action with
        | DiagramItemAction.RectangleStackMessage (stackId , RectangleStackAction.RectangleMessage (rectId, RectangleAction.Select rid)) -> 

            let diagram =
                diagram |> updateAllRectangles RectangleAction.Deselect

            { diagram with selectedRectangle = Some (rid) }

            //match diagram.selectedRectangle with
            //| Some selected when selected = rid ->                
            //    { diagram with selectedRectangle = None }
            //| _ ->
                
        | _ -> 
            diagram
        
    let moveItemLeft (model: DiagramAppModel) id = 
        let _order = 
            model.order 
            |> PList.toList
            |> DS.List.shiftLeft id 
            |> PList.ofList

        {model with order = _order }

    let moveItemRight (model: DiagramAppModel) id =
        let _order = 
            model.order 
            |> PList.toList
            |> DS.List.shiftRight id 
            |> PList.ofList

        {model with order = _order }  

    let updateBordersAndRectangles model =
        let bordersTable =
            model.items.Values
            |> Seq.collect(fun x -> 
                let prim = x.primaryStack.borders.Values |> Seq.map(fun y -> y.id, y)
                let sec = x.secondaryStack |> Option.map (fun s -> s.borders.Values |> Seq.map (fun y -> y.id, y)) |> Option.defaultValue Seq.empty
                Seq.append prim sec
            ) |> HMap.ofSeq

        let rectanglesTable =
            model.items.Values
            |> Seq.collect(fun x -> 
                let prim = x.primaryStack.rectangles.Values |> Seq.map(fun y -> y.id, y)
                let sec = x.secondaryStack |> Option.map (fun s -> s.rectangles.Values |> Seq.map (fun y -> y.id, y)) |> Option.defaultValue Seq.empty 
                Seq.append prim sec
            ) |> HMap.ofSeq

        { model with bordersTable = bordersTable; rectanglesTable = rectanglesTable }

    let updateDiagramItem diagramItemId model a =
        {
            model with 
                items = 
                    model.items
                    |> HMap.alter diagramItemId (fun item ->
                        match item with
                        | Some r -> Some (DiagramItemApp.update r a)
                        | None   -> None
                    )
               // connectionApp   = updateConnections model a
        }            

    let findStackFromId (model : DiagramAppModel) sid =
        let foundmany =
            model.items
            |> HMap.map (fun key item -> 
                (DiagramItem.tryFindStackFromId item sid)
                |> Option.map (fun x -> (key, x))
            )
            |> DS.HMap.filterNone

        let rectangleStack =
            foundmany
            |> DS.HMap.values
            |> List.tryHead

        rectangleStack

    let updateDiagram (model : DiagramAppModel) sid actionParams =
        let opt = findStackFromId model sid
        let items = 
            match opt with
            | Some (iid, stack) -> 
                let itemMessage = DiagramItemAction.RectangleStackMessage actionParams
                model.items 
                |> HMap.update iid (fun item -> 
                    match item with
                    | Some r -> DiagramItemApp.update r itemMessage
                    | None   -> failwith "invalid diagramItemId")
            | None -> model.items
        { model with items = items }

    let deselectAllRectangleBorders model =
        let updatedItems =
            model.items 
            |> HMap.map(fun _ v ->                                
                DiagramItemAction.RectangleStackMessage(
                    v.primaryStack.id, RectangleStackAction.DeselectBorder
                ) |> DiagramItemApp.update v
            )

        { model with selectedBorders = HMap.empty; items = HMap.union model.items updatedItems }      

    let update (msg: DiagramAppAction) (model: DiagramAppModel) =
                
        let model =
            match msg with
            | AddItem r ->
                let order = model.order.Append r.id
                let newRange = Range1d(model.dataRange, r.itemDataRange)

                let updatedItems =
                    model.items.Add (r.id,r)
                    |> HMap.map (fun _ i -> 
                       let diff = newRange.Max-i.itemDataRange.Max
                       Log.warn "Difference is %A" diff
                       DiagramItemApp.update i (DiagramItemAction.UpdateElevationDifference diff)
                    )                
                {
                    model with
                        items = updatedItems
                        order = order
                        dataRange = newRange
                }             
            | UpdateItem diagramItem ->
                let newRange = Range1d(model.dataRange, diagramItem.itemDataRange)

                let updatedItems =
                    model.items 
                    |> HMap.alter diagramItem.id (function
                        | Some item ->                             
                            (newRange.Max - item.itemDataRange.Max)
                            |> DiagramItemAction.UpdateElevationDifference
                            |> DiagramItemApp.update item
                            |> Some                            
                        | None -> None                    
                    )                    
                {
                    model with
                        items = updatedItems
                        dataRange = newRange
                }
            | DeleteStack sid ->
                let order = model.order.Remove sid
                let newItems = model.items.Remove sid
                
                let newRange = 
                    newItems 
                    |> HMap.values 
                    |> Seq.map(fun x -> x.itemDataRange) 
                    |> Seq.fold (fun s x -> Range1d(s,x)) Range1d.Invalid
                   
                let items = 
                    newItems 
                    |> HMap.map (fun _ a -> 
                        let diff = newRange.Max-a.itemDataRange.Max
                        DiagramItemApp.update a (DiagramItemAction.UpdateElevationDifference diff))
                {
                    model with
                        items = items
                        order = order
                        dataRange = newRange
                }             
            | DiagramItemMessage (diagramItemId, a) ->
                let model = 
                    match a with 
                    | DiagramItemAction.HeaderMessage hm ->
                        match hm with
                        | HeaderAction.LeftArrowMessage (Arrow.MouseMessage (MouseAction.OnLeftClick)) ->    
                            moveItemLeft model diagramItemId                                
                        | HeaderAction.RightArrowMessage (Arrow.MouseMessage (MouseAction.OnLeftClick)) ->                            
                            moveItemRight model diagramItemId
                        | _ -> model
                    | _ -> model
                                                              
                let model = updateSelectedRectangle model diagramItemId a

                let model = updateDiagramItem diagramItemId model a                
               
                match a with
                | RectangleStackMessage(_, SelectBorder _) ->
                    let selectedBorders =
                        model.items.Values 
                        |> Seq.choose(fun x -> x.primaryStack.selectedBorder)          
                        |> Seq.choose(fun x -> model.bordersTable |> HMap.tryFind x)
                        |> Seq.map(fun x ->
                            x.id, x.contactId
                        ) |> HMap.ofSeq
                        
                    
                    //Log.line "[DiagramApp] selected borders %A" (blurg |> HSet.toList)

                    { model with selectedBorders = selectedBorders }
                | RectangleStackMessage(_, RectangleStackAction.RectangleMessage (_, (RectangleAction.Select id))) ->
                    match model.selectedRectangle with
                    | Some rectId ->                         
                        //find the two rectangle borders
                        let borders = 
                            model.bordersTable.Values 
                            |> Seq.filter(fun x -> x.lowerRectangle = rectId || x.upperRectangle = rectId)            
                            |> Seq.map(fun x -> x.contactId)
                            |> Seq.pairwise                            
                            |> Seq.tryHead                        

                        match borders with
                        | Some (left, right)->
                            Log.line "found contacts %A %A" left right
                            model
                        | _ -> model                       
                    | None ->
                        model
                | _ -> model              
            //| ConnectionMessage msg -> 
            //    //{ model with connectionApp = ConnectionApp.update model.connectionApp msg}
            //    model
            | MouseMove p -> 
                //let _conApp = 
                //    ConnectionApp.update 
                //        model.connectionApp 
                //        (ConnectionApp.Action.MouseMoved p)
                //{ model with connectionApp = _conApp }
                model
            | MoveLeft id ->
                moveItemLeft model id
            | MoveRight id ->
                moveItemRight model id
            | UpdateColour rectFun -> //(cmap, _id) ->
                let stacks =
                    model.items
                    |> HMap.map (fun id r -> 
                      DiagramItemApp.update r (DiagramItemAction.UpdateColour rectFun)
                    ) //(cmap, _id)))
                {model with items = stacks}
            | UpdateRectangle (id, a) ->
                let ids = tryGetIdPair model id
                match ids with
                | Some (diagramId, stackId) ->
                    let stackMessage = 
                        DiagramItemAction.RectangleStackMessage
                            (stackId, RectangleStackAction.RectangleMessage (id, a))
                
                    updateItemFromId model diagramId stackMessage
                | None -> model
            | SetYScaling scaleValue ->
                let yPixelPerUnitSvg = Math.Pow(2.0, scaleValue)
                Log.warn "[ScalingStuff] by %f" yPixelPerUnitSvg

                let stacks =
                    model.items
                    |> HMap.map (fun _ r -> DiagramItemApp.update r (DiagramItemAction.SetYScaling yPixelPerUnitSvg))

                { model with yScaleValue = scaleValue; yToSvg = yPixelPerUnitSvg; items = stacks } |> layoutDiagramItems
            | UpdateXSizes f ->
                let stacks =
                    model.items
                    |> HMap.map (fun _ r -> DiagramItemApp.update r (DiagramItemAction.UpdateXSizes f))
                { model with items = stacks } |> layoutDiagramItems     
            | KeyboardMessage m ->
                //Log.line "DiagramApp received kbmsg %A" m
                let (_kb, _model) = Keyboard.update model.keyboard model m
                {_model with keyboard = _kb}
            | CorrelationsMessage a -> 
                // update correlations first
                let model = { model with correlations = CorrelationsApp.update model.correlations a}
                // fix diagram afterwards
                match a with 
                | CorrelationsAction.FlattenHorizon correlationId ->
                    let referenceCorrelation = model.correlations.correlations |> HMap.find correlationId
                    let itemOffsets = 
                        referenceCorrelation.contacts 
                        |> HMap.choose (fun rectanlgeBorderId borderContactId ->
                            let rectangelBorder = model.bordersTable |> HMap.find rectanlgeBorderId
                            let upperRectangleId = rectangelBorder.upperRectangle 
                            match tryGetIdPair model upperRectangleId with 
                            | Some (itemId, stackId) -> 
                                let diagramItem = model.items |> HMap.find itemId
                                    
                                let stack =
                                    match diagramItem.primaryStack, diagramItem.secondaryStack with
                                    | prim, _ when prim.id = stackId -> prim
                                    | _, Some sec when sec.id = stackId -> sec
                                    | _ -> failwith "cannot happen...tryGetIdPair provides a valid stackId"

                                let height, _ =
                                    stack.order 
                                    |> List.fold (fun (distance, finished) rectangleId ->
                                        match finished with
                                        | true -> (distance, finished)
                                        | false -> 
                                            let rectHeight = 
                                                stack.rectangles 
                                                |> HMap.find rectangleId 
                                                |> fun x -> x.worldHeight
                                            (distance + rectHeight, rectangleId = upperRectangleId) // stop adding heights after visiting the upperRectangle
                                        ) (0.0, false)
                                Some (itemId, height) // total height from stack-begin to referenzCorrelation
                            | None -> None)

                    let items = itemOffsets |> HMap.values
                    if items.IsEmpty() then
                        Log.warn "Flatten horizon failed to detect offsets!" // TODO (this happens when a correlation is deleted and re-created directly afterwards)
                        model
                    else
                        let maxOffset = items |> Seq.map snd |> Seq.max
                        let offsetList = items |> Seq.map (fun (id, offset) -> id, maxOffset - offset)

                        let updatedItems = 
                            items
                            |> Seq.fold (fun oldMap (diagramItemId, offset) -> 
                            oldMap 
                            |> HMap.alter diagramItemId (fun optionItem ->
                                optionItem |> Option.map (fun item -> { item with flattenHorizon = Some { offsetGlobal = maxOffset - offset; offsetFromStackTop = offset }}))
                            ) model.items                        
                        { model with items = updatedItems }
                | CorrelationsAction.DefaultHorizon -> 
                    let updatedItems = model.items |> HMap.map (fun _ item -> {item with flattenHorizon = None })
                    { model with items = updatedItems }
                | CorrelationsAction.Select _ ->
                                        
                    //Actual selection is handled in the general correlation app update

                    let selectedCorrelation = 
                        model.correlations.selectedCorrelation
                        |> Option.bind(fun x ->
                            model.correlations.correlations |> HMap.tryFind x
                        )

                    match selectedCorrelation with
                    | Some c ->
                        Log.line "[DiagramApp] selecting correlation %A" c
                        let updatedItems = 
                            c.contacts.Keys 
                            |> HSet.toList
                            |> List.choose(fun rectangleBorderId -> 

                                //retrieve diagramItemId and rectangleStackId for rectangleBorderId
                                model.items 
                                |> HMap.toList
                                |> List.choose(fun (itemId, item) ->
                                    (item.primaryStack.borders |> HMap.tryFind rectangleBorderId)
                                    |> Option.map(fun _ -> (itemId, item.primaryStack.id))
                                ) 
                                |> List.tryHead                                
                                |> Option.map(fun (diaId,stackId) -> diaId, stackId, rectangleBorderId)
                            )
                            |> List.choose(fun (diaId,stackId,borderId) ->
                                //update diagramitems with selectborder action
                                model.items 
                                |> HMap.tryFind diaId
                                |> Option.map(fun x -> 
                                    DiagramItemAction.RectangleStackMessage(
                                        stackId, RectangleStackAction.SelectBorder(borderId, true)
                                    ) 
                                    |> DiagramItemApp.update x 
                                )
                            )
                            |> List.map(fun x -> (x.id, x))
                            |> HMap.ofList
                                                                                    
                        { model with selectedBorders = c.contacts; items = HMap.union model.items updatedItems }
                    | None ->
                        model |> deselectAllRectangleBorders
                | CorrelationsAction.Delete _ ->
                    model |> deselectAllRectangleBorders                  
                | CorrelationsAction.Create _ ->
                    model |> deselectAllRectangleBorders                                        
                | _ -> model
        
        model 
        |> layoutDiagramItems
        |> updateBordersAndRectangles
                    
    let updateRectangle model (rectangleId : RectangleId) a =
        let ids = tryGetIdPair model rectangleId
        match ids with
        | Some (diagramId, stackId) ->     
            let msg =
                DiagramItemMessage (diagramId, 
                    DiagramItemAction.RectangleStackMessage (stackId, 
                        (RectangleStackAction.RectangleMessage (rectangleId, a)
                    )
                ))
            model |> update msg
        | None -> model
    
    let view (model : MDiagramAppModel) =

        let stacks = 
            alist {
                let ranges = model.dataRange
                for id in model.order do
                    let! item = AMap.find id model.items

                    yield
                        DiagramItemApp.view ranges item
                        |> UI.map (fun x -> DiagramAppAction.DiagramItemMessage(id, x))
            }                

        let correlationlines = 
            CorrelationsApp.viewCorrelationsSVG model.correlations model.bordersTable model.rectanglesTable  
            |> UI.map (fun x -> DiagramAppAction.CorrelationsMessage x)
            |> AList.single
                        
        let distanceLabels = 
            model.order
            |> AList.map (fun x -> 
                model.items 
                |> AMap.find x 
                |> Mod.map (fun item -> 
                    let pos = 
                        Mod.map2 (fun c (d:CorrelationDrawing.Size2D) -> 
                            let offset = V2d(d.width / 2.0, 0.0)
                            (c-offset, c+offset)
                        ) item.header.centre item.header.dim
                    let posAndPoint = Mod.map2 (fun p c -> p, c) pos item.contactPoint
                    posAndPoint |> Mod.toAList)
                |> Mod.toAList
                |> AList.concat
            )
            |> AList.concat
            |> PRo3D.Base.AList.pairwise
            |> AList.map (fun (a, b) -> 
                let (aLeft, aRight), aPoint = a
                let (bLeft, bRight), bPoint = b
                
                let labelPos = (aRight + bLeft) / 2.0 + V2d(0.0, -2.0) 
                let labelText = sprintf "%.2fm" (V3d.Distance (aPoint, bPoint))
                let label = Svgplus.Base.drawText' labelPos labelText CorrelationDrawing.Orientation.Horizontal CorrelationDrawing.TextAnchor.Middle
                
                let lineStart = aRight + V2d(4.0, 0.0)
                let lineEnd = bLeft + V2d(-4.0, 0.0)
                let line = Svgplus.Base.drawLine lineStart lineEnd C4b.Black 2.0

                let tickOffset = V2d(0.0,3.0)
                let lineEnd = Svgplus.Base.drawLine (lineEnd+tickOffset) (lineEnd-tickOffset) C4b.Black 1.5
                let lineStart = Svgplus.Base.drawLine (lineStart+tickOffset) (lineStart-tickOffset) C4b.Black 1.5
                
                Incremental.Svg.g AttributeMap.empty ([ line; lineEnd; lineStart; label] |> AList.ofList)
            )

        let lst =             
            correlationlines
            |> AList.append stacks
            |> AList.append distanceLabels

        Incremental.Svg.g AttributeMap.empty lst
  
    let createCorrelation (model : DiagramAppModel) =
        
        //edit select or create new from selected borders
        match model.correlations.selectedCorrelation with
        | Some correlationId ->
            model |> update (CorrelationsMessage(CorrelationsAction.Edit(correlationId, model.selectedBorders)))
        | None ->
            model |> update (CorrelationsMessage(CorrelationsAction.Create(model.selectedBorders)))
        
        //let corr = 
        //    CorrelationsApp.update 
        //        model.correlations 
        //        (model.selectedBorders |> CorrelationsAction.Create)

        //{ model with correlations = corr; selectedBorders = HMap.empty }
    
    let init : DiagramAppModel =

        let keyboard =
                Keyboard.init ()
                |> (Keyboard.register {
                    update = (fun (model : DiagramAppModel) -> 
                        model |> update (DiagramAppAction.SetYScaling(model.yScaleValue + 0.1)))
                    key    = Aardvark.Application.Keys.OemPlus
                    ctrl   = false
                    alt    = false
                })
                |> (Keyboard.register {
                    update = (fun (model : DiagramAppModel) -> 
                        model |> update (DiagramAppAction.SetYScaling(model.yScaleValue - 0.1)))
                    key    = Aardvark.Application.Keys.OemMinus
                    ctrl   = false
                    alt    = false
                })|> (Keyboard.register {
                    update = (fun (model : DiagramAppModel) -> 
                        model |> update (DiagramAppAction.SetYScaling(model.yScaleValue + 0.1)))
                    key    = Aardvark.Application.Keys.Add
                    ctrl   = false
                    alt    = false
                })|> (Keyboard.register {
                    update = (fun (model : DiagramAppModel) -> 
                        model |> update (DiagramAppAction.SetYScaling(model.yScaleValue - 0.1)))
                    key    = Aardvark.Application.Keys.Subtract
                    ctrl   = false
                    alt    = false
                })
                |> (Keyboard.register {
                    update = createCorrelation
                    key    = Aardvark.Application.Keys.Enter
                    ctrl   = false
                    alt    = false
                })

        {
            items              = HMap.empty
            order              = PList.empty            
            correlations       = { version = CorrelationsModel.current; correlations =  HMap.empty; selectedCorrelation = None; alignedBy = None } //CorrelationsModel.init
            selectedRectangle  = None
            dataRange          = Range1d.Invalid
            selectedBorders    = HMap.empty
            bordersTable       = HMap.empty
            rectanglesTable    = HMap.empty
            yToSvg             = Math.Pow(2.0, 5.0) // must be the same scale func...
            yScaleValue        = 5.0
            keyboard           = keyboard
        }