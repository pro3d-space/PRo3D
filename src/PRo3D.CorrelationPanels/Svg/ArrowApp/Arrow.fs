namespace Svgplus

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI
open CorrelationDrawing
open Svgplus.ArrowType
open Svgplus.Paths


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Arrow =

  type Action =
    | MouseMessage of MouseAction

  let init (direction : Direction) : Arrow =
    let arrow = 
      {
        centre  = V2d(100.0)
        direction = direction
        length    = 80.0
        height    = 40.0
        horz      = 0.5
        vert      = 0.5
        stroke    = 5.0
        fill      = false
        colour    = C4b.Black
        onEnter   = (fun a -> {a with fill = true})
        onLeave   = (fun a -> {a with fill = false})
      }
    arrow

  let update (model : Arrow) (action : Action) =
    match action with
      | MouseMessage m -> 
        match m with
          | MouseAction.OnMouseEnter ->
            model.onEnter model
          | MouseAction.OnMouseLeave ->
            model.onLeave model
          | _ -> model
          

  let arrowPath (centre : V2d) 
                (length : float) 
                (height : float)
                (vert   : float) 
                (horz  : float) =
    let leftUpper = 
      V2d(centre.X - length * 0.5, centre.Y - height * vert * 0.5) 
    let shaftRightUpper = 
      V2d(leftUpper.X + length * horz, leftUpper.Y)
    let headUpper =
      V2d(shaftRightUpper.X, centre.Y - height * 0.5)
    let tip =
      V2d(centre.X + length * 0.5, centre.Y)
    let headLower =
      V2d(shaftRightUpper.X, centre.Y + height * 0.5)
    let shaftRightLower = 
      V2d(shaftRightUpper.X, centre.Y + height * vert * 0.5)
    let shaftLeftLower =
      V2d(centre.X - length * 0.5, shaftRightLower.Y) 

    let path =
      move leftUpper
        >> lineTo shaftRightUpper
        >> lineTo headUpper
        >> lineTo tip
        >> lineTo headLower
        >> lineTo shaftRightLower
        >> lineTo shaftLeftLower
        >> close
    path
    


  let view (model : MArrow) =
    let actions = MouseActions.init ()

    let arrow = 
      alist {
        let! centre = model.centre  
        let! length = model.length
        let! height = model.height
        let! vert = model.vert
        let! horz = model.horz        
        let path = arrowPath centre length height vert horz
        let! col = model.colour
        let! fill = model.fill
        let! stroke = model.stroke
        let! dir = model.direction
        let degrees = 
          match dir with  
            | Direction.Right  -> 0
            | Direction.Up    -> 90
            | Direction.Left -> 180
            | Direction.Down  -> 270

        yield (buildPathRotate path col centre stroke fill degrees)
      }

    let clickable =
      Svgplus.Incremental.clickableRectangle model.centre 
                                             model.length 
                                             model.height 
                                             actions 
                                             AList.empty
    clickable |> UI.map MouseMessage
              |> AList.single
              |> AList.append arrow


         
      
      
      
      