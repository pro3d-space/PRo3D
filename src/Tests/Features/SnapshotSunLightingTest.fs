/// TC-8.2 (Record and Generate Images), end to end, for sun illumination: batch-renders
/// sequenced-bookmark animations through the real PRo3D.Snapshots.exe with
/// LightingMode.SunShadow active and per-bookmark SPICE observation times.
///
/// Two scenarios against the Hera workshop Dimorphos scene (GIS setup injected TYPED
/// through the real Scene codec, which also exercises the lightingMode serialization):
///
///  - close-up: the camera is pinned to the scene's saved view (bookmark observation
///    info carries no target, and observer = the body itself keeps the GIS surface
///    placement at identity), so any difference between the frames is sun-driven.
///  - approach phase: complete observation info (target Dimorphos, observer HERA), so
///    the generator's SPICE look-at path positions the camera at HERA's real location
///    per epoch and the surface at Dimorphos' HERA-relative position -- the same
///    scenario pro3d-tool's simulate-image verb renders, with an AFC-like 5.5 deg FoV.
///
/// Gated on: the C:\pro3ddata workshop fixture, $PRO3D_SPICE_KERNELS, a GL runtime, and
/// a built PRo3D.Snapshots.exe next to the test binaries.
module PRo3D.Tests.SnapshotSunLightingTest

open System
open System.Diagnostics
open System.IO

open Aardvark.Base
open Aardvark.UI.Primitives          // Calendar
open FSharp.Data.Adaptive            // HashMap
open Chiron
open Expecto

open PRo3D.Base
open PRo3D.Base.Gis                  // EntitySpiceName, FrameSpiceName
open PRo3D.Core
open PRo3D.Core.Gis                  // ObservationInfo
open PRo3D.Core.SequencedBookmarks   // SequencedBookmarkModel
open PRo3D.SimulatedViews            // Snapshot models
open PRo3D.Viewer                    // Scene
open PRo3D.Tests

let private fixtureScene = @"C:\pro3ddata\HERA\Workshop2\scenes\viewDimorphosDraco.pro3d"

// Both epochs keep the sun on the scene camera's side of the body (the DIMORPHOS_FIXED
// sun direction swings ~30 deg/h), two hours apart for an unmistakable lighting change.
let private epochA = DateTime(2027, 3, 15, 14, 0, 0, DateTimeKind.Utc)
let private epochB = DateTime(2027, 3, 15, 16, 0, 0, DateTimeKind.Utc)

/// Lit-surface pixel count plus a grayscale copy, for the assertions. Only near-neutral
/// pixels count as surface: sun-shaded OPC terrain is gray, while gizmos that survive
/// into snapshots (annotations, markers) are saturated colors and must not let a frame
/// pass with no actual surface in view.
let private litStatistics (path : string) =
    let pix = PixImage.Load(path).ToPixImage<byte>(Col.Format.RGB)
    let m = pix.GetMatrix<C3b>()
    let w = int m.Size.X
    let h = int m.Size.Y
    let mutable lit = 0
    let gray = Array.zeroCreate<byte> (w * h)
    for y in 0 .. h - 1 do
        for x in 0 .. w - 1 do
            let c = m.[int64 x, int64 y]
            let hi = max c.R (max c.G c.B)
            let lo = min c.R (min c.G c.B)
            let neutral = int hi - int lo < 30
            gray.[y * w + x] <- if neutral then hi else 0uy
            if neutral && hi > 15uy then lit <- lit + 1
    lit, gray

/// Everything both scenarios share; returns None with a skip reason when a
/// prerequisite is missing.
let private prerequisites () =
    if not (File.Exists fixtureScene) then
        Result.Error (sprintf "workshop fixture not found: %s" fixtureScene)
    else
        match PRo3D.Tool.Spice.resolveKernelRoot null with
        | Result.Error e -> Result.Error (sprintf "no SPICE kernel tree: %s" e)
        | Result.Ok kernelRoot ->
            let kernel = Path.Combine(kernelRoot, "mk", "hera_plan.tm")
            if not (File.Exists kernel) then
                Result.Error (sprintf "planning metakernel missing: %s" kernel)
            else
                match PRo3D.Tests.Render.context.Value with
                | None -> Result.Error "no OpenGL runtime in this environment"
                | Some _ ->
                    let exe = Path.Combine(AppContext.BaseDirectory, "PRo3D.Snapshots.exe")
                    if not (File.Exists exe) then
                        Result.Error (sprintf "PRo3D.Snapshots.exe not built (expected at %s)" exe)
                    else Result.Ok (kernel, exe)

