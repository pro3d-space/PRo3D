namespace Aardvark.Opc

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
open PRo3D.Core

open PRo3D.Core.Drawing
open System.Collections.Generic


module AnnotationViewer = 


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
                return { v with pointSize = v.size }
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
                [<SourceVertexIndex>]       i       : int
            }


        // since we need special extension feature not provided by fshade we simply import the functionality (standard approach)
        [<GLSLIntrinsic("gl_DrawIDARB",requiredExtensions=[|"GL_ARB_shader_draw_parameters"|])>]
        let drawId () : int = raise <| FShade.Imperative.FShadeOnlyInShaderCodeException "drawId"

        type UniformScope with
            member x.MVPs       : M44d[]  = x?StorageBuffer?MVPs
            member x.LineWidths : float[] = x?StorageBuffer?LineWidths
            member x.Colors     : V4d[]   = x?StorageBuffer?Colors

        let indirectLineVertex (v : ThickLineVertex) =
            vertex {
                let id = drawId()
                return 
                    { v with 
                        c = uniform.Colors.[id]; 
                        pos = uniform.MVPs.[id] * v.pos
                        w = uniform.LineWidths.[id]
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

    
    let createAnnotationSg (view : aval<CameraView>) (frustum : aval<Frustum>) (showOld : aval<bool>) (annotations : Annotations) =
        let model = AdaptiveGroupsModel.Create annotations.annotations

        let annoSet = 
            model.flat 
            |> AMap.choose (fun _ y -> y |> PRo3D.Core.Drawing.DrawingApp.tryToAnnotation) // TODO v5: here we collapsed some avals - check correctness
            |> AMap.toASet

        let config : Sg.innerViewConfig =
            {
                nearPlane        = AVal.constant 0.1
                hfov             = AVal.constant 60.0         
                arrowThickness   = AVal.constant 10.0
                arrowLength      = AVal.constant 10.0
                dnsPlaneSize     = AVal.constant 10.0
                offset           = AVal.constant 0.0
                pickingTolerance = AVal.constant 10.0
            }


        let lines (viewProj : aval<M44d>) =
              let instanceAttribs = 
                  AVal.custom (fun t -> 
                      Log.startTimed "mk lines"
                      let annos = annoSet.Content.GetValue(t)
                      let modelTrafos = List<M44d>()
                      let vertices = List<_>()
                      let colors = List<_>()
                      let tolerances = List<float32>()
                      let lineWidths = List<float32>()
                      let dcis = List<DrawCallInfo>()
                      let depthOffsets = List<float32>()
                      for (id,anno) in annos do   
                          let kind = anno.geometry.GetValue t
                          let p = PRo3D.Core.Drawing.Sg.getPolylinePoints anno
                          let ps = p.GetValue(t)
                          let offset = 0.0
                          let color = anno.color.c.GetValue(t)
                          let thickness = anno.thickness.value.GetValue(t)
                          let tolerance = 0.0
                          let modelTrafo = anno.modelTrafo.GetValue(t)
                          //let modelTrafo = Trafo3d.Identity

                          let start = vertices.Count
                          let lines = Array.pairwise ps

                          let isVisible = anno.visible.GetValue(t)

                          for (a,e) in lines do
                              vertices.Add(modelTrafo.Backward.TransformPos a |> V3f)
                              vertices.Add(modelTrafo.Backward.TransformPos e |> V3f)

                          let dci = DrawCallInfo(FaceVertexCount = lines.Length * 2, BaseVertex = start, FirstIndex = 0,
                                                 FirstInstance = 0, InstanceCount = 1)
       

                          dcis.Add(dci)
                          modelTrafos.Add(modelTrafo.Forward)
                          lineWidths.Add(float32 thickness)
                          colors.Add(C4f color)
                          tolerances.Add(float32 tolerance)
                          depthOffsets.Add(float32 offset)

                      let r = 
                          {| points = vertices.ToArray();
                             drawCallInfos = dcis.ToArray();
                             modelTrafos = modelTrafos.ToArray();
                             lineWidths = lineWidths.ToArray();
                             colors = colors.ToArray();
                             tolerances = tolerances.ToArray() 
                          |}
                      Log.stop()
                      r
                  )
              let mvps = 
                (instanceAttribs, viewProj) ||> AVal.map2 (fun i vp -> 
                    let r = Array.map (fun m -> let r : M44d = vp * m in M44f.op_Explicit r) i.modelTrafos
                    r
                )
              let indirect = instanceAttribs |> AVal.map (fun i -> IndirectBuffer.ofArray false i.drawCallInfos)
              Sg.indirectDraw IndexedGeometryMode.LineList indirect
              |> Sg.vertexAttribute DefaultSemantic.Positions (instanceAttribs |> AVal.map (fun i -> i.points))
              //|> Sg.index  (instanceAttribs |> AVal.map (fun i -> Array.init (i.points.Length * 4) id))
              |> Sg.uniform "MVPs" mvps
              |> Sg.uniform "LineWidths" (instanceAttribs |> AVal.map (fun i -> i.lineWidths))
              |> Sg.uniform "Colors" (instanceAttribs |> AVal.map (fun i -> i.colors))
              |> Sg.uniform "Tolerances" (instanceAttribs |> AVal.map (fun i -> i.tolerances))
              |> Sg.shader { 
                    do! LineShader.indirectLineVertex
                    do! LineShader.thickLine
                    //do! OpcViewer.Base.Shader.DepthOffset.depthOffsetFS 
              }
              |> Sg.uniform "DepthOffset" (AVal.constant 0.0)



        let points (viewProj : aval<M44d>) =
            let instanceAttribs = 
                AVal.custom (fun t -> 
                    Log.startTimed "creating points"
                    let annos = annoSet.Content.GetValue(t)
                    let modelPos = List<V3d>()
                    let colors = List<C4b>()
                    let sizes = List<float32>()
                    for (id,anno) in annos do   
                        let kind = anno.geometry.GetValue t
                        match kind with
                        | Geometry.Point ->
                            let p = PRo3D.Core.Drawing.Sg.getPolylinePoints anno
                            let c = anno.color.c
                            let size = anno.thickness.value |> AVal.map(fun x -> x + 0.5)
                            let px = p.GetValue(t)
                            modelPos.Add(px.[0])
                            colors.Add(c.GetValue(t))
                            sizes.Add(float32 <| size.GetValue(t))
                        | Geometry.DnS -> 
                            let p = PRo3D.Core.Drawing.Sg.getPolylinePoints anno
                            let c = anno.color.c.GetValue(t)
                            let size = anno.thickness.value |> AVal.map(fun x -> x + 0.5)
                            let size = size.GetValue(t)
                            let px = p.GetValue(t)
                            for p in px do 
                                modelPos.Add(p)
                                colors.Add(C4b.White)
                                sizes.Add(float32 size)
                        | _ -> ()

                    Log.stop()
                    modelPos.ToArray(), colors.ToArray(), sizes.ToArray()
                )
            let mvps = 
                (instanceAttribs, viewProj) ||> AVal.map2 (fun (p,_,_) v -> 
                    let r = Array.map (fun p -> V3f (v.TransformPosProj p)) p
                    r
                )
            let colors = instanceAttribs |> AVal.map (fun (mvp, c, s) -> c)
            let sizes = instanceAttribs |> AVal.map (fun (mvp, c, s) -> s )
            Sg.draw IndexedGeometryMode.PointList
            |> Sg.vertexAttribute DefaultSemantic.Positions mvps
            |> Sg.vertexAttribute DefaultSemantic.Colors colors
            |> Sg.vertexAttribute "Sizes" sizes
            |> Sg.shader { 
                  do! PointsShader.pointSpriteVertex
                  do! PointsShader.pointSpriteFragment
               }

        let mvp = (view,frustum) ||> AVal.map2 (fun c f -> (CameraView.viewTrafo c * Frustum.projTrafo f).Forward)
        let points = points mvp
        let lines = lines mvp

        let onOff b (s : ISg) = 
            adaptive { 
                let! b = b
                if b then return s else return Sg.empty
            }

        let newSg =
            Sg.ofList [points; lines]
            |> onOff (AVal.map not showOld)

        let sg = 
            annoSet 
            |> ASet.map(fun (_,a) -> 
                let c = UI.mkColor model a
                let picked = UI.isSingleSelect model a
                let showPoints = 
                  a.geometry 
                    |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)
    
                let sg = Sg.finishedAnnotation a c config view showPoints picked (AVal.constant false)
                sg :> ISg
            )
            |> Sg.set 
            |> onOff showOld

        Sg.ofSeq [Sg.dynamic sg; Sg.dynamic newSg]

    let run (scene : OpcScene) (annotations : Annotations) =

        Aardvark.Init()

        use app = new OpenGlApplication()
        let win = app.CreateGameWindow(1)
        win.RenderAsFastAsPossible <- true
        let runtime = win.Runtime

        let runner = 
            match win.Runtime :> IRuntime with
            | :? Aardvark.Rendering.GL.Runtime as r -> Some (r.CreateLoadRunner 1)
            | _ -> None

        let serializer = FsPickler.CreateBinarySerializer()

        let showSurface = true

        let sg = 
            if showSurface && runner.IsSome then
                let runner = runner.Value
                scene.patchHierarchies |> Seq.toList |> List.map (fun basePath -> 
                    let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                    let t = PatchLod.toRoseTree h.tree
                    Sg.patchLod win.FramebufferSignature runner basePath scene.lodDecider false false ViewerModality.XYZ t
                ) |> Sg.ofList 
            else 
                Sg.empty

        let speed = AVal.init scene.speed

        let bb = scene.boundingBox
        let initialView = CameraView.lookAt bb.Max bb.Center bb.Center.Normalized
        let view = initialView |> DefaultCameraController.controlWithSpeed speed win.Mouse win.Keyboard win.Time
        let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 scene.near scene.far (float s.X / float s.Y))

        let lodVisEnabled = cval true
        let fillMode = cval FillMode.Fill
        let showOld = cval false

        win.Keyboard.KeyDown(Keys.PageUp).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value * 1.5)
        )

        win.Keyboard.KeyDown(Keys.PageDown).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value / 1.5)
        )

        win.Keyboard.KeyDown(Keys.L).Values.Add(fun _ -> 
            transact (fun _ -> lodVisEnabled.Value <- not lodVisEnabled.Value)
        )

        win.Keyboard.KeyDown(Keys.L).Values.Add(fun _ -> 
            transact (fun _ -> lodVisEnabled.Value <- not lodVisEnabled.Value)
        )
        
        win.Keyboard.KeyDown(Keys.O).Values.Add(fun _ -> 
            transact (fun _ -> showOld.Value <- not showOld.Value)
        )

        win.Keyboard.KeyDown(Keys.F).Values.Add(fun _ -> 
            transact (fun _ -> 
                fillMode.Value <-
                    match fillMode.Value with
                    | FillMode.Fill -> FillMode.Line
                    | _-> FillMode.Fill
            )
        )




        let sg = 
            sg
            |> Sg.andAlso (createAnnotationSg view frustum showOld annotations)
            |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
            |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo)
            //|> Sg.transform preTransform
            |> Sg.effect [
                    //DefaultSurfaces.trafo |> toEffect
                    Shader.stableTrafo |> toEffect
                    DefaultSurfaces.constantColor C4f.White |> toEffect
                    DefaultSurfaces.diffuseTexture |> toEffect
                    Shader.LoDColor |> toEffect
                ]
            |> Sg.uniform "LodVisEnabled" lodVisEnabled
            |> Sg.fillMode fillMode


        Log.startTimed "rendering first frame"
        let fbo = runtime.CreateFramebuffer(win.FramebufferSignature, AVal.constant (V2i(1024,1024))).GetValue()
        let t = runtime.CompileRender(win.FramebufferSignature, sg)
        t.Run(RenderToken.Empty, fbo)
        Log.stop()

        win.RenderTask <- t
        win.Run()
        0