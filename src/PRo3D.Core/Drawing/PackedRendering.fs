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
open PRo3D.Core.Drawing
open PRo3D.Base

open Adaptify.FSharp.Core

module PackedRendering =


    module StableLight =
        open FShade

        type AttrVertex =
            {
                [<Position>]                pos     : V4d            
                [<TexCoord>]                tc      : V2d
                [<Color>]                   c       : V4d
                [<Normal>]                  n       : V3d
                [<Semantic("LightDir")>]    ldir    : V3d
            }

        let stableLight (v : AttrVertex) =
            fragment {
                let n = v.n |> Vec.normalize
                let c = v.ldir |> Vec.normalize
     
                let diffuse = Vec.dot c n |> abs            
 
                return V4d(v.c.XYZ * diffuse, v.c.W)
            }

        [<ReflectedDefinition>]
        let transformNormal (n : V3d) =
            uniform.ModelViewTrafoInv.Transposed * V4d(n, 0.0)
            |> Vec.xyz
            |> Vec.normalize

        let stableTrafo' (v : AttrVertex) =
            vertex {
                let mvp : M44d = uniform?ModelViewTrafo
                let vp = mvp * v.pos
                return  
                    { v with
                        pos  = uniform.ProjTrafo * vp
                        n    = transformNormal v.n
                        ldir = V3d.Zero - vp.XYZ |> Vec.normalize
                    } 
            } 


        type UniformScope with
            member x.Color : V4d = uniform?Color

        let uniformColor (v : Effects.Vertex) =
            fragment {
                return uniform.Color
            }


    module PointsShader =
        open FShade
        open PRo3D.Base.Shader.DepthOffset

        type PointVertex =
            {
                [<Position>] pos : V4d
                [<Semantic("np")>] np : V4d
                [<Semantic("Sizes")>] size : float
                [<PointSize>] pointSize : float
                [<Color>] c : V4d
                [<PointCoord>] tc : V2d

                [<Depth(DepthWriteMode.OnlyLess)>]
                depth : float
            }

        let pointSpriteVertex (v : PointVertex) =
            vertex {
                let p = uniform.ProjTrafo * v.pos 
                return { v with pointSize = v.size; pos = p; np = v.pos }
            }

        let pointSpriteFragment (v : PointVertex) =
            fragment {
                let tc = v.tc

                let c = 2.0 * tc - V2d.II
                if c.Length > 1.0 then
                    discard()

                let n = V3d(c, Math.Sqrt(1.0 -  Vec.dot c c)) |> Vec.normalize
                let p = v.np + V4d(n, 1.0)

                let pp = uniform.ProjTrafo * p
                let nd = pp.Z / pp.W
                let d = (nd + 1.0) / 2.0

                let d = (d - uniform.DepthOffset)  / v.pos.W
                return { v with c = v.c;  depth = ((depthDiff() * d) + depthNear() + depthFar()) / 2.0  }
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

                [<Semantic("PickingTolerance")>] tolerance : float
                [<Semantic("LineWidth")>] width : float
                [<Semantic("ObjId")>] obId : int
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

            member x.MV : M44d = x?MV


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

        let noIndirectLineVertexPicking (v : ThickLineVertex) =
            vertex {
                let width = v.width
                let pos = uniform.MV * v.pos
                let p = uniform.ProjTrafo * pos
                return 
                    { v with 
                        c = v.c 
                        pos = p
                        w = width + 5.0 + v.tolerance * 5.0
                        id = v.obId
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

        let noIndirectLineVertex (v : ThickLineVertex) =
            vertex {
                let id = v.obId
                let isSelected = id = uniform.SelectedId
                let width = v.width
                let pos = uniform.MV * v.pos
                let p = uniform.ProjTrafo * pos
                return 
                    { v with 
                        c = if isSelected then V4d.IOOI else v.c
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



    let lines__ (depthOffset : aval<float>) (selectedAnnotation : aval<int>) (selected : aset<Guid>) (annoSet: aset<Guid * AdaptiveAnnotation>) (view : aval<M44d>) =
          let data = 
              AVal.custom (fun t -> 
                  Log.startTimed "mk lines"
                  let annos = annoSet.Content.GetValue(t)
                  let selected = selected.Content.GetValue(t)
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
                      let color = if HashSet.contains id selected then C4b.VRVisGreen else anno.color.c.GetValue(t)
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


    let linesNoIndirect (depthOffset : aval<float>) (selectedAnnotation : aval<int>) (selected : aset<Guid>) (annoSet: aset<Guid * AdaptiveAnnotation>) (view : aval<M44d>) =
          let data = 
              AVal.custom (fun t -> 
                  Log.startTimed "mk lines"
                  let annos = annoSet.Content.GetValue(t)
                  let selected = selected.Content.GetValue(t)
                  let vertices = List<_>()
                  let colors = List<_>()
                  let tolerances = List<float32>()
                  let lineWidths = List<float32>()
                  let dcis = List<DrawCallInfo>()
                  let annoId = List<int>()
                  let ids = List<System.Guid>()
                  let mutable b = Box3d.Invalid

                  let mutable modelTrafo = None

                  let mutable oid = 0
                  for (id,anno) in annos do   
                      let kind = anno.geometry.GetValue t
                      let p = PRo3D.Core.Drawing.Sg.getPolylinePoints anno
                      let ps = p.GetValue(t)
                      b <- Box3d(b, Box3d(ps))
                      let offset = 0.0
                      let color = if HashSet.contains id selected then C4b.VRVisGreen else anno.color.c.GetValue(t)
                      let thickness = anno.thickness.value.GetValue(t)
                      let tolerance = 0.0
                      let modelTrafo = 
                          match modelTrafo with
                          | None -> 
                                let t = anno.modelTrafo.GetValue(t)
                                modelTrafo <- Some t
                                t
                          | Some t -> t

                      let isVisible = anno.visible.GetValue(t)

                      ids.Add(id)

                      if isVisible then
                          for i in 0 .. ps.Length - 2 do
                              vertices.Add(modelTrafo.Backward.TransformPos ps.[i] |> V3f)
                              vertices.Add(modelTrafo.Backward.TransformPos ps.[i+1] |> V3f)
                              lineWidths.Add(float32 thickness)
                              lineWidths.Add(float32 thickness)
                              annoId.Add(oid)
                              annoId.Add(oid)
                              colors.Add(C4f color)
                              colors.Add(C4f color)
                              tolerances.Add(float32 tolerance)
                              tolerances.Add(float32 tolerance)

                      oid <- oid + 1


                  let r = 
                      {| points = vertices.ToArray();
                         drawCallInfos = dcis.ToArray();
                         lineWidths = lineWidths.ToArray();
                         colors = colors.ToArray();
                         tolerances = tolerances.ToArray() 
                         ids = ids.ToArray()
                         annoId = annoId.ToArray()
                         modelTrafo = Option.defaultValue Trafo3d.Identity modelTrafo
                      |}
                  Log.stop()
                  r, b
              )

          let instanceAttribs = AVal.map fst data
          let boundingBox = AVal.map snd data
          let mv = (data, view) ||> AVal.map2 (fun d v -> v * (fst d).modelTrafo.Forward)
          let sg = 
              Sg.draw IndexedGeometryMode.LineList
              |> Sg.vertexAttribute DefaultSemantic.Positions (instanceAttribs |> AVal.map (fun i -> i.points))
              |> Sg.vertexAttribute (Sym.ofString "LineWidth") (instanceAttribs |> AVal.map (fun i -> i.lineWidths))
              |> Sg.vertexAttribute (Sym.ofString "ObjId") (instanceAttribs |> AVal.map (fun i -> i.annoId))
              |> Sg.vertexAttribute DefaultSemantic.Colors (instanceAttribs |> AVal.map (fun i -> i.colors))
              |> Sg.vertexAttribute (Sym.ofString "PickingTolerance") (instanceAttribs |> AVal.map (fun i -> i.tolerances))
              |> Sg.uniform "DepthOffset" (depthOffset |> AVal.map (fun depthWorld -> depthWorld / (100.0 - 0.1))) 
              |> Sg.uniform "MV" mv
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
                  do! LineShader.noIndirectLineVertexPicking
                  do! LineShader.thickLine
                  do! PRo3D.Base.Shader.DepthOffset.depthOffsetFS 
                  do! Picking.pickId
            }
            |> Sg.uniform "PickingTolerance" (pickingTolerance |> AVal.map (fun p -> p * 2.0))
            |> Sg.compile runtime signature
            |> RenderTask.renderToColorAndDepth viewport

        pickColors


    let packedRender lines =
        lines 
        |> Sg.shader { 
                do! LineShader.noIndirectLineVertex
                do! LineShader.thickLine
                do! PRo3D.Base.Shader.DepthOffset.depthOffsetFS 
        }


    let points (selected : aset<Guid>) (annoSet: aset<Guid * AdaptiveAnnotation>) (depthOffset : aval<float>) (view : aval<M44d>) =
        let instanceAttribs = 
            AVal.custom (fun t -> 
                Log.startTimed "creating points"
                let annos = annoSet.Content.GetValue(t)
                let selected = selected.Content.GetValue(t)
                let modelPos = List<V3d>()
                let colors = List<C4b>()
                let sizes = List<float32>()
                for (id,anno) in annos do   
                    let kind = anno.geometry.GetValue t
                    let isVisible = anno.visible.GetValue(t)
                    if isVisible then
                        let isSelected = HashSet.exists (fun (x : Guid) -> x = id) selected
                        let c = anno.color.c
                        let color = if isSelected then C4b.VRVisGreen else c.GetValue(t)
                        match kind with
                        | Geometry.Point ->
                            let p = PRo3D.Core.Drawing.Sg.getPolylinePoints anno
                            let c = anno.color.c
                            let size = anno.thickness.value |> AVal.map(fun x -> x + 0.5)
                            let px = p.GetValue(t)
                            modelPos.Add(px.[0])
                            colors.Add(color)
                            sizes.Add(float32 <| size.GetValue(t))
                        | Geometry.DnS -> 
                            if isSelected then
                                let p = PRo3D.Core.Drawing.Sg.getPolylinePoints anno
                                let c = anno.color.c.GetValue(t)
                                let size = anno.thickness.value |> AVal.map(fun x -> x + 0.5)
                                let size = size.GetValue(t)
                                let px = p.GetValue(t)
                                for p in px do 
                                    modelPos.Add(p)
                                    colors.Add(color)
                                    sizes.Add(float32 size)
                        | _ -> ()

                Log.stop()
                modelPos.ToArray(), colors.ToArray(), sizes.ToArray()
            )
        let mvs = 
            (instanceAttribs, view) ||> AVal.map2 (fun (p,_,_) v -> 
                let r = Array.map (fun p -> V3f (v.TransformPos p)) p
                r
            )
        let colors = instanceAttribs |> AVal.map (fun (mvp, c, s) -> c)
        let sizes = instanceAttribs |> AVal.map (fun (mvp, c, s) -> s )
        Sg.draw IndexedGeometryMode.PointList
        |> Sg.vertexAttribute DefaultSemantic.Positions mvs
        |> Sg.vertexAttribute DefaultSemantic.Colors colors
        |> Sg.vertexAttribute "Sizes" sizes
        |> Sg.uniform "DepthOffset" depthOffset
        |> Sg.shader { 
              do! PointsShader.pointSpriteVertex
              do! PointsShader.pointSpriteFragment
              //do! DepthOffset.depthOffsetFS
           }


    let fastDns (config : Sg.innerViewConfig) (fcm : AdaptiveFalseColorsModel) (annoSet: aset<Guid * AdaptiveAnnotation>) (view : aval<CameraView>) = 
        
        let stableLight = 
            FShade.Effect.compose [
                //do! Shader.screenSpaceScale
                StableLight.stableTrafo'   |> toEffect
                StableLight.uniformColor   |> toEffect
                StableLight.stableLight    |> toEffect
            ]

        let scaledLines = 
            FShade.Effect.compose [
                toEffect DefaultSurfaces.stableTrafo
                toEffect DefaultSurfaces.vertexColor
                toEffect DefaultSurfaces.thickLine
            ]

        let attributes = AVal.custom (fun t -> 
            Log.line "create DNS annotations"
            let discsTrafos = List<_>()
            let discColors = List<C4b>()
            let coneTrafos = List<_>()
            let coneColors = List<_>()
            
            let annos = annoSet.Content.GetValue(t)
            let planeSize = config.dnsPlaneSize.GetValue(t)
            let arrowLength = config.arrowLength.GetValue(t)
            let arrowThickness = config.arrowThickness.GetValue(t)

            let lineVertices = List<V3f>()
            let lineColors = List<C4b>()

            let mutable generalLineTrafo = None

            for (id,anno) in annos do
                let visible = anno.visible.GetValue(t)
                let showDns = anno.showDns.GetValue(t)
                let dnsResults = anno.dnsResults.GetValue(t)
                match dnsResults with
                | AdaptiveSome s when visible && showDns -> 
                    let p = PRo3D.Core.Drawing.Sg.getPolylinePoints anno
                    let dipAngle = s.dipAngle.GetValue(t)
                    let _ = fcm.Current.GetValue(t)
                    let r = PRo3D.FalseColorLegendApp.Draw.getColorDnS fcm s.dipAngle
                    let ps = p.GetValue(t)
                    let color = r.GetValue(t)

                    if ps.Length > 0 then
                        let center = ps.[ps.Length / 2]
                        
                        let lengthFactor = 
                            (ps |> Array.toList |> Calculations.getDistance) / 3.0

                        let posTrafo = center |> Trafo3d.Translation

                        let modelTrafoLines = 
                            match generalLineTrafo with
                            | None -> 
                                let modelTrafo = anno.modelTrafo.GetValue(t)
                                generalLineTrafo <- Some modelTrafo
                                modelTrafo
                            | Some t -> t

                        let plane = s.plane.GetValue(t)
                        let lineLength = arrowLength * lengthFactor

                        let discRadius = planeSize * lengthFactor
                        // disc
                        let discTrafo = Trafo3d.RotateInto(V3d.ZAxis, plane.Normal) * posTrafo
                        let discThickness = discRadius * 0.01
                        let cylinderTrafo = Trafo3d.Scale(discRadius,discRadius,discThickness) * discTrafo
                        discsTrafos.Add(cylinderTrafo)
                        discColors.Add(color)

                        // dip arrow
                        let dip = s.dipDirection.GetValue(t)
                        let coneHeight = lineLength * 0.2
                        let coneRadius = coneHeight * 0.3
                        let dipHeadTrafo = Trafo3d.RotateInto(V3d.ZAxis, dip) * Trafo3d.Translation(center + dip.Normalized * lineLength)
                        let coneTrafo = Trafo3d.Scale(coneRadius, coneRadius, coneHeight) * dipHeadTrafo
                        coneTrafos.Add(coneTrafo)
                        coneColors.Add(color)

                        // dip arrow (line)
                        let dipLine = Line3d(center, center + dip.Normalized * lineLength)
                        lineVertices.Add(modelTrafoLines.Backward.TransformPos(dipLine.P0) |> V3f)
                        lineVertices.Add(modelTrafoLines.Backward.TransformPos(dipLine.P1) |> V3f)
                        lineColors.Add(color); lineColors.Add(color)

                        // strike line
                        let strike = s.strikeDirection.GetValue(t)
                        let strikeLine = Line3d(center - strike.Normalized * lineLength, center + strike.Normalized * lineLength)
                        lineVertices.Add(modelTrafoLines.Backward.TransformPos(strikeLine.P0) |> V3f)
                        lineVertices.Add(modelTrafoLines.Backward.TransformPos(strikeLine.P1) |> V3f)
                        lineColors.Add(C4b.Red); lineColors.Add(C4b.Red)

                        ()
                    else 
                        ()
                | _ -> ()

            {| discTrafos = discsTrafos.ToArray(); discColors = discColors.ToArray(); 
               coneTrafos = coneTrafos.ToArray(); coneColors = coneColors.ToArray(); 
               modelTrafoLines = Option.defaultValue Trafo3d.Identity generalLineTrafo; lineVertices = lineVertices.ToArray(); lineColors = lineColors.ToArray() |}
        )

        let discSg = 
            let discModelViews = 
                (attributes,view) ||> AVal.map2 (fun d view -> 
                    let viewMatrix = (CameraView.viewTrafo view)
                    let forward = Array.zeroCreate d.discTrafos.Length
                    let backward = Array.zeroCreate d.discTrafos.Length
                    d.discTrafos |> Array.iteri (fun i (modelMatrix : Trafo3d) -> 
                        let mv =  modelMatrix * viewMatrix
                        forward.[i] <- M44f mv.Forward
                        backward.[i] <- M44f mv.Backward
                    ) 
                    forward :> System.Array, backward :> System.Array
                )
            let colors = attributes |> AVal.map (fun a -> a.discColors :> System.Array)

            let instancedUniforms =
                Map.ofList [
                    "ModelViewTrafo",    (typeof<M44f>,   AVal.map fst discModelViews)
                    "ModelViewTrafoInv", (typeof<M44f>,   AVal.map snd discModelViews)
                    "Color",             (typeof<C4b>,    colors        )
                ]

            let cylinder = 
                Sg.cylinder' 24 C4b.White 1.0 1.0
                |> Sg.effect [stableLight]

            Sg.instanced' instancedUniforms cylinder

        let coneSg = 
            let discModelViews = 
                (attributes,view) ||> AVal.map2 (fun d view -> 
                    let viewMatrix = (CameraView.viewTrafo view)
                    
                    let forward = Array.zeroCreate d.coneTrafos.Length
                    let backward = Array.zeroCreate d.coneTrafos.Length
                    d.coneTrafos |> Array.iteri (fun i (modelMatrix : Trafo3d) -> 
                        let mv =  modelMatrix * viewMatrix
                        forward.[i] <- M44f mv.Forward
                        backward.[i] <- M44f mv.Backward
                    ) 
                    forward :> System.Array, backward :> System.Array
                )
            let colors = attributes |> AVal.map (fun a -> a.coneColors :> System.Array)

            let instancedUniforms =
                Map.ofList [
                    "ModelViewTrafo",    (typeof<M44f>,   AVal.map fst discModelViews)
                    "ModelViewTrafoInv", (typeof<M44f>,   AVal.map snd discModelViews)
                    "Color",             (typeof<C4b>,    colors )
                ]

            let cone = 
                Sg.cone' 24 C4b.White 1.0 1.0
                |> Sg.effect [stableLight]

            Sg.instanced' instancedUniforms cone

        let lines = 
            Sg.draw IndexedGeometryMode.LineList
            |> Sg.vertexAttribute DefaultSemantic.Positions (attributes |> AVal.map (fun o -> o.lineVertices))
            |> Sg.vertexAttribute DefaultSemantic.Colors (attributes |> AVal.map (fun o -> o.lineColors))
            |> Sg.trafo (attributes |> AVal.map (fun a -> a.modelTrafoLines))
            |> Sg.uniform "LineWidth" config.arrowThickness    
            |> Sg.effect [scaledLines]

        Sg.ofSeq [discSg; coneSg; lines]