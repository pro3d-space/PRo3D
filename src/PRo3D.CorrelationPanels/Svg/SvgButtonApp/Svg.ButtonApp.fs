namespace Svgplus

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Button =
    open Aardvark.Base
    open Aardvark.Base.Incremental
    open Aardvark.UI
    open Svgplus.Base
    
    
    type Action =
    | OnLeftClick       of IMod<V2d>
    | OnRightClick      of IMod<V2d>
    | OnMouseDown       of (Aardvark.Application.MouseButtons * IMod<V2d>)
    | OnMouseUp         of (Aardvark.Application.MouseButtons * IMod<V2d>)
    | OnMouseEnter
    | OnMouseLeave
    | SetVisible        of bool
    
    module Lens =
        let posX = 
            {new Lens<Svgplus.Button, float>() with
              override x.Get(r)   = r.pos.X
              override x.Set(r,v) = 
                {r with pos = V2d (v, r.pos.Y)}
              //override x.Update(r,f) = 
              //  {r with pos =  V2d (f r.pos.X, r.pos.Y)}
            }
        
        let posY = 
            {new Lens<Svgplus.Button, float>() with
              override x.Get(r)   = r.pos.Y
              override x.Set(r,v) = 
                {r with pos = V2d (r.pos.X, v)}
              //override x.Update(r,f) = 
              //  {r with pos = V2d (r.pos.X, f r.pos.Y)}
            }
        
        let pos =
            {new Lens<Svgplus.Button, V2d>() with
              override x.Get(r)   = r.pos
              override x.Set(r,v) = 
                {r with pos = v}
              //override x.Update(r,f) = 
              //  {r with pos = f r.pos}
            }
    
    
    let init = {
        id             = ButtonId.newId ()
        pos            = V2d (0.0)
        radius         = 2.0
        rHoverChange   = 1.0
        stroke         = 1.0
        color          = C4b(44,127,184)
        colChange      = V3i(0,0,0)
        isToggled      = true
        fill           = true
        isHovering     = false
        transitionSec  = 0.5
    }
    
    let initc 
        (centre : V2d) 
        (radius : float)
        (margin : float) =

        let x = centre.X - radius + margin
        let y = centre.Y - radius + margin
        { init with pos = V2d (x,y) }
        
    let update (model : Button) (action : Action) =
        match action with
        | OnLeftClick  v -> 
            let newCol = (model.color -- model.colChange)
            { model with 
                isToggled  = (not model.isToggled)
                color      = newCol
                colChange  = -model.colChange
            }
        | OnRightClick  v -> model
        | OnMouseEnter -> 
            { model with 
                radius     = model.radius + model.rHoverChange
                isHovering = true
            }
        | OnMouseLeave  -> 
            { model with 
                radius     = model.radius - model.rHoverChange
                isHovering = false
            }
        | OnMouseDown (b,v) -> model
        | OnMouseUp   (b,v) -> model
        | SetVisible b -> model

    let view (model : MButton) = 
        let atts = 
            Attributes.Incremental.circle 
                model.pos model.color model.stroke model.radius model.fill
        
        let rightOrUp b pos = 
            match b with 
            | Aardvark.Application.MouseButtons.Right ->
                (OnRightClick pos)
            | _ -> 
                OnMouseUp (b, pos)
        
        let enter = Aardvark.UI.Events.onMouseEnter (fun _ -> OnMouseEnter)
        let exit  = Aardvark.UI.Events.onMouseLeave (fun _ -> OnMouseLeave)
        let down  = Aardvark.UI.Events.onMouseDown  (fun b v -> OnMouseDown (b, model.pos))
        let up    = Aardvark.UI.Events.onMouseUp    (fun b v -> rightOrUp    b  model.pos )

        let left  = 
            (Aardvark.UI.Svg.Events.onClickAttributes [(fun _ -> (OnLeftClick model.pos))]) 
            |> AMap.ofList
        
        let st = 
            [
                (GUI.CSS.Incremental.transition model.transitionSec)
            ] 
            |> GUI.CSS.Incremental.style
        
        let actions = [enter;exit;down;up] |> AMap.ofList
                        
        Incremental.circle' 
            ((AMap.union atts actions) |> AMap.union left |> AMap.union st)
          
    
