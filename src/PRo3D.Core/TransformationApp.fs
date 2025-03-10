namespace PRo3D.Core.Surface

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives

open System

open FSharp.Data.Adaptive
open Aardvark.Rendering    

open Aardvark.Rendering   

open Aardvark.Data.Opc
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.Data.Opc

open PRo3D.Base
open PRo3D.Core
open PRo3D.Base.Gis

open Aardvark.UI.Primitives
        
module TransformationApp =

    //open Aardvark.UI.ChoiceModule

    module EulerMode =
        let getTrafo (m : EulerMode) (x : float) (y : float) (z : float)=
            let x = Trafo3d.RotationXInDegrees x
            let y = Trafo3d.RotationYInDegrees y
            let z = Trafo3d.RotationZInDegrees z
            match m with
            | EulerMode.XYZ -> x * y * z
            | EulerMode.XZY -> x * z * y
            | EulerMode.YXZ -> y * x * z
            | EulerMode.YZX -> y * z * x
            | EulerMode.ZXY -> z * x * y
            | EulerMode.ZYX -> z * y * x
   
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
    | SetEulerMode          of EulerMode

    // calc reference system from pivot
    let getNorthAndUpFromPivot
        //(transform : Transformations) 
        (pivot : V3d) 
        (refsys : ReferenceSystem) =
            
            //let upP = CooTransformation.getUpVector transform.pivot.value refsys.planet
            let upP = CooTransformation.getUpVector pivot refsys.planet
            let eastP = V3d.OOI.Cross(upP)
        
            let northP  = 
                match refsys.planet with 
                | Planet.None | Planet.JPL -> V3d.IOO
                | Planet.ENU -> V3d.OIO
                | _ -> upP.Cross(eastP) 

            let noP = 
                Rot3d.Rotation(upP, refsys.noffset.value |> Double.radiansFromDegrees).Transform(northP)
            noP, upP, eastP

    let getReferenceSystemBasis 
        (pivot     : V3d)
        (refSystem : ReferenceSystem) =

        let northCorrection = Trafo3d.RotationZInDegrees(refSystem.noffset.value)

        match refSystem.planet with
        | Planet.Earth
        | Planet.ENU -> 
            Trafo3d.FromOrthoNormalBasis(V3d.IOO, V3d.OIO, V3d.OOI) * northCorrection
        | Planet.Mars ->
            //let upP = CooTransformation.getUpVector pivot refSystem.planet
            //let east = V3d.OOI.Cross(upP)
            //let north = upP.Cross(east)
            //Log.line "%A,%A,%A" upP.Length east.Length north.Length

            let north, up, east = 
                if not(pivot = V3d.Zero) then
                    getNorthAndUpFromPivot pivot refSystem
                else 
                    let north = refSystem.northO.Normalized        
                    let up    = refSystem.up.value.Normalized
                    let east  = north.Cross(up).Normalized
                    north, up, east
              
            let refSysRotation = 
                Trafo3d.FromOrthoNormalBasis(north, east, up)
            refSysRotation
        | Planet.JPL -> 
            Trafo3d.FromOrthoNormalBasis(-V3d.IOO, V3d.OIO, -V3d.OOI) * northCorrection
        | Planet.None -> 
            northCorrection
        | _ -> failwith ""

    let translationFromReferenceSystemBasis
        (translation    : V3d)
        (pivot          : V3d )
        (refSystem      : ReferenceSystem) =
            let refsysbasis = getReferenceSystemBasis V3d.Zero refSystem 
            refsysbasis.Forward.TransformPos(translation) 
   
    let calcFullTrafo 
        (translation : V3d)
        (yaw : float)
        (pitch : float)
        (roll : float)
        (pivot : V3d)
        (refSystem : ReferenceSystem)
        (observedSystem : Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (scale:float)
        (eulerMode : EulerMode) = 

           let refSysBasis = getReferenceSystemBasis pivot refSystem
           let originTrafo = pivot |> Trafo3d.Translation

           //translation along north, east, up directions         
           let fullTrafo = 
                originTrafo.Inverse * 
                    refSysBasis.Inverse *
                            EulerMode.getTrafo eulerMode roll pitch yaw *
                            Trafo3d.Scale(scale)
                    * refSysBasis
                * originTrafo 
                * Trafo3d.Translation(refSysBasis.Forward.TransformPos(translation))
        
         
           let observerationTrafo = 
                match observedSystem, observerSystem with
                | Some observedSystem, Some observerSystem ->
                    match CooTransformation.transformBody observedSystem.body (Some observedSystem.referenceFrame) observerSystem.body observerSystem.referenceFrame observerSystem.time with
                    | None -> Trafo3d.Identity
                    | Some t -> t.Trafo
                | _ -> Trafo3d.Identity


           // translate back to the pivot point (=rotation center)
           // do rotation around north, east and up
           // do scaling
           // translate back from pivot
           //let rotAndScale = originTrafo * rotation * Trafo3d.Scale(scale)  * originTrafo.Inverse //* observerationTrafo
           ////let rotAndScale = originTrafo * refSysRotation.Inverse * eulerRotation * Trafo3d.Scale(scale) * refSysRotation * originTrafo.Inverse
           
           //// do translation than rotation (not sure why this order is working)
           //let newTrafo = rotAndScale * translationTrafo
           
           observerationTrafo * fullTrafo
    

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
           let! scale = transform.scaling.value
           let! usePivot = transform.usePivot
           let! transf = transform.Current
           let! observedSystem = observedSystem
           let! observerSystem = observerSystem
           let! mode = transform.eulerMode

           //let northN, upN, eastN =
           //    if usePivot then
           //        getNorthAndUpFromPivot transf refSys
           //    else
           //     north, up, north.Cross(up)

           let newTrafo = calcFullTrafo translation yaw pitch roll (if usePivot then pivot else V3d.Zero) refSys observedSystem observerSystem scale mode
           return newTrafo
        }
           
    
    let fullTrafo' 
        (transform : Transformations) 
        (refsys : ReferenceSystem) 
        (observedSystem : Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>) =

        //let north, up, east = 
        //    if transform.usePivot then
        //        getNorthAndUpFromPivot transform refsys
        //    else 
        //        refsys.northO, refsys.up.value, refsys.northO.Cross(refsys.up.value)

        calcFullTrafo 
            transform.translation.value 
            transform.yaw.value 
            transform.pitch.value
            transform.roll.value 
            transform.pivot.value
            refsys
            observedSystem
            observerSystem
            transform.scaling.value
            transform.eulerMode

    let resetRotation (model : Transformations) =
        let yaw = { model.yaw with value = 0.0}
        let pitch = { model.pitch with value = 0.0}
        let roll = { model.roll with value = 0.0}
        {model with yaw = yaw; pitch = pitch; roll = roll}

    let refSysTranslation 
        (transform : Transformations)
        (translation : V3d)
        (pivot       : V3d)
        (refSys : ReferenceSystem) =

            let north, up, east = 
                if transform.usePivot then
                    getNorthAndUpFromPivot pivot refSys
                else 
                    refSys.northO, refSys.up.value, refSys.northO.Cross(refSys.up.value)
            
            let refSysRotation = Trafo3d.FromOrthoNormalBasis(north, east, up)
            let trans = translation |> Trafo3d.Translation
            (refSysRotation.Inverse * trans * refSysRotation)

    let updateTransformationForNewPivot 
        (model : Transformations) =
        //let yaw = { model.yaw with value = 0.0 }
        //let pitch = { model.pitch with value = 0.0 }
        //let roll = { model.roll with value = 0.0 }
        //let scale = { model.scaling with value = 1.0}
        let pChanged = 
            if not (model.pitch.value = 0.0) && not (model.yaw.value = 0.0) && not (model.roll.value = 0.0) then true else false
        let trafo' = (model.translation.value |> Trafo3d.Translation)
        //{ model with yaw = yaw; pitch = pitch; roll = roll; trafo = trafo'; scaling = scale}
        { model with trafo = trafo'; pivotChanged = pChanged; (*yaw = yaw; pitch = pitch; roll = roll; scaling = scale*)} 
    

    let update<'a> 
        (model : Transformations)
        (act : Action) 
        (refSys : ReferenceSystem)=
        match act with
        | SetTranslation t ->    
            let t' = Vector3d.update model.translation t
            let transPivot = refSysTranslation model (t'.value - model.trafo.Forward.C3.XYZ) model.pivot.value refSys //refSysTranslation model t'.value refSys
            let pivot = transPivot.Forward.TransformPos model.oldPivot
            let p' = Vector3d.updateV3d model.pivot pivot
            { model with translation =  t'; (* pivot = p'; pivotChanged = false; *) trafoChanged = true} 
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
        | SetPivotPoint p -> // object space
            if model.usePivot then
                let p' = Vector3d.update model.pivot p
                //let m' = updateTransformationForNewPivot model
                //{ m' with pivot =  p'; oldPivot = p'.value; trafoChanged = false} 
                let fulTrafo : Trafo3d = fullTrafo' model refSys None None
                { model with pivot = p'; oldPivot = p'.value; trafoChanged = false} 
            else model
        | SetPickedPivotPoint p -> // world space.......
            if model.usePivot then
                let fulTrafo : Trafo3d = fullTrafo' model refSys None None
                let pivotObjectSpace = fulTrafo.Inverse.TransformPos(p) 
                let p' = Vector3d.updateV3d model.pivot pivotObjectSpace
                //let m' = updateTransformationForNewPivot model
                { model with pivot = p'; oldPivot = p'.value; trafoChanged = false} 
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
        | SetEulerMode m -> 
            { model with eulerMode = m }
   
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
            let mode : aval<EulerMode> = AVal.constant EulerMode.XYZ
            let modeDropDown = 
                let values = 
                    [ EulerMode.XYZ, "XYZ"; EulerMode.XZY, "XZY"; EulerMode.YXZ, "YXZ"; EulerMode.YZX, "YZX"; EulerMode.ZXY, "ZXY"; EulerMode.ZYX, "ZYX"]
                    |> List.map (fun (m, v) -> m, text v)
                    |> AMap.ofList
                Dropdown.dropdown SetEulerMode false None mode AttributeMap.empty values 

            require GuiEx.semui (
                Html.table [  
                    //Html.row "Visible:" [GuiEx.iconCheckBox model.useTranslationArrows ToggleVisible ]
                    Html.row "Translation (m):" [viewV3dInput model.translation |> UI.map SetTranslation ]
                    Html.row "Scale:"           [Numeric.view' [NumericInputType.InputBox]   model.scaling  |> UI.map SetScaling ]
                    Html.row "Yaw   (Z,deg):"     [Numeric.view' [InputBox] model.yaw |> UI.map SetYaw]
                    Html.row "Pitch (Y,deg):"     [Numeric.view' [InputBox] model.pitch |> UI.map SetPitch]
                    Html.row "Roll  (X,deg):"     [Numeric.view' [InputBox] model.roll |> UI.map SetRoll]
                    Html.row "flip Z:"          [GuiEx.iconCheckBox model.flipZ FlipZ ]
                    Html.row "sketchFab:"       [GuiEx.iconCheckBox model.isSketchFab ToggleSketchFab ]
                    Html.row "use Pivot:"       [GuiEx.iconCheckBox model.usePivot ToggleUsePivot ]
                    Html.row "show PivotPoint:" [GuiEx.iconCheckBox model.showPivot TogglePivotVisible ]
                    Html.row "Pivot Point (m):" [viewPivotPointInput model.pivot |> UI.map SetPivotPoint ]
                    Html.row "Pivot Size:"      [Numeric.view' [InputBox] model.pivotSize |> UI.map SetPivotSize]
                    Html.row "Mode" [modeDropDown]
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
        let viewObjectSpace (model:AdaptiveTransformations) =
            let point = PRo3D.Base.Sg.dot (AVal.constant C4b.GreenYellow) model.pivotSize.value model.pivot.value 
            Sg.ofList [point] |> Sg.onOff model.showPivot
