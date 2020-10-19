namespace PRo3D.Core

open System

module UI =
    open Aardvark.UI
    open FSharp.Data.Adaptive

    let toAlignmentString (alignment : TTAlignment) =
        match alignment with
        | TTAlignment.Top      -> "top center"
        | TTAlignment.Right    -> "right center"
        | TTAlignment.Bottom   -> "bottom center"   
        | TTAlignment.Left     -> "left center" 
        | _ -> alignment |> sprintf "unknown alignment %A" |> failwith

    let wrapToolTip (text:string) (alignment:TTAlignment ) (dom:DomNode<'a>) : DomNode<'a> =
        //dom
        let attr = 
            [ attribute "title" text
              attribute "data-position" (toAlignmentString alignment) //"top center"
              attribute "data-variation" "mini" ] 
                |> AttributeMap.ofList
                //|> AttributeMap.union dom.                
                
        onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
            dom.WithAttributes attr     
        )

    let wrapToolTipRight (text:string) (dom:DomNode<'a>) : DomNode<'a> =
        //dom
        let attr = 
            [ attribute "title" text
              attribute "data-position" "right center"
              attribute "data-variation" "mini"] 
                |> AttributeMap.ofList
                //|> AttributeMap.union dom.Attributes                
                
        onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
            dom.WithAttributes attr     
        )

    let wrapToolTipBottom (text:string) (dom:DomNode<'a>) : DomNode<'a> =
        //dom
        let attr = 
            [ attribute "title" text
              attribute "data-position" "bottom center"
              attribute "data-variation" "mini"] 
                |> AttributeMap.ofList
                //|> AttributeMap.union dom.Attributes                
                
        onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
            dom.WithAttributes attr     
        )


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

