module UnprojectTest

open System
open System.IO
open System.Globalization

open Expecto

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.ImageMapping
open PRo3D.Tool

/// Tests for turning an image pixel into a body-fixed surface coordinate.
///
/// How an incoming coordinate file addresses its pixels is DEFINITIONAL -- a property of
/// whatever produced the centroids, recorded nowhere in the imagery or its metadata. It is
/// declared by the caller via --pixel-convention, never inferred. The first tests pin the
/// declaration down so a refactor cannot quietly change what the flag means.
///
/// The last test checks the rest of the chain against ground truth that does not come from
/// this code at all -- see its own comment.

module private Synthetic =

    /// A camera with no SPICE involved, for testing the pixel algebra on its own.
    let camera (size : V2i) : ProjectorCamera =
        let near, far = 1.0, 1000.0
        let frustum = Frustum.perspective 30.0 near far (float size.X / float size.Y)
        let view = CameraView.lookAt (V3d(0.0, 0.0, 100.0)) V3d.Zero V3d.OIO |> CameraView.viewTrafo
        let proj = Frustum.projTrafo frustum
        { view = view; proj = proj; full = view * proj; distance = 100.0; near = near; far = far }

let private addressingTests =
    let image = InstrumentObservation.PixelConvention.Image
    let fits = InstrumentObservation.PixelConvention.Fits
    let size = V2i(4, 4)

    testList "addressing" [

        // The half-texel question, pinned. Pixel 0 is a pixel, and its centre sits half a
        // pixel inside the image, not on the image's outer edge.
        test "pixel 0 addresses the centre of the first pixel, not the image corner" {
            let ndc = InstrumentObservation.pixelToNdc size image (V2d(0.0, 0.0))
            Expect.floatClose Accuracy.high ndc.X -0.75 "first column centre in a 4-wide image"
            Expect.floatClose Accuracy.high ndc.Y 0.75 "first row centre in a 4-high image"
            Expect.isGreaterThan ndc.X -1.0
                "pixel 0 must not land on the image edge -- that is the half-texel error"
        }

        test "the centre of an even-sized image is the NDC origin" {
            let ndc = InstrumentObservation.pixelToNdc size image (V2d(1.5, 1.5))
            Expect.floatClose Accuracy.high ndc.X 0.0 "x"
            Expect.floatClose Accuracy.high ndc.Y 0.0 "y"
        }

        test "the last pixel is symmetric with the first" {
            let first = InstrumentObservation.pixelToNdc size image (V2d(0.0, 0.0))
            let last = InstrumentObservation.pixelToNdc size image (V2d(3.0, 3.0))
            Expect.floatClose Accuracy.high last.X -first.X "x is mirrored"
            Expect.floatClose Accuracy.high last.Y -first.Y "y is mirrored"
        }

        // fits and image are the same physical pixels counted differently: FITS starts at 1
        // and counts rows upwards from the bottom, so its (1,1) is the image convention's
        // bottom-left pixel.
        test "fits (1,1) is the same point as image (0, height-1)" {
            let a = InstrumentObservation.pixelToNdc size fits (V2d(1.0, 1.0))
            let b = InstrumentObservation.pixelToNdc size image (V2d(0.0, float (size.Y - 1)))
            Expect.floatClose Accuracy.high a.X b.X "x"
            Expect.floatClose Accuracy.high a.Y b.Y "y"
        }

        // FITS pixel coordinates are REAL numbers, not indices: the FITS Standard (4.0, section 8)
        // and WCS Paper I (Greisen & Calabretta 2002, A&A 395, 1061, section 2.1.2) define the
        // centre of the first pixel as 1.0, with pixel k covering [k-0.5, k+0.5]. CRPIX being a
        // floating-point keyword in that same system is the clearest evidence -- CRPIX1 = 320.5
        // is legal and means the boundary between pixels 320 and 321. So a sub-pixel centroid
        // needs no special handling, and nothing here may round or floor.
        test "pixel coordinates are continuous, not an integer grid" {
            for conv in [ image; fits ] do
                let b = float conv.baseIndex
                let atFirst = InstrumentObservation.pixelToNdc size conv (V2d(b, b))
                let atSecond = InstrumentObservation.pixelToNdc size conv (V2d(b + 1.0, b))
                let halfway = InstrumentObservation.pixelToNdc size conv (V2d(b + 0.5, b))
                Expect.floatClose Accuracy.veryHigh halfway.X ((atFirst.X + atSecond.X) / 2.0)
                    "a coordinate halfway between two pixel centres must map halfway between them"

                // and the mapping is strictly linear over a fractional sweep
                for t in [ 0.0; 0.13; 0.5; 0.87; 1.0; 2.4 ] do
                    let got = InstrumentObservation.pixelToNdc size conv (V2d(b + t, b))
                    let expected = atFirst.X + t * (atSecond.X - atFirst.X)
                    Expect.floatClose Accuracy.veryHigh got.X expected
                        (sprintf "fractional coordinate %g must not be rounded" t)
        }

        test "pixel and ndc round trip" {
            for conv in [ image; fits ] do
                for p in [ V2d(0.0, 0.0); V2d(1.7, 2.3); V2d(3.0, 3.0) ] do
                    let p = V2d(p.X + float conv.baseIndex, p.Y + float conv.baseIndex)
                    let back = InstrumentObservation.ndcToPixel size conv (InstrumentObservation.pixelToNdc size conv p)
                    Expect.floatClose Accuracy.veryHigh back.X p.X "x"
                    Expect.floatClose Accuracy.veryHigh back.Y p.Y "y"
        }

        test "pixelRay and projectToPixel are inverse" {
            let size = V2i(640, 512)
            let cam = Synthetic.camera size
            for p in [ V2d(0.0, 0.0); V2d(319.5, 255.5); V2d(123.25, 400.75); V2d(639.0, 511.0) ] do
                let ray = InstrumentObservation.pixelRay cam size image p
                let world = ray.GetPointOnRay 50.0
                match InstrumentObservation.projectToPixel cam size image world with
                | None -> failtestf "projectToPixel returned None for %A" p
                | Some back ->
                    Expect.floatClose Accuracy.medium back.X p.X "x"
                    Expect.floatClose Accuracy.medium back.Y p.Y "y"
        }

    ]

