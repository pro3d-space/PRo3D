namespace PRo3D.Core.Surface

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open System

open FSharp.Data.Adaptive
open Aardvark.Rendering    

open Aardvark.Rendering   

open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.VRVis.Opc

open PRo3D.Base
open PRo3D.Core

open Aardvark.Base.MultimethodTest
        
module TranslationApp =

    //open Aardvark.UI.ChoiceModule
   
    type Action =
    | SetTranslation   of Vector3d.Action
    | SetYaw           of Numeric.Action
    | SetPitch         of Numeric.Action
    | SetRoll          of Numeric.Action
    | SetPivotPoint    of Vector3d.Action
    | FlipZ    
    | ToggleSketchFab
    | ToggleVisible
    

    let update<'a> (model : Transformations) (act : Action) =
        match act with
        | SetTranslation t ->    
            let t' = Vector3d.update model.translation t
            { model with translation =  t' } // trafo = Trafo3d.Translation t'.value}
        | SetYaw a ->    
            let yaw = Numeric.update model.yaw a
            { model with yaw = yaw }
        | SetPitch a ->    
            let pitch = Numeric.update model.pitch a
            { model with pitch = pitch }
        | SetRoll a ->    
            let roll = Numeric.update model.roll a
            { model with roll = roll }
        | FlipZ ->
            //let trafo' = model.trafo * Trafo3d.Scale(1.0, 1.0, -1.0)
            { model with flipZ = (not model.flipZ)} //; trafo = trafo' }
        | ToggleVisible   -> 
            { model with useTranslationArrows = not model.useTranslationArrows}
        | ToggleSketchFab   -> 
            { model with isSketchFab = not model.isSketchFab }
        | SetPivotPoint p ->   
            let oldPivot = model.pivot.value
            let p' = Vector3d.update model.pivot p
            { model with pivot =  p'; pivotChanged = true; oldPivot = oldPivot} 
   
    module UI =
        
        let viewV3dInput (model : AdaptiveV3dInput) =  
            Html.table [                            
                Html.row "north" [Numeric.view' [InputBox] model.x |> UI.map Vector3d.Action.SetX]
                Html.row "east"  [Numeric.view' [InputBox] model.y |> UI.map Vector3d.Action.SetY]
                Html.row "up"    [Numeric.view' [InputBox] model.z |> UI.map Vector3d.Action.SetZ]
            ]   
            
        let viewPivotPointInput (model : AdaptiveV3dInput) =  
            Html.table [                            
                Html.row "X" [Numeric.view' [InputBox] model.x |> UI.map Vector3d.Action.SetX]
                Html.row "Y" [Numeric.view' [InputBox] model.y |> UI.map Vector3d.Action.SetY]
                Html.row "Z" [Numeric.view' [InputBox] model.z |> UI.map Vector3d.Action.SetZ]
            ]  

        let view (model:AdaptiveTransformations) =
            
            require GuiEx.semui (
                Html.table [  
                    //Html.row "Visible:" [GuiEx.iconCheckBox model.useTranslationArrows ToggleVisible ]
                    Html.row "Translation (m):" [viewV3dInput model.translation |> UI.map SetTranslation ]
                    Html.row "Yaw (deg):"       [Numeric.view' [InputBox] model.yaw |> UI.map SetYaw]
                    Html.row "Pitch (deg):"     [Numeric.view' [InputBox] model.pitch |> UI.map SetPitch]
                    Html.row "Roll (deg):"      [Numeric.view' [InputBox] model.roll |> UI.map SetRoll]
                    Html.row "flip Z:"          [GuiEx.iconCheckBox model.flipZ FlipZ ]
                    Html.row "sketchFab:"       [GuiEx.iconCheckBox model.isSketchFab ToggleSketchFab ]
                    //Html.row "Pivot Point:"     [Incremental.text (model.pivot |> AVal.map (fun x -> x.ToString ()))]
                    Html.row "Pivot Point (m):" [viewPivotPointInput model.pivot |> UI.map SetPivotPoint ]
                ]
            )
