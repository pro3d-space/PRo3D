/// Replays every fixture in data/regions through the region invariants - one test per file, so
/// adding a regression case is dropping in a file exported from the geometry lab.
module RegionFixtureTests

open System.IO
open Expecto
open Aardvark.Base
open PRo3D.Base.Geometry
open PRo3D.Base.Geometry.RegionOps

let private fixtureDir = Path.Combine(__SOURCE_DIRECTORY__, "data", "regions")

/// Strokes derived from the region's bounds: through the centre at four angles, with both ends
/// well outside. Whether each one actually cuts depends on the shape - cutViolations and
/// roundTripViolations state the right facts for both branches, so no case analysis is needed.
let private strokesFor (r : Region) =
    let bb = bounds r
    let reach = bb.Size.Length * 2.0 + 1.0
    [ for deg in [ 0.0; 45.0; 90.0; 135.0 ] do
        let a = deg * Constant.RadiansPerDegree
        let d = V2d(cos a, sin a)
        yield bb.Center - d * reach, bb.Center + d * reach ]

let private replay (path : string) =
    let regions = RegionFixture.read (File.ReadAllText path)
    Expect.isNonEmpty regions "the fixture parsed to no regions - broken file, not an empty case"
    let violations =
        [
            for i, r in List.indexed regions do
                for v in RegionInvariants.mergeIdempotenceViolations r do
                    yield sprintf "region %d: %s" i v
                for p0, p1 in strokesFor r do
                    for v in RegionInvariants.cutViolations p0 p1 r do
                        yield sprintf "region %d, stroke %A .. %A: %s" i p0 p1 v
                    for v in RegionInvariants.roundTripViolations p0 p1 r do
                        yield sprintf "region %d, stroke %A .. %A: %s" i p0 p1 v

            for i, a in List.indexed regions do
                for j, b in List.indexed regions do
                    if i < j then
                        for v in RegionInvariants.mergeViolations a b do
                            yield sprintf "regions %d and %d: %s" i j v
        ]
    match violations with
    | [] -> ()
    | vs -> failtest (String.concat "\n" vs)

let tests () =
    let files =
        if Directory.Exists fixtureDir then
            Directory.GetFiles(fixtureDir, "*" + RegionFixture.Extension) |> Array.sort
        else
            [||]
    testList "region fixtures" [
        // the seed corpus is committed, so an empty directory means a broken path, not "no cases"
        yield test "the fixture directory holds at least the seed corpus" {
            Expect.isTrue (files.Length > 0) (sprintf "no %s files under %s" RegionFixture.Extension fixtureDir)
        }
        for f in files do
            yield test (Path.GetFileName f) { replay f }
    ]
