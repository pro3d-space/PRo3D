namespace CorrelationDrawing

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Base.Rendering
open Aardvark.UI
open UIPlus
open UIPlus.Tables

open PRo3D.Base.Annotation

open CorrelationDrawing
open CorrelationDrawing.SemanticTypes
open CorrelationDrawing.Types


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CorrelationSemantic = 

  module Lens = 
    let _color     : Lens<CorrelationSemantic,C4b>    = CorrelationSemantic.Lens.color     |. ColorInput.Lens.c
    let _thickness : Lens<CorrelationSemantic,float>  = CorrelationSemantic.Lens.thickness |. NumericInput.Lens.value
    let _labelText : Lens<CorrelationSemantic,string> = CorrelationSemantic.Lens.label     |. TextInput.Lens.text

  [<Literal>]
  let ThicknessDefault = 1.0

  //let LEVELS = [0;1;2;3;4;5;6;7;8]

  let initial id = {
      version       = CorrelationSemantic.current
      id            = id |> CorrelationSemanticId
      timestamp     = Time.getTimestamp
      state         = State.New
      label         = TextInput.init      
      color         = { c = C4b.Red }
      thickness     = 
            {
              Numeric.init with 
                value  = ThicknessDefault
                min    = 0.5
                max    = 10.0
                step   = 0.5 
                format = "{0:0.0}"
            }        
      semanticType  = SemanticType.Metric
      geometryType  = Geometry.Line
      level         = NodeLevel.init 0
  }

  let initInvalid = 
    initial (CorrelationSemanticId.invalid |> CorrelationSemanticId.value)
    

  /////// DEFAULT SEMANTICS
  let initialHorizon0 id = 
      {
          initial id with 
              label         = {TextInput.init with text = "Horizon0"}
              color         = {c = new C4b(37,52,148)}
              thickness     = {Numeric.init with value = 6.0}
              semanticType  = SemanticType.Hierarchical
              geometryType  = Geometry.Polyline
              level         = NodeLevel.init 0
      }

  let initialHorizon1 id = {
    initial id with 
      label         = {TextInput.init with text = "Horizon1"}
      color      = {c = new C4b(44,127,184)}
      thickness  = {Numeric.init with value = 5.0}
      semanticType  = SemanticType.Hierarchical
      geometryType  = Geometry.Polyline
      level         = NodeLevel.init 1
    }

  let initialHorizon2 id = {
    initial id with 
      label         = {TextInput.init with text = "Horizon2"}
      color      = {c = new C4b(65,182,196)}
      thickness  = {Numeric.init with value = 4.0}
      semanticType  = SemanticType.Hierarchical
      geometryType  = Geometry.Polyline
      level         = NodeLevel.init 2
    }

  let initialHorizon3 id = {
    initial id with 
      label         = {TextInput.init with text = "Horizon3"}
      color      = {c = new C4b(127,205,187)}
      thickness  = {Numeric.init with value = 3.0}
      semanticType  = SemanticType.Hierarchical
      geometryType  = Geometry.Polyline
      level         = NodeLevel.init 3
    }

  let initialHorizon4 id = {
    initial id with 
      label         = {TextInput.init with text = "Horizon4"}
      color      = {c = new C4b(199,233,180)}
      thickness  = {Numeric.init with value = 2.0}
      semanticType  = SemanticType.Hierarchical
      geometryType  = Geometry.Polyline
      level         = NodeLevel.init 4
    }


  let initialGrainSize id = {
    initial id with 
      label         = {TextInput.init with text = "Grainsize"}
      color      = {c = new C4b(252,141,98)}
      thickness  = {Numeric.init with value = 1.0}
      semanticType  = SemanticType.Metric
      geometryType  = Geometry.Line
      level         = NodeLevel.invalid
    }

  let initialGrainSize2 id = {
    initial id with 
      label         = {TextInput.init with text = "Grainsize"}
      color      = {c = new C4b(247,252,185)}
      thickness  = {Numeric.init with value = 1.0}
      semanticType  = SemanticType.Metric
      geometryType  = Geometry.Line
      level         = NodeLevel.invalid
    }

  let initialCrossbed id = {
    initial id with 
      label         = {TextInput.init with text = "Crossbed"}
      color      = {c = new C4b(231,138,195)}
      thickness  = {Numeric.init with value = 1.0}
      semanticType  = SemanticType.Angular
      geometryType  = Geometry.Line
      level         = NodeLevel.invalid
    }

  let impactBreccia id = {
    initial id with 
      label         = {TextInput.init with text = "Impact Breccia"}
      color      = {c = new C4b(166,216,84)}
      thickness  = {Numeric.init with value = 1.0}
      semanticType  = SemanticType.Angular
      geometryType  = Geometry.Line
      level         = NodeLevel.invalid
    }
    
  ////// ACTIONS
  type Action = 
      | SetState            of State
      | ColorPickerMessage  of ColorPicker.Action
      | ChangeThickness     of Numeric.Action
      | TextInputMessage    of TextInput.Action
      | SetLevel            of int
      | SetSemanticType     of SemanticType
      | SetGeometryType     of Geometry
      | Save
      | Cancel

  ////// UPDATE
  let update (model : CorrelationSemantic) (a : Action) = 
      match a with
          | TextInputMessage m -> 
              {model with label = TextInput.update model.label m}
          | ColorPickerMessage m -> 
              { model with color = ColorPicker.update model.color m }
          | ChangeThickness m -> 
              { model with thickness = Numeric.update model.thickness m }
          | SetState state -> 
              {model with state = state}
          | SetLevel i ->
              {model with level = i |> NodeLevel}
          | SetSemanticType st ->
              {model with semanticType = st}
          | SetGeometryType gt ->
              {model with geometryType = gt}
          | _ -> model


  ////// VIEW
