module FillVolumeTests

open Expecto
open FsCheck
open Aardvark.Base
open PRo3D.Base
open PRo3D.Base.Annotation

// ---------------------------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------------------------

/// Directed edges whose reverse is missing. Empty means the triangle list is a closed surface
/// with consistent orientation - exactly what the z-fail stencil count needs; a single missing
/// reverse edge is a leak.
let private openEdges (tris : V3d[]) =
    let edges = System.Collections.Generic.Dictionary<struct(V3d * V3d), int>()
    let add a b =
        let k = struct(a, b)
        match edges.TryGetValue k with
        | true, n -> edges.[k] <- n + 1
        | _       -> edges.[k] <- 1
    for i in 0 .. 3 .. tris.Length - 3 do
        add tris.[i] tris.[i + 1]
        add tris.[i + 1] tris.[i + 2]
        add tris.[i + 2] tris.[i]
    [ for KeyValue(struct(a, b), n) in edges do
        let back = match edges.TryGetValue(struct(b, a)) with | true, m -> m | _ -> 0
        if back <> n then yield (a, b, n, back) ]

/// Signed volume by the divergence theorem: positive for an outward-oriented closed surface.
let private signedVolume (tris : V3d[]) =
    let mutable acc = 0.0
    for i in 0 .. 3 .. tris.Length - 3 do
        acc <- acc + Vec.Dot(tris.[i], Vec.Cross(tris.[i + 1], tris.[i + 2])) / 6.0
    acc

let private xyChart = SurfaceChart.ofPlane (Plane3d(V3d.OOI, V3d.Zero))

let private square = [| V3d(0,0,0); V3d(10,0,0); V3d(10,10,0); V3d(0,10,0) |]

let private fixedReach (m : float) = fun (_ : float) -> m

let private build reach chart axis ring =
    FillVolume.tryCompute (fixedReach reach) chart axis ring |> Option.map (fun v -> v.prism)

/// Rings in the XY plane at strictly increasing angles: simple, any convexity.
let private simpleRingGen =
    gen {
        let! n = Gen.choose (3, 12)
        let! radii = Gen.listOfLength n (Gen.choose (1, 20))
        return
            radii
            |> List.mapi (fun i r ->
                let a = Constant.PiTimesTwo * float i / float n
                V3d(float r * cos a, float r * sin a, 0.0))
            |> Array.ofList
    }

/// Anything, including self-intersecting rings - the tessellator invents vertices there, and
/// the walls must still close the volume.
let private arbitraryRingGen =
    gen {
        let! n = Gen.choose (3, 12)
        let! coords = Gen.listOfLength n (Gen.choose (-10, 10) |> Gen.two)
        let! zs = Gen.listOfLength n (Gen.choose (-3, 3))
        return
            List.zip coords zs
            |> List.map (fun ((x, y), z) -> V3d(float x, float y, 0.1 * float z))
            |> Array.ofList
    }

let private config = { FsCheckConfig.defaultConfig with maxTest = 2000 }

// ---------------------------------------------------------------------------------------------

