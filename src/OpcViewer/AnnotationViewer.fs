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
open Aardvark.Rendering
open System

open Adaptify.FSharp.Core

open PRo3D.Core.PackedRendering
open PRo3D.Base


module AnnotationViewer = 

    
    let createAnnotationSg (win : IRenderWindow) (view : aval<CameraView>) (frustum : aval<Frustum>) (showOld : aval<bool>) (annotations : Annotations) =
        let model = AdaptiveGroupsModel.Create annotations.annotations
        let runtime = win.Runtime

        let annoSet = 
            model.flat 
            |> AMap.choose (fun _ y -> y |> PRo3D.Core.Drawing.DrawingApp.tryToAnnotation) // TODO v5: here we collapsed some avals - check correctness
            |> AMap.toASet

        let config : Sg.innerViewConfig =
            {
                nearPlane        = AVal.constant 0.1
                hfov             = AVal.constant 60.0         
                arrowThickness   = AVal.constant 2.0
                arrowLength      = AVal.constant 2.0
                dnsPlaneSize     = AVal.constant 1.0
                offset           = AVal.constant 2.4
                pickingTolerance = AVal.constant 0.01
            }


        //let selectedAnnotation = cval -1
        let hoveredAnnotation = cval -1
        let picked = cval None


        let mv = view |> AVal.map (fun c -> (CameraView.viewTrafo c).Forward)
        let points = points (model.selectedLeaves |> ASet.map (fun x -> x.id)) annoSet config.offset mv
        let lines, pickIds, bb = PackedRendering.linesNoIndirect config.offset hoveredAnnotation ASet.empty annoSet mv

        let pickColors, pickDepth = 
            //let size = AVal.constant (V2i(128,128))
            let size = win.Sizes
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
            |> Sg.uniform "PickingTolerance" config.pickingTolerance
            |> Sg.compile runtime signature
            |> RenderTask.renderToColorAndDepth size

        if true then
            win.Mouse.Move.Values.Add(fun (o,p) -> 
                
                let r = pickColors.GetValue(AdaptiveToken.Top,RenderToken.Empty)
                let offset = V3i(p.Position.X,p.Position.Y,0)
                printfn "rt size: %A" r.Size
                if p.Position.X < r.Size.X - 1 && p.Position.Y < r.Size.Y - 1 then
                    let r = runtime.Download(r,0,0,Box2i.FromMinAndSize(p.Position, V2i(1,1))) |> unbox<PixImage<float32>>
                    let m = r.GetMatrix<C4f>()
                    //let center = m.Size.XY / 2L
                    //let ids = pickIds.GetValue()
                    //let mutable bestDist = Double.MaxValue
                    //let mutable bestId = -1
                    //m.ForeachCoord(fun (c : V2l) ->
                    //    let d = Vec.lengthSquared (c - center)
                    //    if d < bestDist then
                    //        let p = m.[c]
                    //        let id : int = BitConverter.SingleToInt32Bits(p.A)
                    //        if id > 0 && id < ids.Length then
                    //            bestDist <- d
                    //            bestId <- id
                    //)
                    //if bestId > 0 then
                    //    Log.line "hit %A" (id, ids.[bestId]
                    //    transact (fun _ -> selectedAnnotation.Value <- id)
                    let p = m.[0,0]
                    let id : int = BitConverter.SingleToInt32Bits(p.A)
                    let ids = pickIds.GetValue()
                    if id > 0 && id < ids.Length then
                        Log.line "hit %A" (id, ids.[id])
                        transact (fun _ -> hoveredAnnotation.Value <- id)
                    else 
                        transact (fun _ -> hoveredAnnotation.Value <- -1)
                    //r.SaveAsImage("guh.tiff")
                    ()
            )

            win.Mouse.Click.Values.Add(fun b -> 
                if b = MouseButtons.Right then
                    let hovered = hoveredAnnotation.GetValue()
                    let ids = pickIds.GetValue()
                    transact (fun _ -> 
                        if hovered > 0 && hovered < ids.Length then
                            picked.Value <- Some ids.[hovered]
                        else 
                            picked.Value <- None
                    )
            )

        let overlay = 
            Sg.fullScreenQuad
            |> Sg.scale 0.1
            |> Sg.translate -0.8 0.8 0.0
            |> Sg.diffuseTexture pickColors
            |> Sg.shader {
                do! DefaultSurfaces.trafo
                do! LensShader.lens
            }
            |> Sg.uniform "MousePosition" ((win.Mouse.Position, win.Sizes) ||> AVal.map2 (fun (p : PixelPosition) s -> printf "%A" p.NormalizedPosition; V2d(p.NormalizedPosition.X,1.0-p.NormalizedPosition.Y)))
            |> Sg.viewTrafo' Trafo3d.Identity
            |> Sg.projTrafo' Trafo3d.Identity


        let lineSg  =
            lines 
            |> Sg.shader { 
                  do! LineShader.noIndirectLineVertex
                  do! LineShader.thickLine
                  do! PRo3D.Base.Shader.DepthOffset.depthOffsetFS 
            }

        let onOff b (s : ISg) = 
            adaptive { 
                let! b = b
                if b then return s else return Sg.empty
            }

        let newSg =
            Sg.ofList [points; lineSg]
            |> onOff (AVal.map not showOld)

        let sg = 
            annoSet 
            |> ASet.map(fun (_,a) -> 
                let c = UI.mkColor model a
                let picked = UI.isSingleSelect model a
                let showPoints = 
                  a.geometry 
                    |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)
    
                let sg = Sg.finishedAnnotationOld a c config view showPoints picked (AVal.constant false)
                sg :> ISg
            )
            |> Sg.set 
            |> onOff showOld


    
        let fc = AdaptiveFalseColorsModel.Create PRo3D.Base.FalseColorsModel.initDnSLegend
        let dnsOld =  
            annoSet 
            |> ASet.map (fun (_,a) -> 
                Sg.finishedAnnotationDiscs a config fc view :> ISg
            )
            |> Sg.set
            |> onOff showOld


        let newDns = 
            fastDns config fc annoSet view |> onOff (AVal.map not showOld)

        Log.startTimed "[Drawing] creating finished annotation geometry"
        let selected =              
            annoSet 
            |> ASet.map(fun (g,a) -> 
                let c = UI.mkColor model a
                let picked = picked |> AVal.map (function | Some v when g = v -> true | _ -> false)
                let showPoints = 
                  a.geometry 
                    |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)
                
                let vm = view |> AVal.map (fun v -> (CameraView.viewTrafo v).Forward)
                let points = PRo3D.Core.Drawing.Sg.getPolylinePoints a   
                let width = a.thickness.value |> AVal.map (fun x -> x + 6.0) // 3.0
                let spheres = PRo3D.Base.Sg.drawSpheresFast vm points width (AVal.constant C4b.Blue)

                let sg = Sg.finishedAnnotation a c config view showPoints picked (AVal.constant false) :> ISg
                sg
                //spheres :> ISg
            )
            |> Sg.set               
        Log.stop()

        Sg.ofSeq [Sg.dynamic sg; Sg.dynamic newSg; Sg.dynamic dnsOld; Sg.dynamic newDns; overlay; selected]

    let run (scene : OpcScene) (annotations : Annotations) =

        Aardvark.Init()

        use app = new OpenGlApplication()
        let win = app.CreateGameWindow(1)
        win.RenderAsFastAsPossible <- true
        win.VSync <- false
        let runtime = win.Runtime

        let runner = 
            match (win.Runtime :> obj) |> unbox<IRuntime> with
            | :? Aardvark.Rendering.GL.Runtime as r -> Some (r.CreateLoadRunner 1)
            | _ -> None

        let serializer = FsPickler.CreateBinarySerializer()

        let showSurface = false

        let sg = 
            if showSurface && runner.IsSome then
                let runner = runner.Value
                scene.patchHierarchies |> Seq.toList |> List.map (fun basePath -> 
                    let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                    let t = PatchLod.toRoseTree h.tree
                    Sg.patchLod win.FramebufferSignature runner basePath scene.lodDecider false false ViewerModality.XYZ true t
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
            |> Sg.andAlso (createAnnotationSg win view frustum showOld annotations)
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