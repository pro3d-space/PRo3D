namespace UIPlus


    open System
    open Aardvark.Base
    open Aardvark.UI

    module Menus = 
      module Incremental = 
        let toMenuItem label action =
          div [clazz "item"; onClick (fun _ -> action)]
              [Incremental.text label]
        
        let toMouseOverMenu itemList =
          div [clazz "left floated item"] [
              div [clazz "ui simple pointing dropdown top left"]
                [ 
                  span [clazz "text"][text "Load"]
                  i [clazz "dropdown icon"][]
                  Incremental.div (AttributeMap.ofList [clazz "menu";style "margin-top: 0rem"])
                      itemList //[div [clazz "header"][text "load"]]
                ]
          ]

      let toMouseOverMenu itemList =
        div [clazz "left floated item"] [
            div [clazz "ui simple pointing dropdown top left"]
              [ 
                span [clazz "text"][text "Load"]
                i [clazz "dropdown icon"][]
                div [clazz "menu";style "margin-top: 0rem"]
                    itemList //[div [clazz "header"][text "load"]]
              ]
        ]

      let toMenuItem label action =
        div [clazz "item"; onClick (fun _ -> action)]
            [text label]

      let saveCancelMenu saveAction cancelAction =
        div [
              clazz "ui buttons"; 
              style "vertical-align: top; horizontal-align: middle"
            ]
            [
              button 
                [
                  clazz "compact ui button"; 
                  onMouseClick (fun _ -> saveAction)
                ]
                [
                  text "Save"
                ]

              div [clazz "or"][]

              button 
                [
                  clazz "compact ui button"; 
                  onMouseClick (fun _ -> cancelAction)
                ]
                [
                  text "Cancel"
                ]
            ]