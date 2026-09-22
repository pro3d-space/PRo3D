/// Regression case for the bulk-edit panel's dip-direction rose.
///
/// The panel aggregates dip azimuths over the whole multi-selection
/// (ViewerGUI.viewBulkAnnotationProperties), which is the path a group "Select All"
/// drives. These tests load a generated fixture of many mixed annotations and check the
/// aggregate the panel would bin, using the same RoseDiagram.includes gate the panel
/// applies, so a change to the toggle semantics or the NaN handling fails here.
///
/// The fixture is produced by tools/testdata/make_bulk_annotations.py and is checked in
/// under src/Tests/data (small and text-based, per docs/tests/TestData.md). It needs
/// neither a GL context nor the PRo3D.Resources.TestData submodule, so it always runs.
module BulkAnnotationRoseTest

open System
open System.IO

open Aardvark.Base
open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Viewer

/// Mirrors Render.dataDir; duplicated so this list does not depend on the GL-bearing
/// TestHelpers module, which would drag a runtime into a pure-data test.
let private dataDir = Path.Combine(__SOURCE_DIRECTORY__, "data")
let private fixture = Path.Combine(dataDir, "bulk-rose-annotations.pro3d.ann")

/// Counts baked into the generator's distribution at seed 20260825, count 250.
/// They are asserted rather than recomputed so that regenerating the fixture with a
/// different seed or mix is a deliberate, visible change.
let private expectedTotal = 250
let private expectedGroupName = "Bulk rose test"
let private expectedBothToggles = 219
let private expectedDnSOnly = 166
let private expectedPolylineOnly = 53

/// The 3000-annotation version of the same generated distribution. Too large for the
/// main repo, so it lives in PRo3D.Resources.TestData alongside the OPC fixtures
/// (docs/tests/TestData.md) and the tests using it skip when the submodule is absent.
/// This is the one that makes a group "Select All" big enough to measure.
let private largeFixture =
    Path.Combine(__SOURCE_DIRECTORY__, "resources", "annotations",
                 "bulk-rose-annotations-3000.pro3d.ann")

let private largeSkipReason () =
    if File.Exists largeFixture then None
    else Some (sprintf "no bulk annotation fixture at %s - run: git submodule update --init src/Tests/resources"
                       (Path.GetFullPath largeFixture))

let private expectedLargeTotal = 3000
let private expectedLargeBothToggles = 2620
let private expectedLargeDnSOnly = 1739
let private expectedLargePolylineOnly = 881

let private loadFixture () =
    DrawingUtilities.IO.loadAnnotationsFromFile fixture

/// Every annotation in the fixture, as the flat map the bulk path reads.
let private annotationsOf (d : Annotations) =
    d.annotations.flat
    |> HashMap.choose (fun _ leaf ->
        match leaf with
        | Leaf.Annotations a -> Some a
        | _ -> None)

/// The aggregate the panel produces for a given pair of toggles: the same fold over
/// (geometry, dipAzimuth) gated by RoseDiagram.includes, with the missing-dnsResults
/// case mapped to NaN exactly as AVal.bindAdaptiveOption does in ViewerGUI.
let private rosedAzimuths usePolyline useDnS (d : Annotations) =
    annotationsOf d
    |> HashMap.fold (fun acc _ (a : Annotation) ->
        let az =
            match a.dnsResults with
            | Some dns -> dns.dipAzimuth
            | None -> nan
        if RoseDiagram.includes usePolyline useDnS a.geometry az then az :: acc else acc) []

