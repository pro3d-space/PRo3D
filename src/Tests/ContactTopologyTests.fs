/// Faces of a geological map from its contacts (#843): synthetic bodies where the answer is
/// known in closed form.
///
/// n great circles in general position cut a sphere into n^2 - n + 2 faces, and a point's face
/// is given by its sign vector (the side of every circle it lies on) - so "two labels share a
/// face exactly when their sign vectors agree" checks locating as well as tracing.
module ContactTopologyTests

open System
open Expecto
open FsCheck
open Aardvark.Base
open PRo3D.Base.Geometry
open PRo3D.Base.Geometry.ContactTopology

// ---------------------------------------------------------------------------------------------
// synthetic bodies and contacts
// ---------------------------------------------------------------------------------------------

/// Dimorphos-sized
let private radius = 77.0

/// samples per full circle: 0.5 degrees, ~0.67 m at this radius
let private samples = 720

/// well below the sample spacing, well above the chords' sagitta (~1 mm)
let private tolerance = 0.03

let private basis (n : V3d) =
    let n = n.Normalized
    let u = (if abs n.Z < 0.9 then Vec.Cross(n, V3d.OOI) else Vec.Cross(n, V3d.IOO)).Normalized
    n, u, Vec.Cross(n, u)

/// Surface radius in direction d: a sphere, or a bumpy star-shaped body.
type private Body =
    | Sphere
    | Bumpy

let private surface (body : Body) (d : V3d) =
    let d = d.Normalized
    match body with
    | Sphere -> d * radius
    | Bumpy ->
        let lon = atan2 d.Y d.X
        let lat = asin (clamp -1.0 1.0 d.Z)
        d * radius * (1.0 + 0.1 * sin (3.0 * lon) * cos (2.0 * lat))

/// Circle of angular radius rho (pi/2 = great circle) around axis n, from angle a0 to a1
/// (radians), sampled at the global density; closed when it spans a full turn.
let private arc (body : Body) (n : V3d) (rho : float) (a0 : float) (a1 : float) =
    let n, u, v = basis n
    let count = max 2 (int (ceil (abs (a1 - a0) / Constant.PiTimesTwo * float samples)))
    Array.init (count + 1) (fun i ->
        let a = a0 + (a1 - a0) * float i / float count
        surface body (n * cos rho + (u * cos a + v * sin a) * sin rho))

let private circle body n rho = arc body n rho 0.0 Constant.PiTimesTwo

let private contact (pts : V3d[]) = { id = Guid.NewGuid(); points = pts }

let private label (body : Body) (d : V3d) (unitName : string) =
    { id = Guid.NewGuid(); position = surface body d; unit = unitName }

let private run (contacts : V3d[] list) (labels : Label list) =
    compute V3d.Zero tolerance (contacts |> List.map contact |> Array.ofList) (Array.ofList labels)

let private faceOfLabel (r : Result) (l : Label) = Map.tryFind l.id r.labelFace

// ---------------------------------------------------------------------------------------------
// generators
// ---------------------------------------------------------------------------------------------

let private directionGen =
    gen {
        let! x = Gen.choose (-1000, 1000)
        let! y = Gen.choose (-1000, 1000)
        let! z = Gen.choose (-1000, 1000)
        let v = V3d(float x, float y, float z)
        return if v.Length < 50.0 then V3d.OOI else v.Normalized
    }

/// 1..6 great circles plus 12 probe directions; general position is enforced in the generator
/// (not by discarding), so every generated case is checked
let private arrangementGen =
    gen {
        let! n = Gen.choose (1, 6)
        let! normals = Gen.listOfLength n directionGen
        let! probes = Gen.listOfLength 12 directionGen
        return Array.ofList normals, Array.ofList probes
    }
    |> Gen.filter (fun (normals, _) ->
        // same test as generalPosition below, duplicated here because of declaration order
        let n = normals.Length
        let ok = ref true
        for i in 0 .. n - 1 do
            for j in i + 1 .. n - 1 do
                if abs (Vec.Dot(normals.[i], normals.[j])) >= 0.95 then ok.Value <- false
                for k in j + 1 .. n - 1 do
                    let p = Vec.Cross(normals.[i], normals.[j]).Normalized
                    if abs (Vec.Dot(p, normals.[k])) <= 0.05 then ok.Value <- false
        ok.Value)

let private config = { FsCheckConfig.defaultConfig with maxTest = 60 }

