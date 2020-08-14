namespace PRo3D.Base
    
open Aardvark.Base
open Aardvark.Base.Rendering
open FShade
open Aardvark.GeoSpatial.Opc
open OpcViewer.Base
open Aardvark.GeoSpatial.Opc.Shader

//// TODO "needed to move to module - needs merge"
//module OtherShader =
//module Shader =
          
//        type UniformScope with
//            member x.PointSize : float = uniform?PointSize

//        type SuperVertex = 
//            {
//                [<Position>] pos :  V4d
//                [<SourceVertexIndex>] i : int
//            }
        

//        type pointVertex =
//            {
//                [<Position>] pos : V4d
//                [<PointSize>] p : float
//                [<Color>] c : V4d
//                [<TexCoord; Interpolation(InterpolationMode.Sample)>] tc : V2d
//                [<SourceVertexIndex>] i : int
//            }

//        let constantColor (color : V4d) (v : pointVertex) =
//            vertex {
//                let ps : float = uniform?PointSize
//                return { v with c = color; p = ps }
//            }

//        let singleColor (v : pointVertex) =
//            vertex {
//                let ps : float = uniform?PointSize
//                let c  : V4d   = uniform?SingleColor
//                return { v with c = c; p = ps }
//            }

//        let differentColor (v : pointVertex) =
//            vertex {
//                let ps : float = uniform?PointSize
//                return { v with c = v.c; p = ps }
//            }

//        let pointTrafo (v : pointVertex) =
//            vertex {
//                let vp = uniform.ModelViewTrafo * v.pos
//                return { 
//                    v with 
//                        pos = uniform.ProjTrafo * vp
//                }
//            }

//        let pointSpriteFragment (v : pointVertex) =
//            fragment {
//                let tc = v.tc

//                let c = 2.0 * tc - V2d.II
//                if c.Length > 1.0 then
//                    discard()

//                return v
//            }

//        let lines (t : Triangle<pointVertex>) =
//            line {
//                yield t.P0
//                yield t.P1
//                restartStrip()
            
//                yield t.P1
//                yield t.P2
//                restartStrip()

//                yield t.P2
//                yield t.P0
//                restartStrip()
//            }

//        //let lines (t : Triangle<SuperVertex>) =
//        //    line {
//        //        yield t.P0
//        //        yield t.P1
//        //        restartStrip()
            
//        //        yield t.P1
//        //        yield t.P2
//        //        restartStrip()

//        //        yield t.P2
//        //        yield t.P0
//        //        restartStrip()
//        //    }
        
    
//        let private colormap =
//            sampler2d {
//                texture uniform?ColorMapTexture
//                filter Filter.MinMagMipLinear
//                addressU WrapMode.Wrap
//                addressV WrapMode.Wrap
//        }

//        let falseColor (v : Vertex) =
//            fragment {           
//                if uniform?falseColors then
//                    let range : V2d = uniform?MinMax
//                    let norm = V2d((v.scalar - range.X)/ (range.Y - range.X), 0.5)

//                    let c = colormap.Sample(norm).XYZ

//                    return v.c * V4d(c.X, c.Y, c.Z, 1.0)
//                else
//                    return v.c
//            }

//        let selectionColor (v : Vertex) =
//            fragment {
//                if uniform?selected then
//                    //let c : V4d = uniform?selectionColor
//                    //return c * v.c
//                    let gamma = 1.3
//                    return V4d(v.c.X ** (1.0 / gamma), v.c.Y ** (1.0 / gamma),v.c.Z ** (1.0 / gamma), 1.0)
//                else return v.c
//            }

    
    
//        let private diffuseSampler =
//            sampler2d {
//                texture uniform?DiffuseColorTexture
//                filter Filter.Anisotropic
//                maxAnisotropy 16
//                addressU WrapMode.Wrap
//                addressV WrapMode.Wrap
//            }
    
//        let improvedDiffuseTexture (v : Effects.Vertex) =
//            fragment {
//                let texColor = diffuseSampler.Sample(v.tc,-1.0)
//                return texColor
//            }
            
//        let falseColorLegend2 (v : Vertex) =
//            fragment {    
    
