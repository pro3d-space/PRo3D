namespace PRo3D.Core.Drawing

open System
open Adaptify.FSharp.Core
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core


module AnnotationBulkEdit = 

    type Action =         
    | SetSemantic     of Semantic
    | ChangeThickness of Numeric.Action
    | ChangeColor     of ColorPicker.Action
    | SetText         of string
    | SetTextSize     of Numeric.Action
    | ToggleVisible
    | ToggleShowDns

    let view (model : AdaptiveAnnotation) = 

        require GuiEx.semui (
            Html.table [                                                         
                Html.row "Semantic:"    [Html.SemUi.dropDown model.semantic SetSemantic]      
                Html.row "Thickness:"   [Numeric.view' [InputBox] model.thickness |> UI.map ChangeThickness ]
                Html.row "Color:"       [ColorPicker.view model.color |> UI.map ChangeColor ]                
                Html.row "TextSize:"    [Numeric.view' [InputBox] model.textsize |> UI.map SetTextSize ]
                Html.row "Visible:"     [GuiEx.iconCheckBox model.visible ToggleVisible ]
                Html.row "Show DnS:"    [GuiEx.iconCheckBox model.showDns ToggleShowDns ]
            ]
        )