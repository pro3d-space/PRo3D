namespace Aardvark.UI

open System
open Aardvark.Base




namespace Aardvark.UI.Primitives

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI

module ColorPicker =
    
    type Action = SetColor of C4b

    let viewAdvanced (defaultPalette : ColorPicker.Palette) (paletteFile : string) (localStorageKey : string) (model : AdaptiveColorInput) =
        ColorPicker.view { ColorPicker.Config.Default with localStorageKey = Some localStorageKey; palette = Some defaultPalette } SetColor model.c

    let view (model : AdaptiveColorInput) = 
        ColorPicker.view ColorPicker.Config.Default SetColor model.c

    let update _ _ = failwith ""

    let defaultPalette = ColorPicker.Palette.Default


namespace PRo3D.Core

module Config =
    
    let colorPaletteStore = ""