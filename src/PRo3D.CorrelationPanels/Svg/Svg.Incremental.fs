namespace Svgplus

open Aardvark.UI
open Aardvark.Base
open FSharp.Data.Adaptive
open Svgplus.Attributes
open CorrelationDrawing

module Incremental =
    
    let lineAtts 
        (a : aval<V2d>) 
        (b : aval<V2d>) 
        (color : aval<C4b>) 
        (strokeWidth : aval<float>) 
        (actions : amap<string,AttributeValue<'a>>) =

        amap {
            let! a = a
            let! b = b
            let! c = color
            let! s = strokeWidth
            yield (atf "x1" a.X)
            yield (atf "y1" a.Y)
            yield (atf "x2" b.X)
            yield (atf "y2" b.Y)
            yield (atc "stroke" c)
            yield (atf "stroke-width" s)
        } 
        |> AttributeMap.ofAMap
        |> AttributeMap.union (actions |> AttributeMap.ofAMap)

    let drawLine 
        (a : aval<V2d>) 
        (b : aval<V2d>) 
        (color : aval<C4b>) 
        (strokeWidth : aval<float>) 
        (actions : amap<string,AttributeValue<'a>>) =

        let atts = lineAtts a b color strokeWidth actions
        Incremental.elemNS' "line" Incremental.Svg.svgNS atts (AList.empty)
    
    let drawDottedLine 
        (a : aval<V2d>) 
        (b : aval<V2d>) 
        (color : aval<C4b>) 
        (strokeWidth : aval<float>) 
        (dashLength : aval<float>)
        (dashDist : aval<float>)
        (actions : amap<string,AttributeValue<'a>>) =

        let lineAtts = lineAtts a b color strokeWidth actions
      
        let dashArrayAtt =                        
            amap {
                let! dl = dashLength
                let! dd = dashDist
                yield ats "stroke-dasharray" (sprintf "%f,%f" dl dd)
            } 
            |> AttributeMap.ofAMap
    
        let atts = AttributeMap.union lineAtts dashArrayAtt
    
        Incremental.elemNS' "line" Incremental.Svg.svgNS atts (AList.empty)
    
    let circle 
        (upperLeft : aval<V2d>) 
        (radius    : aval<float>) 
        (color     : aval<C4b>)
        (stroke    : aval<float>) 
        (fill      : aval<bool>) = 

        let atts = 
            (Incremental.circle upperLeft color stroke radius fill)
            |> AttributeMap.ofAMap
    
        Incremental.elemNS' "circle" Incremental.Svg.svgNS atts (AList.empty)
    
    let circle' (atts : amap<string,AttributeValue<'a>>) =
        let a = AttributeMap.ofAMap(atts)
        Incremental.elemNS' "circle" Incremental.Svg.svgNS a (AList.empty)
    
    let clickableRectangle 
        (centre: aval<V2d>) 
        (width : aval<float>) 
        (height : aval<float>) 
        actions 
        children =
        
        let attlist =
            amap {
                let! centre = centre
                let! width = width
                let! height = height
                
                let leftUpper = V2d(centre.X - width * 0.5, centre.Y - height * 0.5)
                yield clazz "clickable"
                yield atf "x" leftUpper.X
                yield atf "y" leftUpper.Y
                yield atf "width" width
                yield atf "height" height
            }
        
        let atts = 
            actions
            |> AMap.union attlist
            |> AttributeMap.ofAMap
                     
        Incremental.elemNS' "rect" Incremental.Svg.svgNS atts children
    
    let clickableRectangle' 
        (centre: aval<V2d>) 
        (dim   : aval<Size2D>) 
        actions  =

        let width  = AVal.map (fun x -> x.width) dim
        let height = AVal.map (fun x -> x.height) dim
        clickableRectangle centre width height actions AList.empty
    
    //let drawBorderedRectangle (leftUpper         : aval<V2d>) 
    //                          (size              : aval<Size2D>)
    //                          (fill              : aval<C4b>) 
    //                          (borderColors      : aval<BorderColors>)
    //                          (bWeight           : aval<SvgWeight>)
    //                          (selectionCallback : _ -> 'msg)
    //                          (selected          : aval<bool>)
    //                          (dottedBorder      : aval<bool>) =
    //  adaptive {
    //    let! size = size
    //    let! leftUpper = leftUpper
    //    let! color = fill
    //    let! bColors = borderColors
    //    let lborder = bColors.lower
    //    let uborder = bColors.upper
    //    let! isSelected = selected 
    //    let! bWeight = bWeight
    
    //    let rfun = Svgplus.Base.drawBorderedRectangle
    //                        leftUpper
    //                        size
    //                        color lborder uborder
    //                        bWeight
    //                        selectionCallback
    //                        isSelected
    
    //    let! dottedBorder = dottedBorder
    //    return match dottedBorder with
    //            | true ->
    //                rfun true
    //            | false ->
    //                rfun false
    //  }

