namespace CorrelationDrawing

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open System
open Aardvark.Base.Incremental
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering.Text
open Contact
open UIPlus

open PRo3D.Base.Annotation

open CorrelationDrawing.Types
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.SemanticTypes
open CorrelationDrawing.XXX

module CorrelationDrawing =    

    let initial : CorrelationDrawingModel = 
      let keyboard = Keyboard.init ()
      let onEnter model = 
        match model.working with
          | Some w  ->
              {model with working   = None}
          | None   -> model
      let onDelete model = 
        {model with 
            working   = None
            hoverPosition = None 
        }
      let _keyboard =
            keyboard
              |> (Keyboard.registerKeyDown
                    {
                      update = onEnter
                      key    = Keys.Enter
                      ctrl   = false
                      alt    = false
                    })
              |> (Keyboard.registerKeyDown
                    {
                      update = onDelete
                      key    = Keys.Delete
                      ctrl   = false
                      alt    = false
                    })

      {
        keyboard = _keyboard
        hoverPosition = None
        working = None
        projection = Projection.Viewpoint
        exportPath = @"."
      }

    type Action =
        | Clear
        | DoNothing
        | AnnotationMessage       of Contact.Action
        | KeyboardMessage         of Keyboard.Action
        | SetGeometry             of Geometry
        | SetProjection           of Projection
        | SetExportPath           of string
        | Move                    of V3d
        | Exit    
        | AddPoint                of V3d
        | ToggleSelectPoint       of (string * V3d)
        | DeselectAllPoints       
        | SelectPoints            of List<(V3d * string)>
        | KeyDown                 of key : Keys
        | KeyUp                   of key : Keys      
        | Export
           
    let isDone (model : CorrelationDrawingModel) (semApp : SemanticsModel) =
      let selSem = SemanticApp.getSelectedSemantic semApp
      match model.working with
        | Some w -> 
          match (selSem.geometryType, (w.points |> PList.count)) with
                                  | Geometry.Point, 0 ->  true
                                  | Geometry.Line,  1 ->  true
                                  | _                     ->  false
        | None -> 
          match selSem.geometryType with
            | Geometry.Point -> true
            | _ -> false

    let newPoint p = { point = p; selected = false}  
    let addPoint  (model : CorrelationDrawingModel) (semanticApp : SemanticsModel) (point : V3d) =
      let geometryType = (SemanticApp.getSelectedSemantic semanticApp).geometryType
      let working = 
        match model.working with
        | Some w ->
          {
            w with 
              points   = w.points |> PList.append (newPoint point)
              geometry = geometryType
          } |> Some
          
        | None ->             
          {
            Contact.createNewContact geometryType with
              points        = PList.single (newPoint point)
              semanticId    = semanticApp.selectedSemantic                
              projection    = model.projection
          } |> Some //add annotation states
            
      { model with working = working }

    let update (model : CorrelationDrawingModel) (semanticApp : SemanticsModel) (act : Action)  =
        match (act, model.isDrawing) with
            | Clear, _         ->
                {model with working = None
                }
            | DoNothing, _             -> 
                model

            | Move p, true -> 
                { model with hoverPosition = Some (Trafo3d.Translation p) }
            | AddPoint m, true         -> 
                match isDone model semanticApp with
                  | true               -> 
                    let model = addPoint model semanticApp m
                    {model with working       = None}
                  | false  -> addPoint model semanticApp m             
            | KeyboardMessage m, _ -> 
               let (keyboard, model) = Keyboard.update model.keyboard model m
               {model with keyboard = keyboard}
            | Exit, _                 -> 
                    { model with hoverPosition = None }
            | SetGeometry mode, _     -> model
                    //{ model with geometry = mode }
            | SetProjection mode, _   ->
                    { model with projection = mode }        
            | SetExportPath s, _      ->
                    { model with exportPath = s }
            | Export, _               ->
                    //let path = Path.combine([model.exportPath; "drawing.json"])
                    //printf "Writing %i annotations to %s" (model.annotations |> PList.count) path
                    //let json = model.annotations |> PList.map JsonTypes.ofAnnotation |> JsonConvert.SerializeObject
                    //Serialization.writeToFile path json 
                    
                    model
            | _ -> model

 ///////////// MARS
    //let sky = Mars.Terrain.up
    //let patchBB = Mars.Terrain.CapeDesire.patchBB()



    //|> Sg.andAlso terrain

 //////////////////
    module Sg =        

        
        let computeScale (view : IMod<CameraView>) (p:IMod<V3d>) (size:float) =
            adaptive {
                let! p = p
                let! v = view
                let distV = p - v.Location
                let distF = V3d.Dot(v.Forward, distV)
                return distF * size / 800.0 //needs hfov at this point
            }

            
        let makeBrushSg (hovered : IMod<Trafo3d option>) (color : IMod<C4b>) = //(size : IMod<float>)= 
            let trafo =
                hovered |> Mod.map (function o -> match o with 
                                                    | Some t-> t
                                                    | None -> Trafo3d.Scale(V3d.Zero))
            Sg.sphereDyn (color) (Mod.constant 0.05) |> Sg.trafo(trafo) // TODO hardcoded stuff
       
        let view (model         : MCorrelationDrawingModel)
                 (semanticApp   : MSemanticsModel) 
                 (cam           : IMod<CameraView>) 
                 (sgFlags       : IMod<SgFlags>) =      

          let marsSg =
            sgFlags 
              |> Mod.map 
                (fun flags ->
                  let events = 
                    [
                        Sg.onMouseMove (fun p -> (Action.Move p))
                        Sg.onClick(fun p -> Action.AddPoint p)
                        Sg.onLeave (fun _ -> Action.Exit)
                    ]
                  match Flags.isSet SgFlags.TestTerrain flags with
                    | true ->
                      Mars.Terrain.Test.dummyMars events
                    | false ->
                      Mars.Terrain.CapeDesire.getRealMars events
                ) |> Sg.dynamic
          let sgWorking = 
            
            model.working |> Mod.map (fun x ->
              match x with
                | Some a -> (Contact.Sg.view a cam semanticApp true) 
                              |> ASet.map (fun sg -> sg |> Sg.map AnnotationMessage)
                              |> Sg.set
                | None   -> Sg.ofList [])
              |> Sg.dynamic
          [
            marsSg
            sgWorking
            makeBrushSg model.hoverPosition (SemanticApp.getColor semanticApp semanticApp.selectedSemantic);
          ]
            
            
            
