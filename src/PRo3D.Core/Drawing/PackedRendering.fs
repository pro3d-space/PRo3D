namespace PRo3D.Core


open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.Data.Opc
open Aardvark.Application.Slim
open Aardvark.GeoSpatial.Opc

open Aardvark.UI
open Aardvark.UI.Primitives

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
                [<Position>]                pos     : V4f            
                [<TexCoord>]                tc      : V2f
                [<Color>]                   c       : V4f
                [<Normal>]                  n       : V3f
                [<Semantic("LightDir")>]    ldir    : V3f
            }

        let stableLight (v : AttrVertex) =
            fragment {
                let n = v.n |> Vec.normalize
                let c = v.ldir |> Vec.normalize
     
                let diffuse = Vec.dot c n |> abs            
 
                return V4f(v.c.XYZ * diffuse, v.c.W)
            }

        [<ReflectedDefinition>]
        let transformNormal (n : V3f) =
            uniform.ModelViewTrafoInv.Transposed * V4f(n, 0.0f)
            |> Vec.xyz
            |> Vec.normalize

        let stableTrafo' (v : AttrVertex) =
            vertex {
                let mvp : M44f = uniform?ModelViewTrafo
                let vp = mvp * v.pos
                return  
                    { v with
                        pos  = uniform.ProjTrafo * vp
                        n    = transformNormal v.n
                        ldir = V3f.Zero - vp.XYZ |> Vec.normalize
                    } 
            } 


        type UniformScope with
            member x.Color : V4f = uniform?Color

        let uniformColor (v : Effects.Vertex) =
            fragment {
                return uniform.Color
            }


    module PointsShader =
        open FShade
        open PRo3D.Base.Shader.DepthOffset

        type PointVertex =
            {
                [<Position>] pos : V4f
                [<Semantic("np")>] np : V4f
                [<Semantic("Sizes")>] size : float32
                [<PointSize>] pointSize : float32
                [<Color>] c : V4f
                [<PointCoord>] tc : V2f

                [<Depth(DepthWriteMode.OnlyLess)>]
                depth : float32
            }

        let pointSpriteVertex (v : PointVertex) =
            vertex {
                let p = uniform.ProjTrafo * v.pos 
                return { v with pointSize = v.size; pos = p; np = v.pos }
            }

        let pointSpriteFragment (v : PointVertex) =
            fragment {
                let tc = v.tc

                let c = 2.0f * tc - V2f.II
                if c.Length > 1.0f then
                    discard()

                // the sprite reads as a small sphere: every fragment sits on the hemisphere
                // facing the camera, so its depth is that of the view-space centre pushed towards
                // the viewer along the sprite normal. The offset stays in XYZ - carrying it in a
                // V4f would also move the point off the w=1 plane and project it to an entirely
                // different depth.
                let n = V3f(c, sqrt(1.0f -  Vec.dot c c)) |> Vec.normalize
                let p = V4f(v.np.XYZ + n, 1.0f)

                let pp = uniform.ProjTrafo * p
                // Identical to PRo3D.Base.Shader.DepthOffset.depthOffsetFS, which is what the
                // lines and fills use: a bias applied in clip space and divided by w, so it stays
                // a constant distance in world units. Dividing the *depth* by w - what this did
                // before - drove every distant dot towards 0, i.e. in front of everything, which
                // is why dots showed through the terrain from the far side of the planet while
                // the lines and fills of the same annotation did not.
                let d = (pp.Z - uniform.DepthOffset) / pp.W
                return { v with c = v.c;  depth = ((depthDiff() * d) + depthNear() + depthFar()) / 2.0f  }
            }

    module LineShader =

        open FShade

        type ThickLineVertex = 
            {
                [<Position>]                pos     : V4f
                [<Color>]                   c       : V4f
                [<Semantic("LineCoord")>]   lc      : V2f
                [<Semantic("Width")>]       w       : float32
                [<Semantic("Id")>]          id      : int
                [<SourceVertexIndex>]       i       : int

                [<Semantic("PickingTolerance")>] tolerance : float32
                [<Semantic("LineWidth")>] width : float32
                [<Semantic("ObjId")>] obId : int
            }


        // since we need special extension feature not provided by fshade we simply import the functionality (standard approach)
        [<GLSLIntrinsic("gl_DrawIDARB",requiredExtensions=[|"GL_ARB_shader_draw_parameters"|])>]
        let drawId () : int = 
            onlyInShaderCode "drawId"

        type UniformScope with
            member x.MVs          : M44f[]  = x?StorageBuffer?MVs
            member x.LineWidths   : float32[] = x?StorageBuffer?LineWidths
            member x.Colors       : V4f[]   = x?StorageBuffer?Colors
            member x.SelectedId   : int     = x?SelectedId
            member x.PickingTolerance : float32 = x?PickingTolerance

            member x.MV : M44f = x?MV


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
                        w = width + 5.0f + uniform.PickingTolerance * 5.0f
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
                        w = width + 5.0f + v.tolerance * 5.0f
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
                        c = if isSelected then V4f.IOOI else uniform.Colors.[id]; 
                        pos = p
                        w = if isSelected then width * 2.0f else width
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
                        c = if isSelected then V4f.IOOI else v.c
                        pos = p
                        w = if isSelected then width * 2.0f else width
                        id = id
                    }
            }

        [<GLSLIntrinsic("mix({0}, {1}, {2})")>]
        let Lerp (a : V4f) (b : V4f) (s : float32) : V4f = failwith ""

        [<ReflectedDefinition>]
        let clipLine (plane : V4f) (p0 : ref<V4f>) (p1 : ref<V4f>) =
            let h0 = Vec.dot plane p0.Value
            let h1 = Vec.dot plane p1.Value

            // h = h0 + (h1 - h0)*t
            // 0 = h0 + (h1 - h0)*t
            // (h0 - h1)*t = h0
            // t = h0 / (h0 - h1)
            if h0 > 0.0f && h1 > 0.0f then
                false
            elif h0 < 0.0f && h1 > 0.0f then
                let t = h0 / (h0 - h1)
                p1.Value <- p0.Value + t * (p1.Value - p0.Value)
                true
            elif h1 < 0.0f && h0 > 0.0f then
                let t = h0 / (h0 - h1)
                p0.Value <- p0.Value + t * (p1.Value - p0.Value)
                true
            else
                true

        [<ReflectedDefinition>]
        let clipLinePure (plane : V4f) (p0 : V4f) (p1 : V4f) =
            let h0 = Vec.dot plane p0
            let h1 = Vec.dot plane p1

            // h = h0 + (h1 - h0)*t
            // 0 = h0 + (h1 - h0)*t
            // (h0 - h1)*t = h0
            // t = h0 / (h0 - h1)
            if h0 > 0.0f && h1 > 0.0f then
                (false, p0, p1)
            elif h0 < 0.0f && h1 > 0.0f then
                let t = h0 / (h0 - h1)
                let p11 = p0 + t * (p1 - p0)
                (true, p0, p11)
            elif h1 < 0.0f && h0 > 0.0f then
                let t = h0 / (h0 - h1)
                let p01 = p0 + t * (p1 - p0)
            
                (true, p01, p1)
            else
                (true, p0, p1)

        let thickLine (line : Line<ThickLineVertex>) =
            triangle {
                let t = line.P0.w
                let sizeF = V3f(float32 uniform.ViewportSize.X, float32 uniform.ViewportSize.Y, 1.0f)

                let mutable pp0 = line.P0.pos
                let mutable pp1 = line.P1.pos        
                            
                let add = 2.0f * V2f(t,t) / sizeF.XY
                            
                let a0 = clipLine (V4f( 1.0f,  0.0f,  0.0f, -(1.0f + add.X))) &&pp0 &&pp1
                let a1 = clipLine (V4f(-1.0f,  0.0f,  0.0f, -(1.0f + add.X))) &&pp0 &&pp1
                let a2 = clipLine (V4f( 0.0f,  1.0f,  0.0f, -(1.0f + add.Y))) &&pp0 &&pp1
                let a3 = clipLine (V4f( 0.0f, -1.0f,  0.0f, -(1.0f + add.Y))) &&pp0 &&pp1
                let a4 = clipLine (V4f( 0.0f,  0.0f,  1.0f, -1.0f)) &&pp0 &&pp1
                let a5 = clipLine (V4f( 0.0f,  0.0f, -1.0f, -1.0f)) &&pp0 &&pp1

                if a0 && a1 && a2 && a3 && a4 && a5 then
                    let p0 = pp0.XYZ / pp0.W
                    let p1 = pp1.XYZ / pp1.W

                    let fwp = (p1.XYZ - p0.XYZ) * sizeF

                    let fw = V3f(fwp.XY, 0.0f) |> Vec.normalize
                    let r = V3f(-fw.Y, fw.X, 0.0f) / sizeF
                    let d = fw / sizeF
                    let p00 = p0 - r * t - d * t
                    let p10 = p0 + r * t - d * t
                    let p11 = p1 + r * t + d * t
                    let p01 = p1 - r * t + d * t

                    let rel = t / (Vec.length fwp)

                    yield { line.P0 with i = 0; pos = V4f(p00 * pp0.W, pp0.W); lc = V2f(-1.0f, -rel); w = rel }      // restore W component for depthOffset
                    yield { line.P0 with i = 0; pos = V4f(p10 * pp1.W, pp1.W); lc = V2f( 1.0f, -rel); w = rel }      // restore W component for depthOffset
                    yield { line.P1 with i = 1; pos = V4f(p01 * pp0.W, pp0.W); lc = V2f(-1.0f, 1.0f + rel); w = rel } // restore W component for depthOffset
                    yield { line.P1 with i = 1; pos = V4f(p11 * pp1.W, pp1.W); lc = V2f( 1.0f, 1.0f + rel); w = rel } // restore W component for depthOffset
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
                pos : V4f

                [<Color>]
                c : V4f

            }

        /// As Vertex, plus a sub-index within the object - the control point index, for the vertex
        /// handle draw. Flat, like the object id, so no interpolation can corrupt it.
        type SubVertex =
            {
                [<Semantic("Id");Interpolation(InterpolationMode.Flat)>]
                id : int

                [<Semantic("SubId");Interpolation(InterpolationMode.Flat)>]
                subId : int

                [<Position>]
                pos : V4f

                [<Color>]
                c : V4f
            }

        [<GLSLIntrinsic("intBitsToFloat({0})")>]
        let intBitsToFloat (i : int) : float32 = failwith ""

        // The pick target is Rgba32f cleared to (0,0,0,-1).
        //
        //   alpha : the packed object id, indexing PackedRendering.orderedAnnotations. -1 = miss.
        //   red   : the sub-index within that object, or -1 when the fragment is not a sub-object.
        //           Today only vertex handles set it, which is how a readback tells "clicked the
        //           annotation" from "clicked one of its control points".
        //
        // Green and blue still carry the fragment colour and are read by nothing but the debug lens
        // overlay in OpcViewer's AnnotationViewer.

        let pickId (v : Vertex) =
            fragment {
                let i = v.id
                return V4f(-1.0f, v.c.Y, v.c.Z, float32 i)
            }

        /// As pickId, but writes the control point index into red so the readback can tell a handle
        /// hit from a hit on the annotation body.
        let pickVertexId (v : SubVertex) =
            fragment {
                return V4f(float32 v.subId, v.c.Y, v.c.Z, float32 v.id)
            }




    module FillShader =

        open FShade

        type FillVertex =
            {
                [<Position>] pos : V4f
                [<Color>]    c   : V4f
            }

        type FillPickVertex =
            {
                [<Position>] pos : V4f
                [<Color>]    c   : V4f
                [<Semantic("ObjId")>] obId : int
                [<Semantic("Id")>]    id   : int
            }

        /// Plain MV + projection. No geometry shader, so the clip-space W survives into the
        /// fragment stage untouched and depthOffsetFS can divide by it - unlike the line path,
        /// which has to restore W explicitly after thickLine.
        let fillVertex (v : FillVertex) =
            vertex {
                let mv : M44f = uniform?MV
                let vp = mv * v.pos
                return { v with pos = uniform.ProjTrafo * vp }
            }

        /// As fillVertex, but forwards the packed object id into the Id semantic Picking.pickId
        /// writes out.
        let fillVertexPicking (v : FillPickVertex) =
            vertex {
                let mv : M44f = uniform?MV
                let vp = mv * v.pos
                return { v with pos = uniform.ProjTrafo * vp; id = v.obId }
            }


    module VertexHandleShader =

        open FShade
        open PRo3D.Base.Shader.DepthOffset

        type HandleVertex =
            {
                [<Position>] pos : V4f

                /// Radius in pixels. Per vertex; the pick pass inflates it.
                [<Semantic("Sizes")>] size : float32

                [<Color>] c : V4f

                /// Which corner of the quad this vertex is, in [-1,1]^2. Supplied per vertex by
                /// `vertexHandles`, which emits six vertices per control point.
                ///
                /// The billboard is expanded here rather than in a geometry shader: a stock sprite
                /// stage such as DefaultSurfaces.pointSprite would not carry ObjId/SubId through,
                /// which is the whole point of this draw, and gl_PointSize would additionally
                /// depend on GL_PROGRAM_POINT_SIZE. Six vertices per handle is nothing.
                [<Semantic("HandleCorner")>] corner : V2f

                // ObjId is the per-vertex attribute; Id is what Picking.pickVertexId reads. Same
                // in/out pair the line path uses (ThickLineVertex.obId -> id).
                [<Semantic("ObjId")>] obId : int

                // Id and SubId travel to the fragment stage, so they must be flat - integers cannot
                // be interpolated - and must agree with Picking.SubVertex, which declares them flat.
                // Without this the pick pass composes two fragment shaders with conflicting
                // interpolation for the same varying and the draw silently produces nothing, while
                // the visible pass (which never sees Picking.SubVertex) renders perfectly.
                [<Semantic("Id"); Interpolation(InterpolationMode.Flat)>] id : int
                [<Semantic("SubId"); Interpolation(InterpolationMode.Flat)>] subId : int
            }

        type UniformScope with
            /// Control point index currently hovered, or -1. Compared per vertex, like SelectedId.
            member x.HoveredVertex : int = uniform?HoveredVertex
            /// Control point index currently grabbed, or -1.
            member x.GrabbedVertex : int = uniform?GrabbedVertex

            /// Size in pixels of the target this pass renders into.
            ///
            /// Supplied explicitly rather than read from uniform.ViewportSize, because the handles
            /// are drawn into two different targets - the render control and the offscreen pick
            /// buffer - and the quad has to be scaled by the size of whichever one it is in.
            member x.HandleViewport : V2f = uniform?HandleViewport

        // Everything below is written out inline rather than sharing a [<ReflectedDefinition>]
        // helper or module-level colour constants: FShade has to be able to inline the whole body,
        // and self-contained shader code removes that question entirely.
        //
        // The palette is deliberately independent of the annotation's own colour - handles have to
        // stay legible whatever the group colour is, and green already reads as "selected"
        // everywhere else in PRo3D.

        /// Places a handle from its local-space position through the MV uniform, matching the
        /// convention linesNoIndirect and fills use, then offsets it to its quad corner. The offset
        /// is scaled by the clip-space W so the perspective divide leaves a constant pixel size.
        let handleVertex (v : HandleVertex) =
            vertex {
                let mv : M44f = uniform?MV
                let clip = uniform.ProjTrafo * (mv * v.pos)

                // a zero viewport would push every corner to infinity and the quad would silently
                // never rasterize
                let vpX = max 1.0f uniform.HandleViewport.X
                let vpY = max 1.0f uniform.HandleViewport.Y
                // NDC spans 2 units across the viewport, hence the factor of two
                let dx = v.corner.X * v.size * 2.0f / vpX * clip.W
                let dy = v.corner.Y * v.size * 2.0f / vpY * clip.W

                let c =
                    if v.subId = uniform.GrabbedVertex then V4f(0.2f, 0.8f, 0.2f, 1.0f)
                    elif v.subId = uniform.HoveredVertex then V4f(1.0f, 0.85f, 0.1f, 1.0f)
                    else V4f(1.0f, 1.0f, 1.0f, 1.0f)

                return { v with pos = V4f(clip.X + dx, clip.Y + dy, clip.Z, clip.W); c = c }
            }

        /// As handleVertex, but fattens the quad so a handle is forgiving to click, and forwards the
        /// object id for Picking.pickVertexId. The colour is left alone - the pick pass reads ids.
        let handleVertexPicking (v : HandleVertex) =
            vertex {
                let mv : M44f = uniform?MV
                let clip = uniform.ProjTrafo * (mv * v.pos)

                let vpX = max 1.0f uniform.HandleViewport.X
                let vpY = max 1.0f uniform.HandleViewport.Y
                // literal, like the +5.0f noIndirectLineVertexPicking adds to line width: one less
                // uniform to bind on a pass whose only job is to be hit-tested
                let size = v.size + 5.0f
                let dx = v.corner.X * size * 2.0f / vpX * clip.W
                let dy = v.corner.Y * size * 2.0f / vpY * clip.W

                return { v with pos = V4f(clip.X + dx, clip.Y + dy, clip.Z, clip.W); id = v.obId }
            }

        /// Shades the sprite as a flat disc and discards outside it, so the pickable region is
        /// exactly the visible circle - which is what makes a grab land where it looks like it will.
        ///
        /// Writes no gl_FragDepth. PointsShader.pointSpriteFragment does, computing a sphere normal
        /// and re-projecting it, but a handle is an editing overlay for one annotation: it should be
        /// reachable whenever it is on screen, and both passes disable the depth test for exactly
        /// that reason. Custom depth here would only reintroduce the question.
        let handleFragment (v : HandleVertex) =
            fragment {
                // `corner` interpolates across the quad, so it doubles as the disc coordinate
                if v.corner.Length > 1.0f then
                    discard()
                return { v with c = v.c }
            }

        /// The pick pass's fragment stage: the same disc, writing the ids instead of a colour.
        ///
        /// One shader rather than handleFragment followed by Picking.pickVertexId. Chaining two
        /// fragment shaders makes FShade carry HandleVertex's whole record - including its
        /// [<Position>] - between them, and the draw silently produces nothing.
        ///
        /// Channel layout matches Picking.pickId: alpha is the packed object id, red is the
        /// sub-index within it (here the control point), and pickId writes -1 to red so that
        /// red >= 0 means "this is a handle".
        let handlePickFragment (v : HandleVertex) =
            fragment {
                if v.corner.Length > 1.0f then
                    discard()
                return V4f(float32 v.subId, v.c.Y, v.c.Z, float32 v.id)
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
            member x.MousePosition : V2f = uniform?MousePosition

        let lens (v : Vertex) =
            fragment {
                return s.SampleLevel(v.tc.XY * 0.1f - V2f.II*0.05f  + uniform.MousePosition,0.0f)
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
                      let ps = PRo3D.Core.Drawing.Sg.getPolylinePointsAt anno t
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
          let indirect = instanceAttribs |> AVal.map (fun i -> IndirectBuffer.ofArray' true 0 i.drawCallInfos.Length i.drawCallInfos)
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


    /// The packed object-id space. Object id N means "index N of this array", and the Guid there
    /// is what a pick reads back.
    ///
    /// Every packed draw that writes ObjId must derive its ids from this one cached ordering.
    /// Two separate enumerations of the same aset are not guaranteed to agree, and a
    /// disagreement shows up as clicking one annotation and selecting another.
    let orderedAnnotations (annoSet : aset<Guid * AdaptiveAnnotation>) : aval<(Guid * AdaptiveAnnotation)[]> =
        AVal.custom (fun t -> annoSet.Content.GetValue(t) |> HashSet.toArray)

    let linesNoIndirect (cbc : AdaptiveColorByCategoryModel) (depthOffset : aval<float>) (selectedAnnotation : aval<int>) (selected : aset<Guid>) (ordered : aval<(Guid * AdaptiveAnnotation)[]>) (view : aval<M44d>) =
          let data =
              AVal.custom (fun t ->
                  Log.startTimed "mk lines"
                  let annos = ordered.GetValue(t)
                  let selected = selected.Content.GetValue(t)
                  // hoisted above the annotation loop; every field is pulled with this
                  // token, so the color buffers rebuild on any panel edit
                  let cbcSettings =
                      if cbc.enabled.GetValue t then Some (ColorByCategory.readSettings cbc t) else None
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
                      let ps = PRo3D.Core.Drawing.Sg.getPolylinePointsAt anno t
                      b <- Box3d(b, Box3d(ps))
                      let offset = 0.0
                      let baseColor =
                          match cbcSettings with
                          | Some s -> ColorByCategory.resolve s anno t
                          | None   -> anno.color.c.GetValue(t)
                      let color = if HashSet.contains id selected then C4b.VRVisGreen else baseColor
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



    /// Radius added to a handle on top of the annotation's line thickness, so a control point is a
    /// target rather than a hairline.
    let private handleSizeBonus = 6.0f

    let pickRenderTarget (runtime : IRuntime) (pickingTolerance : aval<float>) lines fills handles (view : aval<CameraView>) (frustum : aval<Frustum>) (viewport : aval<V2i>) =
        let pickColors =
            let signature =
                runtime.CreateFramebufferSignature [
                    DefaultSemantic.Colors, TextureFormat.Rgba32f
                    DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
                ]

            let withCamera sg =
                sg
                |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
                |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo) //(size |> AVal.map (fun s -> Frustum.perspective 20.0 0.01 10000.0 (s.X / s.Y)))

            let pickColors =
                lines
                |> withCamera
                |> Sg.shader {
                      do! LineShader.noIndirectLineVertexPicking
                      do! LineShader.thickLine
                      do! PRo3D.Base.Shader.DepthOffset.depthOffsetFS
                      do! Picking.pickId
                }
                |> Sg.uniform "PickingTolerance" (pickingTolerance |> AVal.map (fun p -> p * 2.0))
                |> Sg.compile runtime signature

            // the object ids the fill emits index the same array as the lines', so a click inside
            // a filled annotation reads back the annotation it belongs to.
            //
            // Deliberately none of the visible pass's state: blending would corrupt the id, which
            // this target carries in the alpha channel.
            let pickFills =
                fills
                |> withCamera
                |> Sg.shader {
                      do! FillShader.fillVertexPicking
                      do! PRo3D.Base.Shader.DepthOffset.depthOffsetFS
                      do! Picking.pickId
                }
                |> Sg.compile runtime signature

            // control point handles of the annotation being edited. These write the same object id
            // in alpha, plus the control point index in red, which is how the readback tells a
            // handle from the annotation body.
            let pickHandles =
                handles
                |> withCamera
                |> Sg.uniform "HandleViewport" (viewport |> AVal.map (fun (v : V2i) -> V2d(float v.X, float v.Y)))
                |> Sg.shader {
                      do! VertexHandleShader.handleVertexPicking
                      do! VertexHandleShader.handlePickFragment
                }
                // No depth test. This pass contains no terrain - only lines, fills and handles -
                // so depth here only arbitrates between annotation geometry, and a handle must
                // always beat the outline running through it. Tuning depth offsets to achieve that
                // is fragile; drawing last with the test off says it outright.
                |> Sg.depthTest (AVal.constant DepthTest.None)
                |> Sg.compile runtime signature

            let cleared = RenderTask.ofList [
                runtime.CompileClear(signature, C4f(0.0f,0.0f,0.0f,-1.0f))
                // fills first so an outline still wins the pick over its own interior
                pickFills
                pickColors
                // handles last: grabbing a control point has to beat picking the line through it
                pickHandles
            ]

            cleared |> RenderTask.renderToColor viewport

        pickColors


    let packedRender lines =
        lines 
        |> Sg.shader { 
                do! LineShader.noIndirectLineVertex
                do! LineShader.thickLine
                do! PRo3D.Base.Shader.DepthOffset.depthOffsetFS 
        }


    /// The pick pass still draws the flat cap (it has no terrain to stencil against), and a flat
    /// cap needs a far larger bias than a surface-hugging line: it only touches the terrain at
    /// its rim.
    let private fillDepthOffsetFactor = 5.0

    // Aardvark.Rendering exports a Geometry type of its own, and it is opened after
    // PRo3D.Base.Annotation - so the annotation one needs saying explicitly
    type private AnnoGeometry = PRo3D.Base.Annotation.Geometry

    let private isFillable (g : AnnoGeometry) =
        match g with
        | AnnoGeometry.Polygon
        | AnnoGeometry.Ellipse
        | AnnoGeometry.AxisEllipse
        | AnnoGeometry.Axis4PEllipse -> true
        // DnS already visualises its plane through showDns; open geometries have no interior
        | _ -> false

    /// Packed fill geometry. Raw: the visible and pick passes need different shaders and very
    /// different render state, so neither is baked in. Same split as linesNoIndirect /
    /// packedRender.
    type FillGeometry =
        {
            /// Flat caps carrying the object id, for the pick pass.
            caps        : ISg
            /// Puts the prism buffers (positions, colours) and `MV` around `sg`, whose leaves
            /// draw ranges of them.
            withVolumes : ISg -> ISg
            /// The prism of fill i is vertices [first, first + count) of the buffers.
            ranges      : aval<DrawCallInfo[]>
        }

    let private identityTrafo = AVal.constant Trafo3d.Identity

    /// Builds the caps and prisms of every visible, filled annotation in `entries`. Position i
    /// in `entries` is object id i. Each entry's trafo is applied on the CPU, so annotations
    /// under different trafos still share one buffer.
    let private fillGeometry
        (cbc         : AdaptiveColorByCategoryModel)
        (planet      : aval<Planet>)
        (reach       : aval<FillReach>)
        (depthOffset : aval<float>)
        (entries     : aval<(AdaptiveAnnotation * aval<Trafo3d>)[]>)
        (view        : aval<M44d>) : FillGeometry =

        let data =
            AVal.custom (fun t ->
                let annos = entries.GetValue(t)
                let reachOf = FillReach.resolve (planet.GetValue t) (reach.GetValue t)
                let capPositions = List<V3f>()
                let capColors = List<C4f>()
                let capIds = List<int>()
                let volPositions = List<V3f>()
                let volColors = List<C4f>()
                // per fill: first vertex and vertex count of its prism in volPositions
                let volRanges = List<struct(int * int)>()
                let mutable pivot = None
                // hoisted like linesNoIndirect: Color by Category also recolors the fill
                let cbcSettings =
                    if cbc.enabled.GetValue t then Some (ColorByCategory.readSettings cbc t) else None

                for i in 0 .. annos.Length - 1 do
                    let (anno, trafo) = annos.[i]
                    let visible  = anno.visible.GetValue(t)
                    let showFill = anno.showFill.GetValue(t)
                    let geometry = anno.geometry.GetValue(t)

                    if visible && showFill && isFillable geometry then
                        let points = anno.points.Content.GetValue(t) |> IndexList.toArray

                        let plane =
                            // the dip-and-strike plane is what the ellipse ring was built on, so
                            // reusing it makes fill and outline coincide exactly
                            let fromDns =
                                match anno.dnsResults.GetValue(t) with
                                | AdaptiveSome dns ->
                                    let p = dns.plane.GetValue(t)
                                    SurfaceChart.tryOfPlane p |> Option.map (fun c -> c, p.Normal)
                                | _ -> None
                            // otherwise fit, using the same plane calculatePolygonArea reports on
                            fromDns
                            |> Option.orElseWith (fun () ->
                                let p = Calculations.calculateVertexPlane points
                                SurfaceChart.tryOfPlane p |> Option.map (fun c -> c, p.Normal))

                        // the plane normal is the extrusion axis: right on flat ground and on a
                        // vertical rock face alike, and free - no per-vertex up vector (SPICE)
                        match plane |> Option.bind (fun (chart, normal) -> FillVolume.tryCompute reachOf chart normal points) with
                        | None -> ()
                        | Some volume ->
                            let rgb =
                                match cbcSettings with
                                | Some s -> (ColorByCategory.resolve s anno t).ToC4f()
                                | None   -> anno.fillColor.c.GetValue(t).ToC4f()
                            let alpha = anno.fillAlpha.value.GetValue(t)
                            let color = C4f(rgb.R, rgb.G, rgb.B, float32 alpha)

                            let trafo = trafo.GetValue(t)
                            // world-space points shifted by a common pivot purely to keep the
                            // float32 vertex buffer precise at planetary magnitudes
                            let origin =
                                match pivot with
                                | Some p -> p
                                | None ->
                                    let p = trafo.Forward.TransformPos volume.cap.[0]
                                    pivot <- Some p
                                    p
                            let local (p : V3d) = trafo.Forward.TransformPos p - origin |> V3f

                            for p in volume.cap do
                                capPositions.Add(local p)
                                capColors.Add color
                                capIds.Add i
                            volRanges.Add(struct(volPositions.Count, volume.prism.Length))
                            for p in volume.prism do
                                volPositions.Add(local p)
                                volColors.Add color

                {| capPositions = capPositions.ToArray()
                   capColors    = capColors.ToArray()
                   capIds       = capIds.ToArray()
                   volPositions = volPositions.ToArray()
                   volColors    = volColors.ToArray()
                   volRanges    = volRanges.ToArray()
                   modelTrafo   = pivot |> Option.map Trafo3d.Translation |> Option.defaultValue Trafo3d.Identity |})

        let mv = (data, view) ||> AVal.map2 (fun d v -> v * d.modelTrafo.Forward)

        let caps =
            Sg.draw IndexedGeometryMode.TriangleList
            |> Sg.vertexAttribute DefaultSemantic.Positions (data |> AVal.map (fun d -> d.capPositions))
            |> Sg.vertexAttribute DefaultSemantic.Colors    (data |> AVal.map (fun d -> d.capColors))
            |> Sg.vertexAttribute (Sym.ofString "ObjId")    (data |> AVal.map (fun d -> d.capIds))
            |> Sg.uniform "MV" mv
            |> Sg.uniform "DepthOffset"
                (depthOffset |> AVal.map (fun d -> (d * fillDepthOffsetFactor) / (100.0 - 0.1)))

        // one buffer for all prisms, drawn as one range per fill (packedFillRender)
        let volPositions = data |> AVal.map (fun d -> d.volPositions)
        let volColors    = data |> AVal.map (fun d -> d.volColors)
        let withVolumes (sg : ISg) =
            sg
            |> Sg.vertexAttribute DefaultSemantic.Positions volPositions
            |> Sg.vertexAttribute DefaultSemantic.Colors    volColors
            |> Sg.uniform "MV" mv

        let ranges =
            data |> AVal.map (fun d ->
                d.volRanges |> Array.map (fun (struct(first, count)) ->
                    DrawCallInfo(FaceVertexCount = count, FirstIndex = first, InstanceCount = 1)))

        { caps = caps; withVolumes = withVolumes; ranges = ranges }

    /// Packs the filled interiors of every annotation with showFill. Object id i is position i
    /// in `ordered`, the array linesNoIndirect indexes, so fill and outline picks agree.
    let fills
        (cbc : AdaptiveColorByCategoryModel) (planet : aval<Planet>) (reach : aval<FillReach>)
        (depthOffset : aval<float>) (ordered : aval<(Guid * AdaptiveAnnotation)[]>) (view : aval<M44d>) =
        let entries = ordered |> AVal.map (Array.map (fun (_, a) -> a, identityTrafo))
        fillGeometry cbc planet reach depthOffset entries view

    /// As fills, for the legacy branch: each annotation under its own (SPICE) trafo, still in
    /// one draw. Object ids are not meaningful here - that branch has no pick target.
    let fillsWithTrafos
        (cbc : AdaptiveColorByCategoryModel) (planet : aval<Planet>) (reach : aval<FillReach>)
        (depthOffset : aval<float>) (annos : aset<AdaptiveAnnotation * aval<Trafo3d>>) (view : aval<M44d>) =
        let entries = annos.Content |> AVal.map HashSet.toArray
        fillGeometry cbc planet reach depthOffset entries view

    /// The fills run ahead of the main pass, so outlines, points and everything else
    /// annotation-related draw over them; the terrain is already in the depth buffer from the
    /// preceding render command. One pass for all fills - their order comes from the ordered
    /// render command in packedFillRender, not from passes (which are expensive).
    let fillPass = RenderPass.before "annotation-fills" RenderPassOrder.Arbitrary RenderPass.main

    /// Fills per batch: one stencil bit each (Depth24Stencil8).
    [<Literal>]
    let FillsPerBatch = 8

    // Stencil draw of the fill in slot j: flip bit j wherever a prism face lies behind the
    // terrain (z-fail parity). A terrain point inside a closed prism has an odd number of its
    // faces behind it, one outside an even number - independent of face orientation, and right
    // with the camera inside the prism too.
    let private markParity =
        { StencilMode.None with
            DepthFail   = StencilOperation.Invert
            CompareMask = StencilMask 0xff }

    // Colour draw of the fill in slot j: colour where bit j is set and clear it on the way, so
    // every pixel is coloured once per fill. The other bits - other fills of the batch - are
    // left alone.
    let private colourBit (j : int) =
        { StencilMode.None with
            Comparison  = ComparisonFunction.NotEqual
            Reference   = 0
            CompareMask = StencilMask (1 <<< j)
            Pass        = StencilOperation.Zero
            Fail        = StencilOperation.Keep
            DepthFail   = StencilOperation.Keep }

    /// Visible fills: per fill, mark the terrain inside its prism in the stencil, then colour
    /// exactly those pixels. Lies on the terrain at every level of detail, with no depth bias -
    /// the fix for the z-fighting of #841.
    ///
    /// Why not all at once: the colour draw paints a pixel from the prism faces covering it on
    /// screen, and only the stencil knows which prism contains the terrain there. With one
    /// shared mark, a near prism's wall paints the terrain of another fill behind it. So each
    /// fill gets a stencil bit of its own: eight fills per batch, marked together, then
    /// coloured together - and the batches run strictly one after the other, as an ordered
    /// render command inside one pass:
    ///
    ///     [ stencil batch 0; colour batch 0; stencil batch 1; colour batch 1; ... ]
    ///
    /// Every fill is a range of one shared vertex buffer (an adaptive indirect draw), and a
    /// batch always has all eight slots - unused ones draw nothing - so editing a fill only
    /// changes buffer contents and ranges; render objects come and go only when the number of
    /// filled annotations crosses a multiple of eight.
    let packedFillRenderBatched (fillsPerBatch : int) (fills : FillGeometry) =
        let empty = DrawCallInfo(FaceVertexCount = 0, InstanceCount = 1)

        // slot i of the fill order: fill i's range, or nothing
        let slot (i : int) =
            fills.ranges |> AVal.map (fun r ->
                IndirectBuffer.ofArray [| if i < r.Length then r.[i] else empty |])

        let batch (b : int) =
            let draws (state : int -> ISg -> ISg) =
                Sg.ofList [
                    for j in 0 .. fillsPerBatch - 1 do
                        Sg.indirectDraw IndexedGeometryMode.TriangleList (slot (b * fillsPerBatch + j))
                        |> state j
                ]

            let stencil =
                draws (fun j sg ->
                    sg
                    |> Sg.stencilMode' markParity
                    |> Sg.stencilWriteMask' (StencilMask (1 <<< j)))
                // LessOrEqual, not Less: a back face beyond the far plane is clamped to depth 1,
                // which equals a cleared (empty) pixel - and must not count as behind terrain
                |> Sg.depthTest (AVal.constant DepthTest.LessOrEqual)
                |> Sg.writeBuffers' (Set.ofList [WriteBuffer.Stencil])

            let colour =
                draws (fun j sg ->
                    sg
                    |> Sg.stencilMode' (colourBit j)
                    |> Sg.stencilWriteMask' (StencilMask (1 <<< j)))
                // the stencil already says "terrain inside the prism"; testing depth again would
                // drop pixels whose nearest prism face lies behind the terrain
                |> Sg.depthTest (AVal.constant DepthTest.None)
                |> Sg.blendMode (AVal.constant BlendMode.Blend)
                |> Sg.writeBuffers' (Set.ofList [WriteBuffer.Color DefaultSemantic.Colors; WriteBuffer.Stencil])

            stencil, colour

        // built once per batch index and kept: the batch count only grows and shrinks at its
        // end, and an existing batch's scene graph never changes
        let batches = Dictionary<int, ISg * ISg>()
        let batchAt (b : int) =
            match batches.TryGetValue b with
            | true, sgs -> sgs
            | _ ->
                let sgs = batch b
                batches.[b] <- sgs
                sgs

        let commands =
            fills.ranges
            |> AVal.map (fun r -> (r.Length + fillsPerBatch - 1) / fillsPerBatch)
            |> AVal.map (fun batchCount ->
                IndexList.ofList [
                    for b in 0 .. batchCount - 1 do
                        let stencil, colour = batchAt b
                        yield stencil
                        yield colour
                ])
            |> AList.ofAVal

        Sg.execute (RenderCommand.Ordered commands)
        |> fills.withVolumes
        |> Sg.shader {
            do! FillShader.fillVertex
        }
        // both faces of every prism, and prism faces beyond the far (or before the near)
        // plane clamped instead of clipped - a clipped face would break the parity
        |> Sg.cullMode (AVal.constant CullMode.None)
        |> Sg.depthClamp' true
        |> Sg.pass fillPass

    /// packedFillRenderBatched with all eight stencil bits in use.
    let packedFillRender (fills : FillGeometry) = packedFillRenderBatched FillsPerBatch fills

    /// Fills against the terrain depth as a texture (#841, "B2"): every prism in ONE draw,
    /// independent of how many fills there are.
    ///
    /// Each prism face fragment that lies *behind* the terrain adds its fill's premultiplied
    /// colour into a float target - back faces with +1, front faces with -1. That is the z-fail
    /// count again, but per fill and in colour: a terrain point inside a prism collects +1 x its
    /// colour, one outside it collects nothing (its faces behind the point cancel), and prisms
    /// entirely in front of the terrain contribute nothing at all - which is what the shared
    /// stencil could not express, and why a near prism no longer paints a far fill's terrain.
    /// The sums are order-independent, so all fills go into one draw; a fullscreen composite
    /// then lays them over the terrain before the outlines.
    ///
    /// Where translucent fills overlap on the terrain they blend as a weighted average with
    /// alpha min(1, sum of alphas), not as true "over" - order-independent by construction.
    module DeferredFills =

        module Shader =
            open FShade

            let private terrainDepth =
                sampler2dMS {
                    texture uniform?TerrainDepth
                    filter Filter.MinMagPoint
                }

            let private accumulation =
                sampler2d {
                    texture uniform?FillAccumulation
                    filter Filter.MinMagPoint
                }

            type AccumVertex =
                {
                    [<Position>]    pos   : V4f
                    [<Color>]       c     : V4f
                    [<FragCoord>]   fc    : V4f
                    [<FrontFacing>] front : bool
                }

            /// Signed, premultiplied contribution of a prism face lying behind the terrain.
            /// Depth clamp keeps faces beyond the far plane (and before the near plane) from
            /// being clipped, but gl_FragCoord.z arrives unclamped - clamped here, so such a
            /// face sits at depth 1 (0), exactly where the depth range ends.
            let accumulate (v : AccumVertex) =
                fragment {
                    let terrain = terrainDepth.Read(V2i v.fc.XY, 0).X
                    // no surface at this pixel: nothing to fill
                    if terrain >= 1.0f then discard()
                    let z = clamp 0.0f 1.0f v.fc.Z
                    if z <= terrain then discard()
                    let w = if v.front then -1.0f else 1.0f
                    return V4f(v.c.XYZ * v.c.W * w, v.c.W * w)
                }

            type CompositeVertex =
                {
                    [<Position>]  pos : V4f
                    [<FragCoord>] fc  : V4f
                    [<Color>]     c   : V4f
                }

            /// The sums back to straight-alpha colour. abs: which face counts +1 depends on the
            /// winding convention, but it is the same for every prism, so all inside sums share
            /// one sign.
            let composite (v : CompositeVertex) =
                fragment {
                    let s = accumulation.Read(V2i v.fc.XY, 0)
                    let a = abs s.W
                    if a < 0.0001f then discard()
                    let alpha = min a 1.0f
                    return V4f(abs s.XYZ / a, alpha)
                }

        let private additive =
            BlendMode.simple BlendFactor.One BlendFactor.One

        /// The accumulation: all prisms of `fills`, one draw, into an RGBA32F texture of `size`,
        /// tested against `terrainDepth` (multisampled, the depth of the surfaces as rendered
        /// for this view; sample 0 is read). `viewTrafo`/`projTrafo` must be the ones the
        /// surfaces were rendered with.
        let accumulationTexture
            (runtime      : IRuntime)
            (fills        : FillGeometry)
            (terrainDepth : aval<ITexture>)
            (size         : aval<V2i>)
            (viewTrafo    : aval<Trafo3d>)
            (projTrafo    : aval<Trafo3d>) : aval<ITexture> =

            let signature = runtime.CreateFramebufferSignature [ DefaultSemantic.Colors, TextureFormat.Rgba32f ]

            let sg =
                Sg.draw IndexedGeometryMode.TriangleList
                |> fills.withVolumes
                |> Sg.shader {
                    do! FillShader.fillVertex
                    do! Shader.accumulate
                }
                |> Sg.texture "TerrainDepth" terrainDepth
                |> Sg.cullMode (AVal.constant CullMode.None)
                |> Sg.depthClamp' true
                |> Sg.depthTest (AVal.constant DepthTest.None)
                |> Sg.blendMode (AVal.constant additive)
                |> Sg.viewTrafo viewTrafo
                |> Sg.projTrafo projTrafo

            runtime.CompileRender(signature, sg)
            |> RenderTask.renderToColorWithClear size (clear { color C4f.Zero })
            |> AdaptiveResource.cast<ITexture>

        /// The fills laid over the terrain: a fullscreen pass in the fill pass, so outlines
        /// and everything else annotation-related draw over it.
        let composite (accumulation : aval<ITexture>) : ISg =
            Sg.fullScreenQuad
            |> Sg.texture "FillAccumulation" accumulation
            |> Sg.shader {
                do! DefaultSurfaces.trafo
                do! Shader.composite
            }
            |> Sg.viewTrafo' Trafo3d.Identity
            |> Sg.projTrafo' Trafo3d.Identity
            |> Sg.depthTest (AVal.constant DepthTest.None)
            |> Sg.blendMode (AVal.constant BlendMode.Blend)
            |> Sg.writeBuffers' (Set.ofList [WriteBuffer.Color DefaultSemantic.Colors])
            |> Sg.pass fillPass

        /// accumulationTexture and composite together: the visible fills of `fills`.
        let render runtime fills terrainDepth size viewTrafo projTrafo =
            accumulationTexture runtime fills terrainDepth size viewTrafo projTrafo
            |> composite

    /// Draggable control-point handles for one annotation - the one being edited.
    ///
    /// The object id written to alpha indexes the same `ordered` array lines and fills use, so a
    /// pick resolves to a Guid through the identical path. The control point index goes to red,
    /// where `Picking.pickId` writes -1; red >= 0 is therefore what distinguishes "clicked a
    /// handle" from "clicked the annotation".
    ///
    /// Emits `anno.points` - the control points the user clicked - and deliberately not
    /// `Drawing.Sg.getPolylinePoints`, which returns the terrain-sampled polyline and would put a
    /// handle on every sample.
    ///
    /// Returns bare geometry so the visible and pick passes can attach different shaders.
    let vertexHandles
        (depthOffset : aval<float>)
        (selected    : aval<Option<Guid>>)
        (ordered     : aval<(Guid * AdaptiveAnnotation)[]>)
        (view        : aval<M44d>) =

        let data =
            AVal.custom (fun t ->
                let annos    = ordered.GetValue(t)
                let selected = selected.GetValue(t)

                let positions = List<V3f>()
                let subIds    = List<int>()
                let objIds    = List<int>()
                let sizes     = List<float32>()
                let colors    = List<C4f>()
                let corners   = List<V2f>()
                let mutable pivotTrafo = None

                // the object id *is* the index into the shared ordering
                let index =
                    match selected with
                    | None -> None
                    | Some sel -> annos |> Array.tryFindIndex (fun (id, _) -> id = sel)

                match index with
                | None -> ()
                | Some oid ->
                    let (_, anno) = annos.[oid]
                    let visible   = anno.visible.GetValue(t)
                    let geometry  = anno.geometry.GetValue(t)

                    if visible && Geometry.isVertexEditable geometry then
                        let pivot = anno.modelTrafo.GetValue(t)
                        pivotTrafo <- Some pivot

                        let thickness = anno.thickness.value.GetValue(t)
                        let size = float32 thickness + handleSizeBonus

                        // two triangles per control point, all six vertices at the same world
                        // position and told apart only by their corner offset, which the vertex
                        // shader turns into a screen-space quad
                        let quad =
                            [| V2f(-1.0f, -1.0f); V2f(1.0f, -1.0f); V2f(1.0f, 1.0f)
                               V2f(-1.0f, -1.0f); V2f(1.0f, 1.0f); V2f(-1.0f, 1.0f) |]

                        let mutable i = 0
                        for p in anno.points.Content.GetValue(t) do
                            let local = pivot.Backward.TransformPos p |> V3f
                            for c in quad do
                                positions.Add local
                                corners.Add c
                                subIds.Add i
                                objIds.Add oid
                                sizes.Add size
                                // the shader chooses the visible colour; supplied because every
                                // packed draw does and HandleVertex declares Colors as an input
                                colors.Add(C4f(1.0f, 1.0f, 1.0f, 1.0f))
                            i <- i + 1

                {| points     = positions.ToArray()
                   corners    = corners.ToArray()
                   subIds     = subIds.ToArray()
                   objIds     = objIds.ToArray()
                   sizes      = sizes.ToArray()
                   colors     = colors.ToArray()
                   modelTrafo = Option.defaultValue Trafo3d.Identity pivotTrafo |})

        let mv = (data, view) ||> AVal.map2 (fun d v -> v * d.modelTrafo.Forward)

        Sg.draw IndexedGeometryMode.TriangleList
        |> Sg.vertexAttribute DefaultSemantic.Positions   (data |> AVal.map (fun d -> d.points))
        |> Sg.vertexAttribute (Sym.ofString "HandleCorner") (data |> AVal.map (fun d -> d.corners))
        |> Sg.vertexAttribute (Sym.ofString "ObjId")      (data |> AVal.map (fun d -> d.objIds))
        |> Sg.vertexAttribute (Sym.ofString "SubId")      (data |> AVal.map (fun d -> d.subIds))
        |> Sg.vertexAttribute (Sym.ofString "Sizes")      (data |> AVal.map (fun d -> d.sizes))
        |> Sg.vertexAttribute DefaultSemantic.Colors      (data |> AVal.map (fun d -> d.colors))
        |> Sg.uniform "MV" mv

    /// Visible pass for the vertex handles. `hovered` and `grabbed` are control point indices, or
    /// -1; the shader compares them per vertex the way SelectedId works for lines.
    let packedVertexHandleRender (hovered : aval<int>) (grabbed : aval<int>) (viewport : aval<V2i>) handles =
        handles
        |> Sg.uniform "HoveredVertex" hovered
        |> Sg.uniform "GrabbedVertex" grabbed
        |> Sg.uniform "HandleViewport" (viewport |> AVal.map (fun (v : V2i) -> V2d(float v.X, float v.Y)))
        |> Sg.shader {
            do! VertexHandleShader.handleVertex
            do! VertexHandleShader.handleFragment
        }
        // Always visible while the annotation is being edited, and never occluded by its own
        // outline. The pick pass makes the same choice, so what you see is what you can grab.
        |> Sg.depthTest (AVal.constant DepthTest.None)
        |> Sg.writeBuffers' (Set.ofList [WriteBuffer.Color DefaultSemantic.Colors])

    /// Point sprites as bare geometry, positions `view`-transformed on the CPU. `points` adds the
    /// viewer's shader; the map projection view (#772) adds its own.
    let pointsGeometry (cbc : AdaptiveColorByCategoryModel) (selected : aset<Guid>) (annoSet: aset<Guid * AdaptiveAnnotation>) (depthOffset : aval<float>) (view : aval<M44d>) =
        let instanceAttribs =
            AVal.custom (fun t ->
                Log.startTimed "creating points"
                let annos       = annoSet.Content.GetValue(t)
                let selected    = selected.Content.GetValue(t)
                let modelPos    = List<V3d>()
                let colors      = List<C4b>()
                let sizes       = List<float32>()
                let cbcSettings =
                    if cbc.enabled.GetValue t then Some (ColorByCategory.readSettings cbc t) else None

                for (id,anno) in annos do
                    let kind = anno.geometry.GetValue t
                    let isVisible = anno.visible.GetValue(t)
                    if isVisible then
                        let isSelected = HashSet.exists (fun (x : Guid) -> x = id) selected
                        let baseColor =
                            match cbcSettings with
                            | Some s -> ColorByCategory.resolve s anno t
                            | None   -> anno.color.c.GetValue(t)
                        let color = if isSelected then C4b.VRVisGreen else baseColor
                        // Surface-attribute pointwise / both: a colored dot at every clicked
                        // control point, for any geometry (polylines/polygons otherwise show
                        // no dots). resolvePoints gives one color per control point.
                        let surfacePointwise =
                            match cbcSettings with
                            | Some s ->
                                s.attributeKind = ColorAttributeKind.SurfaceAttribute
                                && s.surfaceColoring <> SurfaceColoringMode.Annotation
                            | None -> false
                        match cbcSettings with
                        | Some s when surfacePointwise ->
                            let cps  = anno.points.Content.GetValue(t) |> IndexList.toArray
                            let cols = ColorByCategory.resolvePoints s anno t cps.Length
                            let size = anno.thickness.value.GetValue(t) + 0.5
                            for i in 0 .. cps.Length - 1 do
                                modelPos.Add(cps.[i])
                                colors.Add(if isSelected then C4b.VRVisGreen else cols.[i])
                                sizes.Add(float32 size)
                        | _ ->
                            match kind with
                            | Geometry.Point ->
                                let px   = PRo3D.Core.Drawing.Sg.getPolylinePointsAt anno t
                                let size = anno.thickness.value.GetValue(t) + 0.5

                                modelPos.Add(px.[0])
                                colors.Add(color)
                                sizes.Add(float32 size)
                            | Geometry.DnS ->
                                if isSelected then
                                    let px   = PRo3D.Core.Drawing.Sg.getPolylinePointsAt anno t
                                    let size = anno.thickness.value.GetValue(t) + 0.5

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
        // scaled like the lines and fills, now that the dots bias their depth the same way
        |> Sg.uniform "DepthOffset" (depthOffset |> AVal.map (fun depthWorld -> depthWorld / (100.0 - 0.1)))

    let points (cbc : AdaptiveColorByCategoryModel) (selected : aset<Guid>) (annoSet: aset<Guid * AdaptiveAnnotation>) (depthOffset : aval<float>) (view : aval<M44d>) =
        pointsGeometry cbc selected annoSet depthOffset view
        |> Sg.shader {
              do! PointsShader.pointSpriteVertex
              do! PointsShader.pointSpriteFragment
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
                    let ps = PRo3D.Core.Drawing.Sg.getPolylinePointsAt anno t
                    let dipAngle = s.dipAngle.GetValue(t)
                    let _ = fcm.Current.GetValue(t)
                    let r = PRo3D.FalseColorLegendApp.Draw.getColorDnS fcm s.dipAngle
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
                (attributes,view) 
                ||> AVal.map2 (fun d view -> 
                    let viewMatrix = (CameraView.viewTrafo view)
                    let forward = Array.zeroCreate d.discTrafos.Length
                    let backward = Array.zeroCreate d.discTrafos.Length

                    d.discTrafos 
                    |> Array.iteri (fun i (modelMatrix : Trafo3d) -> 
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
                    
                    let forward  = Array.zeroCreate d.coneTrafos.Length
                    let backward = Array.zeroCreate d.coneTrafos.Length

                    d.coneTrafos 
                    |> Array.iteri (fun i (modelMatrix : Trafo3d) -> 
                        let mv =  modelMatrix * viewMatrix
                        forward.[i]  <- M44f mv.Forward
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