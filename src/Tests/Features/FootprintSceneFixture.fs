/// Builds the view-plan footprint fixture scene for issue #733.
///
/// Not a test: issue #733 (no footprint boundary on the surface) is a rendering
/// defect, and asserting on the uniform that feeds the shader proves nothing about
/// what is drawn. So this produces a scene to open instead - a surface with a rover
/// placed on it, an instrument selected and the footprint switched on - which shows
/// the regression in a 6.0.0 build and the fix in a patched one.
///
/// Run it with:
///     dotnet run --project src/Tests -- --make-footprint-scene [outDir]
///
/// It drives the same ViewerActions the UI raises (import, place rover, select
/// instrument, toggle footprint, save), so the scene is exactly what a user would
/// have produced by hand.
///
/// Needs the src/Tests/resources submodule and a GL context, like every other
/// OPC-backed fixture here.
module PRo3D.Tests.FootprintSceneFixture

open System
open System.IO

open Aardvark.Base
open Aardvark.Geometry                    // ConcreteKdIntersectionTree
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.SimulatedViews
open PRo3D.Viewer

open PRo3D.Tests

/// Straight-down ray cast onto the imported surface, the way the viewer picks: from
/// well above `p` along -up. Returns the hit and the (warmed) kd-tree cache, which
/// the rover placement then reuses to settle the wheels onto the terrain.
let private dropOnSurface
    (m        : Model)
    (cache    : HashMap<string, ConcreteKdIntersectionTree>)
    (up       : V3d)
    (height   : float)
    (p        : V3d) =

    let surfaceFilter = fun (_id : Guid) (l : Leaf) (_s : SgSurface) -> l.visible && l.active
    let ray = FastRay3d(Ray3d(p + up * height, -up))
    let surfaces = m.scene.surfacesModel

    match SurfaceIntersection.doKdTreeIntersection
              surfaces m.scene.referenceSystem (fun _ -> None) None ray surfaceFilter cache false with
    | Some hitInfo, cache -> Some (ray.Ray.GetPointOnRay hitInfo.hit.RayHit.T), cache
    | None, cache -> None, cache

/// Any unit vector perpendicular to up - the direction the rover is pointed in.
/// Prefers the reference system's north; falls back to an arbitrary tangent for
/// surfaces with no meaningful north (north parallel to up).
let private headingOf (refSystem : ReferenceSystem) (up : V3d) =
    let north = refSystem.north.value
    let tangential = north - up * (Vec.dot north up)
    if tangential.Length > 1e-6 then tangential.Normalized
    else
        let seed = if abs up.X < 0.9 then V3d.IOO else V3d.OIO
        (seed - up * (Vec.dot seed up)).Normalized

/// Replicates Shader.footPrintF for one world-space point: is it inside the instrument
/// frustum, and close enough to an edge of the image (or to the near plane) to be
/// painted as the footprint boundary? `vp` is the same instrument clip <- world matrix
/// the per-patch uniform is built from, so this predicts what the surface will show.
let private onFootprintBorder (vp : M44d) (p : V3d) =
    let clip = vp * V4d(p, 1.0)
    if abs clip.W < 1e-12 then false
    else
        let f = clip.XYZ / clip.W
        let threshold = 0.05
        let inside = f.X > -1.0 && f.X < 1.0 && f.Y > -1.0 && f.Y < 1.0 && f.Z > -1.0 && f.Z < 1.0
        inside &&
        (f.X < -1.0 + threshold || f.X > 1.0 - threshold ||
         f.Y < -1.0 + threshold || f.Y > 1.0 - threshold ||
         f.Z < -1.0 + threshold)

let private theOnlySurface (m : Model) =
    match m.scene.surfacesModel.surfaces.flat |> HashMap.toList with
    | [ (id, _) ] -> id
    | other -> failwithf "expected exactly one surface after import, got %d" (List.length other)