/// The fixture scene with the GIS setup injected: surface registered as Dimorphos in
/// its body-fixed frame, SunShadow on, kernel set. Observer = the body itself, so the
/// GIS surface placement collapses to identity and the scene's saved camera stays
/// valid. (An external observer would place the surface at its SPICE position relative
/// to that observer -- kilometres away from a camera pinned in OPC coordinates.)
let private prepareScene (dir : string) (kernel : string) : string * Scene =
    let scene : Scene =
        File.ReadAllText fixtureScene |> Json.parse |> Json.deserialize

    let surfaceId =
        scene.surfacesModel.surfaces.flat
        |> HashMap.toList
        |> List.tryPick (fun (guid, leaf) ->
            match leaf with
            | Leaf.Surfaces _ -> Some guid
            | _ -> None)
    match surfaceId with
    | None -> failtest "the fixture scene contains no surface"
    | Some surfaceId ->

    let gis = scene.gisApp
    let scene =
        { scene with
            // the exploration-point marker would count as lit pixels in the assertions
            config = { scene.config with showExplorationPointGui = false }
            gisApp =
                { gis with
                    // entity spheres are drawn at the bodies' SPICE positions and would
                    // cover the surface exactly in the approach-phase views
                    entities =
                        gis.entities
                        |> HashMap.map (fun _ e -> { e with draw = false; showTrajectory = false })
                    projectedImageList =
                        { gis.projectedImageList with
                            lightingMode = PRo3D.ImageMapping.LightingMode.SunShadow }
                    gisSurfaces =
                        HashMap.ofList [
                            surfaceId,
                            { surfaceId = surfaceId
                              entity = Some (EntitySpiceName "DIMORPHOS")
                              referenceFrame = Some (FrameSpiceName "DIMORPHOS_FIXED") } ]
                    defaultObservationInfo =
                        { gis.defaultObservationInfo with
                            observer = Some (EntitySpiceName "DIMORPHOS")
                            referenceFrame = Some (FrameSpiceName "DIMORPHOS_FIXED")
                            time = Calendar.fromDate epochA }
                    spiceKernel = Some (CooTransformation.SPICEKernel.ofPath kernel) } }

    let scenePath = Path.Combine(dir, "sunlighting.pro3d")
    scene |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty
    |> fun s -> File.WriteAllText(scenePath, s)
    // the annotations sidecar is loaded by scene name; reuse the fixture's
    File.Copy(fixtureScene + ".ann", scenePath + ".ann")
    scenePath, scene

let private mkBookmark (scene : Scene) (info : ObservationInfo) (name : string) =
    let bookmark : Bookmark =
        {
            version        = Bookmark.current
            key            = Guid.NewGuid()
            name           = name
            cameraView     = scene.cameraView
            exploreCenter  = scene.exploreCenter
            navigationMode = NavigationMode.FreeFly
        }
    { SequencedBookmarkModel.init bookmark with observationInfo = Some info }

let private writeAnimation (dir : string) (fieldOfView : float) (snapshots : list<BookmarkSnapshot>) =
    let animation : BookmarkSnapshotAnimation =
        {
            fieldOfView = Some fieldOfView
            nearplane   = 0.1
            farplane    = 100000.0
            resolution  = V2i(640, 480)
            snapshots   = snapshots
        }
    let path = Path.Combine(dir, "batch.json")
    SnapshotAnimation.BookmarkAnimation animation
    |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty
    |> fun s -> File.WriteAllText(path, s)
    path

/// Runs the exe and returns its stdout. The exit code is deliberately not asserted:
/// the exe has a known, unrelated Task.Dispose crash during shutdown -- the rendered
/// outputs are the contract.
let private runBatch (exe : string) (dir : string) (scenePath : string) (asnapPath : string) (outDir : string) =
    let psi =
        ProcessStartInfo(
            FileName = exe,
            Arguments = sprintf "--scn \"%s\" --asnap \"%s\" --out \"%s\" --exitOnFinish" scenePath asnapPath outDir,
            WorkingDirectory = dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true)
    use proc = Process.Start psi
    let stdout = proc.StandardOutput.ReadToEndAsync()
    let stderr = proc.StandardError.ReadToEndAsync()
    if not (proc.WaitForExit(600_000)) then
        proc.Kill(true)
        failtest "PRo3D.Snapshots.exe did not finish within 10 minutes"
    File.WriteAllText(Path.Combine(dir, "snapshots.stdout.log"), stdout.Result)
    File.WriteAllText(Path.Combine(dir, "snapshots.stderr.log"), stderr.Result)
    stdout.Result

