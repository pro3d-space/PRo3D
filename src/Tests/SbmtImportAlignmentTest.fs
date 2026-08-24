module SbmtImportAlignmentTest

open System
open System.IO
open System.Globalization

open Expecto
open Aardvark.Base
open FSharp.Data.Adaptive
open Newtonsoft.Json.Linq

open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

// ---------------------------------------------------------------------------
// Fixture data
// ---------------------------------------------------------------------------
//
// Import fixtures live under `imports/` in a PRo3D.Resources.TestData checkout,
// resolved the same way every other data-backed list resolves it: PRO3D_TEST_DATA
// first, the suite-wide --testdatasource second. See docs/SbmtImport.md.
//
//   imports/basicSBMT-dimorphos-v4/   a complete SBMT v4 export of Dimorphos,
//                                     one file per structure kind. Committed, so
//                                     the tests using it run for everyone.
//
// Two fixtures are not in the checkout and are looked for at an external root:
//
//   pointOnPike.points.txt + anno.json    the same "Pike" feature of the Dimorphos
//     shape model, once as an SBMT point export (centerXYZ in km, SHM frame) and
//     once as a PRo3D-native point picked by hand and exported as cartesian GeoJSON
//     (metres, same SHM frame). They should agree to within ~10 m given manual-pick
//     precision on a ~170 m asteroid.
//
//   Dimo_Bould_Glob_7_Maurizio            a ~4,800-ellipse boulder catalog, used for
//     the bulk import and drawing-model timings. Too large to redistribute.
//
// Both are searched under <root>/imports first, so dropping them into the checkout
// is enough; otherwise <PRO3D_PRIVATE_TESTDATA>/shapemodels/testdata (or the exact
// directory named by PRO3D_SBMT_TESTDATA) still finds them where they are.
// Every test that needs a missing fixture skips.

module private Data =

    let private existingFile (path : string) =
        if File.Exists path then Some path else None

    /// Root of a PRo3D.Resources.TestData checkout.
    let root = TestUtils.Roots.testData

    /// Where the non-redistributable catalogs live when they are not in the
    /// checkout: <PRO3D_PRIVATE_TESTDATA>/shapemodels/testdata, or the exact
    /// directory named by PRO3D_SBMT_TESTDATA.
    let private externalRoot =
        TestUtils.Roots.firstExisting [
            Environment.GetEnvironmentVariable "PRO3D_SBMT_TESTDATA"
            TestUtils.Roots.privateDir [ "shapemodels"; "testdata" ] |> Option.defaultValue ""
        ]

    /// One file of the committed SBMT v4 sample export, e.g. "points" or "ellipses".
    let basicSbmt (root : Option<string>) (kind : string) =
        root
        |> Option.map (fun r ->
            Path.Combine(r, "imports", "basicSBMT-dimorphos-v4", sprintf "sbmtimport.%s.txt" kind))
        |> Option.bind existingFile

    /// A fixture kept outside the checkout: <root>/imports first, external root second.
    let external' (root : Option<string>) (fileName : string) =
        [ root         |> Option.map (fun r -> Path.Combine(r, "imports", fileName))
          externalRoot |> Option.map (fun r -> Path.Combine(r, fileName)) ]
        |> List.choose id
        |> List.tryPick existingFile

    /// Skip message naming the variable that fixes it.
    let missing (what : string) =
        sprintf "missing fixture: %s (set PRO3D_TEST_DATA to a PRo3D.Resources.TestData \
                 checkout, PRO3D_PRIVATE_TESTDATA for the external catalogs)" what
// ---------------------------------------------------------------------------
// Synthetic SBMT files
// ---------------------------------------------------------------------------
//
// The real catalogs are optional, so the format-level behaviour is pinned with
// files we generate here. Column layout per docs/SbmtImport.md:
//   point   (17 cols): 0 id | 1 name | 2-4 centerXYZ | 5-7 centerLLR
//                      | 8-11 coloringValue | 12 diameter | 13 flattening
//                      | 14 regularAngle | 15 colorRGB | 16 label
//   ellipse (18 cols): as above with 16 gravityAngle, 17 label

let private tab = "\t"

let private inv (x : float) = x.ToString("R", CultureInfo.InvariantCulture)

