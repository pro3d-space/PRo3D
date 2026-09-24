/// A per-point profile export with surface properties must finish in seconds, not
/// hours. On the HERA Dimorphos OPC (Dimorphos_DRACO1_DRACO2_Earth) a ~450 point
/// profile ran for ages and needed ~10 GB: every layer except `Earth` is served by
/// the per-vertex *.aara data, but `Earth` exists only as a texture, and the texture
/// fallback decoded that whole 8652x4324 TIFF once per exported point.
///
/// Drives the real export (`AnnotationExportViewer.export`) against the scene
/// `cases/slowProfileExport.pro3d` of the test-data checkout, loaded into a headless
/// viewer. Needs PRO3D_TEST_DATA (or --testdatasource) and a GL context for the OPC
/// scene graph; skips otherwise.
module PRo3D.Tests.SlowProfileExportTest

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks

open Expecto

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Viewer

open PRo3D.Tests

/// Generous on purpose: the per-vertex path does a ray cast and a few small reads per
/// point — measured ~7 s / +1.25 GB cold (KdTree loading) and ~1 s / ~50 MB warm.
/// These bounds only separate "works" from "decodes an image per point".
let private timeBudget          = TimeSpan.FromSeconds 60.0
let private peakGrowthBudget    = 2048L * 1024L * 1024L
let private warmAllocatedBudget = 250L * 1024L * 1024L

let private mb (bytes : int64) = float bytes / (1024.0 * 1024.0)

/// Polls the process's private bytes while `f` runs, since the transient decode
/// buffers are what blow up — a before/after comparison would miss them after a GC.
let private withPeakPrivateBytes (f : unit -> 'a) =
    use proc = Process.GetCurrentProcess()
    let baseline = proc.PrivateMemorySize64
    let mutable peak = baseline
    use stop = new CancellationTokenSource()
    let poller =
        Task.Run(fun () ->
            while not stop.IsCancellationRequested do
                proc.Refresh()
                peak <- max peak proc.PrivateMemorySize64
                Thread.Sleep 50)
    try
        let r = f ()
        r, baseline, peak
    finally
        stop.Cancel()
        poller.Wait()

let private testDataRoot (parameters : TestUtils.TestParameters) =
    [ Environment.GetEnvironmentVariable "PRO3D_TEST_DATA"
      parameters.testDataSource |> Option.defaultValue "" ]
    |> List.tryFind (fun p -> not (String.IsNullOrWhiteSpace p) && Directory.Exists p)

let tests (parameters : TestUtils.TestParameters) =
    testList "slow profile export" [

        test "profile export with surface properties on Dimorphos stays within time and memory" {
            let scene =
                testDataRoot parameters
                |> Option.map (fun root -> Path.Combine(root, "cases", "slowProfileExport.pro3d"))

            match scene, Render.context.Value with
            | None, _ -> skiptest "PRO3D_TEST_DATA is not set"
            | Some s, _ when not (File.Exists s) -> skiptest (sprintf "no scene at %s" s)
            | _, None -> skiptest "no OpenGL runtime in this environment"
            | Some scene, Some _ ->

            Startup.init ()
            let freshModel, update = Render.makeViewer ()
            let loaded = update (freshModel ()) (ViewerAction.LoadScene scene)

            // the scene stores the OPC by absolute path; without it the export would
            // sample nothing and pass for the wrong reason
            if loaded.scene.surfacesModel.sgSurfaces |> HashMap.isEmpty then
                skiptest "the scene's OPC surface did not load (it is referenced by absolute path)"

            let annotations = AnnotationExportViewer.annotationsInScope ExportScope.All loaded.drawing.annotations
            Expect.isNonEmpty annotations "the scene holds the profile annotation"

            let settings =
                { AnnotationExportSettings.applyPreset ExportPreset.Profile AnnotationExportSettings.initial with
                    scope                   = ExportScope.All
                    sampleSurfaceProperties = true }

            let context : AnnotationExportViewer.SurfaceSamplingContext = {
                surfaces       = loaded.scene.surfacesModel
                observedSystem = fun v -> Gis.GisApp.getSpiceReferenceSystem loaded.scene.gisApp v
                observerSystem = Gis.GisApp.getObserverSystem loaded.scene.gisApp
            }

            let outputPath = Path.Combine(TestUtils.outputDir parameters "SlowProfileExport", "profile.csv")
            if File.Exists outputPath then File.Delete outputPath

            let allocatedBefore = GC.GetTotalAllocatedBytes true
            let clock = Stopwatch.StartNew()

            // on a separate task so a regression fails at the budget instead of
            // hanging the suite; there is no cancellation, so a runaway export keeps
            // going in the background until the test process exits
            let (task, finished), baseline, peak =
                withPeakPrivateBytes (fun () ->
                    let t =
                        Task.Run(fun () ->
                            AnnotationExportViewer.export
                                settings outputPath loaded.drawing loaded.scene.referenceSystem context)
                    t, t.Wait timeBudget)

            clock.Stop()
            let allocated = GC.GetTotalAllocatedBytes true - allocatedBefore

            Log.line "[SlowProfileExport] cold: %.1f s, %.0f MB allocated, private bytes %.0f -> peak %.0f MB (+%.0f)"
                clock.Elapsed.TotalSeconds (mb allocated) (mb baseline) (mb peak) (mb (peak - baseline))

            Expect.isTrue finished
                (sprintf "the export finishes within %.0f s (still running after %.0f s)"
                    timeBudget.TotalSeconds clock.Elapsed.TotalSeconds)

            match task.Result with
            | Some message -> Log.line "[SlowProfileExport] export reported: %s" message
            | None -> ()

            Expect.isLessThan (peak - baseline) peakGrowthBudget
                (sprintf "private bytes grow by at most %.0f MB (grew %.0f MB)" (mb peakGrowthBudget) (mb (peak - baseline)))

            // The cold run's allocation is dominated by loading the KdTrees and
            // triangle mappings of the patches hit (~5 GB here), which interactive
            // picking pays too. What an export itself costs per point only shows once
            // those are cached: this second run is where an image decode per point
            // (~100 MB each) would stand out.
            let warmAllocatedBefore = GC.GetTotalAllocatedBytes true
            let warmClock = Stopwatch.StartNew()
            AnnotationExportViewer.export settings outputPath loaded.drawing loaded.scene.referenceSystem context |> ignore
            warmClock.Stop()
            let warmAllocated = GC.GetTotalAllocatedBytes true - warmAllocatedBefore

            Log.line "[SlowProfileExport] warm: %.1f s, %.0f MB allocated"
                warmClock.Elapsed.TotalSeconds (mb warmAllocated)

            Expect.isLessThan warmAllocated warmAllocatedBudget
                (sprintf "with the KdTrees loaded, the export allocates at most %.0f MB (allocated %.0f MB)"
                    (mb warmAllocatedBudget) (mb warmAllocated))

            // and it still has to be the export that was asked for
            Expect.isTrue (File.Exists outputPath) "the CSV was written"
            let lines = File.ReadAllLines outputPath
            Expect.isGreaterThan lines.Length 2 "the CSV has a header and point rows"
            let header = lines.[0].Split ','
            for layer in [ "Elevation"; "Slope"; "LonLatRad" ] do
                Expect.isTrue
                    (header |> Array.exists (fun c -> c.IndexOf(layer, StringComparison.OrdinalIgnoreCase) >= 0))
                    (sprintf "the per-vertex layer %s has a column (header: %s)" layer lines.[0])
        }
    ]
