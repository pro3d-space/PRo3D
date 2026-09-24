/// Frame time of the map projection view (#772), rung 4 of its testing ladder.
///
/// Same harness as the #719 surface-effect benchmark (`SurfaceEffectBenchmark.Bench`): no
/// per-frame sleep or readback, GPU time queries over batches, settle until two frames are
/// identical. Same reasons for being a tool, not a test: `Tests.dll --bench-map`.
///
/// Arms: projection (equirectangular, polar north) x LoD (finest = what phase 1 draws,
/// root = the coarsest level any decider could fall back to) x size. The finest/root ratio
/// is the most a map LoD decider (phase 1.5) could save on this dataset.
module PRo3D.Tests.MapProjectionBenchmark

open System
open System.IO
open System.Diagnostics

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load

open PRo3D.Core
open PRo3D.MapProjection
open PRo3D.Tests
open PRo3D.Tests.SurfaceEffectBenchmark

/// Only the root patch: the cheapest any decider can draw.
let private rootOnly : PatchLod.LodDecider = fun _ _ _ _ _ _ -> false

/// Cleared to a colour no surface texture of the test data contains, so dark terrain (and
/// the black sky of a camera frame used as a texture layer) still counts as drawn.
let private background = C4f(1.0f, 0.0f, 1.0f, 1.0f)

let private covered (img : PixImage<byte>) =
    let m = img.GetMatrix<C4b>()
    let mutable n = 0L
    m.ForeachIndex(fun i -> let c = m.[i] in if not (c.R > 250uy && c.G < 5uy && c.B > 250uy) then n <- n + 1L)
    float n / float (m.SX * m.SY)

/// Fraction of the viewport the map occupies at zoom 1: the fitted extent, a disc for polar maps.
let private expectedCoverage (kind : MapProjectionKind) (size : V2i) =
    let e = Projection.extent kind Projection.defaultMaxColatitude
    let pixelsPerUnit = min (float size.X / e.Size.X) (float size.Y / e.Size.Y)
    let rect = (e.Size.X * pixelsPerUnit) * (e.Size.Y * pixelsPerUnit) / float (size.X * size.Y)
    if kind = MapProjectionKind.Equirectangular then rect else rect * Math.PI / 4.0

