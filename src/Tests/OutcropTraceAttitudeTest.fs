/// The parts of the outcrop-trace mean attitude that can be silently wrong.
///
/// The first four cases are the comparison table in plans/outcropTraces.md turned into
/// assertions, so the choice of averaging method is pinned by tests rather than by a
/// paragraph. Their synthetic orientations come from
/// tools/analysis/compare_attitude_averaging.py, which is checked in so the script and the
/// tests cannot drift apart. No GL context and no test data are needed.
module OutcropTraceAttitudeTest

open System

open Aardvark.Base
open Aardvark.Rendering

open Expecto

open PRo3D.Base.Annotation
open PRo3D.Core

/// ENU frame used by the fixtures: east = X, north = Y, up = Z, azimuth clockwise from north.
let private up = V3d.OOI
let private north = V3d.OIO

/// Pole (unit normal) of a plane with the given dip angle and dip direction, in degrees.
/// The pole is tilted `dip` from vertical, trending `azimuth + 180`.
let private pole (dipDeg : float) (azDeg : float) =
    let d = dipDeg.RadiansFromDegrees()
    let a = azDeg.RadiansFromDegrees()
    V3d(-(sin d) * (sin a), -(sin d) * (cos a), cos d)

let private contribution (dipDeg : float) (azDeg : float) (center : V3d) =
    { OutcropTrace.plane = Plane3d(pole dipDeg azDeg, center)
      OutcropTrace.center = center }

/// Same bed, but with the fitted normal flipped - which is what `DipAndStrikeResults.plane`
/// can legitimately contain, since the regression's sign is never corrected on the way in.
let private flipped (c : OutcropTrace.Contribution) =
    { c with plane = Plane3d(-c.plane.Normal, c.center) }

let private attitudeOf (cs : array<OutcropTrace.Contribution>) =
    match OutcropTrace.meanAttitude cs with
    | Some a -> a
    | None -> failtest "meanAttitude returned None for a non-empty selection"

let private dipOf (a : MeanAttitude) = OutcropTrace.dipAndDipDirection up north a |> fst

