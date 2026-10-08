namespace PRo3D.Composition

open System.Runtime.InteropServices

open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core

/// Group colours of the tool strip (docs/ToolStrip.md). Held as (r, g, b) rather than a hex
/// string so the same definition yields both the solid chip colour and a toned down
/// translucent wash of it.
module ToolColors =
    let navigation = (0x4a, 0xa3, 0xff)   // blue
    let annotation = (0x3f, 0xb9, 0x50)   // green
    let selection  = (0xe3, 0xb3, 0x41)   // amber
    let placement  = (0xb0, 0x83, 0xf0)   // violet
    let reference  = (0x4f, 0xd1, 0xc5)   // teal
    /// interactions that are hidden from the UI and so belong to no group
    let neutral    = (0xcc, 0xcc, 0xcc)

    let hex ((r, g, b) : int * int * int) = sprintf "#%02x%02x%02x" r g b

    /// Translucent version of a group colour. `alpha` is a CSS literal passed through
    /// verbatim - formatting a float here would emit a decimal comma under a German
    /// locale and silently void the declaration.
    let rgba (alpha : string) ((r, g, b) : int * int * int) =
        sprintf "rgba(%d, %d, %d, %s)" r g b alpha

    /// Group an interaction belongs to, expressed as its colour. This is also what
    /// defines the grouping drawn in a tool strip, so a host adding an interaction to a
    /// group here must put it into the matching block of its strip too.
    let ofInteraction (i : Interactions) =
        match i with
        | Interactions.DrawAnnotation
        | Interactions.PickAnnotation
        | Interactions.CutAnnotation
        | Interactions.EditAnnotation           -> annotation
        | Interactions.PickSurface
        | Interactions.SelectArea               -> selection
        | Interactions.PlaceRover
        | Interactions.PickDistancePoint
        | Interactions.PlaceSceneObject
        | Interactions.PlaceScaleBar
        | Interactions.PickPivotPoint           -> placement
        | Interactions.PlaceCoordinateSystem
        | Interactions.PickSurfaceRefSys
        | Interactions.PickExploreCenter        -> reference
        | _                                     -> neutral

/// What the toolbars say about a tool: its name, the click hint and the tooltip.
module ToolText =

    /// How the hint lines name the gesture that runs the active tool. Direct Tool Mode puts
    /// the tool on a plain left click, so the modifier must drop out of the text or every
    /// hint reads wrong.
    let clickGesture (directToolMode : bool) =
        if directToolMode then "Click"
        else
            let ctrl = if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "CMD" else "CTRL"
            sprintf "%s+click" ctrl

    let interactionText (directToolMode : bool) (i : Interactions) =
        let click = clickGesture directToolMode
        match i with
        | Interactions.PickExploreCenter     -> sprintf "%s to place arcball center" click
        | Interactions.PlaceCoordinateSystem -> sprintf "%s to place coordinate cross" click
        | Interactions.DrawAnnotation        -> sprintf "%s to pick point on surface" click
        | Interactions.PickAnnotation        -> sprintf "%s on annotation to select" click
        | Interactions.CutAnnotation         -> sprintf "%s to draw separating polyline" click
        | Interactions.PickSurface           -> sprintf "%s on surface to select" click
        | Interactions.PlaceRover            -> sprintf "%s to (1) place rover and (2) pick lookat" click
        | Interactions.TrafoControls         -> "not implemented"
        | Interactions.PlaceSurface          -> "not implemented"
        | Interactions.PlaceScaleBar         -> sprintf "%s to place scale bar" click
        | Interactions.PlaceSceneObject      -> sprintf "%s to place scene object" click
        | Interactions.PickPivotPoint        -> sprintf "%s to place pivot point" click
        | Interactions.PickSurfaceRefSys     -> sprintf "%s to place additional reference system for selected surface" click
        | _ -> ""

    /// As interactionText, but also reflects whether a control point is currently in hand.
    /// Click-to-grab has no drag affordance to feel out, so the hint line is most of what makes
    /// the gesture discoverable.
    let interactionTextWithState (directToolMode : bool) (i : Interactions) (grabbed : bool) =
        let click = clickGesture directToolMode
        match i with
        | Interactions.EditAnnotation when grabbed -> sprintf "%s to drop the point, ESC to cancel" click
        | Interactions.EditAnnotation -> sprintf "%s a vertex of the selected annotation to move it" click
        | _ -> interactionText directToolMode i

    let interactionTooltip (i : Interactions) : string =
        match i with
        | Interactions.PickExploreCenter     -> "Pick the camera pivot point if ArcBall navigation is activated."
        | Interactions.PlaceCoordinateSystem -> "Pick a point on the surface and choose a unit of measurement to adapt the size of the axis gizmo."
        | Interactions.PickSurfaceRefSys     -> "Pick a point on the selected surface to give it its own reference system, and choose a unit of measurement to adapt the size of its cross."
        | Interactions.DrawAnnotation        -> "Choose an annotation mode to draw an annotation on a surface."
        | Interactions.PlaceRover            -> "Select a rover model in the rover menu."
        | Interactions.PickAnnotation        -> "Select an annotation in the main view. The selected annotation will be highlighted green."
        | Interactions.EditAnnotation        -> "Move the vertices of the selected annotation. Its control points appear as handles; click one to pick it up, move the cursor over the surface and click again to put it down. Clicking an annotation selects it."
        | Interactions.PickSurface           -> "Select a surface in the main view. The selected surface will be highlighted green."
        | _ -> ""

    /// Display name of the selected tool, shown as the secondary-toolbar chip.
    let interactionName (i : Interactions) : string =
        match i with
        | Interactions.DrawAnnotation        -> "Draw Annotation"
        | Interactions.PickAnnotation        -> "Select Annotation"
        | Interactions.CutAnnotation         -> "Cut Annotation"
        | Interactions.EditAnnotation        -> "Edit Annotation"
        | Interactions.PickSurface           -> "Select Surface"
        | Interactions.SelectArea            -> "Select Area"
        | Interactions.PlaceRover            -> "Place Rover"
        | Interactions.PickDistancePoint     -> "Place Distance Point"
        | Interactions.PlaceSceneObject      -> "Place Scene Object"
        | Interactions.PlaceScaleBar         -> "Place Scalebar"
        | Interactions.PickPivotPoint        -> "Pick Pivot Point"
        | Interactions.PlaceCoordinateSystem -> "Place Coordinate System"
        | Interactions.PickSurfaceRefSys     -> "Place Surface Reference System"
        | Interactions.PickExploreCenter     -> "Pick Explore Center"
        | _                                  -> "Tool Settings"

