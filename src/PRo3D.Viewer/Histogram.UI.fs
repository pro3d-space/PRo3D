namespace Pro3D.AnnotationStatistics

open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive
open PRo3D.Base

module HistogramUI =
    
    //calculate everything with float, round only at the end
    let yPixelValue (value:int) (rangeFrom:Range1i) (rangeTo:Range1i) =   
        let fVal = float(value)
        let fMinTo = float(rangeTo.Min)
        let fMinFrom = float(rangeFrom.Min)
        let fRangeFrom = float(rangeFrom.Size)
        let fRangeTo = float(rangeTo.Size)

        int(round(fMinTo + fRangeTo * ((fVal-fMinFrom) / fRangeFrom)))
        //rangeTo.Min + rangeTo.Size * (value-rangeFrom.Min) / rangeFrom.Size

    let drawText (position:V2i) (text:string) =
           
           let textAttr =
               amap{
                   yield style "font-size:10px; fill:white"
                   yield attribute "x" (sprintf "%i" position.X)     
                   yield attribute "y" (sprintf "%i" position.Y) 
                   yield attribute "text-anchor" "middle"                
               }|> AttributeMap.ofAMap
           
           Incremental.Svg.text textAttr (AVal.constant text)

    let getHoverColor =
        C4b.VRVisGreen
        |> Html.ofC4b 
        |> sprintf "%s"

    let hoverRectangle
        (x:int) 
        (y:int) 
        (id:int)
        (width:int) 
        (height:int)
        (style':string)
         = 
     
        [   
            yield onMouseEnter(fun _ -> EnterBin id)
            yield onMouseLeave(fun _ -> ExitBin)
            yield style style'                
            yield attribute "x" (sprintf "%ipx" x)
            yield attribute "y" (sprintf "%ipx" y)
            yield attribute "width" (sprintf "%ipx" width) 
            yield attribute "height" (sprintf "%ipx" height) 
            yield attribute "pointer-events" "all"
        ] |> AttributeMap.ofList

    let rectangleFromBin
        (x:int) 
        (y:int) 
        (width:int) 
        (height:int)
        (style':string)
         = 
         
        [   
            yield style style'                
            yield attribute "x" (sprintf "%ipx" x)
            yield attribute "y" (sprintf "%ipx" y)
            yield attribute "width" (sprintf "%ipx" width) 
            yield attribute "height" (sprintf "%ipx" height) 
            yield attribute "pointer-events" "none"
        ] |> AttributeMap.ofList
    
    let axis (xDomain:Range1i) (yDomain:Range1i) (strokeCol:string) (strokeWidth:string)=
        
        let style' = "stroke:" + strokeCol + ";stroke-width:" + strokeWidth

        let attributes = 
            [
                yield style style'
                yield attribute "x1" (sprintf "%ipx" xDomain.Min)
                yield attribute "y1" (sprintf "%ipx" yDomain.Min)
                yield attribute "x2" (sprintf "%ipx" xDomain.Max)
                yield attribute "y2" (sprintf "%ipx" yDomain.Max)
            ]
        Svg.line attributes

    let axisTicks (coords: List<V4i>) = 

        let attributes (pos:V4i) = 
            amap{
                yield style "stroke:white;stroke-width:1"
                yield attribute "x1" (sprintf "%ipx" pos.X)
                yield attribute "y1" (sprintf "%ipx" pos.Y)
                yield attribute "x2" (sprintf "%ipx" pos.Z)
                yield attribute "y2" (sprintf "%ipx" pos.W)
            }|> AttributeMap.ofAMap

        [
            for c in coords do                
                yield Incremental.Svg.line (attributes c)
        ]

    let axisLabels (coords: List<float*V2i>) (textRotation:Option<string>) (textAnchor:string) (formatting:bool) =
        
        let tickLabelAttr (x:int) (y:int) =

            let xStr = (sprintf "%i" x)
            let yStr = (sprintf "%i" y)

            let x',y',transformation = 
                match textRotation with
                | Some rotation ->                     
                    let translation = "translate(" + xStr + " " + yStr + ")"
                    let tr = translation + " " + rotation 
                    ("0", "0", tr)
                | None -> (xStr,yStr,"")
                            
            amap{
                yield style "font-size:8px; fill:white"
                yield attribute "x" x'     
                yield attribute "y" y'
                yield attribute "text-anchor" textAnchor
                yield attribute "transform" transformation
            }|> AttributeMap.ofAMap

        [
            for c in coords do
                let value,xy = c
                let str = if formatting then (Formatting.Len(value).ToString()) else ((int(value)).ToString())
                yield Incremental.Svg.text (tickLabelAttr xy.X xy.Y) (AVal.constant str)
        ]

    let labelRotation (scalar:float) =
        let basis = 40.0
        let add = scalar * 20.0
        let additional = int(basis + add)
        let r = 
            if additional > 90 
                then 90
            else 
                additional
        let rotation = sprintf "%i" r

        "rotate(-" + rotation + ")"  

       
    
    //Do Not delete
    //note: it is assumed that divHeight can be divided by 10 without residue
    //e.g. 100, 200, 350, 420 etc. but not e.g. 123, 555, 1024 etc.
    //let yAxisLabelsAdaptive
    //    (stepSize:int)
    //    (step:int)
    //    (tickCount:int)
    //    (maximum:int)
    //    (divWidth:int) 
    //    (divHeight:int) 
    //    (startX:int)=
        

    //    let tickLineAttr (y:int) =
    //        amap{
    //            yield style "stroke:green; stroke-opacity:0.3"               
    //            yield attribute "x1" (sprintf "%ipx" startX)
    //            yield attribute "y1" (sprintf "%ipx" y)
    //            yield attribute "x2" (sprintf "%ipx" divWidth)
    //            yield attribute "y2" (sprintf "%ipx" y)
    //        }|> AttributeMap.ofAMap
             

    //    [
    //        for i in 0..step..tickCount do   
    //            let value = (i*stepSize)
    //            let y = yPixelValue value maximum divHeight
    //            let yInv = divHeight - y                 
    //            yield Incremental.Svg.line (tickLineAttr yInv)
    //            yield Incremental.Svg.text (tickLabelAttr 0 yInv) (AVal.constant (value.ToString()))
        
    //    ]


    //as long as the ticks computed are > max. ticks, the stepSize is increased (ticking gets more coarse)
    //roundedMax rounds the max count to something that is divisible by stepSize without residue
    //let rec computeTickStepsize 
    //    (maxTicks:int)
    //    (tickValue:int) 
    //    (stepSize:int)
    //    (maxBinCount:int) 
    //    (increase:int) =

    //    if tickValue <= maxTicks then (stepSize,tickValue)
    //    else
    //        let currStepSize = stepSize * increase
    //        let modulo = maxBinCount % currStepSize
    //        let roundedMax = maxBinCount + (currStepSize-modulo)  
    //        let newTickCount = roundedMax / currStepSize
    //        computeTickStepsize maxTicks newTickCount currStepSize maxBinCount (increase+1)
            
 
    let drawHistogram' (h: AdaptiveHistogramModel) (dimensions:V2i) =
        let marginTop = 5
        let marginBottom = 35
        let divWidth = dimensions.X
        let divHeight = dimensions.Y
        let startX = 20   
        let binGap = 10
        let binGapHalf = binGap/2

        let hist =            
            alist
             { 
                let! bins = h.bins
                let! maxCount = h.maxBinValue
                let! max = h.domainEnd.value
                let! min = h.domainStart.value
                let! hoveredBin = h.hoveredBin
                let! peekItem = h.peekItem
                let peekId, peekValue = peekItem |> Option.defaultValue (-1,0.0)

                let! data = h.data
                let meanValue = 
                    data 
                    |> List.map (fun (_,d) -> d)
                    |> List.average
                

                //---code for adaptive y axis labels
                //first define y-Axis labelling properties
                //let maxTicks = 10
                //let initialStepSize = 10
                //let fineUntil = 20
                //let fineTick = 2    //this should be a value that fineUntil is divisible by without residue and results in a tickvalue <= maxTicks
                           
                //let newMaxCount,tickCount,stepSize,step = 
                //    if maxCount < fineUntil then          
                //        let max = if (maxCount % fineTick = 0) then maxCount else (maxCount+1)
                //        (max, max / fineTick, fineTick, 1)
                //    else                        
                //        let stepSize,ticks = computeTickStepsize maxTicks (maxTicks+1) initialStepSize maxCount 1
                //        let roundedMax = stepSize * ticks
                //        (roundedMax, ticks, stepSize, 1)
                //----
                
                let n = bins.Length
                let binWidth = int(floor(float(divWidth-startX-((n+1)*binGap)) / float(n)))
                let meanBinId = int((meanValue-min)/((max-min)/float(n)))

                for i in 0..(bins.Length-1) do
                    let bin = bins.Item i  
                    let x = startX + i * (binWidth+binGap)
                    let binHeight = (yPixelValue bin.count (Range1i(0,maxCount)) (Range1i(0, (divHeight-marginBottom)))) - marginTop
                    let maxHeight = (yPixelValue maxCount (Range1i(0,maxCount)) (Range1i(0, (divHeight-marginBottom)))) - marginTop
                    let y = divHeight-marginBottom-binHeight                    

                    match hoveredBin with
                    | Some b -> 
                        if bin.id = b then
                            let hoverStyle = "fill:none;stroke:"+ getHoverColor + ";stroke-width:2;stroke-opacity:0.8"
                            yield Incremental.Svg.rect (hoverRectangle x marginTop bin.id binWidth maxHeight hoverStyle) 
                            yield Incremental.Svg.rect (rectangleFromBin x y binWidth binHeight "fill:green") 
                            let textX = x+(binWidth/2)
                            yield (drawText (V2i(textX, (marginTop+10))) (sprintf "%i" bin.count))
                        else
                            yield Incremental.Svg.rect (hoverRectangle x marginTop bin.id binWidth maxHeight "fill:none;stroke:green;stroke-width:2;stroke-opacity:0.3")
                            yield Incremental.Svg.rect (rectangleFromBin x y binWidth binHeight "fill:green")
                             
                    | None -> 
                        yield Incremental.Svg.rect (hoverRectangle x marginTop bin.id binWidth maxHeight "fill:none;stroke:green;stroke-width:2;stroke-opacity:0.3") 
                        yield Incremental.Svg.rect (rectangleFromBin x y binWidth binHeight "fill:green")

                    
                    if i = meanBinId then 
                        let y1 = marginTop
                        let y2 = divHeight-marginBottom
                        let x1 = float(x)
                        let x2 = float(x + binWidth)
                        let x''= int(round(x1 + (x2-x1) * ((meanValue-bin.range.Min) / bin.range.Size)))
                        yield axis (Range1i(x'', x'')) (Range1i(y1, y2)) "red" "1"

                    //show peek item if there is any
                    if peekId = i then             
                        //check if the value would be outside the domain
                        let mutable xPeek = 0
                        if peekValue < min then xPeek <- startX
                        elif peekValue > max then xPeek <- (startX + (bins.Length)*binWidth + (bins.Length-1)*binGap + binGapHalf) 
                        else
                            let x1 = float(x)
                            let x2 = float(x + binWidth)
                            xPeek <- int(round(x1 + (x2-x1) * ((peekValue-bin.range.Min) / bin.range.Size)))
                        yield axis (Range1i(xPeek, xPeek)) (Range1i(marginTop, (divHeight-marginBottom))) "aqua" "1"
                    

                let xAxisLabelTransform =                     
                    let str = Formatting.Len(max).ToString() 
                    let threshold = str.Length * 6
                    if threshold > binWidth then
                        Some(labelRotation (float(threshold)/float(binWidth)))
                    else
                        None

                let xCoords = 
                    
                    let yPos = divHeight-(marginBottom/2)
                    [
                    for i in 0..(bins.Length-1) do
                        let bin = bins.Item i  
                        let rangeEnd = bin.range.Max                                                                
                        let xPos = (startX + (i+1)*binWidth + i*binGap + binGapHalf) 

                        //for first bin also show the start value (=domain start)
                        if i = 0 then yield (bin.range.Min, V2i(startX, yPos))

                        yield (rangeEnd, V2i(xPos, yPos))
                    ]

                let xTickCoords =
                    xCoords |> List.map (fun elem -> 
                        let _,c = elem
                        let y' = divHeight - marginBottom
                        let y1 = y' + 3
                        let y2 = y' - 3
                        V4i(c.X, y1, c.X, y2)                        
                    )

                yield (axis (Range1i(15,15)) (Range1i(marginTop,(divHeight-marginBottom))) "white" "2") //y axis
                yield (axis (Range1i(15, divWidth)) (Range1i(divHeight-marginBottom, divHeight-marginBottom)) "white" "2")
                yield! (axisLabels [(0.0, V2i(0,(divHeight-marginBottom))); (float(maxCount), V2i(0, marginTop))] None "start" false) //yAxis Labels
                yield! (axisLabels xCoords xAxisLabelTransform "middle" true) //xAxis Labels
                yield! (axisTicks xTickCoords) //xAxis Ticks
             }
              
        Incremental.Svg.svg AttributeMap.empty hist

    let histogramSettings (hist:AdaptiveHistogramModel) =
        div [style "width:100%; margin-bottom:5"] [                
            text "Settings"
            Html.table[
                Html.row "domain min" [Numeric.view' [InputBox] hist.domainStart |> UI.map SetDomainMin]
                Html.row "domain max" [Numeric.view' [InputBox] hist.domainEnd |> UI.map SetDomainMax]
                Html.row "number of bins" [Numeric.view' [InputBox] hist.numOfBins |> UI.map SetBinNumber]
            ]
        ]

    //old version, use drawHistogram' instead
    //let drawHistogram (h: AdaptiveHistogramModel) (width:int) = 
        
    //    let height = 10
    //    let offSetY = 100        
    //    let xStart = 15

    //    let attrRects (bin:BinModel) (idx:int)= 
    //        amap{
    //            let! n = h.numOfBins.value
    //            let binV = bin.count                     
    //            let w = (width-xStart) / (int(n))
                
    //            yield style "fill:green;fill-opacity:1.0"                
    //            yield attribute "x" (sprintf "%ipx" (xStart + idx * w))
    //            yield attribute "y" (sprintf "%ipx" ((height-(binV*10)+offSetY)))
    //            yield attribute "width" (sprintf "%ipx" w) 
    //            yield attribute "height" (sprintf "%ipx" (binV*10)) 
    //        } |> AttributeMap.ofAMap       

    //    let attrText (idx:int) (labelLength:int)= 
    //        amap{
    //            let! n = h.numOfBins.value              
    //            let w = (width-xStart) / (int(n))                
    //            let x = (xStart + idx * w)
    //            let labelWidthPx = (labelLength*5)
    //            let y = height + offSetY + labelWidthPx
    //            let rotation = 
    //                let basis = 40.0
    //                let add = (float(labelWidthPx) / float(w)) * 20.0
    //                let additional = int(basis + add)
    //                let r = 
    //                    if additional > 90 
    //                        then 90
    //                    else 
    //                        additional
    //                sprintf "%i" r
 
    //            let transform = 
    //                let stringX = sprintf "%i" x
    //                let stringY = sprintf "%i" y                                
    //                let translate = "translate(" + stringX + " " + stringY + ")"
    //                let rotate = "rotate(-" + rotation + ")"                              
    //                translate + " " + rotate

    //            yield style "font-size:6px; fill:white; position:center"
    //            yield attribute "x" "0"
    //            yield attribute "y" "0"
    //            yield attribute "transform" transform
    //        } |> AttributeMap.ofAMap 

    //    let attrLine =
    //        amap{
    //            let! maxBinValue = h.maxBinValue
    //            yield style "stroke:white;stroke-width:2"
    //            yield attribute "x1" "10px"
    //            yield attribute "y1" (sprintf "%ipx" ((height+offSetY)))
    //            yield attribute "x2" "10px"
    //            yield attribute "y2" (sprintf "%ipx" ((height-(maxBinValue*10)+offSetY)))
    //        }|> AttributeMap.ofAMap 

    //    let attrTickLine (y:int) =
    //        amap{
    //            yield style "stroke:green; stroke-opacity:0.3"               
    //            yield attribute "x1" (sprintf "%ipx" xStart)
    //            yield attribute "y1" (sprintf "%ipx" y)
    //            yield attribute "x2" (sprintf "%ipx" width)
    //            yield attribute "y2" (sprintf "%ipx" y)
    //        }|> AttributeMap.ofAMap 

    //    let attrTickLabel (y:int) =
    //        amap{
    //            yield style "font-size:8px; fill:white"
    //            yield attribute "x" "0px"
    //            yield attribute "y" (sprintf "%ipx" y)                
    //        }|> AttributeMap.ofAMap 

    //    let rectangles =            
    //        alist{ 
    //            let! bins = h.bins
    //            //bins as rectangles + labels
    //            for i in 0..(bins.Length-1) do
    //                let bin = bins.Item i
    //                let label = sprintf "%i-%i" (int(bin.range.Min)) (int(bin.range.Max))
    //                yield Incremental.Svg.rect (attrRects bin i)
    //                yield Incremental.Svg.text (attrText i label.Length) (AVal.constant label)
                
    //            //y axis
    //            yield Incremental.Svg.line attrLine

    //            //y axis ticks + labels
    //            let! maxBinValue = h.maxBinValue
    //            let y1 = height+offSetY
    //            let y2 = height-(maxBinValue*10)+offSetY
    //            let gap = (y2 - y1) / maxBinValue                
    //            for j in 0..maxBinValue do                                    
    //                yield Incremental.Svg.line (attrTickLine (y1 + (j*gap)))
    //                yield Incremental.Svg.text (attrTickLabel (y1 + (j*gap))) (AVal.constant (j.ToString()))
    //        }
    //    Incremental.Svg.svg AttributeMap.empty rectangles
    
    
    

