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
        | StartLine start -> {m with lineStart = start}
        | UpdateLine curr -> {m with lineEnd = curr}
        | FinishSelection -> {m with draw = false}

    let view (m:AdaptiveLineSelectionModel) =

        //testing
        

        let dom = 
            alist{
                let! s = m.lineStart
                let xPos = (sprintf "%ipx" s.X)
                let yPos = (sprintf "%ipx" s.Y)
                let style' = "position: absolute; " + "top: " + yPos + "; left: " + xPos + "; color:white"

                div[
                    style style'
                    attribute "width" "30px"
                    attribute "height" "30px"
                    ][text "test"] 

                
            }
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
        
        Incremental.div AttributeMap.empty dom



