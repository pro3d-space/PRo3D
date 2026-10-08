namespace PRo3D.Lite

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Primitives.Golden
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Composition

/// PRo3D Lite's pages: a Golden Layout shell (menu, status line) around the 3D view and four
/// panels, each built from the PRo3D.Core sub-app views.
module LiteGui =

    let dependencies =
        Html.semui @ [
            { kind = Stylesheet; name = "semui-overrides"; url = "./resources/semui-overrides.css" }
            { kind = Stylesheet; name = "fonts";           url = "./resources/fonts.css" }
            { kind = Script;     name = "utilities";       url = "./resources/utilities.js" }
        ]

    let private panelBody (content : list<DomNode<LiteAction>>) =
        require dependencies (
            body [ style "background: #1B1C1E; color: #dddddd; width:100%; height:100%; overflow-y:auto; overflow-x:hidden; padding:6px" ] content
        )

    let private heading (s : string) =
        h5 [ clazz "ui inverted horizontal divider header" ] [ text s ]

    // --- file dialogs (Electron, via Aardium) ------------------------------------------------

    let private jsOpenDirectories title =
        sprintf "top.aardvark.dialog.showOpenDialog({ title: '%s', properties: ['openDirectory', 'multiSelections'] }).then(result => { top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths); });" title

    let private jsOpenFile title filterName (extensions : list<string>) =
        let ext = extensions |> List.map (sprintf "'%s'") |> String.concat ","
        sprintf "top.aardvark.dialog.showOpenDialog({ title: '%s', filters: [{ name: '%s', extensions: [%s] }], properties: ['openFile'] }).then(result => { top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths); });" title filterName ext

    let private jsSaveFile title filterName (extensions : list<string>) =
        let ext = extensions |> List.map (sprintf "'%s'") |> String.concat ","
        sprintf "top.aardvark.dialog.showSaveDialog({ title: '%s', filters: [{ name: '%s', extensions: [%s] }] }).then(result => { top.aardvark.processEvent('__ID__', 'onsave', result.filePath); });" title filterName ext

    let private menuItem (label : string) (attributes : list<Attribute<LiteAction>>) =
        div ([ clazz "ui inverted item" ] @ attributes) [ text label ]

    /// The Viewer's top bar, reduced to Lite: the hamburger menu, the planet and the scene name
    /// on the first row; the selected tool's chip, settings and click hint on the second.
    let private mainMenu =
        ToolBar.mainMenu [
            ToolBar.subMenu "Surfaces" [
                menuItem "Import OPCs" [
                    Dialogs.onChooseFiles ImportOpcs
                    clientEvent "onclick" (jsOpenDirectories "Select OPC directories")
                ]
            ]
            ToolBar.subMenu "Scene" [
                menuItem "Open scene" [
                    Dialogs.onChooseFiles OpenScene
                    clientEvent "onclick" (jsOpenFile "Open scene" "Scene (*.pro3d)" [ "pro3d" ])
                ]
                menuItem "Save scene" [ onClick (fun _ -> SaveScene) ]
                menuItem "Save scene as" [
                    Dialogs.onSaveFile SaveSceneAs
                    clientEvent "onclick" (jsSaveFile "Save scene as" "Scene (*.pro3d)" [ "pro3d" ])
                ]
            ]
            ToolBar.subMenu "Annotations" [
                menuItem "Load annotations" [
                    Dialogs.onChooseFiles LoadAnnotations
                    clientEvent "onclick" (jsOpenFile "Load annotations" "Annotations (*.pro3d.ann)" [ "ann" ])
                ]
                menuItem "Save annotations" [
                    Dialogs.onSaveFile SaveAnnotations
                    clientEvent "onclick" (jsSaveFile "Save annotations" "Annotations (*.pro3d.ann)" [ "ann" ])
                ]
            ]
            ToolBar.subMenu "View" [
                menuItem "Home" [ onClick (fun _ -> Home) ]
                // brings closed panels back
                menuItem "Reset layout" [ onClick (fun _ -> GoldenMsg GoldenLayout.Message.ResetLayout) ]
            ]
        ]

    /// The tools that have settings on the second toolbar row.
    let private hasToolSettings (i : Interactions) =
        match i with
        | Interactions.DrawAnnotation
        | Interactions.PlaceCoordinateSystem -> true
        | _ -> false

    let private toolSettings (m : AdaptiveLiteModel) =
        m.interaction |> AVal.map (fun interaction ->
            match interaction with
            | Interactions.DrawAnnotation ->
                Drawing.UI.viewAnnotationToolsHorizontal "" m.refSystem.planet m.drawing |> UI.map DrawingMsg
            | Interactions.PlaceCoordinateSystem ->
                Html.Layout.horizontal [
                    Html.Layout.boxH [ Html.SemUi.dropDown' m.refSystem.scaleChart m.refSystem.selectedScale ReferenceSystemAction.SetScale id ]
                    |> UI.wrapToolTip DataPosition.Bottom "Measurement to adapt the size of the axis gizmo"
                    Html.Layout.boxH [ GuiEx.iconToggle m.refSystem.isVisible "unhide icon" "hide icon" ReferenceSystemAction.ToggleVisible ]
                    |> UI.wrapToolTip DataPosition.Bottom "Toggle visibility of axis gizmo"
                ] |> UI.map RefSystemMsg
            | _ -> div [] []
        )

    let private topBar (m : AdaptiveLiteModel) =
        div [ clazz "pro3d-topbar" ] [
            div [ clazz "ui menu"; style "padding:0; margin:0" ] [
                mainMenu
                div [ clazz "item topmenu" ] [
                    Html.Layout.horizontal [
                        Html.Layout.boxH [ div [ style "font-weight:bold" ] [ text "Reference System:" ] ]
                        Html.Layout.boxH [ Html.SemUi.dropDown m.refSystem.planet ReferenceSystemAction.SetPlanet ] |> UI.map RefSystemMsg
                    ]
                ]
                // scene name pinned to the right edge of the main row
                div [ clazz "item topmenu"; style "margin-left:auto" ] [
                    Incremental.text (m.scenePath |> AVal.map (Option.map IO.Path.GetFileName >> Option.defaultValue "*new scene"))
                ]
            ]
            // Lite has no direct tool mode: tools always run on Ctrl+click
            ToolBar.secondaryRow
                m.interaction (AVal.constant false) (m.drawing.vertexGrab |> AVal.map Option.isSome)
                hasToolSettings (toolSettings m)
        ]

    /// The Viewer's icon strip with Lite's tools.
    let private toolStrip (m : AdaptiveLiteModel) =
        // a divider wherever the colour group changes
        let entries =
            LiteApp.tools
            |> List.pairwise
            |> List.collect (fun ((a, _, _), (b, icon, tooltip)) ->
                let tool = ToolStrip.Tool (icon, tooltip, b)
                if ToolColors.ofInteraction a = ToolColors.ofInteraction b then [ tool ] else [ ToolStrip.Divider; tool ])
            |> fun rest ->
                match LiteApp.tools with
                | (first, icon, tooltip) :: _ -> ToolStrip.Tool (icon, tooltip, first) :: rest
                | [] -> rest
        ToolStrip.view {
            navigationMode    = m.navigation.navigationMode
            mapViewEnabled    = m.refSystem.planet |> AVal.map (fun p -> p <> Planet.None)
            setNavigationMode = fun mode -> NavigationMsg (Navigation.Action.SetNavigationMode mode)
            interaction       = m.interaction
            setInteraction    = SetInteraction
            tools             = entries
        }

    // --- 3D view ----------------------------------------------------------------------------

    let private renderView (runtime : IRuntime) (m : AdaptiveLiteModel) =
        let frustum = m.config.frustumModel.frustum
        let view    = m.navigation.camera.view
        let cam     = AVal.map2 Camera.create view frustum

        // surfaces: one pass per priority group, clicks go to the surface picker while Ctrl
        // is held (the camera has the mouse otherwise)
        let surfaceEvents : SurfaceView.SurfacePickEvents<LiteAction> =
            {
                enabled = fun () -> AVal.force m.ctrlFlag
                // hover only matters while a tool is armed: it arms a grabbed control point
                preview = Some (fun () -> true)
                click   = fun hit name -> PickSurfaceHit (hit, name)
                move    = fun _ _ -> SurfaceHover
            }
        let surfaceGroups =
            m.surfaces.sgGrouped
            |> AList.map (fun group ->
                group
                |> AMap.map (fun _ sgSurface ->
                    SurfaceView.pickableSurfaceSg
                        surfaceEvents m.surfaces.surfaces.singleSelectLeaf sgSurface m.surfaces.surfaces.flat
                        frustum m.refSystem (AVal.constant None) (AVal.constant None))
                |> AMap.toASet
                |> ASet.map snd
                |> Sg.set
            )

        // annotations: picked through their own GPU pick target while a pick tool is armed
        let allowAnnotationPicking =
            (m.interaction, m.ctrlFlag) ||> AVal.map2 (fun interaction armed ->
                armed &&
                match interaction with
                | Interactions.PickAnnotation
                | Interactions.EditAnnotation -> true
                | _ -> false)
        let allowVertexEditing = m.interaction |> AVal.map (fun i -> i = Interactions.EditAnnotation)

        let annotations, discs =
            DrawingApp.view
                m.config HostConfigs.mdrawingConfig (AVal.constant None) view frustum runtime
                m.viewPortSize allowAnnotationPicking allowVertexEditing m.drawing

        let depthTested =
            Sg.ofList [ discs; annotations ]
            |> Sg.map DrawingMsg
            |> Sg.fillMode (AVal.constant FillMode.Fill)
            |> Sg.cullMode (AVal.constant CullMode.None)

        let overlayed =
            Sg.ofList [
                Sg.view m.config HostConfigs.mrefConfig m.refSystem view |> Sg.map RefSystemMsg
                Navigation.Sg.view m.navigation |> Sg.map (fun _ -> NoOp)
                |> Sg.onOff (
                    (m.navigation.navigationMode, m.config.showExplorationPointGui)
                    ||> AVal.map2 (fun mode show -> show && mode = NavigationMode.ArcBall))
                DrawingApp.viewTextLabels m.config HostConfigs.mdrawingConfig view m.drawing |> Sg.map (fun _ -> NoOp)
            ]

        let commands =
            SurfaceView.renderCommands surfaceGroups overlayed depthTested
            |> Aardvark.UI.RenderCommand.Ordered
            |> Sg.execute

        let attributes =
            AttributeMap.unionMany [
                // the camera listens unless Ctrl hands the left button to the active tool
                NavigationView.controllerAttributes (m.ctrlFlag |> AVal.map not) m.navigation
                |> AttributeMap.mapAttributes (AttributeValue.map NavigationMsg)
                AttributeMap.ofList [
                    style "width:100%; height:100%; background-color: #222222"
                    attribute "data-samples" "4"
                    attribute "useMapping" "true"
                    // focus on hover, so Ctrl and the drawing keys work without a click first
                    attribute "onmouseenter" "this.focus()"
                    onKeyDown KeyDownMsg
                    onKeyUp KeyUpMsg
                ]
            ]

        DomNode.RenderControl(attributes, cam, commands)

    // --- panels -----------------------------------------------------------------------------

    /// Remove / move / clear for the selected annotation or group - the full viewer's buttons.
    let private annotationActions (m : AdaptiveLiteModel) =
        let groups = m.drawing.annotations
        adaptive {
            let! lastSelected = groups.lastSelectedItem
            match lastSelected with
            | SelectedItem.Group ->
                let! group = groups.activeGroup
                return GroupsApp.viewGroupButtons group |> UI.map (DrawingAction.GroupsMessage >> DrawingMsg)
            | _ ->
                let! leaf = groups.activeChild
                match! groups.singleSelectLeaf with
                | Some _ -> return GroupsApp.viewLeafButtons leaf |> UI.map (DrawingAction.GroupsMessage >> DrawingMsg)
                | None -> return div [ style "font-style:italic" ] [ text "no annotation selected" ]
        }

    let private annotationsPanel (m : AdaptiveLiteModel) =
        panelBody [
            div [] [
                button [ clazz "ui small inverted button"; onClick (fun _ -> DrawingMsg DrawingAction.Undo) ] [ text "Undo (Ctrl+Z)" ]
                button [ clazz "ui small inverted button"; onClick (fun _ -> DrawingMsg DrawingAction.Redo) ] [ text "Redo (Ctrl+Y)" ]
            ]
            heading "Annotations"
            Drawing.UI.viewAnnotationGroups m.drawing |> UI.map DrawingMsg
            heading "Actions"
            Incremental.div AttributeMap.empty (AList.ofAValSingle (annotationActions m))
        ]

    let private noAnnotation = div [ style "font-style:italic" ] [ text "no annotation selected" ]

    let private propertiesPanel (m : AdaptiveLiteModel) =
        let properties =
            m.drawing.annotations
            |> GroupsApp.viewSelected (function
                | AdaptiveAnnotations ann -> AnnotationProperties.view "" ann
                | _ -> noAnnotation) AnnotationPropsMsg
        let results =
            m.drawing.annotations
            |> GroupsApp.viewSelected (function
                | AdaptiveAnnotations ann -> AnnotationProperties.viewResults ann m.refSystem.up.value
                | _ -> noAnnotation) AnnotationPropsMsg
        panelBody [
            heading "Properties"
            Incremental.div AttributeMap.empty (AList.ofAValSingle properties)
            heading "Measurements"
            Incremental.div AttributeMap.empty (AList.ofAValSingle results)
        ]

    let private surfacesPanel (m : AdaptiveLiteModel) =
        panelBody [
            div [ style "margin-bottom:6px" ] [
                button [
                    clazz "ui small inverted button"
                    Dialogs.onChooseFiles ImportOpcs
                    clientEvent "onclick" (jsOpenDirectories "Select OPC directories")
                ] [ text "Import OPC" ]
                button [ clazz "ui small inverted button"; onClick (fun _ -> Home) ] [ text "Home" ]
            ]
            SurfaceApp.surfaceUI m.scenePath "" m.refSystem m.surfaces |> UI.map SurfacesMsg
        ]

    /// The view settings that do something in Lite - the full viewer's config panel without
    /// the preview cursor, LoD colours, orientation cube and leaf labels (Lite draws none of them).
    let private viewConfig (m : AdaptiveLiteModel) =
        let c = m.config
        let numeric types (value : AdaptiveNumericInput) action = Numeric.view' types value |> UI.map action
        Html.table [
            Html.row "Navigation Sensitivity:" [
                numeric [ NumericInputType.Slider; NumericInputType.InputBox ] c.navigationSensitivity ConfigProperties.Action.SetNavigationSensitivity
                |> UI.wrapToolTip DataPosition.Bottom "Page Up / Page Down in the 3D view"
            ]
            Html.row "Near Plane:"              [ numeric [ NumericInputType.InputBox ] c.nearPlane ConfigProperties.Action.SetNearPlane ]
            Html.row "Far Plane:"               [ numeric [ NumericInputType.InputBox ] c.farPlane ConfigProperties.Action.SetFarPlane ]
            Html.row "Picking Tolerance:"       [ numeric [ NumericInputType.InputBox ] c.pickingTolerance ConfigProperties.Action.SetPickingTolerance ]
            Html.row "Import Triangle Size(m):" [ numeric [ NumericInputType.InputBox ] c.importTriangleSize ConfigProperties.Action.SetImportTriangleSize ]
            Html.row "Arrow Length:"            [ numeric [ NumericInputType.InputBox ] c.arrowLength ConfigProperties.Action.SetArrowLength ]
            Html.row "Arrow Thickness:"         [ numeric [ NumericInputType.InputBox ] c.arrowThickness ConfigProperties.Action.SetArrowThickness ]
            Html.row "D+S Plane Size:"          [ numeric [ NumericInputType.InputBox ] c.dnsPlaneSize ConfigProperties.Action.SetDnSPlaneSize ]
            Html.row "Orbit Centre Marker:"     [ GuiEx.iconCheckBox c.showExplorationPointGui ConfigProperties.Action.ToggleExplorationPointGui ]
        ] |> UI.map ConfigMsg

    let private scenePanel (m : AdaptiveLiteModel) =
        panelBody [
            heading "View"
            viewConfig m
            heading "Camera"
            CameraProperties.view m.refSystem m.navigation.camera
            // navigation mode: the tool strip; planet: the top bar
            heading "Reference system"
            ReferenceSystemApp.UI.view m.refSystem |> UI.map RefSystemMsg
        ]

    let view (runtime : IRuntime) (m : AdaptiveLiteModel) =
        pages (function
            | Pages.Page "render" ->
                let size = "{ X: $(document).width(), Y: $(document).height() }"
                require dependencies (
                    body [
                        style "background: #1B1C1E; width:100%; height:100%; margin:0; overflow:hidden"
                        onEvent "onresize" [ size ] (List.head >> Pickler.json.UnPickleOfString >> Resize)
                        onEvent "onfocus"  [ size ] (List.head >> Pickler.json.UnPickleOfString >> Resize)
                    ] [
                        renderView runtime m
                        // frame, bearing, pitch, position, lat/lon/alt - top left, as in the Viewer
                        CameraOverlay.view m.refSystem m.navigation.camera.view
                        // navigation + tool selector, overlaid on the right edge
                        toolStrip m
                    ]
                )
            | Pages.Page "annotations" -> annotationsPanel m
            | Pages.Page "properties"  -> propertiesPanel m
            | Pages.Page "surfaces"    -> surfacesPanel m
            | Pages.Page "scene"       -> scenePanel m
            | Pages.Page unknown ->
                panelBody [ div [ style "color:red" ] [ text (sprintf "Unknown panel: %s" unknown) ] ]
            | Pages.Body ->
                require dependencies (
                    body [ style "width:100%; height:100%; margin:0; overflow:hidden; background:#1B1C1E" ] [
                        topBar m
                        // in normal flow below the two 34px toolbar rows, as in the Viewer: Golden
                        // Layout positions its panels from the container's page offset, so an
                        // absolutely placed container would shift them a second time
                        div [ style "height: calc(100% - 68px - 24px)" ] [
                            GoldenLayout.view [ style "width:100%; height:100%" ] m.golden
                        ]
                        div [ style "height:24px; padding:3px 8px; color:#bbbbbb; background:#141516; font-size:12px; box-sizing:border-box" ] [
                            Incremental.text m.status
                        ]
                    ]
                )
        )
