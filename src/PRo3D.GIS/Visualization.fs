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
open Aardvark.FontProvider


open PRo3D.Extensions
open PRo3D.Extensions.FSharp
open PRo3D.SPICE
open PRo3D.Core
open PRo3D.Core.InstrumentMetadata
open Aardvark.PixImage.LibTiff
open PRo3D.InstrumentData
open PRo3D.InstrumentVisualization
open PRo3D.Core.Gis

type Self = Self

module Visualization =

    let createProjectedTexture (currentProjectedImage : aval<Option<string * ParsedMetadata>>) : aval<ITexture> =
        currentProjectedImage 
        |> AVal.bind (fun img -> 
            match img with
            | Some (img, (Some mbi, _)) -> 
                match MultiBandReader.tryReadMultiBandTiff img false with
                | Result.Ok img -> 
                    let images = InstrumentImageTextures.instrumentImageToTexture true img 
                    match Array.tryItem 0 images with
                    | Some img -> 
                        PixTexture2d(img.pi, false) :> ITexture |> AVal.constant
                    | _ -> 
                        Log.warn "channel of out of bounds"
                        DefaultTextures.checkerboard
                | _ -> 
                    Log.warn "could not load texture"
                    DefaultTextures.checkerboard
            | _ -> 
                DefaultTextures.checkerboard
        )


    let projectDirect (observer : string) (referenceFrame : string) (metadata : ParsedMetadata) 
                (targetBody : string) (boresight : Option<Trafo3d> ): Option<Trafo3d> =

        let farPlaneMars = 30101626.50 * 1000.0
        let instruments =
            let frustum = Frustum.perspective 5.5306897076421 1000.0 farPlaneMars 1.0
            let hsh = Frustum.perspective 15.23999 1000.0 farPlaneMars (217.0 / 409.0)
            let hsh2 = Frustum.perspective 15.23999  1000.0 farPlaneMars (409.0 / 217.0)
            let hsh3 = Frustum.perspective 9.9 1000.0 farPlaneMars (217.0 / 409.0)
            Map.ofList [
                "HERA_AFC-1", frustum
                "HERA_AFC-2", frustum
                "HERA_HSH", hsh2
            ]
        match metadata with
        | Some mbi, _ -> 
            match InstrumentProjection.instrument2SpiceName mbi.instrument with
            | None -> 
                Log.warn "could not get instrument spice name"
                None
            | Some spiceName -> 
                let p = {
                        instrumentReferenceFrame = spiceName
                        target = InstrumentImages.FocusBody targetBody
                        cameraSource = InstrumentImages.CameraSource.InBody "HERA"
                        instrumentName = spiceName
                        supportBody = "SUN"
                        time = mbi.obs_date
                        boresightAdjustment =boresight
                    }
                let t = InstrumentProjection.projectOntoQuat referenceFrame observer instruments p (-mbi.targetPos * 1000.0) mbi.sc_quat
                let spice = InstrumentProjection.projectOnto referenceFrame observer instruments p
                spice
        | _  -> 
            None

    let project (observer : string) (referenceFrame : string) (currentProjectedImage : Option<string * ParsedMetadata>) 
                (projection : InstrumentProjection) : Option<Trafo3d> =

        let farPlaneMars = 30101626.50 * 1000.0
        let instruments =
            let frustum = Frustum.perspective 5.5306897076421 1000.0 farPlaneMars 1.0
            let hsh = Frustum.perspective 15.23999 1000.0 farPlaneMars (217.0 / 409.0)
            let hsh2 = Frustum.perspective 15.23999  1000.0 farPlaneMars (409.0 / 217.0)
            let hsh3 = Frustum.perspective 9.9 1000.0 farPlaneMars (217.0 / 409.0)
            Map.ofList [
                "HERA_AFC-1", frustum
                "HERA_AFC-2", frustum
                "HERA_HSH", hsh2
            ]
        match currentProjectedImage with
        | Some (_, (Some mbi,_)) -> 
            let p = {
                projection with
                    time = mbi.obs_date
                }
            let t = InstrumentProjection.projectOntoQuat referenceFrame observer instruments p (-mbi.targetPos * 1000.0) mbi.sc_quat
            let spice = InstrumentProjection.projectOnto referenceFrame observer instruments p
            spice
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