module ProfileAttributeExtractionTest

open System
open System.IO
open System.Collections.Generic

open Expecto

open Aardvark.Base
open Aardvark.Geometry
open Aardvark.Data.Opc
open Aardvark.SceneGraph.Opc

open MBrace.FsPickler

open OpcViewer.Base
open OpcViewer.Base.KdTrees
open Aardvark.VRVis.Opc.KdTrees

open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Core.Drawing

module Data =
    let boundingBox = Box3d.Parse("[[-89.180763245, -87.157432556, -56.789569855], [87.699211121, 86.719993591, 58.670009613]]")

    let opcBasePath (testDataSource : string) =
        Path.Combine(testDataSource, "Dimorphos_DRACO1", "Dimorphos_DRACO1")

    let annotationPath (testDataSource : string) =
        Path.Combine(testDataSource, "Dimorphos_DRACO1", "testAnnotatation.pro3d.ann")

    /// Directory holding OPC hierarchies that ship per-vertex attribute layers
    /// (`*.aara` files listed in each patch's `<Attributes>`). Set PRO3D_AARA_OPC to
    /// point at one, e.g. the HERA AARA_Textures export's "Dimorphos" folder.
    let aaraOpcBasePath (testDataSource : string) =
        let candidates =
            [
                Environment.GetEnvironmentVariable "PRO3D_AARA_OPC"
                Path.Combine(testDataSource, "AARA_Textures", "Dimorphos")
            ]
        candidates
        |> List.tryFind (fun p -> not (String.IsNullOrWhiteSpace p) && Directory.Exists p)

let private noHitFilter =
    Func<IIntersectableObjectSet,int,int,RayHit3d,bool>(fun _ _ _ _ -> false)

let private intersectAllKdTrees
    (kdTreeMap : HashMap<Box3d, Level0KdTree>)
    (cache : ref<HashMap<string, ConcreteKdIntersectionTree>>)
    (ray : FastRay3d) =

    let mutable bestHit : (ObjectRayHit * Level0KdTree) option = None
    let mutable bestT = Double.MaxValue

    for (bb, lvl0Tree) in kdTreeMap |> HashMap.toList do
        let mutable tmin = 0.0
        let mutable tmax = Double.MaxValue
        if ray.Intersects(bb, &tmin, &tmax) then
            let kdTree, newCache = DebugKdTreesX.loadObjectSet cache.Value lvl0Tree
            cache.Value <- newCache
            if not (isNull kdTree.KdIntersectionTree.ObjectSet) then
                let kdi = kdTree.KdIntersectionTree
                let mutable hit = ObjectRayHit.MaxRange
                try
                    if kdi.Intersect(ray, null, noHitFilter, 0.0, Double.MaxValue, &hit) then
                        if hit.RayHit.T < bestT then
                            bestT <- hit.RayHit.T
                            bestHit <- Some (hit, lvl0Tree)
                with _ -> ()
    bestHit

let private loadHierarchies (opcBasePath : string) =
    let serializer = Serialization.binarySerializer
    Directory.GetDirectories(opcBasePath)
    |> Array.map (fun basePath ->
        PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
    )

let private buildPatchInfoLookup (hierarchies : PatchHierarchy[]) =
    let d = Dictionary<string, PatchFileInfo * OpcPaths>()
    for h in hierarchies do
        for leaf in QTree.getLeaves h.tree do
            let info = leaf.info
            let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
            d.[dir +/ info.Positions] <- (info, OpcPaths h.opcPaths.Opc_DirAbsPath)
    d

let initKdTreeLoading() =
    Serialization.init()
    Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
    Serialization.registry.RegisterFactory (fun _ -> Init.incorePickler)

let init() =
    Aardvark.Init()
    initKdTreeLoading()

