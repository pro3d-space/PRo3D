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
/// The panel draws the image with screen NDC = projector NDC (a full-screen quad sampled at
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
        }

    // projectDirect calls SPICE behind a global lock; hovering must not pay for it per
    // mouse move. Keyed like ProjectedImagesListHelpers' projector cache.
    let private projectorCache =
        System.Collections.Concurrent.ConcurrentDictionary<Guid * ProjectionMethod * (float * float * float) * string * string, Option<Trafo3d>>()

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
                Ok { imageId = image.id; full = full; toWorld = placement * surface.preTransform; size = size }

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

    /// Pointer events carrying the position relative to the element and its size; throttled
    /// to ~30/s on the client, since each move triggers a KdTree pick. (Mouse, not pointer
    /// events: the render control registers its own pointer handlers.)
    let private ndcEvent (name : string) (throttle : bool) (f : Option<V2d> -> ViewerAction) =
        name, AttributeValue.Event {
            clientSide = fun send src ->
                let sendIt =
                    String.concat ";" [
                        "var rect = this.getBoundingClientRect()"
                        send src [ "{ X: ((event.clientX - rect.left) / rect.width).toFixed(10), Y: ((event.clientY - rect.top) / rect.height).toFixed(10) }" ]
                    ]
                if throttle then
                    sprintf "var now = performance.now(); if (!this.__iiT || now - this.__iiT > 33) { this.__iiT = now; %s }" sendIt
                else sendIt
            serverSide = fun _ _ args ->
                match args with
                | rel :: _ ->
                    let r : V2d = Pickler.json.UnPickleOfString rel
                    Seq.singleton (f (Some (V2d(2.0 * r.X - 1.0, 1.0 - 2.0 * r.Y))))
                | _ -> Seq.empty
        }

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
        // same transfer function as the "Selected Image" preview (ProjectedImageApp)
        Sg.fullScreenQuad
        |> Sg.noEvents
        |> Sg.texture "InstrumentImage" texture
        |> Sg.texture "ColormapTexture" colormap
        |> Sg.uniform "MinValue" (extract 0.0 (fun i -> i.falseColorModel.lowerBound.value))
        |> Sg.uniform "MaxValue" (extract 1.0 (fun i -> i.falseColorModel.upperBound.value))
        |> Sg.uniform "UseFalseColor" (extract false (fun i -> i.falseColorPreview |> AVal.map not))
        |> Sg.uniform "DataType" (extract 2 (fun i -> i.dataType |> AVal.map int))
        |> Sg.shader { do! PRo3D.ImageMapping.Shaders.hshColors }

    /// Crosshair at a projector NDC, as an overlay in percent of the image extent.
    let private crosshair (colour : string) (ndc : V2d) =
        let x = 50.0 * (ndc.X + 1.0)
        let y = 50.0 * (1.0 - ndc.Y)
        let line (s : string) = div [ style (sprintf "position:absolute; pointer-events:none; background:%s; %s" colour s) ] []
        div [ style "position:absolute; inset:0; pointer-events:none" ] [
            line (sprintf "left:%.4f%%; top:0; bottom:0; width:1px" x)
            line (sprintf "top:%.4f%%; left:0; right:0; height:1px" y)
        ]

    let private fmt (v : float) = v.ToString("0.0", Globalization.CultureInfo.InvariantCulture)

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

        let camera =
            AVal.constant (Camera.create (CameraView.lookAt V3d.OOI V3d.Zero V3d.OIO) (Frustum.ortho (Box3d(-V3d.III, V3d.III))))

        let attributes =
            AttributeMap.ofList [
                style "position:absolute; inset:0; width:100%; height:100%"
                ndcEvent "onmousemove" true ImageInspectorHover
                "onmouseleave", AttributeValue.Event {
                    clientSide = fun send src -> send src []
                    serverSide = fun _ _ _ -> Seq.singleton (ImageInspectorHover None)
                }
            ]

        // the image extent: render control and overlays share it, so overlay percentages
        // and pointer fractions are both image fractions. Only attributes and overlays
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
                let! main = fromMainView
                match main with
                | Some ndc -> yield crosshair "#00e5ff" ndc
                | None -> ()
                let! hover = m.imageHover
                match hover with
                | Some h -> yield crosshair "rgba(255,255,0,0.6)" h.ndc
                | None -> ()
            }

        let readout =
            alist {
                let! ctx = context
                let! hover = m.imageHover
                let line =
                    match ctx, hover with
                    | Result.Error e, _ ->
                        sprintf "Image Inspector: %s. Select an image in GIS View -> projected images." e
                    | Ok _, None ->
                        "hover the image: yellow = pointer, its surface hit is marked in 3D; cyan = the 3D cursor seen from this image"
                    | Ok _, Some h ->
                        let px =
                            match h.pixel with
                            | Some p -> sprintf "pixel (%s, %s)" (fmt p.X) (fmt p.Y)
                            | None -> sprintf "ndc (%.4f, %.4f)" h.ndc.X h.ndc.Y
                        match h.hit with
                        | Some _ -> px + " -> surface hit"
                        | None -> px + " -> no surface hit"
                yield text line
            }

        div [ style "position:relative; width:100%; height:100%; display:flex; flex-direction:column; color:#ddd; font-size:12px" ] [
            div [ style "flex:1; min-height:0; display:flex; align-items:center; justify-content:center; overflow:hidden" ] [
                Incremental.div extentAttributes (
                    alist {
                        yield DomNode.RenderControl(attributes, camera, imageSg m, RenderControlConfig.standard)
                        yield Incremental.div (AttributeMap.ofList [ style "position:absolute; inset:0; pointer-events:none" ]) overlays
                    })
            ]
            Incremental.div (AttributeMap.ofList [ style "padding:4px 8px; border-top:1px solid #333" ]) readout
        ]
