namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation

[<ModelType>]
type AnnoStats = {
    lengthStats :   HashMap<string, float>   //min, max, avg
    bearingStats :  HashMap<string, float>   //min, max, avg
}


[<ModelType>]
type AnnoStatsModel = {
    selectedAnnotations: HashMap<Guid, Annotation>    
    annoStatistics:      AnnoStats
}

module AnnoStats =
    let initial =
        {
            selectedAnnotations = HashMap.empty
            annoStatistics = 
                                {
                                    lengthStats = HashMap.empty
                                    bearingStats = HashMap.empty
                                }
        }
    

