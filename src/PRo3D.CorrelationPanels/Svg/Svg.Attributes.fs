namespace Svgplus

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI

module Attributes =
    
  let inline atf (attributeName : string) (value : float) =
    attribute attributeName (sprintf "%.4f" value)

  let inline ats (attributeName : string) (value : string) =
    attribute attributeName value

  let inline atc (attributeName : string) (value : C4b) =
    attribute attributeName (Html.ofC4b value)

  let inline g'' a x = elemNS "g" Svg.svgNS a x

  let toGroup (content : List<DomNode<'a>>) (atts : List<Attribute<'a>>) =
    g'' ([clazz "g"] @ atts) content

  module Incremental =      
    open CorrelationDrawing

    let inline elemNS' (tagName : string) (ns : string) (attrs : AttributeMap<'msg>) (children : alist<DomNode<'msg>>) =
      DomNode.Node(tagName, ns, attrs, children)

    let inline g' x = elemNS' "g" Incremental.Svg.svgNS x

    let toGroup (content : alist<DomNode<'a>>) 
                (atts : amap<string, AttributeValue<'a>>) =
      let foo = 
          (amap {yield (clazz "g")})
      let atts = 
        AttributeMap.ofAMap (AMap.union foo atts)
      g' atts content

    let inline position (value : aval<V2d>) =
      amap {
        let! v = value
        yield atf "cx" v.X
        yield atf "cy" v.Y
      }

    let inline xywh (leftUpper : aval<V2d>) (dim : aval<Size2D>) =
      amap {
        let! pos = leftUpper
        let! dim = dim
        yield atf "x" pos.X
        yield atf "y" pos.Y
        yield atf "width"  dim.width
        yield atf "height" dim.height
      }

    let inline xywh' (centre : aval<V2d>) (dim : aval<Size2D>) =
      amap {
        let! centre = centre
        let! dim = dim 
        yield atf "x" (centre.X - dim.width * 0.5)
        yield atf "y" (centre.Y - dim.height * 0.5)
        yield atf "width"  dim.width
        yield atf "height" dim.height
      }

    let inline stroke (color : aval<C4b>)
                      (width : aval<float>) =
      amap {
        let! c = color
        yield atc "stroke" c
        let! w = width
        yield atf "stroke-width" w
      }    

    let inline radius (value : aval<float>) =
      amap {
        let! v = value
        yield atf "r" v
      }

    let inline fill (color : aval<C4b>) =
      amap {
         let! c = color
         yield atc "fill" c
      }

    let inline bFill (fill : aval<bool>) (color : aval<C4b>) =
      amap {
         let! c = color
         let! fill = fill
         if fill then yield atc "fill" c
      }

    let circle  (_position  : aval<V2d>)
                (_color     : aval<C4b>)
                (_width     : aval<float>)
                (_radius    : aval<float>)
                (_fill      : aval<bool>) =
      let atts = 
        (position _position)
          |> AMap.union (stroke _color _width)
          |> AMap.union (radius _radius)
      atts
        |> AMap.union (bFill _fill _color)

    

    