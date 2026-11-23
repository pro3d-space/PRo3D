/// Surface comparison (docs/SurfaceComparison.md): area statistics between two surfaces.
///
/// The fixture is the DART Dimorphos shape model v003 (Daly et al. 2023, NASA PDS,
/// doi:10.26007/er6h-qf14): the 0.98 m global model and the 5 cm local patch
/// `0883s26429`. The patch alone is 72 MB, too large for PRo3D.Resources.TestData, so both
/// OBJs are downloaded from the PDS Small Bodies Node on first use into a local cache
/// ($PRO3D_TEST_DOWNLOADS, else %LOCALAPPDATA%\PRo3D\test-downloads). The data-backed cases
/// self-skip when offline or without a GL context; the first case needs neither.
module PRo3D.Tests.SurfaceComparisonTest

open System
open System.IO
open System.Net.Http

open Aardvark.Base
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Comparison
open PRo3D.Viewer

open PRo3D.Tests

// last, so nothing opened above shadows `test`
open Expecto

module private Fixture =

    let baseUrl =
        "https://pds-smallbodies.astro.umd.edu/holdings/pds4-dart_shapemodel-v1.0/data_derived_dimorphos_model_v003/"

    /// file name, expected size in bytes (guards against a truncated download)
    let globalModel = "dimorphos_g_0980mm_spc_obj_0000n00000_v003.obj", 14454232L
    let localPatch  = "dimorphos_l_0050mm_spc_obj_0883s26429_v003.obj", 72123832L

    /// Centre of an area on the patch (km, body-fixed), where the comparison failed with
    /// "Could not calculate any distances" in both distance modes.
    let areaLocation = V3d(-0.011101039853103727, -0.08372859499872444, -0.00847804630929598)
    let areaRadius   = 0.012 // 12 m

    let cacheDir =
        let root =
            match Environment.GetEnvironmentVariable "PRO3D_TEST_DOWNLOADS" with
            | p when not (String.IsNullOrWhiteSpace p) -> p
            | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData, "PRo3D", "test-downloads")
        Path.Combine(root, "dart_shapemodel_v003")

    /// The cached file, downloading it first if needed; Error with the reason otherwise.
    let fetch ((name, size) : string * int64) : Result<string, string> =
        let target = Path.Combine(cacheDir, name)
        if File.Exists target && FileInfo(target).Length = size then Result.Ok target
        else
            try
                Directory.CreateDirectory cacheDir |> ignore
                let part = target + ".part"
                use client = new HttpClient(Timeout = TimeSpan.FromMinutes 10.0)
                use response = client.GetAsync(baseUrl + name, HttpCompletionOption.ResponseHeadersRead).Result
                response.EnsureSuccessStatusCode() |> ignore
                do
                    use input = response.Content.ReadAsStreamAsync().Result
                    use output = File.Create part
                    input.CopyTo output
                if FileInfo(part).Length <> size then
                    Result.Error (sprintf "%s: downloaded %d bytes, expected %d" name (FileInfo(part).Length) size)
                else
                    File.Move(part, target, true)
                    Result.Ok target
            with e ->
                Result.Error (sprintf "could not download %s: %s" name e.Message)

    let both () =
        match fetch globalModel, fetch localPatch with
        | Result.Ok g, Result.Ok l -> Result.Ok (g, l)
        | Result.Error e, _ | _, Result.Error e -> Result.Error e

let private comparison (msg : ComparisonAction) = ViewerAction.ComparisonMessage msg

let private areaSize (v : float) = comparison (UpdateDefaultAreaSize (Numeric.SetValue v))

/// The single comparison area, after an update.
let private onlyArea (m : Model) =
    match m.scene.comparisonApp.areas |> HashMap.toValueList with
    | [ a ] -> a
    | areas -> failtestf "expected one comparison area, found %d" areas.Length

let private distancesOf (area : AreaSelection) =
    match area.statistics with
    | Some s -> s.distances
    | None -> failtestf "%s has no statistics: no ray hit both surfaces" area.label

/// Imports both OBJs into a headless viewer, places the area and runs the area statistics.
let private compareIn (update : Model -> ViewerAction -> Model) (m : Model)
                      (surface1 : string) (surface2 : string) (mode : DistanceMode) =
    [
        comparison (SelectSurface1 surface1)
        comparison (SelectSurface2 surface2)
        comparison (SetDistanceMode mode)
        areaSize Fixture.areaRadius
        comparison (AddSelectionArea Fixture.areaLocation)
        comparison UpdateAreaMeasurements
    ]
    |> List.fold update m

let tests (parameters : TestUtils.TestParameters) =
    testList "surface comparison" [

        // The view looked the selected area up with AMap.find; an area whose surfaces did not
        // resolve was dropped from the map while still selected, and the next render crashed.
        test "an area survives an update whose surfaces do not resolve" {
            let update m msg =
                ComparisonApp.update m SurfaceModel.initial ReferenceSystem.initial HashMap.empty HashMap.empty msg |> fst
            let m =
                [ AddSelectionArea Fixture.areaLocation
                  SelectSurface1 "not loaded 1"
                  SelectSurface2 "not loaded 2"
                  UpdateAreaMeasurements ]
                |> List.fold update ComparisonApp.init
            Expect.equal (HashMap.count m.areas) 1 "the area is kept"
            match m.selectedArea with
            | Some id -> Expect.isTrue (HashMap.containsKey id m.areas) "the selected area is still in the map"
            | None -> failtest "the new area is selected"
        }

        testList "DART Dimorphos v003, 5 cm local patch vs 0.98 m global model" [
            let withViewer (body : (Model -> ViewerAction -> Model) -> Model -> string -> string -> unit) () =
                match Fixture.both (), Render.context.Value with
                | Result.Error reason, _ -> skiptest reason
                | _, None -> skiptest "no OpenGL runtime in this environment"
                | Result.Ok (globalPath, patchPath), Some _ ->
                    Startup.init ()
                    let freshModel, update = Render.makeViewer ()
                    let m =
                        [ ViewerAction.ImportObject (MeshLoaderType.Wavefront, [ globalPath ])
                          ViewerAction.ImportObject (MeshLoaderType.Wavefront, [ patchPath ]) ]
                        |> List.fold update (freshModel ())
                    body update m (Path.GetFileName patchPath) (Path.GetFileName globalPath)

            for mode in [ DistanceMode.SurfaceNormal; DistanceMode.Spherical ] do
                testCase (sprintf "%A: distances inside the area" mode) (withViewer (fun update m patch globalModel ->
                    let m = compareIn update m patch globalModel mode
                    let distances = distancesOf (onlyArea m)
                    Expect.isGreaterThan distances.Length 100 "most vertices of the area get a distance"
                    // same body, two SPC reconstructions: they agree to well under 5 m
                    Expect.isLessThan (List.max distances) 0.005 "the surfaces are less than 5 m apart"
                ))

            // all distances are 0, so the outlier threshold (8x the mean) is 0 too, and a
            // strict `<` filtered every distance out ("The input sequence was empty")
            testCase "a surface against itself: all distances 0" (withViewer (fun update m _ globalModel ->
                let m = compareIn update m globalModel globalModel DistanceMode.SurfaceNormal
                let distances = distancesOf (onlyArea m)
                Expect.isNonEmpty distances "the area has distances"
                Expect.all distances (fun d -> d < 1e-9) "a surface is 0 away from itself"
            ))
        ]
    ]
