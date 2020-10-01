namespace PRo3D

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open System

open FSharp.Data.Adaptive
open Aardvark.Base.Rendering    

open Aardvark.Base.Rendering   

open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.SceneGraph.FShadeSceneGraph
open Aardvark.VRVis.Opc

open PRo3D.Base
open PRo3D.Core

open Aardvark.Base.MultimethodTest
        
module TranslationApp =

    //open Aardvark.UI.ChoiceModule
   
    type Action =
        | SetTranslation   of Vector3d.Action
        | SetYaw           of Numeric.Action
        | ToggleVisible
    

    let update<'a> (model : Transformations) (act : Action) =
        match act with
        | SetTranslation t ->    
            let t' = Vector3d.update model.translation t
            { model with translation =  t' } // trafo = Trafo3d.Translation t'.value}
        | SetYaw a ->    
            let yaw = Numeric.update model.yaw a
            { model with yaw = yaw }
        | ToggleVisible   -> 
            { model with useTranslationArrows = not model.useTranslationArrows}

   
    module UI =
        
        let viewV3dInput (model : AdaptiveV3dInput) =  
            Html.table [                            
                Html.row "north" [Numeric.view' [InputBox] model.x |> UI.map Vector3d.Action.SetX]
                Html.row "east"  [Numeric.view' [InputBox] model.y |> UI.map Vector3d.Action.SetY]
                Html.row "up"    [Numeric.view' [InputBox] model.z |> UI.map Vector3d.Action.SetZ]
            ]       

        let view (model:AdaptiveTransformations) =
            
            require GuiEx.semui (
                Html.table [  
                    //Html.row "Visible:" [GuiEx.iconCheckBox model.useTranslationArrows ToggleVisible ]
                    Html.row "Translation (m):" [viewV3dInput model.translation |> UI.map SetTranslation ]
                    Html.row "Yaw (deg):" [Numeric.view' [InputBox] model.yaw |> UI.map SetYaw]
                ]
            )
