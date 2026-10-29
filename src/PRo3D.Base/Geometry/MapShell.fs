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
            /// unit direction from the centre to a point well inside the face - as far from its
            /// boundary as the cap's triangles allow - for writing the unit's code there
            interior : V3d
            /// a few points well inside the face, spread over it (`interior` first): a large
            /// face writes its code at the one facing the camera
            anchors  : V3d[]
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
            // along the great circle (slerp): equal pieces, none longer than maxAngle - a
            // normalised straight-line blend makes the middle pieces longer than the outer ones
            let w = angle s e
            let pts = Array.init (n - 1) (fun i ->
                let t = float (i + 1) / float n
                (s * sin ((1.0 - t) * w) + e * sin (t * w)).Normalized)
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
                // the side of the edge's great circle: valid at any distance (a tangent-plane test
                // at the nearest point degenerates far away - e.g. at the antipode, a pole candidate)
                Vec.Dot(Vec.Cross(a, b), q) < 0.0
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
        // just across the face's own edges, at a few distances: these lie outside even when the
        // face is everything but a thin or non-convex strip that no other candidate hits
        for c in cycles do
            let stride = max 1 (c.Length / 32)
            let mutable i = 0
            while i < c.Length do
                let a = c.[i]
                let b = c.[(i + 1) % c.Length]
                let m = ((a + b) * 0.5).Normalized
                let d = b - a
                let len = d.Length
                if len > 1e-12 then
                    // the face is on the left of a -> b seen from outside; this points right
                    let right = Vec.Cross(d / len, m).Normalized
                    for k in [| 1.0; 4.0; 16.0 |] do add (m + right * (k * len))
                i <- i + stride
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
    let rec private subdivide (chart : Chart) (maxAngle : float) (boundary : HashSet<struct(V3d * V3d)>) (depth : int) (out : List<V3d>)
                              (pa : V2d, a : V3d) (pb : V2d, b : V3d) (pc : V2d, c : V3d) =
        // a boundary edge is never split: the neighbouring face, triangulated in its own chart,
        // would split it elsewhere and the two faces' walls would no longer coincide (boundary
        // edges are already densified to maxAngle, identically from both sides - `between`)
        // An edge is long when its ends are far apart - or when the chart bends it far away: near
        // the projection pole a chart edge between two close points can sweep over most of the
        // body, and only its chart midpoint shows that
        let long (px : V2d, x : V3d) (py : V2d, y : V3d) =
            depth < 16 && not (boundary.Contains(struct(x, y))) &&
            (angle x y > maxAngle ||
             angle (chart.ToSphere((px + py) * 0.5)).Normalized (x + y).Normalized > 0.25 * maxAngle)
        let ab, bc, ca = long (pa, a) (pb, b), long (pb, b) (pc, c), long (pc, c) (pa, a)
        if not (ab || bc || ca) then
            out.Add a; out.Add b; out.Add c
        else
            let mid (p : V2d, _ : V3d) (q : V2d, _ : V3d) =
                let m = (p + q) * 0.5
                m, chart.ToSphere(m).Normalized
            let A = (pa, a)
            let B = (pb, b)
            let C = (pc, c)
            let go = subdivide chart maxAngle boundary (depth + 1) out
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

    /// Points well inside a face: of the cap's triangle centres, the one farthest from the
    /// boundary (a pole of inaccessibility, approximately) first, then - for a face large enough
    /// - up to `maxAnchors - 1` more, each as far from the chosen ones as possible while keeping
    /// at least a third of the best clearance. Both sets are thinned to bound the cost.
    let private anchorsOf (cap : V3d[]) (boundary : V3d[][]) : V3d[] =
        let maxAnchors = 8
        let triCount = cap.Length / 3
        let bs = Array.concat boundary
        let bStride = max 1 (bs.Length / 400)
        let tStride = max 1 (triCount / 300)
        let candidates = List<struct(V3d * float)>()
        let mutable t = 0
        while t < triCount do
            let c = (cap.[3 * t] + cap.[3 * t + 1] + cap.[3 * t + 2]).Normalized
            let mutable d = Double.PositiveInfinity
            let mutable i = 0
            while i < bs.Length do
                let x = Vec.Distance(c, bs.[i])
                if x < d then d <- x
                i <- i + bStride
            candidates.Add(struct(c, d))
            t <- t + tStride
        // the face's area-weighted centre: where a compact face's code belongs. Its triangles may
        // all fan out from the rim (an ellipse), with every triangle centre near the edge
        let centre =
            let mutable s = V3d.Zero
            for t in 0 .. triCount - 1 do
                let a = cap.[3 * t]
                let b = cap.[3 * t + 1]
                let c = cap.[3 * t + 2]
                s <- s + (a + b + c) * Vec.Cross(b - a, c - a).Length
            s
        if centre.Length > 1e-12 then
            let c = centre.Normalized
            if not (isOutside boundary c) then
                let mutable d = Double.PositiveInfinity
                for x in bs do
                    let e = Vec.Distance(c, x)
                    if e < d then d <- e
                candidates.Add(struct(c, d))
        if candidates.Count = 0 then [||]
        else
            let mutable best = 0
            for i in 1 .. candidates.Count - 1 do
                let struct(_, d) = candidates.[i]
                let struct(_, bd) = candidates.[best]
                if d > bd then best <- i
            let struct(first, clearance) = candidates.[best]
            let chosen = List<V3d>()
            chosen.Add first
            let eligible = candidates |> Seq.filter (fun (struct(_, d)) -> d >= 0.35 * clearance) |> Seq.map (fun (struct(c, _)) -> c) |> Seq.toArray
            let mutable fin = false
            while not fin && chosen.Count < maxAnchors do
                // the eligible point farthest from all chosen ones
                let mutable far = V3d.Zero
                let mutable farD = -1.0
                for c in eligible do
                    let mutable d = Double.PositiveInfinity
                    for x in chosen do
                        let e = Vec.Distance(c, x)
                        if e < d then d <- e
                    if d > farD then
                        farD <- d
                        far <- c
                // spread: 1.5 clearances apart, at most ~30 degrees (chord 0.5) - so a large cap
                // gets anchors all over, not just at its single most inaccessible point
                if farD < min (1.5 * clearance) 0.5 then fin <- true
                else chosen.Add far
            chosen.ToArray()

    /// Outward cap triangles (unit directions) of a face, with its densified boundary, or None
    /// when it cannot be triangulated.
    let private capDirections (shell : Shell) (cycles : V3d[][]) : Option<V3d[] * V3d[][]> =
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
                let boundary = HashSet<struct(V3d * V3d)>()
                for c in cycles do
                    for i in 0 .. c.Length - 1 do
                        let a = c.[i]
                        let b = c.[(i + 1) % c.Length]
                        boundary.Add(struct(a, b)) |> ignore
                        boundary.Add(struct(b, a)) |> ignore
                let out = List<V3d>()
                for t in ts do
                    let a = (t.P0, t.A0)
                    let b = (t.P1, t.A1)
                    let c = (t.P2, t.A2)
                    if ccw then subdivide chart shell.maxAngle boundary 0 out a b c
                    else subdivide chart shell.maxAngle boundary 0 out a c b
                Some (out.ToArray(), cycles)

    /// The closed shell volume over cap triangles given as unit directions (outward).
    let private shellOver (shell : Shell) (cap : V3d[]) (anchors : V3d[]) : FaceVolume =
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
        { cap = outerCap; volume = res.ToArray()
          interior = anchors |> Array.tryHead |> Option.defaultValue V3d.OOI
          anchors = anchors }

    /// The whole sphere as outward cap triangles (unit directions): an octahedron, split until
    /// no edge spans more than maxAngle.
    let private wholeCap (shell : Shell) : V3d[] =
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
        out.ToArray()

    /// Six times the volume of the cone from the centre over cap triangles (unit directions):
    /// positive for an outward cap, negative for one that came out inside-out.
    let private signedCap (cap : V3d[]) =
        let mutable s = 0.0
        for t in 0 .. cap.Length / 3 - 1 do
            s <- s + Vec.Dot(cap.[3 * t], Vec.Cross(cap.[3 * t + 1], cap.[3 * t + 2]))
        s

    /// The volume over a map face (ContactTopology.Face.cycles: world points, face on the left
    /// of each cycle seen from outside). None when the face cannot be triangulated.
    ///
    /// A face that is nearly the whole body - everything but a small region, its complement -
    /// cannot be triangulated in a chart: the pole has to lie in the small complement, so the rest
    /// of the body is squeezed around the chart's origin, inside triangles whose corners and edges
    /// all lie at the complement (no edge test can see that, and the triangulation comes out
    /// inside-out). Such a face is the whole shell plus its complement's volume turned inside out:
    /// counted (+1/-1, DeferredFills) the two cancel over the complement, so terrain there is
    /// outside the face and everywhere else inside.
    let tryFace (shell : Shell) (cycles : V3d[][]) : Option<FaceVolume> =
        match capDirections shell cycles with
        | Some (cap, boundary) when signedCap cap > 0.0 ->
            Some (shellOver shell cap (anchorsOf cap boundary))
        | _ ->
            let reversed = cycles |> Array.map Array.rev
            match capDirections shell reversed with
            | Some (complementCap, complementBoundary) when signedCap complementCap > 0.0 ->
                let whole = wholeCap shell
                // anchors: points of the whole sphere that lie in the face, not in its complement
                let faceBoundary = complementBoundary |> Array.map Array.rev
                let inFace =
                    [| for t in 0 .. whole.Length / 3 - 1 do
                         let c = (whole.[3 * t] + whole.[3 * t + 1] + whole.[3 * t + 2]).Normalized
                         if not (isOutside faceBoundary c) then
                             yield whole.[3 * t]; yield whole.[3 * t + 1]; yield whole.[3 * t + 2] |]
                let anchors = anchorsOf inFace faceBoundary
                let all = shellOver shell whole anchors
                let complement = shellOver shell complementCap [||]
                let inverted = Array.zeroCreate complement.volume.Length
                for t in 0 .. complement.volume.Length / 3 - 1 do
                    inverted.[3 * t] <- complement.volume.[3 * t]
                    inverted.[3 * t + 1] <- complement.volume.[3 * t + 2]
                    inverted.[3 * t + 2] <- complement.volume.[3 * t + 1]
                Some { all with volume = Array.append all.volume inverted }
            | _ -> None

    /// The volume over the whole body: for a map whose contacts close no face yet.
    let wholeBody (shell : Shell) : FaceVolume =
        shellOver shell (wholeCap shell) [||]
