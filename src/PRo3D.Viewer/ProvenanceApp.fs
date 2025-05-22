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
open Aardvark.UI.Animation
open Aardvark.UI.Animation.Deprecated

module ProvenanceApp =

    open ProvenanceApp


    let applyPModel (baseModel : Model) (model : PModel) : Model =
        let setCameraView =
            PRo3D.Viewer.Model.navigation_ >-> NavigationModel.camera_ >-> CameraControllerState.view_
         
        let setDrawing =
            PRo3D.Viewer.Model.drawing_ 

        let baseModel = 
            baseModel 
            |> Optic.set setDrawing model.annotations
           
        baseModel

    //let toAction (model : Model) (msg : PMessage) : list<ViewerAction> =
    //    match msg with
    //    | PMessage.SetCameraView view -> 
    //        [ ViewerAction.SetCamera view ]

    let collapse (o : PMessage) (n : PMessage) =
        match o, n with
        | PMessage.SetCameraView _, PMessage.SetCameraView c -> Some (PMessage.SetCameraView c)
        | _ , _ -> None


    type ProcenanceAction<'msg> =
        | TrackMessage of 'msg
        | OverwrideLastNode of 'msg
        | Ignore

    let reduceAction (oldModel : Model) (newModel : Model) (lastMsg : Option<PMessage>) (msg : ViewerAnimationAction) : ProcenanceAction<PMessage>  =
        match msg with
        | ViewerMessage (ViewerAction.NavigationMessage (Action.FreeFlyAction FreeFlyController.Message.Rendered)) -> 
            Ignore
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
        | ViewerMessage (ViewerAction.DrawingMessage DrawingAction.Nop) -> 
            Ignore
        | ViewerMessage (ViewerAction.LoadScene s) ->   
            TrackMessage (PMessage.LoadScene s)
        | ViewerMessage (ViewerAction.DrawingMessage _) | ViewerMessage (ViewerAction.PickSurface(_,_,_)) -> 
            match oldModel.drawing.working, newModel.drawing.working with
            | Some o, None -> PMessage.FinishAnnotation o.key |> TrackMessage
            | _ -> Ignore
        | _ -> 
            Ignore

    let reduceModel (model : Model) : PModel =
        { 
            cameraView = model.navigation.camera.view
            annotations = model.drawing
        }

    
    let emptyWithModel (enabled : bool) (m : Model) = 
        if enabled then
            let i = { id = ProvenanceModel.newNodeId(); model = reduceModel m |> Some }
        
            let pm = { 
                nodes = HashMap.ofList [i.id, i]
                edges = HashMap.empty; lastEdge = None
                automaticRecording = false
                currentTrail = []
                selectedNode = None
                initialNode = Some i.id
            }


            { m with 
                provenanceModel = pm
            }
        else    
            m

    let track (oldModel : Model) (newModel : Model) (msg : ViewerAnimationAction) : Model =
        if newModel.provenanceModel.automaticRecording then
            let action = reduceAction oldModel newModel (ProvenanceModel.tryTip newModel.provenanceModel) msg
            let reducedModel = reduceModel newModel
            match action with
            | TrackMessage pMsg -> 
                let provenaceModel = ProvenanceModel.newNode newModel.provenanceModel reducedModel pMsg
                { newModel with provenanceModel = provenaceModel }
            | OverwrideLastNode pMsg -> 
                let provenaceModel = ProvenanceModel.updateTip pMsg reducedModel newModel.provenanceModel 
                { newModel with provenanceModel = provenaceModel }
            | Ignore -> 
                newModel
        else
            let last =
                match newModel.provenanceModel.currentTrail with
                | mostRecent :: xs -> Some mostRecent
                | _ -> (ProvenanceModel.tryTip newModel.provenanceModel)
            let action = reduceAction oldModel newModel last msg
            let reducedModel = reduceModel newModel
            match action with
            | TrackMessage pMsg -> 
                let newTrail = pMsg :: newModel.provenanceModel.currentTrail
                Optic.set (Model.provenanceModel_ >-> ProvenanceModel.currentTrail_) newTrail newModel
            | OverwrideLastNode pMsg -> 
                let newTrail = 
                    match newModel.provenanceModel.currentTrail with
                    | _ :: rest -> pMsg :: rest // override last element in trail
                    | _ -> [pMsg]
                //let provenaceModel = ProvenanceModel.updateTip pMsg reducedModel newModel.provenanceModel 
                newModel 
                //|> Optic.set Model.provenanceModel_ provenaceModel
                |> Optic.set (Model.provenanceModel_ >-> ProvenanceModel.currentTrail_) newTrail 
            | _ -> 
                newModel



    open Aardvark.UI
    open Aardvark.UI.Primitives

    let dependencies = 
        [
            { url = "./resources/jscytoscape.js"; name = "jscytoscapejs"; kind = Script }
            { url = "https://cdnjs.cloudflare.com/ajax/libs/cytoscape/3.23.0/cytoscape.min.js"; name = "jscytoscapejslib"; kind = Script }
            { url = "https://cdnjs.cloudflare.com/ajax/libs/dagre/0.8.5/dagre.min.js"; name = "dagre"; kind = Script }
            { url = "https://cdn.rawgit.com/cytoscape/cytoscape.js-dagre/1.5.0/cytoscape-dagre.js"; name = "graphlib"; kind = Script }
            //{ url = "https://cdn.jsdelivr.net/npm/cytoscape-dagre@2.5.0/cytoscape-dagre.min.js"; name = "dagre"; kind = Script }
        ]

    let view (m : AdaptiveModel) =
        let storage = ProvenanceModel.nopStorage ()
        div [style "width: 100%; height: 100%"] [
            let nodes = 
                m.provenanceModel.nodes 
                |> AMap.map (fun k v -> v |> ProvenanceModel.Thoth.CyNode.fromPNode storage |> ProvenanceModel.Thoth.CyNode.toJs)
                |> AMap.toASetValues
                |> ASet.channel

            let edges = 
                m.provenanceModel.edges 
                |> AMap.map (fun k v -> v |> ProvenanceModel.Thoth.CyEdge.fromPEdge |> ProvenanceModel.Thoth.CyEdge.toJs)
                |> AMap.toASetValues 
                |> ASet.channel

            let selectedNode =
                m.provenanceModel.selectedNode
                |> AVal.map (Option.map (fun v -> v.id))
                |> AVal.channel

            let events = 
                onEvent' "clickNode" [] (fun s -> 
                    match s with
                    | [v] -> 
                        let nodeId : string = Pickler.unpickleOfJson v
                        [ProvenanceMessage.ActivateNode nodeId]
                    | _ ->
                        []
                )

            require dependencies (
                div [style "width:100%; height: 100%"] [
                    div [style "width: 100%; padding: 5px; border: white; border-style: solid"] [
                        let buttonAttibs =
                            amap {
                                yield onClick (fun _ -> ProvenanceMessage.ToggleAutomaticRecording)
                                let! automaticRecording = m.provenanceModel.automaticRecording
                                if automaticRecording then 
                                    yield clazz "ui green tiny button"
                                else 
                                    yield clazz "ui gray tiny button"
                            } |> AttributeMap.ofAMap
                        yield Incremental.button buttonAttibs (AList.ofList [text "Automatic tracking"])
                        yield button [clazz "ui tiny button"; onClick (fun _ -> ProvenanceMessage.CreateNode)] [text "Track step"] 
                    ]
                    onBoot' ["provenanceNodes", nodes; "provenanceEdges", edges; "selectedNode", selectedNode] "jscytoscape('__ID__')" (
                        div [style "width: 100%; height: 100%"; events] []
                    ) 
                ]
            )
        ]

    /// Creates an animation that interpolates between the camera views src and dst.
    let interpolate (src : CameraView) (dst : CameraView) : IAnimation<'Model, CameraView> =
        let animPos = Animation.Primitives.lerp src.Location dst.Location
        let animOri = Animation.Primitives.slerp src.Orientation dst.Orientation

        (animPos, animOri)
        ||> Animation.map2 (fun pos ori -> CameraView.orient pos ori dst.Sky)

    let animateView (model : Model) (view : CameraView) : Model =
        let animationMessage = 
            CameraAnimations.animateForwardAndLocation view.Location view.Forward view.Up 2.0 "ForwardAndLocation2s"
        let animations = AnimationApp.update model.animations (AnimationAction.PushAnimation(animationMessage))
        { model with animations = animations }


    let update (msg : ProvenanceApp.ProvenanceMessage) (m : Model) : Model =
        match msg with 
        | ProvenanceMessage.SetGraph(g, storage) -> 
            { m with provenanceModel = ProvenanceModel.Thoth.applyDescription storage m.provenanceModel g }

        | ProvenanceMessage.ActivateNode s -> 
            match m.provenanceModel.nodes |> HashMap.tryFind s with
            | None -> m
            | Some n -> 
                match n.model with
                | None -> 
                    Log.warn "no model. rewind and replay not implemented"
                    m
                | Some pm -> 
                    let newModel = applyPModel m pm
                    let animated = 
                        animateView newModel pm.cameraView
                        |> Optic.set (Model.provenanceModel_ >-> ProvenanceModel.selectedNode_) (Some n)

                    // let us create a new branch
                    let newPModel = reduceModel animated
                    if m.provenanceModel.automaticRecording then
                        let pm = ProvenanceModel.afterNode (Label n.id) m.provenanceModel newPModel PMessage.Branch 
                        animated
                        |> Optic.set Model.provenanceModel_ pm
                    else 
                        animated

        | ProvenanceMessage.ToggleAutomaticRecording -> 
            Optic.map (Model.provenanceModel_ >-> ProvenanceModel.automaticRecording_) not m

        | ProvenanceMessage.CreateNode -> 
            let reducedModel = reduceModel m
            let trailMessage = 
                match m.provenanceModel.currentTrail with
                | [] -> "nothing happened"
                | msgs -> msgs |> List.map PMessage.toHumanReadable |> String.concat System.Environment.NewLine
            let pMsg = PMessage.CreateNode trailMessage
            let pModel = 
                match m.provenanceModel.selectedNode with
                | Some n -> 
                    ProvenanceModel.afterNode (Label n.id) m.provenanceModel reducedModel pMsg
                    |> ProvenanceModel.updateTip pMsg reducedModel
                | _ -> 
                    ProvenanceModel.newNode m.provenanceModel reducedModel pMsg
                    |> ProvenanceModel.updateTip pMsg reducedModel
            m 
            |> Optic.set Model.provenanceModel_ pModel 
            |> Optic.set (Model.provenanceModel_ >-> ProvenanceModel.currentTrail_) [] 

