namespace PRo3D.Viewer

open System
open System.IO

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives.Golden
open FSharp.Data.Adaptive

/// Window layouts: built-in dashboards, the user's library in AppData, the last used
/// layout, and the layout sidecar beside scenes. See docs/WindowLayouts.md.
module LayoutApp =

    let private dashboard (name : string) =
        DashboardModes.all |> List.tryFind (fun d -> d.name = name)

    let private libraryNames (dir : string) =
        LayoutLibrary.list dir |> List.map (fun f -> f.name)

    /// Makes `layout` the one shown: pushes it to the browser and remembers it.
    let private apply (dir : string) (name : string) (layout : WindowLayout) (m : LayoutModel) =
        let layout = LayoutOps.sanitize layout
        LayoutLibrary.storeCurrent dir { name = name; layout = layout }
        { m with
            golden      = { (GoldenLayout.update (GoldenLayout.Message.SetWindowLayout layout) m.golden) with DefaultLayout = layout }
            current     = layout
            pushConfirmed = false
            activeName  = name
            activeShape = LayoutOps.shape layout }

    let private create (dir : string) (name : string) (layout : WindowLayout) (dialog : LayoutDialog) =
        let layout = LayoutOps.sanitize layout
        {
            golden        = GoldenLayout.create LayoutConfig.Default layout
            current       = layout
            pushConfirmed = true
            activeName    = name
            activeShape   = LayoutOps.shape layout
            library       = libraryNames dir
            dialog        = dialog
            nameInput     = ""
        }

    /// The layout the user left last time, or the default dashboard. A last layout that
    /// cannot be read is reported in a dialog: a toast would be gone before the window shows.
    let initial (dir : string) : LayoutModel =
        let fallback = DashboardModes.defaultDashboard
        match LayoutLibrary.tryLoadCurrent dir with
        | Ok (Some file) -> create dir file.name file.layout LayoutDialog.None
        | Ok None -> create dir fallback.name fallback.layout LayoutDialog.None
        | Result.Error e ->
            let message =
                sprintf "Your last window layout could not be read (%s). PRo3D starts with the '%s' layout instead; the unreadable file was kept as current.json.corrupt in %s." e fallback.name dir
            create dir fallback.name fallback.layout (LayoutDialog.Notice(message, LayoutDialog.None))

    /// Opens `dialog`, behind a notice that is still waiting to be read.
    let private openDialog (dialog : LayoutDialog) (m : LayoutModel) =
        match m.dialog with
        | LayoutDialog.Notice (message, _) -> { m with dialog = LayoutDialog.Notice(message, dialog) }
        | _ -> { m with dialog = dialog }

    /// Name shown for the active layout; marks a layout whose arrangement the user changed.
    let displayName (m : AdaptiveLayoutModel) =
        (m.activeName, m.activeShape, m.current) |||> AVal.map3 (fun name shape current ->
            if LayoutOps.shape current = shape then name else name + " (modified)"
        )

    /// A scene was opened from `scenePath`: offer the layout stored beside it, unless
    /// there is none or it is arranged like the current one anyway.
    let sceneOpened (dir : string) (scenePath : string) (m : LayoutModel) : LayoutModel * list<string> =
        match SceneLayoutSidecar.tryRead scenePath with
        | Ok None -> m, []
        | Result.Error e ->
            Log.warn "[Layouts] ignoring the layout beside %s: %s" scenePath e
            m, ["The layout stored beside the scene could not be read and was ignored."]
        | Ok (Some file) ->
            let shape = LayoutOps.shape file.layout
            if shape = LayoutOps.shape m.current then m, []
            else
                let inLibrary =
                    LayoutLibrary.list dir |> List.exists (fun e ->
                        match e.layout with
                        | Ok l -> LayoutOps.shape l = shape
                        | Result.Error _ -> false
                    )
                let pending = {
                    sceneName         = Path.GetFileNameWithoutExtension scenePath
                    layout            = file.layout
                    alreadyInLibrary  = inLibrary
                    importIntoLibrary = false
                    apply             = true
                }
                openDialog (LayoutDialog.SceneLayout pending) m, []

    /// A library name derived from `name` that is not taken yet.
    let private freeName (names : list<string>) (name : string) =
        let taken (n : string) = names |> List.exists (fun x -> String.Equals(x, n, StringComparison.OrdinalIgnoreCase))
        if not (taken name) then name
        else
            let rec go i =
                let candidate = sprintf "%s (%d)" name i
                if taken candidate then go (i + 1) else candidate
            go 2

    let update (dir : string) (msg : LayoutAction) (m : LayoutModel) : LayoutModel * list<string> =
        match msg with
        | LayoutAction.Changed json ->
            match LayoutFile.tryParseGolden json with
            | Ok layout ->
                LayoutLibrary.storeCurrent dir { name = m.activeName; layout = layout }
                // Not pushed back: the browser already shows it. But the Golden Layout channel
                // replays its last SetLayout to every client that connects, so a reloaded page
                // would boot into this layout and then be reset to the last applied one. The
                // replayed payload therefore becomes the current layout, under the same version
                // so connected clients do not receive it again -- but only once the browser has
                // shown the pushed arrangement: an event still on its way from before the push
                // would otherwise replace the push before it is sent.
                let confirmed =
                    m.pushConfirmed ||
                    match m.golden.SetLayout with
                    | Some (pushed, _) -> LayoutOps.shape pushed = LayoutOps.shape layout
                    | None -> true
                let setLayout =
                    if confirmed then m.golden.SetLayout |> Option.map (fun (_, version) -> layout, version)
                    else m.golden.SetLayout
                { m with
                    current       = layout
                    pushConfirmed = confirmed
                    golden        = { m.golden with DefaultLayout = layout; SetLayout = setLayout } }, []
            | Result.Error e ->
                Log.warn "[Layouts] ignoring a layout reported by the browser: %s" e
                m, []

        | LayoutAction.ApplyDashboard name ->
            match dashboard name with
            | Some d -> apply dir d.name d.layout m, []
            | None -> m, [sprintf "Unknown layout '%s'." name]

        | LayoutAction.ApplyLibrary name ->
            match LayoutLibrary.tryFind dir name with
            | Some { layout = Ok layout } -> { apply dir name layout m with library = libraryNames dir }, []
            | Some { layout = Result.Error e } ->
                { m with library = libraryNames dir }, [sprintf "The layout '%s' cannot be read: %s" name e]
            | None -> { m with library = libraryNames dir }, [sprintf "The layout '%s' no longer exists." name]

        | LayoutAction.ReopenPanel panelId ->
            apply dir m.activeName (LayoutOps.addPanel panelId m.current) m, []

        | LayoutAction.OpenDialog dialog ->
            let input =
                match dialog with
                | LayoutDialog.Rename name -> name
                | LayoutDialog.SaveAs -> ""
                | _ -> m.nameInput
            { m with dialog = dialog; nameInput = input; library = libraryNames dir }, []

        | LayoutAction.CloseDialog ->
            match m.dialog with
            | LayoutDialog.Notice (_, next) -> { m with dialog = next }, []
            | _ -> { m with dialog = LayoutDialog.None }, []

        | LayoutAction.SetNameInput s ->
            { m with nameInput = s }, []

        | LayoutAction.SaveCurrentAs ->
            let name = m.nameInput.Trim()
            match LayoutLibrary.trySave dir { name = name; layout = m.current } with
            | Ok () ->
                { m with
                    dialog      = LayoutDialog.None
                    library     = libraryNames dir
                    activeName  = name
                    activeShape = LayoutOps.shape m.current }, [sprintf "Layout '%s' saved." name]
            | Result.Error e ->
                Log.warn "[Layouts] could not save layout '%s': %s" name e
                m, [sprintf "Could not save the layout: %s" e]

        | LayoutAction.Delete name ->
            match LayoutLibrary.tryDelete dir name with
            | Ok () -> { m with library = libraryNames dir }, [sprintf "Layout '%s' deleted." name]
            | Result.Error e -> { m with library = libraryNames dir }, [sprintf "Could not delete the layout: %s" e]

        | LayoutAction.Rename ->
            match m.dialog with
            | LayoutDialog.Rename oldName ->
                let newName = m.nameInput.Trim()
                match LayoutLibrary.tryRename dir oldName newName with
                | Ok () ->
                    let active = if m.activeName = oldName then newName else m.activeName
                    { m with dialog = LayoutDialog.Manage; library = libraryNames dir; activeName = active }, []
                | Result.Error e -> m, [sprintf "Could not rename the layout: %s" e]
            | _ -> m, []

        | LayoutAction.SetImportSceneLayout v ->
            match m.dialog with
            | LayoutDialog.SceneLayout p -> { m with dialog = LayoutDialog.SceneLayout { p with importIntoLibrary = v } }, []
            | _ -> m, []

        | LayoutAction.SetApplySceneLayout v ->
            match m.dialog with
            | LayoutDialog.SceneLayout p -> { m with dialog = LayoutDialog.SceneLayout { p with apply = v } }, []
            | _ -> m, []

        | LayoutAction.ConfirmSceneLayout ->
            match m.dialog with
            | LayoutDialog.SceneLayout p ->
                let m = { m with dialog = LayoutDialog.None }
                let name, feedback =
                    if p.importIntoLibrary && not p.alreadyInLibrary then
                        let name = freeName (libraryNames dir) p.sceneName
                        match LayoutLibrary.trySave dir { name = name; layout = p.layout } with
                        | Ok () -> name, [sprintf "Layout '%s' added to your layouts." name]
                        | Result.Error e -> p.sceneName, [sprintf "Could not add the layout to your layouts: %s" e]
                    else p.sceneName, []
                let m = if p.apply then apply dir name p.layout m else m
                { m with library = libraryNames dir }, feedback
            | _ -> m, []

    module UI =

        let private item (attributes : list<Attribute<LayoutAction>>) (label : string) =
            div (clazz "ui inverted item" :: attributes) [ text label ]

        let private subMenu (name : string) (content : DomNode<LayoutAction>) =
            div [ clazz "ui dropdown item" ] [
                text name
                i [ clazz "dropdown icon" ] []
                content
            ]

        /// The "Layout" entries of the main menu.
        let menu (m : AdaptiveLayoutModel) : DomNode<LayoutAction> =
            let library =
                m.library |> AVal.map (fun names ->
                    names
                    |> List.map (fun name ->
                        item [ attribute "data-layout" name; onClick (fun _ -> LayoutAction.ApplyLibrary name) ] name
                    )
                    |> IndexList.ofList
                ) |> AList.ofAVal

            let closed =
                m.current |> AVal.map (fun current ->
                    match LayoutOps.closedPanels current with
                    | [] -> IndexList.single (div [ clazz "ui disabled item" ] [ text "All panels are open" ])
                    | panels ->
                        panels
                        |> List.map (fun p ->
                            item [ attribute "data-panel" p.id; onClick (fun _ -> LayoutAction.ReopenPanel p.id) ] p.title
                        )
                        |> IndexList.ofList
                ) |> AList.ofAVal

            subMenu "Layout" (
                div [ clazz "menu"; attribute "data-test" "layout-menu" ] [
                    for d in DashboardModes.all do
                        item [ attribute "data-dashboard" d.name; onClick (fun _ -> LayoutAction.ApplyDashboard d.name) ] d.name
                    div [ clazz "divider" ] []
                    div [ clazz "header" ] [ text "My Layouts" ]
                    Incremental.div (AttributeMap.ofList [ attribute "data-test" "layout-library" ]) library
                    item [ attribute "data-test" "layout-save-as"; onClick (fun _ -> LayoutAction.OpenDialog LayoutDialog.SaveAs) ] "Save Current Layout As..."
                    item [ attribute "data-test" "layout-manage"; onClick (fun _ -> LayoutAction.OpenDialog LayoutDialog.Manage) ] "Manage My Layouts..."
                    div [ clazz "divider" ] []
                    subMenu "Reopen Panel" (
                        Incremental.div (AttributeMap.ofList [ clazz "menu"; attribute "data-test" "layout-reopen" ]) closed
                    )
                ]
            )

        let private window (title : string) (content : list<DomNode<LayoutAction>>) (buttons : list<DomNode<LayoutAction>>) =
            div [
                clazz "layout-dimmer"
                style "position:fixed; inset:0; background:rgba(0,0,0,0.55); z-index:20000; display:flex; align-items:center; justify-content:center"
            ] [
                div [
                    clazz "ui inverted segment"
                    attribute "data-test" "layout-dialog"
                    style "min-width:360px; max-width:520px; max-height:90vh; overflow:auto"
                ] [
                    h3 [ clazz "ui inverted header" ] [ text title ]
                    div [] content
                    div [ style "margin-top:14px; text-align:right" ] buttons
                ]
            ]

        let private button (attributes : list<Attribute<LayoutAction>>) (label : string) =
            div (clazz "ui small button" :: style "margin-left:6px" :: attributes) [ text label ]

        let private checkbox (testId : string) (checked' : bool) (caption : string) (action : bool -> LayoutAction) =
            div [ style "margin:6px 0" ] [
                div [
                    clazz "ui inverted checkbox"
                    attribute "data-test" testId
                    onClick (fun _ -> action (not checked'))
                ] [
                    input [ attribute "type" "checkbox"; if checked' then attribute "checked" "checked" ]
                    label [] [ text caption ]
                ]
            ]

        let private nameField (m : AdaptiveLayoutModel) =
            div [ clazz "ui inverted fluid input" ] [
                Incremental.input (
                    AttributeMap.ofAMap (amap {
                        yield attribute "type" "text"
                        yield attribute "data-test" "layout-name"
                        yield attribute "placeholder" "Layout name"
                        let! value = m.nameInput
                        yield attribute "value" value
                        yield onChange LayoutAction.SetNameInput
                    })
                )
            ]

        /// Modal windows of the layout feature; absent from the DOM while closed.
        let dialogs (m : AdaptiveLayoutModel) : DomNode<LayoutAction> =
            Incremental.div AttributeMap.empty (
                alist {
                    let! dialog = m.dialog
                    match dialog with
                    | LayoutDialog.None -> ()

                    | LayoutDialog.Notice (message, _) ->
                        yield window "Window Layout" [
                            div [ attribute "data-test" "layout-notice"; style "max-width:480px; word-break:break-word" ] [ text message ]
                        ] [
                            button [ clazz "primary"; attribute "data-test" "layout-notice-ok"; onClick (fun _ -> LayoutAction.CloseDialog) ] "OK"
                        ]

                    | LayoutDialog.SaveAs ->
                        yield window "Save Current Layout" [
                            nameField m
                        ] [
                            button [ onClick (fun _ -> LayoutAction.CloseDialog) ] "Cancel"
                            button [ clazz "primary"; attribute "data-test" "layout-save"; onClick (fun _ -> LayoutAction.SaveCurrentAs) ] "Save"
                        ]

                    | LayoutDialog.Rename name ->
                        yield window (sprintf "Rename '%s'" name) [
                            nameField m
                        ] [
                            button [ onClick (fun _ -> LayoutAction.OpenDialog LayoutDialog.Manage) ] "Cancel"
                            button [ clazz "primary"; attribute "data-test" "layout-rename-confirm"; onClick (fun _ -> LayoutAction.Rename) ] "Rename"
                        ]

                    | LayoutDialog.Manage ->
                        let! names = m.library
                        let rows =
                            match names with
                            | [] -> [ div [ style "color:#ccc" ] [ text "No saved layouts yet." ] ]
                            | names ->
                                names |> List.map (fun name ->
                                    div [ attribute "data-layout-row" name; style "display:flex; align-items:center; margin:4px 0" ] [
                                        div [ style "flex:1" ] [ text name ]
                                        button [ onClick (fun _ -> LayoutAction.ApplyLibrary name) ] "Load"
                                        button [ attribute "data-test" "layout-rename"; onClick (fun _ -> LayoutAction.OpenDialog (LayoutDialog.Rename name)) ] "Rename"
                                        button [ clazz "red"; attribute "data-test" "layout-delete"; onClick (fun _ -> LayoutAction.Delete name) ] "Delete"
                                    ]
                                )
                        yield window "My Layouts" rows [
                            button [ attribute "data-test" "layout-close"; onClick (fun _ -> LayoutAction.CloseDialog) ] "Close"
                        ]

                    | LayoutDialog.SceneLayout p ->
                        yield window "Layout Stored With This Scene" [
                            div [] [ text (sprintf "The scene '%s' comes with a window layout." p.sceneName) ]
                            if p.alreadyInLibrary then
                                div [ style "margin:6px 0; color:#ccc" ] [ text "Your layouts already contain this arrangement." ]
                            else
                                checkbox "layout-scene-import" p.importIntoLibrary "Add it to my layouts" LayoutAction.SetImportSceneLayout
                            checkbox "layout-scene-apply" p.apply "Use it now" LayoutAction.SetApplySceneLayout
                        ] [
                            button [ attribute "data-test" "layout-scene-ignore"; onClick (fun _ -> LayoutAction.CloseDialog) ] "Ignore"
                            button [ clazz "primary"; attribute "data-test" "layout-scene-ok"; onClick (fun _ -> LayoutAction.ConfirmSceneLayout) ] "OK"
                        ]
                }
            )
