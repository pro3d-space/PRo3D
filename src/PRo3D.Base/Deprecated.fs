namespace Aardvark.UI

open System
open Aardvark.Base




namespace Aardvark.UI.Primitives

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI

module ColorPicker =
    
    type Action = SetColor of C4b

    let pickerStyle = { ColorPicker.PickerStyle.Default with showButtons = true }

    let viewAdvanced (defaultPalette : ColorPicker.Palette) (paletteFile : string) (localStorageKey : string) (darkTheme : bool) (model : AdaptiveColorInput) =
        ColorPicker.view 
            { ColorPicker.Config.Default with 
                localStorageKey = Some localStorageKey; 
                palette = Some defaultPalette; 
                darkTheme = darkTheme; 
                pickerStyle = Some pickerStyle 
            } SetColor model.c

    let view (model : AdaptiveColorInput) = 
        ColorPicker.view { ColorPicker.Config.Default with darkTheme = true; pickerStyle = Some pickerStyle }  SetColor model.c

    let update m (SetColor c) = 
        { m with c = c }

    let defaultPalette = ColorPicker.Palette.Default


namespace PRo3D.Core

module Config =
    
    let colorPaletteStore = ""