//    let viewNew (model : MSemantic) =
//        [td [] [text "foobar"]|> UI.map TextInputMessage] // WORKS
//      let thNode = Numeric.view'' 
//                      NumericInputType.InputBox 
//                      model.style.thickness
//                      (AttributeMap.ofList 
//                        [style "margin:auto; color:black; max-width:60px"])

//      let labelNode = 
//        (TextInput.view'' 
//          "box-shadow: 0px 0px 0px 1px rgba(0, 0, 0, 0.1) inset"
//          model.label)
          
        
//      [
//        labelNode
//          |> intoTd
//          |> UI.map Action.TextInputMessage
//        thNode 
//          |> UI.map ChangeThickness
//          |> intoTd
//        ColorPicker.view model.style.color 
//          |> UI.map ColorPickerMessage
//          |> intoTd
//        Html.SemUi.dropDown' (AList.ofList levels) model.level SetLevel (fun x -> sprintf "%i" x)
//          |> intoTd

//        Html.SemUi.dropDown model.semanticType SetSemanticType
//          |> intoTd

//      ]

////////////////////////////////// VIEW NEW ///////////////////////////////////////     

  module View =
    let miniView (model : MCorrelationSemantic) = 
      let domNodeLbl =
          Incremental.label 
            (AttributeMap.union 
               (AttributeMap.ofList [clazz "ui horizontal label"]) 
               (AttributeMap.ofAMap (GUI.CSS.incrBgColorAMap model.color.c)))
            (AList.ofList [Incremental.text (model.label.text)])
          |> intoLeftAlignedTd
          
      [domNodeLbl]
         
         
      //labelNode |> UI.map Action.TextInputMessage

    let viewNew (model : MCorrelationSemantic) =
      let thNode = Numeric.view'' 
                     NumericInputType.InputBox 
                     model.thickness
                     (AttributeMap.ofList 
                        [style "margin:auto; color:black; max-width:60px"])

      let labelNode = 
        (TextInput.view'' 
          "box-shadow: 0px 0px 0px 1px rgba(0, 0, 0, 0.1) inset"
          model.label)
          

      let domNodeSemanticType =  
        intoTd <|
          label [clazz "ui horizontal label"]
                [Incremental.text (Mod.map(fun x -> x.ToString()) model.semanticType)]

      let domNodeColor = 
        let iconAttr =
          amap {
            yield clazz "circle icon"
            let! c = model.color.c
            yield style (sprintf "color:%s" (GUI.CSS.colorToHexStr c))
          }  

        intoTd <|
          div[] [
            Incremental.i (AttributeMap.ofAMap iconAttr) (AList.ofList [])
            Incremental.text (Mod.map(fun (x : C4b) -> GUI.CSS.colorToHexStr x) model.color.c)
          ]//  |> intoTd


      [
        labelNode
          |> intoTd
          |> UI.map TextInputMessage
        thNode 
          |> UI.map ChangeThickness
          |> intoTd
        ColorPicker.view model.color
          |> intoTd
          |> UI.map ColorPickerMessage
        Html.SemUi.dropDown' NodeLevel.availableLevels (model.level |> Mod.map(fun l -> l.value)) SetLevel (fun x -> sprintf "%i" x)
          |> intoTd
        Html.SemUi.dropDown model.semanticType SetSemanticType
          |> intoTd
        Html.SemUi.dropDown model.geometryType SetGeometryType
          |> intoTd
      //[td [] [text "foobar"]|> UI.map TextInputMessage] // WORKS
      ]




 ////////////////////////////////////// EDIT ////////////////////////////////////////////
    let viewEdit (model : MCorrelationSemantic) =
      
      let thNode = Numeric.view'' 
                     NumericInputType.InputBox 
                     model.thickness
                     (AttributeMap.ofList 
                        [style "margin:auto; color:black; max-width:60px"])

      let labelNode = 
        (TextInput.view'' 
          "box-shadow: 0px 0px 0px 1px rgba(0, 0, 0, 0.1) inset"
          model.label)
          

      //let domNodeSemanticType =  
      //  intoTd <|
      //    label [clazz "ui horizontal label"]
      //          [Incremental.text (Mod.map(fun x -> x.ToString()) model.semanticType)]

      let domNodeSemanticType =  
        intoTd <|
                Incremental.text (Mod.map(fun x -> x.ToString()) model.semanticType)

      let domNodeGeometryType =  
        intoTd <|
                Incremental.text (Mod.map(fun x -> x.ToString()) model.geometryType) 

      let domNodeColor = 
        let iconAttr =
          amap {
            yield clazz "circle icon"
            let! c = model.color.c
            yield style (sprintf "color:%s" (GUI.CSS.colorToHexStr c))
          }  

        intoTd <|
          div[] [
            Incremental.i (AttributeMap.ofAMap iconAttr) (AList.ofList [])
            Incremental.text (Mod.map(fun (x : C4b) -> GUI.CSS.colorToHexStr x) model.color.c)
          ]//  |> intoTd


      [
        labelNode
          |> intoTd
          |> UI.map TextInputMessage
        thNode 
          |> UI.map ChangeThickness
          |> intoTd
        ColorPicker.view model.color
          |> intoTd
          |> UI.map ColorPickerMessage
        Html.SemUi.dropDown' NodeLevel.availableLevels (model.level |> Mod.map(fun l -> l.value)) SetLevel (fun x -> sprintf "%i" x)
          |> intoTd
        domNodeSemanticType
        domNodeGeometryType
      //[td [] [text "foobar"]|> UI.map TextInputMessage] // WORKS
      ]
       
    let viewDisplay (model : MCorrelationSemantic) = 
      //[text "foobar" |> intoTd |> UI.map Action.TextInputMessage]
        
      let domNodeLbl =
       // intoTd <| Incremental.text (s.label.text)
          Incremental.label 
            (AttributeMap.union 
               (AttributeMap.ofList [clazz "ui horizontal label"]) 
               (AttributeMap.ofAMap (GUI.CSS.incrBgColorAMap model.color.c)))
            (AList.ofList [Incremental.text (model.label.text)])
          |> intoTd

      let domNodeThickness = 
        intoTd <|
          label [clazz "ui horizontal label"] [
            Incremental.text (Mod.map(fun x -> sprintf "%.1f" x) model.thickness.value)
          ]
        

      let domNodeColor = 
        let iconAttr =
          amap {
            yield clazz "circle icon"
            let! c = model.color.c
            yield style (sprintf "color:%s" (GUI.CSS.colorToHexStr c))
          }  
        intoTd <|
          div[] [
            Incremental.i (AttributeMap.ofAMap iconAttr) (AList.ofList [])
            Incremental.text (Mod.map(fun (x : C4b) -> GUI.CSS.colorToHexStr x) model.color.c)
          ]
           

      let domNodeLevel = 
        intoTd <| 
                Incremental.text (Mod.map(fun (x : NodeLevel) -> sprintf "%i" x.value) model.level)
            


      let domNodeSemanticType =  
        intoTd <|
                Incremental.text (Mod.map(fun x -> x.ToString()) model.semanticType)

      let domNodeGeometryType =  
        intoTd <|
                Incremental.text (Mod.map(fun x -> x.ToString()) model.geometryType)           
                 
      [
        domNodeLbl
        domNodeThickness
        domNodeColor
        domNodeLevel
        domNodeSemanticType
        domNodeGeometryType
      ]
      

    let view (model : MCorrelationSemantic) : IMod<list<DomNode<Action>>> =
        // Mod.constant [td [] [text "foobar"]|> UI.map TextInputMessage] //WORKS
        
        model.state 
          |> Mod.map (fun state -> 
                        match state with
                        
                          | State.Display  -> viewDisplay model // [td [] [text "foobar"]|> UI.map TextInputMessage] WORKS
                          | State.Edit     -> viewEdit model
                          | State.New      -> viewNew model //TODO probably not necessary
                     ) 