module EllipseStatisticsTest

open System
open System.IO
open System.Collections.Generic

open Expecto

open Aardvark.Base
open Aardvark.Geometry
open Aardvark.Data.Opc
open Aardvark.SceneGraph.Opc

open OpcViewer.Base
open OpcViewer.Base.KdTrees
open Aardvark.VRVis.Opc.KdTrees

open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

/// A regular grid mesh over [-extent, extent]², lifted by `height`, integrated straight
/// into an accumulator. `layer` gives each vertex its channel values, or None for a hole.
let private integrateGrid
    (frame   : EllipseIntegration.Frame)
    (extent  : float)
    (spacing : float)
    (height  : V2d -> float)
    (layer   : V3d -> Option<float[]>)
    (components : int) =

    let acc = EllipseIntegration.Accumulator(frame)
    let values = EllipseIntegration.CornerValues("f", components)
    let n = int (ceil (2.0 * extent / spacing))
    let vertex (i : int) (j : int) =
        let xy = V2d(-extent + float i * spacing, -extent + float j * spacing)
        V3d(xy.X, xy.Y, height xy)

    let triangle (p0 : V3d) (p1 : V3d) (p2 : V3d) =
        let corners = [| p0; p1; p2 |]
        let mutable valid = true
        for k in 0 .. 2 do
            match layer corners.[k] with
            | Some v -> Array.blit v 0 values.Values (k * components) components
            | None -> valid <- false
        values.Valid <- valid
        acc.AddTriangle(p0, p1, p2, frame.ToLocal p0, frame.ToLocal p1, frame.ToLocal p2, [| values |])

    for i in 0 .. n - 1 do
        for j in 0 .. n - 1 do
            let a = vertex i j
            let b = vertex i (j + 1)
            let c = vertex (i + 1) j
            let d = vertex (i + 1) (j + 1)
            triangle a b c
            triangle c b d

    acc.Result 0

let private ellipseAt (center : V3d) (angleDeg : float) (a : float) (b : float) =
    let angle = angleDeg * Constant.RadiansPerDegree
    let major = V3d(cos angle, sin angle, 0.0)
    let minor = V3d(-sin angle, cos angle, 0.0)
    { center = center; semiMajor = major * a; semiMinor = minor * b }

let private frameOf (depth : float) (ellipse : SurfaceEllipse) =
    match EllipseIntegration.tryFrame depth ellipse with
    | Some f -> f
    | None -> failtest "ellipse should have a frame"

let private layerOf (stats : EllipseStatistics) (name : string) =
    match stats.layers |> List.tryFind (fun l -> l.name = name) with
    | Some l -> l
    | None -> failtest $"no layer {name}"

let private close (tolerance : float) (actual : float) (expected : float) (what : string) =
    Expect.isLessThan (abs (actual - expected)) tolerance $"{what}: {actual} should be {expected}"

