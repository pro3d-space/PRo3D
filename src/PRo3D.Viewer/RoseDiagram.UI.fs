namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

module RoseDiagramUI =

    let getHoverColor =
        C4b.VRVisGreen
        |> Html.ofC4b 
        |> sprintf "%s"
    
    let drawText (position:V2i) (text:string) (fontSize:string) (textAnchor:string) =
        
        let style' = "font-size:" + fontSize + "px;" + "fill:white"
        
        let textAttr =
            amap{
                yield style style'
                yield attribute "x" (sprintf "%i" position.X)     
                yield attribute "y" (sprintf "%i" position.Y) 
                yield attribute "text-anchor" textAnchor             
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
   
    let hoverTextPos' (center:V2d) (binMiddle:float) (outerRad:float) =
        let p = pointFromAngle center binMiddle (outerRad + 10.0)
        V2i(int(p.X), int(p.Y))

    let averageLine (center:V2d) (innerRad:float) (outerRad:float) (avgAngle:float) (color:string)=

        let startPos = pointFromAngle center avgAngle innerRad
        let endPos = pointFromAngle startPos avgAngle (outerRad-innerRad)
        let p1 = new V2i(int(startPos.X), int(startPos.Y))
        let p2 = new V2i(int(endPos.X), int(endPos.Y))

        let col = "stroke:" + color + ";stroke-width:1"
        
        let attr =
            amap{
                yield style col
                yield attribute "x1" (sprintf "%ipx" p1.X)
                yield attribute "y1" (sprintf "%ipx" p1.Y)
                yield attribute "x2" (sprintf "%ipx" p2.X)
                yield attribute "y2" (sprintf "%ipx" p2.Y)
            }|> AttributeMap.ofAMap

        Incremental.Svg.line attr


    let drawRoseDiagramSection 
        (startAngle:float) 
        (endAngle:float) 
        (center:V2d) 
        (innerRad:float) 
        (outerRad:float) 
        (id:int) 
        (color:string) =

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
            attribute "stroke" color
            onMouseEnter(fun _ -> EnterRDBin id)
            onMouseLeave(fun _ -> ExitRDBin)
        ]


    let drawRoseDiagram (r:AdaptiveRoseDiagramModel) (dimensions:V2i) =
        //two circles as outline
        //individual bin sections
        //dimensions.X = width of the div; dimensions.Y = height of the div
        
        let sect = 
            alist{                            
                let! innerRad = r.innerRad
                let! outerRad = r.outerRad
                let! avgAngle' = r.avgAngle
                let avgAngle = ((avgAngle' + 270.0) % 360.0) * Constant.RadiansPerDegree
                let! peekItem = r.peekItem
                let peekId, peekValue = peekItem |> Option.defaultValue (-1,0.0)
                let peekValue' = ((peekValue + 270.0) % 360.0) * Constant.RadiansPerDegree
            
                //let center = new V2d(float(dimensions.X) /2.0, (float(dimensions.Y) /2.0) - outerRad)
                let center = new V2d(float(dimensions.X) /2.0, (float(dimensions.Y) /2.0))
            
                let! bins = r.bins
                let areaInner = Constant.Pi * (innerRad * innerRad)
                let areaOuter = Constant.Pi * (outerRad * outerRad)
                let areaTotal = areaOuter - areaInner
                let! maxBinValue = r.maxBinValue
                let! hovered = r.hoveredBin
                let! binAngle = r.binAngle
                            
                for i in 0..(bins.Length-1) do
                    let b = bins.Item i  
                    let subArea = (areaTotal / ((float(maxBinValue))/(float(b.count)))) + areaInner
                    let subOuterRadius = Fun.Sqrt(subArea / Constant.Pi)
                    let startRadians = ((b.range.Min+270.0)%360.0) * Constant.RadiansPerDegree
                    let endRadians = ((b.range.Max+270.0)%360.0) * Constant.RadiansPerDegree

                    let color, drawText' =
                        match hovered with 
                        | Some id -> if id = b.id then (getHoverColor, true) else ("none", false)
                        | None -> ("none", false)                          

                    yield drawRoseDiagramSection startRadians endRadians center innerRad subOuterRadius b.id color 
                   
                    if drawText' then                        
                        let angleHalf = binAngle/2.0
                        let binStart = (b.range.Min+270.0)%360.0
                        let binMiddle = ((binStart + angleHalf) % 360.0) * Constant.RadiansPerDegree
                        yield drawText (hoverTextPos' center binMiddle outerRad) (sprintf "%i" b.count) "10" "middle" 

                    if i = peekId then
                        yield averageLine center innerRad outerRad peekValue' "blue"

                let N = bins |> List.fold (fun acc bin -> acc + bin.count) 0
                
                yield drawCircle center innerRad
                yield drawCircle center outerRad
                yield drawText (V2i(10, 20)) (sprintf "N = %i" N) "12" "start"                
                yield averageLine center innerRad outerRad avgAngle "red"

                

                //just for testing
                //yield averageLine center innerRad outerRad (((270.0 + 270.0 ) % 360.0) * Constant.RadiansPerDegree) "magenta"
                //yield averageLine center innerRad outerRad (((180.0 + 270.0 ) % 360.0) * Constant.RadiansPerDegree) "green"
                //yield averageLine center innerRad outerRad (((90.0 + 270.0 ) % 360.0) * Constant.RadiansPerDegree) "yellow"
                //yield averageLine center innerRad outerRad (((360.0 + 270.0 ) % 360.0) * Constant.RadiansPerDegree) "white"


            }
        Incremental.Svg.svg AttributeMap.empty sect

    
    let binAngleDropDown =
        div [ clazz "ui menu"; style "width:150px; height:20px;padding:0px; margin:0px"] [
            onBoot "$('#__ID__').dropdown('on', 'hover');" (
                div [ clazz "ui dropdown item"; style "width:100%"] [
                    i [clazz "dropdown icon"; style "margin:0px 5px"] []
                    text "bin width"
                    div [ clazz "ui menu"] [
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetBinAngle 1.0)] [text "1°"]
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetBinAngle 15.0)] [text "15°"]                        
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetBinAngle 45.0)] [text "45°"] 
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetBinAngle 90.0)] [text "90°"] 
                    ]
                ]
            )
        ] 

    let binAngleDropDown' (r:AdaptiveRoseDiagramModel) = 
        
        let angles = [|1.0; 15.0; 45.0; 90.0|] 
        let values = AMap.ofArray((angles |> Array.map (fun v -> (v, text (sprintf "%.0f°" v)))))
                       
        Html.table[
            Html.row "Bin width" [dropdown1 [ clazz "ui inverted selection dropdown" ] values r.binAngle SetBinAngle]                
        ]
        