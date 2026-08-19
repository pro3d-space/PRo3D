/// Checkpoint 1 of plans/viewerIntegration.md: the pure annotation-level boolean operations,
/// before any viewer message exists.
module AnnotationRegionOpsTests

open Expecto
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive
open PRo3D.Base.Annotation
open PRo3D.Base.Geometry
open PRo3D.Base.Geometry.AnnotationRegionOps

// ---------------------------------------------------------------------------------------------
// fixtures
// ---------------------------------------------------------------------------------------------

/// A polygon annotation from bare points; everything else defaulted through Annotation.make.
let private annoOf (pts : List<V3d>) =
    { Annotation.make Projection.Linear None Geometry.Polygon None { c = C4b.Red } Annotation.Initial.thickness "test" with
        points = IndexList.ofList pts }

let private flat (pts : List<float * float>) =
    annoOf (pts |> List.map (fun (x, y) -> V3d(x, y, 0.0)))

/// The identity projection: vertices stay where the blend put them.
let private noProjection : V3d -> Option<V3d> = fun _ -> None

/// Marks every vertex it touches, so tests can tell invented vertices from survivors.
let private markZ (z : float) : V3d -> Option<V3d> = fun v -> Some (V3d(v.X, v.Y, z))

let private squareA = flat [ 0.,0.;  10.,0.;  10.,10.;  0.,10. ]
let private squareB = flat [ 5.,5.;  15.,5.;  15.,15.;  5.,15. ]
let private farC    = flat [ 50.,50.; 60.,50.; 60.,60.; 50.,60. ]

/// U-shape and a bar capping its opening: their union encloses a hole.
let private uShape = flat [ 0.,0.; 25.,0.; 25.,25.; 17.,25.; 17.,8.; 8.,8.; 8.,25.; 0.,25. ]
let private bar    = flat [ -1.,23.; 26.,23.; 26.,30.; -1.,30. ]

/// Ring area via the region machinery, for world rings lying in z = 0.
let private ringArea (ring : V3d[]) =
    match RegionOps.ofRing ring with
    | Some r -> RegionOps.area r
    | None -> 0.0

let private expectOk (r : Result<list<V3d[]>, Refusal>) =
    match r with
    | Result.Ok rings -> rings
    | Result.Error e -> failtestf "expected Ok, got %A" e

// ---------------------------------------------------------------------------------------------

