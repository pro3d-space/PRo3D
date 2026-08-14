module ProjectedImageMetadataTest

open System
open System.IO

open Expecto
open Aardvark.Base

open PRo3D.Core

// ---------------------------------------------------------------------------
// Test data
// ---------------------------------------------------------------------------
//
// Real HERA/ASPECT MBI sidecar metadata as exported by Fits2Mbi. This is the
// metadata PRo3D parses when projecting an instrument image in the GIS view.
// The file lays its FITS keywords out as:
//
//   "fits_header": [ { "DATE-OBS": { "value": ..., "comment": ... }, ... } ]
//   "spice":       { "SUN_POSX": ..., "SC_QUATW": ..., ... }
//
// i.e. the keyword block is named "fits_header" (not "fits_hdu_headers") and
// the spacecraft quaternion components are SC_QUATW/X/Y/Z (not SC_QUAT0..3).
// A parser keyed on the old names extracts nothing and every field falls back
// to 0 / DateTime.Min.

// The new HERA/ASPECT export ("fits_header" block, SC_QUATW/X/Y/Z quaternion).
let private newFixture =
    Path.Combine(__SOURCE_DIRECTORY__, "data", "ASP_000000_270323T060000_2B.mbi.json")

// An older HERA/AFC2 export. This one uses the original layout the parser was
// written for: a "fits_hdu_headers" array (here with a second ORIGHDR HDU) and
// SC_QUAT0..3 quaternion components. Kept as a fixture so a fix for the new
// format is checked against not regressing the old one.
let private oldFixture =
    Path.Combine(__SOURCE_DIRECTORY__, "data", "AF2_0CO72A_250311T025652_1B.mbi.json")

let private parse (fixture : string) =
    if not (File.Exists fixture) then
        skiptest (sprintf "missing fixture: %s" fixture)
    match InstrumentMetadata.Tiff_Mbi_Json.tryParseJson (File.ReadAllText fixture) with
    | Result.Error e -> failtestf "expected the MBI metadata to parse, but got error: %A" e
    | Result.Ok mbi -> mbi

// A header lookup backed by a plain map, so parseQuaternion can be exercised
// without any JSON.
let private lookup (pairs : (string * float) list) : string -> Option<float> =
    let m = Map.ofList pairs
    fun key -> Map.tryFind key m

