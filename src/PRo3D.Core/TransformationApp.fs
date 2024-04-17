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
open PRo3D.Base.Gis

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
    | ToggleUsePivot
    | SetPivotSize          of Numeric.Action
   
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
        (observedSystem : Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (scale:float)  = 

           
           let east  = north.Cross(up).Normalized
           let refSysRotation = Trafo3d.FromOrthoNormalBasis(north, east, up)

           //translation along north, east, up            
           let trans = translation |> Trafo3d.Translation
           let translationTrafo = refSysRotation.Inverse * trans * refSysRotation
        
           let originTrafo = -pivot |> Trafo3d.Translation
           let eulerRotation = Trafo3d.RotationEulerInDegrees(roll, pitch, -yaw)

           let yawRotation    = Trafo3d.RotationInDegrees(up,  -yaw)
           let pitchRotation  = Trafo3d.RotationInDegrees(east, pitch)
           let rollRotation   = Trafo3d.RotationInDegrees(north,roll)

           let observerationTrafo = 
                match observedSystem, observerSystem with
                | Some observedSystem, Some observerSystem ->
                    match CooTransformation.transformBody observedSystem.body (Some observerSystem.referenceFrame) observerSystem.body observerSystem.referenceFrame observerSystem.time with
                    | None -> Trafo3d.Identity
                    | Some t -> t.Trafo
                | _ -> Trafo3d.Identity

           let rotAndScale = originTrafo * refSysRotation.Inverse  * eulerRotation * Trafo3d.Scale(scale) * refSysRotation  * originTrafo.Inverse * observerationTrafo
           
           let newTrafo = translationTrafo * rotAndScale 
           
           newTrafo
    
    // calc reference system from pivot
    let getNorthAndUpFromPivot
        (transform : Transformations) 
        (refsys : ReferenceSystem) =
            
            let upP = CooTransformation.getUpVector transform.pivot.value refsys.planet
            let eastP = V3d.OOI.Cross(upP)
        
            let northP  = 
                match refsys.planet with 
                | Planet.None | Planet.JPL -> V3d.IOO
                | Planet.ENU -> V3d.OIO
                | _ -> upP.Cross(eastP) 

            let noP = 
                Rot3d.Rotation(upP, refsys.noffset.value |> Double.radiansFromDegrees).Transform(northP)
            noP, upP
            

    let fullTrafo 
        (transform : AdaptiveTransformations) 
        (refsys : AdaptiveReferenceSystem)
        (observedSystem : aval<Option<SpiceReferenceSystem>>)
        (observerSystem : aval<Option<ObserverSystem>>)= 
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
           let! usePivot = transform.usePivot
           let! transf = transform.Current
           let! observedSystem = observedSystem
           let! observerSystem = observerSystem

           let northN, upN =
               if usePivot then
                   getNorthAndUpFromPivot transf refSys
               else
                north, up

           let newTrafo = calcFullTrafo translation yaw pitch roll pivot oldPivot pivotChanged northN.Normalized upN.Normalized observedSystem observerSystem scale
           return newTrafo
        }
           
    
    let fullTrafo' 
        (transform : Transformations) 
        (refsys : ReferenceSystem) 
        (observedSystem : Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>) =

        let north, up = 
            if transform.usePivot then
                getNorthAndUpFromPivot transform refsys
            else 
                refsys.northO, refsys.up.value

        calcFullTrafo 
            transform.translation.value 
            transform.yaw.value 
            transform.pitch.value
            transform.roll.value 
            transform.pivot.value
            transform.oldPivot
            transform.pivotChanged 
            north.Normalized 
            up.Normalized 
            observedSystem
            observerSystem
            transform.scaling.value

    let resetRotation (model : Transformations) =
        let yaw = { model.yaw with value = 0.0}
        let pitch = { model.pitch with value = 0.0}
        let roll = { model.roll with value = 0.0}
        {model with yaw = yaw; pitch = pitch; roll = roll}

    let refSysTranslation 
        (transform : Transformations)
        (translation : V3d)
        (refSys : ReferenceSystem) =

            let north, up = 
                if transform.usePivot then
                    getNorthAndUpFromPivot transform refSys
                else 
                    refSys.northO, refSys.up.value
            let east  = north.Cross(up).Normalized
            
            let refSysRotation = Trafo3d.FromOrthoNormalBasis(north, east, up)
            let trans = translation |> Trafo3d.Translation
            (refSysRotation.Inverse * trans * refSysRotation)

    let updateTransformationForNewPivot 
        (model : Transformations) =
        let yaw = { model.yaw with value = 0.0 }
        let pitch = { model.pitch with value = 0.0 }
        let roll = { model.roll with value = 0.0 }
        let scale = { model.scaling with value = 1.0}
        let pChanged = 
            if not (model.pitch.value = 0.0) && not (model.yaw.value = 0.0) && not (model.roll.value = 0.0) then true else false
        let trafo' = (model.translation.value |> Trafo3d.Translation)
        //{ model with yaw = yaw; pitch = pitch; roll = roll; trafo = trafo'; scaling = scale}
        { model with trafo = trafo'; pivotChanged = pChanged; yaw = yaw; pitch = pitch; roll = roll; scaling = scale} 
    

    let update<'a> 
        (model : Transformations)
        (act : Action) 
        (refSys : ReferenceSystem)=
        match act with
        | SetTranslation t ->    
            let t' = Vector3d.update model.translation t
            let transPivot = refSysTranslation model (t'.value - model.trafo.Forward.C3.XYZ) refSys //refSysTranslation model t'.value refSys
            let pivot = transPivot.Forward.TransformPos model.oldPivot
            let p' = Vector3d.updateV3d model.pivot pivot
            { model with translation =  t'; pivot = p'; pivotChanged = false; trafoChanged = true} 
        | SetPickedTranslation p ->
            let p' = Vector3d.updateV3d model.translation p
            { model with translation =  p'; pivotChanged = false; trafoChanged = true }
        | SetYaw a ->    
            let yaw = Numeric.update model.yaw a
            { model with yaw = yaw; pivotChanged = false; trafoChanged = true }
        | SetPitch a ->    
            let pitch = Numeric.update model.pitch a
            { model with pitch = pitch; pivotChanged = false; trafoChanged = true }
        | SetRoll a ->    
            let roll = Numeric.update model.roll a
            { model with roll = roll; pivotChanged = false; trafoChanged = true }
        | FlipZ ->
            //let trafo' = model.trafo * Trafo3d.Scale(1.0, 1.0, -1.0)
            { model with flipZ = (not model.flipZ); pivotChanged = false} //; trafo = trafo' }
        | ToggleVisible   -> 
            { model with useTranslationArrows = not model.useTranslationArrows}
        | ToggleSketchFab   -> 
            { model with isSketchFab = not model.isSketchFab; pivotChanged = false }
        | SetPivotPoint p ->
            if model.usePivot then
                let p' = Vector3d.update model.pivot p
                let m' = updateTransformationForNewPivot model
                { m' with pivot =  p'; oldPivot = p'.value; trafoChanged = false} 
            else model
        | SetPickedPivotPoint p ->
            if model.usePivot then
                let p' = Vector3d.updateV3d model.pivot p
                let m' = updateTransformationForNewPivot model
                { m' with pivot =  p';oldPivot = p'.value; trafoChanged = false} 
             else model
        | TogglePivotVisible -> 
            { model with showPivot = not model.showPivot}
        | SetScaling a ->
            { model with scaling = Numeric.update model.scaling a; pivotChanged = false; trafoChanged = true }
        | ToggleUsePivot   -> 
            { model with usePivot = not model.usePivot}
        | SetPivotSize s ->    
            let ps = Numeric.update model.pivotSize s
            { model with pivotSize = ps }
   
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
                    Html.row "use Pivot:"       [GuiEx.iconCheckBox model.usePivot ToggleUsePivot ]
                    Html.row "show PivotPoint:" [GuiEx.iconCheckBox model.showPivot TogglePivotVisible ]
                    Html.row "Pivot Point (m):" [viewPivotPointInput model.pivot |> UI.map SetPivotPoint ]
                    Html.row "Pivot Size:"      [Numeric.view' [InputBox] model.pivotSize |> UI.map SetPivotSize]
                    //Html.row "Reset Trafos:"    [button [clazz "ui button tiny"; onClick (fun _ -> ResetTrafos )] []]
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
            let point = PRo3D.Base.Sg.dot (AVal.constant C4b.GreenYellow) model.pivotSize.value model.pivot.value 
            Sg.ofList [point] |> Sg.onOff model.showPivot
