namespace PRo3D.Core

open System

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation

module AnnotationExportApp =

    let private toggle (value : 'a) (set : HashSet<'a>) =
        if set |> HashSet.contains value then set |> HashSet.remove value
        else set |> HashSet.add value

    /// Any manual change invalidates the preset — the settings no longer are
    /// what the preset describes.
    let private custom (model : AnnotationExportModel) =
        { model with preset = ExportPreset.Custom }

    let update (model : AnnotationExportModel) (action : AnnotationExportAction) =
        match action with
        | Open  -> { model with isOpen = true }
        | Close -> { model with isOpen = false }

        | SetPreset preset ->
            let settings =
                AnnotationExportModel.toSettings model
                |> AnnotationExportSettings.applyPreset preset
            AnnotationExportModel.ofSettings model.isOpen preset settings

        | SetFormat format           -> custom { model with format = format }
        | SetGranularity granularity -> custom { model with granularity = granularity }
        | SetScope scope             -> custom { model with scope = scope }
        | SetCoordinates coordinates -> custom { model with coordinates = coordinates }
        | SetLongitude longitude     -> custom { model with longitude = longitude }
        | ToggleSampledPoints        -> custom { model with useSampledPoints = not model.useSampledPoints }

        | ToggleAnnotationField field ->
            custom { model with annotationFields = model.annotationFields |> toggle field }
        | TogglePointField field ->
            custom { model with pointFields = model.pointFields |> toggle field }

        | SetAllAnnotationFields selected ->
            custom { model with
                       annotationFields =
                         if selected then HashSet.ofList AnnotationFields.all else HashSet.empty }

        // needs the surface model to sample surface attributes, so it is
        // handled at viewer level; see ViewerApp.
        | Export _ -> model

    // -------------------------------------------------------------- view ---

    /// Select box over a fixed list of values with custom labels. `Html.SemUi.dropDown`
    /// would label the options with the raw enum names.
    let private dropDown (values : list<'a>) (labelOf : 'a -> string) (selected : aval<'a>) (change : 'a -> 'msg) =
        let optionAttributes (value : 'a) =
            AttributeMap.ofListCond [
                always (attribute "value" (labelOf value))
                onlyWhen (selected |> AVal.map (fun s -> s = value)) (attribute "selected" "selected")
            ]

        let onChange =
            onEvent "onchange" [ "event.target.selectedIndex" ] (fun args ->
                // imperative event callback — forcing is fine here, and gives us
                // a total fallback if the index cannot be resolved
                let current = AVal.force selected
                match args with
                | index :: _ ->
                    match Int32.TryParse index with
                    | true, i -> values |> List.tryItem i |> Option.defaultValue current |> change
                    | _       -> change current
                | [] -> change current)

        Incremental.select
            (AttributeMap.ofList [ onChange; style "color:black; width:100%" ])
            (values |> List.map (fun v -> Incremental.option (optionAttributes v) (AList.single (text (labelOf v)))) |> AList.ofList)

    let private checkBox (label : string) (isSet : aval<bool>) (action : 'msg) =
        div [ clazz "item"; style "padding: 1px 0px" ] [
            GuiEx.iconCheckBox isSet action
            Html.SemUi.stuffStack [ text (" " + label) ]
        ]

    let private sectionHeader (title : string) =
        div [ clazz "ui tiny inverted header"; style "margin-top: 8px" ] [ text title ]

    let private annotationFieldSection (model : AdaptiveAnnotationExportModel) (group : AnnotationFieldGroup) (title : string) =
        let fields = AnnotationFields.all |> List.filter (fun f -> AnnotationFields.groupOf f = group)
        div [] [
            yield sectionHeader title
            for field in fields do
                yield checkBox
                        (AnnotationFields.label field)
                        (model.annotationFields |> ASet.contains field)
                        (ToggleAnnotationField field)
        ]

    /// Explains what the chosen granularity does to the geometry. Without this
    /// it is invisible that "one record per annotation" collapses the whole
    /// annotation to a single coordinate.
    let private granularityHint (model : AdaptiveAnnotationExportModel) =
        Incremental.div (AttributeMap.ofList [ clazz "ui small message" ]) (
            alist {
                let! granularity = model.granularity
                match granularity with
                | ExportGranularity.PerAnnotation ->
                    yield text "One record per annotation. The coordinate is the bounding-box centre of the annotation, not its vertices — switch to \"one record per point\" to export every vertex."
                | _ ->
                    yield text "One record per point of every exported annotation. Annotation attributes are repeated on each of its rows."
            })

    let private pointAttributeSection (model : AdaptiveAnnotationExportModel) =
        Incremental.div AttributeMap.empty (
            alist {
                let! granularity = model.granularity
                if granularity = ExportGranularity.PerPoint then
                    yield sectionHeader "Point attributes"

                    for field in AnnotationFields.allPointFields do
                        yield checkBox
                                (AnnotationFields.pointLabel field)
                                (model.pointFields |> ASet.contains field)
                                (TogglePointField field)

                    // Placeholder for the per-point surface properties (OPC
                    // scalar / texture layers sampled at the point). The
                    // sampling itself is being implemented separately.
                    yield sectionHeader "Surface properties at each point"
                    yield div [ clazz "ui tiny inverted text"; style "opacity: 0.6" ] [
                        text "Not available yet — sampling the surface layers at each point is still being implemented." ]
            })

    /// The save dialog's filter has to follow the chosen file type, so the
    /// client event string is built adaptively rather than as a constant.
    let private exportButton (model : AdaptiveAnnotationExportModel) =
        let attributes =
            amap {
                let! format = model.format
                let extension = AnnotationExportSettings.fileExtension format
                let label = AnnotationExportSettings.formatLabel format
                yield clazz "ui primary button"
                yield Dialogs.onSaveFile Export
                yield clientEvent "onclick"
                        (sprintf
                            "top.aardvark.dialog.showSaveDialog({ title: 'Export Annotations', filters: [{ name: '%s', extensions: ['%s'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"
                            label extension)
            } |> AttributeMap.ofAMap

        Incremental.div attributes (AList.single (text "Export..."))

    /// Body of the export window.
    let view (model : AdaptiveAnnotationExportModel) =
        require GuiEx.semui (
            div [ clazz "ui inverted segment"; style "min-width: 420px; max-width: 520px" ] [
                div [ clazz "ui inverted header" ] [ text "Export annotations" ]

                Html.table [
                    Html.row "File type:" [
                        dropDown
                            [ ExportFormat.Csv; ExportFormat.GeoJson; ExportFormat.Attitude ]
                            AnnotationExportSettings.formatLabel model.format SetFormat ]
                    Html.row "Preset:" [
                        dropDown ExportPreset.all ExportPreset.label model.preset SetPreset ]
                    Html.row "Scope:" [
                        dropDown
                            [ ExportScope.All; ExportScope.Visible; ExportScope.Selected ]
                            AnnotationExportSettings.scopeLabel model.scope SetScope ]
                ]

                // Everything below only applies to the configurable formats.
                Incremental.div AttributeMap.empty (
                    alist {
                        let! format = model.format
                        if AnnotationExportSettings.hasFixedSchema format then
                            yield div [ clazz "ui small message" ] [
                                text "Attitude planes have a fixed schema defined by the external tool that reads them. Only the scope applies." ]
                        else
                            yield Html.table [
                                Html.row "Granularity:" [
                                    dropDown
                                        [ ExportGranularity.PerAnnotation; ExportGranularity.PerPoint ]
                                        AnnotationExportSettings.granularityLabel model.granularity SetGranularity ]
                                Html.row "Coordinates:" [
                                    dropDown
                                        [ CoordinateMode.Cartesian; CoordinateMode.Geographic; CoordinateMode.Both ]
                                        AnnotationExportSettings.coordinateLabel model.coordinates SetCoordinates ]
                                Html.row "Longitude:" [
                                    dropDown
                                        [ LongitudeConvention.Native; LongitudeConvention.Flipped; LongitudeConvention.Signed ]
                                        AnnotationExportSettings.longitudeLabel model.longitude SetLongitude ]
                                Html.row "Sampled points:" [
                                    GuiEx.iconCheckBox model.useSampledPoints ToggleSampledPoints
                                    text " include the surface-following points between the picked ones" ]
                            ]

                            yield granularityHint model

                            yield div [ clazz "ui divider" ] []
                            yield div [ style "display:flex; gap:6px; align-items:center" ] [
                                sectionHeader "Annotation attributes"
                                button [ clazz "ui mini button"; onClick (fun _ -> SetAllAnnotationFields true) ] [ text "all" ]
                                button [ clazz "ui mini button"; onClick (fun _ -> SetAllAnnotationFields false) ] [ text "none" ]
                            ]
                            yield div [ style "max-height: 240px; overflow-y: auto" ] [
                                annotationFieldSection model Identity "Identity"
                                annotationFieldSection model Measurements "Measurements"
                                annotationFieldSection model Ellipse "Ellipse"
                                annotationFieldSection model DipAndStrike "Dip and strike"
                                annotationFieldSection model ErrorMeasures "Planar fit errors"
                            ]

                            yield div [ clazz "ui divider" ] []
                            yield div [ style "max-height: 240px; overflow-y: auto" ] [
                                pointAttributeSection model
                            ]
                    })

                div [ clazz "ui divider" ] []
                div [ style "display:flex; gap:8px; justify-content:flex-end" ] [
                    button [ clazz "ui button"; onClick (fun _ -> Close) ] [ text "Cancel" ]
                    exportButton model
                ]
            ]
        )

    /// Renders `view` as a centred overlay. The panel is simply absent from the
    /// DOM while closed, so there is no JS modal state to keep in sync with the
    /// model.
    let viewModal (model : AdaptiveAnnotationExportModel) =
        Incremental.div AttributeMap.empty (
            alist {
                let! isOpen = model.isOpen
                if isOpen then
                    yield div [
                        clazz "annotation-export-dimmer"
                        style "position:fixed; inset:0; background:rgba(0,0,0,0.55); z-index:20000; display:flex; align-items:center; justify-content:center"
                        onClick (fun _ -> Close)
                    ] [
                        // stop the click on the panel itself from closing the window
                        onBoot "$('#__ID__').on('click', function(e) { e.stopPropagation(); });" (
                            div [ style "max-height:90vh; overflow-y:auto" ] [
                                view model
                            ])
                    ]
            })
