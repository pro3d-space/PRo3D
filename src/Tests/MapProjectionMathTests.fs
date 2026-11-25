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

        test "zooming keeps the map point under the pointer, whatever the panel size" {
            // regression (#772 review): the viewport used to come only from drags, so a wheel
            // step in a panel that was never dragged, or resized since, zoomed about the wrong point
            for kind in kinds do
                let size = V2d(1000.0, 400.0)
                let pointer = V2d(900.0, 120.0)
                let stale = { MapProjectionApp.initial with kind = kind; viewport = V2i(1024, 512); center = V2d(0.2, 0.1); zoom = 2.0 }
                let underPointer (m : MapProjectionModel) =
                    Projection.pixelToMap (Projection.viewProj m.kind Projection.defaultMaxColatitude m.center m.zoom (V2i(int size.X, int size.Y))) (V2i(int size.X, int size.Y)) pointer
                let before = underPointer stale
                let zoomed = MapProjectionApp.update stale (Zoom(1.0, pointer, size))
                Expect.isGreaterThan zoomed.zoom stale.zoom (sprintf "%A: zoomed in" kind)
                Expect.isLessThan (underPointer zoomed - before).Length 1e-9 (sprintf "%A: the point under the pointer stays" kind)
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

        test "a Jezero-sized footprint is sub-pixel on a whole-Mars map, and Zoom to data finds it" {
            // the Jezero_05_04 root patch: 2.7 km across at 18.5 N, 77.4 E on Mars (r = 3393.7 km).
            // This is why the map looked empty for a planet (#772): nothing about it is broken,
            // the data is simply far below one pixel until something zooms there.
            let r = 3393700.0
            let lon, lat = 77.4 * Constant.RadiansPerDegree, 18.51 * Constant.RadiansPerDegree
            let centre = V3d(r * cos lat * cos lon, r * cos lat * sin lon, r * sin lat)
            let east = V3d(-sin lon, cos lon, 0.0)
            let north = V3d(-sin lat * cos lon, -sin lat * sin lon, cos lat)
            let half = 1350.0
            let corners =
                [ for sx in [ -1.0; 1.0 ] do
                    for sy in [ -1.0; 1.0 ] do
                        yield centre + east * (sx * half) + north * (sy * half) ]
            let kind = MapProjectionKind.Equirectangular
            let viewport = V2i(1024, 512)
            match Projection.mapBoxOf kind corners with
            | None -> failwith "the footprint has no extent"
            | Some box ->
                let ll = Projection.inverse kind box.Center
                Expect.floatClose Accuracy.low (ll.X * Constant.DegreesPerRadian) 77.4 "longitude of the footprint"
                Expect.floatClose Accuracy.low (ll.Y * Constant.DegreesPerRadian) 18.51 "latitude of the footprint"
                let pixels = box.Size.X / Projection.unitsPerPixel kind Projection.defaultMaxColatitude 1.0 viewport
                Expect.isLessThan pixels 0.5 "at zoom 1 the whole OPC is well below a pixel"
                // Zoom to data puts it on screen at a usable size
                let fitted = MapProjectionApp.update { MapProjectionApp.initial with kind = kind; viewport = viewport } (FitTo box)
                Expect.isLessThan (fitted.center - box.Center).Length 1e-9 "centred on the data"
                let fittedPixels = box.Size.X / Projection.unitsPerPixel kind Projection.defaultMaxColatitude fitted.zoom viewport
                Expect.isGreaterThan fittedPixels (0.5 * float viewport.Y) "the data fills much of the panel"
                Expect.isLessThan fittedPixels (float viewport.X) "and still fits into it"
        }

        test "a footprint on the date line stays one small box" {
            // unwrapping matters: two corners at +179 and -179 degrees are 2 degrees apart,
            // not 358, or Zoom to data would zoom out to the whole map instead of to the data
            let r = 3393700.0
            let atLonLat (lonDeg : float) (latDeg : float) =
                let lon, lat = lonDeg * Constant.RadiansPerDegree, latDeg * Constant.RadiansPerDegree
                V3d(r * cos lat * cos lon, r * cos lat * sin lon, r * sin lat)
            match Projection.mapBoxOf MapProjectionKind.Equirectangular [ atLonLat 179.0 10.0; atLonLat -179.0 11.0 ] with
            | None -> failwith "no extent"
            | Some box ->
                Expect.floatClose Accuracy.medium (box.Size.X * Constant.DegreesPerRadian) 2.0 "2 degrees wide, not 358"
                Expect.floatClose Accuracy.medium (box.Size.Y * Constant.DegreesPerRadian) 1.0 "1 degree tall"
        }

        test "fitting a single point goes to the deepest zoom, and zoom stays within its range" {
            for kind in kinds do
                let viewport = V2i(800, 600)
                let point = Projection.forward kind 0.3 0.2
                let fitted = MapProjectionApp.update { MapProjectionApp.initial with kind = kind; viewport = viewport } (FitTo (Box2d(point, point)))
                Expect.equal fitted.zoom MapProjectionApp.maxZoom (sprintf "%A: a point has no extent to fit" kind)
                // the whole map fits at zoom 1 and never zooms out further
                let whole = Projection.extent kind Projection.defaultMaxColatitude
                let out = MapProjectionApp.update { MapProjectionApp.initial with kind = kind; viewport = viewport } (FitTo whole)
                Expect.equal out.zoom 1.0 (sprintf "%A: the whole map is zoom 1" kind)
        }

        test "a footprint is grown to be findable, and dropped once the data speaks for itself" {
            // the rule that makes a planet usable (#772): a Jezero-sized speck gets a box of
            // MapSg.footprintMinPixels, while a small body, whose surfaces are the whole map,
            // gets none - there the rectangle would only be clutter
            let kind = MapProjectionKind.Equirectangular
            let viewport = V2i(1024, 512)
            let u = Projection.unitsPerPixel kind Projection.defaultMaxColatitude 1.0 viewport
            let speck =
                let c = Projection.forward kind 1.35 0.32
                Box2d(c - V2d(0.0004, 0.0004), c + V2d(0.0004, 0.0004))   // about 0.05 degrees
            match MapSg.footprintBox u speck with
            | None -> failwith "a sub-pixel footprint has to be drawn"
            | Some grown ->
                Expect.floatClose Accuracy.medium (grown.Size.X / u) MapSg.footprintMinPixels "grown to the minimum size"
                Expect.isLessThan (grown.Center - speck.Center).Length 1e-9 "grown around the data, not moved"
            let wholeBody = Projection.extent kind Projection.defaultMaxColatitude
            Expect.isNone (MapSg.footprintBox u wholeBody) "data filling the map needs no rectangle"
            // zooming in on the speck eventually makes its own outline big enough, and the box goes
            let deep = Projection.unitsPerPixel kind Projection.defaultMaxColatitude 4096.0 viewport
            Expect.isNone (MapSg.footprintBox deep speck) "zoomed in, the data speaks for itself"
        }

        test "the camera marker keeps its size in pixels while zooming" {
            // it is drawn in map space, so its map-space size has to shrink with the zoom
            let kind = MapProjectionKind.Equirectangular
            let viewport = V2i(1024, 512)
            let u1 = Projection.unitsPerPixel kind Projection.defaultMaxColatitude 1.0 viewport
            let u8 = Projection.unitsPerPixel kind Projection.defaultMaxColatitude 8.0 viewport
            Expect.floatClose Accuracy.high (u1 / u8) 8.0 "eight times the zoom, an eighth of the map units per pixel"
            Expect.floatClose Accuracy.high (u1 * float viewport.X) (Projection.twoPi) "at zoom 1 the panel width is the whole 360 degrees"
        }

        test "follow cursor centres the map on the 3D hit point" {
            // #772: on a planet the data is a speck, so the map follows the 3D view's preview
            // cursor. The preview pick only runs while picking, so with nothing under the cursor
            // the map holds what it had rather than jumping home.
            let r = 3393700.0
            let atLonLat (lonDeg : float) (latDeg : float) =
                let lon, lat = lonDeg * Constant.RadiansPerDegree, latDeg * Constant.RadiansPerDegree
                V3d(r * cos lat * cos lon, r * cos lat * sin lon, r * sin lat)
            let held = V2d(0.3, -0.2)
            for kind in kinds do
                let hit = atLonLat 77.4 18.5
                let followed = MapProjectionApp.effectiveCentre kind true (Some hit) held
                // the centre is kept on the map: on the south polar map a northern point lies
                // beyond the cutoff, and the centre is pinned to the edge rather than flying off
                let e = Projection.extent kind Projection.defaultMaxColatitude
                let projected = Projection.forward kind (77.4 * Constant.RadiansPerDegree) (18.5 * Constant.RadiansPerDegree)
                let expected = V2d(clamp e.Min.X e.Max.X projected.X, clamp e.Min.Y e.Max.Y projected.Y)
                Expect.isLessThan (followed - expected).Length 1e-9 (sprintf "%A: centred on the hit point" kind)
                Expect.equal (MapProjectionApp.effectiveCentre kind true None held) held
                    (sprintf "%A: nothing picked, the centre stands" kind)
                Expect.equal (MapProjectionApp.effectiveCentre kind false (Some hit) held) held
                    (sprintf "%A: follow off, the model centre wins" kind)
                // the body centre has no longitude or latitude and must not move the map
                Expect.equal (MapProjectionApp.effectiveCentre kind true (Some V3d.Zero) held) held
                    (sprintf "%A: a degenerate hit is ignored" kind)
        }

        test "taking the map over from following keeps what it shows" {
            // switching the toggle off, or grabbing the map, adopts the followed centre: the map
            // must not jump back to the centre the model held while following
            let followed = V2d(1.1, 0.4)
            let m = { MapProjectionApp.initial with follow = true; center = V2d.Zero }
            let stopped = MapProjectionApp.update m (SetFollow(false, followed))
            Expect.isFalse stopped.follow "follow is off"
            Expect.isLessThan (stopped.center - followed).Length 1e-9 "the followed centre is kept"
            let dragged = MapProjectionApp.update m (DragStart(V2d(10.0, 10.0), V2d(800.0, 400.0), followed))
            Expect.isFalse dragged.follow "a drag takes the map over"
            Expect.isLessThan (dragged.center - followed).Length 1e-9 "and continues from where it was"
        }

        test "the wheel keeps following, and zooms about the pointer when it is off" {
            let size = V2d(1000.0, 500.0)
            let pointer = V2d(900.0, 120.0)
            let following = { MapProjectionApp.initial with follow = true; center = V2d(0.7, 0.1); zoom = 4.0 }
            let zoomed = MapProjectionApp.update following (Zoom(1.0, pointer, size))
            Expect.isGreaterThan zoomed.zoom following.zoom "zoomed in"
            Expect.isTrue zoomed.follow "still following"
            Expect.equal zoomed.center following.center "the centre belongs to the cursor, the wheel leaves it alone"
            let free = { following with follow = false }
            let freeZoomed = MapProjectionApp.update free (Zoom(1.0, pointer, size))
            Expect.notEqual freeZoomed.center free.center "with follow off the wheel zooms about the pointer"
        }
    ]
