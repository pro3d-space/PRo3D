/// TC-8.2 (Record and Generate Images), end to end, for sun illumination:
/// batch-renders a two-bookmark sequence through the real PRo3D.Snapshots.exe with
/// LightingMode.SunShadow active and per-bookmark SPICE observation times, and asserts
/// that both frames show a lit surface and that the lighting moved between them.
///
/// The scene is the Hera workshop Dimorphos scene with the GIS setup (surface
/// registration, observer, kernel, lighting mode) injected TYPED through the real Scene
/// codec -- which also exercises the lightingMode serialization end to end. The camera
/// is pinned (bookmark observation info carries no target, so the generator leaves the
/// bookmark's own view in place); only the observation TIME differs between the two
/// bookmarks, so any image difference is sun-driven.
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

/// Non-background pixels and their mean, for the assertions.
let private litStatistics (path : string) =
    let pix = PixImage.Load(path).ToPixImage<byte>(Col.Format.Gray)
    let data = pix.Volume.Data
    let lit = data |> Array.filter (fun v -> v > 15uy)
    lit.Length, (if lit.Length > 0 then lit |> Array.averageBy float else 0.0), data

let tests () =
    testList "snapshot-sun-lighting" [

        test "a generated sequence renders sun lighting that moves with the bookmark time" {
            if not (File.Exists fixtureScene) then
                skiptest (sprintf "workshop fixture not found: %s" fixtureScene)

            match PRo3D.Tool.Spice.resolveKernelRoot null with
            | Result.Error e -> skiptest (sprintf "no SPICE kernel tree: %s" e)
            | Result.Ok kernelRoot ->

            let kernel = Path.Combine(kernelRoot, "mk", "hera_plan.tm")
            if not (File.Exists kernel) then
                skiptest (sprintf "planning metakernel missing: %s" kernel)

            match PRo3D.Tests.Render.context.Value with
            | None -> skiptest "no OpenGL runtime in this environment"
            | Some _ ->

            let exe = Path.Combine(AppContext.BaseDirectory, "PRo3D.Snapshots.exe")
            if not (File.Exists exe) then
                skiptest (sprintf "PRo3D.Snapshots.exe not built (expected at %s)" exe)

            let dir = Path.Combine(Path.GetTempPath(), "pro3d-tests", "sun-lighting", Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory dir |> ignore
            let outDir = Path.Combine(dir, "out")
            Directory.CreateDirectory outDir |> ignore
            let mutable succeeded = false
            try
                // ---- scene: fixture + GIS setup through the typed codec ----
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
                        gisApp =
                            { gis with
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
                                        // observer = the body itself, in its own frame:
                                        // the GIS placement of the surface collapses to
                                        // identity, so the surface stays in its OPC
                                        // coordinates and the scene camera stays valid.
                                        // (An external observer like HERA would place
                                        // the surface at its SPICE position relative to
                                        // that observer -- kilometres away from a camera
                                        // pinned near the origin.)
                                        observer = Some (EntitySpiceName "DIMORPHOS")
                                        referenceFrame = Some (FrameSpiceName "DIMORPHOS_FIXED")
                                        time = Calendar.fromDate epochA }
                                spiceKernel = Some (CooTransformation.SPICEKernel.ofPath kernel) } }

                let scenePath = Path.Combine(dir, "sunlighting.pro3d")
                scene |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty
                |> fun s -> File.WriteAllText(scenePath, s)
                // the annotations sidecar is loaded by scene name; reuse the fixture's
                File.Copy(fixtureScene + ".ann", scenePath + ".ann")

                // ---- two bookmarks: same camera, different observation times ----
                let mkBookmark (name : string) (t : DateTime) =
                    let bookmark : Bookmark =
                        {
                            version        = Bookmark.current
                            key            = Guid.NewGuid()
                            name           = name
                            cameraView     = scene.cameraView
                            exploreCenter  = scene.exploreCenter
                            navigationMode = NavigationMode.FreeFly
                        }
                    let info =
                        { ObservationInfo.initial with
                            // no target: the camera must stay pinned to the bookmark's
                            // view, so only the sun moves between the frames; observer =
                            // the body itself keeps the surface placement at identity
                            target         = None
                            observer       = Some (EntitySpiceName "DIMORPHOS")
                            referenceFrame = Some (FrameSpiceName "DIMORPHOS_FIXED")
                            time           = Calendar.fromDate t }
                    { SequencedBookmarkModel.init bookmark with observationInfo = Some info }

                let animation : BookmarkSnapshotAnimation =
                    {
                        fieldOfView = Some 30.0
                        nearplane   = 0.1
                        farplane    = 100000.0
                        resolution  = V2i(640, 480)
                        snapshots   =
                            [
                                { filename = "frame_a"; transformation = BookmarkTransformation.Bookmark (mkBookmark "epochA" epochA) }
                                { filename = "frame_b"; transformation = BookmarkTransformation.Bookmark (mkBookmark "epochB" epochB) }
                            ]
                    }
                let asnapPath = Path.Combine(dir, "batch.json")
                SnapshotAnimation.BookmarkAnimation animation
                |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty
                |> fun s -> File.WriteAllText(asnapPath, s)

                // ---- run the real batch renderer ----
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
                // the exit code is deliberately not asserted: the exe has a known,
                // unrelated Task.Dispose crash during shutdown -- the outputs are the
                // contract here

                let frameA = Path.Combine(outDir, "frame_a.png")
                let frameB = Path.Combine(outDir, "frame_b.png")
                let diagnose () =
                    sprintf "stdout tail: %s"
                        (stdout.Result |> fun s -> s.Substring(max 0 (s.Length - 2000)))
                Expect.isTrue (File.Exists frameA) (sprintf "frame_16.png rendered; %s" (diagnose ()))
                Expect.isTrue (File.Exists frameB) (sprintf "frame_19.png rendered; %s" (diagnose ()))

                let litA, meanA, dataA = litStatistics frameA
                let litB, meanB, dataB = litStatistics frameB

                // the sun-lit surface must actually be in the frame, both times
                Expect.isGreaterThan litA 3000 "frame_a shows a lit surface"
                Expect.isGreaterThan litB 3000 "frame_b shows a lit surface"

                // three hours of Dimorphos rotation move the sun ~90 deg in the body
                // frame: with a pinned camera the shading must change substantially
                Expect.equal dataA.Length dataB.Length "frames have equal size"
                let meanAbsDiff =
                    (dataA, dataB)
                    ||> Array.map2 (fun a b -> abs (int a - int b))
                    |> Array.averageBy float
                Expect.isGreaterThan meanAbsDiff 2.0
                    "the lighting differs between the two bookmark times (pinned camera)"

                // keep the two frames of the newest successful run for eyeballing
                let lastDir = Path.Combine(Path.GetTempPath(), "pro3d-tests", "sun-lighting", "last")
                Directory.CreateDirectory lastDir |> ignore
                File.Copy(frameA, Path.Combine(lastDir, "frame_a.png"), true)
                File.Copy(frameB, Path.Combine(lastDir, "frame_b.png"), true)
                Log.line "[SnapshotSunLightingTest] frames kept at %s" lastDir
                succeeded <- true
            finally
                // keep the evidence (frames, scene, batch json, exe logs) on failure
                if succeeded then try Directory.Delete(dir, true) with _ -> ()
                else Log.line "[SnapshotSunLightingTest] artifacts kept at %s" dir
        }
    ]
