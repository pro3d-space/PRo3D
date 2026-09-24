/// The Boulders export carries the surface statistics inside each ellipse, taken from
/// the surface the ellipse was drawn on, even while that surface is hidden (#644).
///
/// Drives the real export (`AnnotationExportViewer.export`) against the Dimorphos scene
/// `cases/slowProfileExport.pro3d` of the test-data checkout, loaded into a headless
/// viewer. Needs PRO3D_TEST_DATA (or --testdatasource) and a GL context; skips otherwise.
module PRo3D.Tests.BoulderStatisticsExportTest

open System
open System.IO
open System.Globalization

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Viewer

open PRo3D.Tests

// last, so nothing opened above shadows `test`
open Expecto

let private testDataRoot (parameters : TestUtils.TestParameters) =
    [ Environment.GetEnvironmentVariable "PRO3D_TEST_DATA"
      parameters.testDataSource |> Option.defaultValue "" ]
    |> List.tryFind (fun p -> not (String.IsNullOrWhiteSpace p) && Directory.Exists p)

/// An ellipse lying on the body at `p`: plane perpendicular to the radius, long axis
/// along local east.
let private ellipseAt (p : V3d) (a : float) (b : float) (surfaceName : string) (text : string) =
    let up = p.Normalized
    let north = ReferenceSystemApp.northVector up
    let east = Vec.cross north up
    { Annotation.initial with
        key             = Guid.NewGuid()
        text            = text
        surfaceName     = surfaceName
        geometry        = Geometry.AxisEllipse
        points          = IndexList.ofList [ p + east * a; p + north * b; p - east * a; p - north * b ]
        ellipticResults = Some (EllipticAnnotations.Measures.ofAxes up north p (east * a) (north * b)) }

let private hideAllSurfaces (surfaces : SurfaceModel) =
    let flat =
        surfaces.surfaces.flat
        |> HashMap.map (fun _ leaf ->
            match leaf with
            | Leaf.Surfaces s -> Leaf.Surfaces { s with isVisible = false }
            | other -> other)
    { surfaces with surfaces = { surfaces.surfaces with flat = flat } }

let tests (parameters : TestUtils.TestParameters) =
    testList "boulder statistics export" [

        test "Boulders rows carry the statistics of the surface the ellipse was drawn on" {
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
            if loaded.scene.surfacesModel.sgSurfaces |> HashMap.isEmpty then
                skiptest "the scene's OPC surface did not load (it is referenced by absolute path)"

            // the scene's profile line lies on the surface; its ends are where the boulders go
            let profile =
                match AnnotationExportViewer.annotationsInScope ExportScope.All loaded.drawing.annotations with
                | a :: _ -> a
                | [] -> failtest "the scene holds the profile annotation"
            let onSurface = profile.points |> IndexList.toArray
            let first = onSurface |> Array.tryHead |> Option.defaultWith (fun () -> failtest "profile has points")
            let last  = onSurface |> Array.tryLast |> Option.defaultWith (fun () -> failtest "profile has points")

            let boulders =
                [ ellipseAt first 4.0 2.5 profile.surfaceName "B-drawn"
                  ellipseAt last  3.0 3.0 profile.surfaceName "B-drawn-2"
                  ellipseAt last  2.0 1.0 ""                  "B-imported"
                  ellipseAt first 2.0 1.0 "not loaded"        "B-elsewhere" ]

            let drawing =
                { loaded.drawing with
                    annotations =
                        GroupsApp.addLeaves List.empty
                            (boulders |> List.map Leaf.Annotations |> IndexList.ofList)
                            loaded.drawing.annotations }

            let settings = AnnotationExportSettings.initial |> AnnotationExportSettings.applyPreset ExportPreset.Boulders
            let context : AnnotationExportViewer.SurfaceSamplingContext = {
                // hidden on purpose: the ellipse was measured on it, visibility must not matter
                surfaces       = hideAllSurfaces loaded.scene.surfacesModel
                observedSystem = fun v -> Gis.GisApp.getSpiceReferenceSystem loaded.scene.gisApp v
                observerSystem = Gis.GisApp.getObserverSystem loaded.scene.gisApp
            }

            let outputPath = Path.Combine(TestUtils.outputDir parameters "BoulderStatisticsExport", "boulders.csv")
            if File.Exists outputPath then File.Delete outputPath
            let message = AnnotationExportViewer.export settings outputPath drawing loaded.scene.referenceSystem context
            message |> Option.iter (Log.line "[BoulderStatisticsExport] export reported: %s")

            let lines = File.ReadAllLines outputPath
            let header = lines |> Array.tryHead |> Option.defaultValue "" |> fun h -> h.Split ','
            let rows =
                lines
                |> Array.skip 1
                |> Array.map (fun l ->
                    let cells = l.Split ','
                    header |> Array.mapi (fun i h -> h, cells |> Array.tryItem i |> Option.defaultValue "") |> Map.ofArray)
            let row (text : string) =
                rows
                |> Array.tryFind (fun r -> Map.tryFind "text" r = Some text)
                |> Option.defaultWith (fun () -> failtestf "no row for %s" text)
            let number (r : Map<string, string>) (column : string) =
                match r |> Map.tryFind column with
                | Some v when v <> "" -> Double.Parse(v, CultureInfo.InvariantCulture)
                | _ -> nan

            Expect.equal rows.Length 4 "one row per ellipse, the profile line left out"
            for column in [ "surfaceArea"; "footprintArea"; "vertexCount"; "surface_Slope_area"; "surface_Slope_mean"
                            "surface_Slope_std"; "surface_Slope_min"; "surface_Slope_max" ] do
                Expect.contains header column (sprintf "column %s" column)

            for text, a, b in [ "B-drawn", 4.0, 2.5; "B-drawn-2", 3.0, 3.0 ] do
                let r = row text
                let footprint = Constant.Pi * a * b
                Expect.floatClose Accuracy.medium (number r "footprintArea") footprint (sprintf "%s footprint" text)
                Expect.isGreaterThan (number r "surfaceArea") (0.9 * footprint) (sprintf "%s: the hidden surface was integrated" text)
                Expect.isGreaterThan (number r "vertexCount") 10.0 (sprintf "%s rests on several vertices" text)
                let slope = number r "surface_Slope_mean"
                Expect.isTrue (number r "surface_Slope_min" <= slope && slope <= number r "surface_Slope_max")
                    (sprintf "%s: slope mean within its range" text)

            for text in [ "B-imported"; "B-elsewhere" ] do
                let r = row text
                Expect.isTrue (Double.IsNaN (number r "surfaceArea")) (sprintf "%s: no surface, no statistics" text)
                Expect.isTrue (Double.IsNaN (number r "surface_Slope_mean")) (sprintf "%s: no layer statistics" text)
                Expect.floatClose Accuracy.medium (number r "footprintArea") (Constant.Pi * 2.0) (sprintf "%s keeps its footprint" text)

            match message with
            | Some m -> Expect.stringContains m "not loaded" "the export says which ellipses lack statistics"
            | None -> failtest "an ellipse on a surface that is not loaded must be reported"
        }
    ]
