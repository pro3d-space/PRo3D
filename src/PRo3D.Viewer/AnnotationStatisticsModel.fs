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
     start       : float
     theEnd      : float      
     width       : float
}

[<ModelType>]
type Histogram = {
    [<NonAdaptive>]
    id          : Guid    
    data        : List<float>
    maxBinValue : int
    numOfBins   : NumericInput
    domainStart : NumericInput
    domainEnd   : NumericInput
    bins        : List<Bin> //HashMap<Guid, Bin>
}

[<ModelType>]
type RoseDiagram = {
    [<NonAdaptive>]
    id        : Guid
    data      : List<float>
    bins      : List<Bin>
}


[<ModelType>]
type Property = {
    [<NonAdaptive>]
    kind        : Prop    
    data        : List<float>
    min         : float
    max         : float
    avg         : float    
    histogram   : Histogram    
    //roseDiagram : RoseDiagram
}


[<ModelType>]
type AnnotationStatisticsModel = {
    selectedAnnotations : HashMap<Guid, Annotation>
    properties          : HashMap<Prop, Property>    
}

module AnnotationStatistics =
    let initial =
        {
            selectedAnnotations = HashMap.empty
            properties          = HashMap.empty                        
        }


    
    let domainNumeric (value:float) = 
        {
            value = value
            min   = 0.01
            max   = 1000.0 
            step  = 1.00
            format = "{0:0.00}"
        }

    let binNumeric =
        {
            value = 5.00
            min = 5.00
            max = 30.00
            step = 1.00
            format = "{0:0.00}"
        }

    //let initBin (n:int) (min:float) (max:float) = 
    //    {            
    //        value       = n
    //        start       = initNumeric min
    //        theEnd      = initNumeric max         
    //    }

    let getBinMaxValue (bins:List<Bin>) =
        bins |> List.map (fun b -> b.value) |> List.max

    let rec createBins outputList (count:int) (idx:int) (start:float) (width:float) (data:List<float>) =
        if (idx > (count-1)) then outputList
        else             
            let next = start + width
            let n = data |> List.fold (fun acc item -> if (item >= start && item <= next) then acc + 1
                                                       else acc + 0
                                                   ) 0
            

            let bin = [{
                            value       = n
                            start       = start
                            theEnd      = next      
                            width       = width          
                      }]
            createBins (outputList @ bin) count (idx+1) next width data 



    let initHistogram (min:float) (max:float) (data:List<float>) = 
        let domainStart = floor(min)
        let domainEnd = ceil(max)        
        let binWidth = (domainEnd-domainStart) / binNumeric.value
        let bins = createBins [] (int(binNumeric.value)) 0 domainStart binWidth data

        {
            id          = Guid.NewGuid()       
            numOfBins   = binNumeric 
            maxBinValue = getBinMaxValue bins
            domainStart = domainNumeric domainStart
            domainEnd   = domainNumeric domainEnd
            data        = data
            bins        = bins //[(Guid.NewGuid(), (initBin n min max))] |> HashMap.ofList
        }




    

