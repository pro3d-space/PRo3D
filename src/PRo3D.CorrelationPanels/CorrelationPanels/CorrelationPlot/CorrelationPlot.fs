namespace CorrelationDrawing

open System
open FSharp.Data.Adaptive
open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open UIPlus
open Aardvark.UI.Primitives
open Svgplus.DA
open Svgplus
open Svgplus.RectangleStackTypes
open Svgplus.RectangleType
open UIPlus.KeyboardTypes
open Svgplus.DiagramItemType

open CorrelationDrawing
open CorrelationDrawing.Types
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.SemanticTypes
open CorrelationDrawing.LogNodeTypes
open CorrelationDrawing.LogTypes
open CorrelationDrawing.Model
open CorrelationDrawing.Nuevo
open PRo3D
open PRo3D.Base
open Svgplus
open Svgplus.Correlations2
open DS

module LegacyLogHelpers =

    let tryFindLog (model : CorrelationPlotModel) (logId : DiagramItemId) =
        HashMap.tryFind logId model.logs
    
    let tryFindNodeFromRectangleId (model : CorrelationPlotModel) (rid : RectangleId) =                    
        DiagramApp.tryGetIdPair model.diagram rid
            |> Option.bind(fun (diagramId,_) -> tryFindLog model diagramId)
            |> Option.bind (fun lo -> 
                let on = GeologicalLog.findNodeFromRectangleId lo rid
                Option.map (fun n -> (n, lo)) on
            ) 
    
    let tryFindNode (model : CorrelationPlotModel) (logId : DiagramItemId) (nodeId : LogNodeId) =
        let optLog = tryFindLog model logId
        Option.bind (fun log -> 
            GeologicalLog.findNode log (fun n -> n.id = nodeId)) optLog
    
    let tryFindNodeFromRectId (model  : CorrelationPlotModel) (logId  : DiagramItemId) (rectId : RectangleId) =
        let optLog = tryFindLog model logId
        Option.bind (fun log -> 
            GeologicalLog.findNode log (fun n -> n.rectangleId = rectId)) optLog
        
    let getPointsOfLog (model : CorrelationPlotModel) (logId : DiagramItemId) =
        let opt = HashMap.tryFind logId model.logs
        match opt with
        | Some log -> log.annoPoints
        | None     -> HashMap.empty

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CorrelationPlotApp =
               
    let addLogToDiagram 
        (contacts    : ContactsTable)
        (semanticApp : SemanticsModel)
        (config      : DiagramConfig)
        (colourMap   : ColourMap)
        (model       : CorrelationPlotModel)
        (log         : GeologicalLogNuevo) =

        let (item,log2) = 
            LogToDiagram.createDiagram log semanticApp contacts colourMap config

        let diagram =
            model.diagram |> DiagramApp.update (DiagramAppAction.AddItem item)

        { model with 
            diagram   = diagram
            logsNuevo = model.logsNuevo |> HashMap.add log.id log 
            logs      = model.logs |> HashMap.add log2.diagramRef.itemId log2
        }

    let updateLogDiagram
        (contacts    : ContactsTable)
        (semanticApp : SemanticsModel)
        (config      : DiagramConfig)
        (colourMap   : ColourMap)
        (log         : GeologicalLogNuevo) 
        (model       : CorrelationPlotModel) =

        let (item,log2) = 
            LogToDiagram.createDiagram log semanticApp contacts colourMap config

        let diagram =
            model.diagram 
            |> DiagramApp.update (DiagramAppAction.UpdateItem item)

        { 
            model with 
                diagram = diagram
                logsNuevo = 
                    model.logsNuevo 
                    |> HashMap.alter log.id (function | Some _ -> Some log | None -> None)
                logs = 
                    model.logs 
                    |> HashMap.alter log2.diagramRef.itemId (function | Some _ -> Some log2 | None -> None)
        }

    let createNewLog 
        (contacts    : ContactsTable) 
        (semanticApp : SemanticsModel) 
        (planet      : PRo3D.Base.Planet)
        (colourMap   : ColourMap)        
        (model       : CorrelationPlotModel) =          

        if contacts |> HashMap.isEmpty then        
            Log.error "[CorrelationPlot] Creating log failed. There are no annotations."
            model
        else
            let id = LogId.createNew()
            let log = 
                GeologicalLogNuevo.createLog 
                    id 
                    model.param_selectedPoints 
                    model.param_referencePlane 
                    model.param_referenceScale
                    contacts
                    semanticApp 
                    planet
                    
            Log.line "[CorrelationPlot] adding new log with id %A" log.id

            let config : DiagramConfig = 
                {
                    elevationZeroHeight = model.elevationZeroHeight
                    yToSvg              = model.diagram.yToSvg
                    defaultWidth        = model.defaultWidth
                    infinityHeightPixel = 10.0
                    defaultBorderColor  = C4b.Black
                    up                  = model.upVector
                    north               = model.northVector
                }
                
            log |> addLogToDiagram contacts semanticApp config colourMap model            
    
    let rec repairContacts 
        (pivotBordersTable : HashMap<BorderContactId, list<RectangleBorderId>>)
        (contacts          : list<BorderContactId>)
        : list<RectangleBorderId * BorderContactId> * HashMap<BorderContactId, list<RectangleBorderId>> =

        match contacts with            
        | contact :: restOfContacts ->
            match pivotBordersTable |> HashMap.tryFind contact with
            | Some (rect :: restOfBorders) ->

                let updatedBordersTable =
                    pivotBordersTable 
                    |> HashMap.update contact (
                        function
                        | Some _ -> restOfBorders
                        | None -> []                    
                    )
                
                let result, table = repairContacts updatedBordersTable restOfContacts

                ((rect, contact) :: result), table
            | _ -> [], pivotBordersTable
        | _ -> [], pivotBordersTable
        
    let mkCorrelation id (input : list<RectangleBorderId * BorderContactId>) =
        {
            version  = Correlation.current
            id       = id
            contacts = input |> HashMap.ofList
        }

    let rec repairCorrelations 
        (pivotBordersTable : HashMap<BorderContactId, list<RectangleBorderId>>)
        (correlations      : list<Correlation>)
        : list<Correlation> * HashMap<BorderContactId, list<RectangleBorderId>> =

        match correlations with 
        | c :: cs -> 
            let result, table = 
                c.contacts
                |> HashMap.values
                |> Seq.toList  
                |> repairContacts pivotBordersTable

            let c' = 
                result |> mkCorrelation c.id

            // recursion
            let result2, table2 =
                repairCorrelations table cs
            
            c' :: result2, table2
        | [] -> [], pivotBordersTable
        
    let reconstructCorrelations model =

        let pivotTable = 
            model.diagram.bordersTable        
            |> HashMap.map(fun _ v -> v.contactId)
            |> HashMap.pivot      
        
        model.diagram.correlations.correlations
        |> HashMap.values
        |> Seq.toList
        |> repairCorrelations pivotTable           
    
    let diagramConfigFrom(model : CorrelationPlotModel) =        
        {
            elevationZeroHeight = model.elevationZeroHeight
            yToSvg              = model.diagram.yToSvg
            defaultWidth        = model.defaultWidth
            infinityHeightPixel = 10.0
            defaultBorderColor  = C4b.Black
            up                  = model.upVector
            north               = model.northVector
        }        

    let updateLog  index (message : GeologicalLogAction) (logs : HashMap<DiagramItemId, GeologicalLog>) =
        HashMap.update index (fun (x : option<GeologicalLog>) -> GeologicalLog.update x.Value message) logs//hack   
    
    let updateColoursFromCMap model rectangle =
       match model.colorMap.mappings |> HashMap.tryFind rectangle.grainSize.grainType with
       | Some c -> { rectangle with colour = c.colour }
       | None   -> rectangle

    let magicRectangleFunction model rid =
        match DiagramApp.tryFindRectangle model.diagram rid with
        | Some rect ->
            let optn = LegacyLogHelpers.tryFindNodeFromRectangleId model rect.id
            match optn with
            | Some (n, log) ->
                                                      
                let setGrainSize = RectangleAction.SetGrainSize rect.grainSize // fixes also width!
                let changeColor = RectangleAction.UpdateColour (updateColoursFromCMap model)
                let setUncertainty = RectangleAction.SetUncertainty (rect.isUncertain)         
                
                // TODO v5: correlations
                let id = failwith "" // n.id

                // TODO..why is this stuff duplicated in logs and diagram?
                let logs  =
                    model.logs
                    |> updateLog log.diagramRef.itemId (GeologicalLogAction.LogNodeMessage (id, LogNodes.RectangleMessage changeColor))
                    |> updateLog log.diagramRef.itemId (GeologicalLogAction.LogNodeMessage (id, LogNodes.RectangleMessage setGrainSize))
                    |> updateLog log.diagramRef.itemId (GeologicalLogAction.LogNodeMessage (id, LogNodes.RectangleMessage setUncertainty))

                let diagram =
                    model.diagram
                    |> DiagramApp.update (DiagramAppAction.UpdateRectangle (rect.id, setGrainSize))
                    |> DiagramApp.update (DiagramAppAction.UpdateRectangle (rect.id, changeColor))
                    |> DiagramApp.update (DiagramAppAction.UpdateRectangle (rect.id, setUncertainty))
                
                {
                    model with
                        logs = logs
                        diagram = diagram                
                }
            | None -> model
        | None -> model

    let reconstructDiagramsFromLogs 
        (contacts    : ContactsTable)
        (semanticApp : SemanticsModel)
        (colourMap   : ColourMap)        
        (model       : CorrelationPlotModel) =

        Log.line "[CorrelationPlot] found %d logs" model.logsNuevo.Count
        
        let model = { model with logs = HashMap.empty }
        let config = model |> diagramConfigFrom
        
        let model =
            model.logsNuevo 
            |> HashMap.values
            |> Seq.fold(fun acc log ->
                log |> addLogToDiagram contacts semanticApp config colourMap acc
            ) model                            

        let correlations, table = 
            model 
            |> reconstructCorrelations

        let correlations =
            correlations |> List.map(fun x -> x.id, x) |> HashMap.ofList

        let diagram = 
            { model.diagram with correlations = { model.diagram.correlations with correlations = correlations } }
            |> DiagramApp.layoutDiagramItems 
            |> DiagramApp.updateBordersAndRectangles     
                    
        diagram.rectanglesTable
        |> HashMap.keys
        |> Seq.toList 
        |> List.fold magicRectangleFunction model                    
               
    let toDiagramItemId (logId : LogId) =
        logId |> LogId.value |> DiagramItemId.createFrom

    let toLogId (itemId : DiagramItemId) =
        itemId |> DiagramItemId.getValue |> LogId.LogId

    let updateSelectedFacies (updateFun : Facies -> Facies) (model : CorrelationPlotModel) =
        let log = 
            model.selectedLogNuevo 
            |> Option.bind(fun x ->
                model.logsNuevo 
                |> HashMap.tryFind x
            )

        match log, model.selectedFacies with
        | Some l, Some faciesId ->
            //update facies
            let facies =
                l.facies
                |> Facies.updateFacies faciesId updateFun

            //update log            
            {
                model with 
                    logsNuevo =
                        model.logsNuevo 
                        |> HashMap.alter l.id (function 
                            | Some _ -> Some { l with facies = facies } 
                            | None   -> None
                        )
            }                    
        | _ -> 
            model

    let updateFacies (logId : LogId) (faciesId : FaciesId) (updateFun : Facies -> Facies) (model : CorrelationPlotModel) =
        let log = 
            model.logsNuevo 
            |> HashMap.tryFind logId
    
        match log with
        | Some l ->
            //update facies
            let facies =
                l.facies
                |> Facies.updateFacies faciesId updateFun
    
            //update log            
            {
                model with 
                    logsNuevo =
                        model.logsNuevo 
                        |> HashMap.alter l.id (function 
                            | Some _ -> Some { l with facies = facies } 
                            | None   -> None
                        )
            }                    
        | _ -> 
            model

    

                
