namespace PRo3D.Base

open FSharp.Data.Adaptive
open Aardvark.UI

module GuiEx =
    
    let semui = 
      [ 
          { kind = Stylesheet; name = "semui"; url = "https://cdn.jsdelivr.net/semantic-ui/2.2.6/semantic.min.css" }
          { kind = Script; name = "semui"; url = "https://cdn.jsdelivr.net/semantic-ui/2.2.6/semantic.min.js" }
      ]

    module Layout =
        let boxH ch = td [clazz "collapsing"; style "padding: 0px 5px 0px 0px"] ch

        let horizontal ch = table [clazz "ui table inverted segment"; style "backgroundColor: transparent"] [ tbody [] [ tr [] ch ] ]

        let finish<'msg> = td [] []
                   
    let accordion text' icon active content' =
            let title = if active then "title active inverted" else "title inverted"
            let content = if active then "content active" else "content"
           // let arrow = if active then 
                                    
            onBoot "$('#__ID__').accordion();" (
                div [clazz "ui inverted segment"] [
                    div [clazz "ui inverted accordion fluid"] [
                        div [clazz title; style "background-color: #282828"] [
                                i [clazz ("dropdown icon")] []
                                text text'                                
                                div [style "float:right"] [i [clazz (icon + " icon")] []]
                                
                        ]
                        div [clazz content;  style "overflow-y : auto; "] content' //max-height: 35%
                    ]
                ]
            )

    let accordionWithOnClick text' icon active content' iconAction =
        let title = if active then "title active inverted" else "title inverted"
        let content = if active then "content active" else "content"
                                
        onBoot "$('#__ID__').accordion();" (
            div [clazz "ui inverted segment"] [
                div [clazz "ui inverted accordion fluid"] [
                    div [clazz title; style "background-color: #282828"] [
                            i [clazz ("dropdown icon")] []
                            text text'                                
                            div [style "float:right";onClick (fun _ -> iconAction)]
                                [i [clazz (icon + " icon")] []]
                            
                    ]
                    div [clazz content;  style "overflow-y : auto; "] content' //max-height: 35%
                ]
            ]
        )

    let iconToggle (dings : aval<bool>) onIcon offIcon action =
      let toggleIcon = dings |> AVal.map(fun isOn -> if isOn then onIcon else offIcon)

      let attributes = 
        amap {
            let! icon = toggleIcon
            yield clazz icon
            yield onClick (fun _ -> action)
        } |> AttributeMap.ofAMap

      Incremental.i attributes AList.empty

    let iconCheckBox (dings : aval<bool>) action =
      iconToggle dings "check square outline icon" "square icon" action
