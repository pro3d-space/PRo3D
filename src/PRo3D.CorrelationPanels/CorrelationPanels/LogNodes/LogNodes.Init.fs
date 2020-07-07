namespace CorrelationDrawing.LogNodes

type LogNodeAction =
    | RectangleMessage    of Svgplus.RectangleAction  // TODO refactore

module Update = 
    open CorrelationDrawing.LogNodeTypes

    let update  (action : LogNodeAction) (model : LogNode) =

        match action with
        | RectangleMessage m ->
            {model with mainBody = (Svgplus.Rectangle.update model.mainBody m)}
        | _ -> 
            action |> sprintf "[LogNode Update] %A not implemented" |> failwith

module Recursive =
    open Aardvark.Base
    open Aardvark.UI
    open Aardvark.Base.Monads.Optics
    open CorrelationDrawing.LogNodeTypes

    let rec apply (n : LogNode) (f : LogNode -> LogNode) =
        match PList.count n.children with
        | 0     -> f n 
        | other -> 
            let c = n.children |> PList.map (fun (n : LogNode) -> apply n f)
            f {n with children = c}
    
    let applyAll (f : LogNode -> LogNode) (lst : plist<LogNode>) = 
        lst |> PList.map (fun n -> apply n f)

    let rec filterAndCollect (f : LogNode -> bool) (n : LogNode) =
        match PList.count n.children, f n with
        | a, true when a = 0 -> [n]
        | a, false when a = 0 -> []
        | other, true  -> 
            [n] @ 
            (n.children 
                |> PList.toList
                |> List.collect (fun (x : LogNode) -> filterAndCollect f x))
        | other, false -> 
            [] @  
            (n.children 
                |> PList.toList
                |> List.collect (fun (x : LogNode) -> filterAndCollect f x))