let tests () =
    testList "projectedImageMetadata" [

        testList "parseQuaternion" [
            test "old convention SC_QUAT0..3" {
                let get = lookup [ "SC_QUAT0", 0.69457825
                                   "SC_QUAT1", -0.57549785
                                   "SC_QUAT2", -0.18713807
                                   "SC_QUAT3", 0.38902779 ]
                match InstrumentMetadata.Tiff_Mbi_Json.parseQuaternion get with
                | Result.Error e -> failtestf "expected Ok, got error: %s" e
                | Result.Ok q ->
                    Expect.floatClose Accuracy.high q.W 0.69457825 "W"
                    Expect.floatClose Accuracy.high q.X -0.57549785 "X"
                    Expect.floatClose Accuracy.high q.Y -0.18713807 "Y"
                    Expect.floatClose Accuracy.high q.Z 0.38902779 "Z"
            }

            test "new convention SC_QUATW/X/Y/Z" {
                let get = lookup [ "SC_QUATW", 0.856149532097478
                                   "SC_QUATX", -0.5041133031626687
                                   "SC_QUATY", 0.014516492773652462
                                   "SC_QUATZ", -0.11254789070100477 ]
                match InstrumentMetadata.Tiff_Mbi_Json.parseQuaternion get with
                | Result.Error e -> failtestf "expected Ok, got error: %s" e
                | Result.Ok q ->
                    Expect.floatClose Accuracy.high q.W 0.856149532097478 "W"
                    Expect.floatClose Accuracy.high q.X -0.5041133031626687 "X"
                    Expect.floatClose Accuracy.high q.Y 0.014516492773652462 "Y"
                    Expect.floatClose Accuracy.high q.Z -0.11254789070100477 "Z"
            }

            test "missing components yields Error" {
                let get = lookup [ "SC_QUAT0", 1.0; "SC_QUAT1", 0.0 ]  // incomplete
                match InstrumentMetadata.Tiff_Mbi_Json.parseQuaternion get with
                | Result.Ok q -> failtestf "expected Error, got %A" q
                | Result.Error _ -> ()
            }

            test "a complete convention wins even if the other is partially present" {
                // partial old keys present, complete new keys present -> new wins
                let get = lookup [ "SC_QUAT0", 9.0
                                   "SC_QUATW", 0.5; "SC_QUATX", 0.5; "SC_QUATY", 0.5; "SC_QUATZ", 0.5 ]
                match InstrumentMetadata.Tiff_Mbi_Json.parseQuaternion get with
                | Result.Error e -> failtestf "expected Ok, got error: %s" e
                | Result.Ok q ->
                    Expect.floatClose Accuracy.high q.W 0.5 "W"
                    Expect.floatClose Accuracy.high q.X 0.5 "X"
            }
        ]

        test "new HERA/ASPECT MBI sidecar parses into populated Mbi metadata" {
            // Ground-truth values from the fixture's "fits_header" block.
            let expectedSunPos    = V3d(51339760.707945116, -164219676.60315868, -77845583.10634352)
            let expectedEarthPos  = V3d(-97644645.21236482, -169003457.10397458, -79917979.45608161)
            let expectedTargetPos = V3d(1.4123456260112177, 8.779494695122768, 5.016216654125852)
            let expectedQuat      = QuaternionD(0.856149532097478, -0.5041133031626687, 0.014516492773652462, -0.11254789070100477)

            let mbi = parse newFixture

            Expect.equal mbi.instrument "ASPECT" "instrument"

            // DATE-OBS is "2027-03-23T06:00:00.000"
            Expect.equal mbi.obs_date.Year 2027 "observation year"
            Expect.equal mbi.obs_date.Month 3 "observation month"
            Expect.equal mbi.obs_date.Day 23 "observation day"

            Expect.floatClose Accuracy.high mbi.sunPos.X expectedSunPos.X "sun X"
            Expect.floatClose Accuracy.high mbi.sunPos.Y expectedSunPos.Y "sun Y"
            Expect.floatClose Accuracy.high mbi.sunPos.Z expectedSunPos.Z "sun Z"

            Expect.floatClose Accuracy.high mbi.earthPos.X expectedEarthPos.X "earth X"
            Expect.floatClose Accuracy.high mbi.earthPos.Y expectedEarthPos.Y "earth Y"
            Expect.floatClose Accuracy.high mbi.earthPos.Z expectedEarthPos.Z "earth Z"

            Expect.floatClose Accuracy.high mbi.targetPos.X expectedTargetPos.X "target X"
            Expect.floatClose Accuracy.high mbi.targetPos.Y expectedTargetPos.Y "target Y"
            Expect.floatClose Accuracy.high mbi.targetPos.Z expectedTargetPos.Z "target Z"

            // distance to the target body must be the real range, not 0.
            Expect.isGreaterThan mbi.targetPos.Length 0.0 "target distance must be non-zero"

            Expect.floatClose Accuracy.high mbi.sc_quat.W expectedQuat.W "quaternion W"
            Expect.floatClose Accuracy.high mbi.sc_quat.X expectedQuat.X "quaternion X"
            Expect.floatClose Accuracy.high mbi.sc_quat.Y expectedQuat.Y "quaternion Y"
            Expect.floatClose Accuracy.high mbi.sc_quat.Z expectedQuat.Z "quaternion Z"
        }

        test "old HERA/AFC2 MBI sidecar (fits_hdu_headers, SC_QUAT0..3) still parses" {
            // Ground-truth values from the fixture's first "fits_hdu_headers" HDU.
            let expectedSunPos    = V3d(189441673.229, -142281416.0, -70427013.301)
            let expectedEarthPos  = V3d(42906095.27, -119612382.99, -60599274.213)
            let expectedTargetPos = V3d(-757865.528, 701405.369, 286617.65)
            let expectedQuat      = QuaternionD(0.69457825, -0.57549785, -0.18713807, 0.38902779)

            let mbi = parse oldFixture

            Expect.equal mbi.instrument "AFC2" "instrument"

            // DATE-OBS is "2025-03-11T02:56:52.653"
            Expect.equal mbi.obs_date.Year 2025 "observation year"
            Expect.equal mbi.obs_date.Month 3 "observation month"
            Expect.equal mbi.obs_date.Day 11 "observation day"

            Expect.floatClose Accuracy.high mbi.sunPos.X expectedSunPos.X "sun X"
            Expect.floatClose Accuracy.high mbi.sunPos.Y expectedSunPos.Y "sun Y"
            Expect.floatClose Accuracy.high mbi.sunPos.Z expectedSunPos.Z "sun Z"

            Expect.floatClose Accuracy.high mbi.earthPos.X expectedEarthPos.X "earth X"
            Expect.floatClose Accuracy.high mbi.earthPos.Y expectedEarthPos.Y "earth Y"
            Expect.floatClose Accuracy.high mbi.earthPos.Z expectedEarthPos.Z "earth Z"

            Expect.floatClose Accuracy.high mbi.targetPos.X expectedTargetPos.X "target X"
            Expect.floatClose Accuracy.high mbi.targetPos.Y expectedTargetPos.Y "target Y"
            Expect.floatClose Accuracy.high mbi.targetPos.Z expectedTargetPos.Z "target Z"

            Expect.isGreaterThan mbi.targetPos.Length 0.0 "target distance must be non-zero"

            Expect.floatClose Accuracy.high mbi.sc_quat.W expectedQuat.W "quaternion W"
            Expect.floatClose Accuracy.high mbi.sc_quat.X expectedQuat.X "quaternion X"
            Expect.floatClose Accuracy.high mbi.sc_quat.Y expectedQuat.Y "quaternion Y"
            Expect.floatClose Accuracy.high mbi.sc_quat.Z expectedQuat.Z "quaternion Z"
        }

        // Regression for a real bug: ASPECT exports one .mbi.json per
        // observation but dozens of per-band images (Vis_0, NIR1_3, ...)
        // whose file names don't contain the sidecar's name at all. The old
        // lookup derived the sidecar path by stripping a fixed set of known
        // suffixes (_Stacked/_AFC1/_AFC2/_HSH) off the image name, which
        // doesn't match "_Vis_0"/"_NIR1_3"/etc., so it silently returned no
        // mbi metadata -- and the GUI fell back to DateTime.MinValue for
        // every such image ("all dates are zero"). tryParseMetadataForImagePath
        // must now find the sidecar by content (its declared band list),
        // regardless of image file naming.
        testList "tryParseMetadataForImagePath" [
            test "finds mbi metadata for a per-band ASPECT image by content, not by file name" {
                if not (File.Exists newFixture) then
                    skiptest (sprintf "missing fixture: %s" newFixture)

                let bandImagePath = Path.Combine(Path.GetDirectoryName(newFixture), "ASP_000000_270323T060000_2B_Vis_0.tif")
                let (mbi, _) = InstrumentMetadata.tryParseMetadataForImagePath bandImagePath

                match mbi with
                | None -> failtest "expected mbi metadata to be found for a per-band image name"
                | Some mbi ->
                    Expect.notEqual mbi.obs_date DateTime.MinValue "obs_date must not fall back to DateTime.MinValue"
                    Expect.equal mbi.obs_date.Year 2027 "observation year"
                    Expect.equal mbi.obs_date.Month 3 "observation month"
                    Expect.equal mbi.obs_date.Day 23 "observation day"
            }

            test "finds mbi metadata for a single-band AFC2 image whose file name matches the sidecar's band list" {
                if not (File.Exists oldFixture) then
                    skiptest (sprintf "missing fixture: %s" oldFixture)

                let imagePath = Path.Combine(Path.GetDirectoryName(oldFixture), "AF2_0CO72A_250311T025652_1B_AFC2.tif")
                let (mbi, _) = InstrumentMetadata.tryParseMetadataForImagePath imagePath

                match mbi with
                | None -> failtest "expected mbi metadata to be found for the AFC2 image"
                | Some mbi ->
                    Expect.notEqual mbi.obs_date DateTime.MinValue "obs_date must not fall back to DateTime.MinValue"
                    Expect.equal mbi.obs_date.Year 2025 "observation year"
            }

            test "returns no mbi metadata for an image no sidecar declares" {
                if not (File.Exists newFixture) then
                    skiptest (sprintf "missing fixture: %s" newFixture)

                let unrelatedImagePath = Path.Combine(Path.GetDirectoryName(newFixture), "does_not_exist_0.tif")
                let (mbi, _) = InstrumentMetadata.tryParseMetadataForImagePath unrelatedImagePath

                Expect.isNone mbi "no sidecar declares this image, so no mbi metadata should be returned"
            }

            // Real HSH sidecar (fits_hdu_headers/SC_QUAT0..3, "Stacked" band
            // naming) copied into the test fixtures, alongside its own
            // per-image .tif.json statistics sidecar, so both halves of
            // ParsedMetadata are exercised end-to-end for a real, non-ASPECT
            // export. The image itself is a zero-byte placeholder --
            // tryParseMetadataForImagePath only ever derives sidecar paths
            // from the given image path, it never reads the image itself.
            test "finds mbi and per-image metadata for a real HSH Stacked image" {
                let dataDir = Path.Combine(__SOURCE_DIRECTORY__, "data")
                let hshSidecar = Path.Combine(dataDir, "HSH_0CR7B2_250312T062000_1B.mbi.json")
                if not (File.Exists hshSidecar) then
                    skiptest (sprintf "missing fixture: %s" hshSidecar)

                let imagePath = Path.Combine(dataDir, "HSH_0CR7B2_250312T062000_1B_Stacked.tif")
                let (mbi, tif) = InstrumentMetadata.tryParseMetadataForImagePath imagePath

                match mbi with
                | None -> failtest "expected mbi metadata to be found for the HSH image"
                | Some mbi ->
                    Expect.equal mbi.instrument "HSH" "instrument"
                    Expect.notEqual mbi.obs_date DateTime.MinValue "obs_date must not fall back to DateTime.MinValue"
                    Expect.equal mbi.obs_date.Year 2025 "observation year"
                    Expect.equal mbi.obs_date.Month 3 "observation month"
                    Expect.equal mbi.obs_date.Day 12 "observation day"

                Expect.isSome tif "expected the per-image .tif.json statistics sidecar to be found too"
            }
        ]

        // End-to-end folder discovery: a directory holding one real multi-band
        // ASPECT sidecar and one real single-band HSH sidecar (each alongside
        // one of its declared images) should resolve real, non-zero obs_dates
        // for both -- exercising discoverInstrumentFolder's shared mbi index
        // instead of a single tryParseMetadataForImagePath lookup.
        test "discoverInstrumentFolder resolves real obs_dates for both ASPECT and HSH conventions" {
            let dataDir = Path.Combine(__SOURCE_DIRECTORY__, "data")
            let discovered =
                InstrumentMetadata.discoverInstrumentFolder dataDir
                |> Seq.map (fun (path, (mbi, _)) -> Path.GetFileName path, mbi)
                |> Map.ofSeq

            match Map.tryFind "ASP_000000_270323T060000_2B_Vis_0.tif" discovered with
            | Some (Some mbi) ->
                Expect.notEqual mbi.obs_date DateTime.MinValue "ASPECT obs_date must not be DateTime.MinValue"
                Expect.equal mbi.obs_date.Year 2027 "ASPECT observation year"
            | other -> failtestf "expected ASPECT mbi metadata in the discovered folder, got %A" other

            match Map.tryFind "HSH_0CR7B2_250312T062000_1B_Stacked.tif" discovered with
            | Some (Some mbi) ->
                Expect.notEqual mbi.obs_date DateTime.MinValue "HSH obs_date must not be DateTime.MinValue"
                Expect.equal mbi.obs_date.Year 2025 "HSH observation year"
            | other -> failtestf "expected HSH mbi metadata in the discovered folder, got %A" other
        }
    ]
