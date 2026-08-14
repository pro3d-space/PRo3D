module RegionOpsTests

open Expecto
open FsCheck
open Aardvark.Base
open PRo3D.Base.Geometry
open PRo3D.Base.Geometry.RegionOps

// ---------------------------------------------------------------------------------------------
// fixtures
// ---------------------------------------------------------------------------------------------

let private ring (pts : (float * float) list) =
    pts |> List.map (fun (x, y) -> V2d(x, y)) |> Array.ofList

let private regionOf pts =
    match ofRing2d (ring pts) with
    | Some r -> r
    | None -> failwithf "could not build a region from %A" pts

let private square = regionOf [ 0.,0.;  10.,0.;  10.,10.;  0.,10. ]
let private lShape = regionOf [ 0.,0.;  6.,0.;   6.,2.;    2.,2.;   2.,6.;  0.,6. ]
let private far    = regionOf [ 50.,50.; 60.,50.; 60.,60.; 50.,60. ]
/// overlaps the square, so merging the two yields one ring
let private overlapping = regionOf [ 5.,5.; 15.,5.; 15.,15.; 5.,15. ]

let private failIfAny (vs : string list) =
    match vs with
    | [] -> ()
    | vs -> failtest (String.concat "; " vs)

// ---------------------------------------------------------------------------------------------
// generators
// ---------------------------------------------------------------------------------------------

/// Angle-ordered vertices, so the ring is simple and stays simple when a vertex is dropped.
let private simpleRingGen =
    gen {
        let! n = Gen.choose (3, 10)
        let! radii = Gen.listOfLength n (Gen.choose (2, 20))
        return
            radii
            |> List.mapi (fun i r ->
                let a = Constant.PiTimesTwo * float i / float n
                V2d(float r * cos a, float r * sin a))
            |> Array.ofList
    }

let private shrinkRing (ring : V2d[]) =
    if ring.Length <= 3 then Seq.empty
    else seq { for i in 0 .. ring.Length - 1 -> Array.append ring.[.. i - 1] ring.[i + 1 ..] }

let private simpleRingArb = Arb.fromGenShrink (simpleRingGen, shrinkRing)

/// A stroke through the origin at a random angle, with both ends far outside any generated ring
/// (whose radii are at most 20). Guaranteed to satisfy cutsThrough, so the cutting branch is
/// actually exercised rather than accidentally skipped.
let private cuttingLineGen =
    gen {
        let! deg = Gen.choose (0, 179)
        let a = float deg * Constant.RadiansPerDegree
        let d = V2d(cos a, sin a)
        return d * -100.0, d * 100.0
    }

/// Parallel to a cutting stroke but pushed well beyond the ring, so it must be a no-op.
let private missingLineGen =
    gen {
        let! deg = Gen.choose (0, 179)
        let a = float deg * Constant.RadiansPerDegree
        let d = V2d(cos a, sin a)
        let n = V2d(-d.Y, d.X)
        return d * -100.0 + n * 60.0, d * 100.0 + n * 60.0
    }

// ---------------------------------------------------------------------------------------------

