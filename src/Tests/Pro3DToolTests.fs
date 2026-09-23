module Pro3DToolTests

open System
open System.Globalization
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

    let simulateImageDefaults : SimulateImageOptions =
        {
            opc = ""
            obj = null
            objScale = 1000.0
            objTexture = null
            time = ""
            mbi = null
            writeMbi = false
            out = ""
            instrument = "MILANI_ASPECT_NIR1"
            observer = "MILANI"
            body = "DIDYMOS"
            frame = "DIDYMOS_FIXED"
            kernel = null
            kernelRoot = null
            distance = 0.0
            width = 256
            height = 256
            albedo = 0.16
            // Opt-in here although the verb default is off: the Didymos fixture has no
            // DRACO layer, so this exercises the fit's graceful fallback to constant
            // albedo (a warning, not a failure).
            deshade = true
            deshadeLayer = "DRACO"
            microScale = 0.5
            microAmplitude = 0.3
            ambient = 0.02
            gain = 0.0
            noShadows = false
            noLighting = false
            textureLayer = null
            textureOnly = false
            // The de-shaded path is what these fixtures exercise (deshade = true above);
            // --texture-albedo is the other branch and would bypass it.
            textureAlbedo = false
            project = null
            projectShader = null
            // the shipped defaults, both swept against a SPICE ray-cast -- see
            // check-lighting.py. 4096 over a body this size is already finer than its
            // geometry, which is why raising it measured no better.
            shadowBias = 0.006
            shadowMap = 4096
            // No binary companion in the scene: these fixtures render Didymos, which IS
            // the primary, and an eclipse cast by its own moon is not what they test.
            occluderBody = null
            occluderFrame = null
            occluderOpc = null
            occluderObj = null
            occluderObjScale = 1000.0
            occluderTextureAlbedo = false
            occluderInScene = false
            // 'lookat', not the verb's 'ck' default: these fixtures render Didymos from
            // MILANI at epochs the CK does not necessarily cover, and the point of them is
            // the shading and the sidecar, not the attitude.
            pointing = "lookat"
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
            HeraSpiceTests.ensureKernelAt [ Path.Combine(HeraSpiceTests.mkDir, "hera_plan.tm"); HeraSpiceTests.spiceFileName ]

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

let private simulateImageTests =
    testList "simulate-image" [

        // The Didymos fixture has no DRACO layer, so this also exercises the de-shading
        // fit's graceful fallback to constant albedo (a warning, not a failure). The epoch
        // is taken from the ASPECT image sidecar, which hera_plan.tm is known to cover --
        // the same trick keeps the sun-angles test independent of kernel versions.
        test "renders a deterministic simulated image with coverage and detail" {
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

            HeraSpiceTests.ensureKernelAt [ Path.Combine(HeraSpiceTests.mkDir, "hera_plan.tm"); HeraSpiceTests.spiceFileName ]

            let outDir = Path.Combine(Path.GetTempPath(), "pro3d-tool-tests", Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory outDir |> ignore
            try
                let hierarchies =
                    Directory.GetDirectories opc
                    |> Array.filter (fun d -> Directory.Exists(Path.Combine(d, "Patches")))
                Expect.isNonEmpty hierarchies "the Didymos OPC has a patch hierarchy"

                let o = { Fixtures.simulateImageDefaults with opc = opc }
                let time = img.mbi.obs_date

                // Through the same resolver the verb uses, so the test exercises the OPC
                // branch of it rather than a hand-built shape only the test has.
                let shape =
                    match SimulateImageVerb.ShapeSource.resolve runtime "DIDYMOS" opc null 1000.0 null None with
                    | Result.Error e -> failtest e
                    | Result.Ok s -> s

                let render (name : string) =
                    let path = Path.Combine(outDir, name)
                    match SimulateImageVerb.processImage runtime o "DIDYMOS" "DIDYMOS_FIXED" "MILANI"
                              "MILANI_ASPECT_NIR1" time path HeraSpiceTests.spiceFileName shape None with
                    | Result.Error e -> failtest e
                    | Result.Ok written ->
                        Expect.isTrue (File.Exists written) "PNG written"
                        written

                let first = render "sim1.png"

                let pix = PixImage.Load(first).ToPixImage<byte>(Col.Format.Gray)
                Expect.equal pix.Size (V2i(256, 256)) "requested output size"

                let data = pix.Volume.Data
                let dark = data |> Array.filter (fun v -> v < 8uy)
                let lit = data |> Array.filter (fun v -> v >= 8uy)
                // Partial coverage: the body does not fill the frame, and space must be
                // black rather than some plausible grey.
                Expect.isGreaterThan lit.Length 0 "the body appears in the frame"
                Expect.isGreaterThan dark.Length 0 "space stays black"
                // Auto-exposure anchors the 99.5th percentile at DN 245, so a sane render
                // reaches high DNs...
                Expect.isGreaterThan (int (Array.max data)) 200 "auto-exposure reaches high DNs"
                // ...and shading over topography produces many distinct values, which a
                // flat disk (dead sun direction, dead normals) would not.
                let distinct = data |> Array.distinct
                Expect.isGreaterThan distinct.Length 32 "the disk is shaded, not flat"

                // Deterministic: blocking loads, fixed warm-up, deterministic auto-gain.
                let second = render "sim2.png"
                Expect.equal (File.ReadAllBytes second) (File.ReadAllBytes first)
                    "two renders of the same epoch are bit-identical"
            finally
                try Directory.Delete(outDir, true) with _ -> ()
        }
    ]

/// The OBJ reader, on meshes small enough to reason about by hand.
///
/// No GPU and no kernels: this is a parser, and the things it can get wrong -- units,
/// winding, the V axis, 1-based and negative indices, polygon fans -- all show up as wrong
/// numbers long before they show up as a wrong picture. The one that motivated writing
/// them down is the scale: the shape models the SPICE kernels ship are in KILOMETRES, and
/// a factor of 1000 renders a body a few pixels across that every downstream complaint
/// then blames on the pointing.
module private ObjFixtures =

    /// A unit cube, ±1 in file units, every face wound outward.
    let cube =
        String.concat "\n" [
            "# a cube"
            "v -1 -1 -1"; "v 1 -1 -1"; "v 1 1 -1"; "v -1 1 -1"
            "v -1 -1 1";  "v 1 -1 1";  "v 1 1 1";  "v -1 1 1"
            "f 1 4 3"; "f 1 3 2"          // -Z
            "f 5 6 7"; "f 5 7 8"          // +Z
            "f 1 2 6"; "f 1 6 5"          // -Y
            "f 2 3 7"; "f 2 7 6"          // +X
            "f 3 4 8"; "f 3 8 7"          // +Y
            "f 4 1 5"; "f 4 5 8"          // -X
            ""
        ]

    /// Same cube with every triangle reversed, i.e. wound inward.
    let flippedCube =
        cube.Split('\n')
        |> Array.map (fun l ->
            match l.Split(' ') with
            | [| "f"; a; b; c |] -> sprintf "f %s %s %s" a c b
            | _ -> l)
        |> String.concat "\n"

    let write (dir : string) (name : string) (text : string) =
        let p = Path.Combine(dir, name)
        File.WriteAllText(p, text)
        p

    let writeGz (dir : string) (name : string) (text : string) =
        let p = Path.Combine(dir, name)
        use fs = File.Create p
        use gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Compress)
        let bytes = Text.Encoding.ASCII.GetBytes text
        gz.Write(bytes, 0, bytes.Length)
        p

    /// A UV sphere the size of Dimorphos, in KILOMETRES so `--obj-scale 1000` applies, with
    /// an equirectangular texture baked as if lit from `light`. Returns (obj, texture).
    ///
    /// A sphere rather than a cube because the fit solves for a direction from the spread
    /// of surface normals, and six of them determine nothing.
    let litSphere (dir : string) (light : V3d) =
        let radius = 0.088
        let nu, nv = 64, 32
        let sb = Text.StringBuilder()
        let vertex i j =
            let theta = float j / float nv * Constant.Pi
            let phi = float i / float nu * Constant.PiTimesTwo
            V3d(radius * sin theta * cos phi, radius * sin theta * sin phi, radius * cos theta)
        for j in 0 .. nv do
            for i in 0 .. nu do
                let p = vertex i j
                sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:R} {1:R} {2:R}\n", p.X, p.Y, p.Z) |> ignore
        for j in 0 .. nv do
            for i in 0 .. nu do
                // OBJ counts V up from the bottom, so the north pole (j = 0) is v = 1
                sb.AppendFormat(CultureInfo.InvariantCulture, "vt {0:R} {1:R}\n",
                                float i / float nu, 1.0 - float j / float nv) |> ignore
        let index i j = j * (nu + 1) + i + 1        // 1-based
        for j in 0 .. nv - 1 do
            for i in 0 .. nu - 1 do
                let a, b = index i j, index (i + 1) j
                let c, d = index (i + 1) (j + 1), index i (j + 1)
                // wound outward, which the reader's winding vote must agree with
                sb.AppendFormat("f {0}/{0} {1}/{1} {2}/{2}\n", a, d, c) |> ignore
                sb.AppendFormat("f {0}/{0} {1}/{1} {2}/{2}\n", a, c, b) |> ignore
        let objPath = Path.Combine(dir, "sphere.obj")
        File.WriteAllText(objPath, sb.ToString())

        // equirectangular, row 0 = the north pole = v 1
        let w, h = 256, 128
        let tex = PixImage<byte>(Col.Format.Gray, V2i(w, h))
        let mutable m = tex.GetChannel Col.Channel.Gray
        for y in 0 .. h - 1 do
            let theta = (float y + 0.5) / float h * Constant.Pi
            for x in 0 .. w - 1 do
                let phi = (float x + 0.5) / float w * Constant.PiTimesTwo
                let n = V3d(sin theta * cos phi, sin theta * sin phi, cos theta)
                m.[x, y] <- byte (255.0 * max 0.0 (Vec.dot n light))
        let texPath = Path.Combine(dir, "sphere.png")
        tex.Save texPath
        objPath, texPath

    /// A scratch directory that cleans itself up.
    let inScratch (f : string -> unit) =
        let dir = Path.Combine(Path.GetTempPath(), "pro3d-obj-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory dir |> ignore
        try f dir
        finally try Directory.Delete(dir, true) with _ -> ()

let private objShapeTests =
    testList "obj" [

        test "reads a cube, scales its units to metres and sees the winding" {
            ObjFixtures.inScratch (fun dir ->
                let path = ObjFixtures.write dir "cube.obj" ObjFixtures.cube
                match ObjShape.read path 1000.0 with
                | Result.Error e -> failtest e
                | Result.Ok m ->
                    Expect.equal m.positions.Length 8 "vertices"
                    Expect.equal (m.index.Length / 3) 12 "triangles"
                    Expect.isEmpty m.texCoords "no vt in this file"
                    // ±1 file unit at 1000 m per unit is a 2 km cube
                    Expect.equal m.bbox.Size (V3d(2000.0, 2000.0, 2000.0)) "extent in metres"
                    Expect.equal m.sourceExtent (V3d(2.0, 2.0, 2.0)) "extent in the file's own units"
                    Expect.equal m.normalFlip 0.0 "outward-wound: the shader must not flip")
        }

        test "an inward-wound mesh asks the shader to flip the generated normal" {
            ObjFixtures.inScratch (fun dir ->
                let path = ObjFixtures.write dir "flipped.obj" ObjFixtures.flippedCube
                match ObjShape.read path 1.0 with
                | Result.Error e -> failtest e
                | Result.Ok m -> Expect.equal m.normalFlip 1.0 "inward-wound")
        }

        test "--obj-scale is metres per file unit" {
            ObjFixtures.inScratch (fun dir ->
                let path = ObjFixtures.write dir "cube.obj" ObjFixtures.cube
                match ObjShape.read path 1.0, ObjShape.read path 1000.0 with
                | Result.Ok a, Result.Ok b ->
                    Expect.equal a.bbox.Size (V3d(2.0, 2.0, 2.0)) "unscaled"
                    Expect.equal b.bbox.Size (V3d(2000.0, 2000.0, 2000.0)) "x1000"
                | Result.Error e, _ | _, Result.Error e -> failtest e)
        }

        test "negative indices count back from the vertices defined so far" {
            ObjFixtures.inScratch (fun dir ->
                let text = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf -3 -2 -1\n"
                let path = ObjFixtures.write dir "neg.obj" text
                match ObjShape.read path 1.0 with
                | Result.Error e -> failtest e
                | Result.Ok m ->
                    Expect.equal m.index [| 0; 1; 2 |] "resolved to the first three vertices")
        }

        test "a polygon is fan-triangulated" {
            ObjFixtures.inScratch (fun dir ->
                let text = "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n"
                let path = ObjFixtures.write dir "quad.obj" text
                match ObjShape.read path 1.0 with
                | Result.Error e -> failtest e
                | Result.Ok m ->
                    Expect.equal (m.index.Length / 3) 2 "a quad is two triangles"
                    Expect.equal m.index [| 0; 1; 2; 0; 2; 3 |] "fanned from the first corner")
        }

        test "texture coordinates arrive with the V axis flipped, and corners de-duplicate" {
            ObjFixtures.inScratch (fun dir ->
                // three positions, but the same position carries two different UVs --
                // which is exactly the case a shared index array cannot represent
                let text =
                    "v 0 0 0\nv 1 0 0\nv 0 1 0\n\
                     vt 0 0\nvt 1 0\nvt 0 1\nvt 1 1\n\
                     f 1/1 2/2 3/3\nf 1/4 2/2 3/3\n"
                let path = ObjFixtures.write dir "uv.obj" text
                match ObjShape.read path 1.0 with
                | Result.Error e -> failtest e
                | Result.Ok m ->
                    // 3 corners for the first face + 1 more for vertex 1's second UV
                    Expect.equal m.positions.Length 4 "one slot per (position, uv) pair"
                    Expect.equal m.texCoords.Length 4 "one uv per slot"
                    Expect.equal (m.index.Length / 3) 2 "triangles"
                    // OBJ counts V up from the bottom; the sampler counts it down
                    Expect.equal m.texCoords.[0] (V2f(0.0f, 1.0f)) "vt 0 0 -> (0, 1)"
                    Expect.equal m.texCoords.[2] (V2f(0.0f, 0.0f)) "vt 0 1 -> (0, 0)")
        }

        test "a gzipped OBJ reads exactly like the plain one" {
            ObjFixtures.inScratch (fun dir ->
                let plain = ObjFixtures.write dir "cube.obj" ObjFixtures.cube
                let gz = ObjFixtures.writeGz dir "cube2.obj.gz" ObjFixtures.cube
                match ObjShape.read plain 1000.0, ObjShape.read gz 1000.0 with
                | Result.Ok a, Result.Ok b ->
                    Expect.equal b.positions a.positions "positions"
                    Expect.equal b.index a.index "indices"
                    Expect.equal b.bbox a.bbox "bounds"
                    Expect.equal b.normalFlip a.normalFlip "winding"
                | Result.Error e, _ | _, Result.Error e -> failtest e)
        }

        test "a file with no geometry is an error, not an empty mesh" {
            ObjFixtures.inScratch (fun dir ->
                let noFaces = ObjFixtures.write dir "points.obj" "v 0 0 0\nv 1 0 0\n"
                Expect.isError (ObjShape.read noFaces 1.0) "vertices but no faces"
                Expect.isError (ObjShape.read (Path.Combine(dir, "nope.obj")) 1.0) "missing file"
                let empty = ObjFixtures.write dir "empty.obj" "# nothing here\n"
                Expect.isError (ObjShape.read empty 1.0) "no vertices")
        }

        // The mesh half of the de-shading fit, end to end on the CPU: texture coordinates,
        // the V flip, the texture sampling and the area-weighted vertex normals all feed
        // `fitFromSamples`, and every one of them can be wrong in a way that still produces
        // a plausible-looking number. Baking a KNOWN light direction into the texture is
        // what turns that into a checkable claim.
        test "the de-shading fit recovers a light direction baked into a mesh's texture" {
            ObjFixtures.inScratch (fun dir ->
                let light = V3d(0.6, -0.8, 0.0).Normalized
                let objPath, texPath = ObjFixtures.litSphere dir light
                match ObjShape.read objPath 1000.0 with
                | Result.Error e -> failtest e
                | Result.Ok mesh ->
                    Expect.isNonEmpty mesh.texCoords "the sphere carries texture coordinates"
                    match ObjShape.deshadeSamples mesh texPath with
                    | Result.Error e -> failtest e
                    | Result.Ok samples ->
                        match SimulateImageVerb.fitFromSamples 0.16 samples with
                        | Result.Error e -> failtest e
                        | Result.Ok fit ->
                            let off =
                                acos (clamp -1.0 1.0 (Vec.dot fit.direction light))
                                * Constant.DegreesPerRadian
                            Expect.isLessThan off 2.0
                                (sprintf "fitted %A is %.2f deg off the baked %A"
                                     fit.direction off light)
                            Expect.isGreaterThan fit.correlation 0.95 "the fit explains the texture")
        }

        // The eclipse occluder's fallback geometry. It is a shape model like any other so
        // that the coarse case and the real primary go through the SAME depth pass; if the
        // tessellation were wrong the only symptom would be a slightly wrong shadow, which
        // is exactly the kind of thing nobody notices.
        test "the reference radii tessellate into an outward-wound ellipsoid" {
            let radii = V3d(409.5, 400.5, 303.5)     // Didymos, from the kernel pool
            let m = ObjShape.ellipsoid radii 64
            Expect.equal m.normalFlip 0.0 "outward-wound, like the shape models"
            Expect.isGreaterThan (m.index.Length / 3) 1000 "tessellated finely enough to be round"
            // the bbox is exact by construction; the vertices have to actually reach it
            Expect.equal m.bbox (Box3d(-radii, radii)) "bounds are the radii"
            let extent =
                m.positions
                |> Array.fold (fun (b : Box3d) p -> b.ExtendedBy(V3d p)) Box3d.Invalid
            Expect.isLessThan (extent.Min - m.bbox.Min).Length 0.01 "vertices reach the minimum"
            Expect.isLessThan (extent.Max - m.bbox.Max).Length 0.01 "vertices reach the maximum"
            // every vertex on the ellipsoid: (x/a)^2 + (y/b)^2 + (z/c)^2 = 1
            let worst =
                m.positions
                |> Array.map (fun p ->
                    let v = V3d p / radii
                    abs (v.LengthSquared - 1.0))
                |> Array.max
            Expect.isLessThan worst 1e-6 "every vertex lies on the ellipsoid"
        }

        test "the occluder falls back to the radii and reads a mesh when given one" {
            let radii = V3d(409.5, 400.5, 303.5)
            match SimulateImageVerb.ShapeSource.occluder null "DIDYMOS" null null 1000.0 radii with
            | Result.Error e -> failtest e
            | Result.Ok s ->
                Expect.equal s.bbox (Box3d(-radii, radii)) "the tessellated radii"
                Expect.isFalse s.hasTexture "a shadow caster needs none"
            ObjFixtures.inScratch (fun dir ->
                let path = ObjFixtures.write dir "cube.obj" ObjFixtures.cube
                match SimulateImageVerb.ShapeSource.occluder null "DIDYMOS" null path 1000.0 radii with
                | Result.Error e -> failtest e
                | Result.Ok s ->
                    Expect.equal s.bbox.Size (V3d(2000.0, 2000.0, 2000.0))
                        "the mesh, not the radii")
            Expect.isError (SimulateImageVerb.ShapeSource.occluder null "DIDYMOS" null "Z:/no-such.obj" 1000.0 radii)
                "a missing occluder mesh is an error, not a silent fallback to the radii"
            // two shape models of one body have no precedence rule worth inventing, the
            // same as --opc against --obj for the target
            Expect.isError
                (SimulateImageVerb.ShapeSource.occluder null "DIDYMOS" "Z:/some.opc" "Z:/some.obj" 1000.0 radii)
                "--occluder-opc and --occluder-obj together are refused"
        }

        test "an untextured shape reports no mean brightness, so the textured modes fall back" {
            let radii = V3d(409.5, 400.5, 303.5)
            match SimulateImageVerb.ShapeSource.occluder null "DIDYMOS" null null 1000.0 radii with
            | Result.Error e -> failtest e
            | Result.Ok s ->
                Expect.isNone (s.meanTextureBrightness ())
                    "the tessellated radii carry no texture to expose against"
            // and a real image does have one, in 0..1
            ObjFixtures.inScratch (fun dir ->
                let tex = Path.Combine(dir, "grey.png")
                let pi = PixImage<byte>(Col.Format.Gray, V2i(8, 8))
                pi.GetChannel(0L).Set(128uy) |> ignore
                pi.SaveAsPng tex
                match SimulateImageVerb.meanBrightnessOf tex with
                | None -> failtest "a readable image has a mean"
                | Some m ->
                    Expect.floatClose Accuracy.medium m (128.0 / 255.0)
                        "the mean is the texel value, normalised to 0..1")
        }

        test "a non-positive scale is refused rather than rendering an inverted body" {
            ObjFixtures.inScratch (fun dir ->
                let path = ObjFixtures.write dir "cube.obj" ObjFixtures.cube
                Expect.isError (ObjShape.read path 0.0) "zero"
                Expect.isError (ObjShape.read path -1.0) "negative"
                Expect.isError (ObjShape.read path nan) "not a number")
        }
    ]

let tests () =
    testList "pro3d-tool" [
        kdTreeTests
        sunAngleTests
        objShapeTests
        simulateImageTests
    ]