//  ///////////////////////////////////////////////////////// UPDATE ////////////////////////////////////////////////////
                    
    

    let updateDiagramItemSelection model logId (overwrite : bool) = 
        match model.selectedLogNuevo with
        | Some oldLog when oldLog = logId && (not overwrite) ->
            // deselect
            let newItemId = logId |> toDiagramItemId
            let diagram = model.diagram |> DiagramApp.update (DiagramAppAction.DiagramItemMessage(newItemId, (DiagramItemAction.SetItemSelected false)))
            { model with selectedLogNuevo = None; diagram = diagram }  
        | Some oldLog ->   
            // deselect old & select new (overwrite)
            let newItemId = logId |> toDiagramItemId
            let oldItemId = oldLog |> toDiagramItemId
            let diagram = 
                model.diagram 
                |>  DiagramApp.update (DiagramAppAction.DiagramItemMessage(oldItemId, (DiagramItemAction.SetItemSelected false)))
                |>  DiagramApp.update (DiagramAppAction.DiagramItemMessage(newItemId, (DiagramItemAction.SetItemSelected true)))
            { model with selectedLogNuevo = Some logId; diagram = diagram }
        | None ->
            // select
            let newItemId = logId |> toDiagramItemId
            let diagram = model.diagram |>  DiagramApp.update (DiagramAppAction.DiagramItemMessage(newItemId, (DiagramItemAction.SetItemSelected true)))
            { model with selectedLogNuevo = Some logId; diagram = diagram } 

    

    let updateDiagramItemSelection' logId overwrite model  = 
        updateDiagramItemSelection model logId overwrite

    let update 
        (annotations : ContactsTable) 
        (semApp      : SemanticsModel) 
        (planet      : Planet)
        (model       : CorrelationPlotModel) 
        (action      : CorrelationPlotAction) = 
                
        match action with
        | Clear ->
            let diagram =  { 
                model.diagram with  
                    items             = HashMap.empty
                    order             = IndexList.empty
                    selectedRectangle = None
            }                        

            { 
                model with 
                    logs                  = HashMap.empty
                    param_selectedPoints  = HashMap<ContactId, V3d>.Empty                                
                    currrentYMapping      = None
                    selectedBorder        = None
                    diagram               = diagram
            }
        | SvgCameraMessage m ->
            let _svgCamera = SvgCamera.update model.svgCamera m
            {model with svgCamera = _svgCamera}
        | KeyboardMessage a -> 
            { model with diagram = model.diagram |> DiagramApp.update (DiagramAppAction.KeyboardMessage a)} // All keyboard events only to diagram app?
        | LogMessage (logId, logmsg) ->
            let log = LegacyLogHelpers.tryFindLog model logId
            match log with
            | Some log ->
                let _dApp =
                    match logmsg with
                    | GeologicalLogAction.TextInputMessage (sid, textMessage) ->
                        let itemMessage = DiagramItemAction.ChangeLabel textMessage
                        DiagramApp.updateItemFromId model.diagram log.diagramRef.itemId itemMessage
                    | GeologicalLogAction.MoveDown _ ->
                        model.diagram |> DiagramApp.update (DiagramAppAction.MoveRight log.diagramRef.itemId)
                    | GeologicalLogAction.MoveUp _ ->
                        model.diagram |> DiagramApp.update (DiagramAppAction.MoveLeft log.diagramRef.itemId)
                    | _ -> model.diagram
                { model with 
                    logs    = updateLog logId logmsg model.logs
                    diagram = _dApp }
            | None -> model
        | SelectLogNuevo logId -> updateDiagramItemSelection model logId false
        | LogPropertiesMessage msg ->                        
            match model.selectedLogNuevo with 
            | Some logId ->      
            
                let logs =
                    model.logsNuevo 
                    |> HashMap.update logId (function
                        | Some log -> 
                            log 
                            |> GeologicalLogNuevoProperties.update msg
                        | None -> 
                            Log.error "[CorrelationPlot] couldn't find log %A" logId
                            GeologicalLogNuevo.initial
                    )

                let diagram =
                    match msg with
                    | GeologicalLogNuevoProperties.Action.SetName name ->
                        let diagramItemId = logId |> toDiagramItemId
                        let items =
                            model.diagram.items
                            |> HashMap.update diagramItemId (function
                                | Some item -> 
                                    item |> LogToDiagram.setItemHeader name
                                | None -> failwith ""
                            )

                        { model.diagram with items = items }

                { model with logsNuevo = logs; diagram = diagram }
            | None -> model            
        | FinishLog ->            
            if (model.param_selectedPoints |> HashMap.isEmpty) then
                Log.line "no points in list for creating log"
                model
            else                                
                model 
                |> createNewLog annotations semApp planet model.colorMap 
        | DeleteLog id -> 
            let logId = id |> toLogId
            let logs = (HashMap.remove id model.logs)
            let logsNuevo = (HashMap.remove logId model.logsNuevo)

            let diagram = 
                model.diagram 
                |> DiagramApp.update (DiagramAppAction.DeleteStack id)
                |> DiagramApp.layoutDiagramItems

            { model with logs = logs; logsNuevo = logsNuevo; diagram = diagram; selectedLogNuevo = None }
        | ToggleEditCorrelations ->
            { model with editCorrelations = not model.editCorrelations }
        | SetSecondaryLevel lvl  -> 
            { model with secondaryLvl = lvl } 
        | CorrelationPlotAction.MouseMove m -> 
            let _d = model.diagram |> DiagramApp.update (DiagramAppAction.MouseMove m)
            { model with diagram = _d }
        | DiagramMessage a -> 
            
            match a with 
            | DiagramAppAction.DiagramItemMessage (itemId, DiagramItemAction.HeaderMessage(HeaderAction.TextMessage(Text.Action.MouseMessage(MouseAction.OnLeftClick)))) ->
                // chatch click-message of label and trigger selection
                updateDiagramItemSelection model (itemId|> toLogId) false            
            | _ ->

                let model = 
                    match a with
                    | DiagramAppAction.DiagramItemMessage 
                        (itemId, DiagramItemAction.RectangleStackMessage 
                            (ridStackId, RectangleStackAction.RectangleMessage
                                (rid, RectangleAction.SetUncertainty (state)))) ->

                        let faciesId = 
                            model.diagram.rectanglesTable 
                            |> HashMap.tryFind rid 
                            |> Option.map(fun x ->
                                x.faciesId |> FaciesId)

                        match faciesId with
                        | Some id ->
                            let logId = LogId.fromDiagramItemId itemId
                            
                            updateFacies logId id (fun x -> { x with isUncertain = state }) model                    
                        | None ->
                            model
                    | _ -> model


                let unpackchain = 
                    (LeanDiagramAction.unpack a
                        LeanDiagramAction.OnLeftClick
                        (fun stackid _ -> model))
            
                let cp =
                    unpackchain model

                let selectMap stackid rectId = 
                    let optnode = LegacyLogHelpers.tryFindNodeFromRectId model stackid rectId
                    match optnode with
                    | Some node -> 

                        //let nodeId = node.id |> LogNodeId.getValue
                        //model.diagram
                        
                        let info = (Rectangle.Lens.grainSize.Get node.mainBody)
                        ColourMap.update model.colorMap (ColourMap.Action.SelectItem info.grainType)
                    | None      ->
                        model.colorMap

                let colorMap =
                    LeanDiagramAction.unpack a
                        LeanDiagramAction.SelectRectangle
                        selectMap
                        model.colorMap
            
                let diagram = 
                    model.diagram  
                    |> DiagramApp.update a
                    |> DiagramApp.layoutDiagramItems
                
                { 
                    cp with 
                        diagram  = diagram
                        colorMap = colorMap
                }
            
        | GrainSizeTypeMessage (rid, action) ->            

            //write grain size data back into domainmodel
            let model = 
                match action with
                | ColourMap.SelectItem grainType ->                    
                    model |> updateSelectedFacies (fun x -> { x with grainType = grainType}) 
                | _ -> 
                    model
            
            let (_logs, _diagram) = 
                match action with
                | ColourMap.SelectItem grainType ->                    
                    let isPrimary = 
                        DiagramApp.tryGetIdPair model.diagram rid 
                        |> Option.bind (fun (_, stackId) -> DiagramApp.findStackFromId model.diagram stackId)
                        |> Option.map (fun (_, stack) -> stack.stackType = StackType.Primary)
                        |> Option.defaultValue false

                    let rect = DiagramApp.tryFindRectangle model.diagram rid
                    match isPrimary, rect with
                    | true, Some r when r.isSelected -> // only update SELECTED rectangle!
                        let optn = LegacyLogHelpers.tryFindNodeFromRectangleId model r.id
                        match optn with
                        | Some (n, log) ->

                            let grainInfo: GrainSizeInfo =
                                let middle = model.colorMap.mappings.[grainType].defaultMiddle
                                let displayWidth = (21.0 + System.Math.Log(middle,2.0)) * 10.0   
                                {
                                    grainType = grainType
                                    middleSize = middle  
                                    displayWidth = displayWidth
                                }
                                                       
                            let setGrainSize = RectangleAction.SetGrainSize grainInfo // fixes also width!
                            let changeColor = RectangleAction.UpdateColour (updateColoursFromCMap model)

                            let id = failwith "" // n.id TODO v5 correlations

                            // TODO..why is this stuff duplicated in logs and diagram?
                            let logs  =
                                model.logs
                                |> updateLog log.diagramRef.itemId (GeologicalLogAction.LogNodeMessage (id, LogNodes.RectangleMessage changeColor))
                                |> updateLog log.diagramRef.itemId (GeologicalLogAction.LogNodeMessage (id, LogNodes.RectangleMessage setGrainSize))

                            let diagram =
                                model.diagram
                                |> DiagramApp.update (DiagramAppAction.UpdateRectangle (r.id, setGrainSize))
                                |> DiagramApp.update (DiagramAppAction.UpdateRectangle (r.id, changeColor))

                            (logs, diagram)
                        | None -> (model.logs, model.diagram)
                    | _ -> 
                        (model.logs, model.diagram)
                | ColourMap.ItemMessage (id, a) -> 
                    let diagram = 
                        model.diagram 
                        |> DiagramApp.update (DiagramAppAction.UpdateColour (updateColoursFromCMap model))
                    (model.logs, diagram)                
                 
            { 
                model with 
                    colorMap     = ColourMap.update model.colorMap action
                    diagram      = _diagram |> DiagramApp.layoutDiagramItems
                    logs         = _logs
            }
        | CorrelationMessage msg ->
            let diagram = model.diagram |> DiagramApp.update (DiagramAppAction.CorrelationsMessage msg)
            { model with diagram = diagram }
        | _ ->
            Log.warn "[CorrelationPlot] unknow message %A" action
            model
       

    let viewLogs (model : AdaptiveCorrelationPlotModel): DomNode<CorrelationPlotAction> =

        let toStyleColor color = (sprintf "color: %s;" (Html.ofC4b color))
        
        let getColor id isHeader = 
            model.selectedLogNuevo
            |> AVal.map(fun x ->
                match x with
                | Some y when y = id ->
                    C4b.VRVisGreen |> toStyleColor
                | _ when isHeader -> 
                    C4b.Gray |> toStyleColor 
                | _ -> 
                    C4b.White |> toStyleColor 
            )

        let iconAttributes id =
            amap {
                yield clazz "ui circle inverted middle aligned icon"
                let! color = getColor id false
                yield style color
                yield onClick(fun _ -> SelectLogNuevo id)
            } |> AttributeMap.ofAMap

        let headerAttributes id = 
            amap {
                yield clazz "header"
                let! color = getColor id true
                yield style color
                yield onClick(fun _ -> SelectLogNuevo id)
            } |> AttributeMap.ofAMap

        let listOfLogs =         
            model.logsNuevo
            |> AMap.valuesToAList
            |> AList.map (fun l -> 
                let content = 
                    div [clazz "item"; style "margin: 0px 5px 0px 10px"][
                        Incremental.i (iconAttributes l.id) AList.empty
                        div [clazz "content"] [
                            Incremental.div 
                                (headerAttributes l.id) 
                                ([Incremental.text (l.name)] |> AList.ofList)
                            div [clazz "description"; style (toStyleColor C4b.White)] [
                                text (l.id |> string)
                            ]
                        ]
                        hr [style ((C4b.White |> toStyleColor) + "margin: 5px 0px 0px 0px")]
                    ]
                l.name, content)
            |> AList.collect (fun (md, node) -> md |> AList.bind (fun str -> AList.single (str, node))) // de-mod the key from the tuple
            |> AList.sortBy fst
            |> AList.map snd

        Incremental.div 
            ([clazz "ui list"] |> AttributeMap.ofList) 
            listOfLogs

    let viewSvg (contacts : MContactsTable) (model : AdaptiveCorrelationPlotModel)  = //TODO refactor
        let svgNode =
            let attsRoot = [
                clazz "svgRoot"
                style "border: 1px solid black"
                //attribute "viewBox" "0 0 600 400"
                attribute "preserveAspectRatio" "xMinYMin meet"
                attribute "height" "100%"
                attribute "width" "100%"
            ]
        
            let attsGroup = 
                SvgCamera.transformationAttributes model.svgCamera
            
            let logSvgList = 
                DiagramApp.view model.diagram |> UI.map DiagramMessage |> AList.single
            
            Svg.svg attsRoot [
                Incremental.Svg.g (AttributeMap.ofAMap attsGroup) logSvgList              
            ]

        require (GUI.CSS.myCss) (
            body [
                attribute "overflow-x" "auto";
                attribute "overflow-y" "auto"; 
                (onMouseDown (fun b p -> 
                    SvgCameraMessage (SvgCamera.Action.MouseDown (b,p)))) 
                (onMouseUp   (fun b p -> 
                    SvgCameraMessage (SvgCamera.Action.MouseUp (b,p))))
                (onMouseMove (fun p   -> 
                   SvgCameraMessage (SvgCamera.Action.MouseMove (V2d p))))
                onKeyDown (fun k -> KeyboardMessage (Keyboard.Action.KeyDown k))
                onKeyUp   (fun k -> KeyboardMessage (Keyboard.Action.KeyUp k))
            ] [
                div [attribute "overflow-x" "auto";attribute "overflow-y" "auto"] [svgNode]
            ])
                                       
    let listView (model : AdaptiveCorrelationPlotModel) =        
        
        let attsBody = [
            style "background: #1B1C1E; height:100%; overflow-y:scroll; overflow-x:hidden; color:white"
        ] 
        

        let logProperties =
            alist {
                let! selected = model.selectedLogNuevo 

                match selected with
                | Some logId ->
                    let! logs = model.logsNuevo |> AMap.toAVal
                    match (logs |> HashMap.tryFind logId) with
                    | Some log -> 
                        yield GeologicalLogNuevoProperties.view log 
                    | None -> ()
                | None -> ()
            }

        let logActions =
            alist {
                let! selected = model.selectedLogNuevo 

                match selected with
                | Some logId ->                    
                    yield div [clazz "ui buttons inverted"] [                    
                        button [
                            clazz "ui icon button"
                            onMouseClick (fun _ -> logId |> toDiagramItemId |> DeleteLog)
                        ] [
                            i [clazz "remove icon red"] []
                        ]
                    ]                    
                | None -> ()
            }                                                     

        require (GUI.CSS.myCss) (
            body attsBody [
                GuiEx.accordion "Logs" "Content" true [
                    viewLogs model
                ]
                GuiEx.accordion "Actions" "Content" true [
                    Incremental.div AttributeMap.empty logActions                     
                ]
                GuiEx.accordion "Properties" "Content" true [
                    Incremental.div AttributeMap.empty logProperties 
                    |> UI.map LogPropertiesMessage
                ]
                GuiEx.accordion "Correlations" "Content" true [
                    CorrelationsApp.viewCorrelations model.diagram.correlations
                    |> UI.map CorrelationMessage
                ]
                GuiEx.accordion "Actions" "Content" true [
                    Incremental.div AttributeMap.empty (CorrelationsApp.viewCorrelationActions model.diagram.correlations)
                    |> UI.map CorrelationMessage
                ]                
            ]
        )
                      
    let threads (model : CorrelationPlotModel) = 
        ThreadPool.empty
    
    let app (annotations) (semApp  : SemanticsModel) (planet) (mAnnotations) 
        : App<CorrelationPlotModel,AdaptiveCorrelationPlotModel,CorrelationPlotAction> =

        {
            unpersist = Unpersist.instance
            threads   = threads
            initial   = CorrelationPlotModel.initial
            update    = (update annotations semApp planet)
            view      = (viewSvg mAnnotations)
        }
    
    let start annoApp semApp mAnnoApp planet =
        App.start (app annoApp semApp mAnnoApp planet)