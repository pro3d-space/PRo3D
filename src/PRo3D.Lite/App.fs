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
                
            let frustum = Frustum.perspective 60.0 0.01 1000.0 1.0 |> AVal.constant
            let camera : aval<Camera> = (model.cameraState.view, frustum) ||> AVal.map2 Camera.create
            let createSgs (clientValues : ClientValues) = 
                let surfaceSg = 
                    model.state.surfaces 
                    |> AMap.toASet 
                    |> ASet.map (fun (surfaceName, surface) -> 
                        surface.opcs 
                        |> AMap.toASet 
                        |> ASet.map (fun (opcId, opc) -> 
                            PatchLod.toRoseTree opc.opc.tree
                            |> Sg.patchLod clientValues.signature runner opcId DefaultMetrics.mars false false ViewerModality.XYZ true 
                        )
                        |> Sg.set
                    )
                    |> Sg.set
                surfaceSg |> Sg.noEvents

            DomNode.RenderControl(renderControlAttributes, camera, createSgs,   
                RenderControlConfig.standard, None
            )

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

        let state =
            {
                surfaces =  HashMap.ofList ["victoria crater", surface]
            }

        {
            unpersist = Unpersist.instance
            threads = threads
            initial =
                {
                   cameraState = initialCamera
                   background = C4b.Black
                   state = state
                }
            update = update
            view = view runner
        } 