namespace PRo3D.Core.Surface

open System
open System.IO
open System.Collections.Generic
open System.Threading

open Aardvark.Base
open Aardvark.Data.Opc
open Aardvark.SceneGraph.Opc

open FSharp.Data.Adaptive

open OpcViewer.Base
open OpcViewer.Base.KdTrees
open Aardvark.VRVis.Opc.KdTrees

open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Core

/// An ellipse on a surface, in world space.
type SurfaceEllipse =
    {
        center    : V3d
        /// semi-major axis vector, metres
        semiMajor : V3d
        /// semi-minor axis vector, metres; its component along `semiMajor` is ignored
        semiMinor : V3d
    }

/// Area-weighted statistics of one channel of a layer.
type ChannelStatistics =
    {
        mean : float
        std  : float
        min  : float
        max  : float
    }

type LayerStatistics =
    {
        name     : string
        /// surface area (m²) over which the layer has a value. Smaller than
        /// `EllipseStatistics.surfaceArea` where the layer has holes (NaN vertices, skirt).
        area     : float
        channels : ChannelStatistics[]
    }

/// What the surface inside an ellipse looks like, integrated over the mesh.
type EllipseStatistics =
    {
        /// mesh area inside the ellipse, m². The true surface area, so a boulder's
        /// flanks count by their real size, not by the ground they cover.
        surfaceArea   : float
        /// π·a·b, m²
        footprintArea : float
        /// distinct mesh vertices inside the ellipse - the number of measurements the
        /// statistics rest on. Interpolating between them adds no information.
        vertexCount   : int
        layers        : list<LayerStatistics>
    }

/// One OPC patch, placed in the world, as `EllipseStatistics.computeOnPatches` reads it.
type EllipsePatch =
    {
        /// the KdTree box of the patch, in OPC space
        box          : Box3d
        /// OPC space to world space (`SurfaceIntersection.surfaceTrafo`)
        surfaceTrafo : Trafo3d
        kd           : LazyKdTree
        /// looked up only for patches an ellipse touches
        patchInfo    : unit -> Option<PatchFileInfo>
    }

