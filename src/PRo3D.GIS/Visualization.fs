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

    /// The stack texture array plus, per layer, the slice it reads.
    /// `slices.[i]` is the slot of layer i (ProjectedStackSlices in the stack shader).
    type StackTextureArray =
        {
            texture : ITexture
            slices  : array<int>
        }

    /// The stack texture array, one slot per image (path, band) -- not per stack
    /// position: an image keeps its slot while it is in the stack, and the layer
    /// order reaches the shader as a slot index per layer (`slices`). Reordering
    /// the stack, and its live drag preview, therefore upload nothing; only an
    /// image new to the array is uploaded, into a free slot. A slot freed by an
    /// image leaving the stack keeps its content until it is needed, so an image
    /// that comes back (a library image hovered again) is not uploaded again.
    ///
    /// Decoded bands are cached per (path, channel) (D7). The GPU array is
    /// reallocated -- and everything re-uploaded -- only when the image size or
    /// format changes or more slots are needed than allocated; it grows by
    /// doubling, up to ProjectedImages.maxCount.
    let createProjectedStackTextureArray (runtime : ITextureRuntime) (layers : aval<array<string * int>>) : aval<StackTextureArray> =
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
        let emptyResult () = { texture = empty.Value :> ITexture; slices = [||] }

        let mutable allocated : Option<IBackendTexture * V2i * TextureFormat * int> = None
        // slot -> the image it holds, and back
        let mutable content : array<Option<string * int>> = [||]
        let slotOf = System.Collections.Generic.Dictionary<string * int, int>()

        AVal.custom (fun t ->
          try
            let ls = layers.GetValue t
            if ls.Length = 0 then emptyResult ()
            else
                let keys = ls |> Array.distinct
                let decoded = keys |> Array.map (fun (p, c) -> decodeCached p c)
                match decoded |> Array.tryPick id with
                | None -> emptyResult ()
                | Some first ->
                    let size = first.Size
                    let format = TextureFormat.ofPixFormat first.PixFormat TextureParams.None
                    let tex =
                        match allocated with
                        | Some (tex, s, f, capacity) when s = size && f = format && capacity >= keys.Length -> tex
                        | current ->
                            current |> Option.iter (fun (old, _, _, _) -> runtime.DeleteTexture old)
                            // gone now: the catch below must not delete it again
                            allocated <- None
                            // a few spare slots (hovered library image, the next +)
                            // rather than doubling: slices are full-resolution images
                            let capacity = min ProjectedImages.maxCount (keys.Length + 4)
                            let tex = runtime.CreateTexture2DArray(size, format, levels = 1, samples = 1, count = capacity)
                            allocated <- Some (tex, size, format, capacity)
                            content <- Array.create capacity None
                            slotOf.Clear()
                            tex
                    let needed = System.Collections.Generic.HashSet<string * int>(keys)
                    // free slots: never used first, then those holding an image no layer needs
                    let free =
                        System.Collections.Generic.Queue<int>(
                            Array.append
                                (Array.init content.Length id |> Array.filter (fun i -> content.[i].IsNone))
                                (Array.init content.Length id |> Array.filter (fun i ->
                                    match content.[i] with
                                    | Some k -> not (needed.Contains k)
                                    | None -> false)))
                    // a fresh allocation's content is undefined; slots without a
                    // usable image must still be written, else they show garbage
                    let black = lazy (PixImage<byte>(Col.Format.Gray, size) :> PixImage)
                    let sw = System.Diagnostics.Stopwatch.StartNew()
                    let mutable uploads = 0
                    for ki in 0 .. keys.Length - 1 do
                        let key = keys.[ki]
                        if not (slotOf.ContainsKey key) then
                            // capacity >= keys.Length, so a free slot exists
                            let slot = free.Dequeue()
                            content.[slot] |> Option.iter (fun old -> slotOf.Remove old |> ignore)
                            match decoded.[ki] with
                            | Some pi when pi.Size = size -> tex.Upload(pi, slice = slot)
                            | Some pi ->
                                // same-instrument stacks share one size (D1); a
                                // mismatched image keeps its slot but stays dark
                                Log.warn "[ProjectedStack] %s is %A, expected %A -- slot %d stays black" (fst key) pi.Size size slot
                                tex.Upload(black.Value, slice = slot)
                            | None -> tex.Upload(black.Value, slice = slot)
                            content.[slot] <- Some key
                            slotOf.[key] <- slot
                            uploads <- uploads + 1
                    if uploads > 0 then
                        Log.line "[ProjectedStack] uploaded %d image(s) (%A) for %d layers in %.1f ms" uploads size ls.Length sw.Elapsed.TotalMilliseconds
                    let slices =
                        ls |> Array.map (fun key ->
                            match slotOf.TryGetValue key with
                            | true, slot -> slot
                            | _ -> 0)
                    { texture = tex :> ITexture; slices = slices }
          with e ->
            // a failure here must not take the surface's render objects down
            // with it -- log loudly and fall back to the (lazily created)
            // 1-slice dummy so the sampler still has an array bound
            Log.error "[ProjectedStack] building the stack texture array failed: %A" e
            allocated |> Option.iter (fun (old, _, _, _) -> try runtime.DeleteTexture old with _ -> ())
            allocated <- None
            content <- [||]
            slotOf.Clear()
            emptyResult ()
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

    /// The projector pose an orientation source stands for.
    let projectorPose (method : ProjectionMethod) (mbi : Tiff_Mbi_Json.Mbi) : ProjectorPose =
        match method with
        | ProjectionMethod.Spice -> ProjectorPose.SpiceAimed
        | ProjectionMethod.MbiAttitudeSpiceRange -> ProjectorPose.SidecarAttitudeSpiceRange mbi.sc_quat
        | _ -> ProjectorPose.Sidecar (mbi.sc_quat, mbi.targetPos)

    /// Resolve an image's projector in the body-fixed frame `originFrame` of `originBody`,
    /// the body the surface's coordinates are relative to. The image's target is the
    /// sidecar's TARGET, falling back to `originBody` for sidecars without one.
    ///
    /// `instruments` are the scene's per-instrument field of view and row flip
    /// (Map.empty = the kernels' values). `nearFar` overrides
    /// the depth range; by default it follows the resolved distance to the target
    /// (InstrumentProjection.nearFarForDistance).
    let resolveProjectorWith (instruments : InstrumentProjection.Instruments) (nearFar : Option<float * float>)
                             (originBody : string) (originFrame : string)
                             (metadata : ParsedMetadata) (boresight : Option<Trafo3d>) (method : ProjectionMethod)
                             : Result<ResolvedProjector, string> =
        match metadata with
        | Some mbi, _ ->
            match InstrumentProjection.instrument2SpiceName mbi.instrument with
            | None -> Result.Error (sprintf "no SPICE frame known for instrument '%s'" mbi.instrument)
            | Some spiceName ->
                let setup = Map.tryFind spiceName instruments
                InstrumentProjection.resolve {
                    instrument  = spiceName
                    spacecraft  = InstrumentProjection.instrument2CameraSource mbi.instrument
                    target      = mbi.target |> Option.defaultValue originBody
                    origin      = originBody
                    originFrame = originFrame
                    time        = mbi.obs_date
                    pose        = projectorPose method mbi
                    boresight   = boresight
                    nearFar     = nearFar
                    fov         = setup |> Option.map (fun s -> s.fov.horizontal, s.fov.vertical)
                    flipRows    = setup |> Option.exists (fun s -> s.flipRows)
                }
        | _ ->
            Result.Error "no mbi sidecar"

    /// resolveProjectorWith at the kernels' instrument values (the tools).
    let resolveProjector (nearFar : Option<float * float>) (originBody : string) (originFrame : string)
                         (metadata : ParsedMetadata) (boresight : Option<Trafo3d>) (method : ProjectionMethod)
                         : Result<ResolvedProjector, string> =
        resolveProjectorWith Map.empty nearFar originBody originFrame metadata boresight method

    /// resolveProjectorWith for callers that only need the matrix; problems are logged.
    let projectDirectWith (instruments : InstrumentProjection.Instruments) (nearFar : Option<float * float>)
                          (originBody : string) (originFrame : string)
                          (metadata : ParsedMetadata) (boresight : Option<Trafo3d>) (method : ProjectionMethod)
                          : Option<Trafo3d> =
        match resolveProjectorWith instruments nearFar originBody originFrame metadata boresight method with
        | Ok p ->
            for w in p.warnings do Log.warn "[projection] %s" w
            Some p.full
        | Result.Error e ->
            Log.warn "[projection] %s" e
            None

    /// The kernels' instrument values, `nearFar` as in projectDirectWith.
    let projectDirectWithNearFar (nearFar : Option<float * float>) (originBody : string) (originFrame : string)
                                 (metadata : ParsedMetadata) (boresight : Option<Trafo3d>) (method : ProjectionMethod)
                                 : Option<Trafo3d> =
        projectDirectWith Map.empty nearFar originBody originFrame metadata boresight method

    /// The kernels' instrument values (the tools); the viewer passes its scene's through
    /// projectDirectWith.
    let projectDirect (originBody : string) (originFrame : string) (metadata : ParsedMetadata)
                      (boresight : Option<Trafo3d>) (method : ProjectionMethod) : Option<Trafo3d> =
        projectDirectWithNearFar None originBody originFrame metadata boresight method

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