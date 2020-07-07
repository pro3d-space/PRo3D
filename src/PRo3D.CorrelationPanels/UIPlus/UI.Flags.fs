namespace UIPlus

    open Aardvark.Base
    open Aardvark.UI
    module Flags =
      open CorrelationDrawing

      let toButtons (t : System.Type) (toggleAction : 'a -> 'msg) =
        let names = System.Enum.GetNames(t)
        seq {
          for str in names.[1..names.Length-1] do
            let e = Flags.parse t str
            yield (UIPlus.Buttons.toggleButton str (fun p -> toggleAction e)) //|> UI.map CorrelationPlotMessage
        } |> List.ofSeq

      let toButtonGroup (t : System.Type) (toggleAction : 'a -> 'msg) =
        div 
          [style "padding: 2px 2px 2px 2px"] 
          (toButtons t toggleAction)