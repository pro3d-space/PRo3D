namespace PRo3D.SceneRender

open System
open System.IO

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.UI

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer

/// One OPC to render, with the transformation from its own coordinates into the frame
/// every camera and sun is given in (world = trafo · p_opc). None: the identity.
type OpcInput =
    {
        path     : string
        trafo    : Option<Trafo3d>
        /// false: the surface's texture off (Surface.textureEnabled), i.e. a plain grey
        /// albedo -- for geometry-only renders and silhouette comparisons
        textured : bool
    }

/// A pinhole camera in the job's frame. The image's up is `up`, its right `forward × up`.
type ViewCamera =
    {
        location : V3d
        forward  : V3d
        up       : V3d
        /// horizontal field of view in degrees; the vertical one follows the image aspect
        fov      : float
    }

/// The sun as a position in the job's frame. The viewer's sun is a direction: the one
/// from the centre of the rendered OPCs towards this position.
type SunInput =
    {
        position : V3d
        /// cast shadows (LightingMode.SunShadow), or shading only (SunDirect)
        shadows  : bool
    }

module ViewCamera =

    /// The camera as the viewer's CameraView, and how many degrees `up` was turned to make
    /// it orthogonal to `forward`. An error names what is wrong.
    let toCameraView (c : ViewCamera) : Result<CameraView * float, string> =
        let f = c.forward
        let u = c.up
        if not (f.AllFinite && u.AllFinite && c.location.AllFinite) then
            Result.Error "location, forward and up must be finite"
        elif f.Length < 1e-12 then Result.Error "forward is zero"
        elif u.Length < 1e-12 then Result.Error "up is zero"
        elif not (c.fov > 0.0 && c.fov < 180.0) then
            Result.Error (sprintf "fov must be in (0, 180) degrees, got %g" c.fov)
        else
            let f = f.Normalized
            let u = u.Normalized
            let ortho = u - f * Vec.dot u f
            if ortho.Length < 1e-6 then Result.Error "forward and up are parallel"
            else
                let up = ortho.Normalized
                let turned = Conversion.DegreesFromRadians (acos (clamp -1.0 1.0 (Vec.dot u up)))
                // lookAt: forward = target - location, right = forward × sky, up = right × forward
                Ok (CameraView.lookAt c.location (c.location + f) up, turned)

    /// Near and far plane enclosing `bounds` from `location`, so that the 24-bit depth
    /// buffer is spent on the scene and not on 0.1 m .. 500 km.
    let nearFar (location : V3d) (bounds : Box3d) : float * float =
        let r = max (0.5 * bounds.Size.Length) 1e-3
        let d = Vec.distance location bounds.Center
        let near = max ((d - r) * 0.5) (r * 1e-4)
        let far = (d + r) * 2.0
        near, far

    /// Aardvark's Frustum.perspective takes the *horizontal* field of view.
    let frustum (c : ViewCamera) (size : V2i) (near : float, far : float) : Frustum =
        Frustum.perspective c.fov near far (float size.X / float size.Y)

module SunInput =

    /// Towards the sun from the centre of `bounds`; None when the sun sits at that centre.
    let direction (bounds : Box3d) (sun : SunInput) : Option<V3d> =
        let d = sun.position - bounds.Center
        if d.AllFinite && d.Length > 1e-9 then Some d.Normalized else None

