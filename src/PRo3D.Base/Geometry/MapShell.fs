namespace PRo3D.Base.Geometry

open System
open System.Collections.Generic
open Aardvark.Base

/// Closed volumes for drawing the units of a geological map (#843) as fills (#842).
///
/// A fill colours the terrain inside a closed volume. For a map unit that volume is the part
/// of a spherical shell around the body centre that lies over the unit: everything between an
/// inner and an outer radius, in the directions of the unit's face (ContactTopology). Walls are
/// radial, so two neighbouring units share their walls exactly and the terrain under a contact
/// belongs to one of them - whatever the relief, and for any unit size up to the whole body.
///
/// The face is triangulated in a stereographic chart whose pole lies outside the face, so even
/// a face larger than a hemisphere (or a band around the body) maps to a bounded region of the
/// plane. Triangles are then subdivided until no edge spans more than `maxAngle`: the outer cap
/// consists of chords, which dip below the outer sphere, and must stay above the terrain.
///
/// Pure: no rendering, no model.
module MapShell =

    type Shell =
        {
            center   : V3d
            /// below the lowest terrain the map colours
            inner    : float
            /// above the highest terrain the map colours
            outer    : float
            /// the longest edge of a cap triangle, as an angle seen from the centre (radians)
            maxAngle : float
        }

    type FaceVolume =
        {
            /// outer cap, outward, triangle list
            cap    : V3d[]
            /// closed and outward-oriented: outer cap, inner cap, radial walls; triangle list
            volume : V3d[]
        }

    /// Longest cap edge for which the outer cap's chords stay at least half the reach above
    /// the highest terrain. At most 20 degrees, so caps stay reasonably round regardless.
    let maxAngleFor (outer : float) (reach : float) =
        if not (outer > 0.0) || not (reach > 0.0) then 0.35
        else
            let c = 1.0 - (0.5 * reach) / outer
            let a = if c <= -1.0 then Constant.Pi else 2.0 * acos (clamp -1.0 1.0 c)
            clamp 0.005 0.35 a

    /// The shell around terrain sampled by `points` (e.g. the contacts' samples): their radii
    /// about `center`, widened by `reach` both ways. The inner radius stays above 5 % of the
    /// lowest point, well clear of the centre.
    let ofPoints (center : V3d) (reach : float) (points : V3d[]) : Option<Shell> =
        if points.Length = 0 then None
        else
            let mutable lo = Double.PositiveInfinity
            let mutable hi = 0.0
            for p in points do
                let r = Vec.Distance(p, center)
                if r < lo then lo <- r
                if r > hi then hi <- r
            if not (hi > 0.0) || Double.IsNaN lo then None
            else
                let reach = max reach (1e-3 * hi)
                let outer = hi + reach
                Some {
                    center   = center
                    inner    = max (lo - reach) (0.05 * lo)
                    outer    = outer
                    maxAngle = maxAngleFor outer reach
                }

    // -----------------------------------------------------------------------------------------
    // directions
    // -----------------------------------------------------------------------------------------

    let private angle (a : V3d) (b : V3d) =
        // atan2 of |a x b| and a.b: accurate for small and large angles alike
        atan2 (Vec.Cross(a, b).Length) (Vec.Dot(a, b))

    let private lexLess (a : V3d) (b : V3d) =
        if a.X <> b.X then a.X < b.X
        elif a.Y <> b.Y then a.Y < b.Y
        else a.Z < b.Z

    /// Points strictly between unit directions a and b, so that no piece spans more than
    /// maxAngle. Computed from the lexicographically smaller end, so the neighbouring face,
    /// which walks the same edge the other way, gets bit-identical points.
    let private between (maxAngle : float) (a : V3d) (b : V3d) : V3d[] =
        let n = int (ceil (angle a b / maxAngle))
        if n <= 1 then Array.empty
        else
            let s, e = if lexLess a b then a, b else b, a
            let pts = Array.init (n - 1) (fun i ->
                let t = float (i + 1) / float n
                (s * (1.0 - t) + e * t).Normalized)
            if lexLess a b then pts else Array.rev pts

    /// A cycle as unit directions, densified to maxAngle, without consecutive duplicates.
    let private directions (shell : Shell) (cycle : V3d[]) : V3d[] =
        let dirs = cycle |> Array.map (fun p -> (p - shell.center).Normalized)
        let res = List<V3d>(dirs.Length)
        for i in 0 .. dirs.Length - 1 do
            let a = dirs.[i]
            let b = dirs.[(i + 1) % dirs.Length]
            if res.Count = 0 || not (res.[res.Count - 1] = a) then res.Add a
            for p in between shell.maxAngle a b do res.Add p
        while res.Count > 1 && res.[0] = res.[res.Count - 1] do res.RemoveAt(res.Count - 1)
        res.ToArray()

    let private perpendicular (n : V3d) =
        let c = Vec.Cross(n, V3d.OOI)
        let c = if c.LengthSquared < 1e-12 then Vec.Cross(n, V3d.IOO) else c
        c.Normalized

    /// Angle of d in the tangent plane at n, counter-clockwise seen from outside.
    let private tangentAngle (n : V3d) (d : V3d) =
        let u = perpendicular n
        let v = Vec.Cross(n, u)
        atan2 (Vec.Dot(d, v)) (Vec.Dot(d, u))

    /// Whether direction q lies outside the face bounded by `cycles` (unit directions, face on
    /// the left seen from outside): q's nearest boundary point has q on its right.
    let private isOutside (cycles : V3d[][]) (q : V3d) =
        let mutable bestD = Double.PositiveInfinity
        let mutable bestC = -1
        let mutable bestI = -1
        let mutable bestT = 0.0
        for c in 0 .. cycles.Length - 1 do
            let cy = cycles.[c]
            for i in 0 .. cy.Length - 1 do
                let a = cy.[i]
                let b = cy.[(i + 1) % cy.Length]
                let d = b - a
                let l2 = Vec.Dot(d, d)
                let t = if l2 <= 1e-30 then 0.0 else clamp 0.0 1.0 (Vec.Dot(q - a, d) / l2)
                let dist = Vec.Distance(a + d * t, q)
                if dist < bestD then
                    bestD <- dist
                    bestC <- c
                    bestI <- i
                    bestT <- t
        if bestC < 0 then false
        else
            let cy = cycles.[bestC]
            let n = cy.Length
            let a = cy.[bestI]
            let b = cy.[(bestI + 1) % n]
            if bestT > 1e-9 && bestT < 1.0 - 1e-9 then
                let p = a + (b - a) * bestT
                Vec.Dot(Vec.Cross(b - a, q - p), p) < 0.0
            else
                // at a vertex v of the path u -> v -> w: the face is the sector swept
                // counter-clockwise from w to u
                let vi = if bestT <= 1e-9 then bestI else (bestI + 1) % n
                let v = cy.[vi]
                let u = cy.[(vi - 1 + n) % n]
                let w = cy.[(vi + 1) % n]
                let wrap x = let r = x % Constant.PiTimesTwo in if r < 0.0 then r + Constant.PiTimesTwo else r
                let aw = tangentAngle v (w - v)
                let au = wrap (tangentAngle v (u - v) - aw)
                let aq = wrap (tangentAngle v (q - v) - aw)
                not (aq < au)

    /// Fibonacci sphere: n roughly evenly spread unit directions.
    let private fibonacci (n : int) =
        let golden = Constant.Pi * (3.0 - sqrt 5.0)
        Array.init n (fun i ->
            let z = 1.0 - 2.0 * (float i + 0.5) / float n
            let r = sqrt (max 0.0 (1.0 - z * z))
            let a = golden * float i
            V3d(r * cos a, r * sin a, z))

    let private fibonacciCandidates = fibonacci 256

    /// A projection pole outside the face, as far from its boundary as the candidates allow.
    let private pole (cycles : V3d[][]) : Option<V3d> =
        let mean (ds : V3d[]) =
            let mutable s = V3d.Zero
            for d in ds do s <- s + d
            s
        let candidates = List<V3d>()
        let add (v : V3d) = if v.Length > 1e-6 then candidates.Add v.Normalized
        let all = mean (Array.concat cycles)
        add -all
        add all
        for c in cycles |> Array.sortByDescending (fun c -> c.Length) |> Array.truncate 16 do
            let m = mean c
            add -m
            add m
        candidates.AddRange fibonacciCandidates
        // clearance: the angular distance to the nearest boundary vertex (chord length, same order)
        let clearance (q : V3d) =
            let mutable m = Double.PositiveInfinity
            for c in cycles do
                for d in c do
                    let x = Vec.Distance(d, q)
                    if x < m then m <- x
            m
        candidates.ToArray()
        |> Array.map (fun q -> q, clearance q)
        |> Array.sortByDescending snd
        |> Array.tryPick (fun (q, cl) -> if cl > 1e-9 && isOutside cycles q then Some q else None)

    /// Stereographic chart with its pole at `pole`: orientation-preserving seen from outside.
    type private Chart(pole : V3d) =
        let c = -pole
        let u = perpendicular c
        let v = Vec.Cross(c, u)
        member x.ToChart(d : V3d) =
            let k = 1.0 + Vec.Dot(d, c)
            V2d(Vec.Dot(d, u) / k, Vec.Dot(d, v) / k)
        member x.ToSphere(p : V2d) =
            let r2 = p.LengthSquared
            (u * (2.0 * p.X) + v * (2.0 * p.Y) + c * (1.0 - r2)) / (1.0 + r2)

    // -----------------------------------------------------------------------------------------
    // triangulation
    // -----------------------------------------------------------------------------------------

    /// Splits triangles (chart point, direction) until every edge spans at most maxAngle.
    /// Midpoints are taken in the chart and depend only on the edge's ends, so triangles
    /// sharing an edge split it identically and the mesh stays conforming.
    let rec private subdivide (chart : Chart) (maxAngle : float) (depth : int) (out : List<V3d>)
                              (pa : V2d, a : V3d) (pb : V2d, b : V3d) (pc : V2d, c : V3d) =
        let long (x : V3d) (y : V3d) = depth < 16 && angle x y > maxAngle
        let ab, bc, ca = long a b, long b c, long c a
        if not (ab || bc || ca) then
            out.Add a; out.Add b; out.Add c
        else
            let mid (p : V2d, _ : V3d) (q : V2d, _ : V3d) =
                let m = (p + q) * 0.5
                m, chart.ToSphere(m).Normalized
            let A = (pa, a)
            let B = (pb, b)
            let C = (pc, c)
            let go = subdivide chart maxAngle (depth + 1) out
            match ab, bc, ca with
            | true, true, true ->
                let mab, mbc, mca = mid A B, mid B C, mid C A
                go A mab mca; go mab B mbc; go mca mbc C; go mab mbc mca
            | true, false, false -> let m = mid A B in go A m C; go m B C
            | false, true, false -> let m = mid B C in go A B m; go A m C
            | false, false, true -> let m = mid C A in go A B m; go m B C
            | true, true, false ->
                let mab, mbc = mid A B, mid B C
                go mab B mbc; go A mab mbc; go A mbc C
            | false, true, true ->
                let mbc, mca = mid B C, mid C A
                go mca mbc C; go A B mbc; go A mbc mca
            | true, false, true ->
                let mab, mca = mid A B, mid C A
                go A mab mca; go mab B C; go mab C mca
            | false, false, false -> ()

    /// Outward cap triangles (unit directions) of a face, or None when it cannot be
    /// triangulated.
    let private capDirections (shell : Shell) (cycles : V3d[][]) : Option<V3d[]> =
        let cycles = cycles |> Array.map (directions shell) |> Array.filter (fun c -> c.Length >= 3)
        if cycles.Length = 0 then None else
        match pole cycles with
        | None -> None
        | Some p ->
            let chart = Chart(p)
            let contours = cycles |> Array.map (fun c -> c |> Array.map chart.ToChart, c)
            let combine (w : float[]) (ds : V3d[]) =
                let mutable acc = V3d.Zero
                for i in 0 .. (min w.Length ds.Length) - 1 do acc <- acc + ds.[i] * w.[i]
                acc.Normalized
            let triangles =
                try PolygonTessellator.Triangulate(contours, TessellationRule.EvenOdd, combine)
                with _ -> []
            match triangles with
            | [] -> None
            | ts ->
                // libtess winds every triangle the same way; settle which from the summed area,
                // never per triangle (slivers have noise for a sign)
                let mutable area = 0.0
                for t in ts do
                    let e1 = t.P1 - t.P0
                    let e2 = t.P2 - t.P0
                    area <- area + (e1.X * e2.Y - e1.Y * e2.X)
                let ccw = area >= 0.0
                let out = List<V3d>()
                for t in ts do
                    let a = (t.P0, t.A0)
                    let b = (t.P1, t.A1)
                    let c = (t.P2, t.A2)
                    if ccw then subdivide chart shell.maxAngle 0 out a b c
                    else subdivide chart shell.maxAngle 0 out a c b
                Some (out.ToArray())

    /// The closed shell volume over cap triangles given as unit directions (outward).
    let private shellOver (shell : Shell) (cap : V3d[]) : FaceVolume =
        let o (d : V3d) = shell.center + d * shell.outer
        let i (d : V3d) = shell.center + d * shell.inner
        let triCount = cap.Length / 3
        let outerCap = Array.zeroCreate cap.Length
        let res = List<V3d>(cap.Length * 2 + 64)
        let edges = HashSet<struct(V3d * V3d)>()
        for t in 0 .. triCount - 1 do
            let a = cap.[3 * t]
            let b = cap.[3 * t + 1]
            let c = cap.[3 * t + 2]
            outerCap.[3 * t] <- o a; outerCap.[3 * t + 1] <- o b; outerCap.[3 * t + 2] <- o c
            res.Add(o a); res.Add(o b); res.Add(o c)
            res.Add(i a); res.Add(i c); res.Add(i b)
            edges.Add(struct(a, b)) |> ignore
            edges.Add(struct(b, c)) |> ignore
            edges.Add(struct(c, a)) |> ignore
        // a wall on every boundary edge a -> b (the cap on its left), facing away from the cap
        for struct(a, b) in edges do
            if not (edges.Contains(struct(b, a))) then
                res.Add(i a); res.Add(i b); res.Add(o b)
                res.Add(i a); res.Add(o b); res.Add(o a)
        { cap = outerCap; volume = res.ToArray() }

    /// The volume over a map face (ContactTopology.Face.cycles: world points, face on the left
    /// of each cycle seen from outside). None when the face cannot be triangulated.
    let tryFace (shell : Shell) (cycles : V3d[][]) : Option<FaceVolume> =
        capDirections shell cycles |> Option.map (shellOver shell)

    /// The volume over the whole body: for a map whose contacts close no face yet.
    let wholeBody (shell : Shell) : FaceVolume =
        // octahedron, split until fine enough
        let x, y, z = V3d.IOO, V3d.OIO, V3d.OOI
        let octa =
            [| x, y, z; y, -x, z; -x, -y, z; -y, x, z
               y, x, -z; -x, y, -z; -y, -x, -z; x, -y, -z |]
        let out = List<V3d>()
        let rec split depth (a : V3d) (b : V3d) (c : V3d) =
            if depth > 12 || (angle a b <= shell.maxAngle && angle b c <= shell.maxAngle && angle c a <= shell.maxAngle) then
                out.Add a; out.Add b; out.Add c
            else
                let ab = (a + b).Normalized
                let bc = (b + c).Normalized
                let ca = (c + a).Normalized
                split (depth + 1) a ab ca
                split (depth + 1) ab b bc
                split (depth + 1) ca bc c
                split (depth + 1) ab bc ca
        for (a, b, c) in octa do split 0 a b c
        shellOver shell (out.ToArray())