let syntheticTests =
    testList "synthetic meshes" [

        // Uniform distribution over an ellipse with semi-axes a, b at angle θ: the
        // variance of x is (a²cos²θ + b²sin²θ) / 4 and its range is ±sqrt(a²cos²θ + b²sin²θ).
        let a, b, angle = 3.0, 2.0, 30.0
        let center = V3d(0.3, -0.2, 0.0)
        let ellipse = ellipseAt center angle a b
        let theta = angle * Constant.RadiansPerDegree
        let spreadX = sqrt (a * a * cos theta ** 2.0 + b * b * sin theta ** 2.0)
        let linear (p : V3d) = Some [| 2.0 * p.X + 5.0; p.Y |]

        let checkLinear (stats : EllipseStatistics) =
            close (1e-3 * stats.footprintArea) stats.surfaceArea (Constant.Pi * a * b) "area"
            let f = layerOf stats "f"
            close 1e-9 f.area stats.surfaceArea "layer area"
            let fx = f.channels.[0]
            close 1e-3 fx.mean (2.0 * center.X + 5.0) "mean"
            close 1e-3 fx.std (2.0 * spreadX / 2.0) "std"
            close 2e-3 fx.min (2.0 * (center.X - spreadX) + 5.0) "min"
            close 2e-3 fx.max (2.0 * (center.X + spreadX) + 5.0) "max"
            close 1e-3 f.channels.[1].mean center.Y "mean of the second channel"

        test "fine flat mesh: exact area and moments of a linear layer" {
            integrateGrid (frameOf 1.0 ellipse) 5.0 0.37 (fun _ -> 0.0) linear 2 |> checkLinear
        }

        test "one triangle larger than the ellipse gives the same answer" {
            // every triangle crosses the rim, so all of it comes from clipping
            integrateGrid (frameOf 1.0 ellipse) 20.0 40.0 (fun _ -> 0.0) linear 2 |> checkLinear
        }

        test "a tilted surface counts its true area, not its footprint" {
            let tilt = 40.0 * Constant.RadiansPerDegree
            let stats = integrateGrid (frameOf 10.0 ellipse) 5.0 0.23 (fun p -> p.X * tan tilt) linear 2
            close 1e-3 stats.surfaceArea (Constant.Pi * a * b / cos tilt) "surface area"
            close 1e-9 stats.footprintArea (Constant.Pi * a * b) "footprint"
            // along the tilt, the surface is stretched uniformly, so the mean stays put
            close 1e-3 (layerOf stats "f").channels.[0].mean (2.0 * center.X + 5.0) "mean"
        }

        test "surface area weights the mean where footprint would not" {
            // x < 0.3 is flat, x > 0.3 rises at 60°: per unit of footprint the steep half
            // has twice the area, so the mean of a layer that is 0 on the flat and 1 on
            // the steep side is 2/3, not the footprint's 1/2
            let circle = ellipseAt center 0.0 2.0 2.0
            let slope = 60.0 * Constant.RadiansPerDegree
            let height (p : V2d) = if p.X > center.X then (p.X - center.X) * tan slope else 0.0
            let side (p : V3d) = Some [| if p.X > center.X + 1e-9 then 1.0 else 0.0 |]
            // a grid line runs along x = 0.3, so no triangle straddles the kink
            let stats = integrateGrid (frameOf 10.0 circle) 5.3 0.02 height side 1
            close 1e-3 stats.surfaceArea (Constant.Pi * 4.0 * 1.5) "surface area"
            // the layer is linear per triangle, so the column of triangles along the kink
            // blends 0 and 1; that strip is 0.02 of 4 m wide
            close 0.005 (layerOf stats "f").channels.[0].mean (2.0 / 3.0) "mean"
        }

        test "holes shrink the layer's area, not the surface's" {
            let holey (p : V3d) = if p.X < center.X then None else linear p
            let stats = integrateGrid (frameOf 1.0 ellipse) 5.0 0.1 (fun _ -> 0.0) holey 2
            close 1e-3 stats.surfaceArea (Constant.Pi * a * b) "surface area"
            close 0.02 ((layerOf stats "f").area / stats.surfaceArea) 0.5 "share with values"
        }

        test "surfaces outside the slab do not count" {
            let stats = integrateGrid (frameOf 1.0 ellipse) 5.0 0.37 (fun _ -> 5.0) linear 2
            Expect.equal stats.surfaceArea 0.0 "nothing inside the slab"
            Expect.isEmpty stats.layers "no layers"
        }

        test "an ellipse without area has no frame" {
            let flat = { center = V3d.Zero; semiMajor = V3d.IOO; semiMinor = V3d.IOO * 2.0 }
            Expect.isNone (EllipseIntegration.tryFrame 1.0 flat) "collinear axes"
        }
    ]

// ---- Dimorphos ---------------------------------------------------------------------

let private noHitFilter =
    Func<IIntersectableObjectSet,int,int,RayHit3d,bool>(fun _ _ _ _ -> false)