let tests () =
    testList "AnnotationRegionOps" [

        testList "charts and regions" [

            test "toRegion returns the input world points exactly" {
                // points on a tilted plane (z = x), so the chart is a genuine projection
                let anno = annoOf [ V3d(0.,0.,0.); V3d(10.,0.,10.); V3d(10.,10.,10.); V3d(0.,10.,0.) ]
                let chart =
                    match commonChart [ anno ] with
                    | Some c -> c
                    | None -> failtest "no chart for a clean tilted ring"
                let region =
                    match toRegion chart anno with
                    | Some r -> r
                    | None -> failtest "the ring did not project"
                let rings = RegionOps.outerRings region
                Expect.equal rings.Length 1 "one ring"
                match rings with
                | [ ring ] ->
                    Expect.equal ring.Length 4 "four vertices"
                    for p in anno.points do
                        Expect.isTrue
                            (ring |> Array.exists (fun v -> Vec.Distance(v, p) < 1e-9))
                            (sprintf "input point %A survives verbatim" p)
                | _ -> failtest "unreachable"
            }

            test "commonChart refuses degenerate input" {
                Expect.isNone (commonChart [ flat [ 0.,0.; 1.,1. ] ]) "two points admit no plane"
                Expect.isNone (commonChart []) "no annotations, no chart"
            }

            test "toRegion refuses a degenerate ring" {
                let chart =
                    match commonChart [ squareA ] with
                    | Some c -> c
                    | None -> failtest "square must have a chart"
                Expect.isNone (toRegion chart (flat [ 0.,0.; 1.,0.; 2.,0. ])) "collinear encloses nothing"
            }
        ]

        testList "union" [

            test "fewer than two operands is refused" {
                Expect.equal (union noProjection []) (Result.Error TooFewAnnotations) "empty"
                Expect.equal (union noProjection [ squareA ]) (Result.Error TooFewAnnotations) "single"
            }

            test "overlapping squares union to one ring with inclusion-exclusion area" {
                let rings = expectOk (union noProjection [ squareA; squareB ])
                Expect.equal rings.Length 1 "one component"
                match rings with
                | [ ring ] ->
                    Expect.floatClose Accuracy.medium (ringArea ring) 175.0 "100 + 100 - 25"
                | _ -> failtest "unreachable"
            }

            test "disjoint squares union to two rings" {
                let rings = expectOk (union noProjection [ squareA; farC ])
                Expect.equal rings.Length 2 "two components"
                let total = rings |> List.sumBy ringArea
                Expect.floatClose Accuracy.medium total 200.0 "areas add"
            }

            test "a union enclosing a gap is refused with the hole count" {
                match union noProjection [ uShape; bar ] with
                | Result.Error (ResultHasHoles n) -> Expect.equal n 1 "exactly the capped notch"
                | other -> failtestf "expected ResultHasHoles, got %A" other
            }

            test "untouched corners survive verbatim, invented vertices are re-projected" {
                let rings = expectOk (union (markZ 99.0) [ squareA; squareB ])
                match rings with
                | [ ring ] ->
                    // corner (0,0) of A is untouched by the union: bitwise survivor
                    Expect.isTrue
                        (ring |> Array.exists (fun v -> v = V3d(0.0, 0.0, 0.0)))
                        "corner (0,0,0) survives exactly"
                    // the two edge crossings (10,5) and (5,10) exist in no input: projected
                    let invented = ring |> Array.filter (fun v -> v.Z = 99.0)
                    Expect.equal invented.Length 2 "exactly the two edge crossings are invented"
                | _ -> failtestf "expected one ring, got %d" rings.Length
            }
        ]

        testList "regressions" [

            test "unionFail.pro3d: two Mars-scale terrain polygons union to one ring" {
                // the pair that produced the first broken union in the viewer (open result ring);
                // real Jezero coordinates, so this also exercises the chart at planetary
                // magnitudes with terrain-varying z. Rings stored closed, as drawn polygons are.
                let ringA =
                    [ V3d(693752.4845169528, 3140964.4859369597, 1081753.5209588252)
                      V3d(693826.0200948773, 3140951.3451355053, 1081749.271953337)
                      V3d(693817.5996431275, 3140969.4912605193, 1081705.7058096773)
                      V3d(693752.3277482664, 3140984.42677805,   1081682.0624189952)
                      V3d(693703.1901611687, 3140982.844498966,  1081725.2078957127)
                      V3d(693717.6165261068, 3140975.9650775343, 1081736.0441335244)
                      V3d(693752.4845169528, 3140964.4859369597, 1081753.5209588252) ]
                let ringB =
                    [ V3d(693836.6654154294, 3140940.082732067,  1081771.9274253566)
                      V3d(693804.8881684946, 3140963.156423218,  1081731.7674766772)
                      V3d(693804.145194177,  3140974.8859479995, 1081681.5826886257)
                      V3d(693873.9332417365, 3140967.6103407787, 1081678.7199219745)
                      V3d(693926.1712929023, 3140950.532698612,  1081691.1129532459)
                      V3d(693953.2448963856, 3140943.269000637,  1081694.45588316)
                      V3d(693940.9927090054, 3140937.732992658,  1081716.1248905866)
                      V3d(693900.5369081451, 3140939.222663753,  1081737.4637383546)
                      V3d(693853.2128994344, 3140941.9378085057, 1081757.507136002)
                      V3d(693833.117173066,  3140939.9575913376, 1081774.6055683242)
                      V3d(693836.6654154294, 3140940.082732067,  1081771.9274253566) ]
                // ring B self-intersects in projection (the hand-drawn spike folds over itself),
                // which EvenOdd resolves into the main contour plus a 0.7 m² sliver. The sliver
                // must be recognised as an artifact - smaller than any operand - and dropped,
                // not exploded into an absurd micro-annotation.
                let a, b = annoOf ringA, annoOf ringB
                let rings = expectOk (union noProjection [ a; b ])
                Expect.equal rings.Length 1 "one component; the self-intersection sliver is dropped"
                match rings with
                | [ ring ] ->
                    Expect.isTrue (ring.Length >= 10) "the outline keeps its shape"
                    // inclusion-exclusion, computed in the union's own chart (the ring is not
                    // planar in world space, so re-fitting it would measure a different chart)
                    let ringAnno = annoOf (List.ofArray ring)
                    let area =
                        match commonChart [ a; b ] with
                        | Some chart ->
                            match toRegion chart ringAnno with
                            | Some r -> RegionOps.area r
                            | None -> failtest "result ring did not project"
                        | None -> failtest "no common chart"
                    Expect.floatClose { absolute = 5.0; relative = 1e-3 } area 14583.0
                        "area is a + b - overlap (6122 + 9161 - 700)"
                    // every surviving vertex is one of the drawn points, bitwise
                    let inputs = List.append ringA ringB
                    let survivors =
                        ring |> Array.filter (fun v -> inputs |> List.exists (fun p -> p = v))
                    Expect.isTrue (survivors.Length >= 10) "most vertices survive verbatim"
                | _ -> failtest "unreachable"
            }
        ]

        testList "cut" [

            test "a straight stroke halves the square" {
                let stroke = [| V3d(-5.,5.,0.); V3d(15.,5.,0.) |]
                let rings = expectOk (cut noProjection squareA stroke)
                Expect.equal rings.Length 2 "two pieces"
                let total = rings |> List.sumBy ringArea
                Expect.floatClose Accuracy.medium total 100.0 "areas sum to the original"
            }

            test "a V-shaped stroke carves a wedge" {
                let stroke = [| V3d(2.,-5.,0.); V3d(5.,5.,0.); V3d(8.,-5.,0.) |]
                let rings = expectOk (cut noProjection squareA stroke)
                Expect.isTrue (rings.Length >= 2) "wedge and remainder"
                let total = rings |> List.sumBy ringArea
                Expect.floatClose Accuracy.medium total 100.0 "areas sum to the original"
            }

            test "a stroke that misses is StrokeDoesNotCut, not a silent no-op" {
                let stroke = [| V3d(-5.,50.,0.); V3d(15.,50.,0.) |]
                Expect.equal (cut noProjection squareA stroke) (Result.Error StrokeDoesNotCut) "missed"
            }

            test "an end inside the annotation refuses the cut" {
                let stroke = [| V3d(-5.,5.,0.); V3d(5.,5.,0.) |]
                Expect.equal (cut noProjection squareA stroke) (Result.Error StrokeDoesNotCut) "end inside"
            }

            test "cut vertices on the stroke are re-projected, corners survive" {
                let stroke = [| V3d(-5.,5.,0.); V3d(15.,5.,0.) |]
                let rings = expectOk (cut (markZ 42.0) squareA stroke)
                let all = rings |> List.collect Array.toList
                Expect.isTrue
                    (all |> List.exists (fun v -> v = V3d(0.0, 0.0, 0.0)))
                    "corner (0,0,0) survives exactly"
                Expect.isTrue
                    (all |> List.exists (fun v -> v.Z = 42.0))
                    "the stroke intersections were re-projected"
            }
        ]
    ]
