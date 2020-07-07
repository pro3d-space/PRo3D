namespace UIPlus

  open Aardvark.Base
  open Aardvark.Base.Incremental
  open Aardvark.UI

  module Labels =
    let textLabel (text : IMod<string>) =
      label [clazz "ui horizontal label"] [Incremental.text text]

    module Incremental = 
      let label (text : IMod<string>) (bgColour : IMod<C4b>) =
        let css =
          amap {
            yield clazz "ui horizontal label"
            yield! (GUI.CSS.incrBgColorAMap bgColour)
          } |> AttributeMap.ofAMap

        Incremental.label 
          css
          (AList.single (Incremental.text text))

      let labelCi (text : IMod<string>) (bgColour : MColorInput) =
        let css =
          amap {
            yield clazz "ui horizontal label"
            yield! (GUI.CSS.incrBgColorAMap bgColour.c)
          } |> AttributeMap.ofAMap

        Incremental.label 
          css
          (AList.single (Incremental.text text))