// ---------------------------------------------------------------------------------------
// end to end, against the shape model's own idea of where its vertices are
// ---------------------------------------------------------------------------------------

module private Data =

    /// Walk up from the test sources, checking each ancestor: the repository may be checked
    /// out as a git worktree, where a fixed number of `..` steps lands somewhere else.
    let private findUpwards (relative : string) =
        let rec go (dir : DirectoryInfo) =
            if isNull dir then None
            else
                let candidate = Path.Combine(dir.FullName, relative)
                if Directory.Exists candidate then Some candidate else go dir.Parent
        go (DirectoryInfo __SOURCE_DIRECTORY__)

    let private firstExisting (candidates : string list) =
        candidates |> List.tryFind (fun p -> not (String.IsNullOrWhiteSpace p) && Directory.Exists p)

    /// Instrument images, from the public test-data clone.
    let instrumentImages () =
        firstExisting [ Environment.GetEnvironmentVariable "PRO3D_TEST_DATA"
                        defaultArg (findUpwards "PRo3D.Resources.TestData") null ]
        |> Option.map (fun r -> Path.Combine(r, "HERA", "Instrument Data"))
        |> Option.filter Directory.Exists

    /// An OPC carrying per-vertex `*.aara` layers. Same variable the profile extraction tests
    /// use, e.g. the HERA AARA_Textures export's Dimorphos folder.
    let aaraOpc () =
        firstExisting [ Environment.GetEnvironmentVariable "PRO3D_AARA_OPC" ]

    /// SPICE kernel root, resolved as pro3d-tool resolves it: either a clone of the ESA hera
    /// kernel repository or its `kernels` subdirectory.
    let kernelRoot () =
        let asRoot (root : string) =
            if String.IsNullOrWhiteSpace root then None
            elif Directory.Exists(Path.Combine(root, "mk")) then Some root
            elif Directory.Exists(Path.Combine(root, "kernels", "mk")) then Some (Path.Combine(root, "kernels"))
            else None
        [ Environment.GetEnvironmentVariable "PRO3D_SPICE_KERNELS"
          defaultArg (findUpwards "spice") null ]
        |> List.tryPick (fun p -> if isNull p then None else asRoot p)

    let aspectImage = "ASP_000000_270323T060000_2B_NIR1_0.tif"

