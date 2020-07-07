namespace Svgplus

open System
    
open Aardvark.Base
open Aardvark.UI
open Svgplus.Attributes
open Svgplus.Paths

open CorrelationDrawing
open CorrelationDrawing.Math

module Base =
  
    let inline b0 (a : int) =
        if a < 0 then 0 else a
    
    let inline (%%) (v : V3i) (a : int) =
        V3i((b0 v.X%a),(b0 v.Y%a),(b0 v.Z%a)) 
    
    let V3iToC4b (v : V3i) =
        let _v = v%%255
        new C4b(v)
    
    let inline (--) (c : C4b) (v : V3i) =
        let col = c.ToV3i ()
        V3iToC4b (col - v)
    
    let inline (++) (c : C4b) (v : V3i) =
        let col = c.ToV3i ()
        V3iToC4b (col + v)
    
    let lighten (colour : C4b) =
        C4b(int colour.R - 20, int colour.G - 20, int colour.B - 20)
    
    let gradient (rgb1 : V3d) (rgb2 : V3d) (count : int)= 
        let RGBToC4b (rgb : V3d) =
            C4b(rgb.X |> Math.Round |> int, 
                rgb.Y |> Math.Round |> int, 
                rgb.Z |> Math.Round |> int)
    
        let diff = (rgb1 - rgb2) / 8.0 
    
        [0 .. count]
        |> List.map (fun x -> 
            match x with
            | a when a <= count/2 -> 
                let rgb = (rgb1 - (float a) * diff)
                RGBToC4b rgb
            | a when a >  count/2 ->
                let rgb = (rgb2 +  float (a - (count/2)) * diff)
                RGBToC4b rgb
            | _ -> C4b.VRVisGreen)
    
    let gradient_blue_green count =
        let green = V3d(50,180,30)
        let blue  = V3d(50,30,180)
        gradient green blue count
    
    let gradient_blue_red count = 
        let red   = V3d(180,50,30)
        let blue  = V3d(50,30,180)
        gradient blue red count
    
    let clickableRectangle' (center: V2d) (width : float) (height : float) (fOnClick : _ -> 'a) =
        let leftUpper = V2d(center.X - width * 0.5, center.Y - height * 0.5)
        Svg.rect (
            [
                clazz "clickable"
                atf "x" leftUpper.X
                atf "y" leftUpper.Y
                atf "width" width
                atf "height" height
            ]@(Aardvark.UI.Svg.Events.onClickAttributes [fOnClick]))
    
    let clickableRectangle (center: V2d) (radius : float) (fOnClick : _ -> 'a) =
        let leftUpper = center - radius
        Svg.rect (
            [
                clazz "clickable"
                atf "x" leftUpper.X
                atf "y" leftUpper.Y
                atf "width" (radius * 2.0)
                atf "height" (radius * 2.0)
            ]@(Aardvark.UI.Svg.Events.onClickAttributes [fOnClick]))
    
    let drawBoldText (a : V2d) (str : string) (orientation : Orientation) = //TODO refactor
        let dir = 
            match orientation with
            | Orientation.Vertical   -> [ats "glyph-orientation-vertical" "90"; ats "writing-mode" "tb"]
            | Orientation.Horizontal -> [ats "glyph-orientation-horizontal" "auto"]
        
        Svg.text
            (dir@[
                  atf "x" a.X
                  atf "y" a.Y
                  ats "font-weight" "bold"
                 ]
            ) str
    
    let drawText (a : V2d) (str : string) (orientation : Orientation) =
        let dir = 
            match orientation with
            | Orientation.Vertical   -> [ats "glyph-orientation-vertical" "90"; ats "writing-mode" "tb"]
            | Orientation.Horizontal -> [ats "glyph-orientation-horizontal" "auto"]
        
        Svg.text
            (dir@[
                  atf "x" a.X
                  atf "y" a.Y
                 ]
            ) str

    let drawText' (center : V2d) (str : string) (orientation : Orientation) (textAnchor: TextAnchor)=
        let dir = 
            match orientation with
            | Orientation.Vertical   -> [ats "glyph-orientation-vertical" "90"; ats "writing-mode" "tb"]
            | Orientation.Horizontal -> [ats "glyph-orientation-horizontal" "auto"]

        let textAnchor = 
            match textAnchor with 
            | TextAnchor.Start -> [ ats "text-anchor" "start" ]
            | TextAnchor.Middle -> [ ats "text-anchor" "middle" ]
            | TextAnchor.End -> [ ats "text-anchor" "end" ]
        
        Svg.text
            (dir@textAnchor@[
                  atf "x" center.X
                  atf "y" center.Y
                 
                 ]
            ) str

    
    let drawLine (a : V2d) (b : V2d) (color : C4b) (strokeWidth : float)=
      Svg.line 
        [
          atf "x1" a.X
          atf "y1" a.Y
          atf "x2" b.X
          atf "y2" b.Y
          atc "stroke" color
          atf "stroke-width" strokeWidth
        ]

    let drawClickableDashedLine (a : V2d) (b : V2d) (color : C4b) (strokeWidth : float) (dashWidth : float) (dashDist : float) (fOnClick : _ -> 'a) =
        Svg.line (
            [
                ats "stroke-dasharray" (sprintf "%f,%f" dashWidth dashDist)
                atf "x1" a.X
                atf "y1" a.Y
                atf "x2" b.X
                atf "y2" b.Y
                atc "stroke" color
                atf "stroke-width" strokeWidth
            ]@(Aardvark.UI.Svg.Events.onClickAttributes [fOnClick]))


    let drawDottedLine (a : V2d) (b : V2d) (color : C4b) (strokeWidth : float) (dashWidth : float) (dashDist : float) =
      Svg.line 
        [
          ats "stroke-dasharray" (sprintf "%f,%f" dashWidth dashDist)
          atf "x1" a.X
          atf "y1" a.Y
          atf "x2" b.X
          atf "y2" b.Y
          atc "stroke" color
          atf "stroke-width" strokeWidth
        ]
    
    let drawHorizontalLine (a : V2d) (length : float) (color : C4b) (strokeWidth : float) (fOnClick : _ -> 'a) =
      Svg.line 
        ([
          atf "x1" a.X
          atf "y1" a.Y
          atf "x2" (a.X + length)
          atf "y2" a.Y
          atc "stroke" color
          atf "stroke-width" strokeWidth
        ]@(Aardvark.UI.Svg.Events.onClickAttributes [fOnClick]))
    
    let drawVerticalLine (a : V2d) (length : float) (color : C4b) (strokeWidth : float) =
      Svg.line 
        [
          atf "x1" a.X
          atf "y1" a.Y
          atf "x2" a.X 
          atf "y2" (a.Y + length)
          atc "stroke" color
          atf "stroke-width" strokeWidth
        ]
    
    let drawHorizontalDottedLine  (a : V2d) (length : float) (color : C4b) (strokeWidth : float) (dashWidth : float) (dashDist : float) =
      Svg.line 
        [
          ats "stroke-dasharray" (sprintf "%f,%f" dashWidth dashDist)
          atf "x1" a.X
          atf "y1" a.Y
          atf "x2" (a.X + length)
          atf "y2" a.Y
          atc "stroke" color
          atf "stroke-width" strokeWidth
        ]
    
    let drawVerticalDottedLine (a : V2d) (length : float) (color : C4b) (strokeWidth : float) (dashWidth : float) (dashDist : float) =
      Svg.line 
        [ 
          ats "stroke-dasharray" (sprintf "%f,%f" dashWidth dashDist)
          //ats "stroke-dasharray" "5,5"
          atf "x1" a.X
          atf "y1" a.Y
          atf "x2" a.X 
          atf "y2" (a.Y + length)
          atc "stroke" color
          atf "stroke-width" strokeWidth
        ]
    
    let drawCircle (upperLeft : V2d) (radius : float) (color : C4b) (strokeWidth : float) (fill : bool) = 
      let fillAttr =
        match fill with
          | true -> [atc "fill" color; atc "stroke" C4b.Black]
          | false -> [ats "fill" "none"; atc "stroke" color]
      Svg.circle
        ([
          atf "cx" (upperLeft.X - radius)
          atf "cy" (upperLeft.Y - radius)
          atf "r" radius
          atf "stroke-width" strokeWidth
        ]@fillAttr)
    
    let drawCircle' (center : V2d) (radius : float) (color : C4b) (strokeWidth : float) (fill : bool) = 
        let upperLeft = center + (new V2d (radius))
        drawCircle upperLeft radius color strokeWidth fill
    
    let drawConcentricCircles (upperLeft : V2d) (outerRadius : float) (innerRadius : float) (circleDist : float) (color : C4b) (nrCircles : int) (strokeWidth : float) =
        let center = upperLeft - (new V2d (outerRadius))
        let radii = 
            [0..nrCircles]
            |> List.map (fun i -> innerRadius + (float i) * circleDist)
        let circles =
            radii
            |> List.map (fun r -> drawCircle' center r color strokeWidth false)
        circles
    
    let drawConcentricCircles' (center : V2d) (outerRadius : float) (innerRadius : float) (color : C4b) (nrCircles : int) (circleDist : float) (strokeWidth : float) =
        let upperLeft = center + (new V2d (outerRadius))
        drawConcentricCircles upperLeft outerRadius innerRadius circleDist color nrCircles strokeWidth
    
    let pointFromAngle (start : V2d) (angle : Angle) (length : float) =
        let endX = start.X + Math.Cos(angle.radians) * length
        let endY = start.Y + Math.Sin(angle.radians) * length
        new V2d (endX, endY)
    
    let drawCircleSegment (center : V2d) (radius : float) (fromPoint : V2d) (toPoint : V2d) (color : C4b) =
      let pathStr = 
         move fromPoint 
          >> (circleSegmentTo radius toPoint)
          >> (lineTo center)
          >> close
      buildPath pathStr color 1.0 true
    
    let drawCircleSegment' (start : V2d) (angleFrom : Angle) (angleTo : Angle) (radius : float) (color : C4b) =
        let end1 = pointFromAngle start angleFrom radius
        let end2 = pointFromAngle start angleTo radius
        drawCircleSegment start radius  end1 end2 color
    
    let drawDonutSegment  (center : V2d) (outerRadius : float) (innerRadius : float) (angleFrom : Angle) (angleTo : Angle) (color : C4b) =
    
        let startInner = pointFromAngle center angleFrom innerRadius
        let endInner   = pointFromAngle center angleTo   innerRadius
        let startOuter = pointFromAngle center angleFrom outerRadius
        let endOuter   = pointFromAngle center angleTo   outerRadius
            
        let pathStr = 
            move startInner 
            >> (circleSegmentTo' innerRadius endInner)
            >> (lineTo endOuter)
            >> (circleSegmentTo outerRadius startOuter)
            >> close
        buildPath pathStr color 1.0 true
    
    let drawLineFromAngle (start : V2d) (angle : Angle) (length : float) (color : C4b) (strokeWidth : float) =
        let endPoint = pointFromAngle start angle length
        drawLine start endPoint color strokeWidth
    
    let drawStarLines (angles: list<Angle>) (center: V2d) (outerRadius: float) (innerRadius: float) (color: C4b) (strokeWidth: float) =
        angles
        |> List.map (fun angle ->
            let start = pointFromAngle center angle innerRadius
            drawLineFromAngle start angle (outerRadius - innerRadius) color strokeWidth
        )
       
    let drawCircleButton (center : V2d) (radius : float) (color : C4b) (filled : bool) (stroke : float) (callback   : list<string> -> 'msg) = 
        
        let margin = 5.0

        let atts = [
            atf "cx" (center.X - radius + margin)
            atf "cy" (center.Y - radius + margin)
            atf "r" radius
            atc "stroke" color 
            atf "stroke-width" stroke 
        ]
    
        toGroup [
            Svg.circle (if filled then atts @ [atc "fill" color] else atts)
        ] (Svg.Events.onClickToggleButton (callback))
                
    let drawRectangle (leftUpper : V2d) width height (color : C4b) =
        Svg.rect [
            atf "x" leftUpper.X
            atf "y" leftUpper.Y
            atf "width" width
            atf "height" height
            atc "fill" color
        ]
    
    let drawRectangleWithStrokeStyle (leftUpper : V2d) width height (color : C4b) (strokeWidth : float) (strokeColor : C4b)=
        Svg.rect [
            atf "x" leftUpper.X
            atf "y" leftUpper.Y
            atf "width" width
            atf "height" height
            atc "stroke" strokeColor
            atf "stroke-width" strokeWidth
            atc "fill" color
        ]
   
    let drawBorderedRectangle (leftUpper: V2d) (size: Size2D) (fill: C4b)(bWeight: SvgWeight) (selectionCallback) (selected: bool) (dottedBorder: bool) =

        let width     = size.width
        let height    = size.height

        let _bweightLeftHorz =
            match selected with //TODO read papers: mark selection state
            | true  -> bWeight.value * 2.0
            | false -> bWeight.value
            
        let elements = 
            [  
                drawVerticalLine leftUpper height C4b.Black _bweightLeftHorz
                match dottedBorder with
                | true  -> drawVerticalDottedLine (new V2d(leftUpper.X + width , leftUpper.Y)) height C4b.Black bWeight.value 3.0 3.0
                | false -> drawVerticalLine (new V2d(leftUpper.X + width , leftUpper.Y)) height C4b.Black bWeight.value
                drawRectangle leftUpper width height fill
            ]

        let singleRectangle = 
            
            toGroup elements selectionCallback

        [singleRectangle]