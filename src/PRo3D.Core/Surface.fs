namespace PRo3D.Core.Surface

open System
open Aardvark.Base
open Aardvark.Geometry
open OpcViewer.Base
open FSharp.Data.Adaptive
open OpcViewer.Base.Picking
open Aardvark.Data.Opc

open PRo3D.Base
open PRo3D.Core
open PRo3D.Base.Gis
open PRo3D.Extensions
open PRo3D.Core.Surface
open PRo3DCompability
open OpcViewer.Base.KdTrees

module SurfaceTransformations = 
    //let computeSolRotation (sol : Sol) (referenceSystem : ReferenceSystem) : Trafo3d =
    //    let north = referenceSystem.northO
    //    let up = referenceSystem.up.value
    //    let east = Vec.cross up north
        
    //    let yawRotation    = Trafo3d.RotationInDegrees(up, -sol.yaw)
    //    let pitchRotation  = Trafo3d.RotationInDegrees(east, sol.pitch)
    //    let rollRotation   = Trafo3d.RotationInDegrees(north, sol.roll)

    //    yawRotation * pitchRotation * rollRotation

    /// TODO Refactor : This is not actually the complete trafo!! TODO rename this
    let fullTrafo' (surf : Surface) (observedSystem : Option<SpiceReferenceSystem>) (observerSystem : Option<ObserverSystem>) (refsys : ReferenceSystem) = 
    
        let north = refsys.northO.Normalized        
        let up    = refsys.up.value.Normalized
        let east  = north.Cross(up).Normalized
              
        let refSysRotation = 
            Trafo3d.FromOrthoNormalBasis(north, east, up)
            
        //translation along north, east, up            
        let trans = surf.transformation.translation.value |> Trafo3d.Translation //        

        let yawRotation    = Trafo3d.RotationInDegrees(up,   -surf.transformation.yaw.value)
        let pitchRotation  = Trafo3d.RotationInDegrees(east,  surf.transformation.pitch.value)
        let rollRotation   = Trafo3d.RotationInDegrees(north, surf.transformation.roll.value)
        
        let rot = yawRotation * pitchRotation * rollRotation
        
        let originTrafo = -surf.transformation.pivot.value |> Trafo3d.Translation

        let spiceTrafo = 
            match observedSystem, observerSystem with
            | Some observedSystem, Some observerSystem ->
                CooTransformation.transformBody observedSystem.body (Some observedSystem.referenceFrame) observerSystem.body observerSystem.referenceFrame observerSystem.time
                |> Option.map (fun t -> t.Trafo) 
                |> Option.defaultValue Trafo3d.Identity
            | _ -> Trafo3d.Identity
        
        originTrafo * rot * originTrafo.Inverse * refSysRotation.Inverse * trans * refSysRotation * spiceTrafo
    
    let fullTrafo (surf : aval<AdaptiveSurface>) (observedSystem : aval<SpiceReferenceSystem>) (observerSystem: aval<ObserverSystem>) (refsys : AdaptiveReferenceSystem) = 
        adaptive {
            let! s = surf
            let! s = s.Current
            let! rSys = refsys.Current
            let! observedSytem = observedSystem
            let! observerSystem = observerSystem
            
            return fullTrafo' s (Some observedSytem) (Some observerSystem) rSys
        }
    
    let fullTrafoForSurface (surf : AdaptiveSurface) (observedSystem : aval<SpiceReferenceSystem>) (observerSystem: aval<ObserverSystem>) (refsys : AdaptiveReferenceSystem) = 
        adaptive {
            let s = surf
            let! s = s.Current
            let! rSys = refsys.Current
            let! observedSystem = observedSystem
            let! observerSystem = observerSystem
            
            return fullTrafo' s (Some observedSystem) (Some observerSystem) rSys
        }