/// General position: no two circles (nearly) the same, no three through (nearly) one point.
let private generalPositionWith (margin : float) (normals : V3d[]) =
    let n = normals.Length
    let pairsDistinct =
        seq { for i in 0 .. n - 1 do for j in i + 1 .. n - 1 -> abs (Vec.Dot(normals.[i], normals.[j])) < 0.95 }
        |> Seq.forall id
    let triplesFree =
        seq {
            for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 do
                    for k in j + 1 .. n - 1 ->
                        // the crossing of i and j must lie clearly off circle k
                        let p = Vec.Cross(normals.[i], normals.[j]).Normalized
                        abs (Vec.Dot(p, normals.[k])) > margin
        } |> Seq.forall id
    pairsDistinct && triplesFree

/// probe away from every circle (its face is unambiguous)
let private clearOfWith (margin : float) (normals : V3d[]) (d : V3d) =
    normals |> Array.forall (fun n -> abs (Vec.Dot(d, n)) > margin)

let private generalPosition normals = generalPositionWith 0.05 normals
let private clearOf normals d = clearOfWith 0.05 normals d

let private signs (normals : V3d[]) (d : V3d) =
    normals |> Array.map (fun n -> Vec.Dot(d, n) > 0.0)

// ---------------------------------------------------------------------------------------------

