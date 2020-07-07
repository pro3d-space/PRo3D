namespace UIPlus
    module Tables =
      open Aardvark.UI
      open System
      open Aardvark.Base
      open Aardvark.Base.Incremental

      let textColorFromBackground (col  : C4b) =
         match  (int col.R + int col.B + int col.G) with
                  | c when c < 400  ->
                    "color: white"
                  | c when c >= 400 ->
                    "color: black"
                  | _ -> ""

      let intoTd (domNode) = 
        td [clazz "center aligned"] [domNode]

      let intoLeftAlignedTd (domNode) =
        td [clazz "left aligned"] [domNode]

      let intoTd' domNode colSpan = 
        td [clazz "center aligned";
            style GUI.CSS.lrPadding;
            attribute "colspan" (sprintf "%i" colSpan)]
           [domNode]
    
      let intoTr domNodes =
        tr [] domNodes

      let intoTr' (domNode) (atts : list<string * AttributeValue<'a>>) =
        tr atts [domNode]

      let intoTrOnClick fOnClick domNodeList =
        tr [onClick (fun () -> fOnClick)] domNodeList

      let intoActiveTr fOnClick domNodeList =
        tr [clazz "active";onClick (fun () -> fOnClick)] domNodeList

      let toTableView (menu : DomNode<'msg>) 
                      (rows : alist<DomNode<'msg>>) 
                      (columnNames : list<string>) = 

        let header = 
          columnNames
            |> List.map (fun str -> th[] [text str])

        require (GUI.CSS.myCss) (
         // body [clazz "ui"; style "background: #1B1C1E;position:fixed;width:100%;height:100%"] [
            div [] [
              menu
              table
                ([clazz "ui celled striped inverted table unstackable";
                                      style "padding: 1px 5px 2px 5px"]) (
                    [
                      thead [][tr[] header]
                      Incremental.tbody  (AttributeMap.ofList []) rows
                    ]
                )
            ]
         // ]
        )


      let toDisplayLabel (str : IMod<string>) =
        Incremental.label 
          (AttributeMap.ofList [clazz "ui horizontal label"]) 
          (AList.ofList [Incremental.text str])

      let toDisplayLabelCol (str : string) (bgColour : IMod<C4b>) =
        Incremental.label 
          (AttributeMap.union 
              (AttributeMap.ofList [clazz "ui horizontal label"]) 
              (AttributeMap.ofAMap (GUI.CSS.incrBgColorAMap bgColour)))
          (AList.ofList [text str])

      