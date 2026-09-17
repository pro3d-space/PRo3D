/// Rung 1 of the map projection testing ladder (#772): the double-precision math the shaders
/// mirror and the rendered pixels are checked against. No GPU, no data.
module MapProjectionMathTests

open System
open Expecto

open Aardvark.Base

open PRo3D.MapProjection

let private deg (d : float) = d * Math.PI / 180.0

let private kinds =
    [ MapProjectionKind.Equirectangular; MapProjectionKind.PolarNorth; MapProjectionKind.PolarSouth ]

let tests () =
    testList "map projection math (#772)" [

        test "longitude, latitude and radius of the body axes" {
            let close (a : V3d) (b : V3d) msg = Expect.isLessThan (a - b).Length 1e-12 msg
            close (Projection.lonLatR (V3d(2.0, 0.0, 0.0))) (V3d(0.0, 0.0, 2.0)) "+X is lon 0 on the equator"
            close (Projection.lonLatR (V3d(0.0, 3.0, 0.0))) (V3d(Projection.halfPi, 0.0, 3.0)) "+Y is lon +90 (east positive)"
            close (Projection.lonLatR (V3d(0.0, 0.0, 5.0))) (V3d(0.0, Projection.halfPi, 5.0)) "+Z is the north pole"
            Expect.floatClose Accuracy.veryHigh (abs (Projection.lonLatR (V3d(-1.0, 0.0, 0.0))).X) Projection.pi "-X is lon 180"
            Expect.equal (Projection.lonLatR V3d.Zero) V3d.Zero "the body centre does not produce NaN"
        }

        test "forward and inverse round-trip on every map" {
            let rnd = RandomSystem(7)
            for kind in kinds do
                for _ in 1 .. 2000 do
                    let lon = rnd.UniformDouble() * Projection.twoPi - Projection.pi
                    let lat =
                        match kind with
                        | MapProjectionKind.Equirectangular -> (rnd.UniformDouble() - 0.5) * Projection.pi
                        // stay on the map's own side, away from the opposite pole
                        | _ -> Projection.polarSign kind * rnd.UniformDouble() * Projection.halfPi * 0.99
                    let back = Projection.inverse kind (Projection.forward kind lon lat)
                    Expect.isLessThan (abs (Projection.wrapPi (back.X - lon))) 1e-9 (sprintf "%A longitude" kind)
                    Expect.isLessThan (abs (back.Y - lat)) 1e-9 (sprintf "%A latitude" kind)
        }

        test "polar stereographic orientation (USGS convention)" {
            let close (a : V2d) (b : V2d) msg = Expect.isLessThan (a - b).Length 1e-12 msg
            let north = Projection.polarSign MapProjectionKind.PolarNorth
            let south = Projection.polarSign MapProjectionKind.PolarSouth
            close (Projection.polar north 0.0 Projection.halfPi) V2d.Zero "north pole in the centre"
            // on the unit sphere the equator sits at rho = 2 tan(45 deg) = 2
            close (Projection.polar north 0.0 0.0) (V2d(0.0, -2.0)) "north map: lon 0 points down"
            close (Projection.polar north (deg 90.0) 0.0) (V2d(2.0, 0.0)) "north map: east is to the right of lon 0"
            close (Projection.polar south 0.0 0.0) (V2d(0.0, 2.0)) "south map: lon 0 points up"
            close (Projection.polar south 0.0 -Projection.halfPi) V2d.Zero "south pole in the centre"
        }

        test "triangle classification: regular, seam, pole" {
            Expect.equal (Projection.classify (deg 10.0) (deg 20.0) (deg 15.0)) Projection.Regular "an ordinary triangle"
            Expect.equal (Projection.classify (deg 179.0) (deg -179.0) (deg 178.5)) Projection.Seam "across the 180 degree meridian"
            Expect.equal (Projection.classify (deg -179.0) (deg 179.0) (deg -178.5)) Projection.Seam "across it from the other side"
            Expect.equal (Projection.classify (deg -120.0) (deg 0.0) (deg 120.0)) Projection.Pole "longitudes winding once around"
            // a thin triangle close to the pole spans a wide longitude range without containing it
            Expect.equal (Projection.classify (deg 10.0) (deg 150.0) (deg 80.0)) Projection.Regular "wide but not around the pole"
        }

        test "unwrapped seam triangles are contiguous" {
            let xs, winding = Projection.unwrap (deg 179.0) (deg -179.0) (deg 178.5)
            Expect.floatClose Accuracy.veryHigh winding 0.0 "no winding"
            Expect.isLessThan (xs.MaxElement - xs.MinElement) (deg 3.0) "a 2 degree triangle stays 2 degrees wide"
            Expect.isGreaterThan xs.MaxElement Projection.pi "it overhangs +180, so the shader adds the shifted copy"
        }

        test "the view fits the extent and inverts through pixelToMap" {
            for kind in kinds do
                for viewport in [ V2i(1024, 512); V2i(800, 600); V2i(300, 900) ] do
                    let vp = Projection.viewProj kind Projection.defaultMaxColatitude V2d.Zero 1.0 viewport
                    let e = Projection.extent kind Projection.defaultMaxColatitude
                    let corner = vp.Forward.TransformPos(V3d(e.Max, 0.0))
                    Expect.isLessThanOrEqual (max (abs corner.X) (abs corner.Y)) (1.0 + 1e-9) (sprintf "%A %A: extent inside NDC" kind viewport)
                    Expect.floatClose Accuracy.high (max (abs corner.X) (abs corner.Y)) 1.0 (sprintf "%A %A: and touching it" kind viewport)
                    // a map-space unit is as many pixels horizontally as vertically
                    let px = float viewport.X * 0.5 * vp.Forward.M00
                    let py = float viewport.Y * 0.5 * vp.Forward.M11
                    Expect.floatClose Accuracy.high px py (sprintf "%A %A: aspect preserved" kind viewport)

            let viewport = V2i(1000, 500)
            let center = V2d(0.4, -0.2)
            let vp = Projection.viewProj MapProjectionKind.Equirectangular Projection.defaultMaxColatitude center 3.0 viewport
            let atCenter = Projection.pixelToMap vp viewport (V2d(500.0, 250.0))
            Expect.isLessThan (atCenter - center).Length 1e-9 "the viewport centre shows the map centre"
            let up = Projection.pixelToMap vp viewport (V2d(500.0, 0.0))
            Expect.isGreaterThan up.Y center.Y "pixel row 0 is the top: higher latitude"
        }

        test "equirectangular extent is 2:1, polar extent reaches the cutoff" {
            let e = Projection.extent MapProjectionKind.Equirectangular Projection.defaultMaxColatitude
            Expect.floatClose Accuracy.veryHigh (e.Size.X / e.Size.Y) 2.0 "360 by 180 degrees"
            let p = Projection.extent MapProjectionKind.PolarNorth Projection.defaultMaxColatitude
            Expect.floatClose Accuracy.veryHigh p.Max.X 2.0 "equator at rho 2"
        }

        test "graticule line counts" {
            Expect.equal (MapSg.graticuleLines MapProjectionKind.Equirectangular Projection.halfPi).Length (25 + 13) "25 meridians, 13 parallels"
            // 6 rings of 180 segments (15..90 degrees colatitude) and 24 meridians
            Expect.equal (MapSg.graticuleLines MapProjectionKind.PolarNorth Projection.halfPi).Length (6 * 180 + 24) "polar rings and spokes"
        }
    ]
