namespace PRo3D.Core

open Aether
open Aether.Operators

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Drawing
open Aardvark.Rendering
open Aardvark.UI.Primitives

open PRo3D.Navigation
open PRo3D.Viewer
open Aardvark.UI.Anewmation
open Aardvark.UI.Animation

module Provenance =


    let applyPModel (baseModel : Model) (model : PModel) : Model =
        let setCameraView =
            PRo3D.Viewer.Model.navigation_ >-> NavigationModel.camera_ >-> CameraControllerState.view_
         
        let setDrawing =
            PRo3D.Viewer.Model.drawing_ 

        let baseModel = 
            baseModel 
            |> Optic.set setCameraView  model.cameraView 
            |> Optic.set setDrawing model.annotations
           
        baseModel

    let toAction (model : Model) (msg : PMessage) : list<ViewerAction> =
        match msg with
        | PMessage.SetCameraView view -> 
            [ ViewerAction.SetCamera view ]

    let collapse (o : PMessage) (n : PMessage) =
        match o, n with
        | PMessage.SetCameraView _, PMessage.SetCameraView c -> Some (PMessage.SetCameraView c)
        | _ , _ -> None


    type ProcenanceAction<'msg> =
        | TrackMessage of 'msg
        | OverwrideLastNode of 'msg
        | Ignore

    let reduceAction (oldModel : Model) (newModel : Model) (lastMsg : Option<PMessage>) (msg : ViewerAnimationAction) : ProcenanceAction<PMessage>  =
        printfn "%A" msg
        match msg with
        | ViewerMessage (ViewerAction.NavigationMessage (Action.FreeFlyAction freeFly)) -> 
            let msg = PMessage.SetCameraView newModel.navigation.camera.view
            match lastMsg with
            | Some (PMessage.SetCameraView _) -> 
                msg |> OverwrideLastNode
            | _ -> 
                msg |> TrackMessage

        | ViewerMessage (ViewerAction.DrawingMessage DrawingAction.Finish) -> 
            match oldModel.drawing.working with
            | Some working -> 
                PMessage.FinishAnnotation working.key |> TrackMessage
            | None -> 
                Log.warn "annot log finish annotation message"
                Ignore
        | ViewerMessage (ViewerAction.DrawingMessage (DrawingAction.Move p)) -> 
            Ignore
        | ViewerMessage (ViewerAction.DrawingMessage m) -> 
            match oldModel.drawing.working, newModel.drawing.working with
            | Some o, None -> PMessage.FinishAnnotation o.key |> TrackMessage
            | _ -> Ignore
        | _ -> 
            Ignore

    let reduceModel (model : Model) : PModel =
        { 
            cameraView = model.scene.cameraView
            annotations = model.drawing
        }

    
    let emptyWithModel (m : Model) = 
        { m with 
            provenanceModel = 
                { 
                    nodes = HashMap.ofList [ "input", { id = "input"; model = reduceModel m |> Some }]; edges = HashMap.empty; lastEdge = None 
                }
        }

    let track (oldModel : Model) (newModel : Model) (msg : ViewerAnimationAction) : Model =
        let reducedModel = reduceModel newModel
        match reduceAction oldModel newModel (ProvenanceModel.tryTip newModel.provenanceModel) msg with
        | TrackMessage pMsg -> 
            let provenaceModel = ProvenanceModel.newNode newModel.provenanceModel reducedModel pMsg
            { newModel with provenanceModel = provenaceModel }
        | OverwrideLastNode pMsg -> 
            let provenaceModel = ProvenanceModel.updateTip newModel.provenanceModel pMsg reducedModel
            { newModel with provenanceModel = provenaceModel }
        | Ignore -> 
            newModel


    open Aardvark.UI
    open Aardvark.UI.Primitives

    let dependencies = 
        [
            { url = "./jscytoscape.js"; name = "jscytoscapejs"; kind = Script }
            { url = "https://cdnjs.cloudflare.com/ajax/libs/cytoscape/3.23.0/cytoscape.min.js"; name = "jscytoscapejslib"; kind = Script }
            { url = "https://cdnjs.cloudflare.com/ajax/libs/dagre/0.8.5/dagre.min.js"; name = "dagre"; kind = Script }
            { url = "https://cdn.rawgit.com/cytoscape/cytoscape.js-dagre/1.5.0/cytoscape-dagre.js"; name = "graphlib"; kind = Script }
            //{ url = "https://cdn.jsdelivr.net/npm/cytoscape-dagre@2.5.0/cytoscape-dagre.min.js"; name = "dagre"; kind = Script }
        ]

    let view (m : AdaptiveModel) =
        div [style "width: 100%; height: 100%"] [
            let nodes = 
                m.provenanceModel.nodes 
                |> AMap.map (fun k v -> v |> ProvenanceModel.Thoth.CyNode.fromPNode |> ProvenanceModel.Thoth.CyNode.toJs)
                |> AMap.toASetValues
                |> ASet.channel

            let edges = 
                m.provenanceModel.edges 
                |> AMap.map (fun k v -> v |> ProvenanceModel.Thoth.CyEdge.fromPNode |> ProvenanceModel.Thoth.CyEdge.toJs)
                |> AMap.toASetValues 
                |> ASet.channel

            let events = 
                onEvent' "clickNode" [] (fun s -> 
                    match s with
                    | [v] -> 
                        let nodeId : string = Pickler.unpickleOfJson v
                        [Provenance.ProvenanceMessage.ActivateNode nodeId]
                    | _ ->
                        []
                )

            require dependencies (
                onBoot' ["provenanceNodes", nodes; "provenanceEdges", edges] "jscytoscape('__ID__')" (
                    div [style "width: 100%; height: 100%"; events] []
                ) 
            )
        ]

    /// Creates an animation that interpolates between the camera views src and dst.
    let interpolate (src : CameraView) (dst : CameraView) : IAnimation<'Model, CameraView> =
        let animPos = Animation.Primitives.lerp src.Location dst.Location
        let animOri = Animation.Primitives.slerp src.Orientation dst.Orientation

        (animPos, animOri)
        ||> Animation.map2 (fun pos ori -> CameraView.orient pos ori dst.Sky)

    let animateView (oldModel : Model) (newModel : Model) : Model =
        let view = newModel.navigation.camera.view
        let animationMessage = 
            CameraAnimations.animateForwardAndLocation view.Location view.Forward view.Up 2.0 "ForwardAndLocation2s"
        let animations = AnimationApp.update newModel.animations (AnimationAction.PushAnimation(animationMessage))
        { newModel with animations = animations }


    let update (msg : Provenance.ProvenanceMessage) (m : Model) : Model =
        match msg with 
        | Provenance.ProvenanceMessage.ActivateNode s -> 
            match m.provenanceModel.nodes |> HashMap.tryFind s with
            | None -> m
            | Some n -> 
                match n.model with
                | None -> 
                    Log.warn "no model. rewind and replay not implemented"
                    m
                | Some pm -> 
                    let newModel = applyPModel m pm
                    let animated = animateView m newModel

                    // let us create a new branch
                 

                    animated