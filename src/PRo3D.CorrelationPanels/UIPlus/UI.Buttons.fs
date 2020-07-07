namespace UIPlus    
    
    module Buttons = 
      open Aardvark.UI
      open System
      open Aardvark.Base
      open Aardvark.Base.Incremental

      let iconButton (iconStr : string) (tooltip : string) (onClick : V2i -> 'msg) = 
        div [clazz "item"]
            [
              button [clazz "ui icon button"; onMouseClick onClick] 
                      [i [clazz iconStr] [] ] |> ToolTips.wrapToolTip tooltip
            ]

      let iconButton' (iconStr : string) (tooltip : string) (onClick : V2i -> 'msg) st = 
        button [clazz "ui small icon button"; onMouseClick onClick; st] 
                [i [clazz iconStr] [] ] |> ToolTips.wrapToolTip tooltip

      let iconButtonNoTooltip (iconStr : string) (onClick : V2i -> 'msg) st = 
        button [clazz "ui small icon button"; onMouseClick onClick; st] 
                [i [clazz iconStr] [] ] 
          
      let toggleButton (str : string) (onClick : list<string> -> 'msg) = 
        Incremental.button
          (AttributeMap.ofList (
                                [clazz "small ui toggle button";
                                 style "margin: 1px 1px 1px 1px"
                                ]@(Event.toggleAttribute onClick)) 
                               )
          (AList.ofList [text str])



          

      module Incremental =
          let iconButton (iconStr : string) (onClick : V2i -> 'msg) = 
                  button [clazz "ui icon button"; onMouseClick onClick] 
                          [i [clazz iconStr] [] ] //TODO |> wrapToolTip tooltip 

          let smallIconButton (iconStr : string) (onClick : V2i -> 'msg) = 
            button [clazz "ui small icon button"; onMouseClick onClick] 
                    [i [clazz iconStr] [] ] //TODO |> wrapToolTip tooltip 

          let toggleButton (str : IMod<string>) (onClick : V2i -> 'msg) = 
            button [clazz "ui toggle button"; onMouseClick onClick] [Incremental.text str]
      
          let getColourIconButton (color : IMod<C4b>) (label : IMod<string>) (onClick : V2i -> 'msg) =
            let icon = 
              let iconAttr =
                amap {
                  yield clazz "circle icon"
                  let! c = color
                  yield style (sprintf "color:%s" (GUI.CSS.colorToHexStr c))
                }      
              Incremental.i (AttributeMap.ofAMap iconAttr) (alist{yield Incremental.text label})
            button [clazz "ui labeled icon button"; onMouseClick onClick] 
                   [icon]



          let getColourIconButton' (color : IMod<C4b>) (onClick : V2i -> 'msg) =
            let icon = 
              let iconAttr =
                amap {
                  yield clazz "circle icon"
                  let! c = color
                  yield style (sprintf "color:%s" (GUI.CSS.colorToHexStr c))
                }      
              Incremental.i (AttributeMap.ofAMap iconAttr) (AList.ofList [])
            button [clazz "ui icon button"; onMouseClick onClick] 
                   [icon]