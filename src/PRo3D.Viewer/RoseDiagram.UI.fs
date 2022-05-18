namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive

module RoseDiagramUI =
    
    let drawText (position:V2i) (text:string) =
        
        let textAttr =
            amap{
                yield style "font-size:12px; fill:white"
                yield attribute "x" (sprintf "%i" position.X)     
                yield attribute "y" (sprintf "%i" position.Y) 
                yield attribute "text-anchor" "start"                
            }|> AttributeMap.ofAMap
        
        Incremental.Svg.text textAttr (AVal.constant text)

    let drawCircle (center:V2d) (radius:float) =
        Svg.circle(
            [
                style "stroke:white;stroke-width:1; fill:none"
                attribute "cx" (sprintf "%f" center.X)      
                attribute "cy" (sprintf "%f" center.Y) 
                attribute "r" (sprintf "%f" radius)                 
            ]
        )

    let pointFromAngle (start : V2d) (angle : float) (length : float) =
        let endX = start.X + Math.Cos(angle) * length
        let endY = start.Y + Math.Sin(angle) * length
        new V2d (endX, endY)

    let drawRoseDiagramSection (startAngle:float) (endAngle:float) (center:V2d) (innerRad:float) (outerRad:float) =
        //polygon points
        let innerStart = pointFromAngle center startAngle innerRad
        let innerEnd = pointFromAngle center endAngle innerRad
        let outerStart = pointFromAngle center startAngle outerRad
        let outerEnd = pointFromAngle center endAngle outerRad

        //polygon path
        //two lines and two circle segments
        let p1 = new V2i(int(innerStart.X), int(innerStart.Y))
        let p2 = new V2i(int(outerStart.X), int(outerStart.Y))
        let p3 = new V2i(int(outerEnd.X), int(outerEnd.Y))
        let p4 = new V2i(int(innerEnd.X), int(innerEnd.Y))
        let c1 = int(outerRad)
        let c2 = int(innerRad)

        Svg.path [
            attribute "d" (sprintf "M %d %d L %d %d A %d %d 0 0 1 %d %d L %d %d A %d %d 0 0 0 %d %d"
                 p1.X p1.Y p2.X p2.Y c1 c1 p3.X p3.Y p4.X p4.Y c2 c2 p1.X p1.Y 
            )
            attribute "fill" "green"
        ]


    let drawRoseDiagram (r:AdaptiveRoseDiagramModel) (dimensions:V2i) =
        //two circles as outline
        //individual bin sections
        //dimensions.X = width of the div; dimensions.Y = height of the div
        
        let sect = 
            alist{                            
                let! innerRad = r.innerRad
                let! outerRad = r.outerRad
            
                let center = new V2d(float(dimensions.X) /2.0, (float(dimensions.Y) /2.0) - outerRad)
            
                let! bins = r.bins
                let areaInner = Constant.Pi * (innerRad * innerRad)
                let areaOuter = Constant.Pi * (outerRad * outerRad)
                let areaTotal = areaOuter - areaInner
                let! maxBinValue = r.maxBinValue
            
                let sections = 
                    bins |> List.map(fun b -> 
                        let subArea = (areaTotal / ((float(maxBinValue))/(float(b.count)))) + areaInner
                        let subOuterRadius = Fun.Sqrt(subArea / Constant.Pi)
                        let startRadians = b.range.Min * Constant.RadiansPerDegree
                        let endRadians = b.range.Max * Constant.RadiansPerDegree
                        drawRoseDiagramSection startRadians endRadians center innerRad subOuterRadius
                    )

                let N = bins |> List.fold (fun acc bin -> acc + bin.count) 0

                yield! sections
                yield drawCircle center innerRad
                yield drawCircle center outerRad
                yield drawText (V2i(20, 20)) (sprintf "N = %i" N)
            }
        Incremental.Svg.svg AttributeMap.empty sect

