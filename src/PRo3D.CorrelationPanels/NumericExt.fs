namespace Aardvark.UI

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Numeric
open Combinators

//extension so attributes can be specified
module Numeric = 
    let inline (=>) a b = Attributes.attribute a b

    let onWheel' (f : Aardvark.Base.V2d -> seq<'msg>) =
      let serverClick (args : list<string>) : Aardvark.Base.V2d = 
          let delta = List.head args |> Pickler.unpickleOfJson
          delta  / Aardvark.Base.V2d(-100.0,-100.0) // up is down in mouse wheel events
      
      onEvent' "onwheel" ["{ X: event.deltaX.toFixed(10), Y: event.deltaY.toFixed(10)  }"] (serverClick >> f)

    // let numericField'<'msg> ( f : Action -> seq<'msg> ) ( atts : AttributeMap<'msg> ) ( model : MNumericInput ) inputType =         
    let numericField'<'msg> ( f : Action -> seq<'msg> )
                            ( atts : AttributeMap<'msg> ) 
                            ( model : MNumericInput ) inputType =         

      let tryParseAndClamp min max fallback (s: string) =
          let parsed = 0.0
          match Double.TryParse(s, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
              | (true,v) -> clamp min max v
              | _ ->  printfn "validation failed: %s" s
                      fallback

      let attributes = 
          amap {                                

              let! min = model.min
              let! max = model.max
              let! value = model.value
              match inputType with
                  | Slider ->   
                      yield "type" => "range"
                      yield onInput' (tryParseAndClamp min max value >> SetValue >> f)   // continous updates for slider
                  | InputBox -> 
                      yield "type" => "number"
                      yield onChange' (tryParseAndClamp min max value >> SetValue >> f)  // batch updates for input box (to let user type)

              let! step = model.step
              yield onWheel' (fun d -> value + d.Y * step |> clamp min max |> SetValue |> f)

              yield "step" => sprintf "%f" step
              yield "min"  => sprintf "%f" min
              yield "max"  => sprintf "%f" max

              let! format = model.format
              yield "value" => formatNumber format value
          } 

      Incremental.input (AttributeMap.ofAMap attributes |> AttributeMap.union atts)


    let view'' (inputType : NumericInputType)
               (model : MNumericInput)
               (attributes : AttributeMap<Action>) :  DomNode<Action> =
        div [][(numericField' (Seq.singleton) attributes model inputType)] //(numericField (Seq.singleton) attributes model inputTypes )

      
