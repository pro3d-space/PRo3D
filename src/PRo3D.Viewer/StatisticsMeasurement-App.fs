namespace Pro3D.AnnotationStatistics

open System
open PRo3D.Base
open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive

module StatisticsMeasurement_App =
 
    //either update the complete measurement (data statistics + visualization)
    //or only the visualization (if the data has not changed, only settings)
    let rec update (m:StatisticsMeasurementModel) (action:StatisticsMeasurementAction) =
        match action with                   
        | StatisticsVisualizationMessage visAction ->             
            let updatedVis = StatisticsVisualization_App.update m.visualization visAction
            {m with visualization = updatedVis}
        | UpdateAll (data, visAction, scale) -> 
            let m' = 
                let dataRange,avg = StatisticsMeasurementModel.calcMinMaxAvg (data |> List.map (fun (_,value) -> value)) scale
                {m with data = data; dataRange = dataRange; avg = avg}
            update m' (StatisticsVisualizationMessage visAction)   

    ///update multiple measurements at once
    let update'
        (measurements: HashMap<MeasurementType, StatisticsMeasurementModel>)
        (updatedData: HashMap<MeasurementType, List<Guid*float>>) 
        =
        measurements
        |> HashMap.map (fun mType measurement -> 
            let data = updatedData |> HashMap.tryFind mType |> Option.defaultValue []
            let visAction =
                match mType.scale with
                | Scale.Metric -> HistogramMessage (UpdateData data)
                | Scale.Angular -> RoseDiagramMessage (UpdateRD data)
            update measurement (UpdateAll (data,visAction, mType.scale))
        )

    ///set the peekItem for each measurement
    let startPeekForEachMeasurement
        (measurements: HashMap<MeasurementType, StatisticsMeasurementModel>)
        (peekValues:HashMap<MeasurementType, float>)
        =
        measurements
        |> HashMap.map (fun mType measurement ->  
            let peekValue = peekValues|> HashMap.find mType
            let visAction =
                match mType.scale with
                | Scale.Metric -> HistogramMessage (PeekBinStart peekValue)
                | Scale.Angular -> RoseDiagramMessage (PeekRDBinStart peekValue)
            update measurement (StatisticsVisualizationMessage visAction)
        )
    
    ///reset the peekItem to None for each measurement
    let endPeekForEachMeasurement (measurements: HashMap<MeasurementType, StatisticsMeasurementModel>) =
        measurements
        |> HashMap.map (fun mType measurement ->              
            let visAction =
                match mType.scale with
                | Scale.Metric -> HistogramMessage PeekBinEnd
                | Scale.Angular -> RoseDiagramMessage PeekRDBinEnd
            update measurement (StatisticsVisualizationMessage visAction)
        )

    let view (m:AdaptiveStatisticsMeasurementModel) =
        let statsTable = 
            div[style "margin-bottom:5"][
                require GuiEx.semui (
                    Html.table [                                    
                        Html.row "Minimum" [Incremental.text (m.dataRange |> AVal.map (fun f -> (sprintf "%.2f" f.Min)))]
                        Html.row "Maximum" [Incremental.text (m.dataRange |> AVal.map (fun f -> (sprintf "%.2f" f.Max)))]
                        Html.row "Average" [Incremental.text (m.avg |> AVal.map (fun f -> (sprintf "%.2f" f)))]
                    ]
                )
                ]
             
        let visualization = 
            div[][
            StatisticsVisualization_App.drawVisualization m.visualization (new V2i(300, 150))     
            |> UI.map StatisticsVisualizationMessage            
            ]
             
        let title = "Measurement: " + m.measurementType.kind.ToString()
        GuiEx.accordion title "Settings" true [
            statsTable                                             
            visualization                           
         ]

    
    

