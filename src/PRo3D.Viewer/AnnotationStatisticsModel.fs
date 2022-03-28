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
     theEnd      : int 
     width       : int
}

[<ModelType>]
type Histogram = {
    id        : Guid
    title     : string
    data      : IndexList<float>
    numOfBins : int
    rangeStart: int
    rangeEnd  : int
    bins      : IndexList<Bin>
}

[<ModelType>]
type Property = {
    minMaxAvg : HashMap<string, float> 
    histogram : Option<Histogram>
    angular   : bool                    //if false, rosediagram not reasonable
    //roseDiagram : Option<RoseDiagram> TODO
}



//[<ModelType>]
//type AnnoStats = {
//    lengthStats     :  HashMap<string, float>   //min, max, avg
//    bearingStats    :  HashMap<string, float>   //min, max, avg
//    histogram       :  Histogram
//    histProperties  :  IndexList<string>
//}


[<ModelType>]
type AnnoStatsModel = {
    selectedAnnotations : HashMap<Guid, Annotation>
    properties          : IndexList<Property>
    propertiesList      : IndexList<string>
}

module AnnoStats =
    let initial =
        {
            selectedAnnotations = HashMap.empty
            properties          = IndexList.empty
            propertiesList      = ["length"; "bearing"; "verticalThickness"] |> IndexList.ofList            
        }
    

