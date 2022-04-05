namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Aardvark.UI
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation

type Prop = 
    | LENGTH
    | BEARING
    | VERTICALTHICKNESS

[<ModelType>]
type Bin = {    
     value       : int
     start       : NumericInput
     theEnd      : NumericInput      
}

[<ModelType>]
type Histogram = {
    [<NonAdaptive>]
    id        : Guid    
    data      : List<float>
    numOfBins : int
    rangeStart: float
    rangeEnd  : float
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
    data      : List<float>
    min       : float
    max       : float
    avg       : float    
    histogram : Histogram    
    //roseDiagram : Option<RoseDiagram>
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


    
    let initNumeric (value: float) = 
        {
        value = value
        min   = 0.01
        max   = 1000.0 
        step  = 1.00
        format = "{0:0.00}"
        }

    let initBin (n:int) (min:float) (max:float) = 
        {            
            value       = n
            start       = initNumeric min
            theEnd      = initNumeric max         
        }

    let initHistogram (min:float) (max:float) (n:int) (data:List<float>) = 
        {
            id         = Guid.NewGuid()       
            numOfBins  = 1 
            rangeStart = min
            rangeEnd   = max
            data       = data
            bins       = [(Guid.NewGuid(), (initBin n min max))] |> HashMap.ofList
        }




    

