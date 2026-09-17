namespace PRo3D.MapProjection

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Core

/// What the panel needs from the viewer. Built by the host (`PRo3D.Viewer.MapProjectionHost`);
/// the map project never sees the viewer model.
type MapInputs =
    {
        /// the scene's planet; the panel only draws for small bodies
        planet   : aval<Planet>
        /// OPC surfaces of the scene body
        surfaces : aset<MapSg.MapSurface>
        annotations : MapAnnotations.AnnotationInputs
    }

/// The map projection panel (#772): update and view.
module MapProjectionApp =

    let initial =
        {
            kind     = MapProjectionKind.Equirectangular
            center   = V2d.Zero
            zoom     = 1.0
            viewport = V2i(1024, 512)
            dragFrom = None
        }

    let maxZoom = 512.0

    let private viewProjOf (m : MapProjectionModel) =
        Projection.viewProj m.kind Projection.defaultMaxColatitude m.center m.zoom m.viewport

    /// Keeps the map centre on the map.
    let private clampCenter (kind : MapProjectionKind) (c : V2d) =
        let e = Projection.extent kind Projection.defaultMaxColatitude
        V2d(clamp e.Min.X e.Max.X c.X, clamp e.Min.Y e.Max.Y c.Y)

    let private withViewport (size : V2d) (m : MapProjectionModel) =
        { m with viewport = V2i(max 1 (int size.X), max 1 (int size.Y)) }

    let update (m : MapProjectionModel) (action : MapProjectionAction) =
        match action with
        | SetKind kind when kind <> m.kind ->
            { m with kind = kind; center = V2d.Zero; zoom = 1.0; dragFrom = None }
        | SetKind _ -> m
        | DragStart (at, size) ->
            { withViewport size m with dragFrom = Some at }
        | DragMove (at, size) ->
            match m.dragFrom with
            | None -> m
            | Some from ->
                // grab and drag: the map point under the pointer follows it
                let m = withViewport size m
                let vp = viewProjOf m
                let a = Projection.pixelToMap vp m.viewport from
                let b = Projection.pixelToMap vp m.viewport at
                { m with center = clampCenter m.kind (m.center + (a - b)); dragFrom = Some at }
        | DragEnd ->
            { m with dragFrom = None }
        | Zoom (steps, at, size) ->
            // zoom about the pointer: the map point under it stays put. The size comes with the
            // event -- the viewport of the last drag may be stale (#772 review)
            let m = withViewport size m
            let before = Projection.pixelToMap (viewProjOf m) m.viewport at
            let zoomed = { m with zoom = clamp 1.0 maxZoom (m.zoom * Math.Pow(1.25, steps)) }
            let after = Projection.pixelToMap (viewProjOf zoomed) zoomed.viewport at
            { zoomed with center = clampCenter m.kind (zoomed.center + (before - after)) }
        | ResetView ->
            { m with center = V2d.Zero; zoom = 1.0; dragFrom = None }

    /// The render control never uses its camera for the map (the effects read `MapViewProj`);
    /// it is there because a render control needs one, and the LoD decider reads it.
    let private camera =
        AVal.constant (Camera.create (CameraView.lookAt V3d.OOI V3d.Zero V3d.OIO) (Frustum.perspective 60.0 0.1 10.0 1.0))

    /// A mouse or wheel event carrying the pointer position and the element size, in pixels.
    /// (Not pointer events: the render control registers its own pointer handlers, which win.)
    let private sizedEvent (name : string) (preventDefault : bool) (extra : list<string>)
                           (f : list<string> -> V2d -> V2d -> MapProjectionAction) =
        name, AttributeValue.Event {
            clientSide = fun send src ->
                String.concat ";" [
                    yield "var rect = getBoundingClientRect(this)"
                    if preventDefault then yield "event.preventDefault()"
                    // toFixed: FsPickler reads a V2d component only from a float literal, not "650"
                    yield send src ([ "{ X: (event.clientX - rect.left).toFixed(10), Y: (event.clientY - rect.top).toFixed(10) }"
                                      "{ X: rect.width.toFixed(10), Y: rect.height.toFixed(10) }" ] @ extra)
                ]
            serverSide = fun _ _ args ->
                match args with
                | pos :: size :: rest ->
                    Seq.singleton (f rest (Pickler.json.UnPickleOfString pos) (Pickler.json.UnPickleOfString size))
                | _ -> Seq.empty
        }

    let private kinds =
        [
            MapProjectionKind.Equirectangular, "Equirectangular"
            MapProjectionKind.PolarNorth,      "Polar north"
            MapProjectionKind.PolarSouth,      "Polar south"
        ]

    let private mapSg (inputs : MapInputs) (m : AdaptiveMapProjectionModel) (values : RenderClientValues) : ISg<MapProjectionAction> =
        match PRo3D.Core.Surface.Sg.hackRunner with
        | None -> Sg.empty
        | Some runner ->
            let maxRadius = MapSg.placedMaxRadius inputs.surfaces
            let viewProj =
                adaptive {
                    let! kind = m.kind
                    let! center = m.center
                    let! zoom = m.zoom
                    let! size = values.size
                    return Projection.viewProj kind Projection.defaultMaxColatitude center zoom size
                }
            let maxColatitude = AVal.constant Projection.defaultMaxColatitude
            let view : MapSg.MapView =
                {
                    kind          = m.kind
                    viewProj      = viewProj
                    maxColatitude = maxColatitude
                    radiusRange   = maxRadius |> AVal.map Projection.radiusRange
                }
            // interactive: patches stream in, detail follows the map window (phase 1.5)
            let lod = MapSg.mapLod m.kind viewProj values.size maxColatitude MapSg.defaultTargetPixels
            let cfg = { OpcSg.defaultConfig values.signature runner lod "map" with asyncLoading = true }
            MapAnnotations.mapWithAnnotations cfg view inputs.surfaces inputs.annotations |> Sg.noEvents

    let private toolbar (m : AdaptiveMapProjectionModel) =
        div [ style "position:absolute; top:6px; left:6px; z-index:10" ] [
            yield
                Incremental.div (AttributeMap.ofList [ clazz "ui mini buttons" ]) (
                    alist {
                        let! current = m.kind
                        for kind, name in kinds do
                            let active = if kind = current then " active" else ""
                            yield button [ clazz ("ui inverted button" + active); onClick (fun _ -> SetKind kind) ] [ text name ]
                    })
            yield button [ clazz "ui mini inverted basic button"; style "margin-left:6px"; onClick (fun _ -> ResetView) ] [ text "Reset view" ]
        ]

    let view (inputs : MapInputs) (m : AdaptiveMapProjectionModel) : DomNode<MapProjectionAction> =
        let available = inputs.planet |> AVal.map CooTransformation.isSmallBody
        let attributes =
            AttributeMap.ofList [
                style "width:100%; height:100%; background-color:#222222"
                clazz "mapprojectionrendercontrol"
                sizedEvent "onmousedown" false [] (fun _ at size -> DragStart(at, size))
                // a move with no button held ends a drag whose mouseup happened outside the panel
                sizedEvent "onmousemove" false [ "event.buttons" ] (fun rest at size ->
                    match rest with
                    | "0" :: _ -> DragEnd
                    | _ -> DragMove(at, size))
                sizedEvent "onmouseup" false [] (fun _ _ _ -> DragEnd)
                // browser deltaY: about 100 per notch, positive when scrolling down (= zoom out)
                sizedEvent "onwheel" true [ "event.deltaY" ] (fun rest at size ->
                    let deltaY =
                        match rest with
                        | d :: _ ->
                            match Double.TryParse(d, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                            | true, v -> v
                            | _ -> 0.0
                        | [] -> 0.0
                    Zoom(-deltaY / 100.0, at, size))
            ]
        Incremental.div (AttributeMap.ofList [ style "position:relative; width:100%; height:100%" ]) (
            alist {
                let! available = available
                if available then
                    yield toolbar m
                    // no host JavaScript needed: the mouse events carry the panel size
                    yield DomNode.RenderControl(attributes, camera, mapSg inputs m, RenderControlConfig.standard)
                else
                    yield
                        div [ style "padding:16px; color:#bbbbbb; font-style:italic" ] [
                            text "The map projection view is available for small bodies (Phobos, Deimos, Didymos, Dimorphos). Set the reference system to one of them."
                        ]
            })

    /// The panel as an app of its own: `PRo3D.MapProjection.exe` (Program.fs) serves it
    /// standalone, PRo3D embeds `view` as a page.
    let app (inputs : MapInputs) : App<MapProjectionModel, AdaptiveMapProjectionModel, MapProjectionAction> =
        {
            initial   = initial
            update    = update
            view      = fun m ->
                require Html.semui (
                    body [ style "background: #1B1C1E; width:100%; height:100%; margin:0; overflow:hidden" ] [
                        view inputs m
                    ]
                )
            threads   = fun _ -> ThreadPool.empty
            unpersist = Unpersist.instance
        }
