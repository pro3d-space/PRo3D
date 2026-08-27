module MbiSidecarTest

open System
open System.IO

open Expecto
open Aardvark.Base
open Aardvark.Rendering

open PRo3D.SPICE
open PRo3D.Core
open PRo3D.ImageMapping
open PRo3D.Tool

/// The .mbi.json sidecar convention PRo3D's projection depends on, and the sidecar writer
/// that states it.
///
/// The first group is about DATA, not code: `InstrumentProjection.projectOntoQuat` reads
/// SC_QUAT as the spacecraft -> J2000 rotation and TRG_POS as target MINUS spacecraft, and a
/// delivery that means either the other way round still parses, still projects, and lands
/// nowhere near the body -- silently. These tests pin the convention against the real HERA
/// fixtures so it can be quoted, and so a future "fix" to the reader has to break them
/// first. The HERA COP synthetic delivery violates both (docs/COP-sidecar-issues.md,
/// issues 6-8), which is why its images do not sit on the terrain.
///
/// The second group closes the loop the other way: write a sidecar for a camera we chose,
/// read it back through the viewer's own `Visualization.projectDirect`, and require the same
/// projector. That is what makes `simulate-image --write-mbi` output importable evidence
/// rather than a plausible-looking file.

// ---------------------------------------------------------------------------------------
// the sidecar convention, straight from the fixtures (no SPICE, no GPU)
// ---------------------------------------------------------------------------------------

let private fixture (name : string) = Path.Combine(__SOURCE_DIRECTORY__, "data", name)

let private parseMbi (path : string) : InstrumentMetadata.Tiff_Mbi_Json.Mbi =
    if not (File.Exists path) then skiptest (sprintf "missing fixture: %s" path)
    match InstrumentMetadata.Tiff_Mbi_Json.tryParseJson (File.ReadAllText path) with
    | Result.Error e -> failtestf "expected %s to parse as mbi metadata: %A" (Path.GetFileName path) e
    | Result.Ok mbi -> mbi

/// Where the target sits in the spacecraft frame, per the sidecar's own two fields.
///
/// `M33d.Rotation (Rot3d q)` takes spacecraft-frame vectors to J2000 (that is the convention
/// under test), so its transpose takes TRG_POS the other way. Under the convention the
/// result must be the instrument's +Z, because TRG_POS points from the spacecraft AT the
/// target -- which is what the camera looks at.
let private targetInSpacecraftFrame (mbi : InstrumentMetadata.Tiff_Mbi_Json.Mbi) : V3d =
    let a = M33d.Rotation (Rot3d mbi.sc_quat)
    (a.Transposed * mbi.targetPos).Normalized

let private conventionTests =
    [ "AF2_0CO72A_250311T025652_1B.mbi.json", "AFC2 / Mars flyby"
      "HSH_0CR7B2_250312T062000_1B.mbi.json", "HSH / Mars flyby"
      "ASP_000000_270323T060000_2B.mbi.json", "ASPECT / Didymos" ]
    |> List.map (fun (file, label) ->
        test (sprintf "sidecar convention: %s" label) {
            let mbi = parseMbi (fixture file)
            let d = targetInSpacecraftFrame mbi
            printfn "[mbiSidecar] %s: target in the spacecraft frame = %A" label d
            // Real deliveries put the target within a fraction of a degree of the boresight;
            // 1 degree leaves room for genuine off-centre pointing without admitting a
            // conjugated quaternion (which lands ~90-180 degrees away).
            Expect.isGreaterThan d.Z (cos (1.0 * Constant.RadiansPerDegree))
                (sprintf
                    "SC_QUAT must be the spacecraft -> J2000 rotation and TRG_POS the vector \
                     FROM the spacecraft TO the target, so the target lands on +Z in the \
                     spacecraft frame. Got %A -- a conjugated quaternion or a negated/\
                     mis-centred TRG_POS looks exactly like this and projects nowhere near \
                     the body." d)
        })

// ---------------------------------------------------------------------------------------
// writer round trip (needs SPICE for the body-fixed -> J2000 rotation)
// ---------------------------------------------------------------------------------------

/// A camera looking at the body centre from `distance` along `direction`, with an arbitrary
/// roll -- deliberately not axis-aligned, so a dropped or transposed rotation cannot pass by
/// symmetry.
let private syntheticCamera (direction : V3d) (distance : float) (up : V3d) =
    let position = direction.Normalized * distance
    CameraView.lookAt position V3d.Zero up |> CameraView.viewTrafo

