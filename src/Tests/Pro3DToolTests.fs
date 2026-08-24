module Pro3DToolTests

open System
open System.IO

open Expecto

open Aardvark.Base
open Aardvark.PixImage.LibTiff

open PRo3D.Core
open PRo3D.ImageMapping
open PRo3D.Tool

/// Tests for the `pro3d-tool` verbs, exercised in-process against the real fixtures.
///
/// The sun-angles case deliberately does NOT call `SunAnglesVerb.run`: that owns the SPICE
/// lifetime (SpiceBoot.init .. Dispose), and every Init/DeInit is a kernel swap. There is no
/// working unload -- the native DeInit never calls kclear_c -- so kernels accumulate and
/// repeated swaps leave stale DAF handles (SPICE(DAFNOSUCHHANDLE)), which is why the whole
/// suite is testSequenced. It goes through HeraSpiceTests.ensureKernelAt instead, which swaps
/// only when the active kernel actually differs, then calls SunAnglesVerb.processImage
/// directly. Net cost to the suite: no extra swaps.
module private Fixtures =

    let testData =
        [
            Environment.GetEnvironmentVariable "PRO3D_TESTDATA"
            Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "PRo3D.Resources.TestData")
        ]
        |> List.tryFind (fun p -> not (String.IsNullOrWhiteSpace p) && Directory.Exists p)
        |> Option.map Path.GetFullPath

    let private under (rel : string) =
        testData
        |> Option.map (fun root -> Path.Combine(root, rel))
        |> Option.filter Directory.Exists

    /// MSL/Stimson OPC -- kdtree fixture. Needs no GPU and no kernels.
    let mslOpc = under "1087_004779_MSLMST_0011"

    /// Didymos OPC and the ASPECT frame -- sun-angles fixtures.
    let didymosOpc = under (Path.Combine("HERA", "Didymos_ASPECT"))
    let aspectImages = under (Path.Combine("HERA", "Instrument Data"))

    let kdTreeDefaults : KdTreeOptions =
        {
            verbose = false
            forcekdtreerebuild = false
            ignoreMasterKdTree = false
            generatedds = false
            skipPatchValidation = false
            overwritedds = false
            degreesOfParallelism = 0
            surfaceDirectory = ""
        }

    let sunAngleDefaults : SunAnglesOptions =
        {
            opc = ""
            images = ""
            image = null
            out = ""
            body = "DIDYMOS"
            frame = "DIDYMOS_FIXED"
            observer = "MILANI"
            kernel = null
            kernelRoot = null
            method = "mbi"
            falseColor = false
            width = 0
            height = 0
        }

    /// A scratch copy: --forcekdtreerebuild rewrites .aakd files in place, and the test data
    /// is a git checkout.
    let copyToTemp (source : string) =
        let root = Path.Combine(Path.GetTempPath(), "pro3d-tool-tests", Guid.NewGuid().ToString("N"))
        let target = Path.Combine(root, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)))
        let rec copyDir (s : string) (t : string) =
            Directory.CreateDirectory t |> ignore
            for f in Directory.GetFiles s do File.Copy(f, Path.Combine(t, Path.GetFileName f), true)
            for d in Directory.GetDirectories s do copyDir d (Path.Combine(t, Path.GetFileName d))
        copyDir source target
        root, target

let private kdTreeTests =
    testList "kdtree" [

        test "a missing surface directory is not success" {
            let code =
                KdTree.run { Fixtures.kdTreeDefaults with surfaceDirectory = Path.Combine("Z:", "no-such-opc") }
            Expect.notEqual code 0 "exit code"
        }

        test "validates the OPC fixture" {
            match Fixtures.mslOpc with
            | None -> skiptest "no OPC test data (set PRO3D_TESTDATA)"
            | Some dir ->
                Expect.equal (KdTree.run { Fixtures.kdTreeDefaults with surfaceDirectory = dir }) 0 "exit code"
        }

        test "forcekdtreerebuild rewrites the kd-trees" {
            match Fixtures.mslOpc with
            | None -> skiptest "no OPC test data (set PRO3D_TESTDATA)"
            | Some dir ->
                let root, work = Fixtures.copyToTemp dir
                try
                    let stamps () =
                        Directory.GetFiles(work, "*.aakd", SearchOption.AllDirectories)
                        |> Array.sort
                        |> Array.map (fun f -> f, File.GetLastWriteTimeUtc f)

                    let before = stamps ()
                    Expect.isNonEmpty before "the fixture ships kd-trees to rebuild"

                    let code =
                        KdTree.run { Fixtures.kdTreeDefaults with surfaceDirectory = work; forcekdtreerebuild = true }
                    Expect.equal code 0 "exit code"

                    let after = stamps ()
                    Expect.equal after.Length before.Length "same set of kd-tree files"
                    Expect.notEqual after before "kd-trees were rewritten"
                finally
                    try Directory.Delete(root, true) with _ -> ()
        }
    ]

