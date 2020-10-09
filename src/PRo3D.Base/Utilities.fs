namespace PRo3D.Base

open System

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.SceneGraph.``Sg Picking Extensions``
open Aardvark.UI
open Aardvark.Rendering.Text
open OpcViewer.Base
open OpcViewer.Base.Shader
open FShade

//TODO refactor: cleanup utilities, move to other projects if applicable, remove dupblicate code from PRo3D.Viewer Utilities

module Double =
    let degreesFromRadians (d:float) =
        d.DegreesFromRadians()
        
    let radiansFromDegrees (d:float) =
        d.RadiansFromDegrees()

module Fail =
    let with1 formatString value =
        value |> sprintf formatString |> failwith

module Console =    

    let print (x:'a) : 'a =
        printfn "%A" x
        x   

module Lenses = 
    let get    (lens : Lens<'s,'a>) (state : 's) : 'a              = lens.Get(state)
    let set    (lens : Lens<'s,'a>) (value : 'a) (state : 's) : 's = lens.Set(state, value)
    let set'   (lens : Lens<'s,'a>) (state : 's) (value : 'a) : 's = lens.Set(state, value)
    let update (lens : Lens<'s,'a>) (f : 'a->'a) (state : 's) : 's = lens.Update(state, f)

module Utilities =
  type ClientStatistics =
    {
        session         : System.Guid
        name            : string
        frameCount      : int
        invalidateTime  : float
        renderTime      : float
        compressTime    : float
        frameTime       : float
    }

  module PRo3DNumeric = 
      open FSharp.Data.Adaptive
      open Aardvark.UI
  
      let inline (=>) a b = Attributes.attribute a b
  
      type Action = 
          | SetValue of float
          | SetMin of float
          | SetMax of float
          | SetStep of float
          | SetFormat of string
  
      let update (model : NumericInput) (action : Action) =
          match action with
          | SetValue v -> { model with value = v }
          | SetMin v ->   { model with min = v }
          | SetMax v ->   { model with max = v }
          | SetStep v ->  { model with step = v }
          | SetFormat s -> { model with format = s }
  
      let formatNumber (format : string) (value : float) =
          String.Format(Globalization.CultureInfo.InvariantCulture, format, value)
  
      let numericField<'msg> ( f : Action -> seq<'msg> ) ( atts : AttributeMap<'msg> ) ( model : AdaptiveNumericInput ) inputType =         
  
          let tryParseAndClamp min max fallback (s: string) =
              let parsed = 0.0
              match Double.TryParse(s, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                  | (true,v) -> clamp min max v
                  | _ ->  printfn "validation failed: %s" s
                          fallback
  
          let onWheel' (f : Aardvark.Base.V2d -> seq<'msg>) =
              let serverClick (args : list<string>) : Aardvark.Base.V2d = 
                  let delta = List.head args |> Pickler.unpickleOfJson
                  delta  / Aardvark.Base.V2d(-100.0,-100.0) // up is down in mouse wheel events
  
              onEvent' "onwheel" ["{ X: event.deltaX.toFixed(10), Y: event.deltaY.toFixed(10)  }"] (serverClick >> f)
  
          let attributes = 
              amap {                
                  yield style "text-align:right; color : black"                
  
                  let! min = model.min
                  let! max = model.max
                  let! value = model.value
                  match inputType with
                      | Slider ->   
                          yield "type" => "range"
                          yield onChange' (tryParseAndClamp min max value >> SetValue >> f)   // continous updates for slider
                      | InputBox -> 
                          yield "type" => "number"
                          yield onChange' (tryParseAndClamp min max value >> SetValue >> f)  // batch updates for input box (to let user type)
  
                  let! step = model.step
                  yield onWheel' (fun d -> value + d.Y * step |> clamp min max |> SetValue |> f)
  
                  yield "step" => sprintf "%f" step
                  yield "min"  => sprintf "%f" min
                  yield "max"  => sprintf "%f" max
  
                  let! format = model.format
                  yield "value" => formatNumber format value
              } 
  
          Incremental.input (AttributeMap.ofAMap attributes |> AttributeMap.union atts)
  
      let numericField' = numericField (Seq.singleton) AttributeMap.empty
  
      let view' (inputTypes : list<NumericInputType>) (model : AdaptiveNumericInput) : DomNode<Action> =
          inputTypes 
              |> List.map (numericField' model) 
              |> List.intersperse (text " ") 
              |> div []
  
      let view (model : AdaptiveNumericInput) =
          view' [InputBox] model
  
      module GenericFunctions =
          let rec applyXTimes (a : 'a) (f : 'a -> int -> 'a) (lastIndex : int) =
              match lastIndex with
              | t when t <= 0 -> f a lastIndex
              | t when t > 0 -> applyXTimes (f a lastIndex) f (lastIndex - 1)
              | _ -> a

  let takeScreenshot baseAddress (width:int) (height:int) name folder =
        let wc = new System.Net.WebClient()
        
        let clientStatistic = 
            let path = sprintf "%s/rendering/stats.json" baseAddress //sprintf "%s/rendering/stats.json" baseAddress
            Log.line "[Screenshot] querying rendering stats at: %s" path
            let result = wc.DownloadString(path)
            let clientBla : list<ClientStatistics> =
                Pickler.unpickleOfJson  result
            match clientBla.Length with
                | 1 -> clientBla // clientBla.[1] 
                | _ -> failwith "no client bla"

        for cs in clientStatistic do
            let screenshot =            
                sprintf "%s/rendering/screenshot/%s?w=%d&h=%d&samples=4" baseAddress cs.name width height
            Log.line "[Screenshot] Running screenshot on: %s" screenshot    

            match System.IO.Directory.Exists folder with
              | true -> ()
              | false -> System.IO.Directory.CreateDirectory folder |> ignore
            
           // let filename = cs.name + name
            wc.DownloadFile(screenshot,Path.combine [folder; name])

module Shader = 
   
    type UniformScope with
        member x.PointSize : float = uniform?PointSize

    type pointVertex =
        {
            [<Position>] pos : V4d
            [<PointSize>] p : float
            [<Color>] c : V4d
            [<TexCoord; Interpolation(InterpolationMode.Sample)>] tc : V2d
            [<SourceVertexIndex>] i : int
        }

    let constantColor (color : V4d) (v : pointVertex) =
        vertex {
            let ps : float = uniform?PointSize
            return { v with c = color; p = ps }
        }

    let singleColor (v : pointVertex) =
        vertex {
            let ps : float = uniform?PointSize
            let c  : V4d   = uniform?SingleColor
            return { v with c = c; p = ps }
        }

    let differentColor (v : pointVertex) =
        vertex {
            let ps : float = uniform?PointSize
            return { v with c = v.c; p = ps }
        }

    let pointTrafo (v : pointVertex) =
        vertex {
            let vp = uniform.ModelViewTrafo * v.pos
            return { 
                v with 
                    pos = uniform.ProjTrafo * vp
            }
        }

    let pointSpriteFragment (v : pointVertex) =
        fragment {
            let tc = v.tc

            let c = 2.0 * tc - V2d.II
            if c.Length > 1.0 then
                discard()

            return v
        }

    let lines (t : Triangle<pointVertex>) =
        line {
            yield t.P0
            yield t.P1
            restartStrip()
            
            yield t.P1
            yield t.P2
            restartStrip()

            yield t.P2
            yield t.P0
            restartStrip()
        }

    let markPatchBorders (v : Effects.Vertex) =
        fragment {    
            if uniform?selected then
                if   (v.tc.X >= 0.99) && (v.tc.X <= 1.0) || (v.tc.X >= 0.0) && (v.tc.X <= 0.01) then
                    return V4d(0.69, 0.85, 0.0, 1.0)
                elif (v.tc.Y >= 0.99) && (v.tc.Y <= 1.0) || (v.tc.Y >= 0.0) && (v.tc.Y <= 0.01) then
                    return V4d(0.69, 0.85, 0.0, 1.0)
                else
                    return v.c
            else return v.c
        }
    
    [<ReflectedDefinition>]
    let hsv2rgb (h : float) (s : float) (v : float) =
        let h = Fun.Frac(h)
        let chr = v * s
        let x = chr * (1.0 - Fun.Abs(Fun.Frac(h * 3.0) * 2.0 - 1.0))
        let m = v - chr
        let t = (int)(h * 6.0)
        match t with
            | 0 -> V3d(chr + m, x + m, m)
            | 1 -> V3d(x + m, chr + m, m)
            | 2 -> V3d(m, chr + m, x + m)
            | 3 -> V3d(m, x + m, chr + m)
            | 4 -> V3d(x + m, m, chr + m)
            | 5 -> V3d(chr + m, m, x + m)
            | _ -> V3d(chr + m, x + m, m)
    
    [<ReflectedDefinition>]
    let mapFalseColors value : float =         
        let invert           = uniform?inverted
        let fcUpperBound     = uniform?upperBound
        let fcLowerBound     = uniform?lowerBound
        let fcInterval       = uniform?interval
        let fcUpperHueBound  = uniform?endC
        let fcLowerHueBound  = uniform?startC
    
        let low         = if (invert = false) then fcLowerBound else fcUpperBound
        let up          = if (invert = false) then fcUpperBound else fcLowerBound
        let interval    = if (invert = false) then fcInterval   else -1.0 * fcInterval        
    
        let rangeValue = up - low + interval
        let normInterv = (interval / rangeValue)
    
        //map range to 0..1 according to lower/upperbound
        let k = (value - low + interval) / rangeValue
    
        //discretize lookup
        let bucket = floor (k / normInterv)
        let k = ((float) bucket) * normInterv |> clamp 0.0 1.0
    
        let uH = fcUpperHueBound * 255.0
        let lH = fcLowerHueBound * 255.0
        //map values to hue range
        let fcHueUpperBound = if (uH < lH) then uH + 1.0 else uH
        let rangeHue = uH - lH // fcHueUpperBound - lH
        (k * rangeHue) + lH
    
    [<ReflectedDefinition>]
    let mapFalseColors2 value : float =
        let fcInterval     = uniform?interval
        let startColorHue  = uniform?startC
        let endColorHue    = uniform?endC
        let invertMapping  = uniform?inverted
        let fcUpperBound   = uniform?upperBound
        let fcLowerBound   = uniform?lowerBound
    
        let range          = fcUpperBound - fcLowerBound
        let numOfRangeGaps = int(round (range / fcInterval)) //round
        let numOfStops     = if (numOfRangeGaps + 2) > 100 then 100 else (numOfRangeGaps + 2)
        //let numOfStops = if (numOfRangeGaps + 2) > 200 then 200 else (numOfRangeGaps + 2)
    
        let startColor = startColorHue *255.0
        let endColor   = endColorHue *255.0
        let hStepSize  =
            if startColor < endColor then 
                (endColor - startColor) / ((float)(numOfStops-1))
            else 
                ((endColor + 1.0) - startColor) / ((float)(numOfStops-1))
                
        let pos = 
            match fcLowerBound < value with
            | true -> (int ( round(value - fcLowerBound) / fcInterval )) //+1//round
            | _ -> 0
    
        let pos1 = 
            match fcUpperBound < value with
            | true -> numOfRangeGaps
            | _ -> pos
                     
        let currColorH = 
            if invertMapping then
                (endColor - (hStepSize * ((float)(pos1))))
            else 
                (startColor + (hStepSize * ((float)(pos1))))
        currColorH

    let falseColorLegend2 (v : AttrVertex) =
        fragment {    
    
            if (uniform?falseColors) 
            then
                let hue = mapFalseColors v.scalar //mapFalseColors2 v.scalar
                let c = hsv2rgb ((clamp 0.0 255.0 hue)/ 255.0 ) 1.0 1.0 // 
                return v.c * V4d(c.X, c.Y, c.Z, 1.0)
            else
                return v.c
        }

    [<ReflectedDefinition>]
    let myTrunc (value : float) =
        clamp 0.0 255.0 value

    //TODO LF ... put all color adaptation mechanisms into 1 shader. Shader code produced by FShade has a ridiculous size ~6500 lines of code

    [<ReflectedDefinition>]
    let mapContrast (col : V4d) =
    //let mapContrast (v : Vertex) =
        //fragment { 
            if (uniform?useContrastS) then
                let c = uniform?contrastS
                let nc = V4d(col.X*255.0, col.Y*255.0, col.Z*255.0, 255.0)
        
                let factor = (259.0 * (c + 255.0)) / (255.0 * (259.0 - c))
                let red    = (myTrunc (factor * (nc.X   - 128.0) + 128.0)) / 255.0
                let green  = (myTrunc (factor * (nc.Y   - 128.0) + 128.0)) / 255.0
                let blue   = (myTrunc (factor * (nc.Z   - 128.0) + 128.0)) / 255.0
                V4d(red, green, blue, 1.0)
            else
                col
        //}

    [<ReflectedDefinition>]
    let mapBrightness (col : V4d) =     
        if (uniform?useBrightnS) then
            let b = uniform?brightnessS
            let nc = V4d(col.X*255.0, col.Y*255.0, col.Z*255.0, 255.0)        
        
            let red   = (myTrunc(nc.X + b)) / 255.0 
            let green = (myTrunc(nc.Y + b)) / 255.0 
            let blue  = (myTrunc(nc.Z + b)) / 255.0 
            V4d(red, green, blue, 1.0)
        else
            col
    
    [<ReflectedDefinition>]
    let mapGamma (col : V4d) =    
        if (uniform?useGammaS) then
            let g = uniform?gammaS
            let gammaCorrection = 1.0 / g
            V4d(col.X**gammaCorrection, col.Y**gammaCorrection, col.Z**gammaCorrection, 1.0)
        else
            col

    [<ReflectedDefinition>]
    let grayscale (col : V4d) =    
        if (uniform?useGrayS) then
            let value = col.X * 0.299 + col.Y * 0.587 + col.Z * 0.114
            V4d(value, value, value, 1.0)
        else
            col
    
    [<ReflectedDefinition>]
    let addColor (col : V4d) =    
        if (uniform?useColorS) then
            let hue : V3d =  uniform?colorS
            let nc = V4d(col.X*255.0, col.Y*255.0, col.Z*255.0, 255.0)

            let red   = (myTrunc( nc.X * hue.X)) / 255.0
            let green = (myTrunc( nc.Y * hue.Y)) / 255.0
            let blue  = (myTrunc( nc.Z * hue.Z)) / 255.0
            V4d(red, green, blue, 1.0)
        else
            col
        
    let mapColorAdaption (v : Effects.Vertex) =
        fragment { 
            return v.c
            |> addColor
            |> mapContrast
            |> mapBrightness
            |> mapGamma
            |> grayscale
        }

module Sg =    

    let colorPointsEffect = 
        lazy 
            Effect.compose [
                toEffect Shader.pointTrafo 
                toEffect Shader.differentColor
                toEffect DefaultSurfaces.pointSprite
                toEffect Shader.pointSpriteFragment
            ]

    let drawColoredPoints pointsF colors pointSize = 
        Sg.draw IndexedGeometryMode.PointList
        |> Sg.vertexAttribute DefaultSemantic.Positions pointsF
        |> Sg.vertexAttribute DefaultSemantic.Colors colors
        |> Sg.uniform "PointSize" pointSize
        |> Sg.effect [colorPointsEffect.Value]

    let singleColorPointsEffect = 
        lazy
            Effect.compose [
                toEffect Shader.pointTrafo
                toEffect Shader.singleColor
                toEffect DefaultSurfaces.pointSprite
                toEffect Shader.pointSpriteFragment
                toEffect Shader.DepthOffset.depthOffsetFS
            ]

    let drawSingleColorPoints (pointsF : aval<V3f[]>) (color : aval<V4d>) pointSize offset = 
        Sg.draw IndexedGeometryMode.PointList
        |> Sg.vertexAttribute DefaultSemantic.Positions pointsF
        |> Sg.uniform "PointSize" pointSize
        |> Sg.uniform "SingleColor" color
        |> Sg.uniform "DepthOffset" (
              offset |> AVal.map (fun depthWorld -> depthWorld / (100.0 - 0.1)))
        |> Sg.effect [singleColorPointsEffect.Value]
    
    //## LINES ##
    let edgeLines (close: bool) (points: alist<V3d>) (trafo: aval<Trafo3d>) : aval<Line3d[]>  =
        points
        |> AList.map(fun d -> trafo.GetValue().Backward.TransformPos d)
        |> AList.toAVal 
        |> AVal.map (fun l ->
            let list = IndexList.toList l   
            match list |> List.tryHead with
            | Some h -> 
                if close then list @ [h] else list
                    |> List.pairwise
                    |> List.map (fun (a,b) -> new Line3d(a,b))
                    |> List.toArray
            | None -> [||])       
      
    let private drawStableLinesHelper (edges: aval<Line3d[]>) (offset: aval<float>) (color: aval<C4b>) (width: aval<float>) = 
        edges
        |> Sg.lines color
        |> Sg.noEvents
        |> Sg.shader {
            do! Shader.StableTrafo.stableTrafo
            do! DefaultSurfaces.vertexColor
            do! Shader.ThickLineNew.thickLine
            do! Shader.DepthOffset.depthOffsetFS 
        }                               
        |> Sg.uniform "LineWidth" width
        |> Sg.uniform "DepthOffset" (offset |> AVal.map (fun depthWorld -> depthWorld / (100.0 - 0.1))) 

    let drawLines (points: alist<V3d>) (offset: aval<float>) (color: aval<C4b>) (width: aval<float>) (trafo: aval<Trafo3d>) : ISg<_> = 
        let edges = edgeLines false points trafo
        drawStableLinesHelper edges offset color width
        |> Sg.trafo trafo
                               
    let drawScaledLines (points: alist<V3d>) (color: aval<C4b>) (width: aval<float>) (trafo: aval<Trafo3d>) : ISg<_> = 
        let edges = edgeLines false points trafo     
        let size = edges |> AVal.map (fun line -> (float (line.Length)) * 100.0)
                                                            
        edges
        |> Sg.lines color
        |> Sg.noEvents
        |> Sg.uniform "WorldPos" (trafo |> AVal.map(fun (x : Trafo3d) -> x.Forward.C3.XYZ))
        |> Sg.uniform "Size" size
        |> Sg.shader {               
            do! DefaultSurfaces.stableTrafo
            do! DefaultSurfaces.vertexColor
            do! DefaultSurfaces.thickLine
        }                               
        |> Sg.trafo trafo
        |> Sg.uniform "LineWidth" width      

    let private pickableContent (points: alist<V3d>) (edges: aval<Line3d[]>) (trafo: aval<Trafo3d>) = 
        adaptive {
            let! edg = edges 
            let! t = trafo
            if edg.Length = 1 then        
                let e = edg |> Array.head
                let cylinder = Cylinder3d(e.P0, e.P1, 0.1)
                return PickShape.Cylinder cylinder
            elif edg.Length > 1 then
                let! xs = points.Content
                let cp = xs |> IndexList.toArray
                let box = cp |> Box3d
                if box.IsInvalid then 
                    Log.warn "invalid pick box for annotation"
                    
                    return PickShape.Box Box3d.Unit
                else
                         
                    return PickShape.Custom(box.Transformed t.Inverse, fun (g : Geometry.RayPart) -> 
                        let hits =
                            edg 
                            |> Array.choose (fun e -> 
                                let c = Cylinder3d(e.P0, e.P1, 0.1)
                                Geometry.RayPart.intersect g c
                            ) 
                        if hits.Length > 0 then Some (Array.min hits) else None
                    )
            else
                Log.warn "invalid pick box for annotation"
                return PickShape.Box Box3d.Unit
        }

    let pickableLine 
        (points : alist<V3d>) 
        (offset : aval<float>) 
        (color : aval<C4b>) 
        (width : aval<float>) 
        (trafo : aval<Trafo3d>) 
        (picking: bool) // picking generally enabled
        (pickAnnotationFunc:  aval<Line3d[]> -> SceneEventKind * (SceneHit -> bool * seq<_>)) = 

        let edges = edgeLines false points trafo 
        let pline = drawStableLinesHelper edges offset color width
      
        let event = pickAnnotationFunc edges        

        if picking then
            let applicator =
                pline 
                |> Sg.pickable' ((pickableContent points edges trafo) |> AVal.map Pickable.ofShape)

            (applicator :> ISg) 
            |> Sg.noEvents
            |> Sg.withEvents [ pickAnnotationFunc edges ]
            |> Sg.trafo trafo
        else 
            pline
            |> Sg.trafo trafo

    //## POINTS ##
    let private sphereSgHelper (color: aval<C4b>) (size: aval<float>) (pos: aval<V3d>) = 
        Sg.sphere 2 color (AVal.constant 1.0)
        |> Sg.noEvents
        |> Sg.trafo(pos |> AVal.map Trafo3d.Translation)
        |> Sg.uniform "Size" size
        |> Sg.uniform "WorldPos" pos

    let dot (color: aval<C4b>) (size: aval<float>) (point: aval<V3d>) =
        let isgDot = sphereSgHelper color size point
        isgDot
        |> Sg.effect [
            toEffect Shader.ScreenSpaceScale.screenSpaceScale
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor          
        ]

    let indexedGeometryDots (points: alist<V3d>) (size: aval<float>) (color: aval<C4b>) =       
        let points' = points |> AList.toAVal |> AVal.map (fun x -> x |> IndexList.toArray)
        let colors = points' |> AVal.map2 (fun c x -> Array.create x.Length c) color
      
        Sg.draw IndexedGeometryMode.PointList
        |> Sg.vertexAttribute DefaultSemantic.Positions points'         
        |> Sg.vertexAttribute DefaultSemantic.Colors colors
        |> Sg.effect [
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor
            toEffect DefaultSurfaces.pointSprite
        ]
        |> Sg.uniform "PointSize" size
    
    let drawSpheres (points: alist<V3d>) (size: aval<float>) (color: aval<C4b>) =
        aset {
            for p in points |> ASet.ofAList do                        
                yield sphereSgHelper color size (AVal.constant p)
        } 
        |> Sg.set
        |> Sg.effect [
            toEffect Shader.ScreenSpaceScale.screenSpaceScale
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor          
        ]
    let stablePoints (trafo : aval<Trafo3d>) (positions : aval<V3d[]>) =
        positions 
        |> AVal.map2(fun (t : Trafo3d) x -> 
            x |> Array.map(fun p -> (t.Backward.TransformPos(p)) |> V3f)) trafo

    let stablePoints' (positions : aval<V3d[]>) : (aval<V3f[]> * aval<Trafo3d>) = 
        let trafo =
            positions 
            |> AVal.map Array.tryHead 
            |> AVal.map (function | Some p -> Trafo3d.Translation p | None -> Trafo3d.Identity)

        stablePoints trafo positions, trafo

    let drawPointList (positions : alist<V3d>) (color : aval<C4b>) (pointSize : aval<double>) (offset : aval<double>)= 
        let positions = positions |> AList.toAVal |> AVal.map IndexList.toArray
        let (pointsF, trafo) = stablePoints' positions

        drawSingleColorPoints 
            pointsF 
            (color |> AVal.map(fun x -> x.ToC4f().ToV4d()))
            pointSize 
            offset
        |> Sg.trafo trafo

    //## TEXT ##
    let private invariantScaleTrafo 
        (view : aval<CameraView>) 
        (near : aval<float>) 
        (pos  : aval<V3d>) 
        (size : aval<double>) 
        (hfov : aval<float>) 
        : aval<Trafo3d> =

        adaptive {
            let! hfov = hfov
            let hfov_rad = Conversion.RadiansFromDegrees(hfov)

            let! near = near
            let! size = size 
            let wz = Fun.Tan(hfov_rad / 2.0) * near * size

            let! p = pos
            let! v = view
            let dist = Vec.Distance(p, v.Location)
            let scale = ( wz / near ) * dist

            return Trafo3d.Scale scale
        }

    let private screenAlignedTrafo (forw : V3d) (up : V3d) (modelTrafo: Trafo3d) =
        let right = up.Cross forw
        let rotTrafo = 
            new Trafo3d(
                new M44d(
                    right.X, up.X, forw.X, 0.0,
                    right.Y, up.Y, forw.Y, 0.0,
                    right.Z, up.Z, forw.Z, 0.0,
                    0.0,     0.0,  0.0,    1.0
                ),
                new M44d(
                    right.X, right.Y, right.Z, 0.0,
                    up.X,    up.Y,    up.Z,    0.0,
                    forw.X,  forw.Y,  forw.Z,  0.0,
                    0.0,     0.0,     0.0,     1.0
                )
        )
        rotTrafo * modelTrafo

    let text 
        (view       : aval<CameraView>) 
        (near       : aval<float>) 
        (hfov       : aval<float>) 
        (pos        : aval<V3d>) 
        (modelTrafo : aval<Trafo3d>) 
        (size       : aval<double>) 
        (text       : aval<string>) =

        let billboardTrafo = 
            adaptive {
                let! modelt = modelTrafo
                let! v = view
                return screenAlignedTrafo v.Forward v.Up modelt
            }
      
        Sg.text (Font.create "Consolas" FontStyle.Regular) C4b.White text
        |> Sg.noEvents
        |> Sg.shader {
            do! Shader.StableTrafo.stableTrafo
        }            
        |> Sg.trafo (invariantScaleTrafo view near pos size hfov)  // fixed pixel size scaling
        |> Sg.trafo billboardTrafo

    let billboardText (view: aval<CameraView>) (pos: aval<V3d>) (text: aval<string>) =
        // new implementation with same result, but does not require CameraView
        let posTrafo = pos |> AVal.map (fun x -> Trafo3d.Translation x)
        let config : TextConfig =
            { 
                TextConfig.Default with
                    renderStyle = RenderStyle.Billboard
                    font = Font.create "Consolas" FontStyle.Regular
                    color = C4b.White
                    align = TextAlignment.Center
            }
        Sg.textWithConfig config text 
        |> Sg.noEvents
        |> Sg.trafo (0.1 |> Trafo3d.Scale |> AVal.constant)  // scale text world size
        |> Sg.trafo posTrafo

    let billboardText' (view: aval<CameraView>) (pos: aval<V3d>) (text: aval<string>) =
        // old implementation needs CameraView
        let posTrafo = pos |> AVal.map (fun x -> Trafo3d.Translation x)
        let billboardTrafo = 
            adaptive {
                let! modelt = posTrafo
                let! v = view
                return screenAlignedTrafo v.Forward v.Up modelt
            }
    
        Sg.text (Font.create "Consolas" FontStyle.Regular) C4b.White text
        |> Sg.noEvents
        |> Sg.effect [
            Shader.StableTrafo.stableTrafo |> toEffect
        ]         
        |> Sg.trafo (0.1 |> Trafo3d.Scale |> AVal.constant )
        |> Sg.trafo billboardTrafo

module Formatting =
    [<StructuredFormatDisplay("{AsString}"); Struct>]
    type Len(meter : float) =
        member x.Angstrom       = meter * 10000000000.0
        member x.Nanometer      = meter * 1000000000.0
        member x.Micrometer     = meter * 1000000.0
        member x.Millimeter     = meter * 1000.0
        member x.Centimeter     = meter * 100.0
        member x.Meter          = meter
        member x.Kilometer      = meter / 1000.0
        member x.Astronomic     = meter / 149597870700.0
        member x.Lightyear      = meter / 9460730472580800.0
        member x.Parsec         = meter / 30856775777948584.0
    
        member private x.AsString = x.ToString()
    
        override x.ToString() =
            if x.Parsec       > 0.5 then sprintf "%.3fpc" x.Parsec
            elif x.Lightyear  > 0.5 then sprintf "%.3fly" x.Lightyear
            elif x.Astronomic > 0.5 then sprintf "%.3fau" x.Astronomic
            elif x.Kilometer  > 0.5 then sprintf "%.3fkm" x.Kilometer
            elif x.Meter      > 1.0 then sprintf "%.2fm"  x.Meter
            elif x.Centimeter > 1.0 then sprintf "%.2fcm" x.Centimeter
            elif x.Millimeter > 1.0 then sprintf "%.0fmm" x.Millimeter
            elif x.Micrometer > 1.0 then sprintf "%.0fµm" x.Micrometer
            elif x.Nanometer  > 1.0 then sprintf "%.0fnm" x.Nanometer
            elif meter        > 0.0 then sprintf "%.0f"   x.Angstrom
            else "0" 

module AList =
    let pairwise (input : alist<'a>) = 
        input 
        |> AList.toAVal
        |> AVal.map(fun x -> 
            x |> IndexList.toList |> List.pairwise |> IndexList.ofList
        )
        |> AList.ofAVal

[<AutoOpen>]
module ScreenshotUtilities = 
    module Utilities =

      type ClientStatistics =
        {
            session         : System.Guid
            name            : string
            frameCount      : int
            invalidateTime  : float
            renderTime      : float
            compressTime    : float
            frameTime       : float
        }

      let downloadClientStatistics baseAddress (webClient : System.Net.WebClient) =
          let path = sprintf "%s/rendering/stats.json" baseAddress //sprintf "%s/rendering/stats.json" baseAddress
          Log.line "[Screenshot] querying rendering stats at: %s" path
          let result = webClient.DownloadString(path)

          let clientBla : list<ClientStatistics> =
              Pickler.unpickleOfJson  result
          match clientBla.Length with
              | 1 | 2 -> clientBla // clientBla.[1] 
              | _ -> failwith (sprintf "Could not download client statistics for %s" path)  //"no client bla"

      let getScreenshotUrl baseAddress clientStatistic width height =
          let screenshot = sprintf "%s/rendering/screenshot/%s?w=%d&h=%d&samples=4" baseAddress clientStatistic.name width height
          Log.line "[Screenshot] Running screenshot on: %s" screenshot    
          screenshot

      let getScreenshotFilename folder name clientStats format =
        match System.IO.Directory.Exists folder with
          | true -> ()
          | false -> System.IO.Directory.CreateDirectory folder |> ignore
            
        Path.combine [folder; name + "_" + clientStats.name + format]

      let takeScreenshotFromAllViews baseAddress (width:int) (height:int) name folder format =
            let wc = new System.Net.WebClient()
            let clientStatistics = downloadClientStatistics baseAddress wc

            for cs in clientStatistics do
                let screenshot = getScreenshotUrl baseAddress cs width height
                let filename = getScreenshotFilename folder name cs format
                wc.DownloadFile(screenshot, filename)
                let fullpath =
                    try System.IO.Path.GetFullPath(filename) with e -> filename
                Log.line "[Screenshot] saved to %s" fullpath

      let takeScreenshot baseAddress (width:int) (height:int) name folder format =
            let wc = new System.Net.WebClient()
            let clientStatistics = downloadClientStatistics baseAddress wc

            let cs =
              match clientStatistics.Length with
                  | 2 -> clientStatistics.[1] 
                  | 1 -> clientStatistics.[0]
                  | _ -> failwith (sprintf "Could not download client statistics")
                
            let screenshot = getScreenshotUrl baseAddress cs width height
            let filename = getScreenshotFilename folder name cs format
            wc.DownloadFile(screenshot,filename)        
