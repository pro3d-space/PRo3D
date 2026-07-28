namespace PRo3D.Core

open System
open System.IO

open MBrace.FsPickler

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

open Aardvark.Data
open Aardvark.Data.Opc
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load

open PRo3D.InstrumentVisualization

/// SPICE bootstrap for standalone viewers and tools.
///
/// Several viewers used to call CooTransformation.Init + AddSpiceKernel directly with a
/// hardcoded absolute metakernel path -- each pointing at a different developer's drive,
/// and each bypassing initCooTrafo's config unpacking, log setup and InstrumentPlatforms
/// init. Go through here instead.
module SpiceBoot =

    /// Default location of this repo's checked-out kernel tree, as a search root for
    /// `resolveSidecarKernel`. Only a fallback -- prefer an explicit path.
    let defaultKernelRoot =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "spice", "kernels")
        |> Path.GetFullPath

    /// Resolve the metakernel an mbi sidecar declares in its SPICE_MK field to a real
    /// file. Sidecars routinely name a kernel version that is not the one on disk, so a
    /// miss here is expected -- callers should fall back to a known-good kernel and say
    /// loudly that they substituted it, rather than failing.
    let resolveSidecarKernel (searchRoot : string) (mkName : string) =
        PRo3D.Base.CooTransformation.tryFindSpiceKernelFile searchRoot mkName

    /// Initialise SPICE with a single metakernel. Dispose to shut down.
    ///
    /// Only one metakernel can be active: there is no native per-kernel unload, and
    /// layering two meta-kernels silently corrupts state (conflicting CK segments for
    /// the same frame). Use `switch` to change kernels, never a second `init`.
    let init (kernelPath : Option<string>) =
        let appData =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pro3D")
        if not (Directory.Exists appData) then Directory.CreateDirectory appData |> ignore
        PRo3D.Base.CooTransformation.initCooTrafo kernelPath appData
        { new IDisposable with
            member _.Dispose() = PRo3D.Base.CooTransformation.deInitCooTrafo() }

    /// Swap the active metakernel (full DeInit + Init + load).
    let switch (kernelPath : string) =
        PRo3D.Base.CooTransformation.switchKernel
            (Path.GetDirectoryName kernelPath) (Path.GetFileName kernelPath)