let tests () =
    Aardvark.Init()

    testList "ContactTopology" [

        test "one great circle: two faces, labels on either side" {
            let north = label Sphere V3d.OOI "A"
            let south = label Sphere -V3d.OOI "B"
            let r = run [ circle Sphere V3d.OOI (Constant.Pi / 2.0) ] [ north; south ]
            Expect.equal r.faces.Length 2 "a closed contact splits the body in two"
            Expect.isTrue r.eulerOk "V - E + F = 2"
            Expect.isEmpty r.openEnds "a closed contact has no open end"
            Expect.notEqual (faceOfLabel r north) (faceOfLabel r south) "the labels lie in different faces"
            let units = r.faces |> Array.map (fun f -> f.unit) |> Set.ofArray
            Expect.equal units (set [ FaceUnit.Unit "A"; FaceUnit.Unit "B" ]) "each face takes its label's unit"
        }

        test "two great circles: four faces" {
            let r = run [ circle Sphere V3d.OOI (Constant.Pi / 2.0); circle Sphere V3d.IOO (Constant.Pi / 2.0) ] []
            Expect.equal r.faces.Length 4 "2^2 - 2 + 2"
            Expect.isTrue r.eulerOk "V - E + F = 2"
            Expect.isTrue (r.faces |> Array.forall (fun f -> f.unit = FaceUnit.Unlabelled)) "no labels, all unlabelled"
        }

        test "a closed contact inside a face is a hole in it" {
            // equator, plus a small circle around the north pole
            let inCap   = label Sphere V3d.OOI "cap"
            let north   = label Sphere (V3d(1.0, 0.0, 0.5)) "north"
            let south   = label Sphere -V3d.OOI "south"
            let r =
                run [ circle Sphere V3d.OOI (Constant.Pi / 2.0); circle Sphere V3d.OOI 0.4 ]
                    [ inCap; north; south ]
            Expect.equal r.faces.Length 3 "cap, northern ring, southern hemisphere"
            Expect.isTrue r.eulerOk "every network on its own: V - E + F = 2"
            let northFace = r.faces |> Array.tryFind (fun f -> f.unit = FaceUnit.Unit "north")
            match northFace with
            | None -> failtest "the northern ring takes the 'north' label"
            | Some f -> Expect.equal f.cycles.Length 2 "the ring is bounded by the equator and by the cap's contact"
            Expect.equal (r.faces |> Array.filter (fun f -> f.unit = FaceUnit.Unit "cap")).Length 1 "the cap is its own face"
            Expect.equal (r.faces |> Array.filter (fun f -> f.unit = FaceUnit.Unit "south")).Length 1 "the south is its own face"
        }

        test "a dangling contact is trimmed and reported, the faces stay" {
            let dangling = arc Sphere V3d.IOO (Constant.Pi / 2.0) 0.3 1.0   // ends in the northern face
            let r = run [ circle Sphere V3d.OOI (Constant.Pi / 2.0); dangling ] []
            Expect.equal r.faces.Length 2 "a contact that cuts nothing changes nothing"
            Expect.equal r.openEnds.Length 2 "both ends of the dangling contact are open"
        }

        test "overshooting ends are trimmed, the crossing still cuts" {
            // a meridian arc crossing the equator twice, ends overshooting into the south
            let meridian = arc Sphere V3d.IOO (Constant.Pi / 2.0) (-0.3) (Constant.Pi + 0.3)
            let r = run [ circle Sphere V3d.OOI (Constant.Pi / 2.0); meridian ] []
            Expect.equal r.faces.Length 3 "the northern half is split in two"
            Expect.equal r.openEnds.Length 2 "the overshooting ends are open, their pieces trimmed"
            Expect.isTrue r.eulerOk "V - E + F = 2"
        }

        test "an end within tolerance of another contact connects" {
            // a meridian arc stopping just short of the equator on both ends
            let gap = 0.5 * tolerance / radius
            let meridian = arc Sphere V3d.IOO (Constant.Pi / 2.0) gap (Constant.Pi - gap)
            let r = run [ circle Sphere V3d.OOI (Constant.Pi / 2.0); meridian ] []
            Expect.equal r.faces.Length 3 "the undershooting ends still connect and split the north"
            Expect.isEmpty r.openEnds "no open end once both ends touch the equator"
        }

        test "labels of different units in one face are a conflict" {
            let a = label Sphere (V3d(0.2, 0.0, 1.0)) "A"
            let b = label Sphere (V3d(-0.2, 0.0, 1.0)) "B"
            let r = run [ circle Sphere V3d.OOI (Constant.Pi / 2.0) ] [ a; b ]
            let conflicted = r.faces |> Array.filter (fun f -> match f.unit with FaceUnit.Conflict _ -> true | _ -> false)
            Expect.equal conflicted.Length 1 "the northern face holds both and is flagged"
        }

        test "a bumpy body has the same topology as the sphere" {
            let normals = [| V3d.OOI; V3d(1.0, 0.2, 0.1); V3d(0.1, 1.0, -0.3) |]
            let r = run [ for n in normals -> circle Bumpy n (Constant.Pi / 2.0) ] []
            Expect.equal r.faces.Length 8 "3^2 - 3 + 2 on a non-spherical, star-shaped body"
            Expect.isTrue r.eulerOk "V - E + F = 2"
        }

        test "scale: 40 great circles (1562 faces), timed" {
            let rnd = Random(841)
            let normals = ResizeArray<V3d>()
            while normals.Count < 40 do
                let d = V3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5)
                if d.Length > 0.1 then
                    let c = Array.append (normals.ToArray()) [| d.Normalized |]
                    // 0.002 rad: crossings >= 0.15 m apart, still 5x the tolerance; 0.05 would
                    // reject almost every 40th circle against 780 crossings
                    if generalPositionWith 0.002 c then normals.Add d.Normalized
            let probes = Array.init 500 (fun _ -> V3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized) |> Array.filter (clearOfWith 0.005 (normals.ToArray()))
            let labels = probes |> Array.mapi (fun i d -> label Sphere d (string i))
            let contacts = [ for n in normals -> circle Sphere n (Constant.Pi / 2.0) ]
            run contacts [] |> ignore   // warm-up (JIT)
            let sw = Diagnostics.Stopwatch.StartNew()
            let r = run contacts (List.ofArray labels)
            sw.Stop()
            let segments = contacts |> List.sumBy (fun c -> c.Length - 1)
            printfn "[contact-topology] 40 circles, %d segments, %d labels: %d faces in %.0f ms" segments labels.Length r.faces.Length sw.Elapsed.TotalMilliseconds
            Expect.equal r.faces.Length (40 * 40 - 40 + 2) "n^2 - n + 2"
            Expect.isTrue r.eulerOk "V - E + F = 2"
            let consistent =
                seq {
                    for i in 0 .. probes.Length - 1 do
                        for j in i + 1 .. probes.Length - 1 ->
                            (signs (normals.ToArray()) probes.[i] = signs (normals.ToArray()) probes.[j]) = (faceOfLabel r labels.[i] = faceOfLabel r labels.[j])
                } |> Seq.forall id
            Expect.isTrue consistent "every pair of labels: same face iff same sides of all circles"
        }

        testPropertyWithConfig config "n great circles: n^2 - n + 2 faces, labels share a face iff their sides agree" <|
            Prop.forAll (Arb.fromGen arrangementGen) (fun (normals, probes) ->
                let normals = normals |> Array.distinctBy (fun n -> V3l(int64 (n.X * 1000.0), int64 (n.Y * 1000.0), int64 (n.Z * 1000.0)))
                let probes = probes |> Array.filter (clearOf normals)
                (generalPosition normals) ==> lazy (
                    let labels = probes |> Array.mapi (fun i d -> label Sphere d (string i))
                    let r = run [ for n in normals -> circle Sphere n (Constant.Pi / 2.0) ] (List.ofArray labels)
                    let n = normals.Length
                    let facesOk = r.faces.Length = n * n - n + 2
                    let placedAll = labels |> Array.forall (fun l -> Map.containsKey l.id r.labelFace)
                    let consistent =
                        seq {
                            for i in 0 .. probes.Length - 1 do
                                for j in i + 1 .. probes.Length - 1 ->
                                    let same = signs normals probes.[i] = signs normals probes.[j]
                                    let together = faceOfLabel r labels.[i] = faceOfLabel r labels.[j]
                                    same = together
                        } |> Seq.forall id
                    facesOk && r.eulerOk && placedAll && consistent))
    ]
