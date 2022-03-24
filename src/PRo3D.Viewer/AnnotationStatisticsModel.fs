namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation

type Bin = {
     id          : Guid
     value       : int
     start       : int
     theEnd      : int //end is a reserved keyword :(
}

[<ModelType>]
type Histogram = {
    id        : Guid
    numOfBins : int
    rangeStart: int
    rangeEnd  : int
    bins      : IndexList<Bin>
}

[<ModelType>]
type AnnoStats = {
    lengthStats :   HashMap<string, float>   //min, max, avg
    bearingStats :  HashMap<string, float>   //min, max, avg
    histogram    :  Histogram
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
                                    histogram = {
                                                    id = Guid.Empty
                                                    numOfBins = 0
                                                    rangeStart = 0
                                                    rangeEnd = 0
                                                    bins = IndexList.empty
                                                }
                                }
        }
    

