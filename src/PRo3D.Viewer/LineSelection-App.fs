namespace Pro3D.AnnotationStatistics

open System
open Adaptify
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering
open FSharp.Data.Adaptive


type LineSelectionAction =
    | StartDrawing
    | StartLine of V2i
    | UpdateLine of V2i
    | FinishSelection

module LineSelectionApp =    
    
    //TODO
    let update (m:LineSelectionModel) (act:LineSelectionAction) =
        match act with
        | StartDrawing -> {m with draw = true}
        | StartLine start -> {m with lineStart = start; lineEnd = start}
        | UpdateLine curr -> {m with lineEnd = curr}
        | FinishSelection -> {m with draw = false}



    let dependencies =
        [
            { kind = Script; url = "resources/LineSelectionHelp.js"; name = "LineSelectionHelp" }
        ]
    
    let myMouseDown (cb : V2i -> 'msg) =
        onEvent "onmousedown" ["transform_to_svgCoords(event, 'lineSelectionSVG')"] (List.head >> Pickler.json.UnPickleOfString >> cb)

    let myMouseMove (cb : V2i -> 'msg) =
        onEvent "onmousemove" ["transform_to_svgCoords(event, 'lineSelectionSVG')"] (List.head >> Pickler.json.UnPickleOfString >> cb)

    let myMouseUp (cb : V2i -> 'msg) =
        onEvent "onmouseup" ["transform_to_svgCoords(event, 'lineSelectionSVG')"] (List.head >> Pickler.json.UnPickleOfString >> cb)



    let view (m:AdaptiveLineSelectionModel) (viewports:amap<string,V2i>) (viewportID:string) =

        //let dom = 
        //    alist{
        //        //let! viewPort = 
        //        //    viewports
        //        //    |> AMap.tryFind viewportID 
        //        //    |> AVal.map (Option.defaultValue V2i.II)
        //        //Log.line "[viewport sizes] X:%A Y:%A" viewPort.X viewPort.Y 
        //        let! s = m.lineStart
        //        let! e = m.lineEnd
        //        let xPosSt = (sprintf "%ipx" s.X)
        //        let yPosSt = (sprintf "%ipx" s.Y)
        //        let xPosEn = (sprintf "%ipx" e.X)
        //        let yPosEn = (sprintf "%ipx" e.Y)
        //        let style' = "position: absolute; " + "top: " + yPosSt + "; left: " + xPosSt + "; color:white"
        //        let style'' = "position: absolute; " + "top: " + yPosEn + "; left: " + xPosEn + "; color:white"

        //        div[
        //            style style'
        //            attribute "width" "30px"
        //            attribute "height" "30px"
        //            ][text "start"] 

        //        div[
        //            style style''
        //            attribute "width" "30px"
        //            attribute "height" "30px"
        //            ][text "end"] 
        //    }

        
        //let line = 
        //    let attr =
        //        amap{                                      
        //            let! s = m.lineStart
        //            let! e = m.lineEnd
        //            yield style "stroke:white;stroke-width:2"                   
        //            yield attribute "x1" (sprintf "%ipx" s.X)
        //            yield attribute "y1" (sprintf "%ipx" s.Y)
        //            yield attribute "x2" (sprintf "%ipx" e.X)
        //            yield attribute "y2" (sprintf "%ipx" e.Y)
        //        }|> AttributeMap.ofAMap 

        //    alist{
        //        Incremental.Svg.line attr
        //    }
        
        //Incremental.div AttributeMap.empty line
        let lineAttr =
                       amap{                                      
                           let! s = m.lineStart
                           let! e = m.lineEnd
                           yield style "stroke:white;stroke-width:2"   
                           //yield clazz "selectionLine"
                           //yield onMouseDown StartLine 
                           //yield onMouseMove UpdateLine
                           //yield onMouseUp UpdateLine
                           yield attribute "x1" (sprintf "%ipx" s.X)
                           yield attribute "y1" (sprintf "%ipx" s.Y)
                           yield attribute "x2" (sprintf "%ipx" e.X)
                           yield attribute "y2" (sprintf "%ipx" e.Y)
                       }|> AttributeMap.ofAMap 
        
        //Incremental.Svg.line lineAttr

        let svgAttr = 
            amap{           
                let! viewPort = 
                    viewports
                    |> AMap.tryFind viewportID 
                    |> AVal.map (Option.defaultValue V2i.II)    
                let style' = sprintf "width:%ipx; height:%ipx" viewPort.X viewPort.Y
                yield style style'  
                yield clazz "lineSelectionSVG"
                yield myMouseDown StartLine 
                yield myMouseMove UpdateLine
                yield myMouseUp UpdateLine                
            }|> AttributeMap.ofAMap 
         

        Incremental.Svg.svg (svgAttr) ( 
            AList.ofList [Incremental.Svg.line lineAttr]
        )
        
        
        
        //require dependencies (
        //        body [] [                    
        //            Incremental.Svg.line lineAttr
        //        ]
        //    )

        

