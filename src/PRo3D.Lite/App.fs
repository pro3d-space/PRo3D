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


module App = 

    let initialCamera = {
            FreeFlyController.initial with
                view = CameraView.lookAt (V3d.III * 3.0) V3d.OOO V3d.OOI
        }

    let update (model : Model) (msg : Message) =
        match msg with
        | ToggleBackground ->
            { model with background = (if model.background = C4b.Black then C4b.White else C4b.Black) }
        | Camera m ->
            { model with cameraState = FreeFlyController.update model.cameraState m }
        | CenterScene ->
            { model with cameraState = initialCamera }


    let renderToScene (cam : aval<Camera>) (createSg : IFramebufferSignature -> ISg)= 
           let scene = 
               Scene.custom (fun (values : ClientValues) -> 

                   let cam = 
                       (values.size, cam) 
                       ||> AVal.map2 (fun size camera -> Camera.create camera.cameraView (RenderControlConfig.fillHeight.adjustAspect size camera.frustum))
                

                   let signature =
                       values.runtime.CreateFramebufferSignature(
                           1,
                           Map.ofList [
                               DefaultSemantic.Colors, RenderbufferFormat.Rgb8
                               DefaultSemantic.Depth, RenderbufferFormat.Depth24Stencil8
                           ]
                       )
                   let mutable task = RenderTask.empty
                   let mutable results : option<IAdaptiveResource<IFramebuffer>> = None

                   let fbo = values.runtime.CreateFramebuffer(signature, values.size)
                   let sg = createSg signature

                   let color =
                       { new AdaptiveResource<ITexture>() with
                           override x.Create() =
                               task <-
                                   RenderTask.ofList [
                                       values.runtime.CompileClear(
                                           signature, 
                                           AVal.constant (Map.ofList [DefaultSemantic.Colors, C4f.White; ]), 
                                           AVal.constant (Some 1.0), AVal.constant None
                                       )
                                       sg |> Sg.camera cam |> Sg.compile values.runtime signature
                                   ]
                               let res = task |> RenderTask.renderTo fbo
                               res.Acquire()
                               results <- Some res

                           override x.Destroy() =
                               match results with
                               | Some res -> res.Release()
                               | None -> ()
                               task.Dispose()
                               task <- RenderTask.empty


                           override x.Compute(t, rt) =
                               match results with
                               | Some results ->
                                   let fbo = results.GetValue(t, rt)
                                              
                                   let getTexture (fbo : IFramebufferOutput) =
                                       match fbo with
                                       | :? ITextureLevel as t -> t.Texture
                                       | _ -> failwithf "not a texture: %A" fbo

                                   let color = fbo.Attachments.[DefaultSemantic.Colors] |> getTexture


                                   color :> ITexture
                               | None ->
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

    let viewScene (runner : Load.Runner) (model : AdaptiveModel) =
        let renderControl = 

            let renderControlAttributes = 
                AttributeMap.ofListCond [
                    always <| style "width: 100%; grid-row: 2; height:100%";
                    always <| attribute "showFPS" "true";         
                    "style", model.background |> AVal.map(fun c -> sprintf "background: #%02X%02X%02X" c.R c.G c.B |> AttributeValue.String |> Some)
                    //attribute "showLoader" "false"    // optional, default is true
                    always <| attribute "data-samples" "4"   
                ]

            let attributes = 
                AttributeMap.unionMany [
                    renderControlAttributes
                    FreeFlyController.extractAttributes model.cameraState Camera |> AttributeMap.ofAMap
                ]
                
            let frustum = Frustum.perspective 60.0 0.1 1000.0 1.0 |> AVal.constant
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
                                do! DefaultSurfaces.stableTrafo
                                do! DefaultSurfaces.diffuseTexture
                            }
                        )
                        |> Sg.set
                    )
                    |> Sg.set
                surfaceSg 
                

            let scene = renderToScene camera createSgs

            //DomNode.RenderControl(attributes, camera, (fun (c : ClientValues) -> createSgs c.signature |> Sg.noEvents), RenderControlConfig.standard, None )
            DomNode.RenderControl(attributes, camera, scene, None)
            

        renderControl


   

    let view (runner : Load.Runner) (model : AdaptiveModel) =

        let renderControl = viewScene runner model


        div [style "display: grid; grid-template-rows: 40px 1fr; width: 100%; height: 100%" ] [
            div [style "grid-row: 1"] [
                text "Hello 3D"
                br []
                button [onClick (fun _ -> CenterScene)] [text "Center Scene"]
                button [onClick (fun _ -> ToggleBackground)] [text "Change Background"]
            ]
            renderControl
            br []
            text "use first person shooter WASD + mouse controls to control the 3d scene"
        ]

    let threads (model : Model) =
        FreeFlyController.threads model.cameraState |> ThreadPool.map Camera


    let app (runtime : IRuntime) =

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

        {
            unpersist = Unpersist.instance
            threads = threads
            initial =
                {
                   cameraState =  { initialCamera with view = cameraView; freeFlyConfig = cfg }
                   background = C4b.Gray
                   state = state
                }
            update = update
            view = view runner
        } 