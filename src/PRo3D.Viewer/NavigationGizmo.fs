namespace PRo3D.Viewer

open System
open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Operators
open PRo3D.Core

/// Small interactive axis gizmo overlaid in the bottom-left corner of the render view.
///
/// It draws the three reference-system axes (X = East, Y = North, Z = Up) as coloured
/// lines ending in labelled circles, plus the three opposite directions (-X/-Y/-Z) as
/// toned-down lines and circles. The gizmo is a pure function of the camera orientation
/// and the reference system, redrawn whenever either changes; it carries no model state
/// and does not touch scene geometry - the bounding-box / framing maths for a click
/// happens in `updateViewer` (see `ViewerAction.OrientCameraToGizmoAxis`).
///
/// Clicking a circle asks the viewer to look straight along that axis onto the centre of
/// the currently multi-selected surfaces' bounding box. With nothing multi-selected the
/// circles render disabled and dispatch nothing.
module NavigationGizmo =

    /// One of the six axis endpoints. X -> East, Y -> North, Z -> Up (reference-system frame).
    type GizmoAxis =
        | PosX | NegX
        | PosY | NegY
        | PosZ | NegZ

    let private allAxes = [ PosX; NegX; PosY; NegY; PosZ; NegZ ]

    let private isPositive =
        function
        | PosX | PosY | PosZ -> true
        | NegX | NegY | NegZ -> false

    let private label =
        function
        | PosX -> "X" | NegX -> "-X"
        | PosY -> "Y" | NegY -> "-Y"
        | PosZ -> "Z" | NegZ -> "-Z"

    /// Base hue per axis: X red, Y green, Z blue (matches the in-scene reference cross).
    let private axisRgb =
        function
        | PosX | NegX -> (0xE0, 0x3B, 0x3B)
        | PosY | NegY -> (0x46, 0xC0, 0x55)
        | PosZ | NegZ -> (0x4A, 0xA3, 0xFF)

    /// ENU basis from the reference-system up/north. `east = north x up`, matching the
    /// convention in `PRo3D.Core.Sg.getOrientationSystem` / `Sg.view`.
    let private enuBasis (up : V3d) (north : V3d) : V3d * V3d * V3d =
        let u = up.Normalized
        let n = north.Normalized
        let e = (Vec.cross n u).Normalized
        e, n, u

    let private axisDir (east : V3d) (north : V3d) (up : V3d) (a : GizmoAxis) : V3d =
        match a with
        | PosX ->  east  | NegX -> -east
        | PosY ->  north | NegY -> -north
        | PosZ ->  up    | NegZ -> -up

    // -- helpers used by the update handler (imperative context; plain values) ------------

    /// World-space unit direction of a gizmo axis for the given reference system.
    let resolveAxisWorldDir (rs : ReferenceSystem) (a : GizmoAxis) : V3d =
        let e, n, u = enuBasis rs.up.value rs.northO
        (axisDir e n u a).Normalized

    /// Camera "up" (sky) to use after snapping onto `a`. For a top/bottom view (Z) the
    /// map convention is North-up; otherwise the reference Up axis stays vertical.
    let gizmoCameraUp (rs : ReferenceSystem) (a : GizmoAxis) : V3d =
        let _, n, u = enuBasis rs.up.value rs.northO
        match a with
        | PosZ ->  n
        | NegZ -> -n
        | _    ->  u

    // -- rendering -----------------------------------------------------------------------

    let private boxSize = 112.0
    let private c       = boxSize / 2.0   // svg centre
    let private ringR   = 38.0            // centre -> axis circle
    let private dotR    = 10.0
    let private fmt (v : float) = sprintf "%f" v   // invariant-culture '.'-decimal

    let private buildSvg
        (mkMsg    : GizmoAxis -> 'msg)
        (enabled  : bool)
        (camView  : CameraView)
        (upRaw    : V3d)
        (northRaw : V3d) : DomNode<'msg> =

        let east, north, up = enuBasis upRaw northRaw
        let right = camView.Right.Normalized
        let camUp = camView.Up.Normalized
        let fwd   = camView.Forward.Normalized

        // project each axis onto the camera plane; depth > 0 => pointing away from viewer
        let projected =
            allAxes
            |> List.map (fun a ->
                let d = (axisDir east north up a).Normalized
                let px = c + ringR * Vec.dot d right
                let py = c - ringR * Vec.dot d camUp
                let depth = Vec.dot d fwd
                {| axis = a; x = px; y = py; depth = depth |})
            // painter's algorithm: farthest first so nearer circles paint on top
            |> List.sortByDescending (fun m -> m.depth)

        let dim = if enabled then 1.0 else 0.35

        let opacityOf pos facingAway =
            (match pos, facingAway with
             | true,  false -> 1.0
             | true,  true  -> 0.5
             | false, false -> 0.42
             | false, true  -> 0.22) * dim

        let rgbStr a =
            let (r, g, b) = axisRgb a
            sprintf "rgb(%d,%d,%d)" r g b

        let guides =
            [ Svg.circle [ "cx" => fmt c; "cy" => fmt c; "r" => fmt ringR
                           "fill" => "none"; "stroke" => "#ffffff"; "stroke-width" => "1"
                           "stroke-opacity" => fmt (0.12 * dim); "pointer-events" => "none" ]
              Svg.circle [ "cx" => fmt c; "cy" => fmt c; "r" => "2.5"
                           "fill" => "#cccccc"; "fill-opacity" => fmt (0.6 * dim)
                           "pointer-events" => "none" ] ]

        // all connecting lines behind all circles
        let lines =
            projected
            |> List.map (fun m ->
                let pos = isPositive m.axis
                let o = opacityOf pos (m.depth > 0.0)
                Svg.line [ "x1" => fmt c; "y1" => fmt c; "x2" => fmt m.x; "y2" => fmt m.y
                           "stroke" => rgbStr m.axis
                           "stroke-width" => (if pos then "2.5" else "1.5")
                           "stroke-opacity" => fmt o
                           "pointer-events" => "none" ])

        let dots =
            projected
            |> List.collect (fun m ->
                let pos = isPositive m.axis
                let col = rgbStr m.axis
                let o = opacityOf pos (m.depth > 0.0)
                let circleAttrs =
                    [ "cx" => fmt m.x; "cy" => fmt m.y; "r" => fmt dotR
                      "fill" => (if pos then col else "rgb(28,29,31)")
                      "fill-opacity" => fmt o
                      "stroke" => col; "stroke-width" => "2"; "stroke-opacity" => fmt o
                      "style" => (if enabled then "cursor:pointer" else "cursor:default")
                      "pointer-events" => (if enabled then "all" else "none") ]
                let clickAttrs =
                    if enabled then [ onClick (fun _ -> mkMsg m.axis) ]
                    else []
                let circle = Svg.circle (circleAttrs @ clickAttrs)
                let txt =
                    Svg.text [ "x" => fmt m.x; "y" => fmt (m.y + 3.2)
                               "text-anchor" => "middle"
                               "font-size" => "10"
                               "font-family" => "Roboto Mono, Consolas, monospace"
                               "fill" => (if pos then "#ffffff" else col)
                               "fill-opacity" => fmt o
                               "pointer-events" => "none" ] (label m.axis)
                [ circle; txt ])

        Svg.svg
            [ "width" => sprintf "%fpx" boxSize
              "height" => sprintf "%fpx" boxSize
              "viewBox" => sprintf "0 0 %f %f" boxSize boxSize
              "style" => "display:block; overflow:visible; user-select:none" ]
            (guides @ lines @ dots)

    /// The gizmo overlay. `cam` is the live camera view (only its orientation is used);
    /// `enabled` is false while no surface is multi-selected.
    let view
        (mkMsg   : GizmoAxis -> 'msg)
        (enabled : aval<bool>)
        (cam     : aval<CameraView>)
        (rs      : AdaptiveReferenceSystem) : DomNode<'msg> =

        let node =
            adaptive {
                let! camView = cam
                let! up      = rs.up.value
                let! north   = rs.northO
                let! isOn    = enabled
                return buildSvg mkMsg isOn camView up north
            }

        // The gizmo floats over the render body, which starts a camera drag / selection
        // rectangle on mousedown and opens the context menu on right click - swallow those
        // so interacting with the gizmo never moves the camera. Same guard as ToolStrip.
        onBoot "$('#__ID__').on('mousedown mouseup click dblclick contextmenu wheel', function(e) { e.stopPropagation(); });" (
            Incremental.div
                (AttributeMap.ofList
                    [ clazz "pro3d-nav-gizmo"
                      style "position:absolute; left:12px; bottom:12px; width:112px; height:112px; pointer-events:none" ])
                (AList.ofAValSingle node)
        )
