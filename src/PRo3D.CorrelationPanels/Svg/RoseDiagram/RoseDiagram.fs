namespace Svgplus

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module RoseDiagram =
    open Aardvark.Base
    open Aardvark.UI
    open Aardvark.Base.Incremental
    open Svgplus.Base
    open Svgplus.RoseDiagramModel
    open Svgplus

    type RoseDiagramAction =
        | ChangeBin of Bin
        | UpdatePosition of V2d
        //| OnClick   

    // angle in range [0°, 360°]
    let init (angles: list<float>) = 
        let binCount = 16
        let binAngle = 360.0 / (float binCount)
        let binAngleHalf = binAngle / 2.0

        let binColors = gradient_blue_green binCount

        let diagramDimension = 50.0 // CAUTION Text is not included in this dimensions

        let calcAvg (anglesInDegree : list<float>) = 
            let tempList = 
                anglesInDegree 
                |> List.map (fun degre -> 
                    let rad = degre.RadiansFromDegrees()
                    Fun.Sin(rad), Fun.Cos(rad))

            let sumSin = tempList |> List.map fst |> List.sum
            let sumCos = tempList |> List.map snd |> List.sum

            let count = anglesInDegree |> List.length |> float
            let averageAngleRadians = Fun.Atan2(sumSin/count,sumCos/count)
            averageAngleRadians.DegreesFromRadians()

        //let check1 = [355.0; 5.0; 15.0] |> calcAvg
        //Log.line "check should be 5° it is: %Arad or %A°" check1 (check1.DegreesFromRadians())    // works

        let binMap = 
            angles 
            |> List.map (fun angle -> 
                let shifted = (angle + (binAngle / 2.0)) % 360.0
                let bin = shifted/binAngle |> int
                bin)
            |> List.groupBy id
            |> List.map (fun (bin, anglesInBin) -> bin, anglesInBin |> List.length)
            |> Map.ofList

        let countPerBin = 
            [ 
                for i in 0 .. binCount-1 -> 
                    let angleCount = 
                        binMap 
                        |> Map.tryFind i
                        |> Option.defaultValue 0

                    let centerAngle = (float i * binAngle) % 360.0
                    let northCenterAngle = centerAngle + 270.0 % 360.0
                    let startDegree = (northCenterAngle - binAngleHalf + 360.0) % 360.0
                    let endDegree = northCenterAngle + binAngleHalf % 360.0
                    {
                        number = i
                        value = float angleCount
                        colour = binColors.Item i
                        startAngle = { radians = startDegree * Constant.RadiansPerDegree }
                        endAngle = { radians = endDegree * Constant.RadiansPerDegree }
                    }
            ]
            |> PList.ofList

        let maxItemsInBin = binMap |> Map.fold (fun state _ value -> if value > state then value else state) 0
        let totalItems = binMap |> Map.fold (fun state _ value -> state + value) 0
          
        let avgAngle = calcAvg angles
        let shiftedAverage = ((avgAngle + 270.0 ) % 360.0) * Constant.RadiansPerDegree

        {
            id            = RoseDiagramId.createNew()
            outerRadius   = diagramDimension / 2.0  // 50
            innerRadius   = diagramDimension / 10.0 // 10 (fixed?)
            countPerBin   = countPerBin
            maxItemsInBin = maxItemsInBin
            totalItems    = totalItems
            averageAngle = { radians = shiftedAverage }
            pos           = V2d.Zero
        }

    let update (model : RoseDiagram) (action : RoseDiagramAction) =
        match action with
        | ChangeBin bin -> 
            let _bins = PList.setAt bin.number bin model.countPerBin
            { model with countPerBin = _bins}
        | UpdatePosition p -> { model with pos = p }

    let view (model : MRoseDiagram) = 
        let lst = 
            alist {
                let centre = V2d.Zero
                let strokeWidth = 1.0
                let! totalElements = model.totalItems
                
                if totalElements <> 0 then
                    let! outer = model.outerRadius
                    let! inner = model.innerRadius

                    let outerArea = Constant.Pi * outer * outer
                    let innerArea = Constant.Pi * inner * inner
                    let maxArea = (outerArea - innerArea)

                    let! maxElements = model.maxItemsInBin |> Mod.map float

                    let calculateSubRadius x = 
                        let subArea = (maxArea / (maxElements/x)) + innerArea
                        let subOuterRadius = Fun.Sqrt(subArea / Constant.Pi)
                        //let subArea = (subOuterRadius * subOuterRadius * Constant.Pi) - innerArea
                        //printfn "x: %A with Radius: %A with Area: (%A/%A)  in porpotion: %.2f%%" x subOuterRadius subArea maxArea ((subArea/maxArea) * 100.0)
                        subOuterRadius

                    let donutSegments = 
                        model.countPerBin
                        |> AList.map (fun bin -> 
                            let subRadius = calculateSubRadius bin.value
                            drawDonutSegment centre inner subRadius bin.startAngle bin.endAngle C4b.Black) // for use-case color is black! otherwise use -> bin.colour)

                    yield! donutSegments

                    //// linear circles visualization (show 5 rings)
                    //let circleDist = (outer - inner) / 5.0
                    //yield! AList.ofList (drawConcentricCircles' centre outer inner C4b.Gray 5 circleDist (strokeWidth * 3.0))
                
                    //// area circles (show 5 rings)
                    //let ringCount = 4.0 // 0% 25% 50% 75% 100%
                    //for x in 0.0 .. (maxElements / ringCount) .. maxElements do
                    //    yield drawCircle' centre (calculateSubRadius x) C4b.Gray strokeWidth false
                    
                    // min and max circle
                    yield drawCircle' centre inner C4b.Black strokeWidth false
                    yield drawCircle' centre outer C4b.Black strokeWidth false

                    let averageLine = 
                        alist {
                            let! a = model.averageAngle
                            let startPoint = pointFromAngle centre a inner
                            yield drawLineFromAngle startPoint a (outer - inner) C4b.Red strokeWidth
                        }
                    yield! averageLine

                    //let angleList = model.countPerBin |> AList.map (fun x -> x.startAngle) |> AList.toList // CAUTION breaks incremental-evaluation!
                    //yield! AList.ofList (drawStarLines angleList centre outer inner C4b.Gray strokeWidth)

                    let! textContent = model.totalItems |> Mod.map (fun x -> sprintf "N = %A" x)
                    yield drawText (V2d(0.0, outer+10.0)) textContent CorrelationDrawing.Orientation.Horizontal
            }

        let transformationAttributes =
            let atts =
                amap {
                    let! offset = model.pos 
                    let! radius = model.outerRadius
                    let transform = sprintf "translate(%f %f)" (offset.X + radius + 10.0) offset.Y // left-center MAGIC +10px
                    yield attribute "transform" transform
                }
            atts

        Incremental.Svg.g (AttributeMap.ofAMap transformationAttributes) lst