/// A 17-column SBMT point row.
let private pointRow (xyzKm : V3d) (colorRGB : string) (label : string) =
    String.Join(tab,
        [| "1"; "default"
           inv xyzKm.X; inv xyzKm.Y; inv xyzKm.Z
           "0.0"; "0.0"; "0.0"
           "NA"; "NA"; "NA"; "NA"
           "0.0"; "1.0"; "0.0"
           colorRGB
           label |])

/// An 18-column SBMT ellipse row. `gravityAngle` sits between colour and label.
let private ellipseRow
    (xyzKm : V3d) (diameterKm : float) (flattening : float)
    (regularAngleDeg : float) (colorRGB : string) (label : string) =
    String.Join(tab,
        [| "1"; "default"
           inv xyzKm.X; inv xyzKm.Y; inv xyzKm.Z
           "0.0"; "0.0"; "0.0"
           "NA"; "NA"; "NA"; "NA"
           inv diameterKm; inv flattening; inv regularAngleDeg
           colorRGB
           "NA"
           label |])

let private header (kind : string) =
    [ "# SBMT Structure File"
      sprintf "# type,%s" kind
      "# ----------------------------------------------------------------"
      "# Length units: kilometers"
      "" ]

/// Writes `lines` to a scratch file and runs `f` on its path, always cleaning up.
let private withSbmtFile (lines : string list) (f : string -> 'a) : 'a =
    let path = Path.Combine(Path.GetTempPath(), sprintf "sbmt_%s.txt" (Guid.NewGuid().ToString("N")))
    File.WriteAllLines(path, lines)
    try f path
    finally
        try File.Delete path with _ -> ()

/// Tangent-plane basis the importer builds at `centerMeters`, recomputed here
/// independently so the tests do not just mirror the implementation's algebra.
let private expectedBasis (centerMeters : V3d) =
    let up = centerMeters.Normalized
    let east = (Vec.cross V3d.ZAxis up).Normalized
    let north = (Vec.cross up east).Normalized
    up, east, north

// ---------------------------------------------------------------------------
// Fixture parsers
// ---------------------------------------------------------------------------

/// Imports the first point of an SBMT file through the real importer.
let private importFirstPoint (trafo : Trafo3d) (path : string) : V3d =
    SbmtImporter.startImporter trafo "DIMORPHOS_SHM" path
    |> IndexList.toList
    |> List.tryHead
    |> Option.bind (fun a -> a.points |> IndexList.toList |> List.tryHead)
    |> Option.defaultWith (fun () -> failwithf "no point parsed from %s" path)

let private parseSelectedGeoJsonPoint (path : string) : V3d =
    let root = JObject.Parse(File.ReadAllText(path))
    let geometries = root.["geometries"] :?> JArray
    let chosen =
        geometries
        |> Seq.tryFind (fun g ->
            let isPoint = string g.["type"] = "Point"
            let isSelected =
                match g.["properties"] with
                | null -> false
                | p ->
                    match p.["isSelected"] with
                    | null -> false
                    | v -> v.Value<bool>()
            isPoint && isSelected)
        |> Option.defaultWith (fun () ->
            failwithf "no Point with isSelected=true found in %s" path)

    let coords = chosen.["coordinates"] :?> JArray
    V3d(coords.[0].Value<float>(),
        coords.[1].Value<float>(),
        coords.[2].Value<float>())

// ---------------------------------------------------------------------------
// Format-level tests -- no external data required
// ---------------------------------------------------------------------------

let private headerTests =
    testList "header" [
        test "detects every known structure type" {
            let cases =
                [ "point",    SbmtImporter.Point
                  "ellipse",  SbmtImporter.Ellipse
                  "circle",   SbmtImporter.Circle
                  "line",     SbmtImporter.Line
                  "polyline", SbmtImporter.Polyline
                  "polygon",  SbmtImporter.Polygon ]

            for kind, expected in cases do
                withSbmtFile (header kind) (fun path ->
                    Expect.equal (SbmtImporter.detectStructureType path) (Some expected)
                        (sprintf "structure type for '%s'" kind))
        }

        test "type token is case insensitive and whitespace tolerant" {
            withSbmtFile [ "#   type,  ELLIPSE  " ] (fun path ->
                Expect.equal (SbmtImporter.detectStructureType path)
                    (Some SbmtImporter.Ellipse) "'#   type,  ELLIPSE  '")
        }

        test "unknown kind maps to Unsupported carrying the raw token" {
            withSbmtFile (header "sphere") (fun path ->
                Expect.equal (SbmtImporter.detectStructureType path)
                    (Some (SbmtImporter.Unsupported "sphere")) "unknown kind")
        }

        test "no type header yields None" {
            withSbmtFile [ "# SBMT Structure File"; "# no kind here"; "" ] (fun path ->
                Expect.isNone (SbmtImporter.detectStructureType path) "missing header")
        }

        test "startImporter fails loudly when the type header is missing" {
            withSbmtFile [ "# SBMT Structure File"; "" ] (fun path ->
                Expect.throws
                    (fun () ->
                        SbmtImporter.startImporter Trafo3d.Identity "F" path |> ignore)
                    "a file without '# type,<kind>' must not import silently")
        }
    ]

let private pointTests =
    testList "point rows" [
        test "km are converted to meters and columns map to the annotation" {
            let row = pointRow (V3d(1.0, -2.0, 3.5)) "10,20,30" "\"Pike\""
            match SbmtImporter.parsePointLine Trafo3d.Identity "DIMORPHOS_SHM" row with
            | None -> failtest "expected the row to parse"
            | Some a ->
                Expect.equal a.geometry Geometry.Point "geometry"
                Expect.equal a.points.Count 1 "a point has exactly one position"
                let p = a.points |> IndexList.toList |> List.head
                Expect.equal p (V3d(1000.0, -2000.0, 3500.0)) "km -> m scaling"
                Expect.equal a.color.c (C4b(10uy, 20uy, 30uy, 255uy)) "colorRGB, alpha forced opaque"
                Expect.equal a.text "Pike" "label with surrounding quotes stripped"
                Expect.isTrue a.visible "imported annotations are visible"
        }

        test "an unparseable colour column falls back to magenta" {
            let row = pointRow V3d.One "NA" "\"x\""
            match SbmtImporter.parsePointLine Trafo3d.Identity "F" row with
            | None -> failtest "expected the row to parse"
            | Some a -> Expect.equal a.color.c C4b.Magenta "fallback colour"
        }

        test "the trafo is applied after the km -> m scale" {
            // RotationY(pi) negates X and Z; it is the analytic SHM -> FIXED flip.
            let row = pointRow (V3d(1.0, 2.0, 3.0)) "0,0,0" "\"\""
            match SbmtImporter.parsePointLine (Trafo3d.RotationY Math.PI) "F" row with
            | None -> failtest "expected the row to parse"
            | Some a ->
                let p = a.points |> IndexList.toList |> List.head
                let expected = V3d(-1000.0, 2000.0, -3000.0)
                Expect.isLessThan (p - expected).Length 1e-6
                    (sprintf "rotated position: got %A, expected %A" p expected)
        }

        test "comments, blank lines and short rows are skipped" {
            let parse raw = SbmtImporter.parsePointLine Trafo3d.Identity "F" raw
            Expect.isNone (parse "# a comment") "comment line"
            Expect.isNone (parse "   ") "blank line"
            Expect.isNone (parse "") "empty line"
            Expect.isNone (parse (String.Join(tab, [| "1"; "2"; "3" |]))) "too few columns"
        }

        test "startImporter parses every data row of a point file" {
            let lines =
                header "point" @
                [ pointRow (V3d(1.0, 0.0, 0.0)) "255,0,0" "\"a\""
                  ""
                  "# trailing comment"
                  pointRow (V3d(0.0, 1.0, 0.0)) "0,255,0" "\"b\"" ]

            withSbmtFile lines (fun path ->
                let annotations = SbmtImporter.startImporter Trafo3d.Identity "F" path
                Expect.equal annotations.Count 2 "two data rows, comments and blanks ignored"
                let texts = annotations |> IndexList.toList |> List.map (fun a -> a.text)
                Expect.equal texts [ "a"; "b" ] "file order is preserved")
        }
    ]

let private ellipseTests =
    // Centre on the +X axis keeps the tangent basis exact and pole-free:
    // up = +X, east = +Y, north = +Z.
    let centerKm = V3d(1.0, 0.0, 0.0)
    let centerM = centerKm * 1000.0
    let diameterKm = 0.02      // 20 m major axis -> semi-major a = 10 m
    let semiMajor = 10.0

    let parseOne diameter flattening angle =
        let row = ellipseRow centerKm diameter flattening angle "0,0,255" "\"b1\""
        match SbmtImporter.parseEllipseLine Trafo3d.Identity "F" row with
        | None -> failtest "expected the ellipse row to parse"
        | Some a -> a

    testList "ellipse rows" [
        test "boundary is planar, centred and correctly sized" {
            let flattening = 0.5
            let a = parseOne diameterKm flattening 0.0
            let boundary = a.points |> IndexList.toArray

            Expect.equal a.geometry Geometry.AxisEllipse "geometry"
            Expect.equal a.text "b1" "label"
            Expect.equal a.color.c (C4b(0uy, 0uy, 255uy, 255uy)) "colour"
            Expect.isGreaterThan boundary.Length 3 "enough samples to form a closed shape"

            // Uniform sampling over a full turn: the mean is the centre exactly.
            let centroid = (Array.fold (+) V3d.Zero boundary) / float boundary.Length
            Expect.isLessThan (centroid - centerM).Length 1e-6
                (sprintf "centroid %A should equal the centre %A" centroid centerM)

            let up, _, _ = expectedBasis centerM
            let maxOffPlane =
                boundary |> Array.map (fun p -> abs (Vec.dot (p - centerM) up)) |> Array.max
            Expect.isLessThan maxOffPlane 1e-6 "boundary lies in the tangent plane"

            let radii = boundary |> Array.map (fun p -> (p - centerM).Length)
            Expect.floatClose Accuracy.medium (Array.max radii) semiMajor "semi-major axis"
            Expect.floatClose Accuracy.medium (Array.min radii) (semiMajor * flattening)
                "semi-minor axis = semi-major * flattening"
        }

        test "regularAngle 0 puts the major axis along local east" {
            let a = parseOne diameterKm 0.5 0.0
            let boundary = a.points |> IndexList.toArray
            let _, east, _ = expectedBasis centerM

            let farthest =
                boundary
                |> Array.maxBy (fun p -> (p - centerM).Length)
            let dir = (farthest - centerM).Normalized
            // The axis is unsigned: accept either end.
            Expect.floatClose Accuracy.medium (abs (Vec.dot dir east)) 1.0
                (sprintf "major axis %A should be parallel to east %A" dir east)
        }

        test "regularAngle 90 rotates the major axis onto local north" {
            let a = parseOne diameterKm 0.5 90.0
            let boundary = a.points |> IndexList.toArray
            let _, _, north = expectedBasis centerM

            let farthest = boundary |> Array.maxBy (fun p -> (p - centerM).Length)
            let dir = (farthest - centerM).Normalized
            Expect.floatClose Accuracy.medium (abs (Vec.dot dir north)) 1.0
                (sprintf "major axis %A should be parallel to north %A" dir north)
        }

        test "flattening 1 degenerates to a circle" {
            let a = parseOne diameterKm 1.0 37.0
            let radii =
                a.points |> IndexList.toArray |> Array.map (fun p -> (p - centerM).Length)
            Expect.floatClose Accuracy.medium (Array.max radii) (Array.min radii)
                "all boundary points are equidistant from the centre"
            Expect.floatClose Accuracy.medium (Array.max radii) semiMajor "radius = semi-major"
        }

        test "short ellipse rows are skipped" {
            // A 17-column row is a valid POINT row but too short for an ellipse.
            let row = pointRow centerKm "0,0,0" "\"x\""
            Expect.isNone (SbmtImporter.parseEllipseLine Trafo3d.Identity "F" row)
                "17 columns is not enough for an ellipse"
        }

        test "circle files are dispatched through the ellipse parser" {
            let lines =
                header "circle" @
                [ ellipseRow centerKm diameterKm 1.0 0.0 "255,255,0" "\"c1\"" ]

            withSbmtFile lines (fun path ->
                let annotations = SbmtImporter.startImporter Trafo3d.Identity "F" path
                Expect.equal annotations.Count 1 "one circle"
                let a = annotations |> IndexList.toList |> List.head
                Expect.equal a.geometry Geometry.AxisEllipse
                    "circles become AxisEllipse annotations")
        }

        test "line, polyline and polygon files import as empty, not as errors" {
            for kind in [ "line"; "polyline"; "polygon" ] do
                withSbmtFile (header kind) (fun path ->
                    let annotations = SbmtImporter.startImporter Trafo3d.Identity "F" path
                    Expect.equal annotations.Count 0
                        (sprintf "'%s' is not implemented yet and must import as empty" kind))
        }
    ]

let private groupingTests =
    let refSys = PRo3D.Core.ReferenceSystem.initial

    /// A point file with `n` rows.
    let pointFile n =
        header "point" @
        [ for i in 1 .. n ->
            pointRow (V3d(float i * 0.001, 0.0, 0.0)) "255,0,255" (sprintf "\"p%d\"" i) ]

    testList "importSbmt grouping" [
        test "a small catalog becomes one flat, collapsed group named after the file" {
            withSbmtFile (pointFile 5) (fun path ->
                let groups, flat, lookup =
                    AnnotationGroupsImporter.importSbmt Trafo3d.Identity path refSys "F"

                Expect.equal groups.Count 1 "exactly one top-level group"
                let top = groups |> IndexList.toList |> List.head
                Expect.equal top.leaves.Count 5 "all leaves directly under the group"
                Expect.equal top.subNodes.Count 0 "small catalogs are not chunked"
                Expect.isFalse top.expanded "imported groups start collapsed"
                Expect.equal top.name (Path.GetFileName path) "group is named after the file"
                Expect.equal flat.Count 5 "flat map holds every annotation"
                Expect.equal lookup.Count 5 "lookup holds every annotation")
        }

        test "a large catalog is chunked into sub-folders without losing leaves" {
            // The chunk size is 100, so 250 rows must produce 3 buckets.
            let n = 250
            withSbmtFile (pointFile n) (fun path ->
                let groups, flat, lookup =
                    AnnotationGroupsImporter.importSbmt Trafo3d.Identity path refSys "F"

                let top = groups |> IndexList.toList |> List.head
                Expect.equal top.leaves.Count 0 "chunked catalogs keep no direct leaves"
                Expect.equal top.subNodes.Count 3 "250 rows at chunk size 100 -> 3 buckets"

                let leavesInChunks =
                    top.subNodes |> IndexList.toList |> List.sumBy (fun s -> s.leaves.Count)
                Expect.equal leavesInChunks n "every annotation lands in exactly one bucket"

                let allCollapsed =
                    top.subNodes |> IndexList.toList |> List.forall (fun s -> not s.expanded)
                Expect.isTrue allCollapsed "buckets start collapsed too"

                Expect.equal flat.Count n "flat map holds every annotation"
                Expect.equal lookup.Count n "lookup holds every annotation"

                let ids = flat |> HashMap.keys |> HashSet.count
                Expect.equal ids n "annotation keys are unique")
        }

        test "imported leaves round-trip as annotations" {
            withSbmtFile (pointFile 3) (fun path ->
                let _, flat, _ =
                    AnnotationGroupsImporter.importSbmt Trafo3d.Identity path refSys "F"

                let allPoints =
                    flat
                    |> HashMap.values
                    |> Seq.forall (fun leaf -> (Leaf.toAnnotation leaf).geometry = Geometry.Point)
                Expect.isTrue allPoints "every leaf is a Point annotation")
        }
    ]

// ---------------------------------------------------------------------------
// Fixture-backed tests -- skipped when the external catalogs are absent
// ---------------------------------------------------------------------------

let private fixtureTests (parameters : TestUtils.TestParameters) =
    let root = Data.root parameters.testDataSource

    // The committed SBMT v4 sample export - available wherever the checkout is.
    let basicPointsFile  = Data.basicSbmt root "points"   |> Option.defaultValue ""
    let basicEllipseFile = Data.basicSbmt root "ellipses" |> Option.defaultValue ""

    // Not redistributable; resolved under <root>/imports or the external root.
    let sbmtPointsFile     = Data.external' root "pointOnPike.points.txt"     |> Option.defaultValue ""
    let annoFile           = Data.external' root "anno.json"                  |> Option.defaultValue ""
    let sbmtBigEllipseFile = Data.external' root "Dimo_Bould_Glob_7_Maurizio" |> Option.defaultValue ""

    testList "fixtures" [
        test "SBMT-imported Pike point aligns with PRo3D-native annotation within 10m (identity trafo)" {
            if not (File.Exists sbmtPointsFile) then
                skiptest (Data.missing "pointOnPike.points.txt")
            if not (File.Exists annoFile) then
                skiptest (Data.missing "anno.json")

            let sbmt     = importFirstPoint Trafo3d.Identity sbmtPointsFile
            let pro3d    = parseSelectedGeoJsonPoint annoFile
            let distance = (sbmt - pro3d).Length

            Expect.isLessThan distance 10.0
                (sprintf "SBMT %A vs PRo3D %A: distance %.3fm" sbmt pro3d distance)
        }

        // Sanity check for the v2 SPICE-based path: when the SbmtImporter is
        // eventually handed a real Trafo3d (e.g. SHM -> DIMORPHOS_FIXED from
        // PRo3D.SPICE.CooTransformation.getRotationTrafo), applying it to both
        // points must preserve their relative distance. This test uses the
        // known SHM->FIXED rotation (180 deg around Y, per hera_v16.tf:493)
        // analytically -- no SPICE kernels required.
        test "SHM->FIXED 180deg-around-Y trafo preserves SBMT/PRo3D distance" {
            if not (File.Exists sbmtPointsFile) then
                skiptest (Data.missing "pointOnPike.points.txt")
            if not (File.Exists annoFile) then
                skiptest (Data.missing "anno.json")

            let flipShmToFixed = Trafo3d.RotationY(Math.PI)

            let pro3d = parseSelectedGeoJsonPoint annoFile

            let sbmtShm = importFirstPoint Trafo3d.Identity sbmtPointsFile
            let sbmtFix = importFirstPoint flipShmToFixed sbmtPointsFile
            let pro3dFix = flipShmToFixed.Forward.TransformPos pro3d

            let dShm = (sbmtShm - pro3d).Length
            let dFix = (sbmtFix - pro3dFix).Length

            Expect.floatClose Accuracy.high dShm dFix
                "frame rotation must preserve relative distance"
        }

        // The synthetic point tests above build their own rows; this one runs the
        // importer over a real SBMT v4 export, headers and all, so a change in the
        // file format is caught rather than only a change in our own writer.
        test "SBMT v4 point export imports with km -> m applied" {
            if not (File.Exists basicPointsFile) then
                skiptest (Data.missing "imports/basicSBMT-dimorphos-v4/sbmtimport.points.txt")

            let annotations =
                SbmtImporter.startImporter Trafo3d.Identity "DIMORPHOS_SHM" basicPointsFile

            Expect.equal annotations.Count 3 "the export holds three points"

            let allPoints =
                annotations
                |> IndexList.toList
                |> List.forall (fun a -> a.geometry = Geometry.Point)
            Expect.isTrue allPoints "every imported structure is a Point"

            // Row 1 centerXYZ, in kilometres:
            //   -0.04636082745224429  -0.07230312879082629  0.015186767116164943
            let first = importFirstPoint Trafo3d.Identity basicPointsFile
            let expected = V3d(-46.36082745224429, -72.30312879082629, 15.186767116164943)
            Expect.isLessThan (first - expected).Length 1e-6
                (sprintf "first point should be centerXYZ scaled to metres, got %A" first)

            // centerLLR's radius column (0.08722216834291732 km) is an independent
            // statement of the same position, so it pins the unit conversion down.
            Expect.floatClose Accuracy.medium first.Length 87.22216834291732
                "distance from the body centre should match the declared radius"
        }

        // Smoke test for the ellipse importer against a real boulder catalog:
        // the sampled boundary of the first ellipse must be planar.
        test "SBMT ellipse import produces a planar boundary" {
            if not (File.Exists basicEllipseFile) then
                skiptest (Data.missing "imports/basicSBMT-dimorphos-v4/sbmtimport.ellipses.txt")

            let annotations =
                SbmtImporter.startImporter Trafo3d.Identity "DIMORPHOS_SHM" basicEllipseFile

            Expect.isGreaterThan annotations.Count 0 "should parse at least one ellipse"

            let firstEllipse = annotations |> IndexList.toArray |> Array.head
            let boundary = firstEllipse.points |> IndexList.toArray

            Expect.equal firstEllipse.geometry Geometry.AxisEllipse "geometry"
            Expect.isGreaterThan boundary.Length 3 "expected enough samples to form a closed shape"

            // Planarity: every triple of non-collinear boundary points defines
            // the same tangent plane. Pick three well-separated samples,
            // compute the normal, verify all others project to zero on it.
            let n3 = boundary.Length / 3
            let p0 = boundary.[0]
            let p1 = boundary.[n3]
            let p2 = boundary.[2 * n3]
            let n = Vec.cross (p1 - p0) (p2 - p0) |> Vec.normalize
            let maxOffPlane =
                boundary
                |> Array.map (fun p -> abs (Vec.dot (p - p0) n))
                |> Array.max
            Expect.isLessThan maxOffPlane 1e-6 "ellipse boundary must be planar"
        }

        // Performance / drawing-model integration test. Runs the FULL import
        // pipeline (SbmtImporter.startImporter + AnnotationGroupsImporter.importSbmt
        // -> groups, flat, lookup) on the big Dimorphos boulder catalog and
        // reports wall-clock time. Asserts the resulting structure is what the
        // Viewer handler would graft onto m.drawing.annotations.
        test "SBMT ellipse import: bulk perf + drawing-model integrity" {
            if not (File.Exists sbmtBigEllipseFile) then
                skiptest (Data.missing "Dimo_Bould_Glob_7_Maurizio")

            let refSys = PRo3D.Core.ReferenceSystem.initial

            let sw = System.Diagnostics.Stopwatch.StartNew()
            let groups, flat, lookup =
                AnnotationGroupsImporter.importSbmt
                    Trafo3d.Identity sbmtBigEllipseFile refSys "DIMORPHOS_SHM"
            sw.Stop()

            let n = flat.Count
            printfn "[perf] imported %d ellipses from %s in %.2fs (%.1f us/ellipse)"
                n
                (Path.GetFileName sbmtBigEllipseFile)
                sw.Elapsed.TotalSeconds
                (sw.Elapsed.TotalMilliseconds * 1000.0 / float (max n 1))

            // Drawing-model integrity: one top-level Node containing all leaves,
            // flat HashMap keyed by the same ids, lookup matches.
            Expect.equal groups.Count 1 "importSbmt should yield exactly one top-level group"
            Expect.isGreaterThan n 1000 "fixture should contain at least 1k ellipses"

            let topNode = groups |> IndexList.toArray |> Array.head
            // Catalog of ~4,800 ellipses gets chunked into sub-folders to keep
            // the AnnotationGroups UI responsive when the user expands it.
            // Total leaves across all sub-folders must equal flat.Count.
            let leavesInChunks =
                topNode.subNodes
                |> IndexList.toArray
                |> Array.sumBy (fun n -> n.leaves.Count)
            Expect.equal (topNode.leaves.Count + leavesInChunks) n
                "leaves across the imported group (chunked or flat) == flat count"
            Expect.isFalse topNode.expanded "imported group must be collapsed by default"

            let allLeavesAreEllipses =
                flat
                |> HashMap.values
                |> Seq.forall (fun leaf ->
                    let a = Leaf.toAnnotation leaf
                    a.geometry = Geometry.AxisEllipse)
            Expect.isTrue allLeavesAreEllipses "every imported leaf is an AxisEllipse"

            Expect.equal lookup.Count n "lookup must have one entry per annotation"

            // Soft perf assertion: surface a hard ceiling so a regression
            // (e.g. dns/results re-introduced into the import path) trips this.
            // Loose enough to not flake on slow CI hardware. Adjust if needed.
            Expect.isLessThan sw.Elapsed.TotalSeconds 30.0
                (sprintf "bulk SBMT import too slow: %.2fs for %d ellipses" sw.Elapsed.TotalSeconds n)
        }

        // End-to-end "drawing model" test. Mirrors the Viewer.fs
        // ImportSbmtAnnotations handler step-by-step on an empty DrawingModel,
        // timing each phase so we can spot where the cost actually lives:
        //   1. parse+import          (file -> Annotation records)
        //   2. merge into rootGroup  (IndexList.append + HashMap.union)
        //   3. showDns flag map      (HashMap.map over the flat dictionary)
        //   4. build AdaptiveDrawingModel  (Adaptify spins up per-leaf cells)
        // Asserts the resulting DrawingModel is internally consistent.
        test "SBMT import end-to-end into a full DrawingModel" {
            if not (File.Exists sbmtBigEllipseFile) then
                skiptest (Data.missing "Dimo_Bould_Glob_7_Maurizio")

            let refSys = PRo3D.Core.ReferenceSystem.initial

            let swImport = System.Diagnostics.Stopwatch.StartNew()
            let imported, importedFlat, importedLookup =
                AnnotationGroupsImporter.importSbmt
                    Trafo3d.Identity sbmtBigEllipseFile refSys "DIMORPHOS_SHM"
            swImport.Stop()

            let initial = DrawingModel.initialdrawing
            let annotations0 = initial.annotations

            let swMerge = System.Diagnostics.Stopwatch.StartNew()
            let newSubNodes =
                annotations0.rootGroup.subNodes
                |> IndexList.append imported
            let newFlatRaw = HashMap.union annotations0.flat importedFlat
            let newLookup = HashMap.union annotations0.groupsLookup importedLookup
            swMerge.Stop()

            // Matches the Viewer handler's post-import showDns adjustment.
            // Cheap for AxisEllipse (the condition is false) but worth timing
            // because HashMap.map allocates a fresh map of size n.
            let swShowDns = System.Diagnostics.Stopwatch.StartNew()
            let newFlat =
                newFlatRaw
                |> HashMap.map (fun _ v ->
                    let a = Leaf.toAnnotation v
                    (if a.geometry = Geometry.DnS then { a with showDns = true } else a)
                    |> Leaf.Annotations)
            swShowDns.Stop()

            let mergedDrawing =
                { initial with
                    annotations =
                        { annotations0 with
                            rootGroup = { annotations0.rootGroup with subNodes = newSubNodes }
                            flat = newFlat
                            groupsLookup = newLookup } }

            let swAdaptive = System.Diagnostics.Stopwatch.StartNew()
            let adaptive = AdaptiveDrawingModel(mergedDrawing)
            swAdaptive.Stop()

            printfn "[drawing-model] import   : %.3fs" swImport.Elapsed.TotalSeconds
            printfn "[drawing-model] merge    : %.3fs" swMerge.Elapsed.TotalSeconds
            printfn "[drawing-model] showDns  : %.3fs" swShowDns.Elapsed.TotalSeconds
            printfn "[drawing-model] adaptive : %.3fs" swAdaptive.Elapsed.TotalSeconds

            let n = newFlat.Count
            Expect.isGreaterThan n 1000 "fixture should contain at least 1k ellipses"
            Expect.equal mergedDrawing.annotations.flat.Count n "DrawingModel.flat count"
            Expect.equal mergedDrawing.annotations.rootGroup.subNodes.Count 1
                "exactly one imported group grafted under rootGroup"
            Expect.equal mergedDrawing.annotations.groupsLookup.Count n
                "groupsLookup must have one entry per annotation"
            Expect.isNotNull (adaptive :> obj) "AdaptiveDrawingModel constructed"

            // Soft regression guard. If any single phase blows up the model
            // (e.g. quadratic HashMap.union, or per-leaf adaptive cell init
            // doing surprising work), this catches it.
            let totalSec =
                swImport.Elapsed.TotalSeconds
                + swMerge.Elapsed.TotalSeconds
                + swShowDns.Elapsed.TotalSeconds
                + swAdaptive.Elapsed.TotalSeconds
            Expect.isLessThan totalSec 30.0
                (sprintf "end-to-end DrawingModel build too slow: %.2fs total for %d ellipses"
                    totalSec n)
        }
    ]

let tests (parameters : TestUtils.TestParameters) =
    testList "sbmtImport" [
        headerTests
        pointTests
        ellipseTests
        groupingTests
        fixtureTests parameters
    ]