//                if (uniform?falseColors) 
//                then
//                    let hue = mapFalseColors v.scalar //mapFalseColors2 v.scalar
//                    let c = hsv2rgb ((clamp 0.0 255.0 hue)/ 255.0 ) 1.0 1.0 // 
//                    return v.c * V4d(c.X, c.Y, c.Z, 1.0)
//                else
//                    return v.c
//            }
    
//        let falseColorLegendTest (v : Vertex) =
//            fragment {    
    
//                if (uniform?falseColors) 
//                then
//                    let fcUpperBound     = uniform?upperBound
//                    let fcLowerBound     = uniform?lowerBound
//                    let k = (v.scalar - fcLowerBound) / (fcUpperBound-fcLowerBound) 
//                    let value = clamp 0.0 1.0 k
//                    return V4d(value, value, value, 1.0) //value // V4d(value, value, value, 1.0)
//                else
//                    return v.c
//            }
      
//        [<ReflectedDefinition>]
//        let myTrunc (value : float) =
//            clamp 0.0 255.0 value
  

//        //TODO LF ... put all color adaptation mechanisms into 1 shader. Shader code produced by FShade has a ridiculous size ~6500 lines of code
    
//        [<ReflectedDefinition>]
//        let mapContrast (col : V4d) =
//        //let mapContrast (v : Vertex) =
//            //fragment { 
//                if (uniform?useContrastS) then
//                    let c = uniform?contrastS
//                    let nc = V4d(col.X*255.0, col.Y*255.0, col.Z*255.0, 255.0)
            
//                    let factor = (259.0 * (c + 255.0)) / (255.0 * (259.0 - c))
//                    let red    = (myTrunc (factor * (nc.X   - 128.0) + 128.0)) / 255.0
//                    let green  = (myTrunc (factor * (nc.Y   - 128.0) + 128.0)) / 255.0
//                    let blue   = (myTrunc (factor * (nc.Z   - 128.0) + 128.0)) / 255.0
//                    V4d(red, green, blue, 1.0)
//                else
//                    col
//            //}
    
//        [<ReflectedDefinition>]
//        let mapBrightness (col : V4d) = 
//        //let mapBrightness (v : Vertex) = 
//            //fragment { 
//                if (uniform?useBrightnS) then
//                    let b = uniform?brightnessS
//                    let nc = V4d(col.X*255.0, col.Y*255.0, col.Z*255.0, 255.0)        
            
//                    let red   = (myTrunc(nc.X + b)) / 255.0 
//                    let green = (myTrunc(nc.Y + b)) / 255.0 
//                    let blue  = (myTrunc(nc.Z + b)) / 255.0 
//                    V4d(red, green, blue, 1.0)
//                else
//                    col
//           // }
    
//        [<ReflectedDefinition>]
//        let mapGamma (col : V4d) = 
//        //let mapGamma (v : Vertex) = 
//            //fragment { 
//                if (uniform?useGammaS) then
//                    let g = uniform?gammaS
//                    let gammaCorrection = 1.0 / g
//                    V4d(col.X**gammaCorrection, col.Y**gammaCorrection, col.Z**gammaCorrection, 1.0)
//                else
//                    col
//            //}
    
//        [<ReflectedDefinition>]
//        let grayscale (col : V4d) =
//        //let grayscale (v : Vertex) = 
//        //    fragment { 
//                if (uniform?useGrayS) then
//                    let value = col.X * 0.299 + col.Y * 0.587 + col.Z * 0.114
//                    V4d(value, value, value, 1.0)
//                else
//                    col
//            //}
    
//        [<ReflectedDefinition>]
//        let addColor (col : V4d) =
//        //let addColor (v : Vertex) = 
//            //fragment { 
//                if (uniform?useColorS) then
//                    let hue : V3d =  uniform?colorS
//                    let nc = V4d(col.X*255.0, col.Y*255.0, col.Z*255.0, 255.0)

//                    let red   = (myTrunc( nc.X * hue.X)) / 255.0
//                    let green = (myTrunc( nc.Y * hue.Y)) / 255.0
//                    let blue  = (myTrunc( nc.Z * hue.Z)) / 255.0
//                    V4d(red, green, blue, 1.0)
//                else
//                    col
//            //}
    
//        let mapColorAdaption (v : Vertex) =
//            fragment { 
//                return v.c
//                        |> addColor
//                        |> mapContrast
//                        |> mapBrightness
//                        |> mapGamma
//                        |> grayscale                    
//            }
  
