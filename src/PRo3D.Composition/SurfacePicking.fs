namespace PRo3D.Composition

open System

open Aardvark.Base
open FSharp.Data.Adaptive
// before the PRo3D opens: OpcViewer.Base has its own `Planet`, which must not shadow PRo3D's
open OpcViewer.Base
open OpcViewer.Base.Picking
open Aardvark.Geometry

open PRo3D
open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Surface

/// Turning a click ray into a point on a surface, via the surfaces' KdTrees. Lifted from the
/// Viewer's `PickSurface` handler so every host picks - and re-projects annotation segments -
/// the same way.
module SurfacePicking =

    /// The KdTrees a host has loaded for intersection, by path. Loading is lazy and the
    /// intersection hands back the grown map, so the cache is mutable; each host instance owns
    /// exactly one (the Viewer used a module-level mutable before).
    type KdTreeCache() =
        member val Trees : HashMap<string, ConcreteKdIntersectionTree> = HashMap.empty with get, set

    /// Where surfaces are placed through SPICE (the GIS observation). Both must be present for
    /// a surface to be moved; a host without GIS passes `Observation.none`.
    type Observation =
        {
            observed : SurfaceId -> Option<SpiceReferenceSystem>
            observer : Option<ObserverSystem>
        }

    module Observation =
        let none = { observed = (fun _ -> None); observer = None }

    type SurfaceFilter = Guid -> Leaf -> SgSurface -> bool

    let onlyActive       : SurfaceFilter = fun _ l _ -> l.active
    let visibleAndActive : SurfaceFilter = fun _ l _ -> l.visible && l.active

    /// Sky projection casts DOWN from above the sample point onto the surface - origin
    /// p + up*d, direction -up. Casting from below (p - up*d, +up) only appeared to work while
    /// getUpVector returned garbage for small bodies (issue #628).
    let skyRay (planet : Planet) (p : V3d) =
        let up = CooTransformation.getUpVector p planet
        let reprojectionDistance =
            match planet with
            | Planet.Mars -> 1000000.0
            | _ -> 100.0
        FastRay3d(p + (up * reprojectionDistance), -up)

    /// The ray an annotation segment sample is re-projected along: from the camera
    /// (Viewpoint) or from the sky above the surface's body (Sky). Other projections have no
    /// re-projection ray.
    let projectionRay
        (projection  : Projection)
        (planetOf    : SurfaceId -> Planet)
        (surfaceId   : SurfaceId)
        (camLocation : V3d)
        (p           : V3d) : Option<FastRay3d> =
        match projection with
        | Projection.Viewpoint ->
            let dir = (p - camLocation).Normalized
            Some (FastRay3d(camLocation, dir))
        | Projection.Sky ->
            Some (skyRay (planetOf surfaceId) p)
        | _ ->
            None

    /// Closest KdTree hit of the surfaces passing `filter`.
    let intersect
        (cache    : KdTreeCache)
        (surfaces : SurfaceModel)
        (refSys   : ReferenceSystem)
        (obs      : Observation)
        (filter   : SurfaceFilter)
        (ray      : FastRay3d) : Option<KdTreeHitInfo> =
        let hit, trees =
            SurfaceIntersection.doKdTreeIntersection
                surfaces refSys obs.observed obs.observer ray filter cache.Trees PRo3D.Core.Config.diagnosticTimings
        cache.Trees <- trees
        hit

    /// The re-projection function a drawing tool samples annotation segments with.
    let hitFunction
        (cache       : KdTreeCache)
        (surfaces    : SurfaceModel)
        (refSys      : ReferenceSystem)
        (obs         : Observation)
        (filter      : SurfaceFilter)
        (projection  : Projection)
        (planetOf    : SurfaceId -> Planet)
        (surfaceId   : SurfaceId)
        (camLocation : V3d) : V3d -> Option<V3d> =
        fun p ->
            projectionRay projection planetOf surfaceId camLocation p
            |> Option.bind (fun ray ->
                intersect cache surfaces refSys obs filter ray
                |> Option.map (fun hitInfo -> ray.Ray.GetPointOnRay hitInfo.hit.RayHit.T)
            )

    /// World -> the surface's own SPICE frame, for surfaces placed by the GIS observation.
    let spiceTrafo (obs : Observation) (surfaceId : SurfaceId) =
        match obs.observed surfaceId, obs.observer with
        | Some observedSystem, Some observerSystem ->
            CooTransformation.transformBody observedSystem.body (Some observedSystem.referenceFrame) observerSystem.body observerSystem.referenceFrame observerSystem.time
            |> Option.map (fun t -> t.Trafo)
            |> Option.defaultValue Trafo3d.Identity
        | _ -> Trafo3d.Identity

    type PickResult =
        {
            surface  : Surface
            /// the hit in world space
            hit      : V3d
            /// re-projection for annotation segments, already mapped into the surface's frame
            hitF     : V3d -> Option<V3d>
            observed : Option<SpiceReferenceSystem>
        }

    /// Picks the closest surface along a click ray.
    let pickSurface
        (cache       : KdTreeCache)
        (surfaces    : SurfaceModel)
        (refSys      : ReferenceSystem)
        (obs         : Observation)
        (filter      : SurfaceFilter)
        (projection  : Projection)
        (planetOf    : SurfaceId -> Planet)
        (camLocation : V3d)
        (ray         : FastRay3d) : Option<PickResult> =
        intersect cache surfaces refSys obs filter ray
        |> Option.map (fun hitInfo ->
            let surf = hitInfo.surface
            // once per pick, not per re-projected sample
            let trafo = spiceTrafo obs surf.guid
            let toLocal (v : V3d) = trafo.Backward.TransformPos v
            {
                surface  = surf
                hit      = ray.Ray.GetPointOnRay(hitInfo.hit.RayHit.T)
                hitF     = hitFunction cache surfaces refSys obs filter projection planetOf surf.guid camLocation >> Option.map toLocal
                observed = obs.observed surf.guid
            }
        )
