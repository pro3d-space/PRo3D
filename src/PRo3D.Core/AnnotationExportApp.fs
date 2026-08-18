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
        // Touching any control invalidates a warning about the previous attempt:
        // it described settings that no longer hold. The two viewer-level actions
        // are excluded because that is where a new warning is set.
        let model =
            match action with
            | Export _ | StopContinuous -> model
            | _                         -> { model with warning = None }

        match action with
        | Open  -> { model with isOpen = true }
        | Close -> { model with isOpen = false }

        | SetPreset preset ->
            let settings =
                AnnotationExportModel.toSettings model
                |> AnnotationExportSettings.applyPreset preset
            AnnotationExportModel.ofSettings model.isOpen preset settings

        | SetFormat format ->
            // keep the model in step with the dropdown: it must never hold a
            // coordinate mode the chosen format no longer offers, or the select
            // would show one thing while the export does another
            let coordinates =
                if AnnotationExportSettings.coordinateModesFor format |> List.contains model.coordinates
                then model.coordinates
                else CoordinateMode.Geographic
            custom { model with format = format; coordinates = coordinates }
        | SetGranularity granularity -> custom { model with granularity = granularity }
        | SetScope scope             -> custom { model with scope = scope }
        | SetCoordinates coordinates -> custom { model with coordinates = coordinates }
        | SetLongitude longitude     -> custom { model with longitude = longitude }
        | ToggleSignedLongitude      -> custom { model with signedLongitude = not model.signedLongitude }
        | ToggleSampledPoints        -> custom { model with useSampledPoints = not model.useSampledPoints }

        // Key is always exported (see AnnotationExportModel.toSettings), so its
        // checkbox is inert rather than lying about the output.
        | ToggleAnnotationField AnnotationField.Key -> model
        | ToggleAnnotationField field ->
            custom { model with annotationFields = model.annotationFields |> toggle field }
        | TogglePointField field ->
            custom { model with pointFields = model.pointFields |> toggle field }

        | SetAllAnnotationFields selected ->
            custom { model with
                       annotationFields =
                         if selected then HashSet.ofList AnnotationFields.all
                         else HashSet.single AnnotationField.Key }

        | SetAllPointFields selected ->
            custom { model with
                       pointFields =
                         if selected then HashSet.ofList AnnotationFields.allPointFields
                         else HashSet.empty }

        // needs the surface model to sample surface attributes, so it is
        // handled at viewer level; see ViewerApp.
        | Export _ -> model
        // the armed state lives on DrawingModel, likewise viewer level
        | StopContinuous -> model

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

    /// Tick box and its label on one line. `Html.SemUi.stuffStack` wraps the
    /// label in a block element, which pushed it onto the next row.
    let private checkBox (label : string) (isSet : aval<bool>) (action : 'msg) =
        div [ style "display:flex; align-items:center; gap:6px; padding:2px 0" ] [
            GuiEx.iconCheckBox isSet action
            span [ style "line-height:1.2" ] [ text label ]
        ]

    let private sectionHeader (title : string) =
        div [ clazz "ui tiny inverted header"; style "margin:10px 0 4px 0" ] [ text title ]

    /// Accordion for the attribute lists.
    ///
    /// Not `GuiEx.accordionWithHeader`: that one reserves the right-hand slot
    /// for an icon and lets the title row size itself, so a header carrying
    /// buttons ends up taller than one without. Here the right-hand slot holds
    /// the buttons and the row has a fixed height, so the two accordions match
    /// whether or not they have any.
    let private attributeAccordion (title : string) (headerRight : list<DomNode<'msg>>) (content : list<DomNode<'msg>>) =
        let stopClick =
            onBoot "$('#__ID__').on('click', function(e) { e.stopPropagation(); } );"

        onBoot "$('#__ID__').accordion();" (
            div [ clazz "ui inverted segment"; style "margin:0 0 8px 0; padding:0" ] [
                div [ clazz "ui inverted accordion fluid" ] [
                    div [
                        clazz "title inverted"
                        style "background-color:#282828; display:flex; align-items:center; \
                               height:36px; padding:0 8px; box-sizing:border-box"
                    ] [
                        i [ clazz "dropdown icon"; style "margin:0 6px 0 0" ] []
                        span [ style "flex:1 1 auto" ] [ text title ]
                        stopClick (div [ style "display:flex; align-items:center; gap:4px" ] headerRight)
                    ]
                    div [ clazz "content"; style "padding:6px 10px 10px 10px" ] content
                ]
            ]
        )

    /// Groups settings that belong together, with a little breathing room.
    let private settingsGroup (content : list<DomNode<'msg>>) =
        div [ style "margin-bottom:10px" ] content

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

    /// Explains what the chosen granularity does to the geometry. It is not
    /// self-evident, and it means something different per file type: a CSV row
    /// holds one coordinate, whereas a GeoJSON feature carries a whole polyline.
    let private granularityHint (model : AdaptiveAnnotationExportModel) =
        Incremental.div (AttributeMap.ofList [ clazz "ui small message" ]) (
            alist {
                let! format = model.format
                let! granularity = model.granularity
                match format, granularity with
                | ExportFormat.GeoJson, ExportGranularity.PerAnnotation ->
                    yield text "One feature per annotation, carrying its full geometry (LineString / Polygon). The geometry attribute stores all vertex coordinates and the properties hold the bounding-box centre coordinates."
                | ExportFormat.GeoJson, _ ->
                    yield text "One Point feature per vertex. Note that a GIS evaluates labels and symbology per feature, so this is how per-point values become individually styleable."
                | _, ExportGranularity.PerAnnotation ->
                    yield text "One row per annotation. The coordinate columns hold the bounding-box centre; the individual vertices are not in the file - switch to \"one record per point\" to export every vertex."
                | _ ->
                    yield text "One row per point of every exported annotation. Annotation attributes are repeated on each of its rows."
            })

    /// Contents of the point-attribute accordion. The caller decides whether to
    /// show it at all, so there is no granularity check here.
    let private pointAttributeFields (model : AdaptiveAnnotationExportModel) =
        [ for field in AnnotationFields.allPointFields do
            yield checkBox
                    (AnnotationFields.pointLabel field)
                    (model.pointFields |> ASet.contains field)
                    (TogglePointField field)

          // Placeholder for the per-point surface properties (OPC scalar /
          // texture layers sampled at the point). The sampling itself is being
          // implemented separately.
          yield sectionHeader "Surface properties at each point"
          yield div [ clazz "ui tiny inverted text"; style "opacity: 0.6" ] [
              text "Not available yet — sampling the surface layers at each point is still being implemented." ] ]

    /// The save dialog's filter has to follow the chosen file type, so the
    /// client event string is built adaptively rather than as a constant.
    let private saveButton (model : AdaptiveAnnotationExportModel) =
        let attributes =
            amap {
                let! format = model.format
                let extension = AnnotationExportSettings.fileExtension format
                let label = AnnotationExportSettings.formatLabel format
                let title =
                    if AnnotationExportSettings.isContinuous format then "Continuously Export Annotations To"
                    else "Export Annotations"
                yield clazz "ui primary button"
                yield Dialogs.onSaveFile Export
                yield clientEvent "onclick"
                        (sprintf
                            "top.aardvark.dialog.showSaveDialog({ title: '%s', filters: [{ name: '%s', extensions: ['%s'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"
                            title label extension)
            } |> AttributeMap.ofAMap

        // "Start..." reads better than "Export..." for a background export that
        // keeps running after the window closes
        let caption =
            model.format
            |> AVal.map (fun format ->
                if AnnotationExportSettings.isContinuous format then "Start..." else "Export...")

        Incremental.div attributes (AList.single (Incremental.text caption))

    /// The primary footer button. While a background export is running and the
    /// continuous file type is selected it stops that export instead of opening
    /// a save dialog — this window is the only place it can be switched off.
    let private exportButton (state : ContinuousExportState) (model : AdaptiveAnnotationExportModel) =
        Incremental.div AttributeMap.empty (
            alist {
                let! format = model.format
                let! isRunning = state.isRunning
                if AnnotationExportSettings.isContinuous format && isRunning then
                    yield button [ clazz "ui red button"; onClick (fun _ -> StopContinuous) ]
                                 [ text "Stop continuous export" ]
                else
                    yield saveButton model
            })

    /// The scrolling middle of the window. The header and the buttons live
    /// outside it so they stay put however long this gets.
    let private settings (state : ContinuousExportState) (model : AdaptiveAnnotationExportModel) =
        div [ style "flex: 1 1 auto; overflow-y: auto; padding: 10px 12px" ] [
            // The preset only pre-fills everything below, so it sits on its own.
            settingsGroup [
                Html.table [
                    Html.row "Preset:" [
                        dropDown ExportPreset.all ExportPreset.label model.preset SetPreset ]
                ]
            ]

            settingsGroup [
                Html.table [
                    Html.row "File type:" [
                        dropDown
                            AnnotationExportSettings.allFormats
                            AnnotationExportSettings.formatLabel model.format SetFormat ]
                    Html.row "Scope:" [
                        dropDown
                            [ ExportScope.All; ExportScope.Visible; ExportScope.Selected ]
                            AnnotationExportSettings.scopeLabel model.scope SetScope ]
                ]
            ]

            // Everything below only applies to the configurable formats.
            Incremental.div AttributeMap.empty (
                alist {
                    let! format = model.format
                    let! granularity = model.granularity
                    if AnnotationExportSettings.isContinuous format then
                        let! target = state.target
                        yield div [ clazz "ui small message" ] [
                            yield text "Choosing a file arms a background export: PRo3D rewrites it as line-delimited GeoJSON whenever the annotations change. No individual attributes can be selected for this export."
                            match target with
                            | Some path ->
                                yield div [ style "margin-top:6px" ] [
                                    text (sprintf "Currently exporting to %s. Use the button below to stop, or pick another file to export there instead." path) ]
                            | None -> ()
                        ]
                    elif AnnotationExportSettings.hasFixedSchema format then
                        yield div [ clazz "ui small message" ] [
                            text "Attitude planes have a fixed schema defined by the external tool that reads them. No individual attributes can be selected for this export." ]
                    else
                        yield settingsGroup [
                            Html.table [
                                Html.row "Granularity:" [
                                    dropDown
                                        [ ExportGranularity.PerAnnotation; ExportGranularity.PerPoint ]
                                        AnnotationExportSettings.granularityLabel model.granularity SetGranularity ]
                                Html.row "Coordinates:" [
                                    dropDown
                                        (AnnotationExportSettings.coordinateModesFor format)
                                        AnnotationExportSettings.coordinateLabel model.coordinates SetCoordinates ]
                                Html.row "Longitude:" [
                                    dropDown
                                        AnnotationExportSettings.allLongitudeConventions
                                        AnnotationExportSettings.longitudeLabel model.longitude SetLongitude ]
                                Html.row "Longitude range:" [
                                    checkBox "write as -180...180 instead of 0...360"
                                        model.signedLongitude ToggleSignedLongitude ]
                                Html.row "Sampled points:" [
                                    checkBox "include the surface-following points between the picked ones"
                                        model.useSampledPoints ToggleSampledPoints ]
                            ]
                            granularityHint model
                        ]

                        // Collapsed by default: the lists are long and most
                        // exports are driven by a preset, so they only need
                        // opening when the columns are being tuned.
                        yield
                            attributeAccordion
                                "Annotation attributes"
                                [ button [ clazz "ui mini button"; style "margin:0"
                                           onClick (fun _ -> SetAllAnnotationFields true) ] [ text "all" ]
                                  button [ clazz "ui mini button"; style "margin:0"
                                           onClick (fun _ -> SetAllAnnotationFields false) ] [ text "none" ] ]
                                [ annotationFieldSection model Identity "Identity"
                                  annotationFieldSection model Measurements "Measurements"
                                  annotationFieldSection model Ellipse "Ellipse"
                                  annotationFieldSection model DipAndStrike "Dip and strike"
                                  annotationFieldSection model ErrorMeasures "Planar fit errors" ]

                        if granularity = ExportGranularity.PerPoint then
                            yield
                                attributeAccordion
                                    "Point attributes"
                                    [ button [ clazz "ui mini button"; style "margin:0"
                                               onClick (fun _ -> SetAllPointFields true) ] [ text "all" ]
                                      button [ clazz "ui mini button"; style "margin:0"
                                               onClick (fun _ -> SetAllPointFields false) ] [ text "none" ] ]
                                    (pointAttributeFields model)
                })
        ]

    /// Body of the export window: a fixed header, a scrolling middle and a
    /// fixed footer, so the title and the buttons never scroll out of reach.
    let view (state : ContinuousExportState) (model : AdaptiveAnnotationExportModel) =
        require GuiEx.semui (
            div [
                clazz "ui inverted segment"
                style "display:flex; flex-direction:column; min-width:440px; max-width:540px; max-height:90vh; padding:0; margin:0"
            ] [
                div [
                    clazz "ui inverted header"
                    style "flex:0 0 auto; margin:0; padding:12px; border-bottom:1px solid rgba(255,255,255,0.15)"
                ] [ text "Export annotations" ]

                // Part of the fixed header rather than the scrolling body, so a
                // warning cannot be scrolled out of sight.
                Incremental.div AttributeMap.empty (
                    alist {
                        let! warning = model.warning
                        match warning with
                        | Some message ->
                            yield div [
                                clazz "ui small warning message"
                                style "flex:0 0 auto; margin:10px 12px 0 12px"
                            ] [ text message ]
                        | None -> ()
                    })

                settings state model

                div [
                    style "flex:0 0 auto; display:flex; gap:8px; justify-content:flex-end; padding:10px 12px; border-top:1px solid rgba(255,255,255,0.15)"
                ] [
                    button [ clazz "ui button"; onClick (fun _ -> Close) ] [ text "Cancel" ]
                    exportButton state model
                ]
            ]
        )

    /// Renders `view` as a centred overlay. The panel is simply absent from the
    /// DOM while closed, so there is no JS modal state to keep in sync with the
    /// model.
    let viewModal (state : ContinuousExportState) (model : AdaptiveAnnotationExportModel) =
        Incremental.div AttributeMap.empty (
            alist {
                let! isOpen = model.isOpen
                if isOpen then
                    yield div [
                        clazz "annotation-export-dimmer"
                        style "position:fixed; inset:0; background:rgba(0,0,0,0.55); z-index:20000; display:flex; align-items:center; justify-content:center"
                        onClick (fun _ -> Close)
                    ] [
                        // stop the click on the panel itself from closing the window.
                        // No scrolling here — `view` scrolls its own middle so the
                        // header and buttons stay put.
                        onBoot "$('#__ID__').on('click', function(e) { e.stopPropagation(); });" (
                            div [ style "display:flex; max-height:90vh" ] [
                                view state model
                            ])
                    ]
            })
