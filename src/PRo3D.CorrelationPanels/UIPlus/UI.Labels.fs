namespace UIPlus

  open Aardvark.Base
  open FSharp.Data.Adaptive
  open Aardvark.UI

  module Labels =
    let textLabel (text : aval<string>) =
      label [clazz "ui horizontal label"] [Incremental.text text]

    module Incremental = 
      let label (text : aval<string>) (bgColour : aval<C4b>) =
        let css =
          amap {
            yield clazz "ui horizontal label"
            yield! (GUI.CSS.incrBgColorAMap bgColour)
          } |> AttributeMap.ofAMap

        Incremental.label 
          css
          (AList.single (Incremental.text text))

      let labelCi (text : aval<string>) (bgColour : AdaptiveColorInput) =
        let css =
          amap {
            yield clazz "ui horizontal label"
            yield! (GUI.CSS.incrBgColorAMap bgColour.c)
          } |> AttributeMap.ofAMap

        Incremental.label 
          css
          (AList.single (Incremental.text text))