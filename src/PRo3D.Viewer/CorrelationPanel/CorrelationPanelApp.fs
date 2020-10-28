namespace PRo3D.Correlations

open System
open System.Diagnostics

open Aardvark.Base
open Aardvark.Base.Rendering
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.UI

open Aardvark.GeoSpatial.Opc

open UIPlus
open PRo3D
open PRo3D.Base.Annotation
open PRo3D.Viewer

open CorrelationDrawing
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.Model
open CorrelationDrawing.XXX
open CorrelationDrawing.LogTypes
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Drawing

open Aardvark.Rendering.Text
open Svgplus.DA
open Svgplus

open Adaptify.FSharp.Core
open FSharp.Data.Adaptive

open PRo3D.Correlations.Model

module Conversion =
    let selectedPoints (points : HashMap<Guid, LogPoint>) : HashMap<AnnotationTypes.ContactId, V3d> =
        points 
        |> HashMap.toList 
        |> List.map (fun (k,v) ->
            ContactId k, v.position )
        |> HashMap.ofList
    
    //let geometry (g : Geometry) : SemanticTypes.GeometryType = 
    //    match g with
    //    | Geometry.Point    ->  SemanticTypes.GeometryType.Point
    //    | Geometry.Line     ->  SemanticTypes.GeometryType.Line
    //    | Geometry.Polyline ->  SemanticTypes.GeometryType.Polyline
    //    | Geometry.Polygon  ->  SemanticTypes.GeometryType.Polygon
    //    | Geometry.DnS      ->  SemanticTypes.GeometryType.DnS
    //    | _ -> g |> sprintf "not implemented %A" |> failwith 
    
    //let projection (g : Projection) : Projection = 
    //    match g with
    //    | Projection.Linear    ->  Types.Projection.Linear
    //    | Projection.Sky       ->  Types.Projection.Sky
    //    | Projection.Viewpoint ->  Types.Projection.Viewpoint
    //    | _ -> g |> sprintf "not implemented %A" |> failwith        

    let semanticsHack (thickness : float) (geometry : Geometry) : string =
        match geometry with
        | Geometry.Polyline | Geometry.DnS -> 
            match thickness with
            | 5.0 -> "Horizon0"
            | 4.0 -> "Horizon0"
            | 3.0 -> "Horizon0"
            | 2.0 -> "Horizon0"
            | 1.0 -> "Horizon1"
            | _ -> "couldn't be inferred"
        | _ -> "couldn't be inferred"
    
    let semanticsId (sem : SemanticId) = 
        let (SemanticId s) = sem                    
        if s.IsEmptyOrNull() then 
            failwith "ecountered empty semantic" 

        else s |> CorrelationDrawing.SemanticTypes.CorrelationSemanticId

    //let semanticsType (sem : SemanticType) : CorrelationDrawing.SemanticTypes.SemanticType =  
    //    sem |> int |> enum<CorrelationDrawing.SemanticTypes.SemanticType>
    
    let toContact (inAnno : Annotation) : CorrelationDrawing.AnnotationTypes.Contact = 
        //let semId = semanticsHack inAnno.thickness.value inAnno.geometry                
                
        let blurg : CorrelationDrawing.AnnotationTypes.Contact = {
            id            = inAnno.key        |> ContactId
            geometry      = inAnno.geometry   //|> geometry
            projection    = inAnno.projection //|> projection
            elevation     = (fun y -> (PRo3D.Base.CooTransformation.getAltitude y V3d.NaN PRo3D.Base.Planet.Mars) + 3000.0)
            semanticType  = inAnno.semanticType //|> semanticsType
            semanticId    = inAnno.semanticId   |> semanticsId

            selected      = false
            hovered       = false
                          
            points        = inAnno.points |> IndexList.map(fun x -> { point = x; selected = false } )            
                          
            visible       = inAnno.visible
            text          = inAnno.text
        }
      //  Log.line "[AnnoConversion] style: %A %A mapped: %A" inAnno.color inAnno.thickness.value blurg.semanticId.id
        blurg
    

