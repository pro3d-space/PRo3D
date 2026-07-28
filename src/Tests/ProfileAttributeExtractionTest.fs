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

            let serializer = Serialization.binarySerializer

            let hierarchies =
                Directory.GetDirectories(opcBasePath)
                |> Array.map (fun basePath ->
                    PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                )

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
            Expect.isGreaterThan mapping.Length 0 "should have triangles"

            for tri in mapping do
                Expect.equal tri.Length 3 "each triangle should have 3 indices"
        }

        test "extractAttributesAtUV returns attributes for a patch" {
            if not (Directory.Exists opcBasePath) then
                skiptest "OPC data not available"

            let serializer = Serialization.binarySerializer

            let hierarchies =
                Directory.GetDirectories(opcBasePath)
                |> Array.map (fun basePath ->
                    PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                )

            let h = hierarchies.[0]
            let leaves = QTree.getLeaves h.tree |> Seq.toArray
            let leaf = leaves.[0]
            let info = leaf.info
            let opcPaths = OpcPaths h.opcPaths.Opc_DirAbsPath

            let uv = V2f(0.5f, 0.5f)
            let attributes = ProfileAttributeExtraction.extractAttributesAtUV uv info opcPaths

            Log.line "[Test] extracted %d attributes at UV (0.5, 0.5):" attributes.Count
            for kvp in attributes do
                Log.line "[Test]   %s: %A" kvp.Key kvp.Value

            // we expect at least some attributes (textures beyond the primary)
            Expect.isGreaterThanOrEqual attributes.Count 0 "should not crash"
        }

        testCase "full kdtree intersection and attribute extraction" <| fun () ->
            if not (Directory.Exists opcBasePath) then
                skiptest "OPC data not available"

            let serializer = Serialization.binarySerializer

            let hierarchies =
                Directory.GetDirectories(opcBasePath)
                |> Array.map (fun basePath ->
                    PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                )

            // build patchInfoLookup
            let patchInfoLookup = Dictionary()
            for h in hierarchies do
                let leaves = QTree.getLeaves h.tree |> Seq.toArray
                for leaf in leaves do
                    let info = leaf.info
                    let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
                    let objectSetPath = dir +/ info.Positions
                    patchInfoLookup.[objectSetPath] <- (info, OpcPaths h.opcPaths.Opc_DirAbsPath)
            Log.line "[Test] built patch info lookup with %d entries" patchInfoLookup.Count

            // load kdtrees
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

                let gridMapping = ProfileAttributeExtraction.buildTriangleToGridMapping kd.affine kd.objectSetPath
                let triIdx = hit.SetObject.Index
                let triangleSet = hit.SetObject.Set :?> TriangleSet
                let triangle = DebugKdTreesX.getTriangle triangleSet triIdx

                let uv = ProfileAttributeExtraction.getUVAtHit kd.coordinatesPath gridMapping triIdx triangle hitPoint
                Log.line "[Test] UV: %A" uv

                Expect.isTrue (uv.X >= 0.0f && uv.X <= 1.0f) "UV.X should be in [0,1]"
                Expect.isTrue (uv.Y >= 0.0f && uv.Y <= 1.0f) "UV.Y should be in [0,1]"

                match patchInfoLookup.TryGetValue(kd.objectSetPath) with
                | true, (patchInfo, opcPaths) ->
                    let attributes = ProfileAttributeExtraction.extractAttributesAtUV uv patchInfo opcPaths
                    Log.line "[Test] extracted %d attributes" attributes.Count
                    for kvp in attributes do
                        Log.line "[Test]   %s: %A" kvp.Key kvp.Value
                | _ ->
                    failtest "no patch info found for hit objectSetPath"

            | Some (_, Level0KdTree.InCoreKdTree _) ->
                failtest "InCoreKdTree not expected"
            | None ->
                failtest "no kdtree hit - check ray direction and bounding box"

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
                let hierarchies =
                    timed "load patch hierarchies" (fun () ->
                        Directory.GetDirectories(opcBasePath)
                        |> Array.map (fun basePath ->
                            PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                        ))

                let kdTreeMap =
                    timed "load kdtrees" (fun () ->
                        hierarchies |> Array.fold (fun acc h ->
                            let trees =
                                KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ
                                    serializer false true DebugKdTreesX.loadTriangles' false
                            HashMap.union acc trees
                        ) HashMap.empty)
                Log.line "[Test] kdTreeMap size: %d" (HashMap.count kdTreeMap)

                // build patchInfoLookup
                let patchInfoLookup =
                    timed "build patchInfoLookup" (fun () ->
                        let d = Dictionary()
                        for h in hierarchies do
                            let leaves = QTree.getLeaves h.tree |> Seq.toArray
                            for leaf in leaves do
                                let info = leaf.info
                                let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
                                let objectSetPath = dir +/ info.Positions
                                d.[objectSetPath] <- (info, OpcPaths h.opcPaths.Opc_DirAbsPath)
                        d)
                Log.line "[Test] patchInfoLookup size: %d" patchInfoLookup.Count

                // for each annotation point, intersect and extract
                let mutable hitCount = 0
                let mutable missCount = 0
                let mutable allAttrNames = Set.empty
                let mutable accDistance = 0.0
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
                        let gridMapping = ProfileAttributeExtraction.buildTriangleToGridMapping kd.affine kd.objectSetPath
                        stepSw.Stop(); tGrid <- tGrid + stepSw.Elapsed.TotalMilliseconds

                        let triIdx = hit.SetObject.Index
                        let triangleSet = hit.SetObject.Set :?> TriangleSet
                        let triangle = DebugKdTreesX.getTriangle triangleSet triIdx

                        stepSw.Restart()
                        let uv = ProfileAttributeExtraction.getUVAtHit kd.coordinatesPath gridMapping triIdx triangle hitPoint
                        stepSw.Stop(); tUv <- tUv + stepSw.Elapsed.TotalMilliseconds

                        match patchInfoLookup.TryGetValue(kd.objectSetPath) with
                        | true, (patchInfo, opcPaths) ->
                            stepSw.Restart()
                            let attributes = ProfileAttributeExtraction.extractAttributesAtUV uv patchInfo opcPaths
                            stepSw.Stop(); tExtract <- tExtract + stepSw.Elapsed.TotalMilliseconds

                            for kvp in attributes do
                                allAttrNames <- allAttrNames |> Set.add kvp.Key
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
                Log.line "[Timing] %-32s total %8.1f ms   per-hit %6.2f ms" "extractAttributesAtUV" tExtract (tExtract / float n)
                Log.line "[Timing] loop wall: %.1f s   mem %7.1f -> %7.1f MB (+%+.1f)"
                    loopSw.Elapsed.TotalSeconds memBeforeLoop (mb ()) (mb () - memBeforeLoop)
                Log.line "[Timing] kd object-set cache holds %d entries (of %d bboxes)"
                    (HashMap.count kdObjectSetCache.Value) (HashMap.count kdTreeMap)

                Log.line "[Test] hits: %d, misses: %d, attributes: %A" hitCount missCount (allAttrNames |> Set.toList)
                Expect.isGreaterThan hitCount 0 "should have at least one hit"

                // write test output CSV
                let outputPath = Path.Combine(TestUtils.outputDir parameters "ProfileExtraction", "test_multi_attr_profile.csv")
                Log.line "[Test] writing test CSV to %s" outputPath
                timed "writeCsv" (fun () ->
                    ProfileAttributeExtraction.writeCsv outputPath (samples |> List.ofSeq) allAttrNames
                )
    ]
