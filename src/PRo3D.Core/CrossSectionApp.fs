namespace PRo3D.Core

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D.Base

type CrossSectionAction =
    | SetCrossSection          of CrossSection
    | ClearCrossSection
    | ToggleCurtainEnabled
    | SetCurtainTexturePath    of string
    | SetCurtainExtrusionDepth of Numeric.Action
    | ToggleCurtainAbsoluteMode
    | SetCurtainTargetAltitude of Numeric.Action
    | SetCurtainTextureDepth          of Numeric.Action
    | SetCurtainTextureStartAltitude  of Numeric.Action
    | ChangeCurtainBaseColor          of ColorPicker.Action
    | ToggleClippingEnabled

module CrossSectionApp =

    let update (model : CrossSectionModel) (action : CrossSectionAction) =
        match action with
        | SetCrossSection cs ->
            { model with crossSection = Some cs }
        | ClearCrossSection ->
            { model with crossSection = None }
        | ToggleCurtainEnabled ->
            { model with curtainEnabled = not model.curtainEnabled }
        | SetCurtainTexturePath path ->
            { model with curtainTexturePath = if path = "" then None else Some path }
        | SetCurtainExtrusionDepth a ->
            { model with curtainExtrusionDepth = Numeric.update model.curtainExtrusionDepth a }
        | ToggleCurtainAbsoluteMode ->
            { model with curtainAbsoluteMode = not model.curtainAbsoluteMode }
        | SetCurtainTargetAltitude a ->
            { model with curtainTargetAltitude = Numeric.update model.curtainTargetAltitude a }
        | SetCurtainTextureDepth a ->
            { model with curtainTextureDepth = Numeric.update model.curtainTextureDepth a }
        | SetCurtainTextureStartAltitude a ->
            { model with curtainTextureStartAltitude = Numeric.update model.curtainTextureStartAltitude a }
        | ChangeCurtainBaseColor a ->
            { model with curtainBaseColor = ColorPicker.update model.curtainBaseColor a }
        | ToggleClippingEnabled ->
            { model with clippingEnabled = not model.clippingEnabled }

    let viewCurtainSettings (model : AdaptiveCrossSectionModel) =
        let jsImportTextureDialog =
            "top.aardvark.dialog.showOpenDialog({title:'Select Cross Section Texture', filters: [{ name: 'Images (*.png, *.jpg, *.jpeg, *.tif, *.tiff)', extensions: ['png','jpg','jpeg','tif','tiff']},], properties: ['openFile']}).then(result => {aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
        require GuiEx.semui (
            Html.table [
                Html.row "Cross Section:" [
                    Incremental.div AttributeMap.empty (
                        alist {
                            let! cs = model.crossSection
                            match cs with
                            | Some _ ->
                                yield button [clazz "ui icon button"; onClick (fun _ -> ClearCrossSection)] [
                                        i [clazz "remove icon red"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Remove"
                            | None ->
                                yield text "none (create one from an annotation)"
                        }
                    )
                ]
                Html.row "Clipping:"          [ GuiEx.iconCheckBox model.clippingEnabled ToggleClippingEnabled ]
                Html.row "Curtain:"           [ GuiEx.iconCheckBox model.curtainEnabled ToggleCurtainEnabled ]
                Html.row "Absolute Altitude:" [ GuiEx.iconCheckBox model.curtainAbsoluteMode ToggleCurtainAbsoluteMode ]
                Html.row "Texture Path:"      [
                    button [
                        clazz "ui icon button"
                        Dialogs.onChooseFiles (fun paths ->
                            match paths with
                            | p :: _ -> SetCurtainTexturePath p
                            | [] -> SetCurtainTexturePath "")
                        clientEvent "onclick" jsImportTextureDialog
                    ] [ i [clazz "folder open icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Choose image"
                    br []
                    Incremental.text (model.curtainTexturePath |> AVal.map (Option.defaultValue "none")) ]
                Html.row "Depth / Alt (m):" [
                    Incremental.div AttributeMap.empty (
                        alist {
                            let! abs = model.curtainAbsoluteMode
                            if abs then
                                yield Numeric.view' [InputBox] model.curtainTargetAltitude |> UI.map SetCurtainTargetAltitude
                            else
                                yield Numeric.view' [InputBox] model.curtainExtrusionDepth |> UI.map SetCurtainExtrusionDepth
                        }
                    )
                ]
                Html.row "Tex Start Alt (m):" [Numeric.view' [InputBox] model.curtainTextureStartAltitude |> UI.map SetCurtainTextureStartAltitude]
                Html.row "Tex Depth (m):" [Numeric.view' [InputBox] model.curtainTextureDepth |> UI.map SetCurtainTextureDepth]
                Html.row "Base Color:" [ColorPicker.view model.curtainBaseColor |> UI.map ChangeCurtainBaseColor]
            ]
        )
