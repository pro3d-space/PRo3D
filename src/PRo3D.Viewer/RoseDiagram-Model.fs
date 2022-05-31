namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Aardvark.UI
open Adaptify
open FSharp.Data.Adaptive

[<ModelType>]
type RoseDiagramModel = 
    {
        [<NonAdaptive>]
        id          : Guid
        data        : List<Guid*float>
        maxBinValue : int
        avgAngle    : float
        bins        : List<BinModel>
        center      : V2d
        innerRad    : float
        outerRad    : float
        binAngle    : float //15°
        hoveredBin  : Option<int>
    }

type RoseDiagramModelAction =    
    | UpdateRD of List<Guid*float>
    | EnterRDBin of int
    | ExitRDBin


module RoseDiagramModel =

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
                    id = i
                    count = 0
                    range = Range1d(startDegree, endDegree)
                    annotationIDs = List.empty
                }
        ]

    //count for rose diagram bins
    let sortRoseDiagramDataIntoBins (bins:List<BinModel>) (data:List<Guid*float>) (angle:float) =    
        
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
            | None -> {bin with count = 0}
        )

    let calculateAvgAngle (angles:List<float>) =
        //from RoseDiagram.fs in Correlation Panels
        let tempList = 
            angles 
            |> List.map (fun degre -> 
                let rad = degre.RadiansFromDegrees()
                Fun.Sin(rad), Fun.Cos(rad))

        let sumSin = tempList |> List.map fst |> List.sum
        let sumCos = tempList |> List.map snd |> List.sum

        let count = angles |> List.length |> float
        let averageAngleRadians = Fun.Atan2(sumSin/count,sumCos/count)        
        let angDeg = averageAngleRadians.DegreesFromRadians()
        angDeg
        

    let initRoseDiagram (data:List<Guid*float>) (avg:float)=
        let binAngle = 15.0
        let initB =  initRoseDiagramBins binAngle
        let bins = sortRoseDiagramDataIntoBins initB data binAngle
        let max = BinModel.getBinMaxValue bins        
        let center = V2d.Zero               

        {
            id   = Guid.NewGuid() 
            data = data
            maxBinValue = max
            avgAngle = avg
            bins = bins
            center = center
            innerRad = 5.0
            outerRad = 50.0
            binAngle = binAngle
            hoveredBin = None
        }