let private sunAngleTests =
    testList "sun-angles" [

        test "writes float32 rasters pixel-aligned to the source image" {
            if not HeraSpiceTests.hasHera then
                skiptest "HERA spice kernels unavailable (or --skip-hera)"

            match Fixtures.didymosOpc, Fixtures.aspectImages with
            | None, _ | _, None -> skiptest "no Didymos/ASPECT test data (set PRO3D_TESTDATA)"
            | Some opc, Some images ->

            match PRo3D.Tests.Render.context.Value with
            | None -> skiptest "no OpenGL runtime in this environment"
            | Some (runtime, _) ->

            match InstrumentObservation.resolveImage images None with
            | Result.Error e -> skiptest (sprintf "no resolvable ASPECT image: %s" e)
            | Result.Ok img ->

            // The ASPECT sidecar names a planning kernel; ops does not cover the epoch.
            // Going through ensureKernelAt reuses the suite's tracking, so this is a no-op
            // when that kernel is already active.
            let mkDir = Path.GetDirectoryName HeraSpiceTests.spiceFileName
            HeraSpiceTests.ensureKernelAt [ Path.Combine(mkDir, "hera_plan.tm"); HeraSpiceTests.spiceFileName ]

            let outDir = Path.Combine(Path.GetTempPath(), "pro3d-tool-tests", Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory outDir |> ignore
            try
                let hierarchies =
                    Directory.GetDirectories opc
                    |> Array.filter (fun d -> Directory.Exists(Path.Combine(d, "Patches")))
                Expect.isNonEmpty hierarchies "the Didymos OPC has a patch hierarchy"

                let o = { Fixtures.sunAngleDefaults with opc = opc; images = images; out = outDir }

                match SunAnglesVerb.processImage runtime o "DIDYMOS" "DIDYMOS_FIXED" "MILANI"
                          ProjectionMethod.MbiBased outDir "hera_plan.tm" hierarchies img with
                | Result.Error e -> failtest e
                | Result.Ok _ ->

                let expected =
                    match img.size with
                    | Some s -> s
                    | None -> failtest "the ASPECT sidecar declares no image size"

                let stem = Path.GetFileNameWithoutExtension img.path

                let read (band : string) =
                    let path = Path.Combine(outDir, sprintf "%s_%s.tif" stem band)
                    Expect.isTrue (File.Exists path) (sprintf "%s raster written" band)
                    match MultiBandReader.tryReadMultiBandTiff path false with
                    | Result.Error e -> failtest (sprintf "%s: %s" band e)
                    | Result.Ok r -> r

                let values (r : TiffReadResult) =
                    match r.buffers with
                    | Float32Bands b -> b.[0]
                    | other -> failtest (sprintf "expected float32 bands, got %A" other)

                let bands = [ "incidence"; "emission"; "phase" ] |> List.map (fun b -> b, read b)

                for (name, r) in bands do
                    Expect.equal (r.width, r.height) (expected.X, expected.Y)
                        (sprintf "%s is pixel-aligned to the source image" name)
                    Expect.equal r.bands 1 (sprintf "%s is single-band" name)
                    Expect.equal r.format Format.Float32 (sprintf "%s is float32" name)

                    let all = values r
                    let good = all |> Array.filter (Single.IsNaN >> not)
                    // Partial coverage: the body does not fill the frame, and everything off
                    // it must read as nodata rather than a plausible zero.
                    Expect.isGreaterThan good.Length 0 (sprintf "%s has data" name)
                    Expect.isLessThan good.Length all.Length (sprintf "%s has NaN nodata off the body" name)
                    Expect.isTrue (good |> Array.forall (fun v -> v >= 0.0f && v <= float32 Math.PI))
                        (sprintf "%s values are radians within [0, pi]" name)

                // Catches a channel mix-up, which the per-band checks above would all pass.
                Expect.notEqual (values (snd bands.[0])) (values (snd bands.[1]))
                    "incidence and emission are distinct rasters"

                let sidecar = Path.Combine(outDir, sprintf "%s_angles.json" stem)
                Expect.isTrue (File.Exists sidecar) "provenance sidecar written"
                let json = File.ReadAllText sidecar
                Expect.stringContains json "radians" "sidecar records units"
                Expect.stringContains json "DIDYMOS" "sidecar records the body"
            finally
                try Directory.Delete(outDir, true) with _ -> ()
        }
    ]

let tests () =
    testList "pro3d-tool" [
        kdTreeTests
        sunAngleTests
    ]
