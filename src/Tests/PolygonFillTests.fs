module PolygonFillTests

open System
open Expecto
open Aardvark.Base
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation

// ---------------------------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------------------------

let private eps = PolygonFill.DefaultEpsilon

/// Summed area of a triangle-list mesh.
let private meshArea (positions : V3d[]) =
    let mutable acc = 0.0
    for i in 0 .. 3 .. positions.Length - 3 do
        let a = positions.[i + 1] - positions.[i]
        let b = positions.[i + 2] - positions.[i]
        acc <- acc + 0.5 * Vec.Length(Vec.cross a b)
    acc

let private triangleCount (positions : V3d[]) = positions.Length / 3

/// XY plane, so a ring built from V3d(x, y, 0) lies exactly in it.
let private xyChart = SurfaceChart.ofPlane (Plane3d(V3d.OOI, V3d.Zero))

let private square = [| V3d(0,0,0); V3d(10,0,0); V3d(10,10,0); V3d(0,10,0) |]

/// Concave L, area 3 * 1 + 1 * 2 = 5... laid out explicitly:
///   (0,0) (3,0) (3,1) (1,1) (1,3) (0,3)  ->  3x1 base plus 1x2 upright = 5
let private concaveL =
    [| V3d(0,0,0); V3d(3,0,0); V3d(3,1,0); V3d(1,1,0); V3d(1,3,0); V3d(0,3,0) |]

let private ellipseRing (a : float) (b : float) (n : int) =
    // samples+1 points, closing point repeated - exactly what computeEllipsePoints emits
    Array.init (n + 1) (fun i ->
        let t = Constant.PiTimesTwo * float i / float n
        V3d(a * cos t, b * sin t, 0.0))

let private isCloseTo (tolerance : float) (a : V3d) (b : V3d) = Vec.Distance(a, b) <= tolerance

// ---------------------------------------------------------------------------------------------