let private intersect
    (kdTreeMap : HashMap<Box3d, Level0KdTree>)
    (cache : ref<HashMap<string, ConcreteKdIntersectionTree>>)
    (ray : FastRay3d) =

    let mutable best : Option<ObjectRayHit * Level0KdTree> = None
    let mutable bestT = Double.MaxValue
    for (bb, level0) in kdTreeMap do
        let mutable tmin = 0.0
        let mutable tmax = Double.MaxValue
        if ray.Intersects(bb, &tmin, &tmax) then
            let kdTree, c = DebugKdTreesX.loadObjectSet cache.Value level0
            cache.Value <- c
            let mutable hit = ObjectRayHit.MaxRange
            if kdTree.KdIntersectionTree.Intersect(ray, null, noHitFilter, 0.0, Double.MaxValue, &hit) && hit.RayHit.T < bestT then
                bestT <- hit.RayHit.T
                best <- Some (hit, level0)
    best

/// Surface-area weighted means by brute force: a dense grid of rays through the ellipse
/// along its normal, each hit weighted by 1/cos of its triangle against the normal (the
/// surface area a unit of footprint carries there). Shares nothing with the integration
/// but the attribute files: triangles come from the KdTree, values from
/// `VertexAttributes.sample`.
let private bruteForce
    (kdTreeMap : HashMap<Box3d, Level0KdTree>)
    (patchInfo : string -> Option<PatchFileInfo>)
    (frame : EllipseIntegration.Frame)
    (spacing : float) =

    let cache = ref HashMap.empty
    let sums = Dictionary<string, float>()
    let weights = Dictionary<string, float>()
    let mutable area = 0.0
    let cell = spacing * spacing
    let nu = int (ceil (frame.a / spacing))
    let nv = int (ceil (frame.b / spacing))
    for i in -nu .. nu do
        for j in -nv .. nv do
            let x = float i * spacing
            let y = float j * spacing
            if (x / frame.a) ** 2.0 + (y / frame.b) ** 2.0 <= 1.0 then
                let origin = frame.center + frame.majorDir * x + frame.minorDir * y + frame.normal * frame.depth
                let ray = FastRay3d(Ray3d(origin, -frame.normal))
                match intersect kdTreeMap cache ray with
                | Some (hit, Level0KdTree.LazyKdTree kd) when hit.RayHit.T <= 2.0 * frame.depth ->
                    let triangle = DebugKdTreesX.getTriangle (hit.SetObject.Set :?> TriangleSet) hit.SetObject.Index
                    let cosine = abs (Vec.dot triangle.Normal frame.normal)
                    let weight = cell / max cosine 1e-3
                    area <- area + weight
                    let mapping = ProfileAttributeExtraction.buildTriangleToGridMapping kd.affine kd.objectSetPath
                    let point = ray.Ray.GetPointOnRay hit.RayHit.T
                    let barycentric = ProfileAttributeExtraction.computeBarycentric triangle point
                    match mapping.TryGetTriangleIndices hit.SetObject.Index, patchInfo kd.objectSetPath with
                    | Some indices, Some info ->
                        let layers = VertexAttributes.getLayers (Path.GetDirectoryName kd.objectSetPath) info
                        for s in VertexAttributes.sample layers mapping.gridSize indices barycentric do
                            if s.values.Length = 1 && not (Double.IsNaN s.values.[0]) then
                                sums.[s.name] <- (match sums.TryGetValue s.name with | true, v -> v | _ -> 0.0) + weight * s.values.[0]
                                weights.[s.name] <- (match weights.TryGetValue s.name with | true, v -> v | _ -> 0.0) + weight
                    | _ -> ()
                | _ -> ()

    area, sums |> Seq.map (fun (KeyValue(name, sum)) -> name, sum / weights.[name]) |> Map.ofSeq

