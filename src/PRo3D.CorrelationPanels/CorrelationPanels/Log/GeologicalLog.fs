namespace CorrelationDrawing

open System

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI
open Aardvark.Application
open Svgplus.RectangleStackTypes
open Svgplus
open Svgplus.RectangleType
open Svgplus.DiagramItemType
open UIPlus
     
open CorrelationDrawing.LogTypes
open CorrelationDrawing.Types
open CorrelationDrawing.SemanticTypes
open CorrelationDrawing.LogNodeTypes
open CorrelationDrawing.AnnotationTypes

open PRo3D.Base
open FParsec

type GeologicalLogAction =
    | SetState          of State
    | ToggleState          
    | LogNodeMessage    of LogNodeId * LogNodes.LogNodeAction
    | TextInputMessage  of DiagramItemId * TextInput.Action
    | MoveUp            of RectangleStackId
    | MoveDown          of RectangleStackId

module GeologicalLog =
    
    let headings = ["name";"move"]
                 
    let findNode (model : GeologicalLog) (f : LogNode -> bool) =
        let nodeList = 
            model.nodes
            |> PList.map (LogNodes.Recursive.filterAndCollect f)
            |> DS.PList.flattenLists

        let node = List.tryHead nodeList
        node
    
    let findNodeFromRectangleId (model : GeologicalLog) (rid   : RectangleId) =
        findNode model (fun (n : LogNode) -> n.rectangleId = rid)
        
    /////////////////////////////////////////// UPDATE /////////////////////////////////////////
    let update (model : GeologicalLog) (action : GeologicalLogAction) =
        Console.ForegroundColor <- ConsoleColor.Cyan
        Log.line "%A" action
        Console.ResetColor()

        match action with
        | MoveDown id -> model
        | MoveUp id   -> model
        | TextInputMessage (m,id) -> model
        | LogNodeMessage (id, m) -> 
            let nodes = 
                model.nodes 
                |> LogNodes.Recursive.applyAll (fun n -> 
                    match n.id = id with
                    | true -> (LogNodes.Update.update m n) 
                    | false -> n
                )

            { model with nodes = nodes }
        | SetState state -> 
            { model with state = state}
        | ToggleState -> 
            let state =
                match model.state with
                | State.New | State.Edit -> State.Display
                | State.Display -> State.Edit
            { model with state = state }

