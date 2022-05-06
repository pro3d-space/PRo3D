namespace Pro3D.AnnotationStatistics

open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive

module HistogramUI =

    let yPixelValue (value:int) (maximum:int) (divHeight:int) =
        let relation = float(value) / float(maximum)
        int(float(divHeight) * relation)        

    let rectanglesFromBins 
        (bin:BinModel) 
        (idx:int) 
        (binWidth:int) 
        (maxBinValue:int)
        (divHeight:int) =
        
        let binHeight = yPixelValue bin.count maxBinValue divHeight
           
        [
            yield style "fill:green"                
            yield attribute "x" (sprintf "%ipx" (15 + idx * binWidth))
            yield attribute "y" (sprintf "%ipx" (divHeight-binHeight))
            yield attribute "width" (sprintf "%ipx" binWidth) 
            yield attribute "height" (sprintf "%ipx" binHeight) 
        ] |> AttributeMap.ofList

    let drawHistogram' (h: AdaptiveHistogramModel) (dimensions:V2i) =
        let width = dimensions.X
        let height = dimensions.Y
        let xStart = 15        

        let rectangles =            
            alist
             { 
                let! bins = h.bins
                let! max = h.maxBinValue
                let binWidth = (width-xStart) / bins.Length
                for i in 0..(bins.Length-1) do
                    let bin = bins.Item i                    
                    yield Incremental.Svg.rect (rectanglesFromBins bin i binWidth max height)         
             }
              
        Incremental.Svg.svg AttributeMap.empty rectangles

            



    let drawHistogram (h: AdaptiveHistogramModel) (width:int) = 
        
        let height = 10
        let offSetY = 100        
        let xStart = 15

        let attrRects (bin:BinModel) (idx:int)= 
            amap{
                let! n = h.numOfBins.value
                let binV = bin.count                     
                let w = (width-xStart) / (int(n))
                
                yield style "fill:green;fill-opacity:1.0"                
                yield attribute "x" (sprintf "%ipx" (xStart + idx * w))
                yield attribute "y" (sprintf "%ipx" ((height-(binV*10)+offSetY)))
                yield attribute "width" (sprintf "%ipx" w) 
                yield attribute "height" (sprintf "%ipx" (binV*10)) 
            } |> AttributeMap.ofAMap       

        let attrText (idx:int) (labelLength:int)= 
            amap{
                let! n = h.numOfBins.value              
                let w = (width-xStart) / (int(n))                
                let x = (xStart + idx * w)
                let labelWidthPx = (labelLength*5)
                let y = height + offSetY + labelWidthPx
                let rotation = 
                    let basis = 40.0
                    let add = (float(labelWidthPx) / float(w)) * 20.0
                    let additional = int(basis + add)
                    let r = 
                        if additional > 90 
                            then 90
                        else 
                            additional
                    sprintf "%i" r
 
                let transform = 
                    let stringX = sprintf "%i" x
                    let stringY = sprintf "%i" y                                
                    let translate = "translate(" + stringX + " " + stringY + ")"
                    let rotate = "rotate(-" + rotation + ")"                              
                    translate + " " + rotate

                yield style "font-size:8px; fill:white; position:center"
                yield attribute "x" "0"
                yield attribute "y" "0"
                yield attribute "transform" transform
            } |> AttributeMap.ofAMap 

        let attrLine =
            amap{
                let! maxBinValue = h.maxBinValue
                yield style "stroke:white;stroke-width:2"
                yield attribute "x1" "10px"
                yield attribute "y1" (sprintf "%ipx" ((height+offSetY)))
                yield attribute "x2" "10px"
                yield attribute "y2" (sprintf "%ipx" ((height-(maxBinValue*10)+offSetY)))
            }|> AttributeMap.ofAMap 

        let attrTickLine (y:int) =
            amap{
                yield style "stroke:green; stroke-opacity:0.3"               
                yield attribute "x1" (sprintf "%ipx" xStart)
                yield attribute "y1" (sprintf "%ipx" y)
                yield attribute "x2" (sprintf "%ipx" width)
                yield attribute "y2" (sprintf "%ipx" y)
            }|> AttributeMap.ofAMap 

        let attrTickLabel (y:int) =
            amap{
                yield style "font-size:8px; fill:white"
                yield attribute "x" "0px"
                yield attribute "y" (sprintf "%ipx" y)                
            }|> AttributeMap.ofAMap 

        let rectangles =            
            alist{ 
                let! bins = h.bins
                //bins as rectangles + labels
                for i in 0..(bins.Length-1) do
                    let bin = bins.Item i
                    let label = sprintf "%i-%i" (int(bin.range.Min)) (int(bin.range.Max))
                    yield Incremental.Svg.rect (attrRects bin i)
                    yield Incremental.Svg.text (attrText i label.Length) (AVal.constant label)
                
                //y axis
                yield Incremental.Svg.line attrLine

                //y axis ticks + labels
                let! maxBinValue = h.maxBinValue
                let y1 = height+offSetY
                let y2 = height-(maxBinValue*10)+offSetY
                let gap = (y2 - y1) / maxBinValue                
                for j in 0..maxBinValue do                                    
                    yield Incremental.Svg.line (attrTickLine (y1 + (j*gap)))
                    yield Incremental.Svg.text (attrTickLabel (y1 + (j*gap))) (AVal.constant (j.ToString()))
            }
        Incremental.Svg.svg AttributeMap.empty rectangles
    
    let histogramSettings (hist:AdaptiveHistogramModel) =
           div [style "width:100%; margin: 0 0 5 0"] [                
               text "Histogram Settings"
               Html.table[
                   Html.row "domain min" [Numeric.view' [InputBox] hist.domainStart |> UI.map SetDomainMin]
                   Html.row "domain max" [Numeric.view' [InputBox] hist.domainEnd |> UI.map SetDomainMax]
                   Html.row "number of bins" [Numeric.view' [InputBox] hist.numOfBins |> UI.map SetBinNumber]
               ]
           ]
    

