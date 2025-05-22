namespace PRo3D.Core.Surface

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos

open System

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
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
    | SetPickedReferenceSystem of V3d
    | TogglePivotVisible
    | SetScaling            of Numeric.Action
    | ToggleUsePivot
    | SetPivotSize          of Numeric.Action
    | SetEulerMode          of EulerMode
    | ToggleRefSysVisible
    | SetRefSysSize         of Numeric.Action

    // calc reference system from pivot
    let getNorthAndUpFromPivot
        //(transform : Transformations) 
        (pivot : V3d) 
        (refsys : ReferenceSystem) =
            
            //let upP = CooTransformation.getUpVector transform.pivot.value refsys.planet
            let upP = CooTransformation.getUpVector pivot refsys.planet
            let eastP = V3d.OOI.Cross(upP.Normalized).Normalized
        
            let northP  = 
                match refsys.planet with 
                | Planet.None | Planet.JPL -> V3d.IOO
                | Planet.ENU -> V3d.OIO
                | _ -> upP.Cross(eastP).Normalized 

            let noP = 
                Rot3d.Rotation(upP, refsys.noffset.value |> Double.radiansFromDegrees).Transform(northP)
            noP, upP, eastP

    let getNorthUpEastFromLocalRefSys
        (refSys : Affine3d) =
        let north = refSys.Linear.C0.Normalized        
        let up    = refSys.Linear.C2.Normalized
        let east  = refSys.Linear.C1.Normalized //north.Cross(up).Normalized
        north, up, east

    let getReferenceSystemBasis_global 
        (refSystem : ReferenceSystem) =

        let northCorrection = Trafo3d.RotationZInDegrees(refSystem.noffset.value)

        match refSystem.planet with
        | Planet.Earth
        | Planet.ENU -> 
            Trafo3d.FromOrthoNormalBasis(V3d.IOO, V3d.OIO, V3d.OOI) * northCorrection
        | Planet.Mars ->
            let north, up, east =
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

    let getReferenceSystemBasis_local 
        (directions : Affine3d)
        (planet : Planet) =

        match planet with
        | Planet.Earth
        | Planet.ENU -> 
            Trafo3d.FromOrthoNormalBasis(V3d.IOO, V3d.OIO, V3d.OOI)
        | Planet.Mars ->
            let north, up, east = getNorthUpEastFromLocalRefSys directions
            let refSysRotation = 
                Trafo3d.FromOrthoNormalBasis(north, east, up)
            refSysRotation
        | Planet.JPL -> 
            Trafo3d.FromOrthoNormalBasis(-V3d.IOO, V3d.OIO, -V3d.OOI)
        | Planet.None -> 
            Trafo3d(directions)
        | _ -> failwith ""

    let translationFromReferenceSystemBasis
        (translation    : V3d)
        (refSystem      : ReferenceSystem) =
            let refsysbasis = getReferenceSystemBasis_global refSystem 
            refsysbasis.Forward.TransformPos(translation) 
   
    
    let calcFullTrafo
        (translation : V3d)
        (yaw : float)
        (pitch : float)
        (roll : float)
        (observedSystem : Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (scale:float)
        (eulerMode : EulerMode) 
        (refSysBasis : Trafo3d)
        (originTrafo : Trafo3d) = 

           //translation along north, east, up directions         
           let fullTrafo_local = 
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


           fullTrafo_local

    let getReferenceSystemBasisAndOriginTrafo
        (refSys : ReferenceSystem)
        (localRefSys : Option<Affine3d>)
        (pivot : V3d)
        (usePivot : bool) =

        match localRefSys, usePivot with
        // uses the local reference system of the surface for reference system basis calculations
        | Some lrs, true -> let rSB = getReferenceSystemBasis_local lrs refSys.planet
                            let oT = pivot |> Trafo3d.Translation
                            rSB, oT
        | _, _ -> let rSB = getReferenceSystemBasis_global refSys
                  let oT = refSys.origin |> Trafo3d.Translation
                  rSB, oT

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
           let! scale = transform.scaling.value
           let! usePivot = transform.usePivot
           let! observedSystem = observedSystem
           let! observerSystem = observerSystem
           let! mode = transform.eulerMode 
           let! localRefSys = transform.refSys

           let refSysBasis, originTrafo = getReferenceSystemBasisAndOriginTrafo refSys localRefSys pivot usePivot
           let newTrafo = calcFullTrafo translation yaw pitch roll observedSystem observerSystem scale mode refSysBasis originTrafo
           return newTrafo
        }

    
    let fullTrafo' 
        (transform : Transformations) 
        (refsys : ReferenceSystem) 
        (observedSystem : Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>) =

        let refSysBasis, originTrafo = getReferenceSystemBasisAndOriginTrafo refsys transform.refSys transform.pivot.value transform.usePivot
        let newTrafo = calcFullTrafo 
                                transform.translation.value 
                                transform.yaw.value 
                                transform.pitch.value
                                transform.roll.value 
                                observedSystem
                                observerSystem
                                transform.scaling.value
                                transform.eulerMode
                                refSysBasis
                                originTrafo
        newTrafo
      
    let refSysTranslation 
        (transform : Transformations)
        (translation : V3d)
        (pivot       : V3d)
        (refSys : ReferenceSystem) =

            let refSysRotation = 
                match transform.refSys, transform.usePivot with
                | Some lrs, true -> getReferenceSystemBasis_local lrs refSys.planet
                | _, _ -> getReferenceSystemBasis_global refSys
            let trans = translation |> Trafo3d.Translation
            (refSysRotation.Inverse * trans * refSysRotation)

    //let updateTransformationForNewPivot 
    //    (model : Transformations) =
    //    let pChanged = 
    //        if not (model.pitch.value = 0.0) && not (model.yaw.value = 0.0) && not (model.roll.value = 0.0) then true else false
    //    let trafo' = (model.translation.value |> Trafo3d.Translation)
    //    //{ model with yaw = yaw; pitch = pitch; roll = roll; trafo = trafo'; scaling = scale}
    //    { model with trafo = trafo'; pivotChanged = pChanged; (*yaw = yaw; pitch = pitch; roll = roll; scaling = scale*)} 
    

    let update<'a> 
        (model : Transformations)
        (act : Action) 
        (refSys : ReferenceSystem)=
        match act with
        | SetTranslation t ->    
            let t' = Vector3d.update model.translation t
            { model with translation =  t'; trafoChanged = true} 
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
            { model with flipZ = (not model.flipZ); pivotChanged = false} 
        | ToggleVisible   -> 
            { model with useTranslationArrows = not model.useTranslationArrows}
        | ToggleSketchFab   -> 
            { model with isSketchFab = not model.isSketchFab; pivotChanged = false }
        | SetPivotPoint p -> // object space
            if model.usePivot then
                let p' = Vector3d.update model.pivot p
                { model with pivot = p'; oldPivot = p'.value; trafoChanged = false} 
            else model
        | SetPickedPivotPoint p -> // world space.......
            if model.usePivot then
                //let newRefSys = ReferenceSystemApp.updateCoordSystem p refSys.planet refSys
                let fulTrafo : Trafo3d = fullTrafo' model refSys None None
                let pivotObjectSpace = fulTrafo.Inverse.TransformPos(p) 
                let p' = Vector3d.updateV3d model.pivot pivotObjectSpace
                { model with pivot = p'; oldPivot = p'.value; trafoChanged = false} 
             else model
        | SetPickedReferenceSystem p -> // world space.......
            let newRefSys = ReferenceSystemApp.updateCoordSystem p refSys.planet refSys
            let north = newRefSys.northO.Normalized        
            let up    = newRefSys.up.value.Normalized
            let east  = north.Cross(up).Normalized
            let af = Affine3d(M33d.FromCols(north, east, up), p) 
            { model with refSys = Some af}
        | TogglePivotVisible -> 
            { model with showPivot = not model.showPivot}
        | SetScaling a ->
            { model with scaling = Numeric.update model.scaling a; pivotChanged = false; trafoChanged = true }
        | ToggleUsePivot   -> 
            { model with usePivot = not model.usePivot}
        | SetPivotSize s ->    
            let ps = Numeric.update model.pivotSize s
            { model with pivotSize = ps }
        | ToggleRefSysVisible -> 
            { model with showTrafoRefSys = not model.showTrafoRefSys}
        | SetRefSysSize s ->    
            let ps = Numeric.update model.refSysSize s
            { model with refSysSize = ps }
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

        let viewLocalRefSysData (refSys : aval<Option<Affine3d>> ) =
            let posStr = refSys|> AVal.map(fun refS -> match refS with | Some rf -> rf.Trans.ToString("0.00") | None -> ("NaN"))
            let northStr = refSys|> AVal.map(fun refS -> match refS with | Some rf -> rf.Linear.C0.ToString("0.00") | None -> ("NaN"))
            let upStr = refSys|> AVal.map(fun refS -> match refS with | Some rf -> rf.Linear.C2.ToString("0.00") | None -> ("NaN"))
            let eastStr = refSys|> AVal.map(fun refS -> match refS with | Some rf -> rf.Linear.C1.ToString("0.00") | None -> ("NaN"))

            Html.table [                                                
                        Html.row "Pos:"   [Incremental.text posStr]
                        Html.row "North:" [Incremental.text northStr]
                        Html.row "Up:"    [Incremental.text upStr]
                        Html.row "East:"  [Incremental.text eastStr]
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
                    Html.row "show local RefSys:" [GuiEx.iconCheckBox model.showTrafoRefSys ToggleRefSysVisible ]
                    Html.row "Local Reference System:" [viewLocalRefSysData model.refSys]
                    Html.row "RefSys Size:"      [Numeric.view' [InputBox] model.refSysSize |> UI.map SetRefSysSize]
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

        let viewTrafoRefSys (model:AdaptiveTransformations) = 
            model.refSys |> AVal.map(fun rf ->
            match rf with
            | Some rS ->
                let trafo = Trafo3d(rS) |> AVal.constant
                Sg.coordinateCross model.refSysSize.value
                |> Sg.noEvents
                |> Sg.trafo trafo
                |> Sg.effect [              
                    Shader.stableTrafo |> toEffect 
                    DefaultSurfaces.vertexColor |> toEffect
                ] |> Sg.onOff model.showTrafoRefSys
             | None -> Sg.empty
             ) |> Sg.dynamic