let tests () =
    testList "RegionOps" [

        testList "construction" [

            test "a square has the expected area" {
                Expect.floatClose Accuracy.medium (area square) 100.0 "10x10"
            }

            test "an L has the expected area" {
                // 6x2 base plus 2x4 upright = 12 + 8
                Expect.floatClose Accuracy.medium (area lShape) 20.0 "L area"
            }

            test "a degenerate ring makes no region" {
                Expect.isNone (ofRing2d (ring [ 0.,0.; 1.,0.; 2.,0. ])) "collinear encloses nothing"
                Expect.isNone (ofRing2d (ring [ 0.,0.; 1.,0. ])) "two points enclose nothing"
            }

            test "containment respects holes" {
                // a ring with a square hole punched out by subtracting a smaller square
                let outer = regionOf [ 0.,0.; 10.,0.; 10.,10.; 0.,10. ]
                let inner = regionOf [ 4.,4.; 6.,4.; 6.,6.; 4.,6. ]
                let withHole = PolyRegion<V3d>.Difference(outer, inner, interpolate)

                Expect.isTrue (contains (V2d(1.0, 1.0)) withHole) "a point in the ring is inside"
                Expect.isFalse (contains (V2d(5.0, 5.0)) withHole) "a point in the hole is outside"
                Expect.floatClose Accuracy.medium (area withHole) 96.0 "100 minus the 2x2 hole"
                Expect.equal (holes withHole |> List.length) 1 "the hole is reported as a hole"
            }
        ]

        testList "cut" [

            test "a stroke across the square splits it in two" {
                let p0, p1 = V2d(-5.0, 5.0), V2d(15.0, 5.0)
                Expect.isTrue (cutsThrough p0 p1 square) "a stroke drawn across must cut"
                let pieces = cut p0 p1 square
                Expect.equal pieces.Length 2 "two halves"
                failIfAny (RegionInvariants.cutViolations p0 p1 square)
            }

            test "a stroke stopping inside is a no-op" {
                // starts outside, ends in the middle: it does not cut through
                let p0, p1 = V2d(-5.0, 5.0), V2d(5.0, 5.0)
                Expect.isFalse (cutsThrough p0 p1 square) "an unfinished stroke must not cut"
                Expect.equal (cut p0 p1 square).Length 1 "the region is returned unchanged"
                failIfAny (RegionInvariants.cutViolations p0 p1 square)
            }

            test "a stroke entirely inside is a no-op" {
                let p0, p1 = V2d(3.0, 5.0), V2d(7.0, 5.0)
                Expect.isFalse (cutsThrough p0 p1 square) "both ends inside is not a cut"
            }

            test "a stroke that misses is a no-op, even when its extension would hit" {
                // parallel to a cutting stroke, offset well clear of the square
                let p0, p1 = V2d(-5.0, 50.0), V2d(15.0, 50.0)
                Expect.isFalse (cutsThrough p0 p1 square) "a stroke that misses must not cut"
                Expect.equal (cut p0 p1 square).Length 1 "unchanged"
            }

            test "cutting is a no-op on a region the stroke never reaches" {
                let p0, p1 = V2d(-5.0, 5.0), V2d(15.0, 5.0)
                Expect.isFalse (cutsThrough p0 p1 far) "the far region is untouched"
            }

            test "a concave shape cut through the notch obeys the invariants" {
                let p0, p1 = V2d(-5.0, 1.0), V2d(15.0, 1.0)
                failIfAny (RegionInvariants.cutViolations p0 p1 lShape)
            }
        ]

        testList "merge" [

            test "overlapping squares merge into one ring" {
                let m = merge square overlapping
                Expect.equal (outerRings m |> List.length) 1 "one outer contour"
                Expect.isEmpty (holes m) "no holes"
                failIfAny (RegionInvariants.mergeViolations square overlapping)
            }

            test "disjoint squares merge into two components" {
                let m = merge square far
                Expect.equal (outerRings m |> List.length) 2 "two outer contours"
                Expect.floatClose Accuracy.medium (area m) 200.0 "areas add"
                failIfAny (RegionInvariants.mergeViolations square far)
            }

            test "merging with itself changes nothing" {
                failIfAny (RegionInvariants.mergeIdempotenceViolations square)
            }
        ]

        testList "round trip" [

            test "square: cut then merge restores the original" {
                failIfAny (RegionInvariants.roundTripViolations (V2d(-5.0, 5.0)) (V2d(15.0, 5.0)) square)
            }

            test "L shape: cut then merge restores the original" {
                failIfAny (RegionInvariants.roundTripViolations (V2d(-5.0, 1.0)) (V2d(15.0, 1.0)) lShape)
            }
        ]

        testList "generated" [

            test "a stroke through the centre always cuts, and obeys the invariants" {
                Prop.forAll (Arb.fromGenShrink ((Gen.zip simpleRingGen cuttingLineGen), fun (r, l) -> shrinkRing r |> Seq.map (fun r -> r, l)))
                    (fun (ringPts, (p0, p1)) ->
                        match ofRing2d ringPts with
                        | None -> true          // degenerate generation, nothing to assert
                        | Some r ->
                            match RegionInvariants.cutViolations p0 p1 r with
                            | [] -> true
                            | vs -> failwith (String.concat "; " vs))
                |> Check.QuickThrowOnFailure
            }

            test "a stroke clear of the ring never cuts" {
                Prop.forAll (Arb.fromGen (Gen.zip simpleRingGen missingLineGen))
                    (fun (ringPts, (p0, p1)) ->
                        match ofRing2d ringPts with
                        | None -> true
                        | Some r ->
                            if cutsThrough p0 p1 r then failwith "a stroke clear of the ring reported a cut"
                            elif (cut p0 p1 r).Length <> 1 then failwith "a no-op cut changed the region"
                            else true)
                |> Check.QuickThrowOnFailure
            }

            test "cut then merge round-trips for any simple ring" {
                Prop.forAll (Arb.fromGenShrink ((Gen.zip simpleRingGen cuttingLineGen), fun (r, l) -> shrinkRing r |> Seq.map (fun r -> r, l)))
                    (fun (ringPts, (p0, p1)) ->
                        match ofRing2d ringPts with
                        | None -> true
                        | Some r ->
                            match RegionInvariants.roundTripViolations p0 p1 r with
                            | [] -> true
                            | vs -> failwith (String.concat "; " vs))
                |> Check.QuickThrowOnFailure
            }

            test "merge invariants hold for any two simple rings" {
                Prop.forAll (Arb.fromGen (Gen.zip simpleRingGen simpleRingGen))
                    (fun (a, b) ->
                        // offset the second so the pair spans overlapping and disjoint cases
                        let b = b |> Array.map (fun p -> p + V2d(15.0, 0.0))
                        match ofRing2d a, ofRing2d b with
                        | Some ra, Some rb ->
                            match RegionInvariants.mergeViolations ra rb with
                            | [] -> true
                            | vs -> failwith (String.concat "; " vs)
                        | _ -> true)
                |> Check.QuickThrowOnFailure
            }
        ]
    ]
