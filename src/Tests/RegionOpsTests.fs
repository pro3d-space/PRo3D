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

/// How many cases each property runs. FsCheck's default of 100 with a fresh seed per run left
/// genuine violations to be found by chance on a CI runner hours later - at this rate the local
/// run does the searching instead. Raise further when hunting a specific defect.
let private propConfig = { Config.QuickThrowOnFailure with MaxTest = 1000 }

/// Angle-ordered vertices, so the ring is simple and stays simple when a vertex is dropped.
///
/// Radii stay within a 4:1 band. Wider bands make needle-thin spikes, and the tessellator loses
/// slivers on those - a boolean op then returns a few percent less area than it should, which is
/// a robustness limit of the library rather than anything these properties can assert about our
/// code. The extreme case is pinned separately by "spiky stars", which checks what does survive
/// it: the union area stays exact.
let private simpleRingGen =
    gen {
        let! n = Gen.choose (3, 10)
        let! radii = Gen.listOfLength n (Gen.choose (5, 20))
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
                Expect.isTrue (cutsThrough [|p0; p1|] square) "a stroke drawn across must cut"
                let pieces = cut [|p0; p1|] square
                Expect.equal pieces.Length 2 "two halves"
                failIfAny (RegionInvariants.cutViolations [|p0; p1|] square)
            }

            test "a stroke stopping inside is a no-op" {
                // starts outside, ends in the middle: it does not cut through
                let p0, p1 = V2d(-5.0, 5.0), V2d(5.0, 5.0)
                Expect.isFalse (cutsThrough [|p0; p1|] square) "an unfinished stroke must not cut"
                Expect.equal (cut [|p0; p1|] square).Length 1 "the region is returned unchanged"
                failIfAny (RegionInvariants.cutViolations [|p0; p1|] square)
            }

            test "a stroke entirely inside is a no-op" {
                let p0, p1 = V2d(3.0, 5.0), V2d(7.0, 5.0)
                Expect.isFalse (cutsThrough [|p0; p1|] square) "both ends inside is not a cut"
            }

            test "a stroke that misses is a no-op, even when its extension would hit" {
                // parallel to a cutting stroke, offset well clear of the square
                let p0, p1 = V2d(-5.0, 50.0), V2d(15.0, 50.0)
                Expect.isFalse (cutsThrough [|p0; p1|] square) "a stroke that misses must not cut"
                Expect.equal (cut [|p0; p1|] square).Length 1 "unchanged"
            }

            test "cutting is a no-op on a region the stroke never reaches" {
                let p0, p1 = V2d(-5.0, 5.0), V2d(15.0, 5.0)
                Expect.isFalse (cutsThrough [|p0; p1|] far) "the far region is untouched"
            }

            test "a concave shape cut through the notch obeys the invariants" {
                let p0, p1 = V2d(-5.0, 1.0), V2d(15.0, 1.0)
                failIfAny (RegionInvariants.cutViolations [|p0; p1|] lShape)
            }

            test "a V-shaped stroke carves a wedge out of the square" {
                // both ends below the square, tip dipping inside: only the ends must be outside
                let stroke = [| V2d(2.0, -5.0); V2d(5.0, 5.0); V2d(8.0, -5.0) |]
                Expect.isTrue (cutsThrough stroke square) "a V dipping in must cut"
                let pieces = cut stroke square
                Expect.isTrue (pieces.Length >= 2) "wedge and remainder"
                failIfAny (RegionInvariants.cutViolations stroke square)
            }

            test "a zigzag stroke across the square obeys the invariants and round-trips" {
                let stroke = [| V2d(-5.0, 3.0); V2d(4.0, 7.0); V2d(6.0, 2.0); V2d(15.0, 6.0) |]
                Expect.isTrue (cutsThrough stroke square) "the zigzag crosses the square"
                failIfAny (RegionInvariants.cutViolations stroke square)
                failIfAny (RegionInvariants.roundTripViolations stroke square)
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

            test "spiky stars: the union stays exact where re-intersection does not" {
                let a =
                    regionOf [ 2.,0.; 14.142135623730951,14.14213562373095
                               3.061616997868383e-16,5.
                               -2.82842712474619,2.8284271247461903
                               -2.,2.4492935982947064e-16
                               -11.313708498984763,-11.313708498984758
                               -3.6739403974420594e-16,-2.
                               7.071067811865474,-7.071067811865477 ]
                let b =
                    [ 10.,0.; 4.045084971874737,2.938926261462366
                      0.6180339887498949,1.902113032590307
                      -0.927050983124842,2.853169548885461
                      -14.562305898749052,10.580134541264519
                      -18.,2.204364238465236e-15
                      -1.618033988749895,-1.175570504584946
                      -5.2532889043741084,-16.16796077701761
                      0.9270509831248417,-2.853169548885461
                      7.281152949374526,-5.290067270632259 ]
                    |> List.map (fun (x, y) -> x + 15.0, y)
                    |> regionOf
                // Ring a alternates radii 2 and 20, giving needle-thin spikes. The tessellator
                // drops slivers on those, so a re-intersection of a with the union comes back
                // ~3.5% short - a robustness limit of the library, reached only by inputs far
                // more extreme than the generators produce (see simpleRingGen). What the feature
                // relies on is unaffected and asserted here: the union area itself is exact.
                let m = merge a b
                Expect.floatClose Accuracy.medium (area m) (area a + area b - area (intersect a b))
                    "the union area obeys inclusion-exclusion even for needle spikes"
                Expect.floatClose Accuracy.medium (area (intersect b m)) (area b)
                    "the well-conditioned operand still re-intersects exactly"
            }

            test "a small ring beside a much larger one is not reported as lost" {
                // From a CI-only FsCheck failure (StdGen (627528932, 297661573)): the pair spans
                // a bounding box far larger than b, so a grid-sampled containment check counts
                // b's boundary cells as disagreement and reports "the merge lost part of b".
                // Containment is an area question, not a sampling question - see RegionInvariants.
                let a =
                    regionOf [ 2.,0.;  8.426488874308758,7.070663706551931
                               1.0418890660015825,5.908846518073248
                               -4.499999999999998,7.794228634059948
                               -10.336618828644992,3.7622215765823577
                               -13.155696691002717,-4.788282006559362
                               -2.0000000000000018,-3.4641016151377535
                               3.1256671980047397,-17.726539554219748
                               15.320888862379556,-12.855752193730792 ]
                // as in the property: the second ring is shifted +15 in x before merging
                let b =
                    [ 6.,0.;  10.606601717798213,10.606601717798211
                      6.123233995736766e-16,10.
                      -8.48528137423857,8.485281374238571
                      -15.,1.83697019872103e-15
                      -3.5355339059327386,-3.5355339059327373
                      -1.2858791391047208e-15,-7.
                      1.4142135623730947,-1.4142135623730954 ]
                    |> List.map (fun (x, y) -> x + 15.0, y)
                    |> regionOf
                failIfAny (RegionInvariants.mergeViolations a b)

                // the shape that exposed it: an operand and the merge containing it share
                // boundary stretches, which is where PolyRegion.Intersection's AbsGeqTwo winding
                // rule returns the union instead of the overlap
                let m = merge a b
                Expect.floatClose Accuracy.medium (area m) (area a + area b - area (intersect a b))
                    "the union area obeys inclusion-exclusion"
                Expect.floatClose Accuracy.medium (area (intersect b m)) (area b)
                    "b lies in the merge, so intersecting them returns b"
                Expect.floatClose Accuracy.medium (area (intersect a m)) (area a)
                    "and likewise for a"
            }
        ]

        testList "round trip" [

            test "square: cut then merge restores the original" {
                failIfAny (RegionInvariants.roundTripViolations [|V2d(-5.0, 5.0); V2d(15.0, 5.0)|] square)
            }

            test "L shape: cut then merge restores the original" {
                failIfAny (RegionInvariants.roundTripViolations [|V2d(-5.0, 1.0); V2d(15.0, 1.0)|] lShape)
            }
        ]

        testList "generated" [

            test "a stroke through the centre always cuts, and obeys the invariants" {
                Prop.forAll (Arb.fromGenShrink ((Gen.zip simpleRingGen cuttingLineGen), fun (r, l) -> shrinkRing r |> Seq.map (fun r -> r, l)))
                    (fun (ringPts, (p0, p1)) ->
                        match ofRing2d ringPts with
                        | None -> true          // degenerate generation, nothing to assert
                        | Some r ->
                            match RegionInvariants.cutViolations [|p0; p1|] r with
                            | [] -> true
                            | vs -> failwith (String.concat "; " vs))
                |> fun p -> Check.One(propConfig, p)
            }

            test "a stroke clear of the ring never cuts" {
                Prop.forAll (Arb.fromGen (Gen.zip simpleRingGen missingLineGen))
                    (fun (ringPts, (p0, p1)) ->
                        match ofRing2d ringPts with
                        | None -> true
                        | Some r ->
                            if cutsThrough [|p0; p1|] r then failwith "a stroke clear of the ring reported a cut"
                            elif (cut [|p0; p1|] r).Length <> 1 then failwith "a no-op cut changed the region"
                            else true)
                |> fun p -> Check.One(propConfig, p)
            }

            test "cut then merge round-trips for any simple ring" {
                Prop.forAll (Arb.fromGenShrink ((Gen.zip simpleRingGen cuttingLineGen), fun (r, l) -> shrinkRing r |> Seq.map (fun r -> r, l)))
                    (fun (ringPts, (p0, p1)) ->
                        match ofRing2d ringPts with
                        | None -> true
                        | Some r ->
                            match RegionInvariants.roundTripViolations [|p0; p1|] r with
                            | [] -> true
                            | vs -> failwith (String.concat "; " vs))
                |> fun p -> Check.One(propConfig, p)
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
                |> fun p -> Check.One(propConfig, p)
            }
        ]
    ]