let tests (parameters : TestUtils.TestParameters) =
    do init()
    let testDataSource = parameters.testDataSource |> Option.defaultValue ""
    let opcBasePath = Data.opcBasePath testDataSource
    let annotationPath = Data.annotationPath testDataSource
    testList "ProfileAttributeExtraction" [

        test "buildTriangleToGridMapping produces valid mapping" {
            if not (Directory.Exists opcBasePath) then
                skiptest "OPC data not available"

            let hierarchies = loadHierarchies opcBasePath
            Expect.isGreaterThan hierarchies.Length 0 "should have at least one hierarchy"

            let h = hierarchies.[0]
            let leaves = QTree.getLeaves h.tree |> Seq.toArray
            Expect.isGreaterThan leaves.Length 0 "should have at least one leaf"

            let leaf = leaves.[0]
            let info = leaf.info
            let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
            let objectSetPath = dir +/ info.Positions

            let affine =
                ViewerModality.XYZ
                |> ViewerModality.matchy info.Local2Global info.Local2Global2d

            let mapping = ProfileAttributeExtraction.buildTriangleToGridMapping affine objectSetPath
            Expect.isGreaterThan mapping.TriangleCount 0 "should have triangles"
            Expect.isGreaterThan mapping.gridSize.X 0 "grid size should be positive"

            // the compact quad form must reproduce exactly what computeGridIndices emits
            let positions = objectSetPath |> Aara.fromFile<V3f>
            let reference = TriangleSet.computeGridIndices mapping.gridSize positions.Data
            Expect.equal mapping.TriangleCount (reference.Length / 3) "triangle count should match computeGridIndices"

            let vertexCount = mapping.gridSize.X * mapping.gridSize.Y
            for triIdx in 0 .. mapping.TriangleCount - 1 do
                match mapping.TryGetTriangleIndices triIdx with
                | None -> failtest $"no grid indices for triangle {triIdx}"
                | Some tri ->
                    Expect.equal tri.Length 3 "each triangle should have 3 indices"
                    for c in 0 .. 2 do
                        Expect.equal tri.[c] reference.[triIdx * 3 + c] "index should match computeGridIndices"
                        Expect.isTrue (tri.[c] >= 0 && tri.[c] < vertexCount) "index should be inside the position grid"
        }

        test "aara header round trip" {
            if not (Directory.Exists opcBasePath) then
                skiptest "OPC data not available"

            let hierarchies = loadHierarchies opcBasePath
            let h = hierarchies.[0]
            let leaf = QTree.getLeaves h.tree |> Seq.head
            let dir = h.opcPaths.Patches_DirAbsPath +/ leaf.info.Name
            let positionsPath = dir +/ leaf.info.Positions

            match VertexAttributes.tryGetLayer positionsPath with
            | None -> failtest $"could not read aara header of {positionsPath}"
            | Some layer ->
                // Positions are V3f grids; cross-check against the reference loader.
                let reference = positionsPath |> Aara.fromFile<V3f>
                Expect.equal layer.header.components 3 "positions have 3 components"
                Expect.equal layer.header.size (reference.Size.XY.ToV2i()) "grid size should match Aara.fromFile"
        }

        testCase "full kdtree intersection and attribute extraction" <| fun () ->
            if not (Directory.Exists opcBasePath) then
                skiptest "OPC data not available"

            let hierarchies = loadHierarchies opcBasePath
            let patchInfoLookup = buildPatchInfoLookup hierarchies
            Log.line "[Test] built patch info lookup with %d entries" patchInfoLookup.Count

            let serializer = Serialization.binarySerializer
            let kdTreeMap =
                hierarchies |> Array.fold (fun acc h ->
                    let trees =
                        KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ
                            serializer false true DebugKdTreesX.loadTriangles' false
                    HashMap.union acc trees
                ) HashMap.empty
            Log.line "[Test] loaded %d kd-tree entries" (kdTreeMap |> HashMap.count)

            // shoot a ray from center of bounding box downward
            let center = Data.boundingBox.Center
            let rayOrigin = center + V3d.OOI * 100.0
            let ray = FastRay3d(Ray3d(rayOrigin, -V3d.OOI))

            let cache = ref (HashMap.empty<string, ConcreteKdIntersectionTree>)
            let bestHit = intersectAllKdTrees kdTreeMap cache ray

            match bestHit with
            | Some (hit, Level0KdTree.LazyKdTree kd) ->
                let hitPoint = ray.Ray.GetPointOnRay(hit.RayHit.T)
                Log.line "[Test] hit at %A" hitPoint

                let mapping = ProfileAttributeExtraction.buildTriangleToGridMapping kd.affine kd.objectSetPath
                let triIdx = hit.SetObject.Index
                let triangleSet = hit.SetObject.Set :?> TriangleSet
                let triangle = DebugKdTreesX.getTriangle triangleSet triIdx
                let weights = ProfileAttributeExtraction.computeBarycentric triangle hitPoint
                let gridIndices = mapping.TryGetTriangleIndices triIdx |> Option.defaultValue [||]

                match ProfileAttributeExtraction.getUVAtHit kd.coordinatesPath mapping.gridSize gridIndices weights with
                | None -> failtest "no texture coordinates at hit"
                | Some uv ->
                    Log.line "[Test] UV: %A" uv
                    Expect.isTrue (uv.X >= 0.0f && uv.X <= 1.0f) "UV.X should be in [0,1]"
                    Expect.isTrue (uv.Y >= 0.0f && uv.Y <= 1.0f) "UV.Y should be in [0,1]"

                    match patchInfoLookup.TryGetValue(kd.objectSetPath) with
                    | true, (patchInfo, opcPaths) ->
                        let attributes =
                            ProfileAttributeExtraction.extractAttributesAtUV uv patchInfo opcPaths (fun _ -> None) (fun _ -> false)
                        Log.line "[Test] extracted %d attributes" attributes.Length
                        for a in attributes do
                            Log.line "[Test]   %s: %A (%A)" a.name a.values a.source
                    | _ ->
                        failtest "no patch info found for hit objectSetPath"

            | Some (_, Level0KdTree.InCoreKdTree _) ->
                failtest "InCoreKdTree not expected"
            | None ->
                failtest "no kdtree hit - check ray direction and bounding box"

        testCase "per-vertex layers are physically consistent" <| fun () ->
            // Only OPCs exported with per-vertex attribute layers can be checked here.
            match Data.aaraOpcBasePath testDataSource with
            | None -> skiptest "no OPC with per-vertex attribute layers available (set PRO3D_AARA_OPC)"
            | Some aaraBasePath ->

            let hierarchies = loadHierarchies aaraBasePath
            let patchInfoLookup = buildPatchInfoLookup hierarchies

            let serializer = Serialization.binarySerializer
            let kdTreeMap =
                hierarchies |> Array.fold (fun acc h ->
                    let trees =
                        KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ
                            serializer false true DebugKdTreesX.loadTriangles' false
                    HashMap.union acc trees
                ) HashMap.empty

            let bounds =
                kdTreeMap |> HashMap.toList |> List.fold (fun (b : Box3d) (bb : Box3d, _) -> b.ExtendedBy bb) Box3d.Invalid
            Log.line "[Test] %d kd-trees, bounds %A" (HashMap.count kdTreeMap) bounds

            let cache = ref (HashMap.empty<string, ConcreteKdIntersectionTree>)

            // shoot rays at the body from six directions so several patches are exercised
            let directions = [ V3d.OOI; -V3d.OOI; V3d.OIO; -V3d.OIO; V3d.IOO; -V3d.IOO ]

            let mutable checkedHits = 0

            for dir in directions do
                let origin = bounds.Center - dir * (bounds.Size.NormMax * 2.0)
                let ray = FastRay3d(Ray3d(origin, dir))
                match intersectAllKdTrees kdTreeMap cache ray with
                | Some (hit, Level0KdTree.LazyKdTree kd) ->
                    let hitPoint = ray.Ray.GetPointOnRay(hit.RayHit.T)
                    let mapping = ProfileAttributeExtraction.buildTriangleToGridMapping kd.affine kd.objectSetPath
                    let triangleSet = hit.SetObject.Set :?> TriangleSet
                    let triangle = DebugKdTreesX.getTriangle triangleSet hit.SetObject.Index
                    let weights = ProfileAttributeExtraction.computeBarycentric triangle hitPoint
                    let gridIndices = mapping.TryGetTriangleIndices hit.SetObject.Index |> Option.defaultValue [||]

                    match patchInfoLookup.TryGetValue(kd.objectSetPath) with
                    | true, (patchInfo, _) ->
                        let patchDir = Path.GetDirectoryName kd.objectSetPath
                        let layers = VertexAttributes.getLayers patchDir patchInfo
                        Expect.isGreaterThan layers.Length 0 $"{patchInfo.Name} should expose per-vertex layers"

                        let sampled = VertexAttributes.sample layers mapping.gridSize gridIndices weights
                        let byName = sampled |> List.map (fun a -> a.name, a.values) |> Map.ofList
                        for a in sampled do
                            Log.line "[Test] %-10s %-10s %A" patchInfo.Name a.name a.values

                        // The offset between the position grid and the (smaller) attribute
                        // grid is the one thing that can silently go wrong, and these
                        // invariants pin it down. The third LonLatRad channel is the vertex
                        // radius, so it must match the picked point's distance from the body
                        // centre; an off-by-one in the offset samples a neighbouring vertex
                        // and breaks this immediately.
                        match byName |> Map.tryFind "LonLatRad" with
                        | Some [| _; _; radius |] ->
                            let expected = hitPoint.Length
                            Expect.isLessThan (abs (radius - expected)) 0.05
                                $"{patchInfo.Name}: LonLatRad radius {radius} should match |hitPoint| {expected}"
                            checkedHits <- checkedHits + 1
                        | _ -> ()

                        // Per-vertex normals are unit length, but the barycentric blend of
                        // three unit vectors is shorter than 1 wherever they diverge - on
                        // Dimorphos's boulder field by several percent. So the invariant is
                        // "never longer than unit, and not degenerate".
                        match byName |> Map.tryFind "Normal" with
                        | Some [| x; y; z |] ->
                            let length = V3d(x, y, z).Length
                            Expect.isLessThan length (1.0 + 1e-3) $"{patchInfo.Name}: Normal should not exceed unit length"
                            Expect.isGreaterThan length 0.5 $"{patchInfo.Name}: Normal should not be degenerate"
                        | _ -> ()

                        // Magnitude is the length of the Gravity vector
                        match byName |> Map.tryFind "Gravity", byName |> Map.tryFind "Magnitude" with
                        | Some [| x; y; z |], Some [| magnitude |] ->
                            let length = V3d(x, y, z).Length
                            Expect.isLessThan (abs (magnitude - length) / magnitude) 1e-3
                                $"{patchInfo.Name}: Magnitude {magnitude} should be |Gravity| {length}"
                        | _ -> ()
                    | _ -> ()
                | _ -> ()

            Expect.isGreaterThan checkedHits 2 "should have validated several patches"
            Log.line "[Test] validated per-vertex layers at %d hits" checkedHits

        testCase "texture fallback reproduces per-vertex values" <| fun () ->
            // The attribute textures store each layer normalised into its *.opcx
            // ChannelsDefinedRange, so the fallback has to map samples back onto that range
            // before they can be compared with the per-vertex values.
            match Data.aaraOpcBasePath testDataSource with
            | None -> skiptest "no OPC with per-vertex attribute layers available (set PRO3D_AARA_OPC)"
            | Some aaraBasePath ->

            match Directory.EnumerateFiles(aaraBasePath, "*.opcx") |> Seq.tryHead with
            | None -> skiptest "no *.opcx next to the OPC"
            | Some opcxPath ->

            let scalarLayers =
                SurfaceUtils.SurfaceAttributes.read opcxPath |> SurfaceProperties.getScalarsHmap
            let rangeOf = ProfileAttributeExtraction.rangeLookup scalarLayers
            Log.line "[Test] %d scalar layers from %s" (HashMap.count scalarLayers) (Path.GetFileName opcxPath)
            Expect.isGreaterThan (HashMap.count scalarLayers) 0 "opcx should declare scalar layers"

            let hierarchies = loadHierarchies aaraBasePath
            let patchInfoLookup = buildPatchInfoLookup hierarchies

            let serializer = Serialization.binarySerializer
            let kdTreeMap =
                hierarchies |> Array.fold (fun acc h ->
                    let trees =
                        KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ
                            serializer false true DebugKdTreesX.loadTriangles' false
                    HashMap.union acc trees
                ) HashMap.empty

            let bounds =
                kdTreeMap |> HashMap.toList |> List.fold (fun (b : Box3d) (bb : Box3d, _) -> b.ExtendedBy bb) Box3d.Invalid
            let cache = ref (HashMap.empty<string, ConcreteKdIntersectionTree>)

            // Only smooth, non-wrapping layers are compared:
            //  - Slope is a gradient field, so nearest-texel and interpolated values
            //    legitimately differ by a large fraction of its range,
            //  - LonLatRad's first channel is a wrapped longitude, so a texel on the other
            //    side of the 0/360 seam reads as the range maximum instead.
            let smoothLayers = Set.ofList [ "Elevation"; "Potential"; "Magnitude" ]

            let mutable compared = 0
            let mutable worst = 0.0
            let mutable worstLayer = ""

            for dir in [ V3d.OOI; -V3d.OOI; V3d.OIO; -V3d.OIO; V3d.IOO; -V3d.IOO ] do
                let origin = bounds.Center - dir * (bounds.Size.NormMax * 2.0)
                let ray = FastRay3d(Ray3d(origin, dir))
                match intersectAllKdTrees kdTreeMap cache ray with
                | Some (hit, Level0KdTree.LazyKdTree kd) ->
                    let hitPoint = ray.Ray.GetPointOnRay(hit.RayHit.T)
                    let mapping = ProfileAttributeExtraction.buildTriangleToGridMapping kd.affine kd.objectSetPath
                    let triangleSet = hit.SetObject.Set :?> TriangleSet
                    let triangle = DebugKdTreesX.getTriangle triangleSet hit.SetObject.Index
                    let weights = ProfileAttributeExtraction.computeBarycentric triangle hitPoint
                    let gridIndices = mapping.TryGetTriangleIndices hit.SetObject.Index |> Option.defaultValue [||]

                    match patchInfoLookup.TryGetValue(kd.objectSetPath) with
                    | true, (patchInfo, opcPaths) ->
                        let patchDir = Path.GetDirectoryName kd.objectSetPath
                        let layers = VertexAttributes.getLayers patchDir patchInfo
                        let fromVertices = VertexAttributes.sample layers mapping.gridSize gridIndices weights
                        let fromTextures =
                            match ProfileAttributeExtraction.getUVAtHit kd.coordinatesPath mapping.gridSize gridIndices weights with
                            | Some uv ->
                                ProfileAttributeExtraction.extractAttributesAtUV uv patchInfo opcPaths rangeOf (fun _ -> false)
                            | None -> []

                        let textureByName = fromTextures |> List.map (fun a -> a.name, a.values) |> Map.ofList

                        for v in fromVertices do
                            if smoothLayers.Contains v.name then
                                match textureByName |> Map.tryFind v.name, rangeOf v.name with
                                | Some t, Some range when t.Length > 0 ->
                                    // judged relative to the layer's range, the only scale a
                                    // normalised encoding can sensibly be measured against
                                    let scale = max 1e-12 range.Size
                                    let deviation = abs (v.values.[0] - t.[0]) / scale
                                    if deviation > worst then
                                        worst <- deviation
                                        worstLayer <- v.name
                                    compared <- compared + 1
                                    Log.line "[Test] %-10s vertex %-14g texture %-14g dev %.4f of range"
                                        v.name v.values.[0] t.[0] deviation
                                | _ -> ()
                    | _ -> ()
                | _ -> ()

            Expect.isGreaterThan compared 0 "should have compared at least one layer"
            Log.line "[Test] compared %d values, worst deviation %.4f of range (%s)" compared worst worstLayer
            // The tolerance is deliberately loose: the two paths sample differently
            // (nearest-texel vs barycentric interpolation) on grids of different size, so a
            // few percent of the layer range is expected. What this pins down is the
            // de-normalisation - without it the texture values are raw [0,1] samples and the
            // deviation is on the order of a whole range.
            Expect.isLessThan worst 0.10
                $"de-normalised texture samples should match per-vertex values (worst: {worstLayer})"

        testCase "end-to-end profile extraction from annotation file" <| fun () ->
            if not (Directory.Exists opcBasePath) then
                skiptest "OPC data not available"
            if not (File.Exists annotationPath) then
                skiptest "annotation file not available"

            let sw = System.Diagnostics.Stopwatch()
            let mb () = float (GC.GetTotalMemory(false)) / (1024.0 * 1024.0)
            let timed label (f : unit -> 'a) =
                let memBefore = mb ()
                sw.Restart()
                let r = f ()
                sw.Stop()
                Log.line "[Timing] %-32s %8.1f ms   mem: %7.1f -> %7.1f MB (+%+.1f)"
                    label sw.Elapsed.TotalMilliseconds memBefore (mb ()) (mb () - memBefore)
                r

            let serializer = Serialization.binarySerializer

            // load annotation
            let drawingModel =
                timed "load annotation file" (fun () ->
                    DrawingUtilities.IO.loadAnnotationsFromFile annotationPath)
            let allAnnotations = drawingModel.annotations.flat |> Leaf.toAnnotations
            let firstAnnotation =
                allAnnotations
                |> HashMap.toList
                |> List.map snd
                |> List.tryHead

            match firstAnnotation with
            | None -> failtest "no annotation found in file"
            | Some (annotation : Annotation) ->
                let points = annotation |> Annotation.retrievePoints
                Log.line "[Test] annotation has %d points" points.Length
                Expect.isGreaterThan points.Length 1 "annotation should have at least 2 points"

                // load hierarchies and kdtrees
                let hierarchies = timed "load patch hierarchies" (fun () -> loadHierarchies opcBasePath)

                let kdTreeMap =
                    timed "load kdtrees" (fun () ->
                        hierarchies |> Array.fold (fun acc h ->
                            let trees =
                                KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ
                                    serializer false true DebugKdTreesX.loadTriangles' false
                            HashMap.union acc trees
                        ) HashMap.empty)
                Log.line "[Test] kdTreeMap size: %d" (HashMap.count kdTreeMap)

                let patchInfoLookup = timed "build patchInfoLookup" (fun () -> buildPatchInfoLookup hierarchies)
                Log.line "[Test] patchInfoLookup size: %d" patchInfoLookup.Count

                // attribute layer ranges - texture samples are normalised into them
                let scalarLayers =
                    let probes = [ opcBasePath; Path.GetDirectoryName opcBasePath ]
                    match probes |> List.tryPick (fun d -> Directory.EnumerateFiles(d, "*.opcx") |> Seq.tryHead) with
                    | Some opcx -> SurfaceUtils.SurfaceAttributes.read opcx |> SurfaceProperties.getScalarsHmap
                    | None ->
                        Log.warn "[Test] no *.opcx found - texture samples stay normalised"
                        HashMap.empty
                let rangeOf = ProfileAttributeExtraction.rangeLookup scalarLayers

                // for each annotation point, intersect and extract
                let mutable hitCount = 0
                let mutable missCount = 0
                let mutable allAttrNames = Set.empty
                let mutable accDistance = 0.0
                let mutable vertexSourced = 0
                let mutable textureSourced = 0
                let samples = ResizeArray<ProfileSample>()

                // per-step accumulators
                let mutable tIntersect = 0.0
                let mutable tGrid = 0.0
                let mutable tUv = 0.0
                let mutable tExtract = 0.0
                let stepSw = System.Diagnostics.Stopwatch()
                let loopSw = System.Diagnostics.Stopwatch()
                let memBeforeLoop = mb ()
                // cache object-sets once across all rays — without this, every point
                // re-loads every kdtree whose bbox it touches
                let kdObjectSetCache = ref (HashMap.empty<string, ConcreteKdIntersectionTree>)
                loopSw.Start()

                for i in 0 .. points.Length - 1 do
                    let p = points.[i]
                    if i > 0 then
                        accDistance <- accDistance + Vec.distance points.[i-1] p
                    let up = p.Normalized
                    let rayOrigin = p + up * 5.0
                    let ray = FastRay3d(Ray3d(rayOrigin, -up))

                    stepSw.Restart()
                    let hitOpt = intersectAllKdTrees kdTreeMap kdObjectSetCache ray
                    stepSw.Stop(); tIntersect <- tIntersect + stepSw.Elapsed.TotalMilliseconds

                    match hitOpt with
                    | Some (hit, Level0KdTree.LazyKdTree kd) ->
                        let hitPoint = ray.Ray.GetPointOnRay(hit.RayHit.T)

                        stepSw.Restart()
                        let mapping = ProfileAttributeExtraction.buildTriangleToGridMapping kd.affine kd.objectSetPath
                        let gridSize = mapping.gridSize
                        stepSw.Stop(); tGrid <- tGrid + stepSw.Elapsed.TotalMilliseconds

                        let triIdx = hit.SetObject.Index
                        let triangleSet = hit.SetObject.Set :?> TriangleSet
                        let triangle = DebugKdTreesX.getTriangle triangleSet triIdx
                        let weights = ProfileAttributeExtraction.computeBarycentric triangle hitPoint
                        let gridIndices = mapping.TryGetTriangleIndices triIdx |> Option.defaultValue [||]

                        stepSw.Restart()
                        let uv = ProfileAttributeExtraction.getUVAtHit kd.coordinatesPath gridSize gridIndices weights
                        stepSw.Stop(); tUv <- tUv + stepSw.Elapsed.TotalMilliseconds

                        match patchInfoLookup.TryGetValue(kd.objectSetPath) with
                        | true, (patchInfo, opcPaths) ->
                            stepSw.Restart()
                            let patchDir = Path.GetDirectoryName kd.objectSetPath
                            let layers = VertexAttributes.getLayers patchDir patchInfo
                            let fromVertices = VertexAttributes.sample layers gridSize gridIndices weights
                            let covered = fromVertices |> List.map (fun a -> a.name) |> Set.ofList
                            let fromTextures =
                                match uv with
                                | Some uv -> ProfileAttributeExtraction.extractAttributesAtUV uv patchInfo opcPaths rangeOf covered.Contains
                                | None    -> []
                            stepSw.Stop(); tExtract <- tExtract + stepSw.Elapsed.TotalMilliseconds

                            let attributes = Dictionary<string, float[]>()
                            for a in fromVertices @ fromTextures do
                                attributes.[a.name] <- a.values
                                allAttrNames <- allAttrNames |> Set.add a.name
                                match a.source with
                                | AttributeSource.VertexData      -> vertexSourced <- vertexSourced + 1
                                | AttributeSource.TextureSampling -> textureSourced <- textureSourced + 1

                            samples.Add({ position = hitPoint; distance = accDistance; attributes = attributes })
                            hitCount <- hitCount + 1
                        | _ ->
                            missCount <- missCount + 1
                    | _ ->
                        missCount <- missCount + 1

                    if (i + 1) % 25 = 0 || i = points.Length - 1 then
                        Log.line "[Timing] point %4d/%d   mem %7.1f MB   elapsed %8.1f s"
                            (i + 1) points.Length (mb ()) (loopSw.Elapsed.TotalSeconds)

                loopSw.Stop()
                let n = max 1 hitCount
                Log.line "[Timing] --- per-point loop totals (hits=%d) ---" hitCount
                Log.line "[Timing] %-32s total %8.1f ms   per-hit %6.2f ms" "intersectAllKdTrees" tIntersect (tIntersect / float n)
                Log.line "[Timing] %-32s total %8.1f ms   per-hit %6.2f ms" "buildTriangleToGridMapping" tGrid (tGrid / float n)
                Log.line "[Timing] %-32s total %8.1f ms   per-hit %6.2f ms" "getUVAtHit" tUv (tUv / float n)
                Log.line "[Timing] %-32s total %8.1f ms   per-hit %6.2f ms" "attribute sampling" tExtract (tExtract / float n)
                Log.line "[Timing] loop wall: %.1f s   mem %7.1f -> %7.1f MB (+%+.1f)"
                    loopSw.Elapsed.TotalSeconds memBeforeLoop (mb ()) (mb () - memBeforeLoop)
                Log.line "[Timing] kd object-set cache holds %d entries (of %d bboxes)"
                    (HashMap.count kdObjectSetCache.Value) (HashMap.count kdTreeMap)
                Log.line "[Test] %d values from per-vertex layers, %d from texture sampling" vertexSourced textureSourced

                Log.line "[Test] hits: %d, misses: %d, attributes: %A" hitCount missCount (allAttrNames |> Set.toList)
                Expect.isGreaterThan hitCount 0 "should have at least one hit"

                // write test output CSV
                let outputPath = Path.Combine(TestUtils.outputDir parameters "ProfileExtraction", "test_multi_attr_profile.csv")
                Log.line "[Test] writing test CSV to %s" outputPath
                timed "writeCsv" (fun () ->
                    ProfileAttributeExtraction.writeCsv outputPath (samples |> List.ofSeq) allAttrNames
                )
    ]