let run () : int =
    let mutable failures = 0
    let check (ok : bool) (what : string) =
        if ok then say "[bench-map]   PASS  %s" what
        else
            failures <- failures + 1
            say "[bench-map]   FAIL  %s" what

    let opcDir =
        match Environment.GetEnvironmentVariable "PRO3D_BENCH_OPC" with
        | null | "" ->
            TestUtils.Roots.dimorphosOpc None
            |> Option.defaultValue ""
        | dir -> dir

    let env (name : string) (fallback : int) =
        match Int32.TryParse(Environment.GetEnvironmentVariable name) with
        | true, v when v > 0 -> v
        | _ -> fallback
    let frames  = env "PRO3D_BENCH_FRAMES" 40
    let repeats = env "PRO3D_BENCH_REPEATS" 5
    let warmup  = env "PRO3D_BENCH_WARMUP" 2
    let sizes   = [ V2i(1024, 768); V2i(1920, 1080) ]

    say "[bench-map] OPC data at %s" opcDir
    if not (Directory.Exists opcDir) then
        say "[bench-map] ABORT: no Dimorphos OPC (set PRO3D_TEST_DATA or PRO3D_BENCH_OPC)"; 2
    else

    match Render.context.Value with
    | None -> say "[bench-map] ABORT: no GL runtime"; 2
    | Some (runtime, signature) ->

    Startup.init ()
    let hierarchies = MapSg.hierarchiesOf opcDir
    let maxR = MapSg.maxRadius hierarchies
    let outDir = Path.Combine(AppContext.BaseDirectory, "benchmark-output")
    Directory.CreateDirectory outDir |> ignore

    let results = Collections.Generic.List<string * V2i * float * float>()
    for size in sizes do
        for kind in [ MapProjectionKind.Equirectangular; MapProjectionKind.PolarNorth ] do
            let lods =
                [ "finest", MapSg.finestLod
                  "maplod", MapSg.mapLod (AVal.constant kind) (AVal.constant (Projection.viewProj kind Projection.defaultMaxColatitude V2d.Zero 1.0 size))
                                         (AVal.constant size) (AVal.constant Projection.defaultMaxColatitude) MapSg.defaultTargetPixels
                  "root", rootOnly ]
            for lodName, decider in lods do
                let name = sprintf "%A/%s" kind lodName
                say "[bench-map] %dx%d %s" size.X size.Y name
                let sw = Stopwatch.StartNew()
                let cfg = { OpcSg.defaultConfig signature (runtime.CreateLoadRunner 1) decider "DIMORPHOS" with asyncLoading = false }
                let view : MapSg.MapView =
                    {
                        kind          = AVal.constant kind
                        viewProj      = AVal.constant (Projection.viewProj kind Projection.defaultMaxColatitude V2d.Zero 1.0 size)
                        maxColatitude = AVal.constant Projection.defaultMaxColatitude
                        unitsPerPixel = AVal.constant 1.0
                        radiusRange   = AVal.constant (Projection.radiusRange maxR)
                    }
                let surface : MapSg.MapSurface =
                    { hierarchies = hierarchies; placement = AVal.constant Trafo3d.Identity; visible = AVal.constant true }
                let sg =
                    MapSg.surfaces cfg MapSg.surfaceEffects view (ASet.single surface)
                    |> Sg.viewTrafo (AVal.constant Trafo3d.Identity)
                    |> Sg.projTrafo (AVal.constant Trafo3d.Identity)
                use bench = new Bench(runtime, signature, sg, size, background)
                let settled = bench.Settle 200
                say "[bench-map]   loaded and settled in %.1f s" sw.Elapsed.TotalSeconds
                let m = bench.Measure(frames, repeats, warmup)
                m.image.Save(Path.Combine(outDir, sprintf "map-%dx%d-%A-%s.png" size.X size.Y kind lodName))
                let g, w = report name m
                let expected = expectedCoverage kind size
                check (covered settled > 0.98 * expected)
                    (sprintf "%s: the map is drawn (coverage %.3f of expected %.3f)" name (covered settled) expected)
                results.Add((name, size, g, w))

    // Annotation load: the non-public boulder catalog (~4,800 SBMT ellipses) when it is on this
    // machine (PRO3D_PRIVATE_TESTDATA/shapemodels/testdata). Measured only; no image is written.
    let catalog =
        TestUtils.Roots.privateDir [ "shapemodels"; "testdata" ]
        |> Option.map (fun d -> Path.Combine(d, "Dimo_Bould_Glob_7_Maurizio"))
        |> Option.filter File.Exists
    match catalog with
    | None -> say "[bench-map] no private boulder catalog: annotation arms skipped"
    | Some file ->
        let annotations = MapAnnotations.load "DIMORPHOS_SHM" file
        say "[bench-map] %d annotations from the private boulder catalog" annotations.Length
        let inputs = { MapAnnotations.none with annotations = MapAnnotations.ofList annotations }
        for size in sizes do
            for kind in [ MapProjectionKind.Equirectangular; MapProjectionKind.PolarNorth ] do
                let name = sprintf "%A/maplod+boulders" kind
                say "[bench-map] %dx%d %s" size.X size.Y name
                let viewProj = Projection.viewProj kind Projection.defaultMaxColatitude V2d.Zero 1.0 size
                let lod = MapSg.mapLod (AVal.constant kind) (AVal.constant viewProj) (AVal.constant size) (AVal.constant Projection.defaultMaxColatitude) MapSg.defaultTargetPixels
                let cfg = { OpcSg.defaultConfig signature (runtime.CreateLoadRunner 1) lod "DIMORPHOS" with asyncLoading = false }
                let view : MapSg.MapView =
                    {
                        kind          = AVal.constant kind
                        viewProj      = AVal.constant viewProj
                        maxColatitude = AVal.constant Projection.defaultMaxColatitude
                        unitsPerPixel = AVal.constant 1.0
                        radiusRange   = AVal.constant (Projection.radiusRange maxR)
                    }
                let surface : MapSg.MapSurface =
                    { hierarchies = hierarchies; placement = AVal.constant Trafo3d.Identity; visible = AVal.constant true }
                let sw = Stopwatch.StartNew()
                let sg =
                    MapAnnotations.mapWithAnnotations cfg view MapSg.MapMarkers.none (ASet.single surface) inputs
                    |> Sg.uniform "ViewportSize" (AVal.constant size)
                    |> Sg.viewTrafo (AVal.constant Trafo3d.Identity)
                    |> Sg.projTrafo (AVal.constant Trafo3d.Identity)
                use bench = new Bench(runtime, signature, sg, size, background)
                bench.Settle 200 |> ignore
                say "[bench-map]   packed and settled in %.1f s" sw.Elapsed.TotalSeconds
                let m = bench.Measure(frames, repeats, warmup)
                let g, w = report name m
                results.Add((name, size, g, w))

    say "[bench-map] summary (median ms/frame, gpu | wall):"
    for (name, size, g, w) in results do
        say "[bench-map]   %4dx%-4d %-28s %7.3f | %7.3f" size.X size.Y name g w
    failures
