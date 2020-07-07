namespace PRo3D.Base
    
open Aardvark.Base
open Aardvark.GeoSpatial.Opc.Shader
open Aardvark.Base.Rendering
open FShade

// TODO "needed to move to module - needs merge"
module OtherShader =
    module Shader = 

        type UniformScope with
            member x.PointSize : float = uniform?PointSize

        type SuperVertex = 
            {
                [<Position>] pos :  V4d
                [<SourceVertexIndex>] i : int
            }
        

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

        //let lines (t : Triangle<SuperVertex>) =
        //    line {
        //        yield t.P0
        //        yield t.P1
        //        restartStrip()
            
        //        yield t.P1
        //        yield t.P2
        //        restartStrip()

        //        yield t.P2
        //        yield t.P0
        //        restartStrip()
        //    }
        
    
        let private colormap =
            sampler2d {
                texture uniform?ColorMapTexture
                filter Filter.MinMagMipLinear
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
        }

        let falseColor (v : Vertex) =
            fragment {           
                if uniform?falseColors then
                    let range : V2d = uniform?MinMax
                    let norm = V2d((v.scalar - range.X)/ (range.Y - range.X), 0.5)

                    let c = colormap.Sample(norm).XYZ

                    return v.c * V4d(c.X, c.Y, c.Z, 1.0)
                else
                    return v.c
            }

        let selectionColor (v : Vertex) =
            fragment {
                if uniform?selected then
                    //let c : V4d = uniform?selectionColor
                    //return c * v.c
                    let gamma = 1.3
                    return V4d(v.c.X ** (1.0 / gamma), v.c.Y ** (1.0 / gamma),v.c.Z ** (1.0 / gamma), 1.0)
                else return v.c
            }

        let markPatchBorders (v : Vertex) =
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
    
        let private diffuseSampler =
            sampler2d {
                texture uniform?DiffuseColorTexture
                filter Filter.Anisotropic
                maxAnisotropy 16
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
            }
    
        let improvedDiffuseTexture (v : Effects.Vertex) =
            fragment {
                let texColor = diffuseSampler.Sample(v.tc,-1.0)
                return texColor
            }
            
        let falseColorLegend2 (v : Vertex) =
            fragment {    
    
                if (uniform?falseColors) 
                then
                    let hue = mapFalseColors v.scalar //mapFalseColors2 v.scalar
                    let c = hsv2rgb ((clamp 0.0 255.0 hue)/ 255.0 ) 1.0 1.0 // 
                    return v.c * V4d(c.X, c.Y, c.Z, 1.0)
                else
                    return v.c
            }
    
        let falseColorLegendTest (v : Vertex) =
            fragment {    
    
                if (uniform?falseColors) 
                then
                    let fcUpperBound     = uniform?upperBound
                    let fcLowerBound     = uniform?lowerBound
                    let k = (v.scalar - fcLowerBound) / (fcUpperBound-fcLowerBound) 
                    let value = clamp 0.0 1.0 k
                    return V4d(value, value, value, 1.0) //value // V4d(value, value, value, 1.0)
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
        //let mapBrightness (v : Vertex) = 
            //fragment { 
                if (uniform?useBrightnS) then
                    let b = uniform?brightnessS
                    let nc = V4d(col.X*255.0, col.Y*255.0, col.Z*255.0, 255.0)        
            
                    let red   = (myTrunc(nc.X + b)) / 255.0 
                    let green = (myTrunc(nc.Y + b)) / 255.0 
                    let blue  = (myTrunc(nc.Z + b)) / 255.0 
                    V4d(red, green, blue, 1.0)
                else
                    col
           // }
    
        [<ReflectedDefinition>]
        let mapGamma (col : V4d) = 
        //let mapGamma (v : Vertex) = 
            //fragment { 
                if (uniform?useGammaS) then
                    let g = uniform?gammaS
                    let gammaCorrection = 1.0 / g
                    V4d(col.X**gammaCorrection, col.Y**gammaCorrection, col.Z**gammaCorrection, 1.0)
                else
                    col
            //}
    
        [<ReflectedDefinition>]
        let grayscale (col : V4d) =
        //let grayscale (v : Vertex) = 
        //    fragment { 
                if (uniform?useGrayS) then
                    let value = col.X * 0.299 + col.Y * 0.587 + col.Z * 0.114
                    V4d(value, value, value, 1.0)
                else
                    col
            //}
    
        [<ReflectedDefinition>]
        let addColor (col : V4d) =
        //let addColor (v : Vertex) = 
            //fragment { 
                if (uniform?useColorS) then
                    let hue : V3d =  uniform?colorS
                    let nc = V4d(col.X*255.0, col.Y*255.0, col.Z*255.0, 255.0)

                    let red   = (myTrunc( nc.X * hue.X)) / 255.0
                    let green = (myTrunc( nc.Y * hue.Y)) / 255.0
                    let blue  = (myTrunc( nc.Z * hue.Z)) / 255.0
                    V4d(red, green, blue, 1.0)
                else
                    col
            //}
    
        let mapColorAdaption (v : Vertex) =
            fragment { 
                return v.c
                        |> addColor
                        |> mapContrast
                        |> mapBrightness
                        |> mapGamma
                        |> grayscale                    
            }
  
        type FootPrintVertex =
            {
                [<Position>]                pos     : V4d            
                [<WorldPosition>]           wp      : V4d
                [<TexCoord>]                tc      : V2d
                [<Color>]                   c       : V4d
                [<Normal>]                  n       : V3d
                [<SourceVertexIndex>]       sv      : int
                [<Semantic("Scalar")>]      scalar  : float
                [<Semantic("LightDir")>]    ldir    : V3d
                [<Semantic("Tex0")>]        tc0     : V4d

            }

        type RoverVertex =
            {
                [<Position>]                  pos         : V4d    
                [<Semantic("RoverPosProj")>]  posProj     : V4d
                [<Color>]                     c       : V4d
            }


      
        let private footprintmap =
            sampler2d {
                texture uniform?FootPrintTexture
                filter Filter.MinMagMipLinear
                borderColor C4f.Black
                addressU WrapMode.Border
                addressV WrapMode.Border
                addressW WrapMode.Border
        }

        type UniformScope with
            member x.RoverMVP : M44d = uniform?RoverMVP
            member x.HasRoverMVP : bool = uniform?HasRoverMVP

        let footprintV (v : RoverVertex) =
            vertex {
                let roverProjSpace = uniform.RoverMVP * v.pos

                return { v with posProj = roverProjSpace  } //v.wp
            }

        let footPrintF (v : RoverVertex) =
            fragment {           
                if uniform?footprintVisible && uniform.HasRoverMVP then
                    let outside = v.c * 0.7 
                    let pos = v.posProj
                    let t = pos.XY / pos.W
                    let c = 
                        if t.X > -1.0 && t.X < 1.0 && t.Y > -1.0 && t.Y < 1.0 then
                            v.c 
                        elif t.X > -1.01 && t.X < 1.01 && t.Y > -1.01 && t.Y < 1.01 then
                            v.c * V4d(1.0, 0.0, 0.0, 1.0)
                        else
                            outside 
              
                    if (pos.Z <= 0.0) then
                        return outside
                    else
                        return c 
             
                else
                    return v.c
            }

        let footprintV2 (v : FootPrintVertex) =
            vertex {
                let instrumentMatrix    : M44d   = uniform?instrumentMVP          
                let vp = uniform.ModelViewTrafo * v.pos
                let wp = uniform.ModelTrafo * v.pos
    
                return { 
                    v with
                        pos  = uniform.ProjTrafo * vp
                        wp   = wp
                        n    = transformNormal v.n
                        ldir = V3d.Zero - vp.XYZ |> Vec.normalize
                        tc0  = instrumentMatrix * v.pos                  

                } 
            } 
  
        let footPrintFOld (v : FootPrintVertex) =
            fragment {           
                if uniform?footprintVisible && uniform.HasRoverMVP then
                    let outside = v.c * 0.7 
                    let t = v.tc0.XY / v.tc0.W
                    let c = 
                        if t.X > -1.0 && t.X < 1.0 && t.Y > -1.0 && t.Y < 1.0 then
                            v.c 
                        elif t.X > -1.01 && t.X < 1.01 && t.Y > -1.01 && t.Y < 1.01 then
                            v.c * V4d(1.0, 0.0, 0.0, 1.0)
                        else
                            outside 
              
                    if (v.tc0.Z <= 0.0) then
                        return outside
                    else
                        return c 
             
                else
                    return v.c
            }
    
        let footPrintF2 (v : FootPrintVertex) =
            fragment {           
                if uniform?footprintVisible then
                    //let col = v.c * V4d(1.0, 0.0, 0.0, 1.0)
                    let proTex0 = v.tc0.XY / v.tc0.W
                    let c = footprintmap.Sample(proTex0)
                    let col = 
                        if (c.W <= 0.50) then
                            (v.c * 0.7)
                        elif (c.W <= 0.999) then
                            v.c * V4d(1.0, 0.0, 0.0, 1.0)
                        else
                            v.c
              
                    if (v.tc0.Z <= 0.0) then
                        return (v.c * 0.7)
                    else
                        return col 
                else
                    return v.c
            }