let tests () =
    testList "outcrop trace mean attitude" [

        // --- the method-choice cases -------------------------------------------------

        test "shallow beds dipping in opposite directions average to horizontal" {
            // 5/000 and 5/180. Averaging dip azimuth circularly and dip angle
            // arithmetically - the rose diagram's arithmetic - returns a 5 deg plane here,
            // with an azimuth resultant of exactly zero, so it would refuse to draw an
            // answer that is perfectly well defined.
            let a = attitudeOf [| contribution 5.0 0.0 V3d.Zero; contribution 5.0 180.0 (V3d(10.0, 0.0, 0.0)) |]
            Expect.floatClose { absolute = 1e-6; relative = 0.0 } (dipOf a) 0.0 "the mean attitude should be horizontal"
            Expect.isGreaterThan a.s.X 0.99 "S1 should show a strong cluster"
            Expect.equal a.shape Cluster "two consistent beds are a cluster"
        }

        test "a near-vertical bed does not cancel itself out" {
            // Eight measurements straddling vertical: all poles are correctly up-oriented
            // and still point in opposing *horizontal* directions, because half the beds
            // lean a fraction one way and half the other. Summing unit normals (the Fisher
            // mean) collapses here and reports 0.11 deg with R = 0.009; the orientation
            // tensor is sign-invariant and gets it right.
            let cs =
                [| 89.5, 90.0; 89.7, 270.0; 89.9, 90.0; 88.9, 270.0
                   89.2, 90.0; 89.6, 270.0; 89.8, 90.0; 89.4, 270.0 |]
                |> Array.mapi (fun i (d, az) -> contribution d az (V3d(float i, 0.0, 0.0)))
            let a = attitudeOf cs
            Expect.isGreaterThan (dipOf a) 89.0 "the mean attitude should stay near vertical"
            Expect.isGreaterThan a.s.X 0.99 "S1 should show a strong cluster"
            Expect.equal a.shape Cluster "eight measurements of one bed are a cluster"
        }

        test "a flipped fitted normal does not change the answer" {
            // n n^T = (-n)(-n)^T, so the stored sign of the regression plane is irrelevant.
            let cs = [| contribution 30.0 120.0 V3d.Zero
                        contribution 32.0 124.0 (V3d(5.0, 0.0, 0.0)) |]
            let a = attitudeOf cs
            let b = attitudeOf [| cs.[0]; flipped cs.[1] |]
            Expect.floatClose Accuracy.high (dipOf b) (dipOf a) "flipping a normal must not move the mean dip"
            Expect.floatClose Accuracy.high b.s.X a.s.X "flipping a normal must not move S1"
        }

        test "a fold is rejected as a girdle rather than averaged" {
            // Two limbs at 40/090 and 40/270. The mean unit normal returns a horizontal
            // plane - perpendicular to both limbs - with R = 0.77, comfortably above any
            // scalar confidence threshold. Only the eigenvalue spectrum catches this.
            let cs =
                [| 40.0, 90.0; 41.0, 88.0; 39.0, 92.0; 40.0, 270.0; 41.0, 272.0; 39.0, 268.0 |]
                |> Array.mapi (fun i (d, az) -> contribution d az (V3d(float i, 0.0, 0.0)))
            let a = attitudeOf cs
            Expect.isGreaterThan (a.s.Y / a.s.X) OutcropTrace.maxGirdleRatio "S2/S1 should exceed the girdle threshold"
            match a.shape with
            | Girdle axis ->
                let _, plunge = OutcropTrace.trendAndPlunge up north axis
                Expect.floatClose { absolute = 1.0; relative = 0.0 } plunge 0.0 "the fold axis should be horizontal"
            | other -> failtestf "expected a girdle, got %A" other
        }

        test "a tight cluster is accepted and the guards do not fire" {
            let cs =
                [| 30.0, 120.0; 32.0, 124.0; 29.0, 117.0; 31.0, 122.0; 30.0, 119.0 |]
                |> Array.mapi (fun i (d, az) -> contribution d az (V3d(float i, 0.0, 0.0)))
            let a = attitudeOf cs
            Expect.equal a.shape Cluster "ordinary bedding data must be usable"
            Expect.floatClose { absolute = 0.2; relative = 0.0 } (dipOf a) 30.38 "mean dip should match the reference value"
            Expect.isGreaterThan a.s.X 0.999 "S1 should be near 1 for a tight cluster"
        }

        test "scattered poles have no dominant attitude" {
            let cs =
                [| 10.0, 0.0; 70.0, 90.0; 45.0, 200.0; 80.0, 300.0; 20.0, 140.0; 60.0, 20.0 |]
                |> Array.mapi (fun i (d, az) -> contribution d az (V3d(float i, 0.0, 0.0)))
            let a = attitudeOf cs
            Expect.equal a.shape NoDominantAttitude "an unstructured selection must be refused"
        }

        // --- degenerate and bookkeeping cases ----------------------------------------

        test "the mean of one annotation is that annotation" {
            let c = contribution 34.0 118.0 (V3d(100.0, 200.0, 300.0))
            let a = attitudeOf [| c |]
            Expect.floatClose Accuracy.high a.s.X 1.0 "S1 must be 1 for a single measurement"
            Expect.floatClose Accuracy.high a.spread 0.0 "a single measurement has no spread"
            Expect.equal a.anchor c.center "the anchor is the annotation's own centre"
            Expect.floatClose { absolute = 1e-6; relative = 0.0 } (dipOf a) 34.0 "the dip is the annotation's own dip"
        }

        test "an empty selection yields no attitude" {
            Expect.isNone (OutcropTrace.meanAttitude [||]) "nothing to combine"
        }

        test "spread is the furthest contributing centre from the anchor" {
            let cs = [| contribution 30.0 120.0 (V3d(-10.0, 0.0, 0.0))
                        contribution 30.0 120.0 (V3d( 10.0, 0.0, 0.0))
                        contribution 30.0 120.0 (V3d(  0.0, 0.0, 0.0)) |]
            let a = attitudeOf cs
            Expect.floatClose Accuracy.high a.spread 10.0 "spread should be the half-extent here"
        }

        test "a fitted projection radius floors on one annotation and follows the spread otherwise" {
            let a = attitudeOf [| contribution 30.0 120.0 V3d.Zero |]
            Expect.floatClose Accuracy.high (OutcropTrace.fitProjectionRadius a) 37.5
                "a single annotation has no spread, so the minimum sizes it"
            let b = attitudeOf [| contribution 30.0 120.0 (V3d(-100.0, 0.0, 0.0))
                                  contribution 30.0 120.0 (V3d( 100.0, 0.0, 0.0)) |]
            Expect.floatClose Accuracy.high (OutcropTrace.fitProjectionRadius b) 150.0
                "otherwise the selection's own footprint plus headroom"
        }

        // --- the precision case -------------------------------------------------------

        test "the view-space plane is exact at planetary scale" {
            // The bug this catches is silent: a world-space transform, or dropping to
            // float32 too early, moves the trace by a fraction of a metre rather than
            // breaking anything visibly. Mars-radius coordinates, camera on the surface.
            let radius = 3396200.0
            let center = V3d(radius, 0.0, 0.0)
            let a = attitudeOf [| contribution 30.0 120.0 center |]

            let eye = center + V3d(50.0, 20.0, 10.0)
            let view = CameraView.lookAt eye center V3d.OOI |> CameraView.viewTrafo

            let plane, extent = OutcropTrace.viewSpaceAttitude view 100.0 a
            let signedDistance (p : V3d) =
                let pv = V3f (view.Forward.TransformPos p)
                Vec.dot (V3f(plane.X, plane.Y, plane.Z)) pv - plane.W

            // a point on the plane, 30 m away from the anchor along the plane
            let inPlane =
                let n = a.plane.Normal
                let t = (if abs n.Z < 0.9 then V3d.OOI else V3d.IOO)
                center + (Vec.cross n t |> Vec.normalize) * 30.0
            Expect.floatClose { absolute = 1e-2; relative = 0.0 } (float (signedDistance inPlane)) 0.0
                "a point on the plane must read zero distance"

            let oneMetreOff = center + a.plane.Normal * 1.0
            Expect.floatClose { absolute = 1e-2; relative = 0.0 } (float (signedDistance oneMetreOff)) 1.0
                "a point 1 m off the plane must read 1 m"

            Expect.floatClose { absolute = 1e-3; relative = 0.0 } (float extent.W) 100.0
                "the projection radius travels in the extent's w"
        }

        // --- the selection gate --------------------------------------------------------

        test "includes gates on annotation type and rejects degenerate planes" {
            let good = Plane3d(pole 30.0 120.0, V3d.Zero)
            Expect.isTrue  (OutcropTrace.includes false true Geometry.DnS good) "DnS with the DnS toggle on"
            Expect.isFalse (OutcropTrace.includes false false Geometry.DnS good) "DnS with the DnS toggle off"
            Expect.isTrue  (OutcropTrace.includes true true Geometry.Polyline good) "polyline with the polyline toggle on"
            Expect.isFalse (OutcropTrace.includes false true Geometry.Polyline good) "polyline with the polyline toggle off"
            Expect.isFalse (OutcropTrace.includes true true Geometry.Point good) "a point has no plane"
            Expect.isFalse (OutcropTrace.includes false true Geometry.DnS (Plane3d(V3d.NaN, V3d.Zero))) "NaN normal"
            Expect.isFalse (OutcropTrace.includes false true Geometry.DnS (Plane3d(V3d.Zero, V3d.Zero))) "zero normal"
        }
    ]