//        type FootPrintVertex =
//            {
//                [<Position>]                pos     : V4d            
//                [<WorldPosition>]           wp      : V4d
//                [<TexCoord>]                tc      : V2d
//                [<Color>]                   c       : V4d
//                [<Normal>]                  n       : V3d
//                [<SourceVertexIndex>]       sv      : int
//                [<Semantic("Scalar")>]      scalar  : float
//                [<Semantic("LightDir")>]    ldir    : V3d
//                [<Semantic("Tex0")>]        tc0     : V4d

//            }

//        type RoverVertex =
//            {
//                [<Position>]                  pos         : V4d    
//                [<Semantic("RoverPosProj")>]  posProj     : V4d
//                [<Color>]                     c       : V4d
//            }


      
//        let private footprintmap =
//            sampler2d {
//                texture uniform?FootPrintTexture
//                filter Filter.MinMagMipLinear
//                borderColor C4f.Black
//                addressU WrapMode.Border
//                addressV WrapMode.Border
//                addressW WrapMode.Border
//        }

//        type UniformScope with
//            member x.RoverMVP : M44d = uniform?RoverMVP
//            member x.HasRoverMVP : bool = uniform?HasRoverMVP

//        let footprintV (v : RoverVertex) =
//            vertex {
//                let roverProjSpace = uniform.RoverMVP * v.pos

//                return { v with posProj = roverProjSpace  } //v.wp
//            }

//        let footPrintF (v : RoverVertex) =
//            fragment {           
//                if uniform?footprintVisible && uniform.HasRoverMVP then
//                    let outside = v.c * 0.7 
//                    let pos = v.posProj
//                    let t = pos.XY / pos.W
//                    let c = 
//                        if t.X > -1.0 && t.X < 1.0 && t.Y > -1.0 && t.Y < 1.0 then
//                            v.c 
//                        elif t.X > -1.01 && t.X < 1.01 && t.Y > -1.01 && t.Y < 1.01 then
//                            v.c * V4d(1.0, 0.0, 0.0, 1.0)
//                        else
//                            outside 
              
//                    if (pos.Z <= 0.0) then
//                        return outside
//                    else
//                        return c 
             
//                else
//                    return v.c
//            }

//        let footprintV2 (v : FootPrintVertex) =
//            vertex {
//                let instrumentMatrix    : M44d   = uniform?instrumentMVP          
//                let vp = uniform.ModelViewTrafo * v.pos
//                let wp = uniform.ModelTrafo * v.pos
    
//                return { 
//                    v with
//                        pos  = uniform.ProjTrafo * vp
//                        wp   = wp
//                        n    = transformNormal v.n
//                        ldir = V3d.Zero - vp.XYZ |> Vec.normalize
//                        tc0  = instrumentMatrix * v.pos                  

//                } 
//            } 
  
//        let footPrintFOld (v : FootPrintVertex) =
//            fragment {           
//                if uniform?footprintVisible && uniform.HasRoverMVP then
//                    let outside = v.c * 0.7 
//                    let t = v.tc0.XY / v.tc0.W
//                    let c = 
//                        if t.X > -1.0 && t.X < 1.0 && t.Y > -1.0 && t.Y < 1.0 then
//                            v.c 
//                        elif t.X > -1.01 && t.X < 1.01 && t.Y > -1.01 && t.Y < 1.01 then
//                            v.c * V4d(1.0, 0.0, 0.0, 1.0)
//                        else
//                            outside 
              
//                    if (v.tc0.Z <= 0.0) then
//                        return outside
//                    else
//                        return c 
             
//                else
//                    return v.c
//            }
    
//        let footPrintF2 (v : FootPrintVertex) =
//            fragment {           
//                if uniform?footprintVisible then
//                    //let col = v.c * V4d(1.0, 0.0, 0.0, 1.0)
//                    let proTex0 = v.tc0.XY / v.tc0.W
//                    let c = footprintmap.Sample(proTex0)
//                    let col = 
//                        if (c.W <= 0.50) then
//                            (v.c * 0.7)
//                        elif (c.W <= 0.999) then
//                            v.c * V4d(1.0, 0.0, 0.0, 1.0)
//                        else
//                            v.c
              
//                    if (v.tc0.Z <= 0.0) then
//                        return (v.c * 0.7)
//                    else
//                        return col 
//                else
//                    return v.c
//            }
