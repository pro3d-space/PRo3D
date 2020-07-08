namespace Test

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives
open Svgplus
open Svgplus.DA
open Svgplus.RoseDiagramModel
open CorrelationDrawing

type Action =
    | ToggleModel
    | ButtonMessage   of Button.Action
    | RDMessage       of RoseDiagram.RoseDiagramAction
    | DiagramMessage  of DiagramAppAction
    | MouseMove       of V2i
    | ArrowMessage    of Arrow.Action
    | HeaderMessage   of HeaderAction

module App =
    open Svgplus.Mutable
    open Svgplus
    open Aardvark.Base.IL.Frontend
    
    let initial : TestModel =
        {
            currentModel  = Primitive.Box
            arrow         = Arrow.init Direction.Right
            header        = {HeaderApp.init with centre = V2d (500.0)} |> HeaderApp.layout true
            svgButton     = {Button.init with pos = V2d (10.0)}
            roseDiagram   = RoseDiagram.init [10.0; 30.0; 60.0]
            //diagramApp    = Diagram.init ra
        }

    let update (model : TestModel) (msg : Action) =
        match msg with
        | ToggleModel -> 
            match model.currentModel with
            | Box -> { model with currentModel = Sphere }
            | Sphere -> { model with currentModel = Primitive.Box }

        | ButtonMessage msg -> 
            match msg with
            | Svgplus.Button.Action.OnLeftClick p -> 
                {model with svgButton = (Svgplus.Button.update model.svgButton msg)}
            | _ -> {model with svgButton = (Svgplus.Button.update model.svgButton msg)}
            
        | RDMessage msg -> {model with roseDiagram = Svgplus.RoseDiagram.update model.roseDiagram msg}
        //| DiagramMessage msg -> 
        //  {model with diagramApp = Diagram.update model.diagramApp msg}
              
        //| MouseMove p -> 
        //  {model with diagramApp = 
        //                Diagram.update model.diagramApp 
        //                                  (Diagram.Action.MouseMove (V2d p))
        //  }
        | ArrowMessage m ->
            {model with arrow = Arrow.update model.arrow m}
        | Action.HeaderMessage m ->
            {model with header = HeaderApp.update model.header m}
        | _ -> failwith "[Appfs] unknown case"

    let view (model : MTestModel) =
        let svgAtts = 
            [
                clazz "svgRoot"
                style "border: 1px solid black"
                attribute "preserveAspectRatio" "xMinYMin meet"
                attribute "height" "100%"
                attribute "width" "100%"
                (onMouseMove (fun p -> Action.MouseMove p))
            ] |> AttributeMap.ofList

        let button = 
            Svgplus.Button.view model.svgButton 
            |> UI.map ButtonMessage 
            |> AList.single

        let rose = 
            RoseDiagram.view model.roseDiagram
            |> UI.map RDMessage
            |> AList.single

        let content = 
            HeaderApp.view model.header 
            |> AList.map (UI.map Action.HeaderMessage)

        require (GUI.CSS.myCss) (
          body [] [
              // CameraController.controlledControl m.cameraState CameraMessage frustum (AttributeMap.ofList att) sg
                  
              //div [style "position: fixed; left: 20px; top: 20px"] [
              //    button [onClick (fun _ -> ToggleModel)] [text "Toggle Model"]
              //]

              Incremental.Svg.svg svgAtts content
              
          ]
        )

    let app =
        {
            initial = initial
            update = update
            view = view
            threads = fun _ -> ThreadPool.empty
            unpersist = Unpersist.instance
        }