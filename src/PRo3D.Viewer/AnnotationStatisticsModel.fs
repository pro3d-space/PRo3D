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
     value       : int
     start       : int
     theEnd      : int 
     width       : int
}

type Histogram = {
    id        : Guid        
    numOfBins : int
    rangeStart: int
    rangeEnd  : int
    bins      : HashMap<Guid, Bin>
}

[<ModelType>]
type RoseDiagram = {
    id        : Guid
    //TODO
}


[<ModelType>]
type Property = {
    [<NonAdaptive>]
    kind      : Prop
    
    min       : float
    max       : float
    avg       : float    
    histogram : Option<Histogram>    
    roseDiagram : Option<RoseDiagram>
}


[<ModelType>]
type AnnoStatsModel = {
    selectedAnnotations : HashMap<Guid, Annotation>
    properties          : HashMap<Prop, Property>
    propertiesList      : IndexList<string>
}

module AnnoStats =
    let initial =
        {
            selectedAnnotations = HashMap.empty
            properties          = HashMap.empty
            propertiesList      = ["length"; "bearing"; "verticalThickness"] |> IndexList.ofList            
        }

    let initHistogram = 
        {
            id         = Guid.NewGuid()       
            numOfBins  = 0 
            rangeStart = 0
            rangeEnd   = 0
            bins       = HashMap.empty
        }

    let initBin = 
        {            
            value       = 0
            start       = 0
            theEnd      = 0
            width       = 0
        }
    

