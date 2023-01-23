namespace PRo3D.Viewer

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

module Provenance =

    module ProvenanceModel =
        let initial = { states = [] }

    let reduce (m : Model) : PModel = 
        { 
            cameraView = m.navigation.camera.view;
            frustum = m.frustum
            drawing = m.drawing
        }

    let applyPModel (baseModel : Model) (model : PModel) : Model =
        let setCameraView =
            PRo3D.Viewer.Model.navigation_ >-> NavigationModel.camera_ >-> CameraControllerState.view_
         
        let baseModel = 
            baseModel 
            |> Optic.set setCameraView   model.cameraView 
            |> Optic.set Model.frustum_  model.frustum
            |> Optic.set Model.drawing_  model.drawing
           
        baseModel

    let toAction (pModel : Model) (msg : PMessage) : list<ViewerAction> =
        match msg with
        | SetCameraView view -> 
            [ ViewerAction.SetCamera view ]

    let collapse (o : PMessage) (n : PMessage) =
        match o, n with
        | PMessage.SetCameraView _, PMessage.SetCameraView c -> Some (PMessage.SetCameraView c)
        | _ , _ -> None

    let addNode (pm : ProvenanceModel) (s : PModel) (msg : PMessage) =
        match pm.states with
        | [] -> { pm with states = [{ input = Some msg; state = s }] }
        | x::xs ->
            match x.input with
            | None -> { pm with states = { input = Some msg; state = s} :: pm.states }
            | Some lastMsg ->
                match collapse lastMsg msg with
                | None -> { pm with states = { input = Some msg; state = s} :: pm.states }
                | Some r -> { pm with states = { input = Some msg; state = s} :: xs }

    let reduceAction (newModel : Model) (msg : ViewerAnimationAction) =
        match msg with
        | ViewerMessage (ViewerAction.NavigationMessage (Action.FreeFlyAction freeFly)) -> 
            PMessage.SetCameraView newModel.navigation.camera.view |> Some
        | _ -> 
            None


    let createMessage (model : Model) (msg : ViewerAnimationAction) (update : Model -> Model) : Model =
        let newModel = update model
        let pModel = reduce newModel
        match reduceAction newModel msg with
        | Some pMsg -> 
            { model with provenanceModel = addNode model.provenanceModel pModel pMsg }
        | _ -> 
            model

