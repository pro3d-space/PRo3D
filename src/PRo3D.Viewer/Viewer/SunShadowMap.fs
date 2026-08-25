namespace PRo3D.Viewer

open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load   // IRuntime.CreateLoadRunner

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

/// Sun shadow mapping for the viewer's OPC surfaces (LightingMode.SunShadow).
///
/// Two halves:
///  - the RECEIVE half lives in the OPC effect stack (terrainSunShadow) and needs the
///    ShadowMap comparison sampler bound on every surface unconditionally -- FShade
///    rejects an unbound sampler at compile time even when the shadow branch is never
///    taken. While shadows are off that binding is a cached 1x1 far-plane dummy.
///  - the CAST half (this module) renders the scene's OPC surfaces from a sun-aligned
///    orthographic camera into a depth map. PatchNodes only render into the signature
///    they were built against (see SnapshotFramebuffer's warning comment), so the caster
///    pass builds its OWN PatchNodes via OpcSg.build against its own signature -- the
///    same approach pro3d-tool's simulate-image verb uses.
///
/// Everything is adaptive and lazy: unless the lighting mode is SunShadow (and a sun
/// direction resolves), the main pass samples the dummy and the caster task is never
/// pulled, so Off/SunDirect scenes pay nothing.
///
/// v1 limitations (documented in the feature docs): the sun direction comes from the
/// first GIS-registered surface, so multi-body scenes with different reference frames
/// share one sun; one global ortho map over the combined bounds targets small-body
/// scenes -- at planetary extents its resolution is useless.
module SunShadowMap =

    type Handle =
        {
            /// Depth map the OPC surfaces sample (the dummy while shadows are off).
            texture       : aval<ITexture>
            /// World -> sun-camera clip space; None whenever shadows are off, which
            /// keeps the per-patch HasShadowMap gate false.
            lightViewProj : aval<Option<Trafo3d>>
        }

    let private shadowMapSize = V2i(4096, 4096)

    /// A 1x1 depth texture backing the shadow comparison sampler whenever no real map
    /// exists. Its CONTENTS are never read -- the shader's HasShadowMap gate is false in
    /// exactly the situations this texture is bound -- so it is deliberately left
    /// uninitialized: this runs during scene-graph construction, where compiling and
    /// executing a clear pass on the GPU is not safe.
    let private createDummyTexture (runtime : IRuntime) : ITexture =
        runtime.CreateTexture(V3i(1, 1, 1), TextureDimension.Texture2D, TextureFormat.DepthComponent32f, 1, 1) :> ITexture

    /// True only in SunShadow mode -- the master switch for both halves.
    let private shadowActive (m : AdaptiveModel) : aval<bool> =
        m.scene.gisApp.projectedImageList.lightingMode
        |> AVal.map (fun l -> l = PRo3D.ImageMapping.LightingMode.SunShadow)

    /// Direction towards the sun in scene space, from the first GIS-registered surface
    /// (v1: one sun for the whole scene).
    let private sunDirection (m : AdaptiveModel) : aval<Option<V3d>> =
        m.scene.gisApp.gisSurfaces
        |> AMap.toAVal
        |> AVal.bind (fun surfs ->
            match surfs |> HashMap.toSeq |> Seq.tryHead with
            | Some (surfaceId, _) -> Gis.GisApp.getSunDirection m.scene.gisApp surfaceId
            | None -> AVal.constant None)

    /// The same placement the main render applies to a surface (viewSingleSurfaceSg):
    /// fullTrafo * preTransform, with the flipZ / sketchFab variants. Replicated here
    /// because the caster geometry must land exactly where the lit geometry is, or
    /// shadows arrive offset.
    let private surfacePlacement (m : AdaptiveModel) (surfaceId : Guid) (surf : AdaptiveSurface) : aval<Trafo3d> =
        let refsys = m.scene.referenceSystem
        let observerSystem = Gis.GisApp.getObserverSystemAdaptive m.scene.gisApp
        let observationSystem = Gis.GisApp.getSpiceReferenceSystemAdaptive m.scene.gisApp surfaceId
        adaptive {
            let! fullTrafo = TransformationApp.fullTrafo surf.transformation refsys observationSystem observerSystem
            let! preTransform = surf.preTransform
            let! flipZ = surf.transformation.flipZ
            let! sketchFab = surf.transformation.isSketchFab
            if flipZ then
                return Trafo3d.Scale(1.0, 1.0, -1.0) * (fullTrafo * preTransform)
            elif sketchFab then
                return Sg.switchYZTrafo
            else
                return fullTrafo * preTransform
        }

    /// The Ag attributes the OPC shaders / captureContext expect on every OPC scene
    /// graph, whether used or not -- without them CompileRender throws "could not get
    /// inh attribute X". Mirrors pro3d-tool's withOpcScaffolding.
    let private withOpcScaffolding (sg : ISg) =
        sg
        |> Sg.texture "ProjectedTexture" DefaultTextures.blackTex
        |> Sg.uniform' "ProjectedImageModelViewProjValid" true
        |> Sg.uniform' "LodVisEnabled" false
        |> PRo3D.Core.Surface.Sg.applyFootprint (AVal.constant M44d.Identity)
        |> PRo3D.Core.SgExtensions.Sg.applyCrossSection (AVal.constant None)
        |> Aardvark.GeoSpatial.Opc.SecondaryTexture.Sg.applySecondaryTextureId
            (AVal.constant (Some { texture = TextureReference.LegacyId 0
                                   channel = ChannelReference.NoChannelSelection }))

    /// All OPC surfaces as shadow casters: fresh PatchNodes against the shadow
    /// signature, each placed with the same trafo as in the main render, visibility
    /// respected. Blocking loads (asyncLoading = false) keep the map deterministic --
    /// acceptable because casters only load while SunShadow is actually on, and the
    /// feature targets small-body scenes.
    let private casterSg (runtime : IRuntime) (signature : IFramebufferSignature) (m : AdaptiveModel) : ISg =
        // The one load runner of the process, created at startup (Program.fs) -- the
        // same one the main surfaces load through. Runners are startup-time singletons:
        // creating a second one lazily inside the first shadow render meant spinning up
        // GL worker contexts in the middle of a running task, which corrupted the
        // thread's context bookkeeping ("cannot release context which is not current",
        // then a ValueOption.Value crash in the snapshot renderer).
        let runner =
            match PRo3D.Core.Surface.Sg.hackRunner with
            | Some r -> r
            | None -> failwith "GL runner was not initialized."
        let noImages : aval<Option<Sg.ProjectedImages>> = AVal.constant None

        m.scene.surfacesModel.surfaces.flat
        |> AMap.toASet
        |> ASet.map (fun (surfaceId, leaf) ->
            match leaf with
            | AdaptiveSurfaces surf ->
                let sg =
                    (surf.opcNames, surf.importPath)
                    ||> AVal.map2 (fun names importPath -> Files.expandNamesToPaths importPath names)
                    |> AVal.map (fun opcPaths ->
                        let existing = opcPaths |> List.filter System.IO.Directory.Exists
                        if List.isEmpty existing then Sg.empty
                        else
                            let cfg =
                                { OpcSg.defaultConfig signature runner DefaultMetrics.mars2 "SunShadowCaster" with
                                    asyncLoading = false
                                    useCompressedTextures = false }
                            OpcSg.build cfg noImages PRo3D.InstrumentVisualization.VisualizationProperties.empty existing
                            |> Sg.ofList)
                    |> Sg.dynamic
                sg
                |> Sg.onOff surf.isVisible
                |> Sg.trafo (surfacePlacement m surfaceId surf)
            | _ ->
                // OBJ and other non-OPC surfaces do not cast in v1
                Sg.empty)
        |> Sg.set
        |> withOpcScaffolding

    /// Combined world-space bounds of all (visible) surfaces -- the volume the sun-ortho
    /// camera must cover.
    let private sceneBounds (m : AdaptiveModel) : aval<Box3d> =
        AVal.custom (fun t ->
            let sgs = m.scene.surfacesModel.sgSurfaces.Content.GetValue t
            let mutable box = Box3d.Invalid
            for (surfaceId, s) in HashMap.toSeq sgs do
                let bb = s.globalBB.GetValue t
                match HashMap.tryFind surfaceId (m.scene.surfacesModel.surfaces.flat.Content.GetValue t) with
                | Some (AdaptiveSurfaces surf) ->
                    let visible = surf.isVisible.GetValue t
                    if visible then
                        let trafo = (surfacePlacement m surfaceId surf).GetValue t
                        box <- box.ExtendedBy(bb.Transformed trafo)
                | _ -> ()
            box)

    /// Sun-side ortho camera over the scene bounds. Frustum.ortho takes the view-space
    /// box Z verbatim as near/far, but the camera sits outside the box looking down -Z,
    /// so near/far are the negated maximum/minimum -- same construction (and same trap)
    /// as pro3d-tool's renderSunShadowMap.
    let private lightCamera (sunDir : aval<Option<V3d>>) (bounds : aval<Box3d>) : aval<Option<Trafo3d * Trafo3d>> =
        (sunDir, bounds) ||> AVal.map2 (fun dir bb ->
            match dir with
            | Some dir when bb.IsValid && bb.Size.Length > 0.0 ->
                let center = bb.Center
                let radius = 0.5 * bb.Size.Length
                let up = if abs (Vec.dot dir V3d.OOI) > 0.98 then V3d.OIO else V3d.OOI
                let view =
                    CameraView.lookAt (center + dir * (3.0 * radius)) center up
                    |> CameraView.viewTrafo
                let vbox = bb.Transformed view
                let proj =
                    { Frustum.ortho vbox with near = -vbox.Max.Z; far = -vbox.Min.Z }
                    |> Frustum.projTrafo
                Some (view, proj)
            | _ -> None)

    /// Everything the active shadow pass needs, created ON FIRST ACTIVATION only --
    /// handle creation happens during scene-graph construction, where no GPU work and no
    /// scene evaluation may run.
    let private createPass (runtime : IRuntime) (m : AdaptiveModel) =
        let sunDir = sunDirection m
        let camera = lightCamera sunDir (sceneBounds m)

        let signature =
            runtime.CreateFramebufferSignature([
                DefaultSemantic.Colors, TextureFormat.Rgba8
                DefaultSemantic.DepthStencil, TextureFormat.DepthComponent32f
            ], 1)

        let sg =
            casterSg runtime signature m
            |> Sg.shader {
                do! PRo3D.SPICE.Shaders.stableTrafo
                do! DefaultSurfaces.constantColor C4f.White
            }
            |> Sg.viewTrafo (camera |> AVal.map (function Some (v, _) -> v | None -> Trafo3d.Identity))
            |> Sg.projTrafo (camera |> AVal.map (function Some (_, p) -> p | None -> Trafo3d.Identity))

        let clearValues =
            clear {
                depth 1.0
                color C4f.Black
            }

        let (_color, depth) =
            sg
            |> Sg.compile runtime signature
            |> RenderTask.renderToColorAndDepthWithClear (AVal.constant shadowMapSize) clearValues

        // keep the adaptive render target alive across frames (see PackedRendering /
        // PRo3D.Lite for the idiom)
        depth.Acquire()
        camera, depth

    let private createHandle (runtime : IRuntime) (m : AdaptiveModel) : Handle =
        let dummy = createDummyTexture runtime
        let active = shadowActive m
        // built lazily on the first SunShadow activation; never when the mode stays off
        let pass = lazy (createPass runtime m)

        // Everything below the `active` bind is untouched while the mode is Off or
        // SunDirect: no SPICE query, no scene-bounds walk, no caster task. That inertness
        // is what makes it safe to sit in every render path unconditionally.
        let lightViewProj =
            active |> AVal.bind (function
                | false -> AVal.constant None
                | true ->
                    let (camera, _) = pass.Value
                    camera |> AVal.map (Option.map (fun (view, proj) -> view * proj)))

        let texture =
            active |> AVal.bind (function
                | false -> AVal.constant dummy
                | true ->
                    let (camera, depth) = pass.Value
                    camera |> AVal.bind (function
                        | Some _ -> depth |> AVal.map (fun t -> t :> ITexture)
                        | None -> AVal.constant dummy))

        { texture = texture; lightViewProj = lightViewProj }

    /// One handle per process: the runtime is a singleton in both the viewer and
    /// PRo3D.Snapshots, and createGroupedSgs may be re-executed on layout changes --
    /// re-creating render tasks there would leak them.
    let private cache = System.Collections.Concurrent.ConcurrentDictionary<IRuntime, Handle>()

    /// Fail-soft: a broken shadow pass must never take the whole render view down with
    /// it. On any error the feature degrades to "no shadows" (dummy map, no light
    /// matrix) and says so loudly.
    let get (runtime : IRuntime) (m : AdaptiveModel) : Handle =
        cache.GetOrAdd(runtime, fun runtime ->
            try
                createHandle runtime m
            with e ->
                Log.error "[SunShadowMap] shadow pass disabled, creation failed: %A" e
                { texture = AVal.constant (createDummyTexture runtime)
                  lightViewProj = AVal.constant None })
