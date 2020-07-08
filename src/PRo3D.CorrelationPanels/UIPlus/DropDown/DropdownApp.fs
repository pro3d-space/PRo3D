namespace UIPlus

open System
open Aardvark.Base
open FSharp.Data.Adaptive

open Aardvark.UI
open UIPlus
open UIPlus.DropdownType


// https://semantic-ui.com/modules/dropdown.html
//TODO use full dropdown markup for more flexible design
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DropdownList =
  type Action<'a> =
    | SetSelected of option<'a>
    | SetColor of C4b
    | SetList of IndexList<'a>

  let init<'a> : DropdownList<'a> = {
    valueList = plist.Empty
    selected = None
    color = C4b.Black
    searchable = true
  }

  let update (model : DropdownList<'a>) (action : Action<'a>) =
    match action with
      | SetSelected a -> {model with selected = a}
      | SetColor col -> {model with color = col}
      | SetList lst -> {model with valueList = lst}
    

  let view' (mDropdown      : MDropdownList<'a, _>)  
            (changeFunction : (option<'a> -> 'msg))
            (labelFunction  : ('a -> aval<string>))
            (getIsSelected  : ('a -> aval<bool>))  =
           

    let attributes (value : 'a) (name : string) =
      let notSelected = 
        (attribute "value" (AVal.force (labelFunction value)))
        
      let selAttr = (attribute "selected" "selected")
      let attrMap = 
          AttributeMap.ofListCond [
              always (notSelected )
              onlyWhen (getIsSelected value) (selAttr)
          ]
      attrMap
       
    

    let alistAttr  = 
      amap {
        let! attr = GUI.CSS.modColorToColorAttr mDropdown.color
        yield attr
        let! lst = (mDropdown.valueList.Content) 
        let callback (i : int) = lst
                              |> IndexList.tryAt(i) 
                              |> changeFunction
         
        yield (onEvent "onchange" 
                       ["event.target.selectedIndex"] 
                       (fun x -> 
                              x 
                                  |> List.head 
                                  |> Int32.Parse 
                                  |> callback)) 
      }

    Incremental.select (AttributeMap.ofAMap alistAttr)                        
      (
        alist {
          let domNode = 
              mDropdown.valueList
                |> AList.mapi(fun i x ->
                    Incremental.option 
                      (attributes x (AVal.force (labelFunction x))) 
                      (AList.ofList [Incremental.text (labelFunction x)]))
          yield! domNode
        }
      )


//  let app = { // @Thomas see view function; what about arguments?
//    unpersist = Unpersist.instance
//    threads = fun _ -> ThreadPool.empty
//    initial = init
//    update = update
//    view = view'
//  }

  //let start () = App.start app


