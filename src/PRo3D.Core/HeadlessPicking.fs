namespace PRo3D.Core.Surface

open System
open System.IO
open System.Collections.Generic

open Aardvark.Base
open Aardvark.Geometry
open Aardvark.Data.Opc
open Aardvark.SceneGraph.Opc

open OpcViewer.Base
open OpcViewer.Base.KdTrees
open Aardvark.VRVis.Opc.KdTrees

open FSharp.Data.Adaptive

open PRo3D.Base

/// A ray hit against an OPC, with everything an attribute lookup needs.
type OpcRayHit =
    {
        position  : V3d
        /// The kd-tree of the patch that was hit -- carries the patch's object-set path,
        /// which is the key for attribute extraction.
        tree      : Level0KdTree
        hit       : ObjectRayHit
    }

/// Ray casting against OPC surfaces without a viewer.
///
/// PRo3D's usual picking path (`SurfaceIntersection.doKdTreeIntersection`) needs a
/// `SurfaceModel`, `SgSurface`s and a `ReferenceSystem` -- the whole viewer-side model. A
/// command line tool has none of that and only wants "where does this ray meet the terrain".
///
/// `src/Tests/ProfileAttributeExtractionTest.fs` still carries its own equivalent of this and
/// should be migrated onto it, so that there is one implementation rather than two.
module HeadlessPicking =

    /// The kd-tree intersection API takes a filter that decides which hits to *omit*, and
    /// every caller here wants all of them.
    let private noHitFilter =
        Func<IIntersectableObjectSet, int, int, RayHit3d, bool>(fun _ _ _ _ -> false)

    /// Must run before any kd-tree is read: the pickler factories are what let FsPickler
    /// deserialise `Level0KdTree` and the in-core variant. Without them loading fails with an
    /// unrelated-looking serialisation error.
    let initKdTreeLoading () =
        Serialization.init()
        Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
        Serialization.registry.RegisterFactory (fun _ -> Init.incorePickler)

    let init () =
        Aardvark.Init()
        initKdTreeLoading ()

    /// Load every patch hierarchy directly under an OPC directory.
    ///
    /// A hierarchy is a subdirectory containing `Patches`; the sibling directories of a real
    /// data folder hold saved scenes and annotations, and handing one of those to
    /// `PatchHierarchy.load` throws.
    let loadHierarchies (opcDir : string) : PatchHierarchy[] =
        let serializer = Serialization.binarySerializer
        Directory.GetDirectories opcDir
        |> Array.filter (fun d -> Directory.Exists(Path.Combine(d, "Patches")))
        |> Array.map (fun basePath ->
            PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath))

    /// The level-0 kd-trees of these hierarchies, keyed by bounding box.
    ///
    /// Loads what is on disk and does not build anything: generating kd-trees for a large OPC
    /// takes minutes to hours, which a caller should ask for explicitly rather than trip over.
    /// An OPC without them yields an empty map, and the caller reports it.
    let loadKdTreeMap (hierarchies : PatchHierarchy seq) : HashMap<Box3d, Level0KdTree> =
        let serializer = Serialization.binarySerializer
        hierarchies
        |> Seq.fold (fun acc h ->
            let trees =
                KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ serializer
                    false true DebugKdTreesX.loadTriangles' false
            HashMap.union acc trees) HashMap.empty

    /// Nearest intersection of a ray with any of the patches.
    ///
    /// The bounding-box test first is not an optimisation detail: a level-0 tree's triangle set
    /// is loaded from disk on first use, so without it every ray would page in every patch of
    /// the body. `cache` carries those loaded trees between calls and must be reused across a
    /// batch.
    let intersectAll
        (kdTreeMap : HashMap<Box3d, Level0KdTree>)
        (cache : HashMap<string, ConcreteKdIntersectionTree>)
        (ray : FastRay3d) : Option<OpcRayHit> * HashMap<string, ConcreteKdIntersectionTree> =

        let mutable cache = cache
        let mutable best : Option<OpcRayHit> = None
        let mutable bestT = Double.MaxValue

        for (bb, lvl0Tree) in kdTreeMap |> HashMap.toList do
            let mutable tmin = 0.0
            let mutable tmax = Double.MaxValue
            if ray.Intersects(bb, &tmin, &tmax) then
                let kdTree, newCache = DebugKdTreesX.loadObjectSet cache lvl0Tree
                cache <- newCache
                if not (isNull kdTree.KdIntersectionTree.ObjectSet) then
                    let kdi = kdTree.KdIntersectionTree
                    let mutable hit = ObjectRayHit.MaxRange
                    try
                        if kdi.Intersect(ray, null, noHitFilter, 0.0, Double.MaxValue, &hit) then
                            if hit.RayHit.T < bestT then
                                bestT <- hit.RayHit.T
                                best <-
                                    Some {
                                        position = ray.Ray.GetPointOnRay hit.RayHit.T
                                        tree = lvl0Tree
                                        hit = hit
                                    }
                    with e ->
                        // One unreadable patch must not lose the whole ray: the nearest hit
                        // among the others is still a valid answer, and a systematically broken
                        // OPC shows up as every ray missing.
                        Log.warn "[HeadlessPicking] intersection failed for %A: %s" bb e.Message

        best, cache
    
// ---------------------------------------------------------------------------------------
// attribute sampling at a hit

    /// Per-vertex `*.aara` layers interpolated at the hit point.
    ///
    /// Only the per-vertex path: the texture fallback decodes one image per layer per sample,
    /// which is fine for a 3D cursor and not for a list of centroids.
    let sampleAttributes
        (patchInfos : Dictionary<string, PatchFileInfo>)
        (hit : OpcRayHit) : (string * float[]) list =

        match hit.tree with
        | Level0KdTree.InCoreKdTree _ -> []
        | Level0KdTree.LazyKdTree kd ->
            try
                let mapping = ProfileAttributeExtraction.buildTriangleToGridMapping kd.affine kd.objectSetPath
                let triangleIndex = hit.hit.SetObject.Index
                match mapping.TryGetTriangleIndices triangleIndex, patchInfos.TryGetValue kd.objectSetPath with
                | Some gridIndices, (true, patchInfo) ->
                    let triangleSet = hit.hit.SetObject.Set :?> TriangleSet
                    let triangle = DebugKdTreesX.getTriangle triangleSet triangleIndex
                    let weights = ProfileAttributeExtraction.computeBarycentric triangle hit.position
                    let layers = VertexAttributes.getLayers (Path.GetDirectoryName kd.objectSetPath) patchInfo
                    VertexAttributes.sample layers mapping.gridSize gridIndices weights
                    |> List.map (fun a -> a.name, a.values)
                | _ -> []
            with e ->
                Log.warn "[HeadlessPicking] attribute sampling failed: %s" e.Message
                []

    /// objectSetPath -> patch info, which is what the attribute layers are listed in.
    let buildPatchInfos (hierarchies : PatchHierarchy seq) =
        // Case-insensitive: OpcPaths probes "patches" before "Patches", so the path spelled here
        // can differ in case from the one persisted in a kd-tree.
        let d = Dictionary<string, PatchFileInfo>(StringComparer.OrdinalIgnoreCase)
        for h in hierarchies do
            for leaf in QTree.getLeaves h.tree do
                let info = leaf.info
                d.[h.opcPaths.Patches_DirAbsPath +/ info.Name +/ info.Positions] <- info
        d
