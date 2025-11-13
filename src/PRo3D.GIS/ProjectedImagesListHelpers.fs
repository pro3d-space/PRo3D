namespace PRo3D.GIS

open System
open FSharp.Data.Adaptive

open Aardvark.Rendering

open PRo3D.Core
open PRo3D.ImageMapping
open PRo3D.Core.Gis
open PRo3D.InstrumentProjection

open PRo3D.Base.Gis
open PRo3D.SPICE

module ProjectedImagesListAppHelper =

    let getSelectedTexture (m : AdaptiveProjectedImageListModel) : aval<Option<string * InstrumentMetadata.ParsedMetadata>> = 
        adaptive {
            let! selected = m.selectedImage
            match selected with
            | None -> return None
            | Some idx -> 
                let! img = AList.tryGet idx m.images
                match img with
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

    let getProjectedImageData (g : AdaptiveGisApp) (surfaceId : Guid) (projectionSurfaceBodyName : string) : Option<Sg.ProjectedImages> =
        let currentProjectedImage =  g.projectedImageList |> getSelectedTexture
        let observer = GisApp.getObserverSystemAdaptive g
        let sunDirection = Gis.GisApp.getSunDirection g surfaceId
        let imageTrafo = 
            AVal.custom (fun t -> 
                match observer.GetValue(t) with
                | None -> None
                | Some o -> 
                    let (EntitySpiceName observer) = o.body
                    let time = o.time
                    let (FrameSpiceName referenceFrame) = o.referenceFrame
                    let p = 
                        {
                            target = InstrumentImages.CameraFocus.FocusBody "MARS"
                            cameraSource =  InstrumentImages.CameraSource.InBody "HERA"
                            instrumentReferenceFrame = "HERA_AFC-1"
                            instrumentName = "HERA_AFC-1"
                            supportBody = "SUN"
                            time = DateTime.Now
                            boresightAdjustment = None
                        } |> AVal.constant
                    currentProjectedImage.GetValue(t)
                    let r = Visualization.creatProjectionFunction (AVal.constant observer) (AVal.constant o.time) (AVal.constant referenceFrame) currentProjectedImage p
                    let result = r projectionSurfaceBodyName
                    result.GetValue(t)
                    
            )
        
        Some { 
                imageProjection = imageTrafo
                localImageProjectionTrafos = AVal.constant [||]
                sunDirection = sunDirection
                sunLightEnabled = sunDirection |> AVal.map Option.isSome
            }