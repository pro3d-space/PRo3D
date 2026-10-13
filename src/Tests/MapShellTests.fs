/// Shell volumes of geological map units (#843): every volume closed and outward, and the
/// volumes of a map's faces partition the shell - each direction lies in exactly one of them.
module MapShellTests

open System
open System.Collections.Generic
open Expecto
open FsCheck
open Aardvark.Base
open PRo3D.Base.Geometry
open PRo3D.Base.Geometry.ContactTopology

let private radius = 77.0
let private samples = 720
let private tolerance = 0.03

let private shell : MapShell.Shell =
    { center = V3d.Zero; inner = radius - 15.0; outer = radius + 15.0; maxAngle = MapShell.maxAngleFor (radius + 15.0) 15.0 }

let private basis (n : V3d) =
    let n = n.Normalized
    let u = (if abs n.Z < 0.9 then Vec.Cross(n, V3d.OOI) else Vec.Cross(n, V3d.IOO)).Normalized
    n, u, Vec.Cross(n, u)

/// closed circle of angular radius rho around n
let private circle (n : V3d) (rho : float) =
    let n, u, v = basis n
    Array.init (samples + 1) (fun i ->
        let a = Constant.PiTimesTwo * float (i % samples) / float samples
        (n * cos rho + (u * cos a + v * sin a) * sin rho) * radius)

let private topology (contacts : V3d[] list) =
    let cs = contacts |> List.map (fun p -> { id = Guid.NewGuid(); points = p }) |> Array.ofList
    compute V3d.Zero tolerance cs [||]

/// every directed edge has its reverse: closed and consistently oriented
let private isClosed (tris : V3d[]) =
    let count = Dictionary<struct(V3d * V3d), int>()
    let add a b =
        let k = struct(a, b)
        match count.TryGetValue k with
        | true, c -> count.[k] <- c + 1
        | _ -> count.[k] <- 1
    for t in 0 .. tris.Length / 3 - 1 do
        let a, b, c = tris.[3 * t], tris.[3 * t + 1], tris.[3 * t + 2]
        add a b; add b c; add c a
    count |> Seq.forall (fun kv ->
        let struct(a, b) = kv.Key
        match count.TryGetValue(struct(b, a)) with
        | true, c -> c = kv.Value
        | _ -> false)

let private signedVolume (tris : V3d[]) =
    let mutable v = 0.0
    for t in 0 .. tris.Length / 3 - 1 do
        let a, b, c = tris.[3 * t], tris.[3 * t + 1], tris.[3 * t + 2]
        v <- v + Vec.Dot(a, Vec.Cross(b, c)) / 6.0
    v

/// generalised winding number of point x (~1 inside a closed outward mesh, ~0 outside)
let private winding (tris : V3d[]) (x : V3d) =
    let mutable acc = 0.0
    for t in 0 .. tris.Length / 3 - 1 do
        let a = tris.[3 * t] - x
        let b = tris.[3 * t + 1] - x
        let c = tris.[3 * t + 2] - x
        let la, lb, lc = a.Length, b.Length, c.Length
        let num = Vec.Dot(a, Vec.Cross(b, c))
        let den = la * lb * lc + Vec.Dot(a, b) * lc + Vec.Dot(b, c) * la + Vec.Dot(c, a) * lb
        acc <- acc + 2.0 * atan2 num den
    acc / (4.0 * Constant.Pi)

let private shellVolume = 4.0 / 3.0 * Constant.Pi * (pown shell.outer 3 - pown shell.inner 3)

let private volumes (r : Result) =
    r.faces |> Array.map (fun f ->
        match MapShell.tryFace shell f.cycles with
        | Some v -> v
        | None -> failwithf "face %d (%d cycles) did not triangulate" f.id f.cycles.Length)

/// a terrain point in direction d, between the shell radii
let private terrain (d : V3d) = d.Normalized * radius

