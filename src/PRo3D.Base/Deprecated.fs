namespace Aardvark.UI

open System
open Aardvark.Base
open Adaptify

[<ModelType;Obsolete>]
type ColorInput = {
    c : C4b
}

module ColorPicker =
    let viewAdvanced _ _ _ _ = failwith ""

    let view _ = failwith ""

    let update _ _ = failwith ""

    let defaultPalette<'a> = failwith ""

    type Action = unit



namespace Aardvark.UI.Primitives

module ColorPicker =
    let viewAdvanced _ _ _ _ = failwith ""

    let view _ = failwith ""

    let update _ _ = failwith ""

    let defaultPalette<'a> = failwith ""

    type Action = unit