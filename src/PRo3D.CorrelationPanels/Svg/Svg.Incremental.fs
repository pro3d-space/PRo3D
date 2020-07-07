namespace Svgplus

open Aardvark.UI
open Aardvark.Base
open Aardvark.Base.Incremental
open Svgplus.Attributes
open CorrelationDrawing

module Incremental =
    
    let lineAtts 
        (a : IMod<V2d>) 
        (b : IMod<V2d>) 
        (color : IMod<C4b>) 
        (strokeWidth : IMod<float>) 
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
        (a : IMod<V2d>) 
        (b : IMod<V2d>) 
        (color : IMod<C4b>) 
        (strokeWidth : IMod<float>) 
        (actions : amap<string,AttributeValue<'a>>) =

        let atts = lineAtts a b color strokeWidth actions
        Incremental.elemNS' "line" Incremental.Svg.svgNS atts (AList.empty)
    
    let drawDottedLine 
        (a : IMod<V2d>) 
        (b : IMod<V2d>) 
        (color : IMod<C4b>) 
        (strokeWidth : IMod<float>) 
        (dashLength : IMod<float>)
        (dashDist : IMod<float>)
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
        (upperLeft : IMod<V2d>) 
        (radius    : IMod<float>) 
        (color     : IMod<C4b>)
        (stroke    : IMod<float>) 
        (fill      : IMod<bool>) = 

        let atts = 
            (Incremental.circle upperLeft color stroke radius fill)
            |> AttributeMap.ofAMap
    
        Incremental.elemNS' "circle" Incremental.Svg.svgNS atts (AList.empty)
    
    let circle' (atts : amap<string,AttributeValue<'a>>) =
        let a = AttributeMap.ofAMap(atts)
        Incremental.elemNS' "circle" Incremental.Svg.svgNS a (AList.empty)
    
    let clickableRectangle 
        (centre: IMod<V2d>) 
        (width : IMod<float>) 
        (height : IMod<float>) 
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
        (centre: IMod<V2d>) 
        (dim   : IMod<Size2D>) 
        actions  =

        let width  = Mod.map (fun x -> x.width) dim
        let height = Mod.map (fun x -> x.height) dim
        clickableRectangle centre width height actions AList.empty
    
    //let drawBorderedRectangle (leftUpper         : IMod<V2d>) 
    //                          (size              : IMod<Size2D>)
    //                          (fill              : IMod<C4b>) 
    //                          (borderColors      : IMod<BorderColors>)
    //                          (bWeight           : IMod<SvgWeight>)
    //                          (selectionCallback : _ -> 'msg)
    //                          (selected          : IMod<bool>)
    //                          (dottedBorder      : IMod<bool>) =
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