let dimorphosTests (parameters : TestUtils.TestParameters) =
    let root = ProfileAttributeExtractionTest.Data.root (parameters.testDataSource |> Option.defaultValue "")
    let opc = root |> Option.bind ProfileAttributeExtractionTest.Data.aaraOpcBasePath

    let accuracy =
      testCase "Dimorphos: integration agrees with brute-force ray sampling" <| fun () ->
        let opcBasePath =
            match opc with
            | Some p -> p
            | None -> skiptest "Dimorphos OPC with per-vertex layers not found (PRO3D_TEST_DATA)"

        ProfileAttributeExtractionTest.init ()
        let serializer = Serialization.binarySerializer
        let hierarchies =
            Directory.GetDirectories opcBasePath
            |> Array.map (fun dir -> PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths dir))

        let infos = Dictionary<string, PatchFileInfo>(StringComparer.OrdinalIgnoreCase)
        for h in hierarchies do
            for leaf in QTree.getLeaves h.tree do
                infos.[Path.GetFullPath(h.opcPaths.Patches_DirAbsPath +/ leaf.info.Name +/ leaf.info.Positions)] <- leaf.info
        let patchInfo (path : string) =
            match infos.TryGetValue(Path.GetFullPath path) with
            | true, info -> Some info
            | _ -> None

        let kdTreeMap =
            hierarchies |> Array.fold (fun acc h ->
                KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ serializer false true DebugKdTreesX.loadTriangles' false
                |> HashMap.union acc
            ) HashMap.empty

        let patches =
            kdTreeMap |> HashMap.toList |> List.choose (fun (box, level0) ->
                match level0 with
                | Level0KdTree.LazyKdTree kd ->
                    Some { box = box; surfaceTrafo = Trafo3d.Identity; kd = kd; patchInfo = fun () -> patchInfo kd.objectSetPath }
                | _ -> None
            )
        Log.line "[EllipseStatistics] %d patches" patches.Length

        let bounds = kdTreeMap |> HashMap.toList |> List.fold (fun (b : Box3d) (bb, _) -> b.ExtendedBy bb) Box3d.Invalid
        let cache = ref HashMap.empty

        // An ellipse lying on the surface where a ray from `direction` lands: normal along
        // the radius, semi-axes in the tangent plane.
        let ellipseFrom (direction : V3d) (a : float) (b : float) =
            let origin = bounds.Center - direction * bounds.Size.NormMax * 2.0
            match intersect kdTreeMap cache (FastRay3d(Ray3d(origin, direction))) with
            | None -> failtest $"no surface in direction {direction}"
            | Some (hit, _) ->
                let p = origin + direction * hit.RayHit.T
                let up = p.Normalized
                let major = Vec.cross up V3d.OOI |> Vec.normalize
                let minor = Vec.cross up major
                { center = p; semiMajor = major * a; semiMinor = minor * b }

        let ellipses =
            [| ellipseFrom V3d.IOO 12.0 7.0
               ellipseFrom (-V3d.OIO) 9.0 9.0
               ellipseFrom (V3d(1.0, 1.0, 0.3).Normalized) 15.0 5.0 |]

        let sw = Diagnostics.Stopwatch.StartNew()
        let results = EllipseStatistics.computeOnPatches (fun _ -> true) EllipseStatistics.defaultDepth ellipses patches
        Log.line "[EllipseStatistics] %d ellipses integrated in %d ms" ellipses.Length sw.ElapsedMilliseconds

        for e in 0 .. ellipses.Length - 1 do
            match results.[e] with
            | None -> failtest $"ellipse {e} has no frame"
            | Some stats ->
                let frame = frameOf (EllipseStatistics.defaultDepth ellipses.[e]) ellipses.[e]
                Log.line "[EllipseStatistics] ellipse %d: surface %.2f m², footprint %.2f m², %d vertices" e stats.surfaceArea stats.footprintArea stats.vertexCount
                Expect.isGreaterThan stats.vertexCount 10 $"ellipse {e} should cover several vertices"
                Expect.isGreaterThanOrEqual stats.surfaceArea (0.999 * stats.footprintArea) $"ellipse {e}: surface area is at least the footprint"

                let area, means = bruteForce kdTreeMap patchInfo frame 0.1
                Log.line "[EllipseStatistics]   brute force: surface %.2f m²" area
                close (0.02 * stats.surfaceArea) area stats.surfaceArea $"ellipse {e} surface area"

                for layer in stats.layers do
                    match layer.channels, means |> Map.tryFind layer.name with
                    | [| c |], Some expected ->
                        Log.line "[EllipseStatistics]   %-10s mean %g (brute force %g), std %g, range %g .. %g" layer.name c.mean expected c.std c.min c.max
                        Expect.isTrue (c.min <= c.mean && c.mean <= c.max) $"{layer.name}: mean inside range"
                        // the brute force samples a grid, so its error scales with the spread
                        close (0.05 * c.std + 1e-9 * abs c.mean) expected c.mean $"ellipse {e} {layer.name} mean"
                    | _ -> ()

    /// The size of a real boulder catalog (the Dimorphos SBMT one has ~4,800 rows),
    /// scattered over the whole body. Pins the cost, not the values.
    let catalog =
        testCase "Dimorphos: a 4,800-boulder catalog integrates in seconds" <| fun () ->
            let opcBasePath =
                match opc with
                | Some p -> p
                | None -> skiptest "Dimorphos OPC with per-vertex layers not found (PRO3D_TEST_DATA)"

            ProfileAttributeExtractionTest.init ()
            let serializer = Serialization.binarySerializer
            let hierarchies =
                Directory.GetDirectories opcBasePath
                |> Array.map (fun dir -> PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths dir))
            let infos = Dictionary<string, PatchFileInfo>(StringComparer.OrdinalIgnoreCase)
            for h in hierarchies do
                for leaf in QTree.getLeaves h.tree do
                    infos.[Path.GetFullPath(h.opcPaths.Patches_DirAbsPath +/ leaf.info.Name +/ leaf.info.Positions)] <- leaf.info
            let kdTreeMap =
                hierarchies |> Array.fold (fun acc h ->
                    KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ serializer false true DebugKdTreesX.loadTriangles' false
                    |> HashMap.union acc
                ) HashMap.empty
            let patches =
                kdTreeMap |> HashMap.toList |> List.choose (fun (box, level0) ->
                    match level0 with
                    | Level0KdTree.LazyKdTree kd ->
                        let info = match infos.TryGetValue(Path.GetFullPath kd.objectSetPath) with | true, i -> Some i | _ -> None
                        Some { box = box; surfaceTrafo = Trafo3d.Identity; kd = kd; patchInfo = fun () -> info }
                    | _ -> None
                )

            let bounds = kdTreeMap |> HashMap.toList |> List.fold (fun (b : Box3d) (bb, _) -> b.ExtendedBy bb) Box3d.Invalid
            let cache = ref HashMap.empty
            let random = RandomSystem(644)
            let ellipses =
                Array.init 4800 (fun _ ->
                    let direction = random.UniformV3dDirection()
                    let origin = bounds.Center - direction * bounds.Size.NormMax * 2.0
                    match intersect kdTreeMap cache (FastRay3d(Ray3d(origin, direction))) with
                    | None -> None
                    | Some (hit, _) ->
                        let p = origin + direction * hit.RayHit.T
                        let up = p.Normalized
                        let major = Vec.cross up V3d.OOI |> Vec.normalize
                        let minor = Vec.cross up major
                        let a = 0.5 + 4.5 * random.UniformDouble()
                        let b = a * (0.4 + 0.6 * random.UniformDouble())
                        Some { center = p; semiMajor = major * a; semiMinor = minor * b })
                |> Array.choose id

            let sw = Diagnostics.Stopwatch.StartNew()
            let results = EllipseStatistics.computeOnPatches (fun _ -> true) EllipseStatistics.defaultDepth ellipses patches
            sw.Stop()
            let covered = results |> Array.filter (function Some s -> s.surfaceArea > 0.0 | None -> false) |> Array.length
            Log.line "[EllipseStatistics] %d ellipses over %d patches integrated in %d ms; %d on the surface"
                ellipses.Length patches.Length sw.ElapsedMilliseconds covered
            Expect.isGreaterThan covered (ellipses.Length * 9 / 10) "nearly every ellipse lies on the surface"
            Expect.isLessThan sw.Elapsed.TotalSeconds 30.0 "a catalog export stays interactive"

    // one after the other: both load the whole OPC, and the brute force would skew the timing
    testSequenced <| testList "Dimorphos" [ accuracy; catalog ]

let tests (parameters : TestUtils.TestParameters) =
    testList "EllipseStatistics" [
        syntheticTests
        dimorphosTests parameters
    ]