/// Integrates per-vertex attribute layers over the part of a mesh inside an ellipse.
///
/// The per-vertex values and the mesh define a function over the surface that is linear
/// inside each triangle - the one the renderer shows. Every statistic is taken of that
/// function, weighted by surface area, so small or large triangles, steep or flat ones,
/// contribute by the area they cover:
///
///   * mean: the integral of a linear function over a polygon is its area times the value
///     at its centroid;
///   * std: the integral of its square is exact with the edge-midpoint rule;
///   * min / max: the extremes of a linear function over a polygon lie on its corners.
///
/// "Inside" means inside the elliptic cylinder through the ellipse along its plane
/// normal, within `depth` metres of the plane. Triangles crossing the rim are clipped.
/// Clipping happens in the ellipse plane, where it is exact; the clipped part's surface
/// area follows from the triangle's because projection onto a plane scales every part
/// of a triangle's area by the same factor.
module EllipseIntegration =

    /// Sides of the polygon the rim is clipped with. Its circumradius is chosen so the
    /// polygon has the ellipse's area; its outline then stays within 0.02% of the rim.
    [<Literal>]
    let ClipSides = 128

    /// Circumradius of the clip polygon, in units of the semi-axes.
    let ClipRadius =
        let n = float ClipSides
        sqrt (Constant.Pi / (n / 2.0 * sin (Constant.PiTimesTwo / n)))

    /// Inradius: a point closer to the centre than this is inside every clip edge.
    let private clipInradius = ClipRadius * cos (Constant.Pi / float ClipSides)

    /// Outward normals of the clip polygon's edges.
    let private clipNormals =
        Array.init ClipSides (fun k ->
            let angle = Constant.PiTimesTwo * (float k + 0.5) / float ClipSides
            V2d(cos angle, sin angle)
        )

    /// An ellipse as a coordinate frame: `toLocal` maps a point to (u, v, w), where (u, v)
    /// are in units of the semi-axes - the ellipse is the unit circle - and w is the signed
    /// distance from the ellipse plane in metres.
    type Frame =
        {
            center   : V3d
            majorDir : V3d
            minorDir : V3d
            normal   : V3d
            a        : float
            b        : float
            depth    : float
        }

        member x.ToLocal (p : V3d) =
            let d = p - x.center
            V3d(Vec.dot d x.majorDir / x.a, Vec.dot d x.minorDir / x.b, Vec.dot d x.normal)

        member x.FootprintArea = Constant.Pi * x.a * x.b

        /// World-space box holding everything the frame can accept.
        member x.Bounds =
            let abs' (v : V3d) = V3d(abs v.X, abs v.Y, abs v.Z)
            let extent = abs' x.majorDir * (x.a * ClipRadius) + abs' x.minorDir * (x.b * ClipRadius) + abs' x.normal * x.depth
            Box3d(x.center - extent, x.center + extent)

    /// None for an ellipse without area.
    let tryFrame (depth : float) (ellipse : SurfaceEllipse) =
        let a = ellipse.semiMajor.Length
        if not (a > 1e-9) then None
        else
            let majorDir = ellipse.semiMajor / a
            let minor = ellipse.semiMinor - majorDir * Vec.dot ellipse.semiMinor majorDir
            let b = minor.Length
            if not (b > 1e-9) || not (depth > 0.0) then None
            else
                let minorDir = minor / b
                Some {
                    center   = ellipse.center
                    majorDir = majorDir
                    minorDir = minorDir
                    normal   = Vec.cross majorDir minorDir
                    a        = a
                    b        = b
                    depth    = depth
                }

    /// Running integrals of one layer.
    type private LayerSums(components : int) =
        member val Area = 0.0 with get, set
        member val Shift : float[] = Array.zeroCreate components
        member val HasShift = false with get, set
        member val Sum : float[] = Array.zeroCreate components
        member val SumSq : float[] = Array.zeroCreate components
        member val Min : float[] = Array.create components Double.PositiveInfinity
        member val Max : float[] = Array.create components Double.NegativeInfinity
        member x.Components = components

    /// Corner values of one layer at one triangle: three corners, channels interleaved.
    type CornerValues(name : string, components : int) =
        member x.Name = name
        member x.Components = components
        member val Values : float[] = Array.zeroCreate (3 * components)
        /// false when a corner carries no value; the triangle then counts for no statistic
        /// of this layer
        member val Valid = false with get, set

    /// Accumulates the triangles of one ellipse.
    type Accumulator(frame : Frame) =
        let layers = Dictionary<string, LayerSums>()
        let clipped = List<V2d>(16)
        let clippedWeights = List<V3d>(16)
        let scratch = List<V2d>(16)
        let scratchWeights = List<V3d>(16)
        let mutable surfaceArea = 0.0

        let sumsOf (name : string) (components : int) =
            match layers.TryGetValue name with
            | true, s when s.Components = components -> Some s
            | true, _ ->
                Log.warn "[EllipseStatistics] %s changes its channel count between patches - skipping those" name
                None
            | _ ->
                let s = LayerSums(components)
                layers.[name] <- s
                Some s

        /// Adds a piece of the triangle - itself a triangle, given by the barycentric
        /// coordinates of its corners in the mesh triangle - with surface area `area`.
        let addPiece (area : float) (la : V3d) (lb : V3d) (lc : V3d) (values : CornerValues[]) =
            surfaceArea <- surfaceArea + area
            for layer in values do
                if layer.Valid then
                    match sumsOf layer.Name layer.Components with
                    | None -> ()
                    | Some s ->
                        let n = layer.Components
                        let v = layer.Values
                        s.Area <- s.Area + area
                        if not s.HasShift then
                            // values are accumulated relative to the first one seen, which
                            // keeps sum-of-squares minus squared-mean well conditioned for
                            // layers with a large offset (a radius, a potential)
                            for c in 0 .. n - 1 do s.Shift.[c] <- v.[c]
                            s.HasShift <- true
                        for c in 0 .. n - 1 do
                            let at (l : V3d) = l.X * v.[c] + l.Y * v.[n + c] + l.Z * v.[2 * n + c]
                            let fa = at la
                            let fb = at lb
                            let fc = at lc
                            let shift = s.Shift.[c]
                            let ga = fa - shift
                            let gb = fb - shift
                            let gc = fc - shift
                            s.Sum.[c] <- s.Sum.[c] + area * (ga + gb + gc) / 3.0
                            let mab = 0.5 * (ga + gb)
                            let mbc = 0.5 * (gb + gc)
                            let mca = 0.5 * (gc + ga)
                            s.SumSq.[c] <- s.SumSq.[c] + area * (mab * mab + mbc * mbc + mca * mca) / 3.0
                            s.Min.[c] <- min s.Min.[c] (min fa (min fb fc))
                            s.Max.[c] <- max s.Max.[c] (max fa (max fb fc))

        /// Sutherland-Hodgman against the clip polygon, carrying barycentric coordinates.
        let clip (p0 : V2d) (p1 : V2d) (p2 : V2d) =
            clipped.Clear(); clippedWeights.Clear()
            clipped.Add p0; clipped.Add p1; clipped.Add p2
            clippedWeights.Add V3d.IOO; clippedWeights.Add V3d.OIO; clippedWeights.Add V3d.OOI
            let mutable k = 0
            while k < ClipSides && clipped.Count >= 3 do
                let normal = clipNormals.[k]
                let count = clipped.Count
                // most edges of the 128-gon miss a small piece entirely; leave it as is
                let mutable crosses = false
                let mutable i = 0
                while not crosses && i < count do
                    if Vec.dot clipped.[i] normal - clipInradius > 0.0 then crosses <- true
                    i <- i + 1
                if crosses then
                    scratch.Clear(); scratchWeights.Clear()
                    for i in 0 .. count - 1 do
                        let j = (i + 1) % count
                        let di = Vec.dot clipped.[i] normal - clipInradius
                        let dj = Vec.dot clipped.[j] normal - clipInradius
                        if di <= 0.0 then
                            scratch.Add clipped.[i]; scratchWeights.Add clippedWeights.[i]
                        if (di <= 0.0) <> (dj <= 0.0) then
                            let t = di / (di - dj)
                            scratch.Add (clipped.[i] + (clipped.[j] - clipped.[i]) * t)
                            scratchWeights.Add (clippedWeights.[i] + (clippedWeights.[j] - clippedWeights.[i]) * t)
                    clipped.Clear(); clippedWeights.Clear()
                    clipped.AddRange scratch; clippedWeights.AddRange scratchWeights
                k <- k + 1

        member x.Frame = frame

        /// Integrates one mesh triangle. `w*` are the corners in world space (for the
        /// area), `l*` the same corners in the frame's local coordinates.
        member x.AddTriangle (w0 : V3d, w1 : V3d, w2 : V3d, l0 : V3d, l1 : V3d, l2 : V3d, values : CornerValues[]) =
            let depthOk = abs ((l0.Z + l1.Z + l2.Z) / 3.0) <= frame.depth
            let outside =
                (l0.X > ClipRadius && l1.X > ClipRadius && l2.X > ClipRadius) ||
                (l0.X < -ClipRadius && l1.X < -ClipRadius && l2.X < -ClipRadius) ||
                (l0.Y > ClipRadius && l1.Y > ClipRadius && l2.Y > ClipRadius) ||
                (l0.Y < -ClipRadius && l1.Y < -ClipRadius && l2.Y < -ClipRadius)

            if depthOk && not outside then
                let area3d = 0.5 * (Vec.cross (w1 - w0) (w2 - w0)).Length
                let p0 = l0.XY
                let p1 = l1.XY
                let p2 = l2.XY
                let area2d = 0.5 * abs ((p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X))
                let inner (p : V2d) = p.LengthSquared <= clipInradius * clipInradius

                if area3d > 0.0 then
                    if inner p0 && inner p1 && inner p2 then
                        addPiece area3d V3d.IOO V3d.OIO V3d.OOI values
                    elif area2d * frame.a * frame.b < 1e-9 * area3d then
                        // seen edge-on from the plane: no share of the footprint to clip by,
                        // so the triangle is in or out as a whole
                        let centroid = (p0 + p1 + p2) / 3.0
                        if centroid.Length <= 1.0 then
                            addPiece area3d V3d.IOO V3d.OIO V3d.OOI values
                    else
                        clip p0 p1 p2
                        let n = clipped.Count
                        for i in 1 .. n - 2 do
                            let q0 = clipped.[0]
                            let qi = clipped.[i]
                            let qj = clipped.[i + 1]
                            let sub = 0.5 * abs ((qi.X - q0.X) * (qj.Y - q0.Y) - (qi.Y - q0.Y) * (qj.X - q0.X))
                            if sub > 0.0 then
                                addPiece (area3d * sub / area2d) clippedWeights.[0] clippedWeights.[i] clippedWeights.[i + 1] values

        member x.Result (vertexCount : int) =
            let layerStatistics =
                layers
                |> Seq.map (fun (KeyValue(name, s)) ->
                    let channels =
                        Array.init s.Components (fun c ->
                            if s.Area > 0.0 then
                                let mean = s.Sum.[c] / s.Area
                                let variance = max 0.0 (s.SumSq.[c] / s.Area - mean * mean)
                                { mean = mean + s.Shift.[c]; std = sqrt variance; min = s.Min.[c]; max = s.Max.[c] }
                            else
                                { mean = nan; std = nan; min = nan; max = nan }
                        )
                    { name = name; area = s.Area; channels = channels }
                )
                |> Seq.sortBy (fun l -> l.name)
                |> Seq.toList

            {
                surfaceArea   = surfaceArea
                footprintArea = frame.FootprintArea
                vertexCount   = vertexCount
                layers        = layerStatistics
            }