/// Construction of OPC level-of-detail scene graphs with the image-projection uniforms
/// attached. Extracted from TestViewer so that offscreen tools and interactive viewers
/// build the identical graph -- what you debug interactively is what gets screenshotted.
module OpcSg =

    /// Everything that does not vary per OPC hierarchy.
    type Config =
        {
            signature       : IFramebufferSignature
            runner          : Load.Runner
            /// Normally OpcScene.lodDecider.
            lodDecider      : PatchLod.LodDecider

            /// false makes patch loading blocking. Required for deterministic offscreen
            /// rendering: with async loading a screenshot captures whatever subset of the
            /// LOD tree happened to have arrived. Interactive use wants true.
            asyncLoading    : bool

            /// SPICE body the patches belong to, e.g. "MARS" or "DIDYMOS".
            body            : string

            useCompressedTextures : bool
        }

    let defaultConfig signature runner lodDecider body =
        {
            signature = signature
            runner = runner
            lodDecider = lodDecider
            asyncLoading = true
            body = body
            useCompressedTextures = true
        }

    /// Build one LOD node per OPC hierarchy, wired up with the projection uniforms.
    ///
    /// `projectedImages` supplies the projector trafos, sun direction and lighting flag;
    /// the per-patch uniforms themselves (including the local->projector matrices) are
    /// computed by ImageProjectionOpcExtensions.projectionUniformMap.
    /// WORKAROUND (2026-07-22, investigation ongoing): OPC datasets are inconsistently
    /// wound, so generateNormal's `cross edge1 edge2` points outward on one and inward on
    /// another. Until the cause is understood, estimate each hierarchy's winding on the
    /// CPU: sample up to ~100 faces of the coarse root patch and vote whether they point
    /// away from the body-fixed origin (the barycenter). Majority inward -> the shader
    /// must flip (returns 1.0), else 0.0. Valid for star-shaped bodies.
    let private estimateNormalFlip (basePath : string) (rootPatch : Patch) : float =
        try
            let ig, _ = Patch.load (OpcPaths.OpcPaths basePath) ViewerModality.XYZ rootPatch.info
            let l2g = rootPatch.info.Local2Global.Forward
            match ig.IndexedAttributes.[DefaultSemantic.Positions], ig.IndexArray with
            | (:? array<V3f> as pos), (:? array<int> as idx) ->
                let triCount = idx.Length / 3
                let stride = max 1 (triCount / 100)   // ~100 samples spread across the patch
                let mutable outward = 0
                let mutable inward = 0
                let mutable t = 0
                while t < triCount do
                    let i = t * 3
                    let a = pos.[idx.[i]]
                    let b = pos.[idx.[i + 1]]
                    let c = pos.[idx.[i + 2]]
                    if not (a.IsNaN || b.IsNaN || c.IsNaN) then
                        let n = l2g.TransformDir (V3d (Vec.cross (b - a) (c - a)))
                        let centroid = l2g.TransformPos (V3d ((a + b + c) / 3.0f))
                        if Vec.dot n centroid > 0.0 then outward <- outward + 1
                        else inward <- inward + 1
                    t <- t + stride
                if outward + inward = 0 then 0.0
                else
                    let flip = if inward > outward then 1.0 else 0.0
                    Log.line "[opc]   winding: %d outward / %d inward -> NormalFlip %.0f"
                        outward inward flip
                    flip
            | _ -> 0.0
        with e ->
            Log.warn "[opc]   could not estimate winding (%s); NormalFlip 0" e.Message
            0.0

    /// Area-weighted centroid of the coarse root patch, in the body-fixed (global) frame:
    /// an estimate of the shape model's centre of figure that -- unlike a plain vertex
    /// mean -- is not biased by uneven tessellation. Also logs the Z (pole-axis) coverage,
    /// so "is a polar cap missing" can be answered rather than assumed. NaN vertices
    /// (genuine holes) are skipped.
    let modelCenterOfFigure (basePath : string) : Option<V3d> =
        try
            let serializer = FsPickler.CreateBinarySerializer()
            let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
            let rootPatch =
                match h.tree with
                | QTree.Node (p, _) -> p
                | QTree.Leaf p -> p
            let ig, _ = Patch.load (OpcPaths.OpcPaths basePath) ViewerModality.XYZ rootPatch.info
            let l2g = rootPatch.info.Local2Global.Forward
            match ig.IndexedAttributes.[DefaultSemantic.Positions], ig.IndexArray with
            | (:? array<V3f> as pos), (:? array<int> as idx) ->
                let g = pos |> Array.map (fun p -> if p.IsNaN then V3d.Zero, false else l2g.TransformPos (V3d p), true)
                // area-weighted (surface) centroid
                let mutable wsum = V3d.Zero
                let mutable atot = 0.0
                // vertex-mean centroid, for contrast
                let mutable vsum = V3d.Zero
                let mutable vn = 0
                let mutable zmin = infinity
                let mutable zmax = -infinity
                for (p, ok) in g do
                    if ok then
                        vsum <- vsum + p; vn <- vn + 1
                        zmin <- min zmin p.Z; zmax <- max zmax p.Z
                let mutable i = 0
                while i + 2 < idx.Length do
                    let (a, oa), (b, ob), (c, oc) = g.[idx.[i]], g.[idx.[i+1]], g.[idx.[i+2]]
                    if oa && ob && oc then
                        let area = 0.5 * (Vec.cross (b - a) (c - a)).Length
                        wsum <- wsum + area * ((a + b + c) / 3.0)
                        atot <- atot + area
                    i <- i + 3
                // how populated are the two polar bands (top/bottom 10% of Z)?
                let band = 0.1 * (zmax - zmin)
                let mutable north = 0
                let mutable south = 0
                for (p, ok) in g do
                    if ok then
                        if p.Z > zmax - band then north <- north + 1
                        if p.Z < zmin + band then south <- south + 1
                Log.line "[model]   Z coverage %.1f .. %.1f m; poles: north band %d verts, south band %d verts"
                    zmin zmax north south
                let vmean = if vn > 0 then vsum / float vn else V3d.Zero
                Log.line "[model]   vertex-mean centroid %.1f m (%.1f %.1f %.1f) -- biased by tessellation"
                    vmean.Length vmean.X vmean.Y vmean.Z
                if atot > 0.0 then Some (wsum / atot) else None
            | _ -> None
        with _ -> None

    let build (cfg : Config)
              (projectedImages : aval<Option<Sg.ProjectedImages>>)
              (imageSettings : VisualizationProperties)
              (hierarchies : seq<string>) : list<ISg> =

        let serializer = FsPickler.CreateBinarySerializer()

        hierarchies
        |> Seq.toList
        |> List.map (fun basePath ->
            let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)

            // Placement assumes the hierarchy is centred on its own body: a secondary body
            // is positioned by translating this origin to the SPICE position. If the
            // global bounding box is NOT centred on the origin, that assumption is false
            // and the body lands offset by whatever the centre offset is. Logged rather
            // than asserted because a legitimately off-centre OPC is possible.
            let rootPatch =
                match h.tree with
                | QTree.Node (p, _) -> p
                | QTree.Leaf p -> p
            let bb = rootPatch.info.GlobalBoundingBox
            Log.line "[opc] %s" (System.IO.Path.GetFileName basePath)
            Log.line "[opc]   global bbox centre %.1f %.1f %.1f  size %.1f %.1f %.1f"
                bb.Center.X bb.Center.Y bb.Center.Z bb.Size.X bb.Size.Y bb.Size.Z
            let offCentre = bb.Center.Length / (0.5 * bb.Size.NormMax)
            if offCentre > 0.1 then
                Log.warn "[opc]   NOT centred on origin: centre is %.0f%% of the half-extent away"
                    (offCentre * 100.0)

            let tree = PatchLod.toRoseTree h.tree

            PatchLod.PatchNode(
                cfg.signature, cfg.runner, basePath, cfg.lodDecider,
                cfg.useCompressedTextures, true, ViewerModality.XYZ,
                PatchLod.CoordinatesMapping.Local, cfg.asyncLoading,
                OpcRenderingExtensions.captureContext,
                ImageProjectionOpcExtensions.projectionUniformMap,
                tree, None, None, PixImagePfim.Loader)
            |> Sg.applyBody (AVal.constant (Some cfg.body))
            |> Sg.applyProjectedImages' (fun _ -> projectedImages)
            |> InstrumentImageVisualization.applyProperties imageSettings
            // Outer, not in projectionUniformMap: a per-patch uniform of this name would
            // shadow it. Unset elsewhere defaults to 0, so the main viewer is unaffected.
            |> Sg.uniform' "NormalFlip" (estimateNormalFlip basePath rootPatch)
        )