/// The vertical icon strip overlaid on the right edge of a render view (docs/ToolStrip.md):
/// navigation modes in one panel, the host's tools in another. Both selections are enum-valued
/// model fields, so exactly one navigation icon and one tool icon is lit at all times. The host
/// passes the tool list, so each host offers exactly its own tools.
module ToolStrip =

    type Entry<'msg> =
        /// switches the interaction
        | Tool    of icon : string * tooltip : string * Interactions
        /// a one-shot command; never reads as active
        | Command of color : string * icon : string * tooltip : string * 'msg
        /// separates two tool groups
        | Divider

    type Spec<'msg> =
        {
            navigationMode    : aval<NavigationMode>
            /// MapView needs a reference body
            mapViewEnabled    : aval<bool>
            setNavigationMode : NavigationMode -> 'msg
            interaction       : aval<Interactions>
            setInteraction    : Interactions -> 'msg
            tools             : list<Entry<'msg>>
        }

    /// One strip button. `isEnabled` is only ever false for MapView without a reference body;
    /// a disabled button dispatches nothing so the model cannot enter that state.
    let private button
        (color     : string)
        (icon      : string)
        (isActive  : aval<bool>)
        (isEnabled : aval<bool>)
        (tooltip   : string)
        (action    : 'msg) =

        let attribs =
            amap {
                let! active  = isActive
                let! enabled = isEnabled
                // active and disabled are independent: MapView can be the current mode while
                // the scene has no reference body, and it must still read as the one active
                // navigation icon - just greyed out.
                let cls =
                    (if active then "pro3d-tool active" else "pro3d-tool")
                    + (if enabled then "" else " disabled")
                yield clazz cls
                yield style (sprintf "--tool-color:%s" color)
                if enabled then
                    yield onClick (fun _ -> action)
            } |> AttributeMap.ofAMap

        Incremental.div attribs (AList.ofList [ i [clazz (icon + " icon")] [] ])
        |> UI.wrapToolTip DataPosition.Left tooltip

    let private divider () = div [clazz "pro3d-tool-divider"] []

    let view (spec : Spec<'msg>) : DomNode<'msg> =
        let navButton icon tooltip mode isEnabled =
            button (ToolColors.hex ToolColors.navigation) icon
                   (spec.navigationMode |> AVal.map (fun x -> x = mode))
                   isEnabled tooltip (spec.setNavigationMode mode)

        let entry (e : Entry<'msg>) =
            match e with
            | Tool (icon, tooltip, interaction) ->
                // the colour is looked up, so ToolColors.ofInteraction stays the one place
                // that says which group a tool belongs to
                button (ToolColors.hex (ToolColors.ofInteraction interaction)) icon
                       (spec.interaction |> AVal.map (fun x -> x = interaction))
                       (AVal.constant true) tooltip (spec.setInteraction interaction)
            | Command (color, icon, tooltip, action) ->
                button color icon (AVal.constant false) (AVal.constant true) tooltip action
            | Divider -> divider ()

        // Clicks must not reach the render body underneath: it starts a camera drag /
        // selection rectangle on mousedown and opens the context menu on right click.
        // Navigation and tools sit in two separate panels with a small gap, so the blue
        // navigation group reads as distinct from the tools. Both panels share the button
        // width and padding, so the icon columns line up.
        onBoot "$('#__ID__').on('mousedown mouseup click dblclick contextmenu wheel', function(e) { e.stopPropagation(); });" (
            div [clazz "pro3d-toolstrip"] [
                div [clazz "pro3d-toolstrip-group"] [
                    navButton "rocket" "Free fly - move the camera freely"
                              NavigationMode.FreeFly (AVal.constant true)
                    // trailing ◎ echoes the bullseye "Pick ArcBall orbit centre" tool
                    navButton "dot circle outline" "ArcBall - orbit the camera around a pivot point  ◎"
                              NavigationMode.ArcBall (AVal.constant true)
                    navButton "map" "Map view - top down, up is north, speed scales with altitude (needs a planet)"
                              NavigationMode.MapView spec.mapViewEnabled
                ]
                div [clazz "pro3d-toolstrip-group"] (spec.tools |> List.map entry)
            ]
        )

/// The second toolbar row (docs/ToolStrip.md): a chip with the selected tool's name in its
/// group colour, the tool's own settings, and the click hint.
module ToolBar =

    /// The row is always rendered, even for a tool without settings, so the dock below never
    /// jumps when switching tools; `hasSettings` drops the item class for those so no stray
    /// divider is left.
    let secondaryRow
        (interaction    : aval<Interactions>)
        (directToolMode : aval<bool>)
        (grabbed        : aval<bool>)
        (hasSettings    : Interactions -> bool)
        (settings       : aval<DomNode<'msg>>) : DomNode<'msg> =

        let itemAttribs =
            amap {
                let! interaction = interaction
                if hasSettings interaction
                then yield clazz "item topmenu"
                else yield clazz "topmenu pro3d-toolbar-empty"
            } |> AttributeMap.ofAMap

        let label =
            let attribs =
                amap {
                    let! interaction = interaction
                    yield clazz "pro3d-toolsettings-chip"
                    yield style (sprintf "background:%s" (ToolColors.hex (ToolColors.ofInteraction interaction)))
                } |> AttributeMap.ofAMap
            div [clazz "item topmenu pro3d-toolsettings-label"] [
                Incremental.div attribs (AList.ofList [ Incremental.text (interaction |> AVal.map ToolText.interactionName) ])
            ]

        let hint =
            div [clazz "item topmenu"; style "font-style:italic"] [
                Incremental.text (AVal.map3 ToolText.interactionTextWithState directToolMode interaction grabbed)
            ]

        div [clazz "ui menu pro3d-secondary-toolbar"; style "padding:0; margin:0"] [
            label
            Incremental.div itemAttribs (AList.ofAValSingle settings)
            hint
        ]

    /// The drop-down main menu at the left of the top bar (hover opens it): a sidebar icon with
    /// one sub-menu per entry.
    let mainMenu (subMenus : list<DomNode<'msg>>) : DomNode<'msg> =
        div [clazz "menu-bar"] [
            div [ clazz "ui top menu"; style "z-index: 10000; padding:0px; margin:0px"] [
                onBoot "$('#__ID__').dropdown('on', 'hover');" (
                    div [ clazz "ui dropdown item"; style "padding:0px 5px"] [
                        i [clazz "large sidebar icon"; style "margin:0px 2px"] []
                        div [ clazz "ui menu"] subMenus
                    ]
                )
            ]
        ]

    /// One sub-menu of `mainMenu`. 150px wide, like the Viewer's, so the arrow stays on the line.
    let subMenu (name : string) (items : list<DomNode<'msg>>) : DomNode<'msg> =
        div [ clazz "ui dropdown item"; style "width: 150px"] [
            text name
            i [clazz "dropdown icon"] []
            div [ clazz "menu"] items
        ]
