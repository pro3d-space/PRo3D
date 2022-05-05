namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Aardvark.UI
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation


[<ModelType>]
type AnnotationStatisticsModel = 
    {
        selectedAnnotations : HashMap<Guid, Annotation>
        properties          : HashMap<MeasurementType, StatisticsMeasurementModel> 
    }

module AnnotationStatistics =
    let initial =
        {
            selectedAnnotations = HashMap.empty
            properties = HashMap.empty
        }

