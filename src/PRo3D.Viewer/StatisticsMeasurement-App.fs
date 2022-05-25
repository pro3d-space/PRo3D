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
    //or only the visualization (if the data has not changed)
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

    //update multiple measurements at once
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

    
    