let tests () =
    Aardvark.Init()

    testList "PolygonFill" [

        // -----------------------------------------------------------------------------------
        // normalize: the duplicate machinery. Stored points carry duplicates that fixing the
        // producers cannot retroactively remove, so this runs on every fill.
        // -----------------------------------------------------------------------------------

        testList "normalize" [

            test "open ring passes through unchanged" {
                let r = PolygonFill.normalize eps square
                Expect.equal r square "an already-clean ring must not be altered"
            }

            test "closing point exactly equal to the first is dropped" {
                let closed = Array.append square [| square.[0] |]
                let r = PolygonFill.normalize eps closed
                Expect.equal r square "the repeated closing point must go"
            }

            test "closing point within epsilon is dropped" {
                let closed = Array.append square [| square.[0] + V3d(eps * 0.1, 0.0, 0.0) |]
                let r = PolygonFill.normalize eps closed
                Expect.equal r.Length 4 "a near-coincident closing point must go"
            }

            test "closing point just outside epsilon is kept" {
                let closed = Array.append square [| square.[0] + V3d(eps * 100.0, 0.0, 0.0) |]
                let r = PolygonFill.normalize eps closed
                Expect.equal r.Length 5 "a genuinely distinct point must survive"
            }

            test "getPolylinePoints doubling is collapsed" {
                // segment i's endPoint is segment i+1's startPoint
                let a, b, c = V3d(0,0,0), V3d(1,0,0), V3d(1,1,0)
                let doubled = [| a; b; b; c; c; a |]
                let r = PolygonFill.normalize eps doubled
                Expect.equal r [| a; b; c |] "doubled interior corners and the closing point must go"
            }

            test "getPolylinePoints tripling is collapsed" {
                // ... and the last interior sample can land on the segment end as well
                let a, b, c = V3d(0,0,0), V3d(1,0,0), V3d(1,1,0)
                let tripled = [| a; b; b; b; c; c; c; a |]
                let r = PolygonFill.normalize eps tripled
                Expect.equal r [| a; b; c |] "tripled vertices must collapse to one"
            }

            test "all-identical points collapse to one" {
                let p = V3d(5,5,5)
                let r = PolygonFill.normalize eps [| p; p; p; p |]
                Expect.equal r [| p |] "nothing but duplicates leaves a single point"
            }

            test "two distinct points padded with duplicates stay two" {
                let a, b = V3d(0,0,0), V3d(1,0,0)
                let r = PolygonFill.normalize eps [| a; a; b; b; b |]
                Expect.equal r [| a; b |] "padding must not be mistaken for a third vertex"
            }

            test "a non-adjacent revisit is not collapsed" {
                // a legitimate figure-eight-ish ring: the repeat is not adjacent, so it stays
                let a, b, c = V3d(0,0,0), V3d(1,0,0), V3d(1,1,0)
                let revisit = [| a; b; a; c |]
                let r = PolygonFill.normalize eps revisit
                Expect.equal r revisit "only *consecutive* duplicates may be removed"
            }

            test "is idempotent" {
                let once = PolygonFill.normalize eps (Array.append square [| square.[0]; square.[0] |])
                let twice = PolygonFill.normalize eps once
                Expect.equal twice once "normalize must be a fixpoint after one application"
            }

            test "empty input is handled" {
                Expect.equal (PolygonFill.normalize eps [||]) [||] "empty in, empty out"
            }
        ]

        // -----------------------------------------------------------------------------------
        // charts
        // -----------------------------------------------------------------------------------

        testList "SurfaceChart" [

            test "ofPlane round-trips points lying in the plane" {
                let plane = Plane3d(V3d(1.0, 1.0, 1.0).Normalized, V3d(3.0, 0.0, 0.0))
                let chart = SurfaceChart.ofPlane plane
                let p = plane.GetPlaneToWorld().TransformPos(V3d(2.0, -5.0, 0.0))

                let rt = chart.toChart p |> Option.bind chart.toWorld
                Expect.isSome rt "the plane chart is total"
                Expect.isTrue (isCloseTo 1e-9 rt.Value p) "a point in the plane must survive the round trip"
            }

            test "ofPlane projects off-plane points onto the plane" {
                let plane = Plane3d(V3d.OOI, V3d.Zero)
                let chart = SurfaceChart.ofPlane plane
                let lifted = chart.toChart (V3d(1.0, 2.0, 37.0)) |> Option.bind chart.toWorld

                Expect.isSome lifted "the plane chart is total"
                Expect.floatClose Accuracy.high (plane.Height lifted.Value) 0.0
                    "the lifted point must land on the plane, losing its height"
            }

            test "ofUpVector round-trips" {
                let origin = V3d(100.0, 200.0, 300.0)
                let chart = SurfaceChart.ofUpVector (V3d(0.0, 0.0, 1.0)) origin
                let p = origin + V3d(4.0, -7.0, 0.0)

                let rt = chart.toChart p |> Option.bind chart.toWorld
                Expect.isSome rt "the up-vector chart is total"
                Expect.isTrue (isCloseTo 1e-9 rt.Value p) "an in-plane point must survive the round trip"
            }

            test "tryOfPlane rejects a NaN plane" {
                // DipAndStrikeResults initialises to NaN and that sentinel round-trips through
                // the project format, so stored planes really can look like this
                let nanPlane = Plane3d(V3d.NaN, Double.NaN)
                Expect.isNone (SurfaceChart.tryOfPlane nanPlane) "a NaN plane cannot define a chart"
            }

            test "tryOfPlane rejects a zero normal" {
                Expect.isNone (SurfaceChart.tryOfPlane (Plane3d(V3d.Zero, 0.0)))
                    "a degenerate normal cannot define a chart"
            }

            test "tryOfPlane accepts a good plane" {
                Expect.isSome (SurfaceChart.tryOfPlane (Plane3d(V3d.OOI, V3d.Zero)))
                    "a well-formed plane must be accepted"
            }
        ]

        // -----------------------------------------------------------------------------------
        // fill
        // -----------------------------------------------------------------------------------

        testList "tryComputeFill" [

            test "convex square has the right area" {
                let fill = PolygonFill.tryComputeFill xyChart square
                Expect.isSome fill "a square must fill"
                Expect.floatClose Accuracy.medium (meshArea fill.Value.positions) 100.0
                    "summed triangle area must equal the square's"
            }

            test "concave L has the right area" {
                let fill = PolygonFill.tryComputeFill xyChart concaveL
                Expect.isSome fill "a concave polygon must fill"
                Expect.floatClose Accuracy.medium (meshArea fill.Value.positions) 5.0
                    "ears must not be cut across the concavity"
            }

            test "concave L agrees with calculatePolygonArea" {
                // ties the rendered fill to the number shown in the properties panel
                let fill = PolygonFill.tryComputeFill xyChart concaveL
                let reported = Calculations.calculatePolygonArea (IndexList.ofArray concaveL)
                Expect.isSome fill "a concave polygon must fill"
                Expect.floatClose Accuracy.medium (meshArea fill.Value.positions) reported
                    "fill area and reported area must not disagree"
            }

            test "ellipse ring approximates pi*a*b" {
                let a, b = 7.0, 3.0
                let fill = PolygonFill.tryComputeFill xyChart (ellipseRing a b 200)
                Expect.isSome fill "an ellipse ring must fill"

                let expected = Constant.Pi * a * b
                let actual = meshArea fill.Value.positions
                Expect.isLessThan (abs (actual - expected) / expected) 0.005
                    "a 200-sample inscribed ring must be within 0.5% of the true area"
            }

            test "triangle count is vertices - 2 for a simple ring" {
                let fill = PolygonFill.tryComputeFill xyChart concaveL
                Expect.isSome fill "a concave polygon must fill"
                Expect.equal (triangleCount fill.Value.positions) (concaveL.Length - 2)
                    "a simple polygon triangulates into n-2 triangles"
            }

            test "output vertices are the input points, not their flattened images" {
                // the whole point of carrying world positions as a tessellator attribute: the
                // fill rim must coincide with the outline the user drew
                let tilted =
                    [| V3d(0,0,0); V3d(10,0,3); V3d(10,10,-2); V3d(0,10,5) |]
                let chart = SurfaceChart.ofPlane (Plane3d(V3d.OOI, V3d.Zero))
                let fill = PolygonFill.tryComputeFill chart tilted

                Expect.isSome fill "a non-planar ring must still fill"
                for p in fill.Value.positions do
                    Expect.isTrue (tilted |> Array.exists (isCloseTo 1e-9 p))
                        (sprintf "%A is not one of the input points - it was re-flattened" p)
            }

            test "every vertex lies on the chart plane for planar input" {
                let plane = Plane3d(V3d.OOI, V3d.Zero)
                let fill = PolygonFill.tryComputeFill (SurfaceChart.ofPlane plane) square
                Expect.isSome fill "a square must fill"
                for p in fill.Value.positions do
                    Expect.floatClose Accuracy.high (plane.Height p) 0.0 "planar input stays planar"
            }

            test "winding does not matter" {
                let cw = PolygonFill.tryComputeFill xyChart square
                let ccw = PolygonFill.tryComputeFill xyChart (Array.rev square)
                Expect.isSome cw "clockwise must fill"
                Expect.isSome ccw "counter-clockwise must fill"
                Expect.floatClose Accuracy.medium
                    (meshArea cw.Value.positions) (meshArea ccw.Value.positions)
                    "the tessellator normalises orientation"
            }

            test "collinear points do not fill" {
                let collinear = [| V3d(0,0,0); V3d(1,0,0); V3d(2,0,0); V3d(3,0,0) |]
                Expect.isNone (PolygonFill.tryComputeFill xyChart collinear)
                    "a degenerate ring has no interior"
            }

            test "fewer than three distinct points do not fill" {
                let a, b = V3d(0,0,0), V3d(1,0,0)
                Expect.isNone (PolygonFill.tryComputeFill xyChart [| a; a; b; b |])
                    "duplicates must not be counted towards the three-vertex minimum"
                Expect.isNone (PolygonFill.tryComputeFill xyChart [| a |]) "a single point has no interior"
                Expect.isNone (PolygonFill.tryComputeFill xyChart [||]) "an empty ring has no interior"
            }

            test "self-intersecting bowtie resolves rather than failing" {
                // pins actual behaviour: libtess applies the winding rule instead of throwing.
                // An earlier draft of the plan wrongly expected this case to be rejected.
                let bowtie = [| V3d(0,0,0); V3d(2,2,0); V3d(2,0,0); V3d(0,2,0) |]
                let fill = PolygonFill.tryComputeFill xyChart bowtie

                Expect.isSome fill "a bowtie must resolve, not crash"
                Expect.isGreaterThan (meshArea fill.Value.positions) 0.0
                    "the winding-rule resolution has positive area"
            }

            test "a chart that does not cover the ring yields no fill" {
                let nowhere =
                    { name = "nowhere"; toChart = (fun _ -> None); toWorld = (fun _ -> None) }
                Expect.isNone (PolygonFill.tryComputeFill nowhere square)
                    "an uncovered ring must degrade to no fill, not to garbage"
            }
        ]
    ]