module ContactsTable =
    let create (annotations : HashMap<Guid, PRo3D.Core.Leaf>) : CorrelationDrawing.AnnotationTypes.ContactsTable =     
        annotations 
        |> Leaf.toAnnotations
        |> HashMap.toList
        |> List.map(fun (_,v) -> 
            let a = v |> Conversion.toContact
            a.id, a
        )
        |> HashMap.ofList

    let add (contacts : ContactsTable) (annotations : HashMap<Guid, Leaf>) : CorrelationDrawing.AnnotationTypes.ContactsTable =
        let k = annotations |> create
        contacts |> HashMap.union k

type CorrelationPanelsMessage = 
| CorrPlotMessage               of CorrelationPlotAction
| SemanticAppMessage            of SemanticAction
| ColourMapMessage              of ColourMap.Action
| LogPickReferencePlane         of Guid
| LogAddSelectedPoint           of Guid * V3d
| LogAddPointToSelected         of Guid * V3d
| LogCancel
| LogConfirm
| LogAssignCrossbeds            of HashSet<Guid>
| UpdateAnnotations             of HashMap<Guid, Leaf>
| ExportLogs                    of string
| RemoveLastPoint
| SetContactOfInterest          of HashSet<CorrelationDrawing.AnnotationTypes.ContactId>
| Nop

