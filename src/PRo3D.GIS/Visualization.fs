namespace PRo3D.InstrumentProjection

open System
open System.IO

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.SceneGraph
open Aardvark.Rendering.Text
open Aardvark.Geometry
open Aardvark.GeoSpatial.Opc

open PRo3D.Extensions
open PRo3D.Extensions.FSharp
open PRo3D.SPICE
open PRo3D.Core
open PRo3D.Core.InstrumentMetadata
open Aardvark.PixImage.LibTiff
open PRo3D.InstrumentData
open PRo3D.InstrumentVisualization
open PRo3D.Core.Gis
open PRo3D.ImageMapping

type Self = Self

module Visualization =

    let createProjectedExrTexture (path : string) (channel : int) : aval<ITexture> = 
        let stream = File.OpenRead path
        let exrTexture = TextureLoading.loadImageFromStream stream (ChannelReference.ChannelWithIndex channel) (Some TextureLoading.TextureFormat.OpenEXR)
        PixTexture2d(exrTexture, true) :> ITexture |> AVal.constant

    let createProjectedTiffTexture (path : string) (channel : int) : aval<ITexture> = 
        match MultiBandReader.tryReadMultiBandTiff path false with
        | Result.Ok img -> 
            let images = InstrumentImageTextures.instrumentImageToTexture true img 
            match Array.tryItem channel images with
            | Some img -> 
                PixTexture2d(img.pi, true) :> ITexture |> AVal.constant
            | _ -> 
                Log.warn "channel of out of bounds"
                DefaultTextures.checkerboard
        | _ -> 
            Log.warn "could not load texture"
            DefaultTextures.checkerboard

    /// Plain single-image formats (png/jpg, e.g. the synthetic HERA COP
    /// renders). No channel concept: PixImage loads the whole image and the
    /// projection shader samples the red channel, which for the grayscale
    /// content of instrument simulations is the image.
    let createProjectedPixTexture (path : string) : aval<ITexture> =
        try
            let pi = PixImage.Load path
            PixTexture2d(PixImageMipMap [| pi |], true) :> ITexture |> AVal.constant
        with e ->
            Log.warn "could not load texture %s: %s" path e.Message
            DefaultTextures.checkerboard

    let createProjectedTexture (currentProjectedImage : aval<Option<string * ParsedMetadata>>) (channel: aval<Channel>) : aval<ITexture> =
        AVal.bind2 (fun img  c ->
            match img with
            | Some (img : string, (Some mbi, _)) ->
                match Path.GetExtension(img).ToLower() with
                | ".tiff" | ".tif" -> createProjectedTiffTexture img c.idx
                | ".exr" -> createProjectedExrTexture img c.idx
                | ".png" | ".jpg" | ".jpeg" -> createProjectedPixTexture img
                | _ -> DefaultTextures.checkerboard
            | _ ->
                DefaultTextures.checkerboard
        ) currentProjectedImage channel

    /// `nearFar` overrides the depth range; when None it is derived from the observation
    /// distance in the mbi sidecar (InstrumentProjection.nearFarForDistance). The old
    /// hardcoded Mars-scale range clipped bodies the size of Didymos entirely.
    let projectDirectWithNearFar (nearFar : Option<float * float>)
                (observer : string) (referenceFrame : string) (metadata : ParsedMetadata)
                (targetBody : string) (boresight : Option<Trafo3d>) (method : ProjectionMethod) : Option<Trafo3d> =

        match metadata with
        | Some mbi, _ ->
            // targetPos is in km
            let distance = mbi.targetPos.Length * 1000.0
            let near, far = nearFar |> Option.defaultValue (InstrumentProjection.nearFarForDistance distance)
            let instruments = InstrumentProjection.instruments near far
            // The sidecar knows what it observed (TARGET header, e.g. "Didymos");
            // the caller's targetBody is only the fallback for older exports
            // that don't carry the header (legacy Mars data).
            let targetBody = mbi.target |> Option.defaultValue targetBody
            match InstrumentProjection.instrument2SpiceName mbi.instrument with
            | None ->
                Log.warn "could not get instrument spice name"
                None
            | Some spiceName ->
                let p = {
                        instrumentReferenceFrame = spiceName
                        target = InstrumentImages.FocusBody targetBody
                        cameraSource = InstrumentImages.CameraSource.InBody (InstrumentProjection.instrument2CameraSource mbi.instrument)
                        instrumentName = spiceName
                        supportBody = "SUN"
                        time = mbi.obs_date
                        boresightAdjustment =boresight
                    }
                let t = InstrumentProjection.projectOntoQuat referenceFrame observer instruments p (-mbi.targetPos * 1000.0) mbi.sc_quat
                let spice = InstrumentProjection.projectOnto referenceFrame observer instruments p
                match method with
                | ProjectionMethod.MbiBased -> t
                | _ -> spice
        | _  ->
            None

    let projectDirect (observer : string) (referenceFrame : string) (metadata : ParsedMetadata)
                (targetBody : string) (boresight : Option<Trafo3d>) (method : ProjectionMethod) : Option<Trafo3d> =
        projectDirectWithNearFar None observer referenceFrame metadata targetBody boresight method

    let project (observer : string) (referenceFrame : string) (currentProjectedImage : Option<string * ParsedMetadata>)
                (projection : InstrumentProjection) : Option<Trafo3d> =

        match currentProjectedImage with
        | Some (_, (Some mbi,_)) ->
            let distance = mbi.targetPos.Length * 1000.0
            let near, far = InstrumentProjection.nearFarForDistance distance
            let instruments = InstrumentProjection.instruments near far
            let p = {
                projection with
                    time = mbi.obs_date
                }
            InstrumentProjection.projectOnto referenceFrame observer instruments p
        | _  ->
            None

    let creatProjectionFunction (observer : aval<string>) (referenceFrame : aval<string>) 
                                (currentProjectedImage : aval<Option<string * ParsedMetadata>>) (instrumentProjection : aval<InstrumentProjection>) =

    
        let projectImage (targetPlanet : string) = 
            AVal.custom (fun t -> 
                let img = currentProjectedImage.GetValue t
                match img with
                | Some (_, (Some mbi,_)) -> 
                    let observer = observer.GetValue t
                    let referenceFrame = referenceFrame.GetValue t
                    let instrumentProjection = instrumentProjection.GetValue t
                    let p = {
                        instrumentProjection with
                            time = mbi.obs_date
                        }
                    project observer referenceFrame img p
                | _  -> 
                    None
            )

        projectImage

    //let createSceneGraph (projectedImageProperties : VisualizationProperties) (referenceFrame : aval<string>) (supportBody : aval<string>)
    //                     (observer : aval<string>) (time : aval<DateTime>) (projectImage : string -> aval<Option<Trafo3d>>) 
    //                     (projectedTexture : aval<ITexture>) (projectionEnabled : aval<bool>) =


    //    let marsProxy = 
    //        let marsTrafo = 
    //            Rendering.fullTrafo referenceFrame supportBody "MARS" (Some "IAU_MARS") observer time
    //            |> AVal.map (Option.defaultValue Trafo3d.Identity)

    //        let marsTexture = 
    //            let getImageStream () = 
    //                typeof<Self>.Assembly.GetManifestResourceStream("PRo3D.InstrumentProjection.resources.marswikiAnnotated.jpg")
    //            StreamTexture(getImageStream)

    //        let sphericalUnitBody (scale : float) = 
    //            PolyMeshPrimitives.Sphere(30, 1.0, C4b.White, DefaultSemantic.DiffuseColorCoordinates, DefaultSemantic.DiffuseColorUTangents, DefaultSemantic.DiffuseColorVTangents)
    //                                .GetIndexedGeometry()

    //            |> Sg.ofIndexedGeometry

    //        sphericalUnitBody 1.0
    //        |> Sg.diffuseTexture' marsTexture
    //        |> Sg.applyProjectedImage projectImage
    //        |> Sg.applyPlanet "mars"
    //        |> Sg.scale (3389.5f * 1000.0) // mars radius in km
    //        |> Sg.trafo marsTrafo
    //        |> Sg.shader {
    //            do! Shaders.genAndFlipTextureCoord
    //            do! ImageProjection.Shaders.useVertexNormals
    //            do! ImageProjection.Shaders.stableImageProjectionTrafo
    //            do! DefaultSurfaces.stableTrafo
    //            do! DefaultSurfaces.diffuseTexture
    //            do! DefaultSurfaces.stableHeadlight
    //            do! ImageProjection.Shaders.stableImageProjection
    //        }
    //        |> InstrumentImageVisualization.applyProperties { projectedImageProperties with instrumentImage = projectedTexture }
    //        |> Sg.uniform' "ProjectedImageModelViewProjValid" projectionEnabled
    //        |> Sg.texture "ProjectedTexture" projectedTexture

    //    marsProxy