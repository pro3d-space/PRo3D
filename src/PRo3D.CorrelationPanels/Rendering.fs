namespace CorrelationDrawing
  open Aardvark.Base
  open Aardvark.Base.Incremental
  open Aardvark.Base.Rendering
  open Aardvark.UI
  open Aardvark.SceneGraph

  module Sg = 
    module Incremental =
      let path (close : bool) (points : alist<V3d>) = 
        adaptive {
          let! points = points.Content
          let points = points |> PList.toList
          let head = points  |> List.tryHead
          return 
            match head with
              | Some h -> 
                  if close then points @ [h] else points
                    |> List.pairwise
                    |> List.map (fun (a,b) -> new Line3d(a, b))
                    |> List.toArray
              | None -> [||]     
        }

      let polyline (points : alist<V3d>) (color : IMod<C4b>) (weight : IMod<float>) =
        (path false points)
          |> Sg.lines color
          |> Sg.effect [
              toEffect DefaultSurfaces.trafo
              toEffect DefaultSurfaces.vertexColor
              toEffect DefaultSurfaces.thickLine                                
              ] 
          |> Sg.noEvents
          |> Sg.uniform "LineWidth" weight
          |> Sg.pass (RenderPass.after "lines" RenderPassOrder.Arbitrary RenderPass.main)
          |> Sg.depthTest (Mod.constant DepthTestMode.None)  


    let sphereDyn (color : IMod<C4b>) (size : IMod<float>) =
      Sg.sphere 3 color size 
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! DefaultSurfaces.vertexColor
            do! DefaultSurfaces.simpleLighting
        }
        |> Sg.noEvents    
    
    let sphereWithEvents color size (events : List<SceneEventKind * (SceneHit -> bool * seq<'msg>)>)  =      
      Sg.sphere 3 color size 
              |> Sg.shader {
                  do! DefaultSurfaces.trafo
                  do! DefaultSurfaces.vertexColor
                  do! DefaultSurfaces.simpleLighting
              }
              |> Sg.requirePicking
              |> Sg.noEvents
              |> Sg.withEvents events
    
    let pathDyn (close : bool) (points : List<V3d>) = 
      let head = points |> List.tryHead
      match head with
        | Some h -> 
            if close then points @ [h] else points
              |> List.pairwise
              |> List.map (fun (a,b) -> new Line3d(a, b)) //Mod.map2 (fun a b -> new Line3d(a, b)) a b)
              |> List.toArray
        | None -> [||]     
      

    let makeCylinderSg (c : C4b) =      
      Sg.cylinder' 3 c 0.1 20.0 
              |> Sg.shader {
                  do! DefaultSurfaces.trafo
                  do! DefaultSurfaces.vertexColor
                  do! DefaultSurfaces.simpleLighting
              }
              |> Sg.noEvents