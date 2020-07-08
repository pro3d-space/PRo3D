namespace Svgplus

open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.UI

open Svgplus.Base
open Svgplus.RectangleType

open UIPlus

open PRo3D.Base
open CorrelationDrawing

type RectangleAction =    
    | Select          of RectangleId
    | Deselect        
    | OnMouseEnter
    | OnMouseLeave
    | UpdateColour    of (Rectangle -> Rectangle)
    | SetWidth        of float
    | SetGrainSize    of UIPlus.GrainSizeInfo
    | SetUncertainty  of bool
    | Nope

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Rectangle =

    module Lens =
        let width = 
            { new Lens<Rectangle, float>() with
                override x.Get(r) =
                    match r.fixedWidth with
                    | Some w -> w
                    | None   -> r.dim.width
                override x.Set(r,v) =
                    { r with
                        dim = {r.dim with width = v}
                    }
            }

        let grainSize = 
            { new Lens<Rectangle, UIPlus.GrainSizeInfo>() with
            override x.Get(r) = r.grainSize
            override x.Set(r,v) =
                let svgX = v.displayWidth
                let updatedRect = r |> Lenses.set width svgX
                // SUPER BAD! this fixes also rect-width!!
                { updatedRect with grainSize = v }
            }
        
        let height = 
            { new Lens<Rectangle, float>() with
                override x.Get(r)   = r.dim.height
                override x.Set(r,v) = 
                    { r with 
                        dim = { r.dim with height = v }
                    }
            }
        
        let posX = 
            {new Lens<Rectangle, float>() with
                override x.Get(r)   = r.pos.X
                override x.Set(r,v) =
                    { r with
                        pos = V2d (v, r.pos.Y) 
                    }
            }
        
        let posY = 
            { new Lens<Rectangle, float>() with
                override x.Get(r)   = r.pos.Y
                override x.Set(r,v) = 
                    { r with 
                        pos = V2d (r.pos.X, v)
                    }
            }
        
        let pos =
            { new Lens<Rectangle, Aardvark.Base.V2d>() with
                override x.Get(r) = V2d (posX.Get r, posY.Get r)
                override x.Set(r,v) =
                    let _r = posX.Set (r,v.X)
                    posY.Set (_r,v.Y)
            }

    let init id = 
        let rect = 
            {
                faciesId          = System.Guid.Empty
                id                = id
                fixedInfinityHeight = None
               
                pos               = V2d (0.0)
                dim               = { width = 50.0; height = 100.0 }
                worldHeight       = 0.0
                fixedWidth        = None
                colour            = { c = C4b.White }
                isUncertain     = true

                isSelected      = false
                isHovering      = false
                
                grainSize = {
                        grainType = GrainType.Unknown
                        middleSize = 0.003
                        displayWidth = 125.0
                    }
            } 
        
        rect
    
    let update (model : Rectangle) (action : RectangleAction) =
        match action with
        | Select id ->
            match model.isSelected with
            | true -> model
            | false -> { model with isSelected = true }
        | Deselect ->
            match model.isSelected with
            | false -> model
            | true -> { model with isSelected = false }
        | OnMouseEnter ->
            { model with isHovering = true }
        | OnMouseLeave ->
            { model with isHovering = false }
        | UpdateColour f -> 
            f model
        | SetWidth w -> 
            Lens.width.Set (model, w)
        | SetGrainSize s ->
            Lens.grainSize.Set (model, s)
        | SetUncertainty b -> 
            { model with isUncertain = b }  
        | Nope -> model

    let view (model : AdaptiveRectangle) =
        alist {
            let! dim  = model.dim
            let! optWidth = model.fixedWidth
            let dim = 
                match optWidth with
                | Some w -> {dim with width = w}
                | None   -> dim

            let! col  = model.colour.c
            let! isSelected  = model.isSelected
            let! isUncertain   = model.isUncertain     
            let! pos  = model.pos
                
            let pos = V2d (pos.X + 67.0, pos.Y)  // MAGIX label width

            let callback = 
                (fun _ -> 
                    match model.isSelected.GetValue() with
                    |true -> Deselect 
                    |false -> Select model.id)

            let right = Aardvark.UI.Events.onMouseUp (fun b _ -> 
                    match b with
                    | Aardvark.Application.MouseButtons.Right -> (SetUncertainty (not (model.isUncertain.GetValue())))
                    | _ -> Nope)
                
            let left = Aardvark.UI.Svg.Events.onClickAttributes [callback]

            let borderedRect = 
                Svgplus.Base.drawBorderedRectangle
                    pos dim col                      
                    SvgWeight.init
                    (List.append left [right])
                    isSelected 
                    isUncertain

            yield! AList.ofList borderedRect                                                         
        }

    let viewBorder (border : AdaptiveRectangleBorder) (rectangles : amap<RectangleId, AdaptiveRectangle>) (selection : bool) (onClickAction  : _ -> 'msg) =
        adaptive {
            
            let! lower = 
                rectangles 
                |> AMap.tryFind border.lowerRectangle

            let! upper = 
                rectangles 
                |> AMap.tryFind border.upperRectangle         
         
            let pos,width =
                (lower, upper) 
                ||> Option.map2(fun a b -> a,b) 
                |> Option.map(fun (l,u) -> 
                    let pos = 
                        l.pos 
                        |> AVal.map(fun y -> V2d(y.X + 67.0, y.Y)) // MAGIC label width
                    
                    let w = //length of wider bordering rectangle
                        AVal.map2(fun a b -> max a.width b.width) u.dim l.dim

                    (pos, w)
                )
                |> Option.defaultValue (~~V2d.Zero, ~~0.0)

            let! pos = pos
            let! width = width

            let! color = border.color

            if selection then
                return drawHorizontalLine 
                    pos
                    (width * 1.01)
                    C4b.VRVisGreen 
                    (SvgWeight.init.value * 4.0)
                    onClickAction
            else

                return drawHorizontalLine 
                    pos
                    width
                    color 
                    SvgWeight.init.value 
                    onClickAction
        }