/// The ASPECT/Didymos setup, whose kernel coverage `InstrumentProjectionComparisonTest`
/// already relies on: the quaternion path resolves there without a spacecraft CK, which is
/// what the round trip needs. Frame, observer and instrument are that fixture's; the CAMERA
/// is synthetic, which is the whole point -- an arbitrary pose has to survive the trip.
let private roundTripTest =
    test "a written sidecar reproduces the camera it was written from" {
        if not HeraSpiceTests.hasHera then
            skiptest "HERA kernels not available -- the sidecar round trip needs a frame rotation"

        let aspect = fixture "ASP_000000_270323T060000_2B.mbi.json"
        if not (File.Exists aspect) then skiptest (sprintf "missing fixture: %s" aspect)
        HeraSpiceTests.loadKernelForMbiContent (File.ReadAllText aspect)

        let frame = "ECLIPJ2000"
        let body = "DIDYMOS"
        let instrument = "MILANI_ASPECT_NIR1"
        let observer = "DIDYMOS"
        let time = (parseMbi aspect).obs_date

        let view = syntheticCamera (V3d(0.3, -0.8, 0.52)) 12000.0 (V3d(0.17, 0.41, 0.9))
        let near, far = InstrumentProjection.nearFarForDistance 12000.0
        match Map.tryFind instrument (InstrumentProjection.instruments near far) with
        | None -> failtestf "no frustum for %s" instrument
        | Some frustum ->

        let proj = Frustum.projTrafo frustum
        let dir = Path.Combine(Path.GetTempPath(), "pro3d-mbi-sidecar", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory dir |> ignore
        try
            // the image only has to exist and be named: the geometry is all in the sidecar
            let imagePath = Path.Combine(dir, "SIM_ASPECT.png")
            PixImage<byte>(Col.Format.Gray, V2i(64, 64)).Save imagePath

            let ctx : MbiSidecar.Context =
                {
                    instrument = instrument
                    body = body
                    frame = frame
                    time = time
                    kernel = HeraSpiceTests.spiceFileName
                    size = V2i(1020, 1020)
                }

            match MbiSidecar.write ctx imagePath view with
            | Result.Error e -> failtestf "could not write the sidecar: %s" e
            | Ok sidecar ->
                Expect.isTrue (File.Exists sidecar) "sidecar written"

                match MbiSidecar.verify ctx imagePath (view * proj) observer with
                | Result.Error e -> failtestf "could not verify the sidecar: %s" e
                | Ok r ->
                    printfn "[mbiSidecar] round trip: boresight %.6f deg, worst corner %.4f px, max element %.3e"
                        r.boresight r.pixels r.matrix
                    // A tenth of a pixel of a 1020-pixel frame: far below anything visible,
                    // far above the double-precision noise of the frame chain.
                    Expect.isLessThan r.pixels 0.1
                        "the viewer must reconstruct the same camera from the sidecar we wrote"
                    Expect.isLessThan r.boresight 1.0e-3 "boresight must round-trip"
        finally
            try Directory.Delete(dir, true) with _ -> ()
    }

/// The writer must state the same convention the fixtures do -- otherwise the two halves of
/// this file could both be self-consistent and both wrong.
let private writerAgreesWithFixtures =
    test "the writer emits the convention the real deliveries use" {
        if not HeraSpiceTests.hasHera then
            skiptest "HERA kernels not available"

        HeraSpiceTests.ensureOpsKernel ()

        let time = DateTime.Parse("2025-03-12T06:20:00Z", Globalization.CultureInfo.InvariantCulture,
                                  Globalization.DateTimeStyles.AdjustToUniversal ||| Globalization.DateTimeStyles.AssumeUniversal)
        let view = syntheticCamera (V3d(0.3, -0.8, 0.52)) 12000.0 (V3d(0.17, 0.41, 0.9))

        match MbiSidecar.deriveObservation "IAU_MARS" "HERA_AFC-1" time view with
        | Result.Error e -> failtestf "could not derive the observation: %s" e
        | Ok obs ->
            let a = M33d.Rotation (Rot3d obs.scQuat)
            let d = (a.Transposed * obs.targetPos).Normalized
            printfn "[mbiSidecar] writer: target in the spacecraft frame = %A" d
            Expect.isGreaterThan d.Z (cos (1.0 * Constant.RadiansPerDegree))
                "the sidecar we write must place the target on the spacecraft frame's +Z, \
                 exactly like the real HERA deliveries"
    }

let tests () =
    testList "mbiSidecar" (conventionTests @ [ roundTripTest; writerAgreesWithFixtures ])
