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
        
module TransformationApp =

    //open Aardvark.UI.ChoiceModule
   
    type Action =
    | SetTranslation        of Vector3d.Action
    | SetPickedTranslation  of V3d
    | SetYaw                of Numeric.Action
    | SetPitch              of Numeric.Action
    | SetRoll               of Numeric.Action
    | FlipZ    
    | ToggleSketchFab
    | ToggleVisible
    | SetPivotPoint         of Vector3d.Action
    | SetPickedPivotPoint   of V3d
    | TogglePivotVisible
    | SetScaling            of Numeric.Action
    //| PivotChanged          of bool


    let getRotation 
        (yaw : float)
        (pitch : float)
        (roll : float)
        (north : V3d)
        (up : V3d) = 

        let east  = north.Cross(up)
        let yawRotation    = Trafo3d.RotationInDegrees(up,   yaw)
        let pitchRotation  = Trafo3d.RotationInDegrees(east, pitch)
        let rollRotation   = Trafo3d.RotationInDegrees(north,roll)

        yawRotation * pitchRotation * rollRotation
   
    let calcFullTrafo 
        (translation : V3d)
        (yaw : float)
        (pitch : float)
        (roll : float)
        (pivot : V3d)
        (oldPivot : V3d)
        (pivotChanged : bool)
        (north : V3d)
        (up : V3d) 
        (scale:float) 
        (firstChange : bool) = 
           
           let east  = north.Cross(up)
           let refSysRotation = Trafo3d.FromOrthoNormalBasis(north, east, up)

           //translation along north, east, up            
           let trans = translation |> Trafo3d.Translation
        
           let originTrafo = -pivot |> Trafo3d.Translation

           let yawRotation    = Trafo3d.RotationInDegrees(up,   yaw)
           let pitchRotation  = Trafo3d.RotationInDegrees(east, pitch)
           let rollRotation   = Trafo3d.RotationInDegrees(north,roll)

           let oldOriginTrafo = -oldPivot |> Trafo3d.Translation
           //let yawRotationReset    = Trafo3d.RotationInDegrees(up,    0.0)
           //let pitchRotationReset  = Trafo3d.RotationInDegrees(east,  0.0)
           //let rollRotationReset   = Trafo3d.RotationInDegrees(north, 0.0)

           //let resetRotation = yawRotationReset * pitchRotationReset * rollRotationReset
           //let resetRotationTrafo = oldOriginTrafo * resetRotation * oldOriginTrafo.Inverse
           let rot = yawRotation * pitchRotation * rollRotation
           let rotAndScaleTrafo = originTrafo * rot * Trafo3d.Scale(scale)  * originTrafo.Inverse
           //let rotTrafo = if firstChange then switchPivotRotation else rotAndScaleTrafo
           let newTrafo = refSysRotation.Inverse * trans * refSysRotation * rotAndScaleTrafo 
           

           if pivotChanged then
               let rotAndScaleTrafo = oldOriginTrafo * rot * Trafo3d.Scale(scale) * oldOriginTrafo.Inverse
               let newPivotTrafo = refSysRotation.Inverse * trans * refSysRotation * rotAndScaleTrafo //* resetRotationTrafo 
               newPivotTrafo
           else 
               newTrafo

    let fullTrafo 
        (transform : AdaptiveTransformations) 
        (refsys : AdaptiveReferenceSystem) = 
        adaptive {
           let! refSys = refsys.Current
           let! translation = transform.translation.value
           let! yaw = transform.yaw.value
           let! pitch = transform.pitch.value
           let! roll = transform.roll.value
           let! pivot = transform.pivot.value
           let! oldPivot = transform.oldPivot
           let! pivotChanged = transform.pivotChanged
           let north = refSys.northO
           let up = refSys.up.value
           let! scale = transform.scaling.value
           let! firstChange = transform.firstChangeAfterNewPivot

           return calcFullTrafo translation yaw pitch roll pivot oldPivot pivotChanged north.Normalized up.Normalized scale firstChange
           }

    let fullTrafo' 
        (transform : Transformations) 
        (refsys : ReferenceSystem) = 

        calcFullTrafo 
            transform.translation.value 
            transform.yaw.value 
            transform.pitch.value
            transform.roll.value 
            transform.pivot.value
            transform.oldPivot
            transform.pivotChanged 
            refsys.northO.Normalized 
            refsys.up.value.Normalized
            transform.scaling.value
            transform.firstChangeAfterNewPivot

    let resetRotation (model : Transformations) =
        let yaw = { model.yaw with value = 0.0}
        let pitch = { model.pitch with value = 0.0}
        let roll = { model.roll with value = 0.0}
        {model with yaw = yaw; pitch = pitch; roll = roll}
    

    let update<'a> (model : Transformations) (act : Action) =
        match act with
        | SetTranslation t ->    
            let t' = Vector3d.update model.translation t
            let fcap = if model.trafoChanged then false else true
            { model with translation =  t'; pivotChanged = false; trafoChanged = true; firstChangeAfterNewPivot = fcap } // trafo = Trafo3d.Translation t'.value}
        | SetPickedTranslation p ->
            let p' = Vector3d.updateV3d model.translation p
            let fcap = if model.trafoChanged then false else true
            { model with translation =  p'; pivotChanged = false; trafoChanged = true; firstChangeAfterNewPivot = fcap }
        | SetYaw a ->    
            let yaw = Numeric.update model.yaw a
            let fcap = if model.trafoChanged then false else true
            { model with yaw = yaw; pivotChanged = false; trafoChanged = true; firstChangeAfterNewPivot = fcap }
        | SetPitch a ->    
            let pitch = Numeric.update model.pitch a
            let fcap = if model.trafoChanged then false else true
            { model with pitch = pitch; pivotChanged = false; trafoChanged = true; firstChangeAfterNewPivot = fcap }
        | SetRoll a ->    
            let roll = Numeric.update model.roll a
            let fcap = if model.trafoChanged then false else true
            { model with roll = roll; pivotChanged = false; trafoChanged = true; firstChangeAfterNewPivot = fcap }
        | FlipZ ->
            //let trafo' = model.trafo * Trafo3d.Scale(1.0, 1.0, -1.0)
            { model with flipZ = (not model.flipZ); pivotChanged = false} //; trafo = trafo' }
        | ToggleVisible   -> 
            { model with useTranslationArrows = not model.useTranslationArrows}
        | ToggleSketchFab   -> 
            { model with isSketchFab = not model.isSketchFab; pivotChanged = false }
        | SetPivotPoint p ->
            let oldPivot = if model.trafoChanged then model.pivot.value else model.oldPivot
            let p' = Vector3d.update model.pivot p
            { model with pivot =  p'; pivotChanged = true; oldPivot = oldPivot; trafoChanged = false} 
        | SetPickedPivotPoint p ->
            let oldPivot = if model.trafoChanged then model.pivot.value else model.oldPivot
            let p' = Vector3d.updateV3d model.pivot p
            { model with pivot =  p'; pivotChanged = true; oldPivot = oldPivot; trafoChanged = false }
        | TogglePivotVisible -> 
            { model with showPivot = not model.showPivot}
        | SetScaling a ->
            { model with scaling = Numeric.update model.scaling a; pivotChanged = false; trafoChanged = true }
        //| PivotChanged changed ->
        //    { model with pivotChanged = changed}
   
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
                    Html.row "Scale:"           [Numeric.view' [NumericInputType.InputBox]   model.scaling  |> UI.map SetScaling ]
                    Html.row "Yaw (deg):"       [Numeric.view' [InputBox] model.yaw |> UI.map SetYaw]
                    Html.row "Pitch (deg):"     [Numeric.view' [InputBox] model.pitch |> UI.map SetPitch]
                    Html.row "Roll (deg):"      [Numeric.view' [InputBox] model.roll |> UI.map SetRoll]
                    Html.row "flip Z:"          [GuiEx.iconCheckBox model.flipZ FlipZ ]
                    Html.row "sketchFab:"       [GuiEx.iconCheckBox model.isSketchFab ToggleSketchFab ]
                    Html.row "Pivot Point (m):" [viewPivotPointInput model.pivot |> UI.map SetPivotPoint ]
                    Html.row "show PivotPoint:" [GuiEx.iconCheckBox model.showPivot TogglePivotVisible ]
                    //Html.row "Pivot Point:"     [Incremental.text (model.pivot |> AVal.map (fun x -> x.ToString ()))]
                ]
            )

        let translationView (model:AdaptiveTransformations) =
            require GuiEx.semui (
                Html.table [ 
                    Html.row "Translation (m):" [viewV3dInput model.translation |> UI.map SetTranslation ]
                ]
            )

    module Sg =
        let view (model:AdaptiveTransformations) =
            let point = PRo3D.Base.Sg.dot (AVal.constant C4b.GreenYellow) (AVal.constant 3.0) model.pivot.value 
            Sg.ofList [point] |> Sg.onOff model.showPivot
