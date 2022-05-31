namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Adaptify


type Scale = 
    | Metric
    | Angular

type Kind = 
    | LENGTH 
    | BEARING 
    //| VERTICALTHICKNESS 
    | DIP_AZIMUTH 
    | STRIKE_AZIMUTH

type MeasurementType = 
    {
        kind : Kind
        scale : Scale
    }


[<ModelType>]
type StatisticsMeasurementModel =
    {
        [<NonAdaptive>]
        measurementType : MeasurementType 
        data            : List<Guid*float>  
        dataRange       : Range1d
        avg             : float         
        visualization   : StatisticsVisualizationModel
    }

type StatisticsMeasurementAction =    
    | UpdateAll of List<Guid*float> * StatisticsVisualizationAction * Scale                     
    | StatisticsVisualizationMessage of StatisticsVisualizationAction          

module StatisticsMeasurementModel =
        
    let calcMinMaxAvg (l:List<float>) (scale:Scale)=
        match (l.IsEmpty) with
        | true -> (Range1d(0.0, 0.0), 0.0)        
        | false -> 
            
            let min = l |> List.min
            let max = l |> List.max
            let range = Range1d(min, max)
            let avg =
                match scale with
                | Scale.Metric ->  l |> List.average
                | Scale.Angular -> RoseDiagramModel.calculateAvgAngle l
            (range, avg) 

    let initMeasurementType (kind:Kind) (scale:Scale) = 
          {
              kind = kind
              scale = scale
          }

    let init (data:List<Guid*float>) (mType:MeasurementType) =

        let dataRange, avg = calcMinMaxAvg (data |> List.map(fun (_,value) -> value)) mType.scale    
        let initialVis = 
            match mType.scale with
            | Scale.Metric -> StatisticsVisualizationModel.Histogram (HistogramModel.initHistogram dataRange data)
            | Scale.Angular -> StatisticsVisualizationModel.RoseDiagram (RoseDiagramModel.initRoseDiagram data avg)
        
        { measurementType = mType
          data = data
          dataRange = dataRange
          avg = avg                                                 
          visualization = initialVis }