/// Difference between two angles in degrees, the short way round the circle. The layer's
/// longitude is interpolated across the 0/360 seam and can read past 360.
let private angleDelta (a : float) (b : float) =
    abs (((a - b) % 360.0 + 540.0) % 360.0 - 180.0)

let private crossCheck =
    testList "against the shape model" [

        // Why this is the test worth having.
        //
        // `LonLatRad` is a per-vertex layer baked into the OPC when the shape model was
        // exported. Nothing in this chain reads it: the lat/lon/alt it is compared against
        // come from unprojecting a pixel, intersecting the kd-trees, and running the hit
        // point through CooTransformation. The two sides share no step, so an error anywhere
        // -- the pixel algebra, the projection matrix, the intersection, the barycentric
        // attribute sampling, the coordinate conversion -- breaks the agreement. A mirrored
        // pixel convention shows up as tens of degrees.
        //
        // Units: the layer stores gradians, longitude x 10/9 and (latitude + 90) x 10/9, with
        // only the radius in metres. See docs/VertexAttributes.md.
        test "lat/lon/alt agree with the OPC's own per-vertex LonLatRad layer" {
            // --skip-hera must stay deterministic: this test swaps the active SPICE kernel, so it
            // has to honour the flag even where the kernels happen to be present.
            if HeraSpiceTests.skipHeraRequested then
                skiptest "--skip-hera was requested"

            match Data.instrumentImages (), Data.aaraOpc (), Data.kernelRoot () with
            | None, _, _ -> skiptest "no instrument images: clone PRo3D.Resources.TestData and set PRO3D_TEST_DATA"
            | _, None, _ -> skiptest "no OPC with per-vertex layers: set PRO3D_AARA_OPC"
            | _, _, None -> skiptest "no SPICE kernels: set PRO3D_SPICE_KERNELS"
            | Some imageFolder, Some opc, Some kernelRoot ->

            let body = "DIMORPHOS"
            let frame = "DIMORPHOS_FIXED"

            match InstrumentObservation.resolveImage imageFolder (Some Data.aspectImage) with
            | Result.Error e -> skiptest (sprintf "could not resolve the image: %s" e)
            | Result.Ok img ->

            match InstrumentObservation.resolveKernel None kernelRoot img with
            | Result.Error e -> skiptest (sprintf "no usable metakernel: %s" e)
            | Result.Ok kernel ->

            // Go through the suite's kernel bookkeeping: one metakernel is active per process
            // and it is swapped by DeInit+Init, so bypassing it corrupts whatever ran before.
            HeraSpiceTests.ensureKernelAt [ kernel ]

            let observer = PRo3D.SPICE.InstrumentProjection.instrument2CameraSource img.mbi.instrument
            match InstrumentObservation.projectorCamera None observer frame body ProjectionMethod.MbiBased img,
                  img.size with
            | Result.Error e, _ -> failtestf "no projector camera: %s" e
            | _, None -> skiptest "the sidecar does not declare the image size"
            | Result.Ok cam, Some size ->

            HeadlessPicking.init ()
            let hierarchies = HeadlessPicking.loadHierarchies opc
            let kdTrees = HeadlessPicking.loadKdTreeMap hierarchies
            if FSharp.Data.Adaptive.HashMap.isEmpty kdTrees then
                skiptest (sprintf "no kd-trees under %s -- build them with `pro3d-tool kdtree`" opc)

            let patchInfos = HeadlessPicking.buildPatchInfos hierarchies
            let convention = InstrumentObservation.PixelConvention.Image

            let mutable cache = FSharp.Data.Adaptive.HashMap.empty
            let mutable hits = 0
            let mutable worstLon = 0.0
            let mutable worstLat = 0.0
            let mutable worstRadius = 0.0

            // Dimorphos is small in a frame aimed at Didymos, so sweep rather than guess.
            for y in 8 .. 16 .. size.Y - 1 do
                for x in 8 .. 16 .. size.X - 1 do
                    let ray = InstrumentObservation.pixelRay cam size convention (V2d(float x, float y))
                    let hit, newCache = HeadlessPicking.intersectAll kdTrees cache (FastRay3d ray)
                    cache <- newCache
                    match hit with
                    | None -> ()
                    | Some hit ->
                        let attributes = HeadlessPicking.sampleAttributes patchInfos hit
                        match attributes |> List.tryFind (fun (n, _) -> n = "LonLatRad"),
                              CooTransformation.tryGetLatLonAlt Planet.Dimorphos hit.position with
                        | Some (_, llr), Some coo when llr.Length >= 3 ->
                            hits <- hits + 1
                            let toDegrees = 9.0 / 10.0
                            worstLon <- max worstLon (angleDelta (llr.[0] * toDegrees) coo.longitude)
                            worstLat <- max worstLat (abs (llr.[1] * toDegrees - 90.0 - coo.latitude))
                            worstRadius <- max worstRadius (abs (llr.[2] - coo.altitude))
                        | _ -> ()

            printfn "[unproject] %d hits with a LonLatRad sample; worst disagreement: lon %.5f deg, lat %.5f deg, radius %.5f m"
                hits worstLon worstLat worstRadius

            if hits < 5 then
                skiptest (sprintf "only %d ray(s) reached the body with an attribute sample -- this geometry cannot exercise the check" hits)

            // Interpolation across a triangle accounts for a small residual; anything larger
            // means a step of the chain is wrong, not merely imprecise.
            Expect.isLessThan worstLon 0.01 "longitude must agree with the shape model's own value"
            Expect.isLessThan worstLat 0.01 "latitude must agree with the shape model's own value"
            Expect.isLessThan worstRadius 0.01 "radius must agree with the altitude computed from the hit point"
        }
    ]


