namespace PRo3D.Core


open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.SceneGraph.Opc
open Aardvark.Application.Slim
open Aardvark.GeoSpatial.Opc

open Aardvark.UI
open Aardvark.SceneGraph
open FSharp.Data.Adaptive 
open MBrace.FsPickler
open PRo3D.Base.Annotation

open System.Collections.Generic
open Aardvark.Rendering
open System

module PackedRendering =

    module PointsShader =
        open FShade

        type PointVertex =
            {
                [<Position>] pos : V4d
                [<Semantic("Sizes")>] size : float
                [<PointSize>] pointSize : float
                [<Color>] c : V4d
                [<PointCoord>] tc : V2d
            }

        let pointSpriteVertex (v : PointVertex) =
            vertex {
                let p = uniform.ProjTrafo * v.pos 
                return { v with pointSize = v.size; pos = p }
            }

        let pointSpriteFragment (v : PointVertex) =
            fragment {
                let tc = v.tc

                let c = 2.0 * tc - V2d.II
                if c.Length > 1.0 then
                    discard()

                return v
            }

    module LineShader =

        open FShade

        type ThickLineVertex = 
            {
                [<Position>]                pos     : V4d
                [<Color>]                   c       : V4d
                [<Semantic("LineCoord")>]   lc      : V2d
                [<Semantic("Width")>]       w       : float
                [<Semantic("Id")>]          id      : int
                [<SourceVertexIndex>]       i       : int
            }


        // since we need special extension feature not provided by fshade we simply import the functionality (standard approach)
        [<GLSLIntrinsic("gl_DrawIDARB",requiredExtensions=[|"GL_ARB_shader_draw_parameters"|])>]
        let drawId () : int = raise <| FShade.Imperative.FShadeOnlyInShaderCodeException "drawId"

        type UniformScope with
            member x.MVs          : M44d[]  = x?StorageBuffer?MVs
            member x.LineWidths   : float[] = x?StorageBuffer?LineWidths
            member x.Colors       : V4d[]   = x?StorageBuffer?Colors
            member x.SelectedId   : int     = x?SelectedId
            member x.PickingTolerance : float = x?PickingTolerance


        let indirectLineVertexPicking (v : ThickLineVertex) =
            vertex {
                let id = drawId()
                let width = uniform.LineWidths.[id]
                let pos = uniform.MVs.[id] * v.pos
                let p = uniform.ProjTrafo * pos
                return 
                    { v with 
                        c = uniform.Colors.[id]; 
                        pos = p
                        w = width + 5.0 + uniform.PickingTolerance * 5.0
                        id = id
                    }
            }

        let indirectLineVertex (v : ThickLineVertex) =
            vertex {
                let id = drawId()
                let isSelected = id = uniform.SelectedId
                let width = uniform.LineWidths.[id]
                let pos = uniform.MVs.[id] * v.pos
                let p = uniform.ProjTrafo * pos
                return 
                    { v with 
                        c = if isSelected then V4d.IOOI else uniform.Colors.[id]; 
                        pos = p
                        w = if isSelected then width * 2.0 else width
                        id = id
                    }
            }

        [<GLSLIntrinsic("mix({0}, {1}, {2})")>]
        let Lerp (a : V4d) (b : V4d) (s : float) : V4d = failwith ""

        [<ReflectedDefinition>]
        let clipLine (plane : V4d) (p0 : ref<V4d>) (p1 : ref<V4d>) =
            let h0 = Vec.dot plane !p0
            let h1 = Vec.dot plane !p1

            // h = h0 + (h1 - h0)*t
            // 0 = h0 + (h1 - h0)*t
            // (h0 - h1)*t = h0
            // t = h0 / (h0 - h1)
            if h0 > 0.0 && h1 > 0.0 then
                false
            elif h0 < 0.0 && h1 > 0.0 then
                let t = h0 / (h0 - h1)
                p1 := !p0 + t * (!p1 - !p0)
                true
            elif h1 < 0.0 && h0 > 0.0 then
                let t = h0 / (h0 - h1)
                p0 := !p0 + t * (!p1 - !p0)
                true
            else
                true

        [<ReflectedDefinition>]
        let clipLinePure (plane : V4d) (p0 : V4d) (p1 : V4d) =
            let h0 = Vec.dot plane p0
            let h1 = Vec.dot plane p1

            // h = h0 + (h1 - h0)*t
            // 0 = h0 + (h1 - h0)*t
            // (h0 - h1)*t = h0
            // t = h0 / (h0 - h1)
            if h0 > 0.0 && h1 > 0.0 then
                (false, p0, p1)
            elif h0 < 0.0 && h1 > 0.0 then
                let t = h0 / (h0 - h1)
                let p11 = p0 + t * (p1 - p0)
                (true, p0, p11)
            elif h1 < 0.0 && h0 > 0.0 then
                let t = h0 / (h0 - h1)
                let p01 = p0 + t * (p1 - p0)
            
                (true, p01, p1)
            else
                (true, p0, p1)

        let thickLine (line : Line<ThickLineVertex>) =
            triangle {
                let t = line.P0.w
                let sizeF = V3d(float uniform.ViewportSize.X, float uniform.ViewportSize.Y, 1.0)

                let mutable pp0 = line.P0.pos
                let mutable pp1 = line.P1.pos        
                            
                let add = 2.0 * V2d(t,t) / sizeF.XY
                            
                let a0 = clipLine (V4d( 1.0,  0.0,  0.0, -(1.0 + add.X))) &&pp0 &&pp1
                let a1 = clipLine (V4d(-1.0,  0.0,  0.0, -(1.0 + add.X))) &&pp0 &&pp1
                let a2 = clipLine (V4d( 0.0,  1.0,  0.0, -(1.0 + add.Y))) &&pp0 &&pp1
                let a3 = clipLine (V4d( 0.0, -1.0,  0.0, -(1.0 + add.Y))) &&pp0 &&pp1
                let a4 = clipLine (V4d( 0.0,  0.0,  1.0, -1.0)) &&pp0 &&pp1
                let a5 = clipLine (V4d( 0.0,  0.0, -1.0, -1.0)) &&pp0 &&pp1

                if a0 && a1 && a2 && a3 && a4 && a5 then
                    let p0 = pp0.XYZ / pp0.W
                    let p1 = pp1.XYZ / pp1.W

                    let fwp = (p1.XYZ - p0.XYZ) * sizeF

                    let fw = V3d(fwp.XY, 0.0) |> Vec.normalize
                    let r = V3d(-fw.Y, fw.X, 0.0) / sizeF
                    let d = fw / sizeF
                    let p00 = p0 - r * t - d * t
                    let p10 = p0 + r * t - d * t
                    let p11 = p1 + r * t + d * t
                    let p01 = p1 - r * t + d * t

                    let rel = t / (Vec.length fwp)

                    yield { line.P0 with i = 0; pos = V4d(p00 * pp0.W, pp0.W); lc = V2d(-1.0, -rel); w = rel }      // restore W component for depthOffset
                    yield { line.P0 with i = 0; pos = V4d(p10 * pp1.W, pp1.W); lc = V2d( 1.0, -rel); w = rel }      // restore W component for depthOffset
                    yield { line.P1 with i = 1; pos = V4d(p01 * pp0.W, pp0.W); lc = V2d(-1.0, 1.0 + rel); w = rel } // restore W component for depthOffset
                    yield { line.P1 with i = 1; pos = V4d(p11 * pp1.W, pp1.W); lc = V2d( 1.0, 1.0 + rel); w = rel } // restore W component for depthOffset
            }

        let Effect =
            toEffect thickLine

    module Picking = 

        open FShade 

        type Vertex = 
            {
                [<Semantic("Id");Interpolation(InterpolationMode.Flat)>]
                id : int

                [<Position>] 
                pos : V4d

                [<Color>]
                c : V4d

            }

        [<GLSLIntrinsic("intBitsToFloat({0})")>]
        let intBitsToFloat (i : int) : float = failwith ""

        let pickId (v : Vertex) = 
            fragment {
                let i = v.id
                return V4d(v.c.X, v.c.Y, v.c.Z, intBitsToFloat i)
            }
        
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

    module LensShader = 
        open FShade
        open Aardvark.Rendering.Effects

        let s =
            sampler2d {
                texture uniform.DiffuseColorTexture
                filter Filter.MinMagMipLinear
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
            }

        type UniformScope with
            member x.MousePosition : V2d = uniform?MousePosition

        let lens (v : Vertex) =
            fragment {
                return s.SampleLevel(v.tc.XY * 0.1 - V2d.II*0.05  + uniform.MousePosition,0.0)
            }



    let lines (depthOffset : aval<float>) (selectedAnnotation : aval<int>) (annoSet: aset<Guid * AdaptiveAnnotation>) (view : aval<M44d>) =
          let data = 
              AVal.custom (fun t -> 
                  Log.startTimed "mk lines"
                  let annos = annoSet.Content.GetValue(t)
                  let modelTrafos = List<M44d>()
                  let vertices = List<_>()
                  let colors = List<_>()
                  let tolerances = List<float32>()
                  let lineWidths = List<float32>()
                  let dcis = List<DrawCallInfo>()
                  let ids = List<System.Guid>()
                  let mutable b = Box3d.Invalid
                  for (id,anno) in annos do   
                      let kind = anno.geometry.GetValue t
                      let p = PRo3D.Core.Drawing.Sg.getPolylinePoints anno
                      let ps = p.GetValue(t)
                      b <- Box3d(b, Box3d(ps))
                      let offset = 0.0
                      let color = anno.color.c.GetValue(t)
                      let thickness = anno.thickness.value.GetValue(t)
                      let tolerance = 0.0
                      let modelTrafo = anno.modelTrafo.GetValue(t)
                      let isVisible = anno.visible.GetValue(t)

                      let start = vertices.Count
                      for i in 0 .. ps.Length - 2 do
                          vertices.Add(modelTrafo.Backward.TransformPos ps.[i] |> V3f)
                          vertices.Add(modelTrafo.Backward.TransformPos ps.[i+1] |> V3f)


                      let dci = DrawCallInfo(FaceVertexCount = (ps.Length - 1) * 2, BaseVertex = start, FirstIndex = 0,
                                             FirstInstance = 0, InstanceCount = if isVisible then 1 else 0)
       

                      dcis.Add(dci)
                      ids.Add(id)
                      modelTrafos.Add(modelTrafo.Forward)
                      lineWidths.Add(float32 thickness)
                      colors.Add(C4f color)
                      tolerances.Add(float32 tolerance)

                  let r = 
                      {| points = vertices.ToArray();
                         drawCallInfos = dcis.ToArray();
                         modelTrafos = modelTrafos.ToArray();
                         lineWidths = lineWidths.ToArray();
                         colors = colors.ToArray();
                         tolerances = tolerances.ToArray() 
                         ids = ids.ToArray()
                      |}
                  Log.stop()
                  r, b
              )

          let instanceAttribs = AVal.map fst data
          let boundingBox = AVal.map snd data

          let mvs = 
            (instanceAttribs, view) ||> AVal.map2 (fun i v -> 
                let r = Array.map (fun m -> let r : M44d = v * m in M44f.op_Explicit r) i.modelTrafos
                r
            )
          let indirect = instanceAttribs |> AVal.map (fun i -> IndirectBuffer.ofArray true i.drawCallInfos)
          let sg = 
              Sg.indirectDraw IndexedGeometryMode.LineList indirect
              |> Sg.vertexAttribute DefaultSemantic.Positions (instanceAttribs |> AVal.map (fun i -> i.points))
              |> Sg.index  (instanceAttribs |> AVal.map (fun i -> Array.init (i.points.Length * 4) id))
              |> Sg.uniform "MVs" mvs
              |> Sg.uniform "LineWidths" (instanceAttribs |> AVal.map (fun i -> i.lineWidths))
              |> Sg.uniform "Colors" (instanceAttribs |> AVal.map (fun i -> i.colors))
              |> Sg.uniform "Tolerances" (instanceAttribs |> AVal.map (fun i -> i.tolerances))
              |> Sg.uniform "DepthOffset" (depthOffset |> AVal.map (fun depthWorld -> depthWorld / (100.0 - 0.1))) 
              |> Sg.uniform "SelectedId" selectedAnnotation
          sg, (instanceAttribs |> AVal.map (fun i -> i.ids )), boundingBox



    let pickRenderTarget (runtime : IRuntime) (pickingTolerance : aval<float>) lines (view : aval<CameraView>) (frustum : aval<Frustum>) (viewport : aval<V2i>) =
        let pickColors, pickDepth = 
            let signature =
                runtime.CreateFramebufferSignature [
                    DefaultSemantic.Colors, { format = RenderbufferFormat.Rgba32f; samples = 1 }
                    DefaultSemantic.Depth, { format = RenderbufferFormat.Depth24Stencil8; samples = 1 }
                ]

            lines
            |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
            |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo) //(size |> AVal.map (fun s -> Frustum.perspective 20.0 0.01 10000.0 (s.X / s.Y)))
            |> Sg.shader { 
                  do! LineShader.indirectLineVertexPicking
                  do! LineShader.thickLine
                  do! DepthOffset.depthOffsetFS 
                  do! Picking.pickId
            }
            |> Sg.uniform "PickingTolerance" (pickingTolerance |> AVal.map (fun p -> p * 2.0))
            |> Sg.compile runtime signature
            |> RenderTask.renderToColorAndDepth viewport

        pickColors


    let packedRender lines =
        lines 
        |> Sg.shader { 
                do! LineShader.indirectLineVertex
                do! LineShader.thickLine
                do! DepthOffset.depthOffsetFS 
        }