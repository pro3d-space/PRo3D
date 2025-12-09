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

    let getSelectedImage (m : AdaptiveProjectedImageListModel) =
        adaptive {
            let! selected = m.selectedImage
            match selected with
            | None -> return None
            | Some idx -> 
                let! img = AList.tryGet idx m.images
                match img with
                | None -> return None
                | Some img -> 
                    return Some img
        }

    let getSelectedImageChannel (m : AdaptiveProjectedImageListModel) =
        m.selectedImage |> AVal.map (fun imgIdx ->
            match imgIdx with
            | None -> {idx = 0; name = None}
            | Some idx -> 
                let img = AList.tryGet idx m.images |> AVal.map (fun img ->
                    match img with
                    | None -> { idx = 0; name = None}
                    | Some img -> img.selectedChannel.GetValue()
                )
                img.GetValue()
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
                        | Some img -> (img.inputMinValue.value, img.inputMaxValue.value) ||> AVal.map2 (fun min max -> Range1d(min, max))
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

    let getProjectedImageData (g : AdaptiveGisApp)  (surfaceId : Guid) (projectionSurfaceBodyName : string) : Option<Sg.ProjectedImages> =
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

                    match img with 
                    | Some (_, metadata) -> 
                        Visualization.projectDirect observer surfaceReferenceFrame metadata projectionSurfaceBodyName (Some borsight)
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

                        images 
                        |> IndexList.toArray
                        |> Array.choose (fun img -> 
                            let metaData = img.texture.GetValue(t) |>  InstrumentMetadata.tryParseMetadataForImagePath 
                            Visualization.projectDirect observer surfaceReferenceFrame metaData projectionSurfaceBodyName (Some borsight)
                        )
                    | _ -> [||]
            )
        
        Some { 
                imageProjection = imageTrafo
                localImageProjectionTrafos = trafos
                sunDirection = sunDirection
                sunLightEnabled = 
                    (g.projectedImageList.lightingMode, sunDirection) ||> AVal.map2 (fun l hasDir -> l <> PRo3D.ImageMapping.LightingMode.Off && Option.isSome hasDir)
            }