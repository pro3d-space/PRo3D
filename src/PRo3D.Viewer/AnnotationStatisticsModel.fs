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

type Prop = 
    {
        kind : Kind
        scale : Scale
    }

[<ModelType>]
type Bin = 
    {    
         count         : int 
         range         : Range1d
         annotationIDs : List<Guid>  //to keep track which annotations are responsible for the count       
    }     

[<ModelType>]
type Histogram = 
    {
        [<NonAdaptive>]
        id          : Guid    
        data        : List<Guid*float>
        maxBinValue : int
        numOfBins   : NumericInput
        domainStart : NumericInput
        domainEnd   : NumericInput
        bins        : List<Bin> 
    }

[<ModelType>]
type RoseDiagram = 
    {
        [<NonAdaptive>]
        id          : Guid
        data        : List<Guid*float>
        maxBinValue : int
        bins        : List<Bin>
        center      : V2d
        innerRad    : float
        outerRad    : float
        binAngle    : float //15°
    }

[<ModelType>]
type Visualization =  //review:very generic naming, we are in a namespace, but this becomes confusing quickly
    | Histogram of value: Histogram
    | RoseDiagram of value: RoseDiagram

[<ModelType>]
type Property = //review: same as visualization
    {
        [<NonAdaptive>]
        prop            : Prop  //review: what is a prop?
        data            : List<Guid*float>  
        dataRange       : Range1d
        avg             : float         
        visualization   : Visualization
    }

[<ModelType>]
type AnnotationStatisticsModel = 
    {
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
        bins |> List.map (fun b -> b.count) |> List.max

    let createHistogramBins (count:int) (min:float) (width:float) =
        [
            for i in 0..(count-1) do
                let start = min + (float(i) * width)
                let en = start + width                
                {                    
                    count = 0
                    range = Range1d(start,en)
                    annotationIDs = List.empty
                }
        ]

    //review: not sure if this is the best way to construct a histogram, but it is functional
    let sortHistogramDataIntoBins (bins:List<Bin>) (data:List<Guid*float>) (min:float) (width:float)=

        let grouping = 
            data 
            |> List.groupBy (fun (_,value) -> 
                let shifted = value - min 
                int(shifted/width)
            )
            |> List.map(fun (binID, innerList) -> 
                let counter = innerList|> List.length
                let annotationIds = innerList |> List.map(fun (id,_) -> id)
                (binID, (counter, annotationIds))
            )
            |> Map.ofList //review: this looks quite smart though

        bins 
        |> List.mapi (fun i bin -> 
            match (grouping.TryFind i) with
            | Some (count,ids) -> { bin with count = count; annotationIDs = ids}
            | None -> bin
        )
    
    let setHistogramBins (data:List<Guid*float>) (min:float) (width:float) (binCount:int) =
        let createBins = createHistogramBins binCount min width
        sortHistogramDataIntoBins createBins data min width
    
    let initHistogram (min:float) (max:float) (data:List<Guid*float>) = 
        let domainStart = floor(min)  //review: range1d
        let domainEnd = ceil(max)        
        let binWidth = (domainEnd-domainStart) / binNumeric.value               
        let bins = setHistogramBins data domainStart binWidth (int(binNumeric.value))
        {
            id          = Guid.NewGuid()       
            numOfBins   = binNumeric 
            maxBinValue = getBinMaxValue bins
            domainStart = domainNumeric domainStart
            domainEnd   = domainNumeric domainEnd
            data        = data
            bins        = bins 
        }
        
    //initial setting, start and end stay constant
    let initRoseDiagramBins (angle:float) =
        let binCount = int(360.0 / angle)

        [
            for i in 0..(binCount-1) do
                //from RoseDiagram.fs in Correlation Panels
                let binAngleHalf = angle / 2.0
                let centerAngle = (float i * angle) % 360.0
                let northCenterAngle = centerAngle + 270.0 % 360.0
                let startDegree = (northCenterAngle - binAngleHalf + 360.0) % 360.0
                let endDegree = (northCenterAngle + binAngleHalf) % 360.0
                //
                {
                    count = 0
                    range = Range1d(startDegree, endDegree)
                    annotationIDs = List.empty
                }
        ]
    
    //count for rose diagram bins
    let sortRoseDiagramDataIntoBins (bins:List<Bin>) (data:List<Guid*float>) (angle:float) =    
        
        let binAngleHalf = angle / 2.0
        let grouping = 
            data 
            |> List.groupBy (fun (_,value) -> 
                let shifted = (value - 270.0 + binAngleHalf + 360.0) % 360.0 
                int(shifted/angle)
            )
            |> List.map(fun (binID, innerList) -> 
                let counter = innerList|> List.length
                let annotationIds = innerList |> List.map(fun (id,_) -> id)
                (binID, (counter, annotationIds))
            )
            |> Map.ofList 

        bins 
        |> List.mapi (fun i bin -> 
            match (grouping.TryFind i) with
            | Some (count,ids) -> { bin with count = count; annotationIDs = ids}
            | None -> bin
        )

    let initRoseDiagram (data:List<Guid*float>) =
        let binAngle = 15.0
        let initB =  initRoseDiagramBins binAngle
        let bins = sortRoseDiagramDataIntoBins initB data binAngle
        let max = getBinMaxValue bins
        let center = V2d.Zero               

        {
            id   = Guid.NewGuid() 
            data = data
            maxBinValue = max
            bins = bins
            center = center
            innerRad = 10.0
            outerRad = 30.0
            binAngle = binAngle
        }

    let initProp (kind:Kind) (scale:Scale) = 
        {
            kind = kind
            scale = scale
        }

