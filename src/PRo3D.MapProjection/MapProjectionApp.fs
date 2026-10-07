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
        /// the scene's body; without one (a scene observed in J2000) there is no map
        planet   : aval<Planet>
        /// OPC surfaces of the scene body
        surfaces : aset<MapSg.MapSurface>
        annotations : MapAnnotations.AnnotationInputs
        /// where the 3D view looks from, body-fixed; drawn as the camera marker. None in the
        /// standalone app, which has no 3D view.
        camera   : aval<Option<V3d>>
        /// what the 3D view's cursor was last over, body-fixed: the preview pick's last hit point
        /// (PRo3D keeps it after Ctrl is released and when a pick misses). Drawn as the cursor
        /// marker, and the centre while *Follow cursor* is on. None only before the first pick, and
        /// in the standalone app unless --cursor is given.
        cursor   : aval<Option<V3d>>
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
            follow   = false
        }

    /// 512 fits a small body, where the whole map is the data. A planet needs far more: at
    /// 1024 pixels a zoom of 512 is 40 m/pixel on Mars, and a Jezero OPC is 2.7 km across.
    /// 32768 reaches 0.6 m/pixel, where the float32 body positions (about 0.2 m at Mars radius)
    /// are still under a pixel.
    let maxZoom = 32768.0

    let private viewProjOf (m : MapProjectionModel) =
        Projection.viewProj m.kind Projection.defaultMaxColatitude m.center m.zoom m.viewport

    /// Keeps the map centre on the map.
    let private clampCenter (kind : MapProjectionKind) (c : V2d) =
        let e = Projection.extent kind Projection.defaultMaxColatitude
        V2d(clamp e.Min.X e.Max.X c.X, clamp e.Min.Y e.Max.Y c.Y)

    /// The centre the map actually shows: the 3D cursor while following, otherwise the model's.
    /// Without a cursor (before the first pick) or with a degenerate one, the model's centre
    /// stands.
    let effectiveCentre (kind : MapProjectionKind) (follow : bool) (cursor : Option<V3d>) (center : V2d) =
        if not follow then center
        else
            match cursor with
            | Some p ->
                let ll = Projection.lonLatR p
                if ll.Z <= 0.0 then center else clampCenter kind (Projection.forward kind ll.X ll.Y)
            | None -> center

    let private withViewport (size : V2d) (m : MapProjectionModel) =
        { m with viewport = V2i(max 1 (int size.X), max 1 (int size.Y)) }

    let update (m : MapProjectionModel) (action : MapProjectionAction) =
        match action with
        | SetKind kind when kind <> m.kind ->
            { m with kind = kind; center = V2d.Zero; zoom = 1.0; dragFrom = None }
        | SetKind _ -> m
        | SetFollow (on, centre) ->
            // stopping keeps what the map shows: the caller passes the followed centre
            if on then { m with follow = true; dragFrom = None }
            else { m with follow = false; center = clampCenter m.kind centre; dragFrom = None }
        | DragStart (at, size, centre) ->
            // grabbing the map is an explicit "I steer now": it stops following and continues from
            // the centre the map is showing, which is the followed one while follow is on
            { withViewport size m with dragFrom = Some at; follow = false; center = clampCenter m.kind centre }
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
            if m.follow then
                // the centre belongs to the cursor while following, so the wheel only changes the
                // zoom -- zooming about the pointer would fight whatever the cursor does next
                zoomed
            else
                let after = Projection.pixelToMap (viewProjOf zoomed) zoomed.viewport at
                { zoomed with center = clampCenter m.kind (zoomed.center + (before - after)) }
        // both set the centre, so they stop following: otherwise the cursor would keep the centre
        // and the button would only change the zoom
        | ResetView ->
            { m with center = V2d.Zero; zoom = 1.0; dragFrom = None; follow = false }
        | FitTo box ->
            // the panel size is the one the last pointer event reported (the initial value until
            // then), so a fit right after opening can be off by the difference; the margin in
            // `fitBox` covers it, and any interaction corrects it
            let center, zoom = Projection.fitBox m.kind Projection.defaultMaxColatitude m.viewport 0.8 maxZoom box
            { m with center = clampCenter m.kind center; zoom = zoom; dragFrom = None; follow = false }

    /// The render control never uses its camera for the map (the effects read `MapViewProj`);
    /// it is there because a render control needs one, and the LoD decider reads it.
    let private camera =
        AVal.constant (Camera.create (CameraView.lookAt V3d.OOI V3d.Zero V3d.OIO) (Frustum.perspective 60.0 0.1 10.0 1.0))

    /// A mouse or wheel event carrying the pointer position and the element size, in pixels;
    /// `f` returns None to ignore it. (Not pointer events: the render control registers its own
    /// pointer handlers, which win.)
    let private sizedEvent (name : string) (preventDefault : bool) (extra : list<string>)
                           (f : list<string> -> V2d -> V2d -> Option<MapProjectionAction>) =
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
                    Option.toList (f rest (Pickler.json.UnPickleOfString pos) (Pickler.json.UnPickleOfString size)) :> seq<_>
                | _ -> Seq.empty
        }

    let private kinds =
        [
            MapProjectionKind.Equirectangular, "Equirectangular"
            MapProjectionKind.PolarNorth,      "Polar north"
            MapProjectionKind.PolarSouth,      "Polar south"
        ]

    /// The centre the map shows, adaptively: `effectiveCentre` over the model and the 3D cursor.
    /// Built once per view and read both by the view-projection and, forced, by the handlers that
    /// take the map over from following (a drag, or switching the toggle off).
    let shownCentre (inputs : MapInputs) (m : AdaptiveMapProjectionModel) =
        adaptive {
            let! kind = m.kind
            let! follow = m.follow
            let! center = m.center
            if not follow then return center
            else
                let! cursor = inputs.cursor
                return effectiveCentre kind follow cursor center
        }

    let private mapSg (inputs : MapInputs) (centre : aval<V2d>) (m : AdaptiveMapProjectionModel) (values : RenderClientValues) : ISg<MapProjectionAction> =
        match PRo3D.Core.Surface.Sg.hackRunner with
        | None -> Sg.empty
        | Some runner ->
            let maxRadius = MapSg.placedMaxRadius inputs.surfaces
            let viewProj =
                adaptive {
                    let! kind = m.kind
                    let! center = centre
                    let! zoom = m.zoom
                    let! size = values.size
                    return Projection.viewProj kind Projection.defaultMaxColatitude center zoom size
                }
            let maxColatitude = AVal.constant Projection.defaultMaxColatitude
            let unitsPerPixel =
                adaptive {
                    let! kind = m.kind
                    let! zoom = m.zoom
                    let! size = values.size
                    return Projection.unitsPerPixel kind Projection.defaultMaxColatitude zoom size
                }
            let view : MapSg.MapView =
                {
                    kind          = m.kind
                    viewProj      = viewProj
                    maxColatitude = maxColatitude
                    unitsPerPixel = unitsPerPixel
                    radiusRange   = maxRadius |> AVal.map Projection.radiusRange
                }
            // interactive: patches stream in, detail follows the map window (phase 1.5)
            let lod = MapSg.mapLod m.kind viewProj values.size maxColatitude MapSg.defaultTargetPixels
            let cfg = { OpcSg.defaultConfig values.signature runner lod "map" with asyncLoading = true }
            let markers : MapSg.MapMarkers = { camera = inputs.camera; cursor = inputs.cursor }
            MapAnnotations.mapWithAnnotations cfg view markers inputs.surfaces inputs.annotations |> Sg.noEvents

    let private toolbar (inputs : MapInputs) (centre : aval<V2d>) (m : AdaptiveMapProjectionModel) =
        div [ style "position:absolute; top:6px; left:6px; z-index:10" ] [
            yield
                Incremental.div (AttributeMap.ofList [ clazz "ui mini buttons" ]) (
                    alist {
                        let! current = m.kind
                        for kind, name in kinds do
                            let active = if kind = current then " active" else ""
                            yield button [ clazz ("ui inverted button" + active); onClick (fun _ -> SetKind kind) ] [ text name ]
                    })
            // the extent is read in the click handler, not in an adaptive computation:
            // forcing is fine in a UI callback and keeps the button out of the map's dependencies
            yield
                Incremental.div (AttributeMap.ofList [ style "display:inline-block" ]) (
                    alist {
                        let extent = MapSg.dataExtent m.kind inputs.surfaces
                        yield
                            button [
                                clazz "ui mini inverted basic button"; style "margin-left:6px"
                                onClick (fun _ ->
                                    match AVal.force extent with
                                    | Some box -> FitTo box
                                    | None -> ResetView)
                            ] [ text "Zoom to data" ]
                    })
            // Follow cursor: switching it off keeps what the map shows, so the handler passes the
            // centre it is showing. Forcing in a click handler is fine; it keeps the button out of
            // the map's dependencies
            yield
                Incremental.div (AttributeMap.ofList [ style "display:inline-block" ]) (
                    alist {
                        let! follow = m.follow
                        let active = if follow then " active" else ""
                        yield
                            button [
                                clazz ("ui mini inverted basic button" + active); style "margin-left:6px"
                                onClick (fun _ -> SetFollow(not follow, AVal.force centre))
                            ] [ text "Follow cursor" ]
                    })
            yield button [ clazz "ui mini inverted basic button"; style "margin-left:6px"; onClick (fun _ -> ResetView) ] [ text "Reset view" ]
        ]

    let view (inputs : MapInputs) (m : AdaptiveMapProjectionModel) : DomNode<MapProjectionAction> =
        let centre = shownCentre inputs m
        // any body the scene is referenced to, planets included: the map is a panel one opens,
        // so a Mars scene pays nothing until it is opened (#772). Without a body (a scene
        // observed in J2000) there is nothing to project onto.
        let available = inputs.planet |> AVal.map (fun p -> p <> Planet.None)
        let attributes =
            AttributeMap.ofList [
                style "width:100%; height:100%; background-color:#222222"
                clazz "mapprojectionrendercontrol"
                // left button only: a right or middle click must not take the map over from following
                sizedEvent "onmousedown" false [ "event.button" ] (fun rest at size ->
                    match rest with
                    | "0" :: _ -> Some (DragStart(at, size, AVal.force centre))
                    | _ -> None)
                // a move with no button held ends a drag whose mouseup happened outside the panel
                sizedEvent "onmousemove" false [ "event.buttons" ] (fun rest at size ->
                    match rest with
                    | "0" :: _ -> Some DragEnd
                    | _ -> Some (DragMove(at, size)))
                sizedEvent "onmouseup" false [] (fun _ _ _ -> Some DragEnd)
                // browser deltaY: about 100 per notch, positive when scrolling down (= zoom out)
                sizedEvent "onwheel" true [ "event.deltaY" ] (fun rest at size ->
                    let deltaY =
                        match rest with
                        | d :: _ ->
                            match Double.TryParse(d, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                            | true, v -> v
                            | _ -> 0.0
                        | [] -> 0.0
                    Some (Zoom(-deltaY / 100.0, at, size)))
            ]
        Incremental.div (AttributeMap.ofList [ style "position:relative; width:100%; height:100%" ]) (
            alist {
                let! available = available
                if available then
                    yield toolbar inputs centre m
                    // no host JavaScript needed: the mouse events carry the panel size
                    yield DomNode.RenderControl(attributes, camera, mapSg inputs centre m, RenderControlConfig.standard)
                else
                    yield
                        div [ style "padding:16px; color:#bbbbbb; font-style:italic" ] [
                            text "The map projection view needs a body to project onto. Set the scene's reference system to a body (Mars, Phobos, Deimos, Didymos, Dimorphos, ...)."
                        ]
            })

    /// The panel as an app of its own: `PRo3D.MapProjection.Standalone.exe` (its Program.fs) serves it
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
