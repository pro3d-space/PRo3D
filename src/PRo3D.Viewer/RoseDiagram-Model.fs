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
        bins        : List<BinModel>
        center      : V2d
        innerRad    : float
        outerRad    : float
        binAngle    : float //15°
    }

type RoseDiagramModelAction =
    | UpdateBinCount


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
            | None -> bin
        )

    let initRoseDiagram (data:List<Guid*float>) =
        let binAngle = 15.0
        let initB =  initRoseDiagramBins binAngle
        let bins = sortRoseDiagramDataIntoBins initB data binAngle
        let max = BinModel.getBinMaxValue bins
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

