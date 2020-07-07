namespace UIPlus
open Aardvark.UI
open Aardvark.Base
open UIPlus.Tables

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ColourMapItem =
    type Action =
        | ColourMessage of ColorPicker.Action
      //| TextMessage   of TextInput.Action
      //| NumericMessage of Numeric.Action

    let update (model : ColourMapItem) (action : Action) =
        match action with
        | ColourMessage m -> 
              let _c = ColorPicker.update model.colour m
              {model with colour = _c}
        //| TextMessage m   ->
        //  let _t = TextInput.update model.label m
        //  {model with label = _t}
        //| NumericMessage m -> 
        //  let _n = Numeric.update model.upper m
        //  {model with upper = _n}

    let view (model : MColourMapItem)  = 
       [
           (div [] [Incremental.text model.label]) |> intoLeftAlignedTd
           ColorPicker.view model.colour |> intoTd |> UI.map ColourMessage
           (div [] [Incremental.text model.upperStr])|> intoLeftAlignedTd
       ]