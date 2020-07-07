namespace Svgplus
  
  module Paths =
    open Aardvark.Base
    open Aardvark.UI
    open Svgplus.Attributes

    /////// PATHS
    let inline (>>) (x : string) (y : string) =
      sprintf "%s %s" x y

    let move (a : V2d) =
      sprintf "M%.2f %.2f" a.X a.Y

    let lineTo (a : V2d)  =
      sprintf "L%.2f %.2f" a.X a.Y

    let curveTo (b : V2d) (c : V2d) =
      sprintf "Q %.2f %.2f %.2f %.2f" b.X b.Y c.X c.Y  

    let close = "Z"  

    let buildPath (str : string) (color : C4b) (stroke : float) (fill : bool) =
      let fillAttr =
        match fill with
          | true -> [atc "fill" color]
          | false -> [ats "fill" "none"]
      Svg.path
        ([
          ats "d" str
          atc "stroke" color
          atf "stroke-witdth" stroke
        ]@fillAttr)

    let buildPathRotate (str : string) (color : C4b) (centre : V2d)
                        (stroke : float) (fill : bool) (degrees : int) =
      let fillAttr =
        match fill with
          | true -> [atc "fill" color]
          | false -> [ats "fill" "none"]
      Svg.path
        ([
          ats "d" str
          atc "stroke" color
          atf "stroke-witdth" stroke
          ats "transform" (sprintf "rotate(%i, %i, %i)" degrees 
                                                        (int centre.X) 
                                                        (int centre.Y))
        ]@fillAttr)

    let circleSegmentTo (radius : float) (b : V2d) =
        sprintf "A %f %f, 0, 0, 0, %f %f" radius radius b.X b.Y

    let circleSegmentTo' (radius : float) (b : V2d) =
        sprintf "A %f %f, 0, 0, 1, %f %f" radius radius b.X b.Y
     

    type CurveType = SmoothCurve | BezierCurve | Elliptical
      
    let drawLinePath (points : List<V2d>) (color : C4b) (stroke : float) =
      let col = Html.ofC4b color
      let d =
        move points.Head 
          >> (points 
              |> List.map (fun x -> lineTo x)
              |> List.tail
              |> List.reduce (fun a b -> a >> b)
             )
      Svg.path 
        [
          ats "d" d
          ats "stroke" col
          atf "stroke-width" stroke
          ats "fill" "none"
        ]

    let drawQuadraticBezierCurve (startPoint: V2d) (endPoint: V2d) (color: C4b) (strokeWidth: float) (horizontal: bool) = 
        let s = startPoint
        let e = endPoint
        let mid = (s + e / 2.0)
        let c1 = if horizontal then s + mid.XO else s + mid.OY
        let c2 = if horizontal then e - mid.XO else e - mid.OY
        
        Svg.path [
            ats "d" (sprintf "M %A %A C%A %A , %A %A , %A %A" s.X s.Y c1.X c1.Y c2.X c2.Y e.X e.Y)
            ats "stroke" (Html.ofC4b color)
            atf "stroke-width" strokeWidth
            ats "fill" "none"
        ]

    let drawClickableCubicBezierCurve (startPoint: V2d) (endPoint: V2d) (color : C4b) (strokeWidth: float) (horizontal: bool) (dashWidth: float) (dashDist: float) (fOnClick : _ -> 'a) =
        let s = startPoint
        let e = endPoint
        let mid = (s + e) / 2.0
        let c1 = if horizontal then s.OY + mid.XO else s.XO + mid.OY
        let c2 = if horizontal then e.OY + mid.XO else e.XO + mid.OY
        
        Svg.path ([
            ats "d" (sprintf "M %A %A C%A %A , %A %A , %A %A" s.X s.Y c1.X c1.Y c2.X c2.Y e.X e.Y)
            ats "stroke" (Html.ofC4b color)
            atf "stroke-width" strokeWidth
            ats "stroke-dasharray" (sprintf "%f,%f" dashWidth dashDist)
            ats "fill" "none"
        ]@(Aardvark.UI.Svg.Events.onClickAttributes [fOnClick]))

    //let drawCurvedPath (points : List<V2d>) (curveType : CurveType) =
    //  match curveType with
    //    | CurveType.SmoothCurve ->
    //        points |> List.map (fun x -> curveTo "S" x x) //TODO control points
    //    | CurveType.BezierCurve ->
    //        points |> List.map (fun x -> curveTo "S" x x)
    //    | CurveType.Elliptical ->
    //        points |> List.map (fun x -> curveTo "S" x x)