/// each probe lies inside exactly one volume, and the volumes add up to the shell
let private partitions (vs : MapShell.FaceVolume[]) (probes : V3d[]) =
    for v in vs do
        Expect.isTrue (isClosed v.volume) "volume closed and consistently oriented"
        Expect.isGreaterThan (signedVolume v.volume) 0.0 "volume outward"
        Expect.isGreaterThan (winding v.volume (terrain v.interior)) 0.5 "the interior point lies in its face"
    let total = vs |> Array.sumBy (fun v -> signedVolume v.volume)
    Expect.floatClose { absolute = 0.0; relative = 0.03 } total shellVolume "volumes fill the shell"
    for p in probes do
        let inside = vs |> Array.filter (fun v -> winding v.volume (terrain p) > 0.5)
        Expect.equal inside.Length 1 (sprintf "direction %A in exactly one volume" p)

let private directionGen =
    gen {
        let! x = Gen.choose (-1000, 1000)
        let! y = Gen.choose (-1000, 1000)
        let! z = Gen.choose (-1000, 1000)
        let v = V3d(float x, float y, float z)
        return if v.Length < 50.0 then V3d.OOI else v.Normalized
    }

let private arrangementGen =
    gen {
        let! n = Gen.choose (1, 5)
        let! normals = Gen.listOfLength n directionGen
        let! probes = Gen.listOfLength 8 directionGen
        return Array.ofList normals, Array.ofList probes
    }
    |> Gen.filter (fun (normals, probes) ->
        let n = normals.Length
        let mutable ok = true
        for i in 0 .. n - 1 do
            for j in i + 1 .. n - 1 do
                if abs (Vec.Dot(normals.[i], normals.[j])) >= 0.95 then ok <- false
                for k in j + 1 .. n - 1 do
                    let p = Vec.Cross(normals.[i], normals.[j]).Normalized
                    if abs (Vec.Dot(p, normals.[k])) <= 0.05 then ok <- false
        // probes clear of every contact, so their face is unambiguous
        ok && probes |> Array.forall (fun d -> normals |> Array.forall (fun c -> abs (Vec.Dot(d, c)) > 0.05)))