/// Statistics of the loaded OPC surfaces inside ellipses. Reads the patch geometry and
/// the per-vertex `*.aara` layers directly; textures are never decoded.
module EllipseStatistics =

    /// Half-thickness of the slab around the ellipse plane that counts as inside, when the
    /// caller has nothing better: the semi-major axis. That takes in a boulder as high as
    /// it is wide and terrain that drops away as steeply, but not the far side of a body
    /// many times the ellipse's size.
    let defaultDepth (ellipse : SurfaceEllipse) = ellipse.semiMajor.Length

    /// Side length, in quads, of the blocks a patch is cut into so an ellipse only
    /// visits the part of the grid it can reach.
    [<Literal>]
    let private BlockSize = 32

    /// Vertices are told apart across patches by their position to the millimetre, so a
    /// vertex on a border two patches share counts once.
    let private vertexKey (p : V3d) =
        struct (int64 (Math.Round(p.X * 1000.0)), int64 (Math.Round(p.Y * 1000.0)), int64 (Math.Round(p.Z * 1000.0)))

    /// Integrates one patch into the accumulators of the ellipses whose bounds it touches.
    let private integratePatch
        (wanted       : string -> bool)
        (patch        : EllipsePatch)
        (accumulators : (EllipseIntegration.Accumulator * System.Collections.Generic.HashSet<struct (int64 * int64 * int64)>)[]) =

        let kd = patch.kd
        let positions = kd.objectSetPath |> Aara.fromFile<V3f>
        let size = positions.Size.XY.ToV2i()
        let w = size.X
        let h = size.Y
        let toWorld = patch.surfaceTrafo.Forward * kd.affine.Forward

        let world =
            positions.Data |> Array.map (fun p ->
                if p.AnyNaN then V3d.NaN else toWorld.TransformPos(p.ToV3d()))

        // The patch is cut once into blocks of quads with world-space bounds, so each
        // ellipse visits only the blocks it can reach instead of the whole grid. A
        // catalog of thousands of small ellipses on a few large patches otherwise
        // transforms every vertex once per ellipse.
        let quadsX = w - 1
        let quadsY = h - 1
        let blocksX = (quadsX + BlockSize - 1) / BlockSize
        let blocksY = (quadsY + BlockSize - 1) / BlockSize
        let blockBounds = Array.create (max 0 (blocksX * blocksY)) Box3d.Invalid
        for by in 0 .. blocksY - 1 do
            for bx in 0 .. blocksX - 1 do
                let mutable box = Box3d.Invalid
                // the vertices of the block's quads, including its far edge
                for y in by * BlockSize .. min ((by + 1) * BlockSize) quadsY do
                    for x in bx * BlockSize .. min ((bx + 1) * BlockSize) quadsX do
                        let p = world.[y * w + x]
                        if not p.AnyNaN then box <- box.ExtendedBy p
                blockBounds.[by * blocksX + bx] <- box

        // candidate quads per ellipse, and the rows each ellipse spans. Ellipses are
        // independent - each writes only its own accumulator, set and lists - so they
        // run in parallel; the patch data they share is only read.
        let candidates = Array.init accumulators.Length (fun _ -> List<int>())
        let firstRows = Array.create accumulators.Length Int32.MaxValue
        let lastRows = Array.create accumulators.Length -1
        // a quad whose corners all lie beyond one side of the clip polygon's box
        let r = EllipseIntegration.ClipRadius

        Tasks.Parallel.For(0, accumulators.Length, fun e ->
            let acc, vertices = accumulators.[e]
            let frame = acc.Frame
            let reach = frame.Bounds
            let quads = candidates.[e]
            for by in 0 .. blocksY - 1 do
                for bx in 0 .. blocksX - 1 do
                    let box = blockBounds.[by * blocksX + bx]
                    if not box.IsInvalid && box.Intersects reach then
                        let y0 = by * BlockSize
                        let x0 = bx * BlockSize
                        let y1 = min ((by + 1) * BlockSize) quadsY
                        let x1 = min ((bx + 1) * BlockSize) quadsX

                        // vertices of the block; a vertex on a block edge is seen by both
                        // blocks, which the set absorbs
                        for y in y0 .. y1 do
                            for x in x0 .. x1 do
                                let p = world.[y * w + x]
                                if not p.AnyNaN then
                                    let l = frame.ToLocal p
                                    if l.X * l.X + l.Y * l.Y <= 1.0 && abs l.Z <= frame.depth then
                                        vertices.Add (vertexKey p) |> ignore

                        for y in y0 .. y1 - 1 do
                            for x in x0 .. x1 - 1 do
                                let a = y * w + x
                                let b = a + w
                                let pa = world.[a]
                                let pb = world.[b]
                                let pc = world.[a + 1]
                                let pd = world.[b + 1]
                                if not (pa.AnyNaN || pb.AnyNaN || pc.AnyNaN || pd.AnyNaN) then
                                    let la = frame.ToLocal pa
                                    let lb = frame.ToLocal pb
                                    let lc = frame.ToLocal pc
                                    let ld = frame.ToLocal pd
                                    let outside =
                                        (la.X > r && lb.X > r && lc.X > r && ld.X > r) ||
                                        (la.X < -r && lb.X < -r && lc.X < -r && ld.X < -r) ||
                                        (la.Y > r && lb.Y > r && lc.Y > r && ld.Y > r) ||
                                        (la.Y < -r && lb.Y < -r && lc.Y < -r && ld.Y < -r) ||
                                        (la.Z > frame.depth && lb.Z > frame.depth && lc.Z > frame.depth && ld.Z > frame.depth) ||
                                        (la.Z < -frame.depth && lb.Z < -frame.depth && lc.Z < -frame.depth && ld.Z < -frame.depth)
                                    if not outside then
                                        quads.Add a
                                        firstRows.[e] <- min firstRows.[e] y
                                        lastRows.[e] <- max lastRows.[e] (y + 1)
        ) |> ignore

        let firstRow = firstRows |> Array.fold min Int32.MaxValue
        let lastRow = lastRows |> Array.fold max -1

        if lastRow >= 0 then
            let rows =
                match patch.patchInfo () with
                | None ->
                    Log.warn "[EllipseStatistics] no patch info for %s - geometry only" kd.objectSetPath
                    [||]
                | Some patchInfo ->
                    VertexAttributes.getLayers (Path.GetDirectoryName kd.objectSetPath) patchInfo
                    |> Array.filter (fun l -> wanted l.name)
                    |> Array.choose (fun l -> VertexAttributes.tryReadRows l size firstRow lastRow)

            // the corner values of one triangle, per layer; scratch space, one set per ellipse
            let fill (values : EllipseIntegration.CornerValues[]) (corner : float[][]) (i0 : int) (i1 : int) (i2 : int) =
                for k in 0 .. rows.Length - 1 do
                    let r = rows.[k]
                    let v = values.[k]
                    let c = corner.[k]
                    let n = r.components
                    let read (slot : int) (index : int) =
                        if r.TryGet(index, c) then
                            Array.blit c 0 v.Values (slot * n) n
                            true
                        else false
                    v.Valid <- read 0 i0 && read 1 i1 && read 2 i2

            Tasks.Parallel.For(0, accumulators.Length, fun e ->
                let acc, _ = accumulators.[e]
                let frame = acc.Frame
                let values = rows |> Array.map (fun r -> EllipseIntegration.CornerValues(r.name, r.components))
                let corner = rows |> Array.map (fun r -> Array.zeroCreate<float> r.components)
                let fill = fill values corner
                for a in candidates.[e] do
                    let b = a + w
                    let c = a + 1
                    let d = b + 1
                    let la = frame.ToLocal world.[a]
                    let lb = frame.ToLocal world.[b]
                    let lc = frame.ToLocal world.[c]
                    let ld = frame.ToLocal world.[d]
                    // the two triangles of a quad, wound as TriangleSet.computeGridIndices does
                    fill a b c
                    acc.AddTriangle(world.[a], world.[b], world.[c], la, lb, lc, values)
                    fill c b d
                    acc.AddTriangle(world.[c], world.[b], world.[d], lc, lb, ld, values)
            ) |> ignore

    /// Integrates the given patches over each ellipse. The result has one entry per
    /// ellipse, None for an ellipse without area; an ellipse off every patch gets
    /// `surfaceArea = 0` and no layers. See `compute` for the parameters.
    let computeOnPatches
        (wanted   : string -> bool)
        (depth    : SurfaceEllipse -> float)
        (ellipses : SurfaceEllipse[])
        (patches  : seq<EllipsePatch>)
        : Option<EllipseStatistics>[] =

        let accumulators =
            ellipses |> Array.map (fun e ->
                EllipseIntegration.tryFrame (depth e) e
                |> Option.map (fun frame ->
                    EllipseIntegration.Accumulator(frame), System.Collections.Generic.HashSet<struct (int64 * int64 * int64)>()
                )
            )

        let bounded =
            accumulators |> Array.choose (Option.map (fun (acc, vertices) -> acc.Frame.Bounds, (acc, vertices)))

        for patch in patches do
            let worldBox = patch.box.Transformed(patch.surfaceTrafo.Forward)
            let touched = bounded |> Array.choose (fun (b, acc) -> if worldBox.Intersects b then Some acc else None)
            if touched.Length > 0 then
                try integratePatch wanted patch touched
                with e -> Log.warn "[EllipseStatistics] %s: %s" patch.kd.objectSetPath e.Message

        accumulators |> Array.map (Option.map (fun (acc, vertices) -> acc.Result vertices.Count))

    /// Integrates the surfaces that pass `filterSurface` over each ellipse.
    ///
    /// `wanted` picks the layers by name; `depth` gives each ellipse the half-thickness of
    /// the slab around its plane that counts as inside (`defaultDepth` if in doubt). The
    /// result has one entry per ellipse, None for an ellipse without area. An ellipse
    /// that lies off every surface gets `surfaceArea = 0` and no layers.
    ///
    /// Where several surfaces overlap inside an ellipse, all of them are integrated;
    /// pick the one the ellipse was drawn on with `filterSurface` to avoid that.
    let compute
        (surfacesModel  : SurfaceModel)
        (refSys         : ReferenceSystem)
        (observedSystem : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (filterSurface  : Guid -> Leaf -> SgSurface -> bool)
        (wanted         : string -> bool)
        (depth          : SurfaceEllipse -> float)
        (ellipses       : SurfaceEllipse[])
        : Option<EllipseStatistics>[] =

        let patches = List<EllipsePatch>()
        for (id, leaf) in surfacesModel.surfaces.flat do
            match surfacesModel.sgSurfaces |> HashMap.tryFind id with
            | Some sgSurface when filterSurface id leaf sgSurface ->
                let surface = Leaf.toSurface leaf
                let trafo = SurfaceIntersection.surfaceTrafo surface refSys (observedSystem surface.guid) observerSystem
                match sgSurface.picking with
                | Picking.KdTree kdMap ->
                    let mutable skipped = 0
                    for (box, level0) in kdMap do
                        match level0 with
                        | Level0KdTree.LazyKdTree kd when not (String.IsNullOrEmpty kd.objectSetPath) ->
                            patches.Add {
                                box          = box
                                surfaceTrafo = trafo
                                kd           = kd
                                patchInfo    = fun () ->
                                    ProfileAttributeExtraction.tryFindPatchInfo sgSurface kd.objectSetPath |> Option.map fst
                            }
                        | _ -> skipped <- skipped + 1
                    if skipped > 0 then
                        Log.warn "[EllipseStatistics] %s: %d patches without a position grid are left out" surface.name skipped
                | _ -> ()
            | _ -> ()

        computeOnPatches wanted depth ellipses patches

/// The ellipse statistics as columns of a per-annotation export record: the *Boulders*
/// CSV (docs/AnnotationExport-CSV.md).
module EllipseStatisticsColumns =

    open PRo3D.Base.Annotation

    [<Literal>]
    let SurfaceArea = "surfaceArea"
    [<Literal>]
    let FootprintArea = "footprintArea"
    [<Literal>]
    let VertexCount = "vertexCount"

    /// The ellipse stored with an annotation at construction, if it is an ellipse and
    /// has one (older files do not).
    let tryEllipse (a : Annotation) : Option<SurfaceEllipse> =
        match a.geometry, a.ellipticResults with
        | (Geometry.AxisEllipse | Geometry.Axis4PEllipse | Geometry.Ellipse), Some e
            when not (e.center.AnyNaN || e.semiMajorAxis.AnyNaN || e.semiMinorAxis.AnyNaN) ->
            Some { center = e.center; semiMajor = e.semiMajorAxis; semiMinor = e.semiMinorAxis }
        | _ -> None

    /// `surface_<layer>_<statistic>`, next to the per-point `surface_<layer>` columns.
    let layerColumn (layer : string) (statistic : string) =
        AnnotationExport.surfaceColumnName (sprintf "%s_%s" layer statistic)

    /// The columns of one ellipse. `None` statistics (no surface to integrate) still
    /// give the footprint, which only needs the ellipse; the other cells stay empty.
    let columnsOf (ellipse : SurfaceEllipse) (statistics : Option<EllipseStatistics>) : list<string * ExportValue> =
        let footprint = Constant.Pi * ellipse.semiMajor.Length * ellipse.semiMinor.Length
        match statistics with
        | None ->
            [ SurfaceArea, VMissing; FootprintArea, ExportValue.ofFloat footprint; VertexCount, VMissing ]
        | Some s ->
            [ yield SurfaceArea,   ExportValue.ofFloat s.surfaceArea
              yield FootprintArea, ExportValue.ofFloat s.footprintArea
              yield VertexCount,   VInt s.vertexCount
              for layer in s.layers do
                  let channels (f : ChannelStatistics -> float) =
                      ExportValue.ofChannels (layer.channels |> Array.map f)
                  yield layerColumn layer.name "area", ExportValue.ofFloat layer.area
                  yield layerColumn layer.name "mean", channels (fun c -> c.mean)
                  yield layerColumn layer.name "std",  channels (fun c -> c.std)
                  yield layerColumn layer.name "min",  channels (fun c -> c.min)
                  yield layerColumn layer.name "max",  channels (fun c -> c.max) ]
