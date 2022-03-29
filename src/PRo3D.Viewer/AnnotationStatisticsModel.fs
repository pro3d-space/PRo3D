namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation

type Prop = 
    | LENGTH
    | BEARING
    | VERTICALTHICKNESS


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
type RoseDiagram = {
    id        : Guid
    //TODO
}


[<ModelType>]
type Property = {
    kind      : Prop
    minMaxAvg : HashMap<string, float> 
    histogram : Option<Histogram>    
    roseDiagram : Option<RoseDiagram>
}


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
    