let tests () =
    Aardvark.Init()

    testList "MapShell" [

        test "whole body: closed shell" {
            let v = MapShell.wholeBody shell
            Expect.isTrue (isClosed v.volume) "closed"
            Expect.floatClose { absolute = 0.0; relative = 0.03 } (signedVolume v.volume) shellVolume "shell volume"
            Expect.isGreaterThan (winding v.volume (terrain V3d.IOO)) 0.5 "terrain inside"
        }

        test "one great circle: two hemispheres" {
            let r = topology [ circle V3d.OOI (Constant.Pi / 2.0) ]
            let vs = volumes r
            Expect.equal vs.Length 2 "two faces"
            for v in vs do
                Expect.floatClose { absolute = 0.0; relative = 0.03 } (signedVolume v.volume) (shellVolume / 2.0) "half the shell"
            partitions vs [| V3d.OOI; -V3d.OOI; V3d(1.0, 2.0, 0.3); V3d(-1.0, 0.2, -0.5) |]
        }

        test "band: two parallel circles, the middle face larger than a hemisphere's boundary allows" {
            let lat = Constant.Pi / 6.0
            let r = topology [ circle V3d.OOI (Constant.Pi / 2.0 - lat); circle -V3d.OOI (Constant.Pi / 2.0 - lat) ]
            let vs = volumes r
            Expect.equal vs.Length 3 "north cap, band, south cap"
            partitions vs [| V3d.OOI; -V3d.OOI; V3d.IOO; V3d(1.0, 1.0, 0.2); V3d(-1.0, 0.0, 0.9) |]
        }

        test "small loop: the face around it covers nearly the whole body" {
            let r = topology [ circle V3d.IOO 0.2 ]
            let vs = volumes r
            Expect.equal vs.Length 2 "inside and outside"
            partitions vs [| V3d.IOO; -V3d.IOO; V3d.OIO; V3d(1.0, 0.1, 0.05); V3d(0.2, -1.0, 0.4) |]
        }

        test "hole: a loop inside a hemisphere" {
            let r = topology [ circle V3d.OOI (Constant.Pi / 2.0); circle (V3d(0.3, 0.2, 1.0)) 0.4 ]
            let vs = volumes r
            Expect.equal vs.Length 3 "south, north with hole, the hole"
            partitions vs [| V3d.OOI; V3d(0.3, 0.2, 1.0); -V3d.OOI; V3d(1.0, 0.0, 0.1); V3d(-1.0, 0.0, 0.3) |]
        }

        test "long contact edges: neighbouring volumes still share their walls" {
            // a contact of four 90-degree pieces along the equator, and a meridian: the long
            // pieces are densified identically from both sides and never split again, so the
            // probes right next to them land in exactly one volume
            let square = [| for i in 0 .. 4 -> let a = Constant.PiTimesTwo * float (i % 4) / 4.0 in V3d(cos a, sin a, 0.0) * radius |]
            let r = topology [ square; circle V3d.OIO (Constant.Pi / 2.0) ]
            let vs = volumes r
            Expect.equal vs.Length 4 "four faces"
            let near (lonDeg : float) (latDeg : float) =
                let lon = lonDeg * Constant.RadiansPerDegree
                let lat = latDeg * Constant.RadiansPerDegree
                V3d(cos lat * cos lon, cos lat * sin lon, sin lat)
            partitions vs [| near 45.0 0.5; near 45.0 -0.5; near 135.0 0.3; near 200.0 -0.3; V3d.OOI; -V3d.OOI |]
        }

        test "a small non-convex map at Mars scale: every face is drawn" {
            // a C-shaped unit a few km across on a 3390 km body: the face around it (the rest of
            // the planet) needs a projection pole inside the C - no mean or Fibonacci direction
            // falls into it
            let R = 3.39e6
            let at (y : float) (z : float) = (V3d(R, y, z)).Normalized * R
            let corners = [| 0.0, 0.0; 4000.0, 0.0; 4000.0, 4000.0; 0.0, 4000.0; 0.0, 3000.0; 3000.0, 3000.0; 3000.0, 1000.0; 0.0, 1000.0 |]
            let ring =
                [| for i in 0 .. corners.Length - 1 do
                     let (y0, z0) = corners.[i]
                     let (y1, z1) = corners.[(i + 1) % corners.Length]
                     let n = max 1 (int (Vec.Distance(V2d(y0, z0), V2d(y1, z1)) / 20.0))
                     for k in 0 .. n - 1 do
                         let t = float k / float n
                         yield at (y0 + (y1 - y0) * t) (z0 + (z1 - z0) * t)
                   yield at 0.0 0.0 |]
            let cs = [| { id = Guid.NewGuid(); points = ring } |]
            let r = compute V3d.Zero 6.0 cs [||]
            Expect.equal r.faces.Length 2 "inside and outside the C"
            let marsShell = (MapShell.ofPoints V3d.Zero 1000.0 ring) |> Option.defaultWith (fun () -> failwith "no shell")
            let vs =
                r.faces |> Array.map (fun f ->
                    match MapShell.tryFace marsShell f.cycles with
                    | Some v -> v
                    | None -> failwithf "face %d not drawn" f.id)
            for v in vs do
                Expect.isTrue (isClosed v.volume) "closed"
            let inside (p : V3d) =
                vs |> Array.filter (fun v -> winding v.volume p > 0.5) |> Array.length
            Expect.equal (inside (at 500.0 500.0)) 1 "in the C's lower arm"
            Expect.equal (inside (at 1500.0 2000.0)) 1 "in the notch"
            Expect.equal (inside (V3d(-R, 0.0, 0.0))) 1 "on the far side"
        }

        testPropertyWithConfig { FsCheckConfig.defaultConfig with maxTest = 25 }
            "great circles: the face volumes partition the shell" <| fun () ->
            Prop.forAll (Arb.fromGen arrangementGen) (fun (normals, probes) ->
                let r = topology [ for n in normals -> circle n (Constant.Pi / 2.0) ]
                partitions (volumes r) probes)
    ]