module DebugKdTreesX = 
   
    let getInvalidIndices3f (positions : V3f[]) =
        positions |> List.ofArray |> List.mapi (fun i x -> if x.AnyNaN then Some i else None) |> List.choose id    

    let getTriangleSet3f (vertices:V3f[]) =
        vertices
        |> Seq.map(fun x -> x.ToV3d())
        |> Seq.chunkBySize 3
        |> Seq.filter(fun x -> x.Length = 3)
        |> Seq.map(fun x -> Triangle3d x)
        |> Seq.filter(fun x -> (IntersectionController.triangleIsNan x |> not)) 
        |> Seq.toArray
        |> TriangleSet

    let getTriangleSet (indices : int[]) (vertices:V3d[]) = 
        indices 
        |> Seq.map(fun x -> vertices.[x])
        |> Seq.chunkBySize 3
        |> Seq.map(fun x -> Triangle3d(x))
        |> Seq.filter(fun x -> (IntersectionController.triangleIsNan x |> not)) 
        |> Seq.toArray
        |> TriangleSet

    let loadTriangles' (affine: Trafo3d) (araPath : string) =
        
        let positions = araPath |> Aara.fromFile<V3f>
                
        let invalidIndices = getInvalidIndices3f positions.Data |> List.toArray
        let size = positions.Size.XY.ToV2i()
        let indices = PRo3DCSharp.ComputeIndexArray(size, invalidIndices)
                  
       // Log.warn "num of inv_indices: %A" invalidIndices.Length
       // Log.warn "num of indices: %A" indices.Length
                       
        positions.Data 
        |> Array.map (fun x ->  x.ToV3d() |> affine.Forward.TransformPos) 
        |> getTriangleSet indices

    let loadTriangles (kd : LazyKdTree) =
        
        loadTriangles' kd.affine kd.objectSetPath 

    /// Lazy KdTrees with their triangles, loaded once per file for the whole process (#874).
    /// The callers' `HashMap` caches are copies threaded through by value: the hover pick
    /// (background thread) and click picks (update thread) each loaded a cold patch into their
    /// own copy, the last write-back dropped the other's, and `Picking` kept a copy only on a
    /// hit - so the same patch was read again and again, seconds each. The `Lazy` makes
    /// concurrent first uses wait for one load instead of starting their own.
    ///
    /// This store is the only place a loaded lazy tree is kept (`loadObjectSet` no longer puts
    /// them into the callers' caches), so dropping an entry frees it. An entry is dropped when it
    /// has not been used for `maxIdle`, and, least recently used first, while the loaded trees
    /// together exceed `budgetBytes`.
    type private LoadedKdTree(tree : Lazy<ConcreteKdIntersectionTree>) =
        member _.Tree = tree
        /// `Stopwatch` timestamp of the last use.
        member val LastUse = 0L with get, set
        /// Estimated size; 0 until loaded.
        member val Bytes = 0L with get, set

    let private envOr (name : string) (fallback : float) =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> fallback
        | s ->
            match Double.TryParse(s, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
            | true, v when v >= 0.0 -> v
            | _ ->
                Log.warn "[KdTree] ignoring %s=%s: not a non-negative number" name s
                fallback

    /// Loaded lazy KdTrees together may hold about this much (triangles plus the tree's file
    /// size). Environment: PRO3D_KDTREE_BUDGET_MB, default 2048.
    let mutable budgetBytes = int64 (envOr "PRO3D_KDTREE_BUDGET_MB" 2048.0 * 1024.0 * 1024.0)
    /// A loaded lazy KdTree unused this long is dropped. Environment: PRO3D_KDTREE_IDLE_MINUTES,
    /// default 10.
    let mutable maxIdle = TimeSpan.FromMinutes (envOr "PRO3D_KDTREE_IDLE_MINUTES" 10.0)

    let private loadedLazyKdTrees =
        System.Collections.Concurrent.ConcurrentDictionary<string, LoadedKdTree>()
    let private evictLock = obj ()

    let private estimateBytes (kdtreePath : string) (tree : ConcreteKdIntersectionTree) =
        let triangles =
            match tree.KdIntersectionTree.ObjectSet with
            | :? TriangleSet as t -> int64 t.Position3dList.Count * 24L // V3d per corner
            | _ -> 0L
        let treeFile = try IO.FileInfo(kdtreePath).Length with _ -> 0L
        triangles + treeFile

    /// Drops loaded trees, oldest use first, while they are idle longer than `maxIdle` or the
    /// loaded ones exceed `budgetBytes`. `keep` (the tree just loaded) stays, even if it alone is
    /// over the budget. Runs on every load and once a minute; O(n log n) in the loaded trees.
    let private evict (keep : string) =
        lock evictLock (fun () ->
            let now = Diagnostics.Stopwatch.GetTimestamp()
            let idleTicks = int64 (maxIdle.TotalSeconds * float Diagnostics.Stopwatch.Frequency)
            let loaded = loadedLazyKdTrees.ToArray() |> Array.filter (fun e -> e.Value.Bytes > 0L)
            Array.sortInPlaceBy (fun (e : Collections.Generic.KeyValuePair<string, LoadedKdTree>) -> e.Value.LastUse) loaded
            let mutable held = 0L
            for e in loaded do held <- held + e.Value.Bytes
            let mutable dropped = 0
            for e in loaded do
                if e.Key <> keep && (now - e.Value.LastUse > idleTicks || held > budgetBytes) then
                    if loadedLazyKdTrees.TryRemove e then
                        held <- held - e.Value.Bytes
                        dropped <- dropped + 1
            if dropped > 0 then
                Log.line "[KdTree] dropped %d loaded KdTrees; %d kept, %.0f MB" dropped (loaded.Length - dropped) (float held / 1048576.0))

    let private idleTimer =
        lazy (
            let tick _ =
                try evict ""
                with e -> Log.warn "[KdTree] dropping idle KdTrees failed: %s" e.Message
            new Threading.Timer(tick, null, TimeSpan.FromMinutes 1.0, TimeSpan.FromMinutes 1.0))

    let private loadLazyKdTree (kd : LazyKdTree) =
        let load () =
            Activity.track "kdtree" (fun () ->
                let tree = KdTrees.loadKdtree kd.kdtreePath
                Log.startTimed "loading kd tree: %s" kd.kdtreePath
                let triangles = kd |> loadTriangles
                Log.stop()
                tree.KdIntersectionTree.ObjectSet <- triangles
                tree)
        let entry = loadedLazyKdTrees.GetOrAdd(kd.kdtreePath, fun _ -> LoadedKdTree(lazy (load ())))
        let tree =
            try entry.Tree.Value
            with _ ->
                // a failed load is not kept: the next pick tries again
                loadedLazyKdTrees.TryRemove(Collections.Generic.KeyValuePair(kd.kdtreePath, entry)) |> ignore
                reraise ()
        entry.LastUse <- Diagnostics.Stopwatch.GetTimestamp()
        if entry.Bytes = 0L then
            // first use after the load; two racing threads may both get here, which is harmless
            entry.Bytes <- max 1L (estimateBytes kd.kdtreePath tree)
            idleTimer.Force() |> ignore
            evict kd.kdtreePath
        tree

    /// Forgets every loaded lazy KdTree: after a surface is removed, or its KdTrees rebuilt.
    let clearLoaded () = loadedLazyKdTrees.Clear()

    /// Drops what is idle or over the budget now, instead of at the next load or timer tick.
    let evictLoaded () = evict ""

    /// The number of loaded lazy KdTrees in the store.
    let loadedCount () = loadedLazyKdTrees.Count

    /// Lazy trees come from the process-wide store above and are not added to `cache`, so that
    /// dropping them from the store frees them; `cache` is passed through for the callers.
    let loadObjectSet (cache : HashMap<string, ConcreteKdIntersectionTree>) (lvl0Tree : Level0KdTree) =
        match lvl0Tree with
        | Aardvark.VRVis.Opc.KdTrees.InCoreKdTree kd ->
          kd.kdTree, cache
        | Aardvark.VRVis.Opc.KdTrees.LazyKdTree kd ->
            match kd.kdTree with
            | Some k -> k, cache
            | None -> loadLazyKdTree kd, cache

    let getTriangle (set : TriangleSet) (index : int) : Triangle3d =
        let pi = index * 3
        let pl = set.Position3dList
        Triangle3d(pl.[pi], pl.[pi+1], pl.[pi + 2])

    let isNotOversized (size) (triangle:Triangle3d) =      
      ((Vec.Distance(triangle.P0, triangle.P1) < size) && 
       (Vec.Distance(triangle.P0, triangle.P2) < size) &&
       (Vec.Distance(triangle.P1, triangle.P2) < size))

    let intersectKdTrees bb (hitObject : Surface) (cache : HashMap<string, ConcreteKdIntersectionTree>) (ray : FastRay3d) (kdTreeMap: HashMap<Box3d, KdTrees.Level0KdTree>) = 

        let kdtree, c = kdTreeMap |> HashMap.find bb |> loadObjectSet cache
        if isNull kdtree.KdIntersectionTree.ObjectSet then
            Log.warn "object set null"

        let kdi = kdtree.KdIntersectionTree 
        let mutable hit = ObjectRayHit.MaxRange
                        
        try
            let hitFilter = //true means being omitted
                fun (a:IIntersectableObjectSet) (b:int) _ _ -> 
                    let triangles = a :?> TriangleSet //TODO TO crashes if not encountering a triangleset
                    //b |> getTriangle triangles |> isNotOversized hitObject.triangleSize.value |> not // = tooBig       
                    false
            
            if PRo3D.Core.KdIntersection.intersect kdi ray null (Func<IIntersectableObjectSet,int,int, RayHit3d,bool>(hitFilter)) 0.0 Double.MaxValue &hit then              
                let info = hit.GetIntersectionRayHitInfo()
                Some (hit, hitObject),c
            else            
                None,c
        with 
          | e -> 
            Log.error "[DebugKdtrees] error in kdtree intersection" 
            Log.error "%A" e
            None,c

/// Result of a KdTree intersection, including patch identification info
/// needed for attribute extraction.
type KdTreeHitInfo = {
    hit       : ObjectRayHit
    surface   : Surface
    /// Bounding box key identifying which Level0KdTree (patch) was hit
    hitBBox   : Box3d
    /// The SgSurface that was hit (provides access to picking kdtrees and dataSource/PatchHierarchies)
    sgSurface : SgSurface
}

module SurfaceIntersection =

    /// OPC space (the space of the KdTree boxes and of `LazyKdTree.affine`'s output) to
    /// world space, for one surface. The forward direction is the one
    /// `doKdTreeIntersection` places its boxes with, so a query that walks the patch
    /// grids directly puts the surface where picking finds it.
    let surfaceTrafo
        (surf           : Surface)
        (refSys         : ReferenceSystem)
        (observedSystem : Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>) =

        let observedSystem, observerSystem =
            match observedSystem, observerSystem with
            | Some observed, Some observer -> Some observed, Some observer
            | _ -> None, None

        let fullTrafo = TransformationApp.fullTrafo' surf.transformation refSys observedSystem observerSystem

        if surf.transformation.flipZ then
            surf.preTransform * fullTrafo * Trafo3d.Scale(1.0, 1.0, -1.0)
        else if surf.transformation.isSketchFab then
            Sg.switchYZTrafo
        else
            surf.preTransform * fullTrafo

    /// The nearest hit of the ray on each surface `filterSurface` accepts (at most one per
    /// surface), and the updated KdTree cache. Shared by `doKdTreeIntersection` (picking: the
    /// highest priority wins) and `doKdTreeIntersectionNearest` (occlusion: the nearest wins).
    let private hitsPerSurface
        (m             : SurfaceModel)
        (refSys        : ReferenceSystem)
        (observedSystem : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (r             : FastRay3d) 
        (filterSurface : Guid -> Leaf -> SgSurface -> bool) 
        (loadedKdTrees : HashMap<string, ConcreteKdIntersectionTree>)
        (verboseLogging : bool)
        : list<KdTreeHitInfo> * HashMap<_,_> =

        let mutable loadedKdTrees = loadedKdTrees
        let activeSgSurfaces =
            m.surfaces.flat
            |> HashMap.toList
            |> List.choose (fun (id,leaf) ->
                match m.sgSurfaces |> HashMap.tryFind id with
                | Some s ->
                    if filterSurface id leaf s then Some (s, leaf)
                    else None
                | _ -> None
            )

        let hits =
            activeSgSurfaces
            |> List.map (fun (d, leaf) -> d, d.picking, Leaf.toSurface leaf)
            |> List.choose (fun (sgSurf, p, surf) ->
                match p with
                | Picking.NoPicking -> None
                | Picking.KdTree kd ->
                    if kd.IsEmpty then Log.error "no kdtree loaded for %s" surf.name; None
                    else
                        //if flipZ then
                        //    return Trafo3d.Scale(scaleFactor) * Trafo3d.Scale(1.0, 1.0, -1.0) * (fullTrafo * preTransform)
                        //else
                        //    return Trafo3d.Scale(scaleFactor) * (fullTrafo * preTransform)
                        let observedSystem, observerSystem =
                            match observedSystem surf.guid, observerSystem with
                            | Some observed, Some observer -> Some observed, Some observer
                            | _ -> None, None

                        let fullTrafo = TransformationApp.fullTrafo' surf.transformation refSys observedSystem observerSystem //SurfaceTransformations.fullTrafo' surf refSys
                        // once per surface, not per box: the same for every patch
                        let trafo =
                            if surf.transformation.flipZ then
                                Trafo3d.Scale(1.0, 1.0, -1.0).Forward * (fullTrafo.Forward * surf.preTransform.Forward)
                            else if surf.transformation.isSketchFab then
                                Sg.switchYZTrafo.Forward
                            else
                                (fullTrafo.Forward * surf.preTransform.Forward)
                        let backward =
                            if surf.transformation.flipZ then
                                Trafo3d.Scale(1.0, 1.0, -1.0).Backward * surf.preTransform.Backward * fullTrafo.Backward
                            else if surf.transformation.isSketchFab then
                                Sg.switchYZTrafo.Backward
                            else
                                surf.preTransform.Backward * fullTrafo.Backward
                        let localRay = r.Ray.Transformed(backward) |> FastRay3d

                        //get bbs that are hit
                        let hitBoxes =
                            kd
                            |> HashMap.toList |> List.map fst
                            |> List.filter(fun x ->
                                let mutable t = 0.0
                                x.Transformed(trafo).Intersects(r.Ray, &t)
                            )
                        //intersect corresponding kdtrees
                        let closestHit =
                            hitBoxes
                            |> List.choose(fun key ->
                                if verboseLogging then Log.startTimed "intersection: %s; pr: %f" surf.name surf.priority.value
                                let ray = localRay
                                let hit, c =
                                    kd |> DebugKdTreesX.intersectKdTrees key surf loadedKdTrees ray
                                if verboseLogging then Log.stop()
                                loadedKdTrees <- c
                                hit |> Option.map (fun (h, s) ->
                                    { hit = h; surface = s; hitBBox = key; sgSurface = sgSurf }
                                )
                            )
                            |> List.sortBy(fun info -> info.hit.RayHit.T)
                            |> List.tryHead

                        closestHit
                | Picking.PickMesh pm -> Log.error "no kdtree loaded for %s" surf.name; None
            )

        hits, loadedKdTrees

    /// Picking: the nearest hit among the surfaces of the highest priority that is hit at all.
    let doKdTreeIntersection
        (m             : SurfaceModel)
        (refSys        : ReferenceSystem)
        (observedSystem : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (r             : FastRay3d) 
        (filterSurface : Guid -> Leaf -> SgSurface -> bool) 
        (loadedKdTrees : HashMap<string, ConcreteKdIntersectionTree>)
        (verboseLogging : bool)
        : option<KdTreeHitInfo> * HashMap<_,_> =

        let hits, loadedKdTrees =
            hitsPerSurface m refSys observedSystem observerSystem r filterSurface loadedKdTrees verboseLogging
        let best =
            hits
            |> List.groupBy(fun info -> info.surface.priority.value) |> List.sortByDescending fst
            |> List.tryHead
            |> Option.bind(fun (_,b) -> b |> List.sortBy (fun info -> info.hit.RayHit.T) |> List.tryHead)
        best, loadedKdTrees

    /// Occlusion: the nearest hit over all accepted surfaces, whatever their priority - a
    /// nearer low-priority surface still hides what is behind it. One pass over the surfaces.
    let doKdTreeIntersectionNearest
        (m             : SurfaceModel)
        (refSys        : ReferenceSystem)
        (observedSystem : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (r             : FastRay3d) 
        (filterSurface : Guid -> Leaf -> SgSurface -> bool) 
        (loadedKdTrees : HashMap<string, ConcreteKdIntersectionTree>)
        : option<KdTreeHitInfo> * HashMap<_,_> =

        let hits, loadedKdTrees =
            hitsPerSurface m refSys observedSystem observerSystem r filterSurface loadedKdTrees false
        let nearest =
            hits |> List.fold (fun (best : Option<KdTreeHitInfo>) h ->
                match best with
                | Some b when b.hit.RayHit.T <= h.hit.RayHit.T -> best
                | _ -> Some h) None
        nearest, loadedKdTrees

