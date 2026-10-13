namespace PRo3D.Base.Geometry

open System
open System.Collections.Generic
open Aardvark.Base

/// Units of a geological map from its contacts (#843, docs/dev/GeologicalMap-plan.md).
///
/// Contacts are polylines on a body's surface; they cut the surface into faces - the map's
/// units. Every step is a question local to one place on the surface, answered in 3D or in the
/// tangent plane there, so a map may cover the whole body: no chart, no poles, no date line.
///
///   1. noding      - where two contact segments cross seen from the centre, or come within
///                    `tolerance` of each other, both are split there
///   2. nodes       - split points and samples within `tolerance` of each other become one node
///   3. graph       - consecutive nodes along each contact are edges; dangling pieces (an end
///                    that reaches no other contact) are trimmed and reported as open ends
///   4. order       - at each node, its edges sorted by angle in the tangent plane
///   5. faces       - tracing half-edges, always turning to the next edge clockwise, yields the
///                    boundary cycles, each with its face on the left (seen from outside)
///   6. components  - a separate contact network inside a face (a closed loop = a hole) makes
///                    faces with several boundary cycles
///   7. labels      - each label point lands in the face of its nearest contact, on its side
///
/// Locating (6, 7) works on the radial projection about `center` - exact on a sphere, where the
/// nearest contact point lies on the geodesic to the label - and keeps the topology on any
/// star-shaped body (each ray from the centre meets the surface once), e.g. Dimorphos. Bodies
/// that are not (67P) need a surface walk instead; out of scope here.
///
/// Pure: no rendering, no model. Arrays and mutable locals inside; nothing escapes mutable.
module ContactTopology =

    type Contact =
        {
            id     : Guid
            /// the contact's sampled points on the surface, in order (a closed contact repeats
            /// its first point at the end)
            points : V3d[]
        }

    type Label =
        {
            id       : Guid
            position : V3d
            unit     : string
        }

    [<RequireQualifiedAccess>]
    type FaceUnit =
        | Unlabelled
        | Unit of string
        /// labels of different units in one face - usually a missing contact between them
        | Conflict of string list

    type Face =
        {
            id       : int
            /// boundary cycles; each runs with the face on its left seen from outside the body
            cycles   : V3d[][]
            /// per cycle and edge: the contact the edge came from
            sources  : Guid[][]
            labels   : Guid list
            unit     : FaceUnit
        }

    type Result =
        {
            faces     : Face[]
            /// ends of contacts that reach no other contact (their dangling pieces are trimmed)
            openEnds  : V3d[]
            /// V - E + F = 2 for every connected contact network; false = degenerate input
            eulerOk   : bool
            /// label id -> face id, for labels that could be placed
            labelFace : Map<Guid, int>
        }

    /// The faces of a set of contacts, before any labels: what `build` makes and `label`
    /// reuses, so assigning units never recomputes the faces.
    type Network =
        {
            /// faces without labels (labels = [], unit = Unlabelled)
            faces    : Face[]
            openEnds : V3d[]
            eulerOk  : bool
            /// the face containing a surface point (located on the radial projection)
            faceAt   : V3d -> Option<int>
        }

    // -----------------------------------------------------------------------------------------
    // spatial hashing
    // -----------------------------------------------------------------------------------------

    /// Uniform grid of axis-aligned boxes, for candidate pairs and nearest queries.
    type private Grid(cell : float) =
        let cells = Dictionary<V3l, List<int>>()
        let key (p : V3d) = V3l(int64 (floor (p.X / cell)), int64 (floor (p.Y / cell)), int64 (floor (p.Z / cell)))

        member x.Cell = cell

        member x.Add(id : int, box : Box3d) =
            let lo = key box.Min
            let hi = key box.Max
            for i in lo.X .. hi.X do
                for j in lo.Y .. hi.Y do
                    for k in lo.Z .. hi.Z do
                        let c = V3l(i, j, k)
                        match cells.TryGetValue c with
                        | true, l -> l.Add id
                        | _ ->
                            let l = List<int>()
                            l.Add id
                            cells.[c] <- l

        /// Every id registered in a cell overlapping `box` (may repeat).
        member x.Query(box : Box3d, f : int -> unit) =
            let lo = key box.Min
            let hi = key box.Max
            let span = float (hi.X - lo.X + 1L) * float (hi.Y - lo.Y + 1L) * float (hi.Z - lo.Z + 1L)
            if span > float cells.Count then
                // a box larger than the occupied space (a far search on a sparse map): walk
                // the occupied cells instead of the box's mostly empty ones
                for kv in cells do
                    let c = kv.Key
                    if c.X >= lo.X && c.X <= hi.X && c.Y >= lo.Y && c.Y <= hi.Y && c.Z >= lo.Z && c.Z <= hi.Z then
                        for id in kv.Value do f id
            else
                for i in lo.X .. hi.X do
                    for j in lo.Y .. hi.Y do
                        for k in lo.Z .. hi.Z do
                            match cells.TryGetValue(V3l(i, j, k)) with
                            | true, l -> for id in l do f id
                            | _ -> ()

        member x.IsEmpty = cells.Count = 0

    /// Union-find over 0 .. n-1.
    type private Union(n : int) =
        let parent = Array.init n id
        member x.Find(i : int) =
            let mutable r = i
            while parent.[r] <> r do
                parent.[r] <- parent.[parent.[r]]
                r <- parent.[r]
            r
        member x.Union(a : int, b : int) =
            let ra = x.Find a
            let rb = x.Find b
            if ra <> rb then parent.[max ra rb] <- min ra rb

    // -----------------------------------------------------------------------------------------
    // geometry helpers
    // -----------------------------------------------------------------------------------------

    /// Closest points of segments p1-q1 and p2-q2: parameters s, t in [0, 1] (Ericson, RTCD 5.1.9).
    let private closestSegmentSegment (p1 : V3d) (q1 : V3d) (p2 : V3d) (q2 : V3d) =
        let d1 = q1 - p1
        let d2 = q2 - p2
        let r = p1 - p2
        let a = Vec.Dot(d1, d1)
        let e = Vec.Dot(d2, d2)
        let f = Vec.Dot(d2, r)
        let eps = 1e-24
        if a <= eps && e <= eps then 0.0, 0.0
        elif a <= eps then 0.0, clamp 0.0 1.0 (f / e)
        else
            let c = Vec.Dot(d1, r)
            if e <= eps then clamp 0.0 1.0 (-c / a), 0.0
            else
                let b = Vec.Dot(d1, d2)
                let denom = a * e - b * b
                let mutable s = if denom > eps then clamp 0.0 1.0 ((b * f - c * e) / denom) else 0.0
                let mutable t = (b * s + f) / e
                if t < 0.0 then
                    t <- 0.0
                    s <- clamp 0.0 1.0 (-c / a)
                elif t > 1.0 then
                    t <- 1.0
                    s <- clamp 0.0 1.0 ((b - c) / a)
                s, t

    /// Where segments p1-q1 and p2-q2 cross seen from `center`: parameters s, t of the points
    /// on the same ray from the centre. Draped contacts cross on the terrain, but their chords
    /// pass each other at different heights - the radial view sees the crossing regardless.
    let private radialCrossing (center : V3d) (p1 : V3d) (q1 : V3d) (p2 : V3d) (q2 : V3d) : Option<float * float> =
        let a1, b1, a2, b2 = p1 - center, q1 - center, p2 - center, q2 - center
        // the plane through the centre and each segment; a segment meets the other's plane
        // at a parameter linear in its position
        let n1 = Vec.Cross(a1, b1)
        let n2 = Vec.Cross(a2, b2)
        let d1 = Vec.Dot(b1 - a1, n2)
        let d2 = Vec.Dot(b2 - a2, n1)
        // a transversal crossing only: segments on (nearly) one plane through the centre -
        // consecutive pieces of a great circle - have a parameter that is rounding noise
        let transversal (d : float) (seg : V3d) (n : V3d) = abs d > 1e-6 * seg.Length * n.Length
        if not (transversal d1 (b1 - a1) n2 && transversal d2 (b2 - a2) n1) then None
        else
            let s = -Vec.Dot(a1, n2) / d1
            let t = -Vec.Dot(a2, n1) / d2
            // a crossing through a sample lands on both segments that share it, each with a
            // parameter a rounding error outside [0, 1]
            let eps = 1e-9
            if s < -eps || s > 1.0 + eps || t < -eps || t > 1.0 + eps then None
            else
                let s = clamp 0.0 1.0 s
                let t = clamp 0.0 1.0 t
                // the planes meet in a line through the centre: same side, not antipodal
                let x1 = a1 + (b1 - a1) * s
                let x2 = a2 + (b2 - a2) * t
                if Vec.Dot(x1, x2) > 0.0 then Some (s, t) else None

    /// Parameter of the point of segment a-b closest to p.
    let private closestOnSegment (a : V3d) (b : V3d) (p : V3d) =
        let d = b - a
        let l2 = Vec.Dot(d, d)
        if l2 <= 1e-24 then 0.0 else clamp 0.0 1.0 (Vec.Dot(p - a, d) / l2)

    /// Any unit vector perpendicular to n.
    let private perpendicular (n : V3d) =
        let c = Vec.Cross(n, V3d.OOI)
        let c = if c.LengthSquared < 1e-12 then Vec.Cross(n, V3d.IOO) else c
        c.Normalized

    /// Angle of direction d in the tangent plane with normal n (counter-clockwise seen from +n).
    let private tangentAngle (n : V3d) (d : V3d) =
        let u = perpendicular n
        let v = Vec.Cross(n, u)
        atan2 (Vec.Dot(d, v)) (Vec.Dot(d, u))

    // -----------------------------------------------------------------------------------------
    // the computation
    // -----------------------------------------------------------------------------------------

    /// Faces of the contacts.
    ///
    /// `center` is the body centre (for the tangent planes and for locating points);
    /// `tolerance` the distance below which two contacts count as touching - a fraction of the
    /// sampling spacing: draped polylines of two contacts never hit each other exactly.
    let build (center : V3d) (tolerance : float) (contacts : Contact[]) : Network =

        // ---- 1. segments -----------------------------------------------------------------
        // vertices: every contact sample; segments between consecutive samples
        let vertPos = List<V3d>()
        let segA = List<int>()          // start vertex
        let segB = List<int>()          // end vertex
        let segContact = List<int>()    // contact index
        for ci in 0 .. contacts.Length - 1 do
            let pts = contacts.[ci].points
            let first = vertPos.Count
            for p in pts do vertPos.Add p
            for i in 0 .. pts.Length - 2 do
                if Vec.Distance(pts.[i], pts.[i + 1]) > 1e-12 then
                    segA.Add(first + i)
                    segB.Add(first + i + 1)
                    segContact.Add ci
        let segCount = segA.Count

        let empty = { faces = [||]; openEnds = [||]; eulerOk = true; faceAt = fun _ -> None }
        if segCount = 0 then empty else

        // ---- 2. noding ---------------------------------------------------------------------
        let meanLength =
            let mutable acc = 0.0
            for s in 0 .. segCount - 1 do acc <- acc + Vec.Distance(vertPos.[segA.[s]], vertPos.[segB.[s]])
            acc / float segCount
        let segGrid = Grid(max (meanLength * 2.0) (tolerance * 4.0))
        // enlarged by the segment's own length too: two draped contacts crossing over rough
        // terrain pass each other at different heights, up to the relief between samples
        let segBox s =
            let a, b = vertPos.[segA.[s]], vertPos.[segB.[s]]
            Box3d([| a; b |]).EnlargedBy(tolerance + Vec.Distance(a, b))
        for s in 0 .. segCount - 1 do segGrid.Add(s, segBox s)

        // split points per segment: (parameter, vertex)
        let splits = Array.init segCount (fun _ -> List<struct(float * int)>())
        let seen = HashSet<struct(int * int)>()
        // a crossing at a segment's end joins that sample rather than splitting the segment
        // there - a split would leave the sample behind as a one-edge stub
        let joins = List<struct(int * int)>()
        let attach (seg : int) (param : float) (vi : int) =
            if param <= 1e-9 then joins.Add(struct(vi, segA.[seg]))
            elif param >= 1.0 - 1e-9 then joins.Add(struct(vi, segB.[seg]))
            else splits.[seg].Add(struct(param, vi))
        for s in 0 .. segCount - 1 do
            segGrid.Query(segBox s, fun o ->
                if o > s && seen.Add(struct(s, o)) then
                    // consecutive segments share a sample - not a crossing
                    let sharesVertex =
                        segA.[s] = segA.[o] || segA.[s] = segB.[o] || segB.[s] = segA.[o] || segB.[s] = segB.[o]
                    if not sharesVertex then
                        let p1, q1 = vertPos.[segA.[s]], vertPos.[segB.[s]]
                        let p2, q2 = vertPos.[segA.[o]], vertPos.[segB.[o]]
                        let radial = radialCrossing center p1 q1 p2 q2
                        let u, v =
                            match radial with
                            | Some uv -> uv
                            | None -> closestSegmentSegment p1 q1 p2 q2
                        let c1 = p1 + (q1 - p1) * u
                        let c2 = p2 + (q2 - p2) * v
                        if Option.isSome radial || Vec.Distance(c1, c2) <= tolerance then
                            let vi = vertPos.Count
                            vertPos.Add((c1 + c2) * 0.5)
                            attach s u vi
                            attach o v vi)

        // ---- 3. nodes: everything within tolerance is one node -----------------------------
        let vertCount = vertPos.Count
        let union = Union(vertCount)
        for struct(a, b) in joins do union.Union(a, b)
        let pointGrid = Grid(max tolerance 1e-9)
        for i in 0 .. vertCount - 1 do
            let p = vertPos.[i]
            let box = Box3d(p, p).EnlargedBy(tolerance)
            pointGrid.Query(box, fun j ->
                if Vec.Distance(vertPos.[j], p) <= tolerance then union.Union(i, j))
            pointGrid.Add(i, Box3d(p, p))

        let nodeOf = Array.zeroCreate vertCount
        let nodeSum = List<V3d>()
        let nodeCount = List<int>()
        let rootNode = Dictionary<int, int>()
        for i in 0 .. vertCount - 1 do
            let r = union.Find i
            let n =
                match rootNode.TryGetValue r with
                | true, n -> n
                | _ ->
                    let n = nodeSum.Count
                    rootNode.[r] <- n
                    nodeSum.Add V3d.Zero
                    nodeCount.Add 0
                    n
            nodeOf.[i] <- n
            nodeSum.[n] <- nodeSum.[n] + vertPos.[i]
            nodeCount.[n] <- nodeCount.[n] + 1
        let nodes = Array.init nodeSum.Count (fun n -> nodeSum.[n] / float nodeCount.[n])

        // ---- 4. edges along each contact ---------------------------------------------------
        let edgeIndex = Dictionary<struct(int * int), int>()
        let edgeA = List<int>()
        let edgeB = List<int>()
        let edgeContact = List<int>()
        let addEdge a b c =
            if a <> b then
                let k = struct(min a b, max a b)
                if not (edgeIndex.ContainsKey k) then
                    edgeIndex.[k] <- edgeA.Count
                    edgeA.Add a
                    edgeB.Add b
                    edgeContact.Add c
        for s in 0 .. segCount - 1 do
            let chain = List<int>()
            chain.Add nodeOf.[segA.[s]]
            let sp = splits.[s] |> Seq.sortBy (fun (struct(t, _)) -> t) |> Seq.toArray
            for struct(_, v) in sp do chain.Add nodeOf.[v]
            chain.Add nodeOf.[segB.[s]]
            for i in 0 .. chain.Count - 2 do addEdge chain.[i] chain.[i + 1] segContact.[s]

        // ---- 5. trim dangling pieces --------------------------------------------------------
        let edgeCount = edgeA.Count
        let alive = Array.create edgeCount true
        let incident = Array.init nodes.Length (fun _ -> List<int>())
        for e in 0 .. edgeCount - 1 do
            incident.[edgeA.[e]].Add e
            incident.[edgeB.[e]].Add e
        let degree = Array.init nodes.Length (fun n -> incident.[n].Count)
        let openEnds = List<V3d>()
        let queue = Queue<int>()
        for n in 0 .. nodes.Length - 1 do
            if degree.[n] = 1 then
                openEnds.Add nodes.[n]
                queue.Enqueue n
        while queue.Count > 0 do
            let n = queue.Dequeue()
            if degree.[n] = 1 then
                for e in incident.[n] do
                    if alive.[e] then
                        alive.[e] <- false
                        degree.[edgeA.[e]] <- degree.[edgeA.[e]] - 1
                        degree.[edgeB.[e]] <- degree.[edgeB.[e]] - 1
                        let other = if edgeA.[e] = n then edgeB.[e] else edgeA.[e]
                        if degree.[other] = 1 then queue.Enqueue other

        // ---- 6. half-edges, angular order, faces --------------------------------------------
        // half-edge 2e runs a -> b, 2e+1 runs b -> a
        let heFrom h = if h % 2 = 0 then edgeA.[h / 2] else edgeB.[h / 2]
        let heTo h = if h % 2 = 0 then edgeB.[h / 2] else edgeA.[h / 2]
        let twin h = h ^^^ 1
        let normalAt (p : V3d) = (p - center).Normalized

        let outgoing = Array.init nodes.Length (fun _ -> List<int>())
        for e in 0 .. edgeCount - 1 do
            if alive.[e] then
                outgoing.[edgeA.[e]].Add(2 * e)
                outgoing.[edgeB.[e]].Add(2 * e + 1)
        let sorted = Array.zeroCreate<int[]> nodes.Length
        let slot = Array.create (2 * edgeCount) 0
        for n in 0 .. nodes.Length - 1 do
            let nrm = normalAt nodes.[n]
            let arr =
                outgoing.[n].ToArray()
                |> Array.sortBy (fun h -> tangentAngle nrm (nodes.[heTo h] - nodes.[n]))
            sorted.[n] <- arr
            for i in 0 .. arr.Length - 1 do slot.[arr.[i]] <- i

        // next(h): at h's end, the edge just clockwise of the way back - the face stays left
        let next h =
            let t = twin h
            let at = sorted.[heFrom t]
            at.[(slot.[t] - 1 + at.Length) % at.Length]

        let cycleOf = Array.create (2 * edgeCount) -1
        let cycles = List<int[]>()
        for e in 0 .. edgeCount - 1 do
            if alive.[e] then
                for h in [| 2 * e; 2 * e + 1 |] do
                    if cycleOf.[h] < 0 then
                        let c = cycles.Count
                        let cyc = List<int>()
                        let mutable cur = h
                        let mutable guard = 0
                        while cycleOf.[cur] < 0 && guard <= 2 * edgeCount do
                            cycleOf.[cur] <- c
                            cyc.Add cur
                            cur <- next cur
                            guard <- guard + 1
                        cycles.Add(cyc.ToArray())

        // ---- 7. connected networks (components) ---------------------------------------------
        let compUnion = Union(nodes.Length)
        for e in 0 .. edgeCount - 1 do
            if alive.[e] then compUnion.Union(edgeA.[e], edgeB.[e])
        let compOfNode = Dictionary<int, int>()
        let compRoots = List<int>()
        for e in 0 .. edgeCount - 1 do
            if alive.[e] then
                let r = compUnion.Find edgeA.[e]
                if not (compOfNode.ContainsKey r) then
                    compOfNode.[r] <- compRoots.Count
                    compRoots.Add r
        let compOf (n : int) = compOfNode.[compUnion.Find n]
        let compCount = compRoots.Count
        let cycleComp = cycles |> Seq.map (fun c -> compOf (heFrom c.[0])) |> Seq.toArray

        // Euler per network: V - E + F = 2
        let eulerOk =
            let v = Array.zeroCreate compCount
            let ed = Array.zeroCreate compCount
            let f = Array.zeroCreate compCount
            let counted = HashSet<int>()
            for e in 0 .. edgeCount - 1 do
                if alive.[e] then
                    let c = compOf edgeA.[e]
                    ed.[c] <- ed.[c] + 1
                    for n in [| edgeA.[e]; edgeB.[e] |] do
                        if counted.Add n then v.[c] <- v.[c] + 1
            for c in cycleComp do f.[c] <- f.[c] + 1
            Array.forall id (Array.init compCount (fun c -> v.[c] - ed.[c] + f.[c] = 2))

        // ---- 8. locating on the radial projection --------------------------------------------
        let unit (p : V3d) = (p - center).Normalized
        let nodeDir = nodes |> Array.map unit
        let aliveEdges = [| for e in 0 .. edgeCount - 1 do if alive.[e] then e |]
        let dirGrid =
            let mean =
                if aliveEdges.Length = 0 then 1.0
                else (aliveEdges |> Array.sumBy (fun e -> Vec.Distance(nodeDir.[edgeA.[e]], nodeDir.[edgeB.[e]]))) / float aliveEdges.Length
            Grid(max (mean * 2.0) 1e-6)
        for e in aliveEdges do dirGrid.Add(e, Box3d([| nodeDir.[edgeA.[e]]; nodeDir.[edgeB.[e]] |]))

        /// The half-edge whose face contains direction q, searching only edges of networks
        /// accepted by `inComp`. None when there is no such edge.
        let locate (inComp : int -> bool) (q : V3d) : Option<int> =
            if dirGrid.IsEmpty then None else
            let mutable best = -1
            let mutable bestD = Double.PositiveInfinity
            let mutable bestT = 0.0
            let consider e =
                if inComp (compOf edgeA.[e]) then
                    let a = nodeDir.[edgeA.[e]]
                    let b = nodeDir.[edgeB.[e]]
                    let t = closestOnSegment a b q
                    let d = Vec.Distance(a + (b - a) * t, q)
                    if d < bestD then
                        best <- e
                        bestD <- d
                        bestT <- t
            // expanding search: a found edge is final once the searched radius exceeds it
            let mutable r = dirGrid.Cell
            let mutable fin = false
            while not fin do
                dirGrid.Query(Box3d(q, q).EnlargedBy r, consider)
                if (best >= 0 && bestD <= r) || r > 4.0 then fin <- true
                else r <- r * 2.0
            if best < 0 then None else
            let a = nodeDir.[edgeA.[best]]
            let b = nodeDir.[edgeB.[best]]
            let len = Vec.Distance(a, b)
            let nearEnd = bestT * len < 1e-9 || (1.0 - bestT) * len < 1e-9
            if not nearEnd then
                // interior of an edge: which side of a -> b, in the tangent plane there
                let p = a + (b - a) * bestT
                let side = Vec.Dot(Vec.Cross(b - a, q - p), p.Normalized)
                Some (if side >= 0.0 then 2 * best else 2 * best + 1)
            else
                // at a node: the sector between two consecutive edges, counter-clockwise
                let n = if bestT < 0.5 then edgeA.[best] else edgeB.[best]
                let nrm = nodeDir.[n]
                let aq = tangentAngle nrm (q - nodeDir.[n])
                // the node's edges by their angle in this same projection
                let around =
                    sorted.[n]
                    |> Array.map (fun h -> h, tangentAngle nrm (nodeDir.[heTo h] - nodeDir.[n]))
                    |> Array.sortBy snd
                // the face left of the last edge at or before q's angle, wrapping round
                let mutable pick = fst around.[around.Length - 1]
                for (h, a) in around do
                    if a <= aq then pick <- h
                Some pick

        // ---- 9. cycles -> faces ---------------------------------------------------------------
        // facesOf.[X].[Y] = the cycle of network X whose face contains network Y
        let faceUnion = Union(cycles.Count)
        if compCount > 1 then
            let probe = Array.zeroCreate compCount
            for e in aliveEdges do probe.[compOf edgeA.[e]] <- nodeDir.[edgeA.[e]]
            let contains = Array2D.create compCount compCount -1
            for x in 0 .. compCount - 1 do
                for y in 0 .. compCount - 1 do
                    if x <> y then
                        match locate (fun c -> c = x) probe.[y] with
                        | Some h -> contains.[x, y] <- cycleOf.[h]
                        | None -> ()
            // cycle c of X and cycle d of Y bound the same face when each holds the other
            // network and no third network separates them
            for x in 0 .. compCount - 1 do
                for y in x + 1 .. compCount - 1 do
                    let c = contains.[x, y]
                    let d = contains.[y, x]
                    if c >= 0 && d >= 0 then
                        let separated =
                            Seq.exists (fun z -> z <> x && z <> y && contains.[z, x] <> contains.[z, y]) (seq { 0 .. compCount - 1 })
                        if not separated then faceUnion.Union(c, d)

        let faceOfCycle = Dictionary<int, int>()
        let faceCycles = List<List<int>>()
        for c in 0 .. cycles.Count - 1 do
            let r = faceUnion.Find c
            match faceOfCycle.TryGetValue r with
            | true, f -> faceCycles.[f].Add c
            | _ ->
                faceOfCycle.[r] <- faceCycles.Count
                let l = List<int>()
                l.Add c
                faceCycles.Add l
        let faceOf (cycle : int) = faceOfCycle.[faceUnion.Find cycle]

        let faces =
            Array.init faceCycles.Count (fun f ->
                let cs = faceCycles.[f].ToArray()
                {
                    id      = f
                    cycles  = cs |> Array.map (fun c -> cycles.[c] |> Array.map (fun h -> nodes.[heFrom h]))
                    sources = cs |> Array.map (fun c -> cycles.[c] |> Array.map (fun h -> contacts.[edgeContact.[h / 2]].id))
                    labels  = []
                    unit    = FaceUnit.Unlabelled
                })

        let faceAt (p : V3d) =
            if faceCycles.Count = 0 then None
            else locate (fun _ -> true) (unit p) |> Option.map (fun h -> faceOf cycleOf.[h])

        { faces = faces; openEnds = openEnds.ToArray(); eulerOk = eulerOk; faceAt = faceAt }

    /// The units of a network's faces from label points.
    let label (network : Network) (labels : Label[]) : Result =
        let faceLabels = Array.init network.faces.Length (fun _ -> List<Label>())
        let mutable labelFace = Map.empty
        for l in labels do
            match network.faceAt l.position with
            | Some f when f >= 0 && f < faceLabels.Length ->
                faceLabels.[f].Add l
                labelFace <- Map.add l.id f labelFace
            | _ -> ()
        let faces =
            network.faces |> Array.map (fun face ->
                let ls = faceLabels.[face.id]
                let units = ls |> Seq.map (fun l -> l.unit) |> Seq.distinct |> Seq.toList
                { face with
                    labels = ls |> Seq.map (fun l -> l.id) |> Seq.toList
                    unit   =
                        match units with
                        | []  -> FaceUnit.Unlabelled
                        | [u] -> FaceUnit.Unit u
                        | us  -> FaceUnit.Conflict us })
        { faces = faces; openEnds = network.openEnds; eulerOk = network.eulerOk; labelFace = labelFace }

    /// Faces of the contacts, with the units of the labels in them: build, then label.
    let compute (center : V3d) (tolerance : float) (contacts : Contact[]) (labels : Label[]) : Result =
        label (build center tolerance contacts) labels
