namespace PRo3D.Lite

open System
open System.IO

open Aardvark.Service

open Aardvark.UI
open Aardvark.UI.Primitives

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering

open MBrace.FsPickler
open Aardvark.SceneGraph
open Aardvark.GeoSpatial.Opc
open Aardvark.SceneGraph.Opc

open PRo3D.Lite
open Aardvark.Rendering


module App = 

    let initialCamera = {
            FreeFlyController.initial with
                view = CameraView.lookAt (V3d.III * 3.0) V3d.OOO V3d.OOI
        }

    let update (model : Model) (msg : Message) =
        match msg with
        | ToggleBackground ->
            { model with background = (if model.background = C4b.Black then C4b.White else C4b.Black) }
        | FreeFlyMessage m ->
            let freeFly = FreeFlyController.update model.cameraState m
            { model with cameraState = freeFly; orbitState = OrbitState.ofFreeFly model.orbitState.radius freeFly }
        | OrbitMessage msg -> 
            let orbitState = OrbitController.update model.orbitState msg
            { model with orbitState = orbitState; cameraState = OrbitState.toFreeFly 3.2 orbitState }
        | CenterScene ->
            { model with cameraState = initialCamera }
        | SetOrbitCenter -> 
            match model.cursor with
            | None -> model
            | Some c -> 
                let orbitState = OrbitController.update model.orbitState (OrbitMessage.SetTargetCenter c)
                { model with orbitState = orbitState; cameraState = OrbitState.toFreeFly 3.2 orbitState }
        | SetCursor pos -> 
            { model with cursor = Some pos }
        | SetMousePos pos -> 
            { model with mousePos = Some pos }

    let renderToScene (cam : aval<Camera>) (createSg : IFramebufferSignature -> ISg)
                      (mousePos : aval<Option<V2i>>)
                      (emit : Message -> unit) = 
           let scene = 
               Scene.custom (fun (values : ClientValues) -> 

                   let cam = 
                       (values.size, cam) 
                       ||> AVal.map2 (fun size camera -> Camera.create camera.cameraView (RenderControlConfig.fillHeight.adjustAspect size camera.frustum))
                

                   let signature =
                       values.runtime.CreateFramebufferSignature(
                           1,
                           Map.ofList [
                               DefaultSemantic.Colors, RenderbufferFormat.Rgba8
                               DefaultSemantic.Depth, RenderbufferFormat.Depth32fStencil8
                           ]
                       )
                   let mutable task = RenderTask.empty
                   let mutable results : option<_> = None


                   //values.runtime.CreateTexture2D(TextureFormat.Rgba8, AVal.constant 1, values.size).Aq
                   //let attachments = 
                   //     [
                   //         DefaultSemantic.Colors, values.runtime.CreateTexture2D(TextureFormat.Rgba8, AVal.constant 1, values.size).GetOutputView()
                   //         DefaultSemantic.Depth, values.runtime.CreateTexture2D(TextureFormat.Depth24Stencil8, AVal.constant 1, values.size).[TextureAspect.Color, 0, *] :> IFramebufferOutput
                   //     ] |> Map.ofList
                        

                   //let fbo = values.runtime.CreateFramebuffer(signature, values.size)
                   let sg = createSg signature

                   let color =
                       { new AdaptiveResource<ITexture>() with
                           override x.Create() =
                               task <-
                                   RenderTask.ofList [
                                       values.runtime.CompileClear(
                                           signature, 
                                           AVal.constant (Map.ofList [DefaultSemantic.Colors, C4f.Black;]), 
                                           AVal.constant (Some 1.0), AVal.constant None
                                       )
                                       sg |> Sg.camera cam |> Sg.compile values.runtime signature
                                   ]
                               let color,depth = task |> RenderTask.renderToColorAndDepth values.size 
                               color.Acquire()
                               depth.Acquire()
                               results <- Some (color,depth)

                           override x.Destroy() =
                               match results with
                               | Some (color,depth) -> color.Release(); depth.Release()
                               | None -> ()
                               task.Dispose()
                               task <- RenderTask.empty


                           override x.Compute(t, rt) =
                               match results with
                               | Some (color, depth) ->
                                   let depth = depth.GetValue(t, rt)
                                   let color = color.GetValue(t, rt)
                                   let projTrafo = values.projTrafo.GetValue(t)
                                   let mousePos = mousePos.GetValue(t)
                                   let camera = cam.GetValue(t)
                                              
                                   let getTexture (fbo : IFramebufferOutput) =
                                       match fbo with
                                       | :? ITextureLevel as t -> t.Texture
                                       | _ -> failwithf "not a texture: %A" fbo

                                   match mousePos with
                                   | Some mousePos when mousePos.AllGreaterOrEqual(V2i.OO) && mousePos.AllSmaller color.Size.XY -> 
                                        
                                       //let m = depth.Download(0,0,Box2i.FromMinAndSize(mousePos, V2i.II)) 
                                       let m = depth.DownloadDepth(0,0, Box2i.FromMinAndSize(mousePos, V2i.II))
                                       let vp = camera.cameraView.ViewTrafo * (Frustum.projTrafo camera.frustum)
                                       let tc = V2d mousePos / V2d color.Size
                                       let depth = m.[0,0] |> float
                                       let ndc = V3d(2.0 * tc.X - 1.0, 1.0 - 2.0 * tc.Y, depth * 2.0 - 1.0)
                                       let wp = vp.Backward.TransformPosProj(ndc)
                                       emit (SetCursor wp)
                                   | _ -> ()

                                   color :> ITexture
                               | _ ->
                                   NullTexture() :> ITexture
                       }

                   let final = 
                       Sg.fullScreenQuad
                       |> Sg.diffuseTexture color
                       |> Sg.shader {
                           do! DefaultSurfaces.diffuseTexture
                       }

                   final |> Sg.compile values.runtime values.signature
               )
           scene

    module Shader = 
        open FShade

        open Aardvark.Rendering.Effects

        type UniformScope with
            member x.CursorViewSpace : V3d = uniform?CursorViewSpace

        type CursorVertex = 
            {
                [<Semantic("ViewPos2")>]
                viewPos : V3d

                [<Position>]
                pos : V4d

                [<Color>]
                c : V4d
            }

        let donutVertex (v : CursorVertex) = 
            vertex {
                let vp = uniform.ModelViewTrafo *  v.pos 
                return 
                    { v with 
                        viewPos = vp.XYZ
                    }
            }

        let donutFragment (v : CursorVertex) =
            fragment {
                
                let d = Vec.length (uniform.CursorViewSpace - v.viewPos)
                let r = Fun.Smoothstep(d, 5.0, 5.2) - Fun.Smoothstep(d, 5.8, 6.0)
                return r * V4d.IIII + v.c * (1.0 - r)
            }

    let viewScene (runner : Load.Runner) (emit : Message -> unit) (model : AdaptiveModel) =
        let renderControl = 

            let renderControlAttributes = 
                AttributeMap.ofListCond [
                    always <| style "width: 100%; grid-row: 2; height:100%";
                    always <| attribute "showFPS" "true";         
                    "style", model.background |> AVal.map(fun c -> sprintf "background: #%02X%02X%02X" c.R c.G c.B |> AttributeValue.String |> Some)
                    //attribute "showLoader" "false"    // optional, default is true
                    always <| attribute "data-samples" "4"   
                    always <| onMouseMove (fun p -> SetMousePos (V2i(p)))

                    onlyWhen (model.cameraMode |> AVal.map (function CameraMode.Orbit -> true | _ -> false )) (onMouseDoubleClick (fun _ -> SetOrbitCenter))
                ]

            let attributes' (model : AdaptiveOrbitState) (f : OrbitMessage -> 'msg) =
                let down = model.dragStart |> AVal.map Option.isSome
                AttributeMap.ofListCond [
                    always <| onCapturedPointerDown None (fun k b p -> MouseDown p |> f)
                    always <| onCapturedPointerUp None (fun k b p -> MouseUp p |> f)
                    always <| onEvent "onRendered" [] (fun _ -> Rendered |> f)
                    always <| onWheel (fun delta -> Wheel delta |> f)
                    onlyWhen down <| onCapturedPointerMove None (fun k p -> MouseMove p |> f)
                ]

            let cameraAttributes = 
                amap {
                    let! mode = model.cameraMode
                    match mode with
                    | CameraMode.Orbit -> 
                        yield! attributes' model.orbitState OrbitMessage |> AttributeMap.toAMap
                    | _ -> 
                        yield! FreeFlyController.extractAttributes model.cameraState FreeFlyMessage 
                }

            let attributes = 
                AttributeMap.unionMany [
                    renderControlAttributes
                    cameraAttributes |> AttributeMap.ofAMap
                    //FreeFlyController.extractAttributes model.cameraState Camera |> AttributeMap.ofAMap
                ]

            let cursorViewPos =
                (model.cursor, model.cameraState.view)
                ||> AVal.map2 (fun c v -> 
                    match c with
                    | None -> V3d(0.0,0.0,-10000.0)
                    | Some c -> v.ViewTrafo.Forward.TransformPos(c)
                )
                
            let frustum = Frustum.perspective 60.0 0.1 10000.0 1.0 |> AVal.constant
            let camera : aval<Camera> = (model.cameraState.view, frustum) ||> AVal.map2 (fun v p -> Camera.create v p)
            let createSgs (signature : IFramebufferSignature) = 
                let surfaceSg = 
                    model.state.surfaces 
                    |> AMap.toASet 
                    |> ASet.map (fun (surfaceName, surface) -> 
                        surface.opcs 
                        |> AMap.toASet 
                        |> ASet.map (fun (opcId, opc) -> 
                            PatchLod.toRoseTree opc.opc.tree
                            |> Sg.patchLod signature runner opcId DefaultMetrics.mars false true ViewerModality.XYZ true 
                            |> Sg.shader {
                                do! Shader.donutVertex
                                do! DefaultSurfaces.stableTrafo
                                do! DefaultSurfaces.diffuseTexture
                                do! Shader.donutFragment
                            }
                        )
                        |> Sg.set
                    )
                    |> Sg.set
                    |> Sg.uniform "CursorViewSpace" cursorViewPos
                    |> Sg.noEvents

                //let surfaceSg = 
                //    IndexedGeometryPrimitives.solidPhiThetaSphere Sphere3d.Unit 20 C4b.DarkRed
                //    |> Sg.ofIndexedGeometry
                //    |> Sg.shader {
                //        do! DefaultSurfaces.trafo
                //        do! DefaultSurfaces.simpleLighting
                //    }
                //    |> Sg.noEvents

                let afterMain = RenderPass.after "cursor" RenderPassOrder.Arbitrary RenderPass.main

                //let cursor = 
                //    Sg.sphere' 3 C4b.Green 0.1
                //    |> Sg.translation (model.cursor |> AVal.map (function None -> V3d.III * 10000.0 | Some p -> p + V3d(0.0,0.0,0.0)))
                //    |> Sg.onOff (model.cursor |> AVal.map Option.isSome)
                //    |> Sg.shader {
                //        do! DefaultSurfaces.stableTrafo
                //    }
                //    |> Sg.writeBuffers' (Set.singleton DefaultSemantic.Colors)
                //    |> Sg.pass afterMain
                //    |> Sg.noEvents
                Sg.ofSeq [surfaceSg]
   
                

            let scene = renderToScene camera createSgs model.mousePos emit

            //DomNode.RenderControl(attributes, camera, (fun (c : ClientValues) -> createSgs c.signature |> Sg.noEvents), RenderControlConfig.standard, None )
            DomNode.RenderControl(attributes, camera, scene, None)
            

        renderControl


    let dependencies = [
        { name = "style"; kind = Stylesheet; url = "./style.css"}
        { name = "semui-overrides"; kind = Stylesheet; url = "semui-overrides.css"}
    ]

    let view (runner : Load.Runner) (emit : Message -> unit) (model : AdaptiveModel) =

        let renderControl = viewScene runner emit model


        //div [style "display: grid; grid-template-rows: 40px 1fr; width: 100%; height: 100%" ] [
        //    div [style "grid-row: 1"] [
        //        text "Hello 3D"
        //        br []
        //        button [onClick (fun _ -> CenterScene)] [text "Center Scene"]
        //        button [onClick (fun _ -> ToggleBackground)] [text "Change Background"]
        //    ]
        //    renderControl
        //    br []
        //    text "use first person shooter WASD + mouse controls to control the 3d scene"
        //]

        let distanceToCamera = 
            (model.cameraState.view, model.cursor) 
            ||> AVal.map2 (fun (cam : CameraView) cursor -> 
                match cursor with 
                | Some c -> Vec.distance c cam.Location |> Some
                | _ -> None
            )

        let attribs = 
            amap {
                let! mousePos = model.mousePos
                yield clazz "disableSelection"
                match mousePos with
                | None -> yield style "display:none"
                | Some p -> 
                    let p = p + V2i(35,-20)
                    yield style (sprintf "position: absolute; left: %d; top: %d; pointer-events: none; font-family: consolas; color: white" p.X p.Y)
            } |> AttributeMap.ofAMap

        let cursorText = 
            distanceToCamera 
            |> AVal.map (fun d -> 
                match d with
                | Some d -> 
                    PRo3D.Base.Formatting.Len(d).ToString() 
                | None -> ""
            )

        require dependencies (
            div [] [
                renderControl
                Incremental.div attribs (AList.ofList [Incremental.text cursorText])
            ]
        )

    let threads (model : Model) =
        let freeFly = FreeFlyController.threads model.cameraState |> ThreadPool.map FreeFlyMessage
        let orbit = OrbitController.threads model.orbitState |> ThreadPool.map OrbitMessage
        ThreadPool.union freeFly orbit


    let app (runtime : IRuntime) (emit : Message -> unit) =

        let runner = runtime.CreateLoadRunner(2)

        let surface = @"D:\pro3d\VictoriaCrater\HiRISE_VictoriaCrater"
        let opcs = Directory.EnumerateDirectories(surface)
        let serializer = FsPickler.CreateBinarySerializer()

        let opcs = 
            opcs 
            |> Seq.toList |> List.map (fun basePath -> 
                basePath, { 
                    opc = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                }
            )
            |> HashMap.ofSeq

        let surface = { opcs = opcs; trafo = Trafo3d.Identity }

        let cameraView = PRo3DApi.Surface.centerView surface
        
        let speed = 5.0
        let cfg =
            { FreeFlyController.initial.freeFlyConfig with
                moveSensitivity = 0.35 + speed
                zoomMouseWheelSensitivity = 0.8 * (2.0 ** speed)
                panMouseSensitivity = 0.01 * (2.0 ** speed)
                dollyMouseSensitivity = 0.01 * (2.0 ** speed)
            }

        let state =
            {
                surfaces =  HashMap.ofList ["victoria crater", surface]
            }

        let freeFlyState = { initialCamera with view = cameraView; freeFlyConfig = cfg }
        let orbitState = { OrbitState.ofFreeFly 300.0 freeFlyState with sky = freeFlyState.view.Up }

        {
            unpersist = Unpersist.instance
            threads = threads
            initial =
                {
                   orbitState = orbitState
                   cameraState =  { initialCamera with view = cameraView; freeFlyConfig = cfg }
                   background = C4b.Gray
                   mousePos = None
                   cursor = None
                   state = state
                   cameraMode = FreeFly
                }
            update = update
            view = view runner emit
        } 