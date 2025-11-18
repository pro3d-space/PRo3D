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
        g.projectedImageList |> getSelectedTexture |> Visualization.createProjectedTexture 

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
                    let time = o.time
                    //let (FrameSpiceName referenceFrame) = o.referenceFrame
                    let surfaceReferenceFrame = surfaceReferenceSystem |> AVal.map (function None -> "J2000" | Some v -> v.referenceFrame.Value)
                    // pull dependencies
                    let borsight = boresightAdjustment.GetValue(t)
                    let img = currentProjectedImage.GetValue(t)
                    let p = 
                        boresightAdjustment 
                        |> AVal.map (fun boresight -> 
                            {
                                target = InstrumentImages.CameraFocus.FocusBody "MARS"
                                cameraSource =  InstrumentImages.CameraSource.InBody "HERA"
                                instrumentReferenceFrame = "HERA_AFC-1"
                                instrumentName = "HERA_AFC-1"
                                supportBody = "SUN"
                                time = DateTime.Now
                                boresightAdjustment = Some boresight
                            } 
                        )
                    let r = Visualization.creatProjectionFunction (AVal.constant observer) surfaceReferenceFrame currentProjectedImage p
                    let result = r projectionSurfaceBodyName
                    result.GetValue(t)
                    
            )
        
        Some { 
                imageProjection = imageTrafo
                localImageProjectionTrafos = AVal.constant [||]
                sunDirection = sunDirection
                sunLightEnabled = sunDirection |> AVal.map Option.isSome
            }