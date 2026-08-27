namespace PRo3D.GIS


open System
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.Core
open PRo3D.ImageMapping
open PRo3D.Core.Gis
open PRo3D.InstrumentProjection

open PRo3D.Base.Gis
open PRo3D.SPICE

open PRo3D.InstrumentVisualization

module ProjectedImagesListAppHelper =

    /// Look an image up by its stable id. `id` is NonAdaptive on the adaptive
    /// model, so this stays a single AVal.map over the list content.
    let tryFindById (id : Guid) (m : AdaptiveProjectedImageListModel) =
        m.images.Content
        |> AVal.map (IndexList.tryFind (fun _ (img : AdaptiveProjectedImageModel) -> img.id = id))

    let getSelectedImage (m : AdaptiveProjectedImageListModel) =
        adaptive {
            match! m.selectedImage with
            | None -> return None
            | Some id -> return! tryFindById id m
        }

    let getSelectedImageChannel (m : AdaptiveProjectedImageListModel) =
        getSelectedImage m
        |> AVal.bind (function
            | None -> AVal.constant { idx = 0; name = None }
            | Some img -> img.selectedChannel
        )

    let getSelectedTexture (m : AdaptiveProjectedImageListModel) : aval<Option<string * InstrumentMetadata.ParsedMetadata>> = 
        adaptive {
            match! getSelectedImage m with
            | None -> return None
            | Some img -> 
                let metaData = 
                    img.texture |> AVal.map InstrumentMetadata.tryParseMetadataForImagePath 
                let! t = img.texture
                let! m = metaData
                return Some (t, m)
        }

    let getProjectedTexture (g : AdaptiveGisApp) : aval<ITexture> =
        g.projectedImageList |> getSelectedTexture |> fun md -> Visualization.createProjectedTexture md (getSelectedImageChannel g.projectedImageList)

    let getProjectionVisualizationProperties (g : AdaptiveGisApp) =
        let selectedImage = getSelectedImage g.projectedImageList
        { 
            VisualizationProperties.empty with 
                projectionOpacity = g.projectedImageList.projectionOpacity.value
                visualizationRange = 
                    selectedImage |> AVal.bind (function 
                        | None -> Range1d.Unit |> AVal.constant
                        | Some img -> (img.falseColorModel.lowerBound.value, img.falseColorModel.upperBound.value) ||> AVal.map2 (fun min max -> Range1d(min, max))
                    )
                colorMapping = 
                    selectedImage |> AVal.bind (function
                        | None -> AVal.constant None
                        | Some img -> 
                            img.colorMap |> AVal.map (Some << InstrumentImageVisualization.getColorMapTexture << ColorMap.getColorMapFileName)
                    )
                dataType = 
                    selectedImage |> AVal.bind (function 
                        | None -> AVal.constant DataType.Float
                        | Some img -> img.dataType
                    )
        }

    /// The (image file, channel) feeding each slice of the stack texture array,
    /// bottom -> top. Surface-independent (textures do not depend on the
    /// projection frame), filtered exactly like getProjectedImageData's layer
    /// array -- stack ids without a library image are dropped in both -- so
    /// slice i always belongs to matrix i.
    let getStackTextureLayers (g : AdaptiveGisApp) : aval<array<string * int>> =
        g.projectedImageList.Current |> AVal.map (fun model ->
            ProjectedImageListModel.effectiveStack model
            |> IndexList.toArray
            |> Array.choose (fun id ->
                ProjectedImageListModel.tryFind id model
                |> Option.map (fun img -> img.texture, img.selectedChannel.idx)))

    /// `lightViewProj`: world -> sun-camera clip space for shadow mapping, produced by
    /// the viewer's shadow-map pass; None (also whenever the lighting mode is not
    /// SunShadow) keeps the per-patch shadow lookup disabled.
    let getProjectedImageData (g : AdaptiveGisApp) (lightViewProj : aval<Option<Trafo3d>>) (surfaceId : Guid) (projectionSurfaceBodyName : string) : Option<Sg.ProjectedImages> =
        let currentProjectedImage =  g.projectedImageList |> getSelectedTexture
        let selectedImage = g.projectedImageList |> getSelectedImage
        let observer = GisApp.getObserverSystemAdaptive g
        let sunDirection = Gis.GisApp.getSunDirection g surfaceId
        let surfaceReferenceSystem = Gis.GisApp.getSpiceReferenceSystemAdaptive g surfaceId
        let computeBoresight (b : AdaptiveBoresightAdjustment) : aval<Trafo3d> = 
            b.Current |> AVal.map (fun b -> 
                Trafo3d.RotationXInDegrees(b.yaw.value) * Trafo3d.RotationYInDegrees(b.pitch.value) * Trafo3d.RotationZInDegrees(b.roll.value)
            )
        let boresightAdjustment = computeBoresight g.projectedImageList.boresightAdjustment
        let imageTrafo = 
            AVal.custom (fun t -> 
                match observer.GetValue(t) with
                | None -> None
                | Some o -> 
                    let (EntitySpiceName observer) = o.body
                    let surfaceReferenceFrame = surfaceReferenceSystem |> AVal.map (function None -> "J2000" | Some v -> v.referenceFrame.Value)

                    let borsight = boresightAdjustment.GetValue(t)
                    let img = currentProjectedImage.GetValue(t)
                    let surfaceReferenceFrame = surfaceReferenceFrame.GetValue(t)
                    let projectionMethod = g.projectedImageList.projectionMethod.GetValue(t)

                    match img with
                    | Some (_, metadata) ->
                        Visualization.projectDirect observer surfaceReferenceFrame metadata projectionSurfaceBodyName (Some borsight) projectionMethod
                    | _ ->
                        None
            )

        let trafos =
            AVal.custom (fun t ->
                match observer.GetValue(t) with
                | None -> [||]
                | Some o ->
                    let m = g.projectedImageList.instrumentVisibility.GetValue(t)
                    match m with
                    |  PRo3D.ImageMapping.InstrumentVisibilityMode.RelativeCount ->
                        let (EntitySpiceName observer) = o.body
                        let surfaceReferenceFrame = surfaceReferenceSystem |> AVal.map (function None -> "J2000" | Some v -> v.referenceFrame.Value)

                        let borsight = boresightAdjustment.GetValue(t)
                        let surfaceReferenceFrame = surfaceReferenceFrame.GetValue(t)
                        let images = g.projectedImageList.images.Content.GetValue(t)
                        let projectionMethod = g.projectedImageList.projectionMethod.GetValue(t)

                        images
                        |> IndexList.toArray
                        |> Array.choose (fun img ->
                            let metaData = img.texture.GetValue(t) |>  InstrumentMetadata.tryParseMetadataForImagePath
                            Visualization.projectDirect observer surfaceReferenceFrame metaData projectionSurfaceBodyName (Some borsight) projectionMethod
                        )
                    | _ -> [||]
            )

        // The projection stack, bottom -> top (multi-image projection). Each
        // layer's projector is computed at that image's own observation time in
        // the surface's reference frame -- body-fixed, so the projection sticks
        // to the terrain regardless of the scene's current time.
        //
        // SPICE is single-threaded and each call takes a global lock (see
        // InstrumentProjection.spiceCallLock), so projectors are memoized per
        // (image, method, boresight, observer, frame) -- reordering or hovering
        // recomputes nothing, only genuinely new configurations pay for SPICE
        // (D8 in plans/multiImageProjection.md). Metadata parses are memoized
        // by texture path (sidecars do not change during a session).
        let stackProjections =
            let metadataCache = System.Collections.Generic.Dictionary<string, InstrumentMetadata.ParsedMetadata>()
            let projectorCache = System.Collections.Generic.Dictionary<Guid * PRo3D.ImageMapping.ProjectionMethod * (float * float * float) * string * string, Option<Trafo3d>>()
            // created ONCE, outside the evaluation: building a fresh AVal.map
            // inside AVal.custom and pulling it with the token adds a new
            // out-of-date dependency on every pass -- the custom never settles
            // and dependent patch uniforms never complete (surfaces vanish)
            let surfaceReferenceFrameA =
                surfaceReferenceSystem |> AVal.map (function None -> "J2000" | Some v -> v.referenceFrame.Value)
            AVal.custom (fun t ->
                match observer.GetValue(t) with
                | None -> [||]
                | Some o ->
                    let (EntitySpiceName observer) = o.body
                    let surfaceReferenceFrame = surfaceReferenceFrameA.GetValue(t)
                    let boresightTrafo = boresightAdjustment.GetValue(t)
                    let model = g.projectedImageList.Current.GetValue(t)
                    let projectionMethod = model.projectionMethod
                    let boresightKey =
                        (model.boresightAdjustment.roll.value,
                         model.boresightAdjustment.pitch.value,
                         model.boresightAdjustment.yaw.value)

                    // a boresight slider drag creates a key per step; keep the
                    // cache bounded rather than evicting cleverly
                    if projectorCache.Count > 512 then projectorCache.Clear()

                    // one layer per stack entry that exists in the library --
                    // even when its projector fails to resolve, so the indices
                    // stay aligned with the stack texture array's slices (which
                    // are built from the same filtered stack, see
                    // getStackTextureLayers)
                    ProjectedImageListModel.effectiveStack model
                    |> IndexList.toArray
                    |> Array.choose (fun id ->
                        match ProjectedImageListModel.tryFind id model with
                        | None -> None
                        | Some img ->
                            let metadata =
                                match metadataCache.TryGetValue img.texture with
                                | true, md -> md
                                | _ ->
                                    let md = InstrumentMetadata.tryParseMetadataForImagePath img.texture
                                    metadataCache.[img.texture] <- md
                                    md
                            let key = (id, projectionMethod, boresightKey, observer, surfaceReferenceFrame)
                            let projector =
                                match projectorCache.TryGetValue key with
                                | true, p -> p
                                | _ ->
                                    let p = Visualization.projectDirect observer surfaceReferenceFrame metadata projectionSurfaceBodyName (Some boresightTrafo) projectionMethod
                                    projectorCache.[key] <- p
                                    p
                            let layer : Sg.ProjectedStackLayer =
                                {
                                    trafo = projector
                                    minMax = V2f(img.falseColorModel.lowerBound.value, img.falseColorModel.upperBound.value)
                                    texturePath = img.texture
                                    channel = img.selectedChannel.idx
                                }
                            Some layer
                    )
            )

        Some {
                imageProjection = imageTrafo
                localImageProjectionTrafos = trafos
                stackProjections = stackProjections
                sunDirection = sunDirection
                sunLightEnabled =
                    (g.projectedImageList.lightingMode, sunDirection) ||> AVal.map2 (fun l hasDir -> l <> PRo3D.ImageMapping.LightingMode.Off && Option.isSome hasDir)
                lightViewProj = lightViewProj
            }