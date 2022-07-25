namespace Pro3D.AnnotationStatistics

open System
open PRo3D.Base
open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive

type AnnoStatsAction =
    | UpdateSingleSelectedAnnotation of Guid * GroupsModel
    | UpdateMultipleSelectedAnnotations of GroupsModel
    | AddNewMeasurement of MeasurementType
    | MeasurementMessage of MeasurementType * StatisticsMeasurementAction
    | Reset //no more annotations are selected
    | DeleteMeasurement of MeasurementType
    | Predict of Annotation


module AnnotationStatisticsApp =     

    let getAnnotationResults
        (annotations: List<Guid*Annotation>)  
        (annotationProperty: AnnotationResults -> float) 
        = 
        annotations 
        |> List.map(fun (annoId, annotation) ->         
            match annotation.results with
            | Some a -> Some(annoId, a |> annotationProperty)
            | None -> None
        )
        |> List.choose(fun o -> o) 

    let getDnSResults 
        (annotations: List<Guid*Annotation>)   
        (dnsProperty: DipAndStrikeResults -> float) 
        =
        annotations 
        |> List.map(fun (annoId, annotation) ->         
            match annotation.dnsResults with
            | Some a -> Some(annoId, a |> dnsProperty)
            | None -> None
        )
        |> List.choose(fun o -> o)  

    let getLength = fun (x:AnnotationResults) -> x.length
    let getBearing = fun (x:AnnotationResults) -> x.bearing
    //let getVerticalThickness = fun (x:AnnotationResults) -> x.verticalThickness
    let getDipAzimuth = fun (x:DipAndStrikeResults) -> x.dipAzimuth
    let getStrikeAzimuth = fun (x:DipAndStrikeResults) -> x.strikeAzimuth

    let getMeasurementData (mType:MeasurementType) (selected:List<Guid*Annotation>) =
        match mType.kind with
        | Kind.LENGTH -> getAnnotationResults selected getLength     
        | Kind.BEARING -> getAnnotationResults selected getBearing              
        //| Kind.VERTICALTHICKNESS -> getAnnotationResults selected getVerticalThickness          
        | Kind.DIP_AZIMUTH -> getDnSResults selected getDipAzimuth
        | Kind.STRIKE_AZIMUTH -> getDnSResults selected getStrikeAzimuth

    let getHoveredIds (m:AnnotationStatisticsModel) =
        
        let getBinIDsList (id:Option<int>) (bins:List<BinModel>)=
            match id with
            | Some i -> 
                let bin = bins |> List.tryFind (fun b -> b.id = i)
                match bin with
                | Some b -> b.annotationIDs
                | None -> failwith "something wrong with bin id assignment"
            | None -> List.empty
        
        m.properties |> HashMap.fold (fun s _ v -> 
                let vis = v.visualization
                let l = 
                    match vis with
                    | Histogram h -> getBinIDsList h.hoveredBin h.bins
                    | RoseDiagram r -> getBinIDsList r.hoveredBin r.bins
                s @ l
            ) List.empty

  
    let rec update (m:AnnotationStatisticsModel) (a:AnnoStatsAction) =
        match a with
        //Add selection if not in the list, remove selection if already in the list
        | UpdateSingleSelectedAnnotation (id, g) ->         
            match (g.flat |> HashMap.tryFind id) with
            | Some l ->                 
                let updatedAnnotations = 
                    match (m.selectedAnnotations |> HashMap.tryFind id) with
                    | Some _ -> m.selectedAnnotations.Remove id
                    | None -> m.selectedAnnotations.Add (id,Leaf.toAnnotation l)
                let a' = updatedAnnotations |> HashMap.toList

                if a'.IsEmpty then (update m Reset)
                else
                    let updatedMeasurements =
                        let updatedData = 
                            m.properties
                            |> HashMap.map (fun info _ -> getMeasurementData info a')
                        StatisticsMeasurement_App.update' m.properties updatedData
                    {m with selectedAnnotations = updatedAnnotations; properties = updatedMeasurements}
            | None -> m

        | UpdateMultipleSelectedAnnotations g ->
            let selected = g.selectedLeaves |> HashSet.map (fun selection -> selection.id) |> HashSet.toList
            let updatedAnnotations = 
                selected 
                |> List.map (fun id -> 
                    match (g.flat |> HashMap.tryFind id) with
                    | Some leaf -> Some(id,Leaf.toAnnotation leaf)
                    | None -> None
                )
                |> List.choose (fun entry -> entry)
                |> HashMap.ofList    
            let a' = updatedAnnotations |> HashMap.toList

            if a'.IsEmpty then (update m Reset)
            else
                let updatedMeasurements =
                    let updatedData = 
                        m.properties
                        |> HashMap.map (fun info _ -> getMeasurementData info a')
                    StatisticsMeasurement_App.update' m.properties updatedData
                {m with selectedAnnotations = updatedAnnotations; properties = updatedMeasurements}
        
        | MeasurementMessage (mType,act) ->
            match (m.properties.TryFind mType) with
            |Some p -> 
                let updatedMeasurement = StatisticsMeasurement_App.update p act
                let updatedPropList = 
                    m.properties 
                    |> HashMap.alter mType (function None -> None | Some _ -> Some updatedMeasurement)
                {m with properties = updatedPropList}
            |None -> m
            
        | AddNewMeasurement mType -> 
            match (m.properties.ContainsKey mType) with
            | true -> m
            | false -> 
                let data = getMeasurementData mType (m.selectedAnnotations |> HashMap.toList)
                let newMeasurement = StatisticsMeasurementModel.init data mType
                let properties = m.properties.Add (mType, newMeasurement)
                {m with properties = properties}
        | Predict annotation ->
            match (m.properties.IsEmpty) with
            | true -> m
            | false -> m //TODO


        | Reset -> 
            {m with selectedAnnotations = HashMap.empty; properties = HashMap.empty}

        | DeleteMeasurement mType ->
            match (m.properties.ContainsKey mType) with
            | true -> 
                let properties = m.properties.Remove mType
                {m with properties = properties}
            | false -> m               
                


//UI related 
module AnnotationStatisticsDrawings =

    let mTypeDropdown =        
        div [ clazz "ui menu"; style "width:150px; height:20px;padding:0px; margin:0px"] [
            onBoot "$('#__ID__').dropdown('on', 'hover');" (
                div [ clazz "ui dropdown item"; style "width:100%"] [
                    text "Properties"
                    i [clazz "dropdown icon"; style "margin:0px 5px"] [] 
                    div [ clazz "ui menu"] [
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> AddNewMeasurement (StatisticsMeasurementModel.initMeasurementType Kind.LENGTH Scale.Metric))] [text "Length"]
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> AddNewMeasurement (StatisticsMeasurementModel.initMeasurementType Kind.BEARING Scale.Angular))] [text "Bearing"]
                        //div [clazz "ui inverted item"; onMouseClick (fun _ -> AddNewMeasurement (StatisticsMeasurementModel.initMeasurementType Kind.VERTICALTHICKNESS Scale.Metric))] [text "Vertical Thickness"] 
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> AddNewMeasurement (StatisticsMeasurementModel.initMeasurementType Kind.DIP_AZIMUTH Scale.Angular))] [text "Dip Azimuth"] 
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> AddNewMeasurement (StatisticsMeasurementModel.initMeasurementType Kind.STRIKE_AZIMUTH Scale.Angular))] [text "Strike Azimuth"] 
                    ]
                ]
            )
        ] 

    let view (m:AdaptiveAnnotationStatisticsModel) =
        Incremental.div (AttributeMap.ofList [style "width:100%; height:auto; margin: 0 5 0 5"]) 
            (                                  
                m.properties 
                |> AMap.map(fun _ v -> 
                    div[] [
                        button [clazz "ui button tiny inverted"; onClick (fun _ -> DeleteMeasurement v.measurementType )] [
                                i [clazz "trash can icon"] []
                                text "Delete property"
                            ]                        
                        StatisticsMeasurement_App.view v|> UI.map (fun f -> MeasurementMessage (v.measurementType,f))
                    ]
                ) 
                |> AMap.toASet 
                |> ASet.toAList 
                |> AList.map(fun (a,b) -> b)                                                                 
            )