let tests () =
    testList "Bulk annotation rose" [

        test "fixture loads and lands in one group" {
            let d = loadFixture ()
            let anns = annotationsOf d
            Expect.equal (HashMap.count anns) expectedTotal "annotation count"

            // A single group holding everything is what makes "Select All" exercise the
            // bulk path in one action.
            let groups = d.annotations.rootGroup.subNodes
            Expect.equal (IndexList.count groups) 1 "exactly one group under root"
            Expect.isEmpty (IndexList.toList d.annotations.rootGroup.leaves)
                "no annotations directly under root"
            match IndexList.tryFirst groups with
            | None -> failtest "group missing"
            | Some g ->
                Expect.equal g.name expectedGroupName "group name"
                Expect.equal (IndexList.count g.leaves) expectedTotal
                    "group holds every annotation"
        }

        test "fixture covers every branch of the rose gate" {
            let anns = loadFixture () |> annotationsOf
            let countOf geo =
                anns |> HashMap.filter (fun _ a -> a.geometry = geo) |> HashMap.count
            // all three matter: two the toggles admit, one they must both reject
            Expect.isGreaterThan (countOf Geometry.DnS) 0 "DnS present"
            Expect.isGreaterThan (countOf Geometry.Polyline) 0 "Polyline present"
            Expect.isGreaterThan (countOf Geometry.Polygon) 0 "Polygon present"
            // and annotations that carry no dip and strike at all
            let noDns = anns |> HashMap.filter (fun _ a -> a.dnsResults.IsNone) |> HashMap.count
            Expect.isGreaterThan noDns 0 "some annotations have no dnsResults"
        }

        test "toggles select the documented subsets" {
            let d = loadFixture ()
            Expect.equal (rosedAzimuths true true d |> List.length) expectedBothToggles
                "Polyline + DnS"
            Expect.equal (rosedAzimuths false true d |> List.length) expectedDnSOnly
                "DnS only"
            Expect.equal (rosedAzimuths true false d |> List.length) expectedPolylineOnly
                "Polyline only"
            Expect.isEmpty (rosedAzimuths false false d) "both toggles off yields nothing"

            // the two single-toggle sets partition the both-toggles set
            Expect.equal (expectedDnSOnly + expectedPolylineOnly) expectedBothToggles
                "DnS and Polyline subsets partition the combined set"
        }

        test "annotations without dip and strike are dropped, not counted as zero" {
            let d = loadFixture ()
            let anns = annotationsOf d
            let admissible =
                anns
                |> HashMap.filter (fun _ a ->
                    (a.geometry = Geometry.Polyline || a.geometry = Geometry.DnS))
                |> HashMap.count
            let selected = rosedAzimuths true true d |> List.length
            let missing =
                anns
                |> HashMap.filter (fun _ a ->
                    (a.geometry = Geometry.Polyline || a.geometry = Geometry.DnS)
                    && a.dnsResults.IsNone)
                |> HashMap.count
            Expect.isGreaterThan missing 0 "fixture must contain such annotations"
            Expect.equal selected (admissible - missing)
                "exactly the ones without dnsResults are dropped"
            // a zero would sit in the north bin and bias the mean
            Expect.isFalse (rosedAzimuths true true d |> List.contains 0.0)
                "no annotation contributes a spurious 0 azimuth"
        }

        test "no NaN reaches the histogram" {
            let azimuths = loadFixture () |> rosedAzimuths true true
            Expect.isFalse (azimuths |> List.exists Double.IsNaN) "no NaN azimuth survives"
            // a NaN would make both sums NaN, so the mean and R would be NaN too
            let sumSin = azimuths |> List.sumBy (fun a -> sin (a * Math.PI / 180.0))
            let sumCos = azimuths |> List.sumBy (fun a -> cos (a * Math.PI / 180.0))
            Expect.isFalse (Double.IsNaN sumSin) "circular sum (sin) is finite"
            Expect.isFalse (Double.IsNaN sumCos) "circular sum (cos) is finite"
        }

        test "azimuths are in range and the sample has a preferred direction" {
            let azimuths = loadFixture () |> rosedAzimuths true true
            Expect.isTrue (azimuths |> List.forall (fun a -> a >= 0.0 && a < 360.0))
                "every azimuth is a compass bearing"

            // The generator lays down two conjugate sets, so the mean resultant length
            // must clear RoseDiagram's minResultant cutoff - otherwise the fixture would
            // not exercise the mean-direction line at all.
            let n = float (List.length azimuths)
            let sumSin = azimuths |> List.sumBy (fun a -> sin (a * Math.PI / 180.0))
            let sumCos = azimuths |> List.sumBy (fun a -> cos (a * Math.PI / 180.0))
            let resultant = sqrt (sumSin * sumSin + sumCos * sumCos) / n
            Expect.isGreaterThan resultant 0.05 "resultant clears minResultant"
            let mean = (atan2 sumSin sumCos) * 180.0 / Math.PI
            let mean = ((mean % 360.0) + 360.0) % 360.0
            Expect.isTrue (mean > 40.0 && mean < 80.0)
                (sprintf "mean direction sits between the two conjugate sets (was %.2f)" mean)
        }
    ]

/// The large fixture from the PRo3D.Resources.TestData submodule. Same generator and the
/// same distribution as the checked-in one, at the scale a group "Select All" actually
/// reaches, so it is the case to profile the bulk-edit panel against.
let largeTests () =
    testList "Bulk annotation rose (large fixture)" [
        match largeSkipReason () with
        | Some reason ->
            test "large fixture (skipped)" { skiptest reason }
        | None ->

        test "3000-annotation fixture loads into one group" {
            let d = DrawingUtilities.IO.loadAnnotationsFromFile largeFixture
            Expect.equal (HashMap.count (annotationsOf d)) expectedLargeTotal "annotation count"
            let groups = d.annotations.rootGroup.subNodes
            Expect.equal (IndexList.count groups) 1 "exactly one group under root"
            match IndexList.tryFirst groups with
            | None -> failtest "group missing"
            | Some g ->
                Expect.equal (IndexList.count g.leaves) expectedLargeTotal
                    "one Select All covers every annotation"
        }

        test "large fixture yields the documented rose aggregate" {
            let d = DrawingUtilities.IO.loadAnnotationsFromFile largeFixture
            Expect.equal (rosedAzimuths true true d |> List.length) expectedLargeBothToggles
                "Polyline + DnS"
            Expect.equal (rosedAzimuths false true d |> List.length) expectedLargeDnSOnly
                "DnS only"
            Expect.equal (rosedAzimuths true false d |> List.length) expectedLargePolylineOnly
                "Polyline only"
            let azimuths = rosedAzimuths true true d
            Expect.isFalse (azimuths |> List.exists Double.IsNaN) "no NaN azimuth survives"
            Expect.isTrue (azimuths |> List.forall (fun a -> a >= 0.0 && a < 360.0))
                "every azimuth is a compass bearing"
        }
    ]
