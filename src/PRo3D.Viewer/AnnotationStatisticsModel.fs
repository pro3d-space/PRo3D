namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Aardvark.UI
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation


type Scale = 
    | Metric
    | Angular

type Kind = 
    | LENGTH 
    | BEARING 
    | VERTICALTHICKNESS 
    | DIP_AZIMUTH 
    | STRIKE_AZIMUTH

type Prop = {
    kind : Kind
    scale : Scale
}

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
    bins        : List<Bin> 
}

[<ModelType>]
type RoseDiagram = {
    [<NonAdaptive>]
    id          : Guid
    data        : List<float>
    maxBinValue : int
    bins        : List<Bin>
    center      : V2d
    innerRad    : float
    outerRad    : float
    binAngle    : float //15°
}

[<ModelType>]
type Visualization =
| Histogram of value: Histogram
| RoseDiagram of value: RoseDiagram

[<ModelType>]
type Property = {
    [<NonAdaptive>]
    prop            : Prop    
    data            : List<float>
    min             : float
    max             : float
    avg             : float         
    visualization   : Visualization
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
            properties = HashMap.empty
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

        let hist = 
            {
                id          = Guid.NewGuid()       
                numOfBins   = binNumeric 
                maxBinValue = getBinMaxValue bins
                domainStart = domainNumeric domainStart
                domainEnd   = domainNumeric domainEnd
                data        = data
                bins        = bins 
            }
        hist

    //TODO

    //initial setting, start and end stay constant
    let initBins (angle:float) =
           let binCount = int(360.0 / angle)

           [
           for i in 1..binCount do

               //from RoseDiagram in Correlation Panels
               let binAngleHalf = angle / 2.0
               let centerAngle = (float i * angle) % 360.0
               let northCenterAngle = centerAngle + 270.0 % 360.0
               let startDegree = (northCenterAngle - binAngleHalf + 360.0) % 360.0
               let endDegree = northCenterAngle + binAngleHalf % 360.0
               //

               {
                   value = 0
                   start = startDegree
                   theEnd = endDegree
                   width = angle
               }

           ]
    
    //count for rose diagram bins
    let calculateBinCount (bins:List<Bin>) (data:List<float>) (angle:float) =        
        let counts = data |> List.groupBy (fun value -> int(value / angle)) 
                          |> List.map(fun (id, vals) -> id, (vals |> List.length)) 
                          |> Map.ofList

        bins |> List.mapi (fun i bin -> match (counts.TryFind i) with
                                          | Some count -> {bin with value = count}
                                          | None -> bin
        )




    let initRoseDiagram (data:List<float>) =

        let binAngle = 15.0
        let bins = calculateBinCount (initBins binAngle) data binAngle
        let max = getBinMaxValue bins
                
        {
            id   = Guid.NewGuid() 
            data = data
            maxBinValue = max
            bins = bins
            center = V2d.Zero
            innerRad = 5.0
            outerRad = 20.0
            binAngle = binAngle
        }

        


    let initProp (kind:Kind) (scale:Scale) = 
        {
            kind = kind
            scale = scale
        }

    




    

