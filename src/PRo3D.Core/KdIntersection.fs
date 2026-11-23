namespace PRo3D.Core

open System
open Aardvark.Base
open Aardvark.Geometry

/// Ray queries against a `KdIntersectionTree` that can hit small triangles.
///
/// Aardvark.Base's ray-triangle test (`Ray3d.HitsTriangle`, Ray3_template.cs) drops a hit when
/// `det = edge01 · (direction × edge02)` lies within an absolute ±1e-7. For a unit direction,
/// det ≈ edge², so every triangle with edges below ≈ 3.2e-4 file units is unhittable: a
/// shape model in kilometres with facets finer than ~30 cm (the 5 cm DART Dimorphos patch)
/// cannot be picked at all. det also scales with |direction|, so the query runs with a
/// lengthened direction and the hit parameter is scaled back. Callers keep seeing `t` along
/// their own ray. Remove once aardvark.base compares det against a relative bound:
/// https://github.com/aardvark-platform/aardvark.base/issues/169 (removal: pro3d-space/PRo3D#828)
/// See docs/KdTrees.md.
module KdIntersection =

    /// Lifts det by 1e6: hittable edges go down from ~3.2e-4 to ~3.2e-7 file units.
    [<Literal>]
    let directionScale = 1.0e6

    /// `KdIntersectionTree.Intersect`, with the same arguments and the same meaning of
    /// `tmin`, `tmax` and `hit.RayHit.T` (the bound on entry, the result on return).
    let intersect
            (kdi          : KdIntersectionTree)
            (ray          : FastRay3d)
            (objectFilter : Func<IIntersectableObjectSet, int, bool>)
            (hitFilter    : Func<IIntersectableObjectSet, int, int, RayHit3d, bool>)
            (tmin         : float)
            (tmax         : float)
            (hit          : byref<ObjectRayHit>) : bool =
        let scaled = FastRay3d(Ray3d(ray.Ray.Origin, ray.Ray.Direction * directionScale))
        let bound = hit.RayHit.T
        let mutable rayHit = hit.RayHit
        rayHit.T <- bound / directionScale
        hit.RayHit <- rayHit
        let found =
            kdi.Intersect(scaled, objectFilter, hitFilter, tmin / directionScale, tmax / directionScale, &hit)
        let mutable rayHit = hit.RayHit
        rayHit.T <- if found then rayHit.T * directionScale else bound
        hit.RayHit <- rayHit
        found