/// Writes the scene and returns its path.
let build (outDir : string) =
    Startup.init ()

    // Importing an OPC infers its reference system, which calls Xyz2LatLonAlt - and
    // CSPICE aborts the process on BODY499_RADII if no kernel pool is loaded. The
    // suite normally gets this from whichever list runs first; do it explicitly here.
    // The default kernels shipped with PRo3D are enough for Mars radii.
    do Aardvark.Base.Aardvark.UnpackNativeDependencies(
        typeof<PRo3D.Extensions.FSharp.CooTransformation.RelState>.Assembly)
    let appData = Path.combine [Environment.GetFolderPath Environment.SpecialFolder.ApplicationData; "Pro3D"]
    CooTransformation.initCooTrafo None appData

    match Render.skipReason () with
    | Some reason -> failwithf "cannot build the footprint fixture: %s" reason
    | None -> ()

    let freshModel, update = Render.makeViewer ()

    if not (Directory.Exists outDir) then Directory.CreateDirectory outDir |> ignore
    let scenePath = Path.Combine(outDir, "footprint.pro3d")

    // 1. import the surface, exactly as "Surface -> Import OPC" does
    let imported = update (freshModel ()) (ViewerAction.ImportSurface [ Render.opcSurfaceDir ])
    let surfaceId = theOnlySurface imported

    let surfaceBox = (imported.scene.surfacesModel.sgSurfaces |> HashMap.find surfaceId).globalBB

    // 2. an initial save gives the scene a path, which loadRoverData requires before
    //    it will read the instrument platforms at all
    let named = update imported (ViewerAction.SaveAs scenePath)
    let withRovers = named |> ViewerIO.loadRoverData

    let rover =
        match withRovers.scene.viewPlans.roverModel.rovers |> HashMap.toList with
        // whichever platform the build ships first; named in the log below so the
        // scene stays reproducible as the platform set changes
        | (_, r) :: _ -> r
        | [] -> failwith "no rovers - the instrument platform XMLs did not load"

    Log.line "[footprint fixture] rover: %s" rover.id

    let selected =
        update withRovers (ViewerAction.RoverMessage (RoverApp.Action.SelectRover (Some rover)))

    // 3. place the rover: two clicks on the surface, position then look-at, the same
    //    pair of AddPoints the PlaceRover interaction raises
    //
    // Which way is up depends on how far the reference system got: freshly imported it
    // may still hold the default, while OPC vertices are in body-fixed coordinates
    // where up at the patch is the radial direction. Take whichever actually hits the
    // terrain rather than assuming, and say so in the log.
    let height = surfaceBox.Size.Length

    let up, cache =
        let candidates =
            [ "reference system", selected.scene.referenceSystem.up.value
              "radial (body-fixed)", surfaceBox.Center.Normalized
              "world Z", V3d.OOI ]
            |> List.filter (fun (_, v) -> v.Length > 1e-6)

        let rec pick cache = function
            | [] ->
                failwithf "no ray onto the surface centre hit anything (bbox %A) - is the kd-tree data present?"
                          surfaceBox
            | (name, v : V3d) :: rest ->
                let v = v.Normalized
                match dropOnSurface selected cache v height surfaceBox.Center with
                | Some _, cache ->
                    Log.line "[footprint fixture] up = %s %A" name v
                    v, cache
                | None, cache -> pick cache rest

        pick HashMap.empty candidates

    let heading = headingOf selected.scene.referenceSystem up

    Log.line "[footprint fixture] surface bbox %A (extent %.1f m)" surfaceBox surfaceBox.Size.Length

    // Stand back from the centre and look across it, so the instrument frustum meets
    // terrain instead of the horizon - a footprint projected past the edge of the patch
    // would be just as invisible as the bug. Take the widest stand-off where both ends
    // still land on terrain; a small patch simply gets a shorter baseline.
    let position, lookAt, cache =
        let rec widest cache = function
            | [] -> failwith "no stand-off along the heading hit terrain at both ends"
            | (fraction : float) :: rest ->
                let reach = surfaceBox.Size.Length * fraction
                let a, cache = dropOnSurface selected cache up height (surfaceBox.Center - heading * reach)
                let b, cache = dropOnSurface selected cache up height (surfaceBox.Center + heading * reach)
                match a, b with
                | Some a, Some b ->
                    Log.line "[footprint fixture] stand-off %.1f m (%.0f%% of the extent)" reach (fraction * 100.0)
                    a, b, cache
                | _ -> widest cache rest

        widest cache [ 0.35; 0.25; 0.15; 0.08; 0.04 ]

    Log.line "[footprint fixture] rover at %A looking at %A" position lookAt

    let refSystem = selected.scene.referenceSystem
    let surfaces = selected.scene.surfacesModel

    let placed =
        [ position; lookAt ]
        |> List.fold
            (fun m p ->
                update m (ViewerAction.ViewPlanMessage
                            (ViewPlanApp.Action.AddPoint(p, refSystem, cache, surfaces))))
            selected

    let viewPlan =
        match placed.scene.viewPlans.viewPlans |> HashMap.toList with
        | [ (_, vp) ] -> vp
        | other -> failwithf "expected exactly one view plan, got %d" (List.length other)

    // 4. The terrain the boundary check scores against: a grid of straight-down casts
    //    over the patch, so "will the outline be visible" is answered against the real
    //    surface rather than its bounding box.
    let samples, cache =
        let n = 40
        let heading2 = Vec.cross up heading |> Vec.normalize
        let grid =
            [| for i in 0 .. n - 1 do
                 for j in 0 .. n - 1 do
                     let u = (float i / float (n - 1) - 0.5) * surfaceBox.Size.Length
                     let v = (float j / float (n - 1) - 0.5) * surfaceBox.Size.Length
                     yield surfaceBox.Center + heading * u + heading2 * v |]
        let hits, cache =
            grid
            |> Array.fold
                (fun (hits, cache) p ->
                    match dropOnSurface placed cache up height p with
                    | Some hit, cache -> hit :: hits, cache
                    | None, cache -> hits, cache)
                ([], cache)
        Array.ofList hits, cache

    Log.line "[footprint fixture] %d terrain samples for the boundary check" samples.Length

    let scoreOf (m : Model) =
        let fp = m.footPrint
        let vp = fp.projectionMatrix * fp.instViewMatrix
        samples |> Array.sumBy (fun p -> if onFootprintBorder vp p then 1 else 0)

    let tiltAxis =
        viewPlan.rover.axes
        |> HashMap.toList
        |> List.map fst
        |> List.sort
        |> List.tryFind (fun name -> name.ToLowerInvariant().Contains "tilt")

    // 5. Choose the instrument and its tilt together, keeping whatever puts the most
    //    terrain on the footprint boundary.
    //
    //    Both have to be chosen, not defaulted. HashMap order is not stable across runs,
    //    so "the first instrument" would make the fixture differ run to run; and with the
    //    axes at rest the camera looks at the horizon, where on a patch this small the
    //    image edges - the only part footPrintF paints - miss the terrain entirely and
    //    would look exactly like the bug. Ties break on the smaller tilt, then on the
    //    instrument name, so the result is reproducible.
    let candidates =
        viewPlan.rover.instruments
        |> HashMap.toList
        |> List.map snd
        |> List.sortBy (fun i -> i.id)

    let tilts =
        match tiltAxis with
        | Some _ -> [ -90.0 .. 5.0 .. 90.0 ]
        | None ->
            Log.warn "[footprint fixture] no tilt axis on this rover; leaving the axes at rest"
            [ 0.0 ]

    let withInstrumentAndFootprint (instrument : Instrument) =
        let m = update placed (ViewerAction.ViewPlanMessage (ViewPlanApp.Action.SelectInstrument (Some instrument)))
        if m.footPrint.isVisible then m
        else update m (ViewerAction.ViewPlanMessage ViewPlanApp.Action.ToggleFootprint)

    let aimed =
        candidates
        |> List.collect (fun instrument ->
            let switchedOn = withInstrumentAndFootprint instrument
            tilts
            |> List.map (fun angle ->
                let m =
                    match tiltAxis with
                    | Some axis ->
                        update switchedOn (ViewerAction.ViewPlanMessage
                            (ViewPlanApp.Action.ChangeAngle(axis, PRo3D.Base.Utilities.PRo3DNumeric.SetValue angle)))
                    | None -> switchedOn
                instrument, angle, scoreOf m, m))
        |> List.sortBy (fun (instrument, angle, score, _) -> -score, abs angle, instrument.id)
        |> List.tryHead
        |> function
           | Some (instrument, angle, score, m) when score > 0 ->
               Log.line "[footprint fixture] %s at tilt %.0f deg puts the boundary across %d of %d samples"
                        instrument.id angle score samples.Length
               m
           | _ ->
               Log.warn "[footprint fixture] no instrument and tilt puts the footprint boundary on this surface                          - the patch is only %.1f m across, so no instrument image fits on it. The scene is still                          written, but expect no outline even with the fix."
                        surfaceBox.Size.Length
               match candidates with
               | instrument :: _ -> withInstrumentAndFootprint instrument
               | [] -> failwithf "rover %s has no instruments" viewPlan.rover.id

    if not aimed.footPrint.isVisible then
        failwith "the footprint is still switched off after ToggleFootprint"

    // 6. point the camera at the terrain before saving, so the scene opens on the
    //    footprint rather than on wherever a fresh viewer happens to look
    let framed =
        update aimed (ViewerAction.SurfaceActions (SurfaceAppAction.FlyToSurface surfaceId))
        |> Render.runAnimationToCompletion update

    // 7. save again, now carrying the view plan
    update framed (ViewerAction.SaveAs scenePath) |> ignore

    // 8. reopen it, because a fixture that does not load is worse than none: the view
    //    plan goes through its own Chiron codec on the way back in.
    let reopened = update (freshModel ()) (ViewerAction.LoadScene scenePath)

    match reopened.scene.viewPlans.viewPlans |> HashMap.toList with
    | [ (_, vp) ] ->
        match vp.selectedInstrument with
        | Some i when vp.footPrint.isVisible ->
            Log.line "[footprint fixture] reopened: %s, instrument %s, footprint on" vp.name i.id
        | Some i -> failwithf "reopened with instrument %s but the footprint switched off" i.id
        | None -> failwith "reopened with no instrument selected"
    | other -> failwithf "reopened with %d view plans" (List.length other)

    Log.line "[footprint fixture] wrote %s" scenePath
    scenePath
