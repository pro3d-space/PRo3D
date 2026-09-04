namespace PRo3D.Viewer

open System
open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Operators
open PRo3D.Base
open PRo3D.Core

/// Small interactive axis gizmo overlaid in the bottom-left corner of the render view.
///
/// It draws the three reference-system directions - north (red), east (green) and up
/// (blue), the same colours as the in-scene reference cross - as lines ending in
/// labelled circles, plus the three opposite directions as toned-down lines and
/// circles. Whether the circles are labelled N/E/U or X/Y/Z depends on the selected
/// reference system; see `labelOf`. The gizmo is a pure function of the camera orientation
/// and the reference system, redrawn whenever either changes; it carries no model state
/// and does not touch scene geometry - the bounding-box / framing maths for a click
/// happens in `updateViewer` (see `ViewerAction.OrientCameraToGizmoAxis`).
///
/// Clicking a circle asks the viewer to look straight along that axis onto the centre of
/// the currently multi-selected surfaces' bounding box. With nothing multi-selected the
/// circles render disabled and dispatch nothing.
module NavigationGizmo =

    /// One of the six endpoints, named by the direction it points in the reference-system
    /// frame. The *labels* drawn on them depend on the reference system - see `labelOf`.
    type GizmoAxis =
        | North | South
        | East  | West
        | Up    | Down

    let private allAxes = [ North; South; East; West; Up; Down ]

    let private isPositive =
        function
        | North | East | Up   -> true
        | South | West | Down -> false

    /// Small bodies are navigated from *outside*: their OPC data wraps the whole body, so
    /// the meaningful frame is the body-fixed one. A local tangent frame at a surface
    /// point has north and east grazing the surface, which makes four of the six snaps
    /// look across the body rather than at it, and it degenerates entirely at the body
    /// centre - where `InferCoordSystem` puts the reference system for whole-body data.
    /// Same predicate and same rationale as `ReferenceSystem.bodyAwareSky`.
    ///
    /// The non-planetary frames (None/JPL/ENU) are already fixed cartesian frames in
    /// `ReferenceSystem.updateCoordSystemAt`, so they are left to flow through `enuBasis`
    /// unchanged - JPL's up is -Z, which a blanket substitution would silently flip.
    let private usesBodyFixedFrame (planet : Planet) = CooTransformation.isSmallBody planet

    /// Reference systems that present their frame as a compass get compass letters; the
    /// ones whose frame is a fixed cartesian one get axis letters. For None/JPL this
    /// mirrors the split the in-scene cross already makes in `PRo3D.Core.Sg.view`.
    let private usesCompassLabels (planet : Planet) =
        match planet with
        | Planet.None | Planet.JPL -> false
        | p                        -> not (usesBodyFixedFrame p)

    /// Where axis letters are used, the mapping follows the in-scene `xyzSystem` cross
    /// and `TransformationApp.getReferenceSystemBasis_global`: X = north, Y = east, Z = up.
    let private labelOf (planet : Planet) =
        if usesCompassLabels planet then
            function
            | North -> "N" | South -> "-N"
            | East  -> "E" | West  -> "-E"
            | Up    -> "U" | Down  -> "-U"
        else
            function
            | North -> "X" | South -> "-X"
            | East  -> "Y" | West  -> "-Y"
            | Up    -> "Z" | Down  -> "-Z"

    /// Base hue per direction: north red, east green, up blue - the same assignment the
    /// in-scene reference cross uses for all planets (`PRo3D.Core.Sg.view`).
    let private axisRgb =
        function
        | North | South -> (0xE0, 0x3B, 0x3B)
        | East  | West  -> (0x46, 0xC0, 0x55)
        | Up    | Down  -> (0x4A, 0xA3, 0xFF)

    /// ENU basis from the reference-system up/north. `east = north x up`, matching the
    /// convention in `PRo3D.Core.Sg.getOrientationSystem` / `Sg.view`.
    let private enuBasis (up : V3d) (north : V3d) : V3d * V3d * V3d =
        let u = up.Normalized
        let n = north.Normalized
        let e = (Vec.cross n u).Normalized
        e, n, u

    /// The (east, north, up) triple the six endpoints are built from. Body-fixed for the
    /// small bodies (see `usesBodyFixedFrame`), the local tangent frame otherwise. The
    /// body-fixed assignment follows the `xyzSystem` cross's letters: X = north, Y = east,
    /// Z = up, so the circles labelled X/Y/Z point along global +X/+Y/+Z.
    let private frameOf (planet : Planet) (up : V3d) (north : V3d) : V3d * V3d * V3d =
        if usesBodyFixedFrame planet then V3d.OIO, V3d.IOO, V3d.OOI
        else enuBasis up north

    let private axisDir (east : V3d) (north : V3d) (up : V3d) (a : GizmoAxis) : V3d =
        match a with
        | North ->  north | South -> -north
        | East  ->  east  | West  -> -east
        | Up    ->  up    | Down  -> -up

    // -- helpers used by the update handler (imperative context; plain values) ------------

    /// World-space unit direction of a gizmo axis for the given reference system.
    let resolveAxisWorldDir (rs : ReferenceSystem) (a : GizmoAxis) : V3d =
        let e, n, u = frameOf rs.planet rs.up.value rs.northO
        (axisDir e n u a).Normalized

    /// Camera "up" (sky) to use after snapping onto `a`. For a top/bottom view the map
    /// convention is North-up; otherwise the reference Up direction stays vertical.
    let gizmoCameraUp (rs : ReferenceSystem) (a : GizmoAxis) : V3d =
        let _, n, u = frameOf rs.planet rs.up.value rs.northO
        match a with
        | Up   ->  n
        | Down -> -n
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
        (northRaw : V3d)
        (planet   : Planet) : DomNode<'msg> =

        let label = labelOf planet
        let east, north, up = frameOf planet upRaw northRaw
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
                let! planet  = rs.planet
                let! isOn    = enabled
                return buildSvg mkMsg isOn camView up north planet
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
