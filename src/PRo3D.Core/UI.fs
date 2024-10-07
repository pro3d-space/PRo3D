namespace PRo3D.Core

open System

module UI =
    open Aardvark.UI

    open Aardvark.UI.Primitives    
    open FSharp.Data.Adaptive

    let mutable enabletoolTips = false

    let toAlignmentString (alignment : DataPosition) =
        match alignment with
        | DataPosition.Top      -> "top center"
        | DataPosition.Right    -> "right center"
        | DataPosition.Bottom   -> "bottom center"   
        | DataPosition.Left     -> "left center" 
        | _ -> 
            alignment |> sprintf "unknown alignment %A" |> failwith

    let wrapToolTip (alignment:DataPosition ) (text:string) (dom:DomNode<'a>) : DomNode<'a> =
        if(enabletoolTips) then
            //dom
            let attr = 
                [ 
                    attribute "title" text
                    attribute "data-position" (toAlignmentString alignment) //"top center"
                    attribute "data-variation" "mini" 
                ] 
                |> AttributeMap.ofList
                    //|> AttributeMap.union dom.                
                
            
            onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
                dom.WithAttributes attr     
            ) 
        else
            dom

    let dropDown'' (values : alist<'a>)(selected : aval<Option<'a>>) (change : Option<'a> -> 'msg) (f : 'a ->string)  =

        let attributes (name : string) =
            AttributeMap.ofListCond [
                always (attribute "value" (name))
                onlyWhen (
                    selected 
                    |> AVal.map (
                        fun x -> 
                            match x with
                            | Some s -> name = f s
                            | None   -> name = "-None-"
                    )) (attribute "selected" "selected")
            ]

        let ortisOnChange  = 
            let cb (i : int) =
                let currentState = values.Content |> AVal.force
                change (IndexList.tryAt (i-1) currentState)
                    
            onEvent "onchange" ["event.target.selectedIndex"] (fun x -> x |> List.head |> Int32.Parse |> cb)

        Incremental.select (AttributeMap.ofList [ortisOnChange; style "color:black"]) 
            (
                alist {
                    yield Incremental.option (attributes "-None-") (AList.ofList [text "-None-"])
                    yield! values |> AList.mapi(fun i x -> Incremental.option (attributes (f x)) (AList.ofList [text (f x)]))
                }
            )

    module Dialogs =    
  
        let onChooseFiles (chosen : list<string> -> 'msg) =
            let cb xs =
                match xs with
                | [] -> chosen []
                | x::[] when x <> null -> 
                    x 
                    |> Aardvark.Service.Pickler.json.UnPickleOfString 
                    |> List.map Aardvark.Service.PathUtils.ofUnixStyle 
                    |> chosen
                | _ -> 
                    chosen []
            onEvent "onchoose" [] cb   

        let onChooseDirectory (id:Guid) (chosen : Guid * string -> 'msg) =
            let cb xs =
                match xs with
                | [] -> chosen (id, String.Empty)
                | x::[] when x <> null -> 
                    let id = id
                    let path = 
                        x 
                        |> Aardvark.Service.Pickler.json.UnPickleOfString 
                        |> List.map Aardvark.Service.PathUtils.ofUnixStyle 
                        |> List.tryHead
                    match path with
                    | Some p -> 
                      chosen (id, p)
                    | None -> chosen (id,String.Empty)
                | _ -> 
                    chosen (id,String.Empty)
            onEvent "onchoose" [] cb   

        let onSaveFile (chosen : string -> 'msg) =
            let cb xs =
                match xs with
                | x::[] when x <> null -> 
                    x 
                    |> Aardvark.Service.Pickler.json.UnPickleOfString 
                    |> Aardvark.Service.PathUtils.ofUnixStyle 
                    |> chosen
                | _ -> 
                    chosen String.Empty //failwithf "onSaveFile: %A" xs
            onEvent "onsave" [] cb

        let onSaveFile1 (chosen : string -> 'msg) (path : Option<string>) =
            let cb xs =
                match path with
                | Some p-> p |> chosen
                | None ->
                    match xs with
                    | x::[] when x <> null -> 
                        x |> Aardvark.Service.Pickler.json.UnPickleOfString |> Aardvark.Service.PathUtils.ofUnixStyle |> chosen
                    | _ -> 
                        String.Empty |> chosen
            onEvent "onsave" [] cb