namespace UIPlus

open FSharp.Data.Adaptive
open Aardvark.Base
open Aardvark.UI
open UIPlus

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TextInput =


  type Action =
    | ChangeText of string
    | Enable
    | Disable
    | ChangeBgColor of C4b

  let init =  {
    version = TextInput.current
    text = ""
    disabled = false
    bgColor = C4b.VRVisGreen
    size = None
  }

  let update (model : TextInput) (action : Action) =
    match action with
      | ChangeText str -> {model with text = str}
      | Enable -> {model with disabled = false}
      | Disable -> {model with disabled = true}
      | ChangeBgColor c -> {model with bgColor = c}

  let view' (styleStr : aval<string>) (model : AdaptiveTextInput): DomNode<Action> = 
    let attr1 =
      amap {
        yield attribute "type" "text"
        let! st = styleStr
        yield style st
        yield onChange (fun str -> ChangeText str)
      }

    let attributes =
      amap {
        let! txt = model.text
        yield attribute "value" txt
      }
    div [clazz "ui icon input"] [(Incremental.input (AttributeMap.ofAMap (AMap.union attr1 attributes)))]
    //style "height: 1.4285em"



  let view'' (styleStr : string) (model : AdaptiveTextInput): DomNode<Action> = 
    let attr1 =
      amap {
        yield attribute "type" "text"
        //let! st = styleStr
        yield style styleStr
        yield onChange (fun str -> ChangeText str)
      }

    let attributes =
      amap {
        let! txt = model.text
        yield attribute "value" txt
      }
    div [clazz "ui icon input"] [(Incremental.input (AttributeMap.ofAMap (AMap.union attr1 attributes)))]

  let view (model : AdaptiveTextInput) : DomNode<Action> =
    view' (AVal.constant "") model  
     
  let app  = {
    unpersist = Unpersist.instance
    threads = fun _ -> ThreadPool.empty
    initial = init
    update = update
    view = view
  }

  let start () = App.start app
