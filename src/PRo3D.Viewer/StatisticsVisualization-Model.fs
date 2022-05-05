
namespace Pro3D.AnnotationStatistics

open Adaptify

[<ModelType>]
type StatisticsVisualizationModel = 
    | Histogram of value: HistogramModel
    | RoseDiagram of value: RoseDiagramModel

type StatisticsVisualizationAction = 
    | HistogramMessage of HistogramModelAction
    | RoseDiagramMessage of RoseDiagramModelAction

