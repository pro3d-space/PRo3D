namespace PRo3D.Core

open System

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering.Text

open Aardvark.UI
open Aardvark.UI.Primitives    

open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

open FShade

open Adaptify.FSharp.Core
open PRo3D.Base

module HeightValidatorApp =    

    let update (model : HeightValidatorModel) up north (action : HeightValidatorAction) : HeightValidatorModel =
        match action with
        | PlaceValidator p ->
            match model.validatorBrush.Count with
            | 0 ->
                Log.line "adding point"
                { model with validatorBrush = model.validatorBrush |> IndexList.add p }
            | 1 ->
                let brush = model.validatorBrush |> IndexList.add p

                Log.line "creating validator"
                let validator = 
                    HeightValidatorModel.createValidator 
                        brush.[0]
                        brush.[1]
                        up
                        north
                        model.validator.inclination.value

                { model with 
                    validator      = validator; 
                    result         = HeightValidatorModel.computeResult validator; 
                    validatorBrush = IndexList.empty 
                }
            | _ -> 
                model
        | ChangeInclination a ->

            let validator = (HeightValidatorModel.updateValidator model.validator a)
           
            let result = HeightValidatorModel.computeResult validator

            { model with validator = validator; result = result }
        | _ ->
            model

    let viewUI (model : AdaptiveHeightValidatorModel) : DomNode<HeightValidatorAction>=        
        let results = model.result

        div [] [
            GuiEx.accordion "Thickness" "Rocket" true [
                require GuiEx.semui (
                    Html.table [         
                        Html.row "Dip Angle:"           [Numeric.view' [InputBox] model.validator.inclination    |> UI.map ChangeInclination]
                        Html.row "cooTrafo Geographic:" [Incremental.text (results.cooTrafoThickness_geographic  |> AVal.map  (fun d -> sprintf "%.4f" (d)))]
                        Html.row "cooTrafo True:"       [Incremental.text (results.cooTrafoThickness_true        |> AVal.map  (fun d -> sprintf "%.4f" (d)))]
                        Html.row "plane Geographic:"    [Incremental.text (results.heightOverHorizontal          |> AVal.map  (fun d -> sprintf "%.4f" (d)))]
                        Html.row "plane True:"          [Incremental.text (results.heightOverPlaneThickness_true |> AVal.map  (fun d -> sprintf "%.4f" (d)))]
                    ]
                )
               // DrawingApp.UI.viewAnnotationToolsHorizontal m.drawing |> UI.map DrawingMessage // CHECK-merge viewAnnotationGroups
            ]
        ]

    let view (model : AdaptiveHeightValidatorModel) =
        
        let posA = 
            adaptive {
                let! brush = model.validatorBrush |> AList.toAVal
                let! pos = model.validator.lower

                match brush.Count with
                | 1 | 2 -> 
                    return brush.[0]
                | _ -> 
                    return pos
            }

        let posA = Sg.dot ~~C4b.Magenta ~~3.0 posA

        let posB = 
            adaptive {
                let! brush = model.validatorBrush |> AList.toAVal
                let! pos = model.validator.upper

                match brush.Count with
                | 2 -> 
                    return brush.[1]
                | _ -> 
                    return pos
            }
       
        let posB = Sg.dot ~~C4b.Magenta ~~3.0 posB

        let inclinedLine = 
            AVal.custom (fun t -> 
                let lower = model.validator.lower.GetValue(t)
                let upper = model.validator.upper.GetValue(t)
                let height = model.result.heightOverHorizontal.GetValue(t)
                let up = model.validator.upVector.GetValue(t)
                
                [| lower; upper - height * up; upper; lower |]
            )
            //alist {
            //    let! lower = model.validator.lower
            //    let! upper = model.validator.upper
            //    let! height = model.result.heightOverHorizontal
            //    let! up = model.validator.upVector
                
            //    yield lower 
            //    yield upper - height * up
            //    yield upper
            //    yield lower 
            //}

        let inclinedLine = 
            Sg.drawScaledLines inclinedLine ~~C4b.Cyan ~~1.0 (model.validator.lower |> AVal.map(Trafo3d.Translation))

        let tiltedUp = 
            (model.validator.tiltedPlane, model.validator.lower, model.result.heightOverPlaneThickness_true) 
            |||> AVal.map3 (fun plane lower height -> 
                [| lower; lower + plane.Normal * height |]
            )

                  
        let tiltedUp = 
            Sg.drawScaledLines tiltedUp ~~C4b.Magenta ~~1.0  (model.validator.lower |> AVal.map(Trafo3d.Translation))
        
        
        
        Sg.ofList[posA; posB; inclinedLine; tiltedUp]

    let viewDiscs (model : AdaptiveHeightValidatorModel) =
        
        // lower disc
        let lowerDiscTrafo =
            AVal.map2(fun (pln:Plane3d) pos -> 
                (Trafo3d.RotateInto(V3d.ZAxis, pln.Normal) * pos))
                model.validator.tiltedPlane
                (model.validator.lower |> AVal.map(Trafo3d.Translation))
        
        let lowerDisc = Sg.discISg ~~C4b.Magenta ~~1.0 ~~(0.01) lowerDiscTrafo
        
        // upper disc
        let upperDiscTrafo =
            AVal.map2(fun (pln:Plane3d) pos -> 
                (Trafo3d.RotateInto(V3d.ZAxis, pln.Normal) * pos))
                model.validator.tiltedPlane
                (model.validator.upper |> AVal.map(Trafo3d.Translation))
        
        let upperDisc = Sg.discISg ~~C4b.Magenta ~~1.0 ~~(0.01) upperDiscTrafo

        Sg.ofList[lowerDisc; upperDisc;]