module CorrelationPanelsApp =        
    open CorrelationDrawing.Nuevo

    let rand = Random () // todo orti : remove only for testing...

    let update 
        (m         : CorrelationPanelModel)
        (reference : ReferenceSystem)
        (msg       : CorrelationPanelsMessage)
        : CorrelationPanelModel =

        match msg with
        | ExportLogs path ->
            
            let rows =
                m.correlationPlot.logsNuevo 
                |> HashMap.values
                |> Seq.toList
                |> List.map(fun x -> 
                    let plane = x.referencePlane.plane

                    let sorted =
                        x.contactPoints
                        |> HashMap.values
                        |> Seq.toList
                        |> List.map (fun x -> plane.Height(x))
                        |> List.sort
                        
                    let thicknesses =
                        sorted 
                        |> List.pairwise
                        |> List.map (fun (h0,h1) -> (h0 - h1) |> abs) 
                        |> List.map (sprintf ",%f")
                        |> List.fold (+) String.Empty

                    x.name + thicknesses
                )                
                |> List.toArray

            File.writeAllLines path rows

            let argument = @"/select," + path
            Process.Start("explorer.exe", argument) |> ignore

            m
        | UpdateAnnotations annomap ->
            let annotations = annomap |> ContactsTable.add m.contacts
            Log.line "[CorrelationPanelsApp] updating annotations"
            { m with contacts = annotations }
        | CorrPlotMessage a -> 
            
            let selectedPoints, referencePlane, scale = 
                match m.logBrush with
                | Some brush -> 
                    match brush.referencePlane with
                    | Some p ->                        
                        brush.pointsTable |> Conversion.selectedPoints, p, brush.planeScale 
                    | None -> 
                        HashMap.empty, DipAndStrikeResults.initial, Double.NaN
                | None -> 
                    HashMap.empty, DipAndStrikeResults.initial, Double.NaN             
                
            let correlationPlot = 
                { 
                    m.correlationPlot with 
                        param_selectedPoints = selectedPoints
                        param_referencePlane = referencePlane 
                        param_referenceScale = scale
                }

            let updatedCorrlationPlot = CorrelationPlotApp.update m.contacts m.semanticApp reference.planet correlationPlot a

            let m = { m with correlationPlot = updatedCorrlationPlot; logBrush = None}

            match a with 
            | CorrelationPlotAction.DiagramMessage
                (Svgplus.DA.DiagramAppAction.DiagramItemMessage
                    (diagramItemId, Svgplus.DiagramItemAction.RectangleStackMessage 
                        (_ , Svgplus.RectangleStackAction.RectangleMessage 
                            (_, Svgplus.RectangleAction.Deselect)))) ->         
                // deselect contactOfInterests and SelectedFacies
                let selectedLogId = 
                    diagramItemId 
                    |> LogId.fromDiagramItemId
                
                let cp = 
                    m.correlationPlot 
                    |> CorrelationPlotApp.updateDiagramItemSelection' selectedLogId false

                let cp = { cp with selectedFacies = None }
                { m with 
                    contactOfInterest = HashSet.empty
                    correlationPlot = cp
                }
            | CorrelationPlotAction.DiagramMessage
                (Svgplus.DA.DiagramAppAction.DiagramItemMessage
                    (diagramItemId, Svgplus.DiagramItemAction.RectangleStackMessage 
                        (_ , Svgplus.RectangleStackAction.RectangleMessage 
                            (_, Svgplus.RectangleAction.Select rid)))) -> 

                Log.warn "[todo orti]%A in %A" rid diagramItemId
                  
                //let rectangleIsSelected = 
                //    m.correlationPlot.diagram.rectanglesTable 
                //    |> HashMap.tryFind rid 
                //    |> Option.map (fun x -> x.isSelected)
                //    |> Option.defaultValue false

                let selectedLogId = 
                    diagramItemId 
                    |> LogId.fromDiagramItemId

                //select log when rectangle/facies is selected
                let selectedFaciesId = 
                    m.correlationPlot.diagram.rectanglesTable 
                    |> HashMap.tryFind rid 
                    |> Option.map(fun x ->
                        x.faciesId |> FaciesId)

                let dia = m.correlationPlot.diagram

                let selectedContacts =
                    match dia.selectedRectangle with
                    | Some rectId ->                         
                        //find the two rectangle borders
                        //let borders = 
                        dia.bordersTable
                            |> HashMap.values
                            |> Seq.filter(fun x -> x.lowerRectangle = rectId || x.upperRectangle = rectId)            
                            |> Seq.map(fun x -> x.contactId |> LogToDiagram.toContactId)
                            |> HashSet.ofSeq

                        //match borders with
                        //| Some (left, right)->
                        //    [left |> LogToDiagram.toContactId; right |> LogToDiagram.toContactId] |> HashSet.ofList
                        //| None -> HashSet.empty
                    | None -> HashSet.empty                     

                let cp = 
                    m.correlationPlot 
                    |> CorrelationPlotApp.updateDiagramItemSelection' selectedLogId true

                let cp = { cp with selectedFacies = selectedFaciesId }

                { m with 
                    correlationPlot = cp
                    contactOfInterest = selectedContacts
                }
            | _ -> m
                       
        | SemanticAppMessage a ->                   
            { m with semanticApp = SemanticApp.update m.semanticApp a }
        | ColourMapMessage a ->
            
            let colorMap = 
                ColourMap.update m.correlationPlot.colorMap a      

            let cp =
                match m.correlationPlot.diagram.selectedRectangle with
                | Some r ->                 
                    CorrelationPlotApp.update 
                        m.contacts
                        m.semanticApp
                        reference.planet
                        m.correlationPlot
                        (GrainSizeTypeMessage (r,a))                
                | None ->
                    m.correlationPlot
                                          
            { m with correlationPlot = { cp with colorMap = colorMap } }
        | LogPickReferencePlane id when m.logginMode = LoggingMode.PickReferencePlane ->
            
            let contactId = ContactId id
            let contact = m.contacts |> HashMap.find contactId

            let points = 
                contact.points
                |> IndexList.toList
                |> List.map(fun x -> x.point)                                
                                           
            let planeScale = 
                Calculations.getDistance (contact.points |> IndexList.map(fun x -> x.point) |> IndexList.toList) / 3.0

            let dns = 
                contact.points
                |> IndexList.map(fun x -> x.point) 
                |> DipAndStrike.calculateDipAndStrikeResults reference.up.value reference.north.value      
            
            let logBrush =
                {
                    pointsTable    = HashMap.empty
                    localPoints    = IndexList.empty
                    modelTrafo     = Trafo3d.Identity
                    referencePlane = dns
                    planeScale     = planeScale
                } |> Some                        
                
            { m with logBrush = logBrush }    
            
        | LogAddSelectedPoint (id,p) when m.logginMode = LoggingMode.PickLoggingPoints ->
           let contactId = ContactId id
           let contact = m.contacts |> HashMap.find contactId                        

           if (contact.semanticType <> SemanticType.Hierarchical) then
               Log.warn "[Correlations] can't pick non hierarchical annotation as log point"
               m
           else
               Log.line "[Correlations] picked logpoint at %A of %A" (contactId |> Contact.getLevelById) (contact.semanticId)
               let logPoint = { annoId = id; position = p }
               
               let logBrush =
                   m.logBrush
                   |> Option.map(fun b ->

                       //set mode trafo on first element
                       let modelTrafo = 
                           if b.pointsTable.IsEmpty then
                               logPoint.position |> Trafo3d.Translation
                           else
                               b.modelTrafo

                       let pointsTable = HashMap.add id logPoint b.pointsTable
                       { 
                           b with
                             pointsTable = pointsTable
                             localPoints = pointsTable |> HashMap.values |> IndexList.ofSeq
                             modelTrafo  = modelTrafo
                       }
                   )
                                                     
               { m with logBrush = logBrush }

        | LogAddPointToSelected (id,p) ->
            let contactId = ContactId id
            let contact = m.contacts |> HashMap.find contactId                        

            if (contact.semanticType <> SemanticType.Hierarchical) then
                Log.warn "[Correlations] can't pick non hierarchical annotation as log point"
                m
            else
                Log.line "[Correlations] picked logpoint at %A of %A" (contactId |> Contact.getLevelById) (contact.semanticId)
                
                let contactId = id |> ContactId
                
                match m.correlationPlot.selectedLogNuevo with
                | Some selectedId ->                    
                    let selectedLog = m.correlationPlot.logsNuevo |> HashMap.find selectedId
                    
                    //let selectedLog = 
                    //    { selectedLog with contactPoints = selectedLog.contactPoints |> HashMap.alter contactId (fun _ -> Some p) }

                    let newLog = 
                        CorrelationDrawing.Nuevo.GeologicalLogNuevo.updateLogWithNewPoints 
                            m.contacts
                            m.semanticApp
                            reference.planet
                            (contactId,p)
                            selectedLog

                    let logs = 
                        m.correlationPlot.logsNuevo 
                        |> HashMap.alter selectedId (function
                            | Some _ -> Some newLog
                            | None -> None
                        )                
                                                          
                    let plot = { m.correlationPlot with logsNuevo = logs }

                    let plot = //completely redraw the whole panel to trigger change
                        { plot with diagram = Svgplus.DiagramApp.init }
                        |> CorrelationPlotApp.reconstructDiagramsFromLogs
                            m.contacts
                            m.semanticApp
                            m.correlationPlot.colorMap

                    let diagram =
                        plot.diagram
                        |> Svgplus.DiagramApp.update (
                            Svgplus.DA.DiagramAppAction.SetYScaling(plot.diagram.yScaleValue))

                    let plot = { plot with diagram = diagram }

                    //update model
                    { m with correlationPlot = plot }
                | None -> m
        | CorrelationPanelsMessage.RemoveLastPoint when m.logginMode = LoggingMode.PickLoggingPoints ->
            let logBrush = 
                match m.logBrush with
                | Some b ->
                    match b.localPoints |> IndexList.toList with
                    | [] -> failwith "[CorrelationPanel] empty brush shouldn't exist"
                    | _ :: [] -> None
                    | x :: xs -> 
                        Some { b with pointsTable = HashMap.remove x.annoId b.pointsTable; localPoints = xs |> IndexList.ofList }
                | None -> None      
            { m with logBrush = logBrush }
        | LogCancel -> 
            match m.logginMode with
            | PickReferencePlane ->
                { m with logBrush = None }
            | PickLoggingPoints ->
                let brush = m.logBrush |> Option.map LogDrawingBrush.clearLogPoints
                { m with logBrush = brush; logginMode = PickReferencePlane }
            | EditLog -> m
        | LogConfirm ->
            match m.logginMode with
            | PickReferencePlane ->
                { m with logginMode = PickLoggingPoints }
            | PickLoggingPoints ->  // finish up and clear brush             
                { m with logBrush = None; logginMode = PickReferencePlane }
            | EditLog -> m
        | LogAssignCrossbeds selected ->
            Log.warn "selected %A" selected

            let log = 
                m.correlationPlot.selectedLogNuevo 
                |> Option.bind(fun x -> m.correlationPlot.logsNuevo |> HashMap.tryFind x)

            let crossBeds =
                selected 
                |> HashSet.choose (fun x -> 
                    let id = x |> ContactId
                    HashMap.tryFind id m.contacts
                )
                |> HashSet.filter(fun x -> x.semanticType = SemanticType.Angular)
                |> HashSet.map(fun x -> x.id)
            
            match log, m.correlationPlot.selectedFacies with
            | Some l, Some faciesId ->
                //update facies
                let facies =
                    l.facies
                    |> Facies.updateFacies faciesId (fun x -> { x with measurements = crossBeds })

                //update log
                let l = { l with facies = facies }

                //update diagram
                //let blurg = //TODO TO refactor, probably put into correlation plot and hide behind event?
                //    CorrelationDrawing.CorrelationPlotApp.updateLogDiagram 
                //        m.contacts 
                //        m.semanticApp
                //        (m.correlationPlot |> CorrelationPlotApp.diagramConfigFrom)
                //        m.correlationPlot.colorMap
                //        l
                //        m.correlationPlot

                let plot = 
                    {
                        m.correlationPlot with 
                            logsNuevo =
                                m.correlationPlot.logsNuevo 
                                |> HashMap.alter l.id (function | Some _ -> Some l | None -> None)
                    }                
                
                let plot = //completely redraw the whole panel to trigger change
                    { plot with diagram = Svgplus.DiagramApp.init }
                    |> CorrelationPlotApp.reconstructDiagramsFromLogs
                        m.contacts
                        m.semanticApp
                        m.correlationPlot.colorMap

                let diagram =
                    plot.diagram
                    |> Svgplus.DiagramApp.update (
                        Svgplus.DA.DiagramAppAction.SetYScaling(plot.diagram.yScaleValue))

                let plot = { plot with diagram = diagram }

                //update model
                { m with correlationPlot = plot }
                
            | _ ->                 
                m     
        | SetContactOfInterest _ -> 
            if m.contacts.IsEmpty = true then
                m
            else
                { m with contactOfInterest = m.contacts |> HashMap.keys |> Seq.take 1 |> HashSet.ofSeq }       
        | _ ->
            Log.warn "[CorrelationPanelsApp] unhandled action %A" msg
            m
     
    let view (m : AdaptiveCorrelationDrawingModel) : DomNode<CorrelationPanelsMessage> = div [] []
    
    let viewMappings (m : AdaptiveCorrelationPanelModel) : DomNode<CorrelationPanelsMessage> =
        ColourMap.view m.correlationPlot.colorMap
        |> UI.map CorrelationPanelsMessage.ColourMapMessage  
              
    let viewSemantics (m : AdaptiveCorrelationPanelModel) : DomNode<CorrelationPanelsMessage> =
        SemanticApp.expertGUI m.semanticApp 
        |> UI.map CorrelationPanelsMessage.SemanticAppMessage
    
    let viewLogs (m : AdaptiveCorrelationPanelModel) : DomNode<CorrelationPanelsMessage> =
        CorrelationPlotApp.listView m.correlationPlot
        |> UI.map CorrelationPanelsMessage.CorrPlotMessage
    
    let viewSvg (m : AdaptiveCorrelationPanelModel) =      
        CorrelationPlotApp.viewSvg m.contacts m.correlationPlot 
        |> (UI.map CorrPlotMessage)

    let viewContactOfInterest (m : AdaptiveCorrelationPanelModel) =
        m.contactOfInterest
        |> ASet.map (fun x -> 
            m.contacts 
            |> AMap.find x
            |> AVal.map (fun a -> 
                let points = a.points |> AList.map (fun p -> p.point) // global
                
                let modelTrafo =
                    points 
                    |> AList.toAVal 
                    |> AVal.map (fun x ->
                        x 
                        |> IndexList.tryFirst 
                        |> Option.map (fun h -> Trafo3d.Translation h)
                        |> Option.defaultValue Trafo3d.Identity)                               

                PRo3D.Base.OutlineEffect.createForLineOrPoint 
                    PRo3D.Base.OutlineEffect.PointOrLine.Line
                    (AVal.constant C4b.VRVisGreen) 
                    (AVal.constant 3.0)
                    5.0
                    RenderPass.main
                    modelTrafo 
                    points
            )
            |> Sg.dynamic
        )
        |> Sg.set                 

    let drawLogSg 
        (cam        : aval<CameraView>) 
        (text       : aval<string>)
        (near       : aval<float>)
        (primary    : aval<C4b>) 
        (secondary  : aval<C4b>) 
        (dnsResults : aval<option<AdaptiveDipAndStrikeResults>>) 
        (modelTrafo : aval<Trafo3d>) 
        (pickable   : Option<LogTypes.LogId * aval<option<LogTypes.LogId>>>)
        (pickingAllowed: aval<bool>) 
        (points     : alist<V3d>) =
      
        let elevationPoints =
            alist {                
                let! maybeDns = dnsResults

                match maybeDns with
                | Some dns -> 
                    let! plane = dns.plane

                    let sorted =
                        points 
                        |> AList.map(fun x -> x, plane.Height(x))
                        |> AList.sortBy snd
                    yield! sorted
                | None -> 
                    yield! AList.empty                
            }   
            
        let points' = elevationPoints |> AList.map fst
    
        //TODO TO: subtle incremental problem ... no clue why
        //let labels =
        //    elevationPoints //alist<position * elevation>
        //    |> AList.pairwise
        //    |> AList.map(fun ((p0,e0),(p1,e1)) ->
        //        let midPoint = ~~Trafo3d.Translation((p0 + p1) / 2.0)

        //        (e0 - e1)
        //        |> abs |> Formatting.Len |> string
        //        |> AVal.constant 
        //        |> billboardText cam midPoint
        //    ) 
        //    |> AList.toASet 
        //    |> Sg.set  
        
        let labels =
            elevationPoints.Content 
            |> AVal.map (fun l ->        
                    [ 
                        for ((p0,e0),(p1,e1)) in l |> IndexList.toList |> List.pairwise do
                            let midPoint = ~~((p0 + p1) / 2.0)

                            yield (e0 - e1)
                            |> abs |> Formatting.Len |> string
                            |> AVal.constant 
                            |> PRo3D.Base.Sg.billboardText cam midPoint
                    ] |> Sg.ofSeq
               )
            |> Sg.dynamic

        let labels2 =
            adaptive {
                let! points = elevationPoints.Content 
                let pairs = points |> IndexList.toList |> List.pairwise
                
                let labels =                    
                    pairs
                    |> List.map(fun ((p0,e0),(p1,e1)) ->
                        let midPoint = ~~((p0 + p1) / 2.0)
                        
                        (e0 - e1)
                        |> abs |> Formatting.Len |> string
                        |> AVal.constant 
                        |> PRo3D.Base.Sg.text cam near ~~60.0 midPoint (midPoint |> AVal.map Trafo3d.Translation) ~~0.05
                    ) |> Sg.ofSeq
                    
                return labels
            
            } |> Sg.dynamic

        //Sg.text view conf.nearPlane conf.hfov pos anno.modelTrafo text anno.textsize.value

        let polyLine = 
            match pickable with 
            | None -> 
                Sg.drawLines points' ~~0.001 secondary ~~5.0 modelTrafo
            | Some (logId, selectedLogId) -> 

                let logIsSelected = 
                    selectedLogId
                    |> AVal.map (function
                        | Some selId when selId = logId -> 
                            true
                        | _ -> 
                            false
                    )


                let event = 
                    SceneEventKind.Click, (fun _ -> 
                        let allowed = pickingAllowed |> AVal.force
                        match allowed with
                        | true ->  true, Seq.ofList[CorrPlotMessage(CorrelationPlotAction.SelectLogNuevo logId)]
                        | false -> true, Seq.empty)
                    
                let linesSg = Sg.pickableLine points' ~~0.001 secondary ~~5.0 modelTrafo true (fun lines -> event)
  
                let selectionSg = 
                    logIsSelected
                    |> AVal.map (function
                        | true ->
                            PRo3D.Base.OutlineEffect.createForLineOrPoint 
                                PRo3D.Base.OutlineEffect.PointOrLine.Both
                                (AVal.constant C4b.Yellow) 
                                (AVal.constant 5.0) 
                                3.0 
                                RenderPass.main 
                                modelTrafo points'
                        | false -> Sg.empty ) 
                    |> Sg.dynamic

                let labelSg = 
                    logIsSelected
                    |> AVal.map (function
                        | true -> labels2                            
                        | false -> Sg.empty
                    ) 
                    |> Sg.dynamic

                [ linesSg; labelSg; selectionSg ] |> Sg.ofList

        points 
        |> AList.map (fun x -> Sg.dot primary ~~8.0 ~~x)
        |> AList.toASet
        |> Sg.set            
        |> Sg.andAlso polyLine
        //|> Sg.andAlso labels2
    
    let viewWorkingLog (planeSize : aval<float>) (cam : aval<CameraView>) near (m : AdaptiveCorrelationPanelModel) (falseColors : AdaptiveFalseColorsModel) =
        
        let logSg =
            m.logBrush 
            |> AVal.map(fun x ->
                match x with
                | AdaptiveSome brush -> 
                    brush.localPoints 
                    |> AList.map(fun x -> x.position)
                    |> drawLogSg 
                        cam 
                        ~~"new log" 
                        near 
                        ~~C4b.Magenta 
                        ~~C4b.DarkMagenta 
                        (brush.referencePlane |> AVal.map Missing.AdaptiveOption.toOption)
                        brush.modelTrafo 
                        None 
                        ~~false // cannot be selected
                | AdaptiveNone -> Sg.empty           
            ) |> Sg.dynamic

        let planeSg = 
            m.logBrush 
            |> AVal.map(fun x ->
                match x with
                | AdaptiveSome brush ->                     
                    Sg.drawTrueThicknessPlane 
                        (brush.planeScale |> AVal.map2(fun a b -> a * b) planeSize) 
                        (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption brush.referencePlane)
                        falseColors
                | AdaptiveNone -> Sg.empty           
            ) |> Sg.dynamic
            
        logSg, planeSg
        
    let viewFinishedLogs (planeSize : aval<float>) (cam : aval<CameraView>) near (falseColors : AdaptiveFalseColorsModel) (m : AdaptiveCorrelationPanelModel) (pickingAllowed: aval<bool>) =
        let logs = 
            m.correlationPlot.logsNuevo
            |> AMap.toASet
            |> ASet.map snd
                    
        let colors id =
            m.correlationPlot.selectedLogNuevo 
            |> AVal.map(function
                | Some selected when selected = id -> C4b.VRVisGreen, C4b.DarkCyan
                | _ -> C4b.Cyan, C4b.DarkCyan            
            )
        
        let logSg = 
            logs 
            |> ASet.map(fun x -> 

                let referencePlane = ~~(Some x.referencePlane)
                
                let points = 
                    x.contactPoints 
                    |> AMap.toASet 
                    |> ASet.map snd
                    |> ASet.toAList

                let trafo =            
                    points
                    |> AList.toAVal 
                    |> AVal.map(fun y -> 
                        match y |> IndexList.tryFirst with
                        | Some head -> Trafo3d.Translation head
                        | None -> Trafo3d.Identity
                    )

                let primary   = colors x.id |> AVal.map fst
                let secondary = colors x.id |> AVal.map snd

                points 
                |> drawLogSg 
                    cam 
                    ~~(x.id.ToString()) 
                    near
                    primary 
                    secondary 
                    referencePlane 
                    trafo 
                    (Some (x.id,  m.correlationPlot.selectedLogNuevo))
                    pickingAllowed
                
            ) |> Sg.set   
    
        let planesSg =
            aset {
                 let! selection = m.correlationPlot.selectedLogNuevo

                 let blu =
                    logs 
                    |> ASet.filter(fun x ->
                        match selection with
                        | Some s when s = x.id -> true                            
                        | _ -> false
                    )
                    |> ASet.map(fun x ->
                        let referencePlane = ~~(Some x.referencePlane)
                        Sg.drawTrueThicknessPlane 
                            ( x.planeScale |> AVal.map2(fun a b -> a * b) planeSize) 
                            referencePlane 
                            falseColors
                    )

                yield! blu
            } |> Sg.set  

        logSg, planesSg

    let viewExportLogButton (path : aval<Option<string>>) =
        let blurg =
            adaptive{
                let! path = path
                match path with 
                | Some p -> return System.IO.Path.ChangeExtension(p,".csv") //p |> changeExtension ".pro3d.ann"
                | None -> return String.Empty
            }

        div [ clazz "ui inverted item"; onMouseClick (fun _ -> ExportLogs (blurg |> AVal.force))][
            text "Export Logs(*.csv)"
        ]
