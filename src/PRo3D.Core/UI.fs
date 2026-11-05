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
                
            // we call 'refresh' to place the tooltip after the DOM elements are rendered to ensure
            // that the tooltip is at the right position
            onBoot "$('#__ID__').popup({inline:true,hoverable:true}).popup('refresh');" (
                dom.WithAttributes attr
            )
        else
            dom

    /// `Html.row` whose label carries a plain `title` tooltip (honours -notooltips). Plain
    /// title rather than `wrapToolTip`'s popup, so it also works in tables that re-render.
    let rowWithTip (label : string) (tip : string) (content : list<DomNode<'msg>>) : DomNode<'msg> =
        tr [] [
            td [ yield clazz "collapsing"; if enabletoolTips then yield attribute "title" tip ] [ text label ]
            td [ clazz "right aligned" ] content
        ]

    /// `rowWithTip` with a label and tooltip that change, e.g. with the scene's body.
    let rowWithTipA (label : aval<string>) (tip : aval<string>) (content : list<DomNode<'msg>>) : DomNode<'msg> =
        let attributes =
            amap {
                yield clazz "collapsing"
                if enabletoolTips then
                    let! t = tip
                    yield attribute "title" t
            } |> AttributeMap.ofAMap
        tr [] [
            Incremental.td attributes (AList.ofList [ Incremental.text label ])
            td [ clazz "right aligned" ] content
        ]

    let dropDownWithEmptyText
                   (values : alist<'a>)(selected : aval<Option<'a>>)
                   (change : Option<'a> -> 'msg) (f : 'a ->string) 
                   (emptyText : string) =

        let attributes (name : string) =
            AttributeMap.ofListCond [
                always (attribute "value" (name))
                onlyWhen (
                    selected 
                    |> AVal.map (
                        fun x -> 
                            match x with
                            | Some s -> name = f s
                            | None   -> name = emptyText
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
                    yield Incremental.option (attributes emptyText) (AList.ofList [text emptyText])
                    yield! values |> AList.mapi(fun i x -> Incremental.option (attributes (f x)) (AList.ofList [text (f x)]))
                }
            )

    let dropDown'' (values : alist<'a>)(selected : aval<Option<'a>>)
                   (change : Option<'a> -> 'msg) (f : 'a ->string)  =
        dropDownWithEmptyText values selected change f "-None-"

    module Dialogs =    
  
        let onChooseFiles (chosen : list<string> -> 'msg) =
            let cb xs =
                match xs with
                | [] -> chosen []
                | x::[] when x <> null -> 
                    x 
                    |> Pickler.json.UnPickleOfString
                    |> List.map Path.ofUnixStyle
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
                        |> Pickler.json.UnPickleOfString
                        |> List.map Path.ofUnixStyle
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
                    |> Pickler.json.UnPickleOfString
                    |> Path.ofUnixStyle
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
                        x |> Pickler.json.UnPickleOfString |> Path.ofUnixStyle |> chosen
                    | _ -> 
                        String.Empty |> chosen
            onEvent "onsave" [] cb