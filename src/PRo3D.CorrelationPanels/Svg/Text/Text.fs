namespace Svgplus

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open UIPlus
open Svgplus
open Svgplus.TextType
open Attributes

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Text =
    
    type Action =
    | MouseMessage      of MouseAction
    | ChangeLabel       of TextInput.Action
    
    let init : Text =
        {
            centre        = V2d.OO
            dim           = {width = 50.0; height = 5.0}
            textInput     = {TextInput.init with text = "LOG_X"}
            bold          = false
            fontSize      = FontSize.defaultSize
            onEnter       = (fun a -> {a with bold = true})
            onLeave       = (fun a -> {a with bold = false})        
        }
    
    let init' text : Text =
        {init with textInput = {TextInput.init with text = text}}
    
    let preferredWidth (model : Text) =
        let fs = float model.fontSize.fontSize
        let nrchars = float model.textInput.text.Length
        1.0 + (fs * 1.5) + (fs * 0.5 * nrchars)
    
    let preferredHeight (model : Text) =
        let fs = float model.fontSize.fontSize
        fs + 2.0
    
    let update (model : Text) (action : Action) =
        match action with
        | ChangeLabel  m ->
            let _textInput = TextInput.update model.textInput m
            {model with textInput = _textInput}
        | MouseMessage m ->
            match m with
            | MouseAction.OnMouseEnter -> model.onEnter model
            | MouseAction.OnMouseLeave -> model.onLeave model
            | _ -> model
    
    let view (model : AdaptiveText) =
        let bold = 
            amap {
                yield (Svgplus.Attributes.ats "text-anchor" "middle")
                yield (Svgplus.Attributes.ats "dy" ".4em")
                yield (Svgplus.Attributes.ats "font-size" "larger")
                let! b = model.bold  
                match b with
                | true ->
                    yield (Svgplus.Attributes.ats "font-weight" "bold")
                | false ->
                    yield (Svgplus.Attributes.ats "font-weight" "normal")
            } |> Aardvark.UI.AttributeMap.ofAMap
        
        let clickable =
            Svgplus.Incremental.clickableRectangle' 
                model.centre 
                model.dim 
                (MouseActions.init ())
        
        let atts = 
            amap {
                let! centre = model.centre
                let! dim = model.dim 
                yield atf "x" centre.X
                yield atf "y" centre.Y
                yield atf "width"  dim.width
                yield atf "height" dim.height
            }
            |> Aardvark.UI.AttributeMap.ofAMap
            |> Aardvark.UI.AttributeMap.union bold
        
        let label = 
            (Aardvark.UI.Incremental.Svg.text atts model.textInput.text)
            |> UI.map Action.ChangeLabel
        
        let content =
          clickable 
          |> UI.map MouseMessage
          |> AList.single
          |> AList.append (AList.single label)

        content
      