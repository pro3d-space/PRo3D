namespace PRo3D.Base

open System
open System.Text.RegularExpressions
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.SceneGraph.``Sg Picking Extensions``
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering.Text
open OpcViewer.Base
open OpcViewer.Base.Shader
open FShade
open System.IO
open Aardvark.Base.Fonts


type Self = Self

//TODO refactor: cleanup utilities, move to other projects if applicable, remove dupblicate code from PRo3D.Viewer Utilities

module Box3d =
    let extendBy (box:Aardvark.Base.Box3d) (b:Aardvark.Base.Box3d) =
        box.ExtendBy(b)
        box    

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

module OPCFilter =
        type UniformScope with
            member x.DiffuseColorTexture : ShaderTextureHandle = x?DiffuseColorTexture
            member x.HasDiffuseColorCoordinates : bool = x?HasDiffuseColorCoordinates
            member x.HasDiffuseColorTexture : bool = x?HasDiffuseColorTexture
            member x.selected : bool = x?selected

        let private diffuseSampler =
            sampler2d {
                texture uniform.DiffuseColorTexture
                filter Filter.Anisotropic
                maxAnisotropy 16
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
            }

        let improvedDiffuseTextureAndColor (v : Effects.Vertex) =
            fragment {
                if uniform.HasDiffuseColorTexture then
                    let texColor = diffuseSampler.Sample(v.tc,-1.0)
                    return texColor
                else
                    return v.c
            }

        let improvedDiffuseTexture (v : Effects.Vertex) =
            fragment {
                let texColor = diffuseSampler.Sample(v.tc,-1.0)
                return texColor
            }

        let markPatchBorders (v : Effects.Vertex) =
            fragment { 
            //if uniform.HasDiffuseColorTexture then
                if uniform.selected then
                    if (v.tc.X >= 0.99) && (v.tc.X <= 1.0) || (v.tc.X >= 0.0) && (v.tc.X <= 0.01) then
                        return V4d(0.69, 0.85, 0.0, 1.0)
                    elif (v.tc.Y >= 0.99) && (v.tc.Y <= 1.0) || (v.tc.Y >= 0.0) && (v.tc.Y <= 0.01) then
                        return V4d(0.69, 0.85, 0.0, 1.0)
                    else
                        return v.c
                else return v.c
            //else return v.c
            }

        let EffectOPCFilter =
            toEffect improvedDiffuseTexture

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
    
        let numericField''<'msg> (continuousUpdate : bool) (f : Action -> seq<'msg>) ( atts : AttributeMap<'msg> ) ( model : AdaptiveNumericInput ) inputType =         
  
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
                    match inputType with
                        | Slider ->   
                            yield "type" => "range"
                            if continuousUpdate then
                              yield onInput' (fun s -> s |> tryParseAndClamp min max (model.value |> AVal.force) |> SetValue |> f) 
                            yield onChange' (fun s -> s |> tryParseAndClamp min max (model.value |> AVal.force) |> SetValue |> f)   // continous updates for slider
                        | InputBox -> 
                            yield "type" => "number"
                            yield onChange' (fun s -> s |> tryParseAndClamp min max (model.value |> AVal.force) |> SetValue |> f)  // batch updates for input box (to let user type)
  
                    let! step = model.step
                    yield "step" => sprintf "%f" step
                    yield "min"  => sprintf "%f" min
                    yield "max"  => sprintf "%f" max
  
                    let! value = model.value
                    yield onWheel' (fun d -> value + d.Y * step |> clamp min max |> SetValue |> f)
  
                    let! format = model.format
                    yield "value" => formatNumber format value
                } 
  
            Incremental.input (AttributeMap.ofAMap attributes |> AttributeMap.union atts)
    
        let numericField f atts model inputType = numericField'' false f atts model inputType

        let numericField' = numericField (Seq.singleton) AttributeMap.empty

        let viewContinuously (inputTypes : list<NumericInputType>) (model : AdaptiveNumericInput) : DomNode<Action> =
            inputTypes 
            |> List.map (numericField'' true (Seq.singleton) AttributeMap.empty model) 
            |> List.intersperse (text " ") 
            |> div []
    
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

    // TODO Refactor: not in use duplicated code
    //let takeScreenshot baseAddress (width:int) (height:int) name folder =
    //      let wc = new System.Net.WebClient()
          
    //      let clientStatistic = 
    //          let path = sprintf "%s/rendering/stats.json" baseAddress //sprintf "%s/rendering/stats.json" baseAddress
    //          Log.line "[Screenshot] querying rendering stats at: %s" path
    //          let result = wc.DownloadString(path)
    //          let clientBla : list<ClientStatistics> =
    //              Pickler.unpickleOfJson  result
    //          match clientBla.Length with
    //          | 1 -> clientBla // clientBla.[1] 
    //          | _ -> failwith "no client bla"

    //      for cs in clientStatistic do
    //          let color = V4f.IIII
    //          let screenshot = sprintf "%s/rendering/screenshot/%s?w=%d&h=%d&samples=2&background=[%f,%f,%f,%f]" baseAddress cs.name width height color.X color.Y color.Z color.W

    //          //let screenshot =            
    //          //    sprintf "%s/rendering/screenshot/%s?w=%d&h=%d&samples=4" baseAddress cs.name width height
    //          Log.line "[Screenshot] Running screenshot on: %s" screenshot    

    //          match System.IO.Directory.Exists folder with
    //          | true -> ()
    //          | false -> System.IO.Directory.CreateDirectory folder |> ignore
              
    //         // let filename = cs.name + name
    //          wc.DownloadFile(screenshot,Path.combine [folder; name])