/// Runs one scenario in a scratch dir kept on failure; copies the frames of a
/// successful run into %TEMP%\pro3d-tests\sun-lighting\<keepAs>.
let private runScenario (keepAs : string) (fieldOfView : float) (mkInfo : DateTime -> ObservationInfo) (check : string -> string -> unit) =
    match prerequisites () with
    | Result.Error reason -> skiptest reason
    | Result.Ok (kernel, exe) ->

    let dir = Path.Combine(Path.GetTempPath(), "pro3d-tests", "sun-lighting", Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    let outDir = Path.Combine(dir, "out")
    Directory.CreateDirectory outDir |> ignore
    let mutable succeeded = false
    try
        let scenePath, scene = prepareScene dir kernel
        let asnapPath =
            writeAnimation dir fieldOfView [
                { filename = "frame_a"; transformation = BookmarkTransformation.Bookmark (mkBookmark scene (mkInfo epochA) "epochA") }
                { filename = "frame_b"; transformation = BookmarkTransformation.Bookmark (mkBookmark scene (mkInfo epochB) "epochB") }
            ]
        runBatch exe dir scenePath asnapPath outDir |> ignore

        let frameA = Path.Combine(outDir, "frame_a.png")
        let frameB = Path.Combine(outDir, "frame_b.png")
        Expect.isTrue (File.Exists frameA) "frame_a.png rendered"
        Expect.isTrue (File.Exists frameB) "frame_b.png rendered"

        check frameA frameB

        // keep the frames of the newest successful run for eyeballing
        let lastDir = Path.Combine(Path.GetTempPath(), "pro3d-tests", "sun-lighting", keepAs)
        Directory.CreateDirectory lastDir |> ignore
        File.Copy(frameA, Path.Combine(lastDir, "frame_a.png"), true)
        File.Copy(frameB, Path.Combine(lastDir, "frame_b.png"), true)
        Log.line "[SnapshotSunLightingTest] frames kept at %s" lastDir
        succeeded <- true
    finally
        // keep the evidence (frames, scene, batch json, exe logs) on failure
        if succeeded then try Directory.Delete(dir, true) with _ -> ()
        else Log.line "[SnapshotSunLightingTest] artifacts kept at %s" dir

let private meanAbsDiff (dataA : byte[]) (dataB : byte[]) =
    Expect.equal dataA.Length dataB.Length "frames have equal size"
    (dataA, dataB)
    ||> Array.map2 (fun a b -> abs (int a - int b))
    |> Array.averageBy float

let tests () =
    testList "snapshot-sun-lighting" [

        test "close-up: the lighting moves with the bookmark time (pinned camera)" {
            let mkInfo (t : DateTime) =
                { ObservationInfo.initial with
                    // no target: the camera stays pinned to the bookmark's view, so only
                    // the sun moves; observer = the body itself keeps the GIS surface
                    // placement at identity
                    target         = None
                    observer       = Some (EntitySpiceName "DIMORPHOS")
                    referenceFrame = Some (FrameSpiceName "DIMORPHOS_FIXED")
                    time           = Calendar.fromDate t }
            runScenario "last" 30.0 mkInfo (fun frameA frameB ->
                let litA, dataA = litStatistics frameA
                let litB, dataB = litStatistics frameB
                Expect.isGreaterThan litA 3000 "frame_a shows a lit surface"
                Expect.isGreaterThan litB 3000 "frame_b shows a lit surface"
                // two hours of Dimorphos rotation move the sun ~60 deg: with a pinned
                // camera the shading must change substantially
                Expect.isGreaterThan (meanAbsDiff dataA dataB) 2.0
                    "the lighting differs between the two bookmark times (pinned camera)")
        }

        test "approach phase: HERA's view of Dimorphos via the SPICE look-at camera" {
            let mkInfo (t : DateTime) =
                { ObservationInfo.initial with
                    // complete info: the generator's replayed ObservationInfoMessages
                    // swing the camera to HERA looking at Dimorphos, and the GIS
                    // placement puts the surface at its HERA-relative SPICE position --
                    // the bookmark's own cameraView is deliberately overridden
                    target         = Some (EntitySpiceName "DIMORPHOS")
                    observer       = Some (EntitySpiceName "HERA")
                    referenceFrame = Some (FrameSpiceName "J2000")
                    time           = Calendar.fromDate t }
            // AFC-like FoV: at HERA's ~9-13 km range, 5.5 deg makes the ~180 m body a
            // disk of roughly 60-100 px in the 640-wide frame
            runScenario "approach" 5.5 mkInfo (fun frameA frameB ->
                let litA, dataA = litStatistics frameA
                let litB, dataB = litStatistics frameB
                // a disk, not a close-up: present but far from filling the frame
                Expect.isGreaterThan litA 800 "frame_a shows the lit body"
                Expect.isGreaterThan litB 800 "frame_b shows the lit body"
                Expect.isLessThan litA (dataA.Length / 3) "frame_a is a distant view, not a close-up"
                Expect.isLessThan litB (dataB.Length / 3) "frame_b is a distant view, not a close-up"
                // HERA moved and the sun moved; the frames must differ
                Expect.isGreaterThan (meanAbsDiff dataA dataB) 0.5
                    "the view changes between the two epochs")
        }
    ]