/// OPCs opened headlessly without a scene file, each placed by its transformation, and
/// rendered as the viewer renders them from explicitly given cameras, optionally lit by
/// a given sun (Gis.SunOverride: the viewer's sun shading and shadow map, no SPICE).
///
/// Not thread-safe; render from the thread that owns the GL runtime.
type OpcRenderer(runtime : IRuntime, signature : IFramebufferSignature, opcs : list<OpcInput>) =

    do
        match opcs with
        | [] -> invalidArg (nameof opcs) "no OPCs given"
        | _ -> ()
        for o in opcs do
            let path = Path.GetFullPath o.path
            if not (Directory.Exists path) then
                raise (DirectoryNotFoundException(sprintf "no OPC directory at %s" path))
            if not (Files.isSurfaceFolder path || Files.isZippedOpcFolder path) then
                invalidArg (nameof opcs) (sprintf "%s is not an OPC directory (a folder holding OPC hierarchies)" path)
            // the import applies a <opc>.trafo beside the directory over preTransform
            // (SceneLoader.addLegacyTrafos): with a trafo given as well, which one wins
            // would be an accident
            let legacy = Path.ChangeExtension(path, ".trafo")
            match o.trafo with
            | Some _ when File.Exists legacy ->
                invalidArg (nameof opcs) (sprintf "%s has a trafo and a legacy trafo file %s; give only one" path legacy)
            | _ -> ()

    let core =
        new ViewerRenderer(runtime, signature, fun update fresh ->
            let triangleSize = fresh.scene.config.importTriangleSize.value
            let surfaces =
                opcs |> List.map (fun o ->
                    let s = SurfaceUtils.mk SurfaceType.SurfaceOPC MeshLoaderType.Unkown triangleSize (Path.GetFullPath o.path)
                    // before the import: the scene graph reads preTransform when it is
                    // built (bounding box, LoD culling)
                    let s = { s with textureEnabled = o.textured }
                    match o.trafo with
                    | Some t -> { s with preTransform = t }
                    | None -> s)
            // one import: import' rebuilds the scene graphs of all surfaces each time
            let m =
                SceneLoader.import' runtime signature (IndexList.ofList surfaces) fresh
                |> ViewerApp.updateSceneWithNewSurface
            let loaded = m.scene.surfacesModel.sgSurfaces |> HashMap.count
            if loaded <> List.length opcs then
                failwithf "%d of %d OPCs loaded (see the log)" loaded (List.length opcs)
            // A fresh viewer shows its exploration point and reference system (both at the
            // data's centre after the import); a scene file decides that itself, bare OPCs
            // are rendered without viewer furniture.
            { m with
                scene =
                    { m.scene with
                        config          = { m.scene.config with showExplorationPointGui = false }
                        referenceSystem = { m.scene.referenceSystem with isVisible = false } } })

    let bounds =
        core.Loaded.scene.surfacesModel.sgSurfaces
        |> HashMap.fold (fun (b : Box3d) _ s -> b.ExtendedBy s.globalBB) Box3d.Invalid

    /// Opens the OPCs with a signature like PRo3D.Snapshots' (RGBA8, depth-stencil, 8x MSAA).
    new(runtime : IRuntime, opcs : list<OpcInput>) =
        new OpcRenderer(runtime, PRo3D.SimulatedViews.SnapshotFramebuffer.createSignature runtime, opcs)

    /// The placed OPCs' bounding box, in the job's frame.
    member x.Bounds : Box3d = bounds

    /// The viewer model as last rendered.
    member x.Model : Model = core.Model

    /// Renders the OPCs from `camera`, lit by `sun` (None: no sun lighting, the viewer's
    /// unlit textures). Width and height of `settings.size` set the aspect.
    member x.Render(camera : ViewCamera, sun : Option<SunInput>, settings : RenderSettings) : PixImage<byte> =
        let view =
            match ViewCamera.toCameraView camera with
            | Ok (view, _) -> view
            | Result.Error e -> invalidArg (nameof camera) e
        let size = settings.size
        let frustum = ViewCamera.frustum camera size (ViewCamera.nearFar camera.location bounds)
        let mode =
            match sun with
            | None -> PRo3D.ImageMapping.LightingMode.Off
            | Some s when s.shadows -> PRo3D.ImageMapping.LightingMode.SunShadow
            | Some _ -> PRo3D.ImageMapping.LightingMode.SunDirect
        let sunDirection = sun |> Option.bind (SunInput.direction bounds)
        let prepare (m : Model) =
            let msgs =
                [
                    ViewerAction.SetCamera view
                    // resizing rebuilds a symmetric frustum from the scene's field of
                    // view: before SetFrustum, never after it
                    ViewerAction.ResizeMainControl (size, core.ViewportId)
                    ViewerAction.SetFrustum frustum
                    ViewerAction.GisAppMessage (Gis.GisAppAction.ProjectedImageListMessage (PRo3D.ImageMapping.ProjectedImageListMessage.SetLightingMode mode))
                ]
            msgs |> List.fold core.Update m
        core.Render(prepare, size, settings.lodLevel, sunDirection)

    interface IDisposable with
        member x.Dispose() = (core :> IDisposable).Dispose()