module Shader = 

    module DepthOffset =

        open FShade
        open Aardvark.Rendering.Effects

        type UniformScope with
            member x.DepthOffset : float = x?DepthOffset

        type VertexDepth = 
            {   
                [<Color>] c : V4d; 
                [<Depth>] d : float
                [<Position>] pos : V4d
            }

        [<GLSLIntrinsic("gl_DepthRange.near")>]
        let depthNear()  : float = onlyInShaderCode ""

        [<GLSLIntrinsic("gl_DepthRange.far")>]
        let depthFar()  : float = onlyInShaderCode ""

        [<GLSLIntrinsic("(gl_DepthRange.far - gl_DepthRange.near)")>]
        let depthDiff()  : float = onlyInShaderCode ""

        let depthOffsetFS (v : VertexDepth) =
            fragment {
                let depthOffset = uniform.DepthOffset
                let d = (v.pos.Z - depthOffset)  / v.pos.W
                return { v with c = v.c;  d = ((depthDiff() * d) + depthNear() + depthFar()) / 2.0  }
            }

        let Effect =
            toEffect depthOffsetFS
   
    type UniformScope with
        member x.PointSize : float = uniform?PointSize

    type PointVertex =
        {
            [<Position>] pos : V4d
            [<PointSize>] p : float
            [<Color>] c : V4d
            [<TexCoord; Interpolation(InterpolationMode.Sample)>] tc : V2d
            [<SourceVertexIndex>] i : int
        }

    let constantColor (color : V4d) (v : PointVertex) =
        vertex {
            let ps : float = uniform?PointSize
            return { v with c = color; p = ps }
        }

    let singleColor (v : PointVertex) =
        vertex {
            let ps : float = uniform?PointSize
            let c  : V4d   = uniform?SingleColor
            return { v with c = c; p = ps }
        }

    let differentColor (v : PointVertex) =
        vertex {
            let ps : float = uniform?PointSize
            return { v with c = v.c; p = ps }
        }

    let pointTrafo (v : PointVertex) =
        vertex {
            let vp = uniform.ModelViewTrafo * v.pos
            return { 
                v with 
                    pos = uniform.ProjTrafo * vp
            }
        }

    //type InstancedVertex = 
    //    {
    //        [<Semantic("MV")>]
    //        mv : M44d
    //        [<Position>]
    //        pos : V4d
    //        [<Color>]
    //        c : V4d
    //    }

    //let stableMVTrafo (v : InstancedVertex) =
    //    vertex {
    //        let vp = v.mv * v.pos
    //        let p = uniform.ProjTrafo * vp
    //        let color : V4d = uniform?Color
    //        return { 
    //            v with 
    //                pos = p
    //                c = color
    //        }
    //    }

    let pointSpriteFragment (v : PointVertex) =
        fragment {
            let tc = v.tc

            let c = 2.0 * tc - V2d.II
            if c.Length > 1.0 then
                discard()

            return v
        }

    let lines (t : Triangle<PointVertex>) =
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
    let transformNormal (n : V3d) =
        uniform.ModelViewTrafoInv.Transposed * V4d(n, 0.0)
        |> Vec.xyz
        |> Vec.normalize

    let stableTrafo' (v : AttrVertex) =
        vertex {
            let mvp : M44d = uniform?MVP?ModelViewTrafo
            let vp = mvp * v.pos
            return  
                { v with
                    pos  = uniform.ProjTrafo * vp
                    wp   = v.pos
                    n    = transformNormal v.n
                    ldir = V3d.Zero - vp.XYZ |> Vec.normalize
                } 
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
            |> grayscale
            |> mapBrightness
            |> mapContrast
            |> mapGamma
        }

    let mapRadiometry (v : Effects.Vertex) =
        fragment { 
            if (uniform?useRadiometry) then
                let abR : V3d =  uniform?abR
                let abG : V3d =  uniform?abG
                let abB : V3d =  uniform?abB
                let nc = V4d(v.c.X*255.0, v.c.Y*255.0, v.c.Z*255.0, 255.0)
        
                let rClamped = clamp abR.X abR.Y nc.X 
                let red = ((rClamped - abR.X) / (abR.Y - abR.X))

                let gClamped = clamp abG.X abG.Y nc.Y 
                let green = ((gClamped - abG.X) / (abG.Y - abG.X))

                let bClamped = clamp abB.X abB.Y nc.Z 
                let blue = ((bClamped - abB.X) / (abB.Y - abB.X))

                return V4d(red, green, blue, 1.0)
            else
                return v.c
        }

    let private colormap =
        sampler2d {
            texture uniform?ColorMapTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
    }


    type FootPrintVertex =
        {
            [<Position>]                pos     : V4d            
            //[<WorldPosition>]           wp      : V4d
            //[<TexCoord>]                tc      : V2d
            [<Color>]                   c       : V4d
            //[<Normal>]                  n       : V3d
            //[<SourceVertexIndex>]       sv      : int
            //[<Semantic("Scalar")>]      scalar  : float
            //[<Semantic("LightDir")>]    ldir    : V3d
            [<Semantic("FootPrintProj")>] tc0     : V4d
            //[<Semantic("Tex1")>]        tc1     : V4d
        }

    //let private footprintmap =
    //    sampler2d {
    //        texture uniform?FootPrintTexture
    //        filter Filter.MinMagMipPoint
    //        borderColor (C4f(0.0,0.0,0.0,0.0))
    //        addressU WrapMode.Border
    //        addressV WrapMode.Border
    //        addressW WrapMode.Border
    //    }  

    let footprintV (v : FootPrintVertex) =
        vertex {
            let footprintProjM  : M44d   = uniform?FootprintModelViewProj // was proj * view (earlier there was pretransform in it?)
            return { 
                v with 
                    tc0 = footprintProjM * v.pos; 
                    //sv = 0
            } 
        }

    let footPrintF (v : FootPrintVertex) =
        fragment {     
            let mutable color = v.c
            if uniform?FootprintVisible then
                let fpt = v.tc0.XYZ / v.tc0.W

                // enable this code to use texture based border (and patterns on etc if needed)
                //if fpt.X > -1.0 && fpt.X < 1.0 && fpt.Y > -1.0 && fpt.Y < 1.0 && fpt.Z > -1.0 && fpt.Z < 1.0 then   
                //    let s = footprintmap.Sample(fpt.XY * 0.5 + V2d.II * 0.5)
                //    color.XYZ <- color.XYZ * (1.0 - s.W) + s.XYZ * s.W

                // TODO: more efficient formuation e.g. using step
                if fpt.X > -1.0 && fpt.X < 1.0 && fpt.Y > -1.0 && fpt.Y < 1.0 && fpt.Z > -1.0 && fpt.Z < 1.0 then   
                    let threshold = 0.05
                    let X = fpt.X < -1.0 + threshold || fpt.X > 1.0 - threshold
                    let Y = fpt.Y < -1.0 + threshold || fpt.Y > 1.0 - threshold
                    let Z = fpt.Z < -1.0 + threshold 
                    if X || Y || Z then  
                        color.X <- 1.0
                        
            return color
        }


    let private secondaryTextureSampler =
        sampler2d {
            texture uniform?SecondaryTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }


    let private transferFunctionSampler =
        sampler2d {
            texture uniform?SecondaryTextureTransferFunction
            filter Filter.MinMagPoint
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
        }

    type UniformScope with
        member x.TextureCombiner : TextureCombiner = uniform?TextureCombiner
        member x.TransferFunctionMode : TransferFunctionMode = uniform?TransferFunctionMode
        member x.TFRange : V2d = uniform?TFRange
        member x.TFBlendFactor : float = uniform?TFBlendFactor
        member x.SecondaryTextureContour : V4d = uniform?SecondaryTextureContour 

    let secondaryTexture (v : Effects.Vertex) =
        fragment {    
            // weired mutable style to prevent fshade from creating deeply nested stuff.
            let mutable color = v.c
            match uniform.TextureCombiner with
            | TextureCombiner.Primary -> 
                color <- v.c
            | _ -> 
                let range = uniform.TFRange
                let e = secondaryTextureSampler.Sample(v.tc)
                let width = range.Y - range.X
                let my = (e.X - range.X) / width
                
                let secondaryColor =
                    match uniform.TransferFunctionMode with
                    | TransferFunctionMode.Ramp -> 
                        if e.X >= range.X && e.X <= range.Y then
                            let mappedColor = transferFunctionSampler.Sample(V2d(my,0.0))
                            mappedColor
                        else
                            v.c
                    | TransferFunctionMode.Passthrough -> 
                        secondaryTextureSampler.Sample(v.tc)
                    | _ -> 
                        v.c
                match uniform.TextureCombiner with
                | TextureCombiner.Secondary -> 
                    color <- secondaryColor
                | TextureCombiner.Multiply -> 
                    color <- V4d(v.c.XYZ * secondaryColor.XYZ, 1.0)
                | TextureCombiner.Blend ->
                    color <- V4d(v.c.XYZ * (1.0 - uniform.TFBlendFactor) + secondaryColor.XYZ * uniform.TFBlendFactor, 1.0)
                | _ -> 
                    color <- v.c

            return color
        }

    [<ReflectedDefinition>]
    let lineAlpha (v : float) (center : float) (lineWidth: float) (lineSmooth : float) = 
        let start = center - lineWidth * 0.5
        let stop = center + lineSmooth * 0.5
        if v >= start && v <= stop then
            1.0
        else
           let alpha = 
                Fun.Smoothstep(v, start - lineSmooth * 0.5, start) -
                Fun.Smoothstep(v, stop, stop + lineSmooth * 0.5)
           alpha


    let contourLines (v : Effects.Vertex) =
        fragment {
            let contourSettings = uniform.SecondaryTextureContour
                            
            let lineColor = 
                if contourSettings.X > 0 then

                    let range = uniform.TFRange
                    let e = secondaryTextureSampler.Sample(v.tc)
                    let width = range.Y - range.X
                    let my = (e.X - range.X) / width

                    let distance = contourSettings.X
                    let lineWidth = contourSettings.Y
                    let lineSmoothing = contourSettings.Z

                    let contourDistance = my % distance
                    let lineAlpha = lineAlpha contourDistance (distance * 0.5) lineWidth lineSmoothing

                    V4d(0.0,0.0,0.0, Fun.Clamp(lineAlpha, 0.0, 1.0))
                else
                    V4d.OOOO


            let finalColor = v.c.XYZ * (1.0 - lineColor.W)
            return V4d(finalColor, 1.0)
        }


    let depthCalculation2 (v : FootPrintVertex) =
        fragment {     
            let mutable color = v.c
            if uniform?DepthVisible then
                let depth = v.tc0.Z 

                let hue = mapFalseColors depth 
                let c = hsv2rgb ((clamp 0.0 255.0 hue)/ 255.0 ) 1.0 1.0 
                let texColor = v.c * V4d(c.X, c.Y, c.Z, 1.0)
                color <- texColor

            return color
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

    let drawColoredPoints (pointsF : aval<V3f[]>) (colors : aval<C4b[]>) (pointSize : aval<float>) = 
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
    let edgeLines (close: bool) (points: aval<V3d[]>) (trafo: aval<Trafo3d>) : aval<Line3d[]>  =
        (points, trafo)
        ||> AVal.map2(fun d t -> d |> Array.map (fun d -> t.Backward.TransformPos d))
        |> AVal.map (fun l -> 
            match l with
            | [||] -> [||]
            | xs -> 
                let xs = if close then Array.append xs [|Array.head xs|] else xs
                xs |> Array.pairwise |> Array.map (fun (a,b) -> Line3d(a,b))
        ) 

    let stableLinesHelperEffect = 
        Effect.compose [
            //toEffect DefaultSurfaces.trafo
            toEffect Shader.stableTrafo'
            toEffect DefaultSurfaces.vertexColor
            toEffect Shader.ThickLineNew.thickLine
            toEffect Shader.DepthOffset.depthOffsetFS 
        ]

    let private drawStableLinesHelper (edges: aval<Line3d[]>) (offset: aval<float>) (color: aval<C4b>) (width: aval<float>) =         
        edges
        |> Sg.lines color
        |> Sg.noEvents
        |> Sg.effect [stableLinesHelperEffect]                            
        |> Sg.uniform "LineWidth" width
        |> Sg.uniform "DepthOffset" (offset |> AVal.map (fun depthWorld -> depthWorld / (100.0 - 0.1))) 

    let drawLines (points: aval<V3d[]>) (offset: aval<float>) (color: aval<C4b>) (width: aval<float>) (trafo: aval<Trafo3d>) : ISg<_> = 
        let edges = edgeLines false points trafo
        drawStableLinesHelper edges offset color width
        |> Sg.trafo trafo
        
    let scaledLines = 
        Effect.compose [
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor
            toEffect Shader.ThickLineNew.thickLine
        ]

    let drawScaledLines 
        (points: aval<V3d[]>) 
        (color: aval<C4b>) 
        (width: aval<float>) 
        (trafo: aval<Trafo3d>) : ISg<_> = 

        let edges = edgeLines false points trafo     
        let size = edges |> AVal.map (fun line -> (float (line.Length)) * 100.0)
                                                            
        edges
        |> Sg.lines color
        |> Sg.noEvents
        |> Sg.uniform "WorldPos" (trafo |> AVal.map(fun (x : Trafo3d) -> x.Forward.C3.XYZ))
        |> Sg.uniform "Size" size
        |> Sg.effect [scaledLines]                            
        |> Sg.trafo trafo
        |> Sg.uniform "LineWidth" width

    let drawSingleLine
        (pointA    : aval<V3d>)
        (pointB    : aval<V3d>)
        (color     : aval<C4b>)        
        (width     : aval<float>)
        (trafo     : aval<Trafo3d>) =

        let line = AVal.map2(fun a b -> Line3d(a,b) |> Array.singleton) pointA pointB

        line
        |> Sg.lines color
        |> Sg.noEvents
        //|> Sg.uniform "WorldPos" (trafo |> AVal.map(fun (x : Trafo3d) -> x.Forward.C3.XYZ))
        //|> Sg.uniform "Size" thickness
        |> Sg.effect [scaledLines]                            
        |> Sg.trafo trafo
        |> Sg.uniform "LineWidth" width

    let private pickableContent
        (points            : aval<V3d[]>) 
        (edges             : aval<Line3d[]>) 
        (trafo             : aval<Trafo3d>) 
        (pickingTolerance  : aval<float>) = 

        adaptive {
            let! edg = edges 
            let! t = trafo
            let! tolerance = pickingTolerance


            if edg.Length = 1 then        
                let e = edg |> Array.head
                let cylinder = Cylinder3d(e.P0, e.P1, tolerance)
                return PickShape.Cylinder cylinder
            elif edg.Length > 1 then
                let! cp = points
                let box = cp |> Box3d
                if box.IsInvalid then 
                    Log.warn "invalid pick box for annotation"
                    
                    return PickShape.Box Box3d.Unit
                else
                         
                    return PickShape.Custom(box.Transformed t.Inverse, fun (g : Geometry.RayPart) -> 
                        let hits =
                            edg 
                            |> Array.choose (fun e -> 
                                let c = Cylinder3d(e.P0, e.P1, tolerance)
                                Geometry.RayPart.intersect g c
                            ) 
                        if hits.Length > 0 then Some (Array.min hits) else None
                    )
            else
                Log.warn "invalid pick box for annotation"
                return PickShape.Box Box3d.Unit
        }

    let pickableLine 
        (points             : aval<V3d[]>) 
        (offset             : aval<float>) 
        (color              : aval<C4b>) 
        (width              : aval<float>) 
        (pickingTolerance   : aval<float>)
        (trafo              : aval<Trafo3d>) 
        (picking            : bool) // picking generally enabled
        (pickAnnotationFunc : aval<Line3d[]> -> SceneEventKind * (SceneHit -> bool * seq<_>)) = 

        let edges = edgeLines false points trafo 
        let pline = drawStableLinesHelper edges offset color width              

        if picking then
            let applicator =
                pline 
                |> Sg.pickable' ((pickableContent points edges trafo pickingTolerance) |> AVal.map Pickable.ofShape)

            applicator 
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

    let dotShader = 
        Effect.compose [
            toEffect Shader.ScreenSpaceScale.screenSpaceScale
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor      
        ]

    let dot (color: aval<C4b>) (size: aval<float>) (point: aval<V3d>) =
        let isgDot = sphereSgHelper color size point
        isgDot
        |> Sg.effect [dotShader]

    let indexedGeometryDotsShader = 
        Effect.compose [
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor
            toEffect DefaultSurfaces.pointSprite
        ]

    let indexedGeometryDots (points: alist<V3d>) (size: aval<float>) (color: aval<C4b>) =       
        let points' = points |> AList.toAVal |> AVal.map (fun x -> x |> IndexList.toArray)
        let colors = points' |> AVal.map2 (fun c x -> Array.create x.Length c) color
      
        Sg.draw IndexedGeometryMode.PointList
        |> Sg.vertexAttribute DefaultSemantic.Positions points'         
        |> Sg.vertexAttribute DefaultSemantic.Colors colors
        |> Sg.effect [indexedGeometryDotsShader]
        |> Sg.uniform "PointSize" size
    
    // TODO: performance - instancing
    let drawSpheres (points: aval<V3d[]>) (size: aval<float>) (color: aval<C4b>) =
        aset {
            for p in points do                        
                yield sphereSgHelper color size (AVal.constant p)
        } 
        |> Sg.set
        |> Sg.effect [dotShader]


    module ScreenSpaceScale =
    
        open Aardvark.Rendering.Effects

        type UniformScope with
            member x.Size : float = x?Size


        type InstanceVertex = { 
               [<Position>]            pos   : V4d 
               [<InstanceTrafo>]       mv : M44d
           }
    
        let screenSpaceScale (v : InstanceVertex) =
            vertex {   
                let vp = v.mv * v.pos

                let dist = abs vp.Z   
                let hvp    = float uniform.ViewportSize.X
                let scale = dist * uniform.Size / hvp 

                let vps = v.mv * V4d(v.pos.X * scale, v.pos.Y * scale, v.pos.Z * scale, 1.0)

                return { v with pos = uniform.ProjTrafo * vps }
                //let loc     = uniform.CameraLocation       

                //let dist = abs v.pos.Z    
                //let scale = dist * uniform.Size / hvp 

                //return 
                //    { v with
                //        pos = V4d(v.pos.X * scale, v.pos.Y * scale, v.pos.Z * scale, v.pos.W)
                //    }
            }

        let projTrafo (v : InstanceVertex) =
            vertex {   
                let vp = v.mv * v.pos
                return { v with pos = uniform.ProjTrafo * vp; }
            }

        type Vertex = {
            [<Position>]                pos     : V4d
            [<Semantic("LightDir")>]    ldir    : V3d
        }

        let lightDir (v : Vertex) = 
            vertex {
                return { v with ldir = -v.pos.XYZ |> Vec.normalize }
            }

    
    let dotInstanced = 
        Effect.compose [
            //toEffect DefaultSurfaces.instanceTrafo
            toEffect ScreenSpaceScale.screenSpaceScale
            toEffect DefaultSurfaces.sgColor      
        ]

    let dotInstancedNoScaling = 
        Effect.compose [
            //toEffect DefaultSurfaces.instanceTrafo
            toEffect ScreenSpaceScale.projTrafo
            toEffect ScreenSpaceScale.lightDir
            toEffect DefaultSurfaces.sgColor   
            toEffect DefaultSurfaces.stableHeadlight
        ]

    let drawSpheresFast (view : aval<CameraView>) (viewportSize : aval<V2i>) (points : aval<V3d[]>) (size : aval<float>) (color : aval<C4b>) = 
        
        // the original pro3d scaling scheme as used in OPCViewer
        // to match all other code, semantically translated from here: https://github.com/aardvark-platform/OpcViewer/blob/b45eb33b532d3fcc1b0242b64dd0191eabda6df6/src/OPCViewer.Base/Shaders.fs
        // larger screen space scaling efforts neeed to be taken to improve on this one: https://github.com/pro3d-space/PRo3D/issues/90

        let mvs = 
            AVal.custom (fun t -> 
                let view = view.GetValue(t)
                let points = points.GetValue(t)
                let viewportSize = viewportSize.GetValue(t)
                let size = size.GetValue(t) 

                let loc = view.Location
                let hvp = float viewportSize.X

                // should be computed outside once.
                let mv = (view |> CameraView.viewTrafo).Forward

                let mvs = 
                    points |> Array.map (fun p -> 
                        let dist = Vec.length (p - loc)
                        let scale = dist * size / hvp
                        mv * M44d.Translation(p) * M44d.Scale(scale) |> M44f
                    )
                mvs
            )

        let geometry = IndexedGeometryPrimitives.solidSubdivisionSphere Sphere3d.Unit 4 C4b.White
        Sg.ofIndexedGeometryInstancedA geometry (mvs |> AVal.map Array.length)
        |> Sg.noEvents
        |> Sg.instanceAttribute DefaultSemantic.InstanceTrafo mvs
        |> Sg.viewTrafo' Trafo3d.Identity
        |> Sg.uniform "Color" color
        |> Sg.uniform "Size" size
        |> Sg.effect [dotInstancedNoScaling]

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
    let invariantScaleTrafo 
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


    let stableTrafoShader = 
        Effect.compose [toEffect Shader.StableTrafo.stableTrafo]

    let consolasFont = Font.create "Consolas" FontStyle.Regular

    let text 
        (view       : aval<CameraView>) 
        (near       : aval<float>) 
        (hfov       : aval<float>) 
        (pos        : aval<V3d>) 
        (modelTrafo : aval<Trafo3d>) 
        (size       : aval<double>) 
        (text       : aval<string>) 
        (color      : aval<C4b>) =

        let billboardTrafo = 
            adaptive {
                let! modelt = modelTrafo
                let! v = view
                return screenAlignedTrafo v.Forward v.Up modelt
            }
        color |> AVal.map( fun c ->
            Sg.text consolasFont c text
            |> Sg.noEvents
            |> Sg.effect [stableTrafoShader]         
            |> Sg.trafo (invariantScaleTrafo view near pos size hfov)  // fixed pixel size scaling
            |> Sg.trafo billboardTrafo
            ) |> Sg.dynamic

    let billboardText (view: aval<CameraView>) (pos: aval<V3d>) (text: aval<string>) =
        // new implementation with same result, but does not require CameraView
        let posTrafo = pos |> AVal.map (fun x -> Trafo3d.Translation x)
        let config : TextConfig =
            { 
                TextConfig.Default with
                    renderStyle = RenderStyle.Billboard
                    font = consolasFont
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
    
        Sg.text consolasFont C4b.White text
        |> Sg.noEvents
        |> Sg.effect [stableTrafoShader]      
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

module ColorBrewer =

    let twelveClassSet3 = [
        C4b(141,211,199);
        C4b(255,255,179);
        C4b(190,186,218);
        C4b(251,128,114);
        C4b(128,177,211);
        C4b(253,180,98);
        C4b(179,222,105);
        C4b(252,205,229);
        C4b(217,217,217);
        C4b(188,128,189);
        C4b(204,235,197);
        C4b(255,237,111)
    ]

    let twelveClassPaired = [
        C4b(166,206,227);
        C4b(31,120,180);
        C4b(178,223,138);
        C4b(51,160,44);
        C4b(251,154,153);
        C4b(227,26,28);
        C4b(253,191,111);
        C4b(255,127,0);
        C4b(202,178,214);
        C4b(106,61,154);
        C4b(255,255,153);
        C4b(177,89,40)
    ]    

    let toMaxValue (color : C4b) : C4b =
        let hsv = HSVf.FromC3f (color.ToC3f())
        HSVf(hsv.H, hsv.S, 1.0f).ToC3f().ToC4b()        

    let assignColors (colors : list<C4b>) (objects: 'a list) =
        objects
        |> List.mapi (fun i obj -> (obj, colors.[i % colors.Length]))

module Sorting =
// Function to split the string into chunks of numbers and non-numbers
    let private splitString (input: string) =
        Regex.Matches(input, @"\D+|\d+")
        |> Seq.cast<Match>
        |> Seq.map (fun m -> m.Value)
        |> Seq.toList
    
    // Function to compare two strings with natural sorting
    let compareNatural (left: string) (right: string) =        

        let rec compareParts (parts1: string list) (parts2: string list) =
            match parts1, parts2 with
            | [], [] -> 0
            | [], _ -> -1
            | _, [] -> 1
            | h1::t1, h2::t2 ->
                let isNum1 = Int32.TryParse(h1)
                let isNum2 = Int32.TryParse(h2)
                match isNum1, isNum2 with
                | (true, num1), (true, num2) -> 
                    // Compare as numbers
                    let cmp = compare num1 num2
                    if cmp <> 0 then cmp else compareParts t1 t2
                | _ -> 
                    // Compare as strings
                    let cmp = compare h1 h2
                    if cmp <> 0 then cmp else compareParts t1 t2
    
        compareParts (splitString left) (splitString right)

module AList =
    let pairwise (input : alist<'a>) = 
        input 
        |> AList.toAVal
        |> AVal.map(fun x -> 
            x |> IndexList.toList |> List.pairwise |> IndexList.ofList
        )
        |> AList.ofAVal

module Copy =
    let rec copyAll' (source : DirectoryInfo) (target : DirectoryInfo) skipExisting =
        
        // Check if the target directory exists, if not, create it.
        if not(Directory.Exists target.FullName) then
            Directory.CreateDirectory target.FullName |> ignore

        // Copy each file into it's new directory.
        for fi in source.GetFiles() do
             let sourceFile = fi.FullName
             let targetFile = Path.Combine(target.FullName, fi.Name)

             if ((sourceFile.ToLower() == targetFile.ToLower()) || (skipExisting && File.Exists(targetFile))) then
                Log.warn "Skipping %s, already exists" targetFile
             else
                Log.line "Copying to %s" targetFile
                fi.CopyTo(Path.Combine((target.ToString()), fi.Name), true) |> ignore      
                
        // Copy each subdirectory using recursion.
        let bla = source.GetDirectories()
        for srcSubDir in bla do
            let nextTgtSubDir = target.CreateSubdirectory(srcSubDir.Name)
            copyAll' srcSubDir nextTgtSubDir skipExisting

    let copyAll source target skipExisting=
        let s = DirectoryInfo(source)
        let t = DirectoryInfo(target)

        copyAll' s t skipExisting

[<AutoOpen>]
module ScreenshotUtilities = 
    module Utilities =
        open System.Net.Http

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

        let downloadClientStatistics baseAddress (httpClient : HttpClient) =
            let path = sprintf "%s/rendering/stats.json" baseAddress //sprintf "%s/rendering/stats.json" baseAddress
            Log.line "[Screenshot] querying rendering stats at: %s" path
            let result = httpClient.GetStringAsync(path).Result

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
              let httpClient = new HttpClient()
              let clientStatistics = downloadClientStatistics baseAddress httpClient

              for cs in clientStatistics do
                  let screenshot = getScreenshotUrl baseAddress cs width height
                  let filename = getScreenshotFilename folder name cs format
                  httpClient.DownloadFile(screenshot, filename)
                  let fullpath =
                      try System.IO.Path.GetFullPath(filename) with e -> filename
                  Log.line "[Screenshot] saved to %s" fullpath

        let takeScreenshot baseAddress (width:int) (height:int) name folder format =
            let httpClient = new HttpClient()
            let clientStatistics = downloadClientStatistics baseAddress httpClient
            
            let cs =
                match clientStatistics.Length with
                | 2 -> clientStatistics.[1] 
                | 1 -> clientStatistics.[0]
                | _ -> failwith (sprintf "Could not download client statistics")
                
            let screenshot = getScreenshotUrl baseAddress cs width height
            let filename = getScreenshotFilename folder name cs format
            httpClient.DownloadFile(screenshot,filename)        

module JsInterop = 
    let escapePath (s : string) =
        s.Replace("\\", "\\\\")

module Electron =
    let openPath (s : string) =
        sprintf "top.aardvark.electron.shell.openPath('%s');" (JsInterop.escapePath s)
    
    let showItemInFolder (s : string) =
        sprintf "top.aardvark.electron.shell.showItemInFolder('%s');" (JsInterop.escapePath s)

module FrustumUtils =
    let withAspect (aspect : float) (frustum : Frustum) =
        Frustum.perspective 
            (Frustum.horizontalFieldOfViewInDegrees frustum)
            frustum.near
            frustum.far
            aspect

    let calculateFrustum (focal : float) (near : float) (far: float) =
        // http://paulbourke.net/miscellaneous/lens/
        // https://photo.stackexchange.com/questions/41273/how-to-calculate-the-fov-in-degrees-from-focal-length-or-distance
        let hfov = 2.0 * atan(11.84 /(focal*2.0))
        Frustum.perspective (hfov.DegreesFromRadians()) near far 1.0

    let calculateFrustum' (focal : float) (near : float) 
                          (far: float) (aspect : float) =
        // http://paulbourke.net/miscellaneous/lens/
        // https://photo.stackexchange.com/questions/41273/how-to-calculate-the-fov-in-degrees-from-focal-length-or-distance
        let hfov = 2.0 * atan(11.84 /(focal*2.0))
        Frustum.perspective (hfov.DegreesFromRadians()) near far aspect
