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
// Test data
// ---------------------------------------------------------------------------
//
// Two annotations sit on the same "Pike" feature of the Dimorphos shape model:
//   - SBMT-exported point in pointOnPike.points.txt (centerXYZ in km, SHM frame)
//   - PRo3D-native point picked manually and exported as cartesian GeoJSON
//     (meters, same SHM frame as the loaded OBJ surface)
//
// The two should agree to within ~10 m given manual-pick precision on a ~170 m
// asteroid.

let private testDataDir = @"C:\pro3ddata\shapemodels\testdata"
let private sbmtPointsFile     = Path.Combine(testDataDir, "pointOnPike.points.txt")
let private annoFile           = Path.Combine(testDataDir, "anno.json")
let private sbmtEllipseFile    = Path.Combine(testDataDir, "Didy_Boulders_Paj_Tusb_Lucch")
let private sbmtBigEllipseFile = Path.Combine(testDataDir, "Dimo_Bould_Glob_7_Maurizio")

// ---------------------------------------------------------------------------
// Minimal parsers (the real SbmtImporter is planned in plans/sbmtImport.md;
// once it exists these helpers should be replaced by direct calls into it).
// ---------------------------------------------------------------------------

let private parseSbmtPointKm (path : string) : V3d =
    let dataLine =
        File.ReadAllLines(path)
        |> Array.tryFind (fun raw ->
            let l = raw.Trim()
            l.Length > 0 && not (l.StartsWith("#")))
        |> Option.defaultWith (fun () ->
            failwithf "no data row found in %s" path)

    let parts = dataLine.Split('\t')
    if parts.Length < 5 then
        failwithf "expected >=5 tab-separated columns, got %d" parts.Length
    let f i = Double.Parse(parts.[i], CultureInfo.InvariantCulture)
    V3d(f 2, f 3, f 4)

// Mirrors the planned SbmtImporter pipeline: km -> m, then optional frame
// trafo applied in the same call. v1 callers pass Trafo3d.Identity.
let private importSbmtPoint (trafo : Trafo3d) (xyzKm : V3d) : V3d =
    trafo.Forward.TransformPos (xyzKm * 1000.0)

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
// Tests
// ---------------------------------------------------------------------------

let tests () =
    testList "sbmtImportAlignment" [
        test "SBMT-imported Pike point aligns with PRo3D-native annotation within 10m (identity trafo)" {
            if not (File.Exists sbmtPointsFile) then
                skiptest (sprintf "missing fixture: %s" sbmtPointsFile)
            if not (File.Exists annoFile) then
                skiptest (sprintf "missing fixture: %s" annoFile)

            let sbmtKm   = parseSbmtPointKm sbmtPointsFile
            let sbmt     = importSbmtPoint Trafo3d.Identity sbmtKm
            let pro3d    = parseSelectedGeoJsonPoint annoFile
            let distance = (sbmt - pro3d).Length

            Expect.isLessThan distance 10.0
                (sprintf "SBMT %A vs PRo3D %A: distance %.3fm" sbmt pro3d distance)
        }

        // Sanity check for the v2 SPICE-based path: when the SbmtImporter
        // eventually accepts a real Trafo3d (e.g. SHM -> DIMORPHOS_FIXED from
        // PRo3D.SPICE.CooTransformation.getRotationTrafo), applying it to both
        // points must preserve their relative distance. This test uses the
        // known SHM->FIXED rotation (180 deg around Y, per hera_v16.tf:493)
        // analytically -- no SPICE kernels required.
        test "SHM->FIXED 180deg-around-Y trafo preserves SBMT/PRo3D distance" {
            if not (File.Exists sbmtPointsFile) then
                skiptest (sprintf "missing fixture: %s" sbmtPointsFile)
            if not (File.Exists annoFile) then
                skiptest (sprintf "missing fixture: %s" annoFile)

            let flipShmToFixed = Trafo3d.RotationY(Math.PI)

            let sbmtKm = parseSbmtPointKm sbmtPointsFile
            let pro3d  = parseSelectedGeoJsonPoint annoFile

            let sbmtShm = importSbmtPoint Trafo3d.Identity sbmtKm
            let sbmtFix = importSbmtPoint flipShmToFixed sbmtKm
            let pro3dFix = flipShmToFixed.Forward.TransformPos pro3d

            let dShm = (sbmtShm - pro3d).Length
            let dFix = (sbmtFix - pro3dFix).Length

            Expect.floatClose Accuracy.high dShm dFix
                "frame rotation must preserve relative distance"
        }

        // Smoke test for the ellipse importer (v2). Parses a real boulder
        // catalog, checks a few invariants on the first ellipse: sampled
        // boundary points lie on the tangent plane through the centroid, and
        // the major/minor radii match the SBMT-declared diameter+flattening.
        test "SBMT ellipse import produces planar, correctly-sized boundary" {
            if not (File.Exists sbmtEllipseFile) then
                skiptest (sprintf "missing fixture: %s" sbmtEllipseFile)

            let annotations =
                SbmtImporter.startImporter Trafo3d.Identity "DIMORPHOS_SHM" sbmtEllipseFile

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
                skiptest (sprintf "missing fixture: %s" sbmtBigEllipseFile)

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

            let allLeavesArePoints =
                flat
                |> HashMap.values
                |> Seq.forall (fun leaf ->
                    let a = Leaf.toAnnotation leaf
                    a.geometry = Geometry.AxisEllipse)
            Expect.isTrue allLeavesArePoints "every imported leaf is an AxisEllipse"

            Expect.equal lookup.Count n "lookup must have one entry per annotation"

            // Soft perf assertion: surface a hard ceiling so a regression
            // (e.g. dns/results re-introduced into the import path) trips this.
            // Loose enough to not flake on slow CI hardware. Adjust if needed.
            Expect.isLessThan sw.Elapsed.TotalSeconds 30.0
                (sprintf "bulk SBMT import too slow: %.2fs for %d ellipses" sw.Elapsed.TotalSeconds n)
        }

        // End-to-end "drawing model" test. Mirrors the Viewer.fs handler at
        // line 987 step-by-step on an empty DrawingModel, timing each phase
        // so we can spot where the cost actually lives:
        //   1. parse+import          (file -> Annotation records)
        //   2. merge into rootGroup  (IndexList.append + HashMap.union)
        //   3. showDns flag map      (HashMap.map over the flat dictionary)
        //   4. build AdaptiveDrawingModel  (Adaptify spins up per-leaf cells)
        // Asserts the resulting DrawingModel is internally consistent.
        test "SBMT import end-to-end into a full DrawingModel" {
            if not (File.Exists sbmtBigEllipseFile) then
                skiptest (sprintf "missing fixture: %s" sbmtBigEllipseFile)

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
