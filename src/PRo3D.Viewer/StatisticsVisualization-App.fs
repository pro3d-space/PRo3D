﻿namespace Pro3D.AnnotationStatistics

open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive

module StatisticsVisualization_App =

    let update (v:StatisticsVisualizationModel) (act:StatisticsVisualizationAction) =
        match (v,act) with
        | Histogram h, HistogramMessage ha -> 
            StatisticsVisualizationModel.Histogram (Histogram_App.update h ha)
        | RoseDiagram r, RoseDiagramMessage ra -> 
            StatisticsVisualizationModel.RoseDiagram (RoseDiagram_App.update r ra)
        | _ -> 
            failwith "this is not a valid combination of visualization and vis action"

    ///draw either a histogram or a rose diagram depending on the metric of the measurement
    let drawVisualization (p:aval<AdaptiveStatisticsVisualizationModelCase>) (dimensions:V2i)=
        
        let v = 
            alist{ 
                let! vis = p
                match vis with
                | AdaptiveHistogram h ->                     
                    yield HistogramUI.histogramSettings h |> UI.map HistogramMessage                     
                    yield HistogramUI.drawHistogram' h dimensions |> UI.map HistogramMessage 
                | AdaptiveRoseDiagram r ->                    
                    yield RoseDiagramUI.binAngleDropDown' r|> UI.map RoseDiagramMessage 
                    yield RoseDiagramUI.drawRoseDiagram r dimensions |> UI.map RoseDiagramMessage 
                }
       
        let attrSVG =
            [   
                attribute "width" (sprintf "%i" dimensions.X)
                attribute "height" (sprintf "%i" dimensions.Y)                                         
            ]|> AttributeMap.ofList

        Incremental.div attrSVG v
        
