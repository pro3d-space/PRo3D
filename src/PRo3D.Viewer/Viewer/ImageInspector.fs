namespace PRo3D.Viewer

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.ImageMapping
open PRo3D.InstrumentProjection

/// Image Inspector (EXPERIMENTAL, milestone 1 of docs/dev/shadowEstimation.md): the selected
/// projected image as a 2D panel, wired both ways to the 3D view.
///
/// - hover in 2D: the pixel's camera ray through the image's projector, intersected with the
///   surfaces, marked in 3D (`hoverSg`);
/// - hover in 3D: the main view's surface hit projected into the image, marked in 2D.
///
/// The panel draws the image with panel NDC = projector NDC, up to zoom/pan (ImageView) (a quad sampled at
/// tc = 0.5 + 0.5 * ndc, exactly as the projection shader samples it), so a pointer position
/// in the panel IS the projector NDC: no pixel convention sits between panel and projection.
/// Pixel coordinates are only a read-out.
///
/// Panel id "imageinspector": "instrumentview" is the View Planner's rover camera.
module ImageInspector =

    /// Everything needed to go between the selected image and render space.
    type Context =
        {
            imageId : Guid
            /// projection surface's body-fixed frame -> projector clip space, the same matrix
            /// the projection stack uses for this image
            full    : Trafo3d
            /// projection surface's body-fixed frame -> render space (its placement)
            toWorld : Trafo3d
            /// native image size from the statistics sidecar, when present
            size    : Option<V2i>
            /// unit vector towards the sun at the image's acquisition time, render space
            sun     : Result<V3d, string>
            /// the body centre (body-fixed origin), render space
            centre  : V3d
        }

    // projectDirect calls SPICE behind a global lock; hovering must not pay for it per
    // mouse move. Keyed like ProjectedImagesListHelpers' projector cache.
    let private projectorCache =
        System.Collections.Concurrent.ConcurrentDictionary<Guid * ProjectionMethod * (float * float * float) * string * string, Option<Trafo3d>>()

    // sun direction per (image, frame, body): SPICE, and constant for an image
    let private sunCache =
        System.Collections.Concurrent.ConcurrentDictionary<Guid * string * string, Result<V3d, string>>()

    /// The surface the projection is placed on: the first one bound to a SPICE body, else one
    /// inheriting the scene body -- the same choice as the fly-to (Viewer.flyToImageCamera).
    let private projectionSurface (gis : Gis.GisApp) (surfaces : SurfaceModel) =
        let bound =
            gis.gisSurfaces
            |> HashMap.toSeq
            |> Seq.tryPick (fun (sid, gs) ->
                match gs.entity, gs.referenceFrame with
                | Some _, Some frame -> Some (sid, frame)
                | _ -> None)
        match bound with
        | Some _ -> bound
        | None ->
            surfaces.surfaces.flat
            |> HashMap.toSeq
            |> Seq.tryPick (fun (sid, _) ->
                Gis.GisApp.getSpiceReferenceSystem gis sid
                |> Option.map (fun r -> sid, r.referenceFrame))

    let tryContext (gis : Gis.GisApp) (surfaces : SurfaceModel) (refSystem : ReferenceSystem) : Result<Context, string> =
        let list = gis.projectedImageList
        let image = list.selectedImage |> Option.bind (fun id -> ProjectedImageListModel.tryFind id list)
        match image, Gis.GisApp.getObserverSystem gis, projectionSurface gis surfaces with
        | None, _, _ -> Result.Error "no image selected"
        | _, None, _ -> Result.Error "no observed body set"
        | _, _, None -> Result.Error "no surface bound to a SPICE body"
        | Some image, Some observerSystem, Some (surfaceId, frame) ->
            let (EntitySpiceName observer) = observerSystem.body
            let b = list.boresightAdjustment
            let boresightKey = (b.roll.value, b.pitch.value, b.yaw.value)
            let key = (image.id, list.projectionMethod, boresightKey, observer, frame.Value)
            let full =
                projectorCache.GetOrAdd(key, fun _ ->
                    // same boresight composition as ProjectedImagesListHelpers.computeBoresight
                    let boresight =
                        Trafo3d.RotationXInDegrees(b.yaw.value) * Trafo3d.RotationYInDegrees(b.pitch.value) * Trafo3d.RotationZInDegrees(b.roll.value)
                    let metadata = InstrumentMetadata.tryParseMetadataForImagePath image.texture
                    // "MARS" only as the fallback target, as the viewer passes it
                    Visualization.projectDirect observer frame.Value metadata "MARS" (Some boresight) list.projectionMethod)
            let surface =
                surfaces.surfaces.flat |> HashMap.tryFind surfaceId |> Option.map Leaf.toSurface
            match full, surface with
            | None, _ -> Result.Error "the image's projector did not resolve (SPICE coverage?)"
            | _, None -> Result.Error "projection surface not found"
            | Some full, Some surface ->
                let observedSystem = Gis.GisApp.getSpiceReferenceSystem gis surfaceId
                let placement = TransformationApp.fullTrafo' surface.transformation refSystem observedSystem (Some observerSystem)
                let size =
                    match InstrumentMetadata.tryParseMetadataForImagePath image.texture with
                    | _, Some meta when meta.image_width > 0 && meta.image_height > 0 -> Some (V2i(meta.image_width, meta.image_height))
                    | _ -> None
                let toWorld = placement * surface.preTransform
                // at the image's own time -- never the scene's current SPICE time
                let sunBody =
                    match observedSystem, InstrumentMetadata.tryParseMetadataForImagePath image.texture with
                    | Some r, (Some mbi, _) ->
                        let (EntitySpiceName body) = r.body
                        sunCache.GetOrAdd((image.id, frame.Value, body), fun _ ->
                            PRo3D.SPICE.InstrumentProjection.withSpiceLock (fun () ->
                                InstrumentObservation.sunDirection frame.Value body mbi.obs_date))
                    | None, _ -> Result.Error "the projection surface has no SPICE body"
                    | _ -> Result.Error "the image has no mbi sidecar (no acquisition time)"
                let sun = sunBody |> Result.map (fun d -> toWorld.Forward.TransformDir d |> Vec.normalize)
                Ok { imageId = image.id; full = full; toWorld = toWorld; size = size
                     sun = sun; centre = toWorld.Forward.TransformPos V3d.Zero }

    /// Camera ray through a projector NDC point, render space.
    let ray (ctx : Context) (ndc : V2d) : Ray3d =
        let near = ctx.full.Backward.TransformPosProj(V3d(ndc.X, ndc.Y, -1.0))
        let far  = ctx.full.Backward.TransformPosProj(V3d(ndc.X, ndc.Y,  1.0))
        let o = ctx.toWorld.Forward.TransformPos near
        let d = ctx.toWorld.Forward.TransformPos far - o
        Ray3d(o, Vec.normalize d)

    /// Render-space point -> projector NDC; None behind the projector or outside the image.
    let project (ctx : Context) (p : V3d) : Option<V2d> =
        let b = ctx.toWorld.Backward.TransformPos p
        let h = ctx.full.Forward.Transform(V4d(b.X, b.Y, b.Z, 1.0))
        if h.W <= 0.0 || not (Double.IsFinite h.W) then None
        else
            let n = V2d(h.X / h.W, h.Y / h.W)
            if abs n.X <= 1.0 && abs n.Y <= 1.0 then Some n else None

    /// Projector NDC -> pixel, 0-based, origin top-left (InstrumentObservation's Image convention).
    let pixelOf (ctx : Context) (ndc : V2d) =
        ctx.size |> Option.map (fun s ->
            InstrumentObservation.ndcToPixel s InstrumentObservation.PixelConvention.Image ndc)

    /// The hover for a pointer position, given a picking function (Picking.pickRay lives
    /// after this file).
    let hover (pick : Ray3d -> Option<V3d>) (ctx : Context) (ndc : V2d) : ImageHover =
        let r = ray ctx ndc
        { ndc = ndc; pixel = pixelOf ctx ndc; direction = r.Direction; hit = pick r }

    // ---------------------------------------------------------------- shadow measurement

    /// Render-space point -> projector NDC, unbounded (the sun line leaves the image).
    let private projectAny (ctx : Context) (p : V3d) : Option<V2d> =
        let b = ctx.toWorld.Backward.TransformPos p
        let h = ctx.full.Forward.Transform(V4d(b.X, b.Y, b.Z, 1.0))
        if h.W <= 0.0 || not (Double.IsFinite h.W) then None
        else Some (V2d(h.X / h.W, h.Y / h.W))

    /// The shadow's direction in the image: the ray from the rim away from the sun, projected.
    /// A projected 3D line is a 2D line, so two points fix it. Origin and unit direction,
    /// projector NDC.
    let sunLine (ctx : Context) (sun : V3d) (rim : V3d) : Option<V2d * V2d> =
        let step = 1e-3 * Vec.distance (ray ctx V2d.Zero).Origin rim
        match projectAny ctx rim, projectAny ctx (rim - sun * step) with
        | Some a, Some b when Vec.distance a b > 1e-12 -> Some (a, Vec.normalize (b - a))
        | _ -> None

    /// Nearest point on the (forward) sun line.
    let snap (origin : V2d, dir : V2d) (q : V2d) =
        origin + dir * max 0.0 (Vec.dot (q - origin) dir)

    /// Where the camera ray through `ndc` meets the sun ray from the rim (closest approach of
    /// the two lines); the parameter along the shadow direction must be positive.
    let private triangulate (ctx : Context) (sun : V3d) (rim : V3d) (ndc : V2d) : Option<V3d> =
        let r = ray ctx ndc
        let u = -sun
        let c = r.Direction
        let w0 = rim - r.Origin
        let b = Vec.dot u c
        let d = Vec.dot u w0
        let e = Vec.dot c w0
        let denom = 1.0 - b * b
        if denom < 1e-12 then None
        else
            let t = (b * e - d) / denom
            if t > 0.0 then Some (rim + u * t) else None

    /// Below this phase angle (degrees) camera and sun rays are too close to parallel for the
    /// triangulation to mean much; the read-out says so.
    let lowPhase = 15.0

    /// The measurement for a rim point and a (snapped) tip pixel. Up is radial from the
    /// body centre (v1; a plane fitted through rim clicks is next).
    let measure (pick : Ray3d -> Option<V3d>) (ctx : Context) (rim : V3d) (tipNdc : V2d) : Result<ShadowResult, string> =
        match ctx.sun with
        | Result.Error e -> Result.Error e
        | Ok sun ->
            let up = Vec.normalize (rim - ctx.centre)
            let elevation = asin (clamp -1.0 1.0 (Vec.dot sun up))
            if elevation <= 0.0 then Result.Error "the sun is below the local horizon at the rim"
            else
                match triangulate ctx sun rim tipNdc with
                | None -> Result.Error "the tip does not lie on the shadow side of the rim"
                | Some tip ->
                    let depthOf (t : V3d) = Vec.dot (rim - t) up
                    let depth = depthOf tip
                    let horizontal = (tip - rim) - up * Vec.dot (tip - rim) up
                    // one pixel further along the sun line
                    let depthPerPixel =
                        match ctx.size, sunLine ctx sun rim with
                        | Some s, Some (_, dir) ->
                            triangulate ctx sun rim (tipNdc + dir * (2.0 / float s.X))
                            |> Option.map (fun t -> abs (depthOf t - depth))
                        | _ -> None
                    let toCamera = Vec.normalize ((ray ctx tipNdc).Origin - rim)
                    Ok {
                        phase = acos (clamp -1.0 1.0 (Vec.dot sun toCamera)) * Constant.DegreesPerRadian
                        tip = tip
                        meshTip = pick (ray ctx tipNdc)
                        up = up
                        depth = depth
                        length = Vec.length horizontal
                        sunElevation = elevation * Constant.DegreesPerRadian
                        depthPerPixel = depthPerPixel
                    }

    /// A measurement click: the first sets the rim (pixel -> mesh), the second the tip
    /// (snapped to the sun line, triangulated); a third starts over.
    let click (pick : Ray3d -> Option<V3d>) (ctx : Context) (s : ShadowMeasure) (ndc : V2d) : ShadowMeasure =
        let setRim () =
            match pick (ray ctx ndc) with
            | Some p -> { s with rim = Some (ndc, p); tip = None; result = None }
            | None -> { s with rim = None; tip = None; result = Some (Result.Error "the rim pixel does not hit the surface") }
        match s.rim, s.tip with
        | Some (_, rim), None ->
            match ctx.sun |> Result.map (fun sun -> sunLine ctx sun rim) with
            | Ok (Some line) ->
                let tip = snap line ndc
                { s with tip = Some tip; result = Some (measure pick ctx rim tip) }
            | Ok None -> { s with result = Some (Result.Error "no sun line in this image") }
            | Result.Error e -> { s with result = Some (Result.Error e) }
        | _ -> setRim ()

    /// 3D: rim, triangulated tip, the sun ray between them, the depth, the mesh hit under the tip.
    let measureSg (s : aval<ShadowMeasure>) (view : aval<CameraView>) : ISg<'msg> =
        (s, view)
        ||> AVal.map2 (fun s view ->
            let cross (c : C4b) (p : V3d) =
                let k = 0.004 * Vec.distance view.Location p
                Sg.ofList [
                    for a in [ V3d.IOO; V3d.OIO; V3d.OOI ] ->
                        PRo3D.Core.Drawing.Sg.lines c 3.0 [| p - a * k; p + a * k |]
                ]
            match s.rim, s.result with
            | Some (_, rim), Some (Ok r) ->
                Sg.ofList [
                    yield cross C4b.Orange rim
                    yield cross C4b.Magenta r.tip
                    yield PRo3D.Core.Drawing.Sg.lines C4b.Yellow 2.0 [| rim; r.tip |]
                    // the depth: straight up from the tip to the rim's height
                    yield PRo3D.Core.Drawing.Sg.lines C4b.Cyan 2.0 [| r.tip; r.tip + r.up * r.depth |]
                    match r.meshTip with
                    | Some m -> yield cross C4b.Gray m
                    | None -> ()
                ]
            | Some (_, rim), _ -> cross C4b.Orange rim
            | _ -> Sg.empty)
        |> Sg.dynamic
        |> Sg.noEvents

    // ---------------------------------------------------------------- 3D marker

    /// Marker at the hovered pixel's surface hit: an axis cross (gimbal colours) and the last
    /// stretch of the camera ray leading to it. Constant screen size; built around the hit
    /// with a translation (Drawing.Sg.lines re-bases on the first point) for precision.
    let hoverSg (hover : aval<Option<ImageHover>>) (view : aval<CameraView>) : ISg<'msg> =
        (hover, view)
        ||> AVal.map2 (fun hover view ->
            match hover with
            | Some { hit = Some p; direction = d } ->
                let s = 0.005 * Vec.distance view.Location p
                let axis (c : C4b) (a : V3d) = PRo3D.Core.Drawing.Sg.lines c 3.0 [| p - a * s; p + a * s |]
                Sg.ofList [
                    axis C4b.Red V3d.IOO
                    axis C4b.Green V3d.OIO
                    axis C4b.Blue V3d.OOI
                    // the camera ray, 6 marker sizes long, ending at the hit
                    PRo3D.Core.Drawing.Sg.lines C4b.Yellow 2.0 [| p; p - d * (6.0 * s) |]
                ]
            | _ -> Sg.empty)
        |> Sg.dynamic
        |> Sg.noEvents

    // ---------------------------------------------------------------- 2D panel

    /// Most magnification the panel allows: at AFC's 1020 px, 64x shows ~16 pixels across.
    let maxScale = 64.0

    /// Zoom/pan state for a panel event. Zoom keeps the image point under the cursor fixed;
    /// the centre stays inside the image so the view cannot be lost.
    let updateView (v : ImageView) (msg : ImageInspectorAction) : ImageView =
        let clampCenter (scale : float) (c : V2d) =
            let r = 1.0 - 1.0 / scale
            V2d(clamp -r r c.X, clamp -r r c.Y)
        match msg with
        | ImageInspectorAction.Zoom (steps, at) ->
            let fixedPoint = ImageView.toImage v at
            let scale = clamp 1.0 maxScale (v.scale * Math.Pow(2.0, steps))
            { v with scale = scale; center = clampCenter scale (fixedPoint - at / scale) }
        | ImageInspectorAction.DragStart p -> { v with dragFrom = Some p; dragMoved = false }
        | ImageInspectorAction.DragMove p ->
            match v.dragFrom with
            | Some from ->
                { v with
                    center = clampCenter v.scale (v.center - (p - from) / v.scale)
                    dragFrom = Some p
                    // a jitter is still a click
                    dragMoved = v.dragMoved || Vec.distance p from > 0.01 }
            | None -> v
        // a buttonless move (Hover) also ends a drag: a mouseup lost outside the panel, or
        // swallowed by the render control, must not leave hovering blocked
        | ImageInspectorAction.DragEnd
        | ImageInspectorAction.Hover _ -> { v with dragFrom = None }
        | ImageInspectorAction.ResetView -> ImageView.initial
        | ImageInspectorAction.Click _
        | ImageInspectorAction.ToggleMeasure
        | ImageInspectorAction.ClearMeasure
        | ImageInspectorAction.CreateScaleBar -> v

    /// A mouse event carrying the panel NDC of the pointer (and optional extra values); moves
    /// are throttled to ~30/s on the client, since each hover triggers a KdTree pick. (Mouse,
    /// not pointer events: the render control registers its own pointer handlers.)
    let private panelEvent (name : string) (throttle : bool) (preventDefault : bool) (extra : list<string>)
                           (f : list<string> -> V2d -> Option<ImageInspectorAction>) =
        name, AttributeValue.Event {
            clientSide = fun send src ->
                let sendIt =
                    String.concat ";" [
                        "var rect = this.getBoundingClientRect()"
                        // toFixed: FsPickler reads a V2d component only from a float literal
                        send src ([ "{ X: ((event.clientX - rect.left) / rect.width).toFixed(10), Y: ((event.clientY - rect.top) / rect.height).toFixed(10) }" ] @ extra)
                    ]
                let prevent = if preventDefault then "event.preventDefault();" else ""
                if throttle then
                    sprintf "%s var now = performance.now(); if (event.buttons !== 0 || !this.__iiT || now - this.__iiT > 33) { this.__iiT = now; %s }" prevent sendIt
                else prevent + sendIt
            serverSide = fun _ _ args ->
                match args with
                | rel :: rest ->
                    let r : V2d = Pickler.json.UnPickleOfString rel
                    match f rest (V2d(2.0 * r.X - 1.0, 1.0 - 2.0 * r.Y)) with
                    | Some a -> Seq.singleton (ImageInspectorMessage a)
                    | None -> Seq.empty
                | _ -> Seq.empty
        }

    let private parseFloat (s : string) =
        match Double.TryParse(s.Trim('"'), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | _ -> None

    let private imageSg (m : AdaptiveModel) : ISg<ViewerAction> =
        let list = m.scene.gisApp.projectedImageList
        let img = PRo3D.GIS.ProjectedImagesListAppHelper.getSelectedImage list
        let texture =
            Visualization.createProjectedTexture
                (PRo3D.GIS.ProjectedImagesListAppHelper.getSelectedTexture list)
                (PRo3D.GIS.ProjectedImagesListAppHelper.getSelectedImageChannel list)
        let extract' (d : aval<'a>) (f : AdaptiveProjectedImageModel -> aval<'a>) =
            img |> AVal.bind (function None -> d | Some i -> f i)
        let extract (d : 'a) f = extract' (AVal.constant d) f
        let colormap =
            extract' DefaultTextures.checkerboard (fun i ->
                i.colorMap |> AVal.map (ColorMap.getColorMapFileName >> PRo3D.InstrumentVisualization.InstrumentImageVisualization.getColorMapTexture))
        // same transfer function as the "Selected Image" preview (ProjectedImageApp); the
        // quad spans projector NDC [-1,1]^2 and the camera (zoom/pan) picks the visible part
        Sg.fullScreenQuad
        |> Sg.noEvents
        |> Sg.texture "InstrumentImage" texture
        |> Sg.texture "ColormapTexture" colormap
        |> Sg.uniform "MinValue" (extract 0.0 (fun i -> i.falseColorModel.lowerBound.value))
        |> Sg.uniform "MaxValue" (extract 1.0 (fun i -> i.falseColorModel.upperBound.value))
        |> Sg.uniform "UseFalseColor" (extract false (fun i -> i.falseColorPreview |> AVal.map not))
        |> Sg.uniform "DataType" (extract 2 (fun i -> i.dataType |> AVal.map int))
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! PRo3D.ImageMapping.Shaders.hshColors
        }

    /// Crosshair at a panel NDC, as an overlay in percent of the panel; None outside it.
    let private crosshair (colour : string) (panel : V2d) =
        if abs panel.X > 1.0 || abs panel.Y > 1.0 then None
        else
            let x = 50.0 * (panel.X + 1.0)
            let y = 50.0 * (1.0 - panel.Y)
            let line (s : string) = div [ style (sprintf "position:absolute; pointer-events:none; background:%s; %s" colour s) ] []
            Some (
                div [ style "position:absolute; inset:0; pointer-events:none" ] [
                    line (sprintf "left:%.4f%%; top:0; bottom:0; width:1px" x)
                    line (sprintf "top:%.4f%%; left:0; right:0; height:1px" y)
                ])

    /// A marker dot at a panel NDC; None outside the panel.
    let private dot (colour : string) (panel : V2d) =
        if abs panel.X > 1.0 || abs panel.Y > 1.0 then None
        else
            Some (
                div [ style (sprintf "position:absolute; pointer-events:none; left:calc(%.4f%% - 5px); top:calc(%.4f%% - 5px); width:10px; height:10px; border-radius:50%%; border:2px solid %s; box-sizing:border-box"
                                (50.0 * (panel.X + 1.0)) (50.0 * (1.0 - panel.Y)) colour) ] [])

    /// The sun line from `a` along `dir` (panel NDC), drawn far past the panel and clipped.
    let private sunLineSvg (a : V2d) (dir : V2d) =
        let b = a + dir * 8.0
        let px (p : V2d) = 50.0 * (p.X + 1.0)
        let py (p : V2d) = 50.0 * (1.0 - p.Y)
        Svg.svg [ attribute "viewBox" "0 0 100 100"; attribute "preserveAspectRatio" "none"
                  style "position:absolute; inset:0; width:100%; height:100%; pointer-events:none; overflow:hidden" ] [
            Svg.line [
                attribute "x1" (sprintf "%.5f" (px a)); attribute "y1" (sprintf "%.5f" (py a))
                attribute "x2" (sprintf "%.5f" (px b)); attribute "y2" (sprintf "%.5f" (py b))
                attribute "stroke" "#ffd400"; attribute "stroke-width" "1.5"; attribute "stroke-dasharray" "6 4"
                attribute "vector-effect" "non-scaling-stroke"
            ]
        ]

    let private fmt (v : float) = v.ToString("0.0", Globalization.CultureInfo.InvariantCulture)
    let private fmt2 (v : float) = v.ToString("0.00", Globalization.CultureInfo.InvariantCulture)

    let view (m : AdaptiveModel) : DomNode<ViewerAction> =
        // whole-model snapshots: they change rarely compared with the hover, and the projector
        // itself is memoized
        let context =
            (m.scene.gisApp.Current, m.scene.surfacesModel.Current, m.scene.referenceSystem.Current)
            |||> AVal.map3 tryContext

        // 3D -> 2D: the main view's surface hit in this image
        let fromMainView =
            (context, m.surfaceIntersection)
            ||> AVal.map2 (fun ctx hit ->
                match ctx, hit with
                | Ok ctx, Some h -> project ctx h.hitPoint
                | _ -> None)

        // the visible part of projector NDC: [center - 1/scale, center + 1/scale]
        let camera =
            m.imageView |> AVal.map (fun v ->
                let h = 1.0 / v.scale
                let box = Box3d(V3d(v.center.X - h, v.center.Y - h, 0.1), V3d(v.center.X + h, v.center.Y + h, 10.0))
                Camera.create (CameraView.lookAt V3d.OOI V3d.Zero V3d.OIO) (Frustum.ortho box))

        let attributes =
            AttributeMap.ofList [
                style "position:absolute; inset:0; width:100%; height:100%; cursor:crosshair"
                // a move with no button held ends a drag whose mouseup happened outside
                panelEvent "onmousemove" true false [ "event.buttons" ] (fun rest p ->
                    match rest with
                    | "0" :: _ -> Some (ImageInspectorAction.Hover (Some p))
                    | _ -> Some (ImageInspectorAction.DragMove p))
                panelEvent "onmousedown" false false [] (fun _ p -> Some (ImageInspectorAction.DragStart p))
                panelEvent "onmouseup" false false [] (fun _ _ -> Some ImageInspectorAction.DragEnd)
                panelEvent "ondblclick" false false [] (fun _ _ -> Some ImageInspectorAction.ResetView)
                panelEvent "onclick" false false [] (fun _ p -> Some (ImageInspectorAction.Click p))
                // browser deltaY: about 100 per notch, positive when scrolling down (= zoom out)
                panelEvent "onwheel" false true [ "event.deltaY" ] (fun rest p ->
                    match rest with
                    | d :: _ -> parseFloat d |> Option.map (fun dy -> ImageInspectorAction.Zoom(-dy / 200.0, p))
                    | [] -> None)
                "onmouseleave", AttributeValue.Event {
                    clientSide = fun send src -> send src []
                    serverSide = fun _ _ _ -> Seq.singleton (ImageInspectorMessage (ImageInspectorAction.Hover None))
                }
            ]

        // the panel extent: render control and overlays share it, so overlay percentages
        // and pointer fractions are both panel fractions. Only attributes and overlays
        // are incremental -- the render control itself is never rebuilt.
        let extentAttributes =
            amap {
                let! ctx = context
                let aspect =
                    match ctx with
                    | Ok { size = Some s } -> sprintf "%d / %d" s.X s.Y
                    | _ -> "1 / 1"
                yield style (sprintf "position:relative; aspect-ratio:%s; max-width:100%%; max-height:100%%; height:100%%" aspect)
            } |> AttributeMap.ofAMap

        let overlays =
            alist {
                let! view = m.imageView
                let! main = fromMainView
                match main |> Option.bind (ImageView.toPanel view >> crosshair "#00e5ff") with
                | Some c -> yield c
                | None -> ()
                let! hover = m.imageHover
                match hover |> Option.bind (fun h -> h.ndc |> ImageView.toPanel view |> crosshair "rgba(255,255,0,0.6)") with
                | Some c -> yield c
                | None -> ()
                // measurement: rim, sun line, snapped tip
                let! ctx = context
                let! s = m.shadowMeasure
                match ctx, s.rim with
                | Ok ctx, Some (rimNdc, rim) ->
                    match ctx.sun |> Result.toOption |> Option.bind (fun sun -> sunLine ctx sun rim) with
                    | Some (a, dir) ->
                        let pa = ImageView.toPanel view a
                        let pb = ImageView.toPanel view (a + dir * 0.01)
                        yield sunLineSvg pa (Vec.normalize (pb - pa))
                    | None -> ()
                    match dot "#ff8c00" (ImageView.toPanel view rimNdc) with
                    | Some d -> yield d
                    | None -> ()
                    match s.tip |> Option.bind (ImageView.toPanel view >> dot "#ff00ff") with
                    | Some d -> yield d
                    | None -> ()
                | _ -> ()
            }

        // The bar is two incremental blocks on purpose: the text follows every hover, the
        // controls only the measurement and the zoom. Rebuilding a button on hover replaces it
        // between mousedown and mouseup -- moving onto it changes the hover -- and the click
        // is lost.
        let readout =
            alist {
                let! ctx = context
                let! hover = m.imageHover
                let! s = m.shadowMeasure
                let line =
                    match ctx, hover with
                    | Result.Error e, _ ->
                        sprintf "Image Inspector: %s. Select an image in GIS View -> projected images." e
                    | Ok _, None ->
                        "hover: yellow = pointer, its surface hit is marked in 3D; cyan = the 3D cursor seen from this image. Wheel zooms, drag pans, double-click resets."
                    | Ok _, Some h ->
                        let px =
                            match h.pixel with
                            | Some p -> sprintf "pixel (%s, %s)" (fmt p.X) (fmt p.Y)
                            | None -> sprintf "ndc (%.4f, %.4f)" h.ndc.X h.ndc.Y
                        match h.hit with
                        | Some _ -> px + " -> surface hit"
                        | None -> px + " -> no surface hit"
                let line =
                    if not s.active then line
                    else
                        match s.rim, s.result with
                        | _, Some (Result.Error e) -> "measure: " + e
                        | None, _ -> "measure: click the crater rim where the shadow starts"
                        | Some _, None -> "measure: click the shadow tip (snaps to the dashed sun line)"
                        | Some _, Some (Ok r) ->
                            let perPx = r.depthPerPixel |> Option.map (fun d -> sprintf " (±%s m/px)" (fmt2 d)) |> Option.defaultValue ""
                            let mesh =
                                r.meshTip |> Option.map (fun mt -> sprintf ", mesh %s m off" (fmt2 (Vec.distance mt r.tip))) |> Option.defaultValue ""
                            let warn = if r.phase < lowPhase then " -- LOW PHASE: sun and camera rays nearly parallel, unreliable" else ""
                            sprintf "depth %s m%s, shadow %s m, sun %s° up, phase %s°%s%s" (fmt2 r.depth) perPx (fmt2 r.length) (fmt r.sunElevation) (fmt r.phase) mesh warn
                yield div [ style "overflow:hidden; text-overflow:ellipsis" ] [ text line ]
            }

        let controls =
            alist {
                let! s = m.shadowMeasure
                let! view = m.imageView
                if view.scale > 1.0 then
                    yield div [ style "white-space:nowrap; margin-left:8px" ] [ text (sprintf "zoom %sx" (fmt view.scale)) ]
                    yield button [ clazz "ui mini inverted basic button"; style "margin-left:8px"; onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ResetView) ] [ text "Reset" ]
                let measureButton = if s.active then "ui mini inverted button active" else "ui mini inverted basic button"
                yield button [ clazz measureButton; style "margin-left:8px"; onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ToggleMeasure) ] [ text "Measure shadow" ]
                if s.active && s.rim.IsSome then
                    yield button [ clazz "ui mini inverted basic button"; onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ClearMeasure) ] [ text "Clear" ]
                match s.result with
                | Some (Ok _) ->
                    yield button [ clazz "ui mini inverted basic button"; onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.CreateScaleBar) ] [ text "Create scale bar" ]
                | _ -> ()
            }

        div [ style "position:relative; width:100%; height:100%; display:flex; flex-direction:column; color:#ddd; font-size:12px" ] [
            div [ style "flex:1; min-height:0; display:flex; align-items:center; justify-content:center; overflow:hidden" ] [
                Incremental.div extentAttributes (
                    alist {
                        yield DomNode.RenderControl(attributes, camera, imageSg m, RenderControlConfig.standard)
                        yield Incremental.div (AttributeMap.ofList [ style "position:absolute; inset:0; pointer-events:none; overflow:hidden" ]) overlays
                    })
            ]
            // fixed-height bar: showing zoom/Reset must not resize the image above it, which
            // would move the image under a resting pointer
            div [ style "flex:none; height:32px; box-sizing:border-box; padding:0 8px; border-top:1px solid #333; display:flex; align-items:center; overflow:hidden; white-space:nowrap" ] [
                Incremental.div (AttributeMap.ofList [ style "flex:1; min-width:0; overflow:hidden" ]) readout
                Incremental.div (AttributeMap.ofList [ style "flex:none; display:flex; align-items:center" ]) controls
            ]
        ]
