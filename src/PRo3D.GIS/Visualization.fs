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

    /// One band of an image as a PixImage, for a stack texture-array slice.
    let private decodeBand (path : string) (channel : int) : Option<PixImage> =
        try
            match Path.GetExtension(path).ToLower() with
            | ".tiff" | ".tif" ->
                match MultiBandReader.tryReadMultiBandTiff path false with
                | Result.Ok img ->
                    InstrumentImageTextures.instrumentImageToTexture false img
                    |> Array.tryItem channel
                    |> Option.map (fun band -> band.pi)
                | _ ->
                    Log.warn "[ProjectedStack] could not read %s" path
                    None
            | ".exr" ->
                use stream = File.OpenRead path
                let mm = TextureLoading.loadImageFromStream stream (ChannelReference.ChannelWithIndex channel) (Some TextureLoading.TextureFormat.OpenEXR)
                mm.ImageArray |> Array.tryHead
            | ".png" | ".jpg" | ".jpeg" ->
                PixImage.Load path |> Some
            | ext ->
                Log.warn "[ProjectedStack] unsupported image format %s (%s)" ext path
                None
        with e ->
            Log.warn "[ProjectedStack] could not decode %s: %s" path e.Message
            None

    /// The stack texture array: slice i carries layer i's image band, aligned
    /// with getProjectedImageData's stackProjections. Decoded bands are cached
    /// per (path, channel) (D7); the GPU array is reallocated only when the
    /// size, format or layer count grows past the current allocation --
    /// reordering the stack is a re-upload, not a realloc.
    let createProjectedStackTextureArray (runtime : ITextureRuntime) (layers : aval<array<string * int>>) : aval<ITexture> =
        let decodeCache = System.Collections.Generic.Dictionary<string * int, Option<PixImage>>()
        let decodeCached (path : string) (channel : int) =
            match decodeCache.TryGetValue ((path, channel)) with
            | true, pi -> pi
            | _ ->
                // bound the decode memory: 96 entries of AFC-sized bands ~ 400 MB
                // worst case; a full clear is crude but correct, misses just decode again
                if decodeCache.Count > 96 then decodeCache.Clear()
                let pi = decodeBand path channel
                decodeCache.[(path, channel)] <- pi
                pi

        // the sampler needs *some* 2d-array bound even with an empty stack
        let empty =
            lazy (
                let t = runtime.CreateTexture2DArray(V2i.II, TextureFormat.Rgba8, levels = 1, samples = 1, count = 1)
                t.Upload(PixImage<byte>(Col.Format.RGBA, V2i.II), slice = 0)
                t
            )

        let mutable allocated : Option<IBackendTexture * V2i * TextureFormat * int> = None
        let mutable uploaded : array<string * int> = [||]

        AVal.custom (fun t ->
          try
            let ls = layers.GetValue t
            if ls.Length = 0 then
                empty.Value :> ITexture
            else
                let decoded = ls |> Array.map (fun (p, c) -> decodeCached p c)
                match decoded |> Array.tryPick id with
                | None -> empty.Value :> ITexture
                | Some first ->
                    let size = first.Size
                    let format = TextureFormat.ofPixFormat first.PixFormat TextureParams.None
                    let tex =
                        match allocated with
                        | Some (tex, s, f, capacity) when s = size && f = format && capacity >= ls.Length -> tex
                        | current ->
                            current |> Option.iter (fun (old, _, _, _) -> runtime.DeleteTexture old)
                            let tex = runtime.CreateTexture2DArray(size, format, levels = 1, samples = 1, count = max ls.Length 1)
                            allocated <- Some (tex, size, format, max ls.Length 1)
                            uploaded <- [||]
                            tex
                    // a fresh allocation's content is undefined; slices without a
                    // usable image must still be written, else they show garbage
                    let black =
                        lazy (PixImage<byte>(Col.Format.Gray, size) :> PixImage)
                    for i in 0 .. ls.Length - 1 do
                        let unchanged = i < uploaded.Length && uploaded.[i] = ls.[i]
                        if not unchanged then
                            match decoded.[i] with
                            | Some pi when pi.Size = size ->
                                tex.Upload(pi, slice = i)
                            | Some pi ->
                                // same-instrument stacks share one size (D1); a
                                // mismatched image keeps its slot but stays dark
                                Log.warn "[ProjectedStack] %s is %A, expected %A -- slice %d stays black" (fst ls.[i]) pi.Size size i
                                tex.Upload(black.Value, slice = i)
                            | None ->
                                tex.Upload(black.Value, slice = i)
                    uploaded <- ls
                    tex :> ITexture
          with e ->
            // a failure here must not take the surface's render objects down
            // with it -- log loudly and fall back to the (lazily created)
            // 1-slice dummy so the sampler still has an array bound
            Log.error "[ProjectedStack] building the stack texture array failed: %A" e
            empty.Value :> ITexture
        )

    /// Frustum wireframe of the hovered image's projector (D5): the NDC cube
    /// corners through the inverse view*proj, in the surface's frame; the
    /// caller supplies the surface's current placement. The "far" rectangle is
    /// cut at the TARGET distance rather than the far plane: nearFarForDistance
    /// spans two orders of magnitude either side of the target, whose NDC depth
    /// is therefore always ~0.98 -- the far plane itself would draw a box 100x
    /// the standoff.
    let hoveredFrustumSg (hoveredProjection : aval<Option<Trafo3d>>) (surfaceTrafo : aval<Trafo3d>) : ISg =
        let lines =
            hoveredProjection |> AVal.map (function
                | None -> [||]
                | Some full ->
                    let inv = full.Backward
                    let c (x : float) (y : float) (z : float) = inv.TransformPosProj(V3d(x, y, z))
                    let zN = -1.0
                    let zT = 0.98
                    let corners z = [| c -1.0 -1.0 z; c 1.0 -1.0 z; c 1.0 1.0 z; c -1.0 1.0 z |]
                    let n = corners zN
                    let f = corners zT
                    [|
                        for i in 0 .. 3 do
                            yield Line3d(n.[i], n.[(i + 1) % 4])
                            yield Line3d(f.[i], f.[(i + 1) % 4])
                            yield Line3d(n.[i], f.[i])
                    |]
            )
        Sg.lines (AVal.constant C4b.Green) lines
        |> Sg.shader {
            do! DefaultSurfaces.stableTrafo
            do! DefaultSurfaces.thickLine
        }
        |> Sg.uniform' "LineWidth" 3.0
        |> Sg.trafo surfaceTrafo

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