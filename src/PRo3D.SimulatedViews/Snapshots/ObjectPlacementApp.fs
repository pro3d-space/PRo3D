namespace PRo3D.SimulatedViews

open System
open Aardvark.Base
open Adaptify
open Aardvark.UI
open PRo3D.Core.Surface
open PRo3D.Core
open FSharp.Data.Adaptive

/// GUI interface to object placement parameters
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ObjectPlacementApp =

    let toObjectPlacementParameters (model : ObjectPlacementApp) 
                              (name  : string)
                              (colorCorrection : ColorCorrection) : ObjectPlacementParameters =
        {
            name         = name
            count        = int model.count.value
            color        = if colorCorrection.useColor then Some colorCorrection.color.c else None
            contrast     = if colorCorrection.useContrast then Some colorCorrection.contrast.value else None
            brightness   = if colorCorrection.useBrightn then Some colorCorrection.brightness.value else None
            gamma        = if colorCorrection.useGamma then Some colorCorrection.gamma.value else None
            scale        = (int model.scaleFrom.value, int model.scaleTo.value) |> V2i |> Some
            xRotation    = (int model.xRotationFrom.value, int model.xRotationTo.value) |> V2i |> Some
            yRotation    = (int model.yRotationFrom.value, int model.yRotationTo.value) |> V2i |> Some
            zRotation    = (int model.zRotationFrom.value, int model.zRotationTo.value) |> V2i |> Some
            maxDistance  = Some model.maxDistance.value
            subsurface   = model.subsurface.value |> int |> Some
            maskColor    = model.maskColor.c |> Some
        }

    let fromPLacementParameters (ssc : ObjectPlacementParameters) =
        let init = ObjectPlacementApp.init
        let scaleFrom =
            match ssc.scale with
                | Some scale -> 
                  {init.scaleFrom with value = float scale.X}
                | None ->  init.scaleFrom
        let scaleTo =
            match ssc.scale with
                | Some scale -> 
                  {init.scaleTo with value = float scale.Y}
                | None ->  init.scaleTo
        let xRotationFrom, xRotationTo =
            match ssc.xRotation with
                | Some xRotation -> 
                  ({init.xRotationFrom with value = float xRotation.X},
                    {init.xRotationTo with value = float xRotation.Y})
                | None ->  (init.xRotationFrom, init.xRotationTo)
        let yRotationFrom, yRotationTo =
            match ssc.yRotation with
                | Some yRotation -> 
                  ({init.yRotationFrom with value = float yRotation.X},
                    {init.yRotationTo with value = float yRotation.Y})
                | None ->  (init.yRotationFrom, init.yRotationTo)
        let zRotationFrom, zRotationTo =
            match ssc.zRotation with
                | Some zRotation -> 
                  ({init.zRotationFrom with value = float zRotation.X},
                    {init.zRotationTo with value = float zRotation.Y})
                | None ->  (init.zRotationFrom, init.zRotationTo)
        let maxDistance =
            match ssc.maxDistance with
                | Some maxDistance -> 
                  {init.maxDistance with value = float maxDistance}
                | None ->  init.maxDistance   
        let subsurface =
            match ssc.subsurface with
                | Some subsurface -> 
                  {init.subsurface with value = float subsurface}
                | None ->  init.subsurface              

        let maskColor =
            match ssc.maskColor with
            | Some c -> {c = c}
            | None -> {c = C4b.Green}

        {init with
            name            = ssc.name
            count           = {init.count with value = float ssc.count}
            scaleFrom       = scaleFrom
            scaleTo         = scaleTo
            xRotationFrom   = xRotationFrom
            xRotationTo     = xRotationTo
            yRotationFrom   = yRotationFrom 
            yRotationTo     = yRotationTo 
            zRotationFrom   = zRotationFrom
            zRotationTo     = zRotationTo
            maxDistance     = maxDistance
            subsurface      = subsurface
            maskColor       = maskColor
        }    
    
    let updateSnapshotSettings (model : SnapshotSettings) (message : SnapshotSettingsAction) = //TODO rno move to own file and module
        match message with
        | SetNumSnapshots num ->  
            {model with numSnapshots     = Numeric.update model.numSnapshots  num} 
        | SetFieldOfView       num -> 
            {model with fieldOfView     = Numeric.update model.fieldOfView  num}   
        | SetRenderMask b ->
            {model with renderMask = b}
    
    let update (model : ObjectPlacementApp) (message : ObjectPlacementAction) =
        match message with
        | SetName        str ->  
            {model with name = str}
        | SetCount       num -> 
            {model with count            = Numeric.update model.count         num}
        | ScaleFrom      num -> 
            {model with scaleFrom        = Numeric.update model.scaleFrom     num}
        | ScaleTo        num -> 
            {model with scaleTo          = Numeric.update model.scaleTo       num}
        | XRotationFrom  num -> 
            {model with xRotationFrom    = Numeric.update model.xRotationFrom num}
        | XRotationTo    num -> 
            {model with xRotationTo      = Numeric.update model.xRotationTo   num}
        | YRotationFrom  num -> 
            {model with yRotationFrom    = Numeric.update model.yRotationFrom num}
        | YRotationTo    num -> 
            {model with yRotationTo      = Numeric.update model.yRotationTo   num}
        | ZRotationFrom  num -> 
            {model with zRotationFrom    = Numeric.update model.zRotationFrom num}
        | ZRotationTo    num -> 
            {model with zRotationTo      = Numeric.update model.zRotationTo   num}
        | SetMaxDistance num -> 
            {model with maxDistance      = Numeric.update model.maxDistance   num}
        | SetSubsurface  num -> 
            {model with subsurface       = Numeric.update model.subsurface    num} 
        | SetMaskColor colorAction ->
            {model with maskColor = ColorPicker.update model.maskColor colorAction}

    let view (model : AdaptiveObjectPlacementApp) = //(selectedName : IMod<string>) =
       require Html.semui (
            Html.table 
              [      
                //Html.row "Name                :" [Html.SemUi.textBox selectedName SetName ]
                Html.row "mask color:"  
                    [
                        ColorPicker.view model.maskColor |> UI.map SetMaskColor 
                    ]
                Html.row "count               :" [ Numeric.view' [InputBox] model.count           |> UI.map SetCount        ]
                Html.row "scaleFrom           :" [ Numeric.view' [InputBox] model.scaleFrom       |> UI.map ScaleFrom       ]
                Html.row "scaleTo             :" [ Numeric.view' [InputBox] model.scaleTo         |> UI.map ScaleTo         ]
                Html.row "xRotationFrom       :" [ Numeric.view' [InputBox] model.xRotationFrom   |> UI.map XRotationFrom   ]
                Html.row "xRotationTo         :" [ Numeric.view' [InputBox] model.xRotationTo     |> UI.map XRotationTo     ]
                Html.row "yRotationFrom       :" [ Numeric.view' [InputBox] model.yRotationFrom   |> UI.map YRotationFrom   ]
                Html.row "yRotationTo         :" [ Numeric.view' [InputBox] model.yRotationTo     |> UI.map YRotationTo     ]
                Html.row "zRotationFrom       :" [ Numeric.view' [InputBox] model.zRotationFrom   |> UI.map ZRotationFrom   ]
                Html.row "zRotationTo         :" [ Numeric.view' [InputBox] model.zRotationTo     |> UI.map ZRotationTo     ]
                Html.row "maxDistance         :" [ Numeric.view' [InputBox] model.maxDistance     |> UI.map SetMaxDistance  ]
                Html.row "subsurface          :" [ Numeric.view' [InputBox] model.subsurface      |> UI.map SetSubsurface   ]
              ]           
            )

    let viewSelected (surfaceModel : AdaptiveSurfaceModel) (models : amap<String, AdaptiveObjectPlacementApp>) =
        adaptive {
            let! guid = surfaceModel.surfaces.singleSelectLeaf
            match guid with
            | Some i -> 
                let! leaf =
                    AMap.tryFind i surfaceModel.surfaces.flat 
                match leaf with
                | Some leaf ->
                    let surface = Surface.SurfaceUtils.leafToSurface leaf
                    let! name = surface.name
                    let! exists = (models |> AMap.keys) |> ASet.contains name
                    if exists then
                        let! placement = models |> AMap.find name
                        return name, view placement
                    else 
                        let empty = div[ style "font-style:italic"][ text "only OBJ models have placement options" ] 
                        return name, empty
                | None ->
                    let empty = div[ style "font-style:italic"][ text "surface id not found" ]
                    return "", empty 
            | None -> 
                let empty = div[ style "font-style:italic"][ text "no surface selected" ] 
                return "", empty 
        }         