// ---------------------------------------------------------------------------------------
// the input table and the output table -- pure, no data, no kernels, runs in CI
// ---------------------------------------------------------------------------------------

module private Table =

    let write (name : string) (content : string) =
        let path = Path.Combine(Path.GetTempPath(), name)
        File.WriteAllText(path, content)
        path

    let parse (name : string) (content : string) =
        match UnprojectVerb.parseInput (write name content) with
        | Result.Error e -> failtestf "expected the table to parse, got: %s" e
        | Result.Ok (header, rows) -> header, rows

    // annotated: PRo3D.Core.Surface.ProfileSample also has `position`, and without this the
    // labels bind to that record instead
    let row (r : UnprojectVerb.InputRow) status : UnprojectVerb.OutputRow =
        { input = r
          status = status
          position = None
          lonLatAlt = None
          range = None
          attributes = [] }

let private tableTests =
    testList "tables" [

        test "the separator is detected, not configured" {
            for name, content in
                [ "comma.csv",     "img.tif,320,256"
                  "semicolon.csv", "img.tif;320;256"
                  "tab.csv",       "img.tif\t320\t256"
                  "space.csv",     "img.tif  320   256" ] do
                let _, rows = Table.parse name content
                Expect.hasLength rows 1 name
                match rows with
                | [ r ] ->
                    Expect.equal r.image "img.tif" (name + ": image")
                    Expect.equal r.pixel (V2d(320.0, 256.0)) (name + ": pixel")
                    Expect.isNone r.error (name + ": should parse")
                | _ -> failtest name
        }

        // A header is recognised by its x/y not being numbers, so both forms work with no flag.
        test "a header is optional and detected by content" {
            let header, rows = Table.parse "hdr.csv" "image,x,y,label\nimg.tif,1,2,a\n"
            Expect.equal header [| "image"; "x"; "y"; "label" |] "header is taken from the file"
            Expect.hasLength rows 1 "the header is not a data row"

            let header, rows = Table.parse "nohdr.csv" "img.tif,1,2,a\n"
            Expect.equal header.[0] "image" "a synthesised header still names the first columns"
            Expect.hasLength rows 1 "the first row is data"
        }

        test "extra columns and sub-pixel coordinates survive" {
            let _, rows = Table.parse "extra.csv" "image,x,y,label,note\nimg.tif,280.5,240.25,boulder_7,keep me\n"
            match rows with
            | [ r ] ->
                Expect.equal r.pixel (V2d(280.5, 240.25)) "fractional coordinates are kept"
                Expect.equal r.cells.[3] "boulder_7" "extra column"
                Expect.equal r.cells.[4] "keep me" "extra column with a space"
            | _ -> failtest "expected one row"
        }

        // One bad line must not cost the caller the other rows.
        test "a malformed row is an error row, not a failed run" {
            let _, rows = Table.parse "bad.csv" "image,x,y\nimg.tif,1,2\nimg.tif,bad,2\nimg.tif\n"
            Expect.hasLength rows 3 "every line is still represented"
            Expect.isNone rows.[0].error "the good row parses"
            Expect.isSome rows.[1].error "unparsable coordinates"
            Expect.isSome rows.[2].error "too few columns"
            Expect.equal rows.[1].cells.[1] "bad" "the original text is kept for the output"
        }

        test "output has one row per input row, in order" {
            let _, rows = Table.parse "order.csv" "image,x,y\na.tif,1,2\nb.tif,3,4\nc.tif,5,6\n"
            let outputs =
                [ Table.row rows.[0] "ok"; Table.row rows.[1] "no-hit"; Table.row rows.[2] "bad-input" ]
            let path = Path.Combine(Path.GetTempPath(), "unproject-order-out.csv")
            UnprojectVerb.writeTable path [| "image"; "x"; "y" |] outputs
            let lines = File.ReadAllLines path
            Expect.hasLength lines 4 "header plus one row each"
            Expect.stringStarts lines.[1] "a.tif,1,2,ok" "first row, in order"
            Expect.stringStarts lines.[2] "b.tif,3,4,no-hit" "second row keeps its status"
            Expect.stringStarts lines.[3] "c.tif,5,6,bad-input" "third row"
        }

        // The classic silent corruption: a European desktop writes 3,14 and the CSV gains a
        // column. Everything numeric must go out invariant regardless of the machine's locale.
        test "numbers are written invariant under a comma-decimal culture" {
            let previous = CultureInfo.CurrentCulture
            try
                CultureInfo.CurrentCulture <- CultureInfo "de-AT"
                let _, rows = Table.parse "culture.csv" "image,x,y\nimg.tif,1,2\n"
                let output =
                    { Table.row rows.[0] "ok" with
                        position = Some (V3d(388.6271284423856, -81.42799660104038, 83.9453709935533))
                        range = Some 9701.6994000637 }
                let path = Path.Combine(Path.GetTempPath(), "unproject-culture-out.csv")
                UnprojectVerb.writeTable path [| "image"; "x"; "y" |] [ output ]
                let line = (File.ReadAllLines path).[1]
                Expect.stringContains line "388.6271284423856" "the decimal point stays a point"
                Expect.equal (line.Split(',').Length) 11 "no stray columns from a decimal comma"
            finally
                CultureInfo.CurrentCulture <- previous
        }

        test "vector layers expand to one column per component" {
            Expect.equal (UnprojectVerb.attributeColumns "Slope" [| 12.0 |])
                [ "Slope", 12.0 ] "a scalar layer keeps its name"
            Expect.equal (UnprojectVerb.attributeColumns "Normal" [| 1.0; 2.0; 3.0 |])
                [ "Normal_x", 1.0; "Normal_y", 2.0; "Normal_z", 3.0 ] "a vector layer splits"
        }
    ]


let tests () =
    testList "unproject" [
        addressingTests
        tableTests
        crossCheck
    ]