let tests () =
    Aardvark.Init()

    testList "FillVolume" [

        testList "FillReach" [

            test "Auto is half the outline size below the body cap" {
                Expect.floatClose Accuracy.high (FillReach.resolve Planet.Mars FillReach.Auto 10.0) 5.0
                    "a 10 m outline on Mars reaches 5 m"
            }

            test "Auto is capped by the body" {
                Expect.floatClose Accuracy.high (FillReach.resolve Planet.Dimorphos FillReach.Auto 1000.0) 15.0
                    "Dimorphos caps the auto reach at 15 m"
            }

            test "Metres is taken as is, negatives clamp to zero" {
                Expect.floatClose Accuracy.high (FillReach.resolve Planet.Mars (FillReach.Metres 42.0) 1.0) 42.0 "fixed reach"
                Expect.floatClose Accuracy.high (FillReach.resolve Planet.Mars (FillReach.Metres -3.0) 1.0) 0.0 "no negative reach"
            }

            test "every body has a positive cap" {
                for p in System.Enum.GetValues(typeof<Planet>) |> Seq.cast<Planet> do
                    Expect.isGreaterThan (FillReach.maxAuto p) 0.0 (sprintf "%A needs a positive cap" p)
            }
        ]

        testList "halfHeight" [

            test "a flat ring gets exactly the reach" {
                Expect.floatClose Accuracy.high (FillVolume.halfHeight 2.0 V3d.OOI square) 2.0 "no spread along z"
            }

            test "the ring's spread along the axis is added" {
                let tilted = [| V3d(0,0,0); V3d(10,0,3); V3d(10,10,3); V3d(0,10,-1) |]
                Expect.floatClose Accuracy.high (FillVolume.halfHeight 2.0 V3d.OOI tilted) 6.0 "spread 4 + reach 2"
            }
        ]

        testList "tryCompute" [

            test "square: closed, outward, volume = area * height" {
                match build 2.0 xyChart V3d.OOI square with
                | None -> failtest "a square must produce a volume"
                | Some tris ->
                    Expect.isEmpty (openEdges tris) "the prism must be closed"
                    Expect.floatClose Accuracy.medium (signedVolume tris) (100.0 * 4.0) "outward, 10x10 by 2*2"
            }

            test "square: two cap triangles each side and two per wall" {
                match build 2.0 xyChart V3d.OOI square with
                | None -> failtest "a square must produce a volume"
                | Some tris -> Expect.equal (tris.Length / 3) (2 + 2 + 4 * 2) "caps 2+2, walls 4*2"
            }

            test "the sign of the axis does not matter" {
                let up   = build 2.0 xyChart V3d.OOI square |> Option.map signedVolume
                let down = build 2.0 xyChart -V3d.OOI square |> Option.map signedVolume
                Expect.isSome up "volume along +z"
                Expect.floatClose Accuracy.medium down.Value up.Value "same outward volume along -z"
            }

            test "reversed ring winding gives the same volume" {
                let fwd = build 2.0 xyChart V3d.OOI square |> Option.map signedVolume
                let rev = build 2.0 xyChart V3d.OOI (Array.rev square) |> Option.map signedVolume
                Expect.floatClose Accuracy.medium rev.Value fwd.Value "orientation is fixed internally"
            }

            test "an outline on a vertical rock face extrudes out of the face" {
                // ring in the XZ plane, axis = its normal (y); a sky-up extrusion would be edge-on
                let wall = square |> Array.map (fun p -> V3d(p.X, 0.0, p.Y))
                let chart = SurfaceChart.ofPlane (Plane3d(V3d.OIO, V3d.Zero))
                match build 1.0 chart V3d.OIO wall with
                | None -> failtest "a vertical outline must produce a volume"
                | Some tris ->
                    Expect.isEmpty (openEdges tris) "closed"
                    Expect.floatClose Accuracy.medium (signedVolume tris) (100.0 * 2.0) "10x10 by 2*1"
            }

            test "the reach comes from the outline size" {
                let mutable seen = nan
                let reachOf s = seen <- s; 1.0
                FillVolume.tryCompute reachOf xyChart V3d.OOI square |> ignore
                Expect.floatClose Accuracy.medium seen (sqrt 200.0) "bounding-box diagonal of the square"
            }

            test "degenerate input gives None" {
                Expect.isNone (build 1.0 xyChart V3d.OOI [| V3d.Zero; V3d.IOO |]) "two points"
                Expect.isNone (build 1.0 xyChart V3d.Zero square) "zero axis"
                Expect.isNone (build 1.0 xyChart (V3d(nan, 0.0, 1.0)) square) "NaN axis"
                Expect.isNone (build 0.0 xyChart V3d.OOI square) "flat ring with no reach has no volume"
            }

            test "a self-intersecting ring whose cap contains a sliver triangle stays closed" {
                // FsCheck counterexample: a crossing vertex nearly collinear with (-8,10)-(-7,3)
                // gives a sliver whose winding sign is rounding noise; re-orienting triangles one
                // by one flipped it and left the prism open
                let ring =
                    [| V3d(5,-4,0.1); V3d(-7,3,-0.2); V3d(5,1,0); V3d(-6,-4,0); V3d(-8,10,0.3); V3d(6,-1,-0.1)
                       V3d(0,8,0.1); V3d(-2,-3,0.1); V3d(-8,-10,-0.3); V3d(-10,0,0); V3d(5,10,0.2) |]
                match build 1.0 xyChart V3d.OOI ring with
                | None -> failtest "the ring must produce a volume"
                | Some tris ->
                    Expect.isEmpty (openEdges tris) "closed"
                    Expect.isGreaterThan (signedVolume tris) 0.0 "outward"
            }

            testPropertyWithConfig config "simple rings: closed, volume = 2h * cap area" <|
                Prop.forAll (Arb.fromGen simpleRingGen) (fun ring ->
                    match build 1.5 xyChart V3d.OOI ring with
                    | None -> true // degenerate rings (all collinear) may refuse
                    | Some tris ->
                        let capArea =
                            match PolygonFill.tryComputeFill xyChart ring with
                            | Some m ->
                                let mutable a = 0.0
                                for i in 0 .. 3 .. m.positions.Length - 3 do
                                    a <- a + 0.5 * Vec.Length(Vec.Cross(m.positions.[i + 1] - m.positions.[i], m.positions.[i + 2] - m.positions.[i]))
                                a
                            | None -> nan
                        List.isEmpty (openEdges tris)
                        && abs (signedVolume tris - capArea * 3.0) <= 1e-6 * (1.0 + capArea))

            testPropertyWithConfig config "arbitrary rings, self-intersections included: closed and outward" <|
                Prop.forAll (Arb.fromGen arbitraryRingGen) (fun ring ->
                    match build 1.0 xyChart V3d.OOI ring with
                    | None -> true
                    | Some tris -> List.isEmpty (openEdges tris) && signedVolume tris > 0.0)
        ]
    ]
