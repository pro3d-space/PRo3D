namespace Svgplus

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open UIPlus
open Attributes        
open Svgplus
open Svgplus.HeaderType
open Svgplus.TextType
open CorrelationDrawing

type HeaderAction =
    | LeftArrowMessage  of Arrow.Action
    | RightArrowMessage of Arrow.Action
    | MouseMessage      of MouseAction
    | TextMessage       of Text.Action

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HeaderApp =
      
    let layout (fixedDims : bool) (model : Header)  =
        let margin = 1.0
        let buttonWidth = 20.0
        
        let (height, width) =
            match fixedDims with
            | true ->
                let height = model.dim.height
                let width  = model.dim.width
                (height, width)
            | false ->
                let height = (Text.preferredHeight model.label)
                let width  = (Text.preferredWidth model.label)
                (height, width + 2.0 * buttonWidth)
        
        let halfWidth           = width * 0.5
        let leftCentre          = model.centre - V2d(halfWidth, 0.0)
        let rightCentre         = model.centre + V2d(halfWidth, 0.0)
        //let buttonWidth         = width * 0.2
        let leftButtonCentre    = leftCentre  + V2d(buttonWidth * 0.5, 0.0)
        let rightButtonCentre   = rightCentre - V2d(buttonWidth * 0.5, 0.0)
        
        let labelwidth          = width - buttonWidth - buttonWidth 
        
        let leftButton = { 
            model.leftButton with 
                height = height
                centre = leftButtonCentre
                length = buttonWidth * 0.8
        }
        
        let rightButton = { 
            model.rightButton with 
                height = height
                centre = rightButtonCentre
                length = buttonWidth * 0.8
        }
        
        let label = { 
            model.label with
                centre = model.centre
                dim    = {
                    width  = labelwidth
                    height = height
                }
            }
        
        {
            model with
                dim         = {height = height; width = width}
                leftButton  = leftButton
                rightButton = rightButton
                label       = label
        }
    
    let init : Header =
          
        let right = Arrow.init Direction.Right
        let left  = Arrow.init Direction.Left
        {
            centre        = V2d.OO
            dim           = {width = 100.0; height = 50.0}
            label         = Text.init
            leftButton    = left
            rightButton   = right
            visible       = true
        }
    
    let centreToPos r =
        V2d(r.centre.X - r.dim.width * 0.5, r.centre.Y - r.dim.height * 0.5)
    
    let posToCentre r (v : V2d) =
        V2d(v.X + r.dim.width * 0.5, v.Y - r.dim.height * 0.5)
    
    module Lens =
        let pos =
            { new Lens<Header, Aardvark.Base.V2d>() with
                override x.Get(r) = (centreToPos r.label) 
                override x.Set(r,v) = {r with centre = posToCentre r.label v} |> (layout false)
                //override x.Update(r,f) = {r with centre = f r.centre} |> (layout false)
            }
        let centre =
            { new Lens<Header, Aardvark.Base.V2d>() with
                override x.Get(r) = r.centre
                override x.Set(r,v) = {r with centre = v} |> (layout false)
                //override x.Update(r,f) = {r with centre = f r.centre} |> (layout false)
            }
        let dim =
            { new Lens<Header, Size2D>() with
                override x.Get(r) = r.dim
                override x.Set(r,v) = {r with dim = v} |> (layout true)
                //override x.Update(r,f) = {r with dim = f r.dim} |> (layout true)
            }
        let width =
            { new Lens<Header, float>() with
                override x.Get(r) = r.dim.width
                override x.Set(r,v) = 
                  {r with dim = {width = v;height = r.dim.height}}
                //override x.Update(r,f) = 
                //  {r with dim = {width = f r.dim.width; height = r.dim.height}} |> (layout true)
            }
        let height =
            { new Lens<Header, float>() with
                override x.Get(r) = r.dim.height
                override x.Set(r,v) = 
                  {r with dim = {height = v;width = r.dim.width}}
                //override x.Update(r,f) = 
                //  {r with dim = {height = f r.dim.width; width = r.dim.width}} |> (layout true)
            }
    
    let update (model : Header) (action : HeaderAction) =
        match action with
        | MouseMessage m -> model
        | LeftArrowMessage m ->
            {model with leftButton = Arrow.update model.leftButton m}
        | RightArrowMessage m ->
            {model with rightButton = Arrow.update model.rightButton m}
        | TextMessage m ->
            {model with label = Text.update model.label m} |> (layout false)
    
    let view (model : AdaptiveHeader) =
        let left  = Arrow.view model.leftButton
        let label = Text.view model.label 
        let right = Arrow.view model.rightButton
        
        alist {
            let! vis = model.visible
            if vis then
                yield! (left  |> AList.map (UI.map HeaderAction.LeftArrowMessage))
                yield! (label |> AList.map (UI.map HeaderAction.TextMessage))
                yield! (right |> AList.map (UI.map HeaderAction.RightArrowMessage))
        }

