namespace UIPlus

    module ToolTips =
      open Aardvark.UI

      let wrapToolTip (text:string) (dom:DomNode<'a>) : DomNode<'a> =
          let attr = 
              [attribute "title" text
               attribute "data-position" "top center"
               attribute "data-variation" "mini" ] 
                  |> AttributeMap.ofList             
                
          onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
              dom.WithAttributes attr     
          )

      let wrapToolTipRight (text:string) (dom:DomNode<'a>) : DomNode<'a> =

          let attr = 
              [ attribute "title" text
                attribute "data-position" "right center"
                attribute "data-variation" "mini"] 
                  |> AttributeMap.ofList             
                
          onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
              dom.WithAttributes attr     
          )

      let wrapToolTipBottom (text:string) (dom:DomNode<'a>) : DomNode<'a> =

          let attr = 
              [ attribute "title" text
                attribute "data-position" "bottom center"
                attribute "data-variation" "mini"] 
                  |> AttributeMap.ofList              
                
          onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
              dom.WithAttributes attr     
          )