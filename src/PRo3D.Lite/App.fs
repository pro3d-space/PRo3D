namespace PRo3D.Lite

open System
open System.IO

open Aardvark.Service


open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering

open Aardvark.SceneGraph
open Aardvark.GeoSpatial.Opc
open Aardvark.SceneGraph.Opc

open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D.Lite
open Aardvark.Rendering
open PRo3D.Base


module App = 

    let setView  (cameraView : CameraView) (model : Model) = 
        let freeFly = { model.freeFlyState with view = cameraView }
        let orbitState = OrbitState.ofFreeFly model.orbitState.radius  freeFly 
        { model with orbitState = orbitState; freeFlyState = freeFly }

    let update (model : Model) (msg : Message) =
        match msg with
        | ToggleBackground ->
            { model with background = (if model.background = C4b.Black then C4b.White else C4b.Black) }
        | FreeFlyMessage m ->
            let freeFly = FreeFlyController.update model.freeFlyState m
            { model with freeFlyState = freeFly; orbitState = OrbitState.ofFreeFly model.orbitState.radius freeFly }
        | OrbitMessage msg -> 
            let orbitState = OrbitController.update model.orbitState msg
            { model with orbitState = orbitState; freeFlyState = OrbitState.toFreeFly 3.2 orbitState }
        | CenterScene ->
            match model.state.surfaces |> HashMap.toSeq |> Seq.map snd |> Seq.tryHead with
            | Some surface -> 
                let cameraView = Api.Surface.centerView model.state.planet surface
                setView cameraView model 
            | None -> 
                model

        | SetOrbitCenter -> 
            match model.cursor with
            | None -> model
            | Some c -> 
                let orbitState = OrbitController.update model.orbitState (OrbitMessage.SetTargetCenter c)
                { model with orbitState = orbitState; freeFlyState = OrbitState.toFreeFly 3.2 orbitState }
        | SetCursor pos -> 
            { model with cursor = Some pos }
        | SetMousePos pos -> 
            { model with mousePos = Some pos }

        | SetCameraMode mode -> 
            { model with cameraMode = mode }

    let renderToScene (background : aval<C4b>) (cam : aval<Camera>) (createSg : aval<Camera> -> aval<V2i> -> IFramebufferSignature -> ISg<_>)
                      (mousePos : aval<Option<V2i>>)
                      (emit : Message -> unit) = 
            let scene = 
                Scene.custom (fun (values : ClientValues) -> 
           
                    let cam = 
                        (values.size, cam) 
                        ||> AVal.map2 (fun size camera -> Camera.create camera.cameraView (RenderControlConfig.fillHeight.adjustAspect size camera.frustum))

                    let signature =
                        values.runtime.CreateFramebufferSignature(
                            Map.ofList [
                                DefaultSemantic.Colors, TextureFormat.Rgba8
                                DefaultSemantic.DepthStencil, TextureFormat.Depth32fStencil8
                            ],
                            samples = 8
                        )
                    let mutable task = RenderTask.empty
                    let mutable results : option<_> = None

                    let resolvedColor = values.runtime.CreateTexture2D(values.size, TextureFormat.Rgba8)

                    let sg = createSg cam values.size signature

                    let color =
                        { new AdaptiveResource<ITexture>() with
                            override x.Create() =
                                task <-
                                    RenderTask.ofList [
                                        values.runtime.CompileClear(
                                            signature,
                                            background,
                                            AVal.constant 1.0
                                        )
                                        sg |> Sg.noEvents |> Sg.camera cam |> Sg.compile values.runtime signature
                                    ]

                                let color,depth = task |> RenderTask.renderToColorAndDepth values.size 
                                color.Acquire()
                                depth.Acquire()
                                resolvedColor.Acquire()

                                results <- Some (color,depth)

                            override x.Destroy() =
                                resolvedColor.Release()
                                match results with
                                | Some (color,depth) -> color.Release(); depth.Release()
                                | None -> ()
                                task.Dispose()
                                task <- RenderTask.empty


                            override x.Compute(t, rt) =
                                match results with
                                | Some (color, depth) ->
                                    let resolvedColor = resolvedColor.GetValue(t)
                                    let depth = depth.GetValue(t, rt)
                                    let color = color.GetValue(t, rt)
                                    let projTrafo = values.projTrafo.GetValue(t)
                                    let mousePos = mousePos.GetValue(t)
                                    let camera = cam.GetValue(t)

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

                                    values.runtime.ResolveMultisamples(color.GetOutputView(), resolvedColor, ImageTrafo.Identity)
                                    resolvedColor :> ITexture
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
            member x.CursorWorldSizeSquared : V4d = uniform?CursorWorldSizeSquared

        type CursorVertex = 
            {
                [<Semantic("ViewPos")>]
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
                
                let d = Vec.lengthSquared (uniform.CursorViewSpace - v.viewPos)
                let r = Fun.Smoothstep(d, uniform.CursorWorldSizeSquared.X, uniform.CursorWorldSizeSquared.Y) - Fun.Smoothstep(d, uniform.CursorWorldSizeSquared.Z, uniform.CursorWorldSizeSquared.W)
                return r * V4d.IIII + v.c * (1.0 - r)
            }

    let viewScene (runner : Load.Runner) (emit : Message -> unit) (model : AdaptiveModel) =
        let renderControl = 

            let renderControlAttributes = 
                AttributeMap.ofListCond [
                    always <| style "width: 100%; grid-row: 2; height:100%"; 
                    "style", model.background |> AVal.map(fun c -> sprintf "background: #%02X%02X%02X" c.R c.G c.B |> AttributeValue.String |> Some)
                    always <| attribute "showLoader" "false"   
                    always <| attribute "data-samples" "1"   
                    always <| onCapturedPointerMove None (fun _ p -> SetMousePos (V2i(p)))

                    onlyWhen (model.cameraMode |> AVal.map (function CameraMode.Orbit -> true | _ -> false )) (onMouseDoubleClick (fun _ -> SetOrbitCenter))
                ]


            let cameraAttributes = 
                amap {
                    let! mode = model.cameraMode
                    match mode with
                    | CameraMode.Orbit -> 
                        yield! OrbitController.extractAttributes model.orbitState OrbitMessage 
                    | _ -> 
                        yield! FreeFlyController.extractAttributes model.freeFlyState FreeFlyMessage 
                }

            let attributes = 
                AttributeMap.unionMany [
                    renderControlAttributes
                    cameraAttributes |> AttributeMap.ofAMap
                ]

            let cursorViewPos =
                (model.cursor, model.freeFlyState.view)
                ||> AVal.map2 (fun c v -> 
                    match c with
                    | None -> V3d(0.0,0.0,-10000.0)
                    | Some c -> v.ViewTrafo.Forward.TransformPos(c)
                )

            let surfaceBBs = 
                let surfaces = model.state.surfaces |> AMap.toASet |> ASet.map snd
                surfaces 
                |> ASet.map (fun s -> 
                    let box = 
                        s.Current |> AVal.map (fun s -> 
                            let bb = Api.Surface.approximateBoundingBox s
                            Box3d.FromMinAndSize(V3d.Zero, bb.Size), bb.Min
                        )
                    Sg.wireBox (AVal.constant C4b.White) (AVal.map fst box)
                    |> Sg.trafo (box |> AVal.map (fun (b, pos) -> Trafo3d.Translation(pos)))
                    |> Sg.noEvents
                )
                |> Sg.set
                |> Sg.shader { 
                    do! DefaultSurfaces.vertexColor
                    do! DefaultSurfaces.stableTrafo
                  }
                
            let frustum = Frustum.perspective 60.0 0.1 10000.0 1.0 |> AVal.constant
            let camera : aval<Camera> = (model.freeFlyState.view, frustum) ||> AVal.map2 (fun v p -> Camera.create v p)
            let createSgs (camera : aval<Camera>) (framebufferSize : aval<V2i>) (signature : IFramebufferSignature) = 

                let surfaceSg = 
                    model.state.surfaces 
                    |> AMap.toASet 
                    |> ASet.map (fun (surfaceName, surface) -> 
                        surface.opcs 
                        |> AMap.toASet 
                        |> ASet.map (fun (opcId, opc) -> 
                            PatchLod.toRoseTree opc.opc.tree
                            |> Sg.patchLod signature runner opcId DefaultMetrics.mars false true ViewerModality.XYZ PatchLod.CoordinatesMapping.Local true 
                            |> Sg.noEvents
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
                    |> Sg.uniform "CursorWorldSizeSquared" (V4f(0.1, 0.12, 0.15, 0.17) |> AVal.constant)
                    |> Sg.noEvents


                let afterMain = RenderPass.after "cursor" RenderPassOrder.Arbitrary RenderPass.main

                let coordinateCross  =
                    let lines = 
                        (model.orbitState.center, model.orbitState.view) 
                        ||> AVal.map2 (fun center view -> 
                            [|Line3d(V3d.Zero, view.Sky)|]
                        )
                    Sg.lines (AVal.constant C4b.Red) lines
                    |> Sg.trafo (model.orbitState.center |> AVal.map (fun center -> Trafo3d.Translation center))
                    |> Sg.shader {
                        do! DefaultSurfaces.stableTrafo
                    }
                    |> Sg.noEvents
                Sg.ofSeq [surfaceSg; surfaceBBs]
                |> Sg.noEvents
   

            let scene = renderToScene model.background camera createSgs model.mousePos emit 

            DomNode.RenderControl(attributes, camera, scene, None)
            

        renderControl


    let dependencies = Html.semui @ [
        { name = "style"; kind = Stylesheet; url = "./style.css"}
        { name = "semui-overrides"; kind = Stylesheet; url = "semui-overrides.css"}
    ] 

    let view (runner : Load.Runner) (emit : Message -> unit) (model : AdaptiveModel) =

        let renderControl = viewScene runner emit model

        let distanceToCamera = 
            (model.freeFlyState.view, model.cursor) 
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
                    Formatting.Len(d).ToString() 
                | None -> ""
            )

        let content =
            div [style "display: grid; grid-template-rows: 30px 1fr; width: 100%; height: 100%; overflow: hidden" ] [
                div [style "grid-row: 1"] [
                    div [] [
                    ]
                    button [onClick (fun _ -> CenterScene)] [text "Center Scene"]
                    button [onClick (fun _ -> ToggleBackground)] [text "Change Background"]
                    //Simple.dropDown [] 
                    //    model.cameraMode SetCameraMode 
                    //    (Map.ofList [
                    //        CameraMode.FreeFly, "Free Fly"; CameraMode.Orbit, "Orbit"; 
                    //    ])
                ]
                div [style "grid-row: 2; width: 100%; height: 100%"] [
                    renderControl
                ]
                Incremental.div attribs (AList.ofList [Incremental.text cursorText])
                br []
            ]

        require dependencies (
            body [style "margin : 0; height: 100%; overflow: hidden"] [
                page (fun request -> 
                    match Map.tryFind "view" request.queryParams with
                    | Some "lite" -> 
                        div [style "width: 100%; height: 100%"] [renderControl]
                    | _ -> 
                        content
                )
            ]
        )

    let threads (model : Model) =
        let freeFly = FreeFlyController.threads model.freeFlyState |> ThreadPool.map FreeFlyMessage
        let orbit = OrbitController.threads model.orbitState |> ThreadPool.map OrbitMessage
        ThreadPool.union freeFly orbit


    let app (runtime : IRuntime) (emit : Message -> unit) =

        let runner = runtime.CreateLoadRunner(2)

        // download garden city from pro3d.space
        let surfaceDir = @"I:\OPC\GardenCity\MSL_Mastcam_Sol_925_id_48420"
        let surface = Api.Surface.loadSurfaceDirectory surfaceDir
        let planet = Planet.Mars

        let bb = Api.Surface.approximateBoundingBox surface
        let cameraView = Api.Surface.centerView' planet bb
        
        let state =
            {
                surfaces =  HashMap.ofList ["StereoMosaic", surface]
                planet   =  planet
            }

        let freeFlyState = { FreeFlyController.initial with freeFlyConfig = Camera.defaultConfig 5.0; view = cameraView }
        let orbitState  = { OrbitState.ofFreeFly (Vec.distance bb.Center bb.Max)  freeFlyState with sky = freeFlyState.view.Sky }

        {
            unpersist = Unpersist.instance
            threads = threads
            initial =
                {
                   orbitState = orbitState
                   freeFlyState =  freeFlyState
                   background = C4b.Gray
                   mousePos = None
                   cursor = None
                   state = state
                   cameraMode = Orbit
                   cursorWorldSphereSize = 20.0
                   
                }
            update = update
            view = view runner emit
        } 