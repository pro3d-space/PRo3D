namespace PRo3D.Base.Geometry

open System
open Aardvark.Base

/// Snapping a picked point onto a nearby contact (#843): a contact drawn to end on another
/// one ends exactly on it, so the two meet in the map's topology instead of leaving a gap.
///
/// "Nearby" is measured as seen from the camera - an angle, i.e. a fixed number of pixels at
/// any zoom - and limited in 3D to the neighbourhood of the picked terrain point, so a contact
/// on the far side of the body that happens to project next to the cursor is never taken.
///
/// Pure: no rendering, no model.
module ContactSnap =

    /// Point of segment a-b closest to the line through `eye` along unit `dir`.
    let private closestToRay (eye : V3d) (dir : V3d) (a : V3d) (b : V3d) =
        let d = b - a
        let r = a - eye
        let dd = Vec.Dot(d, d)
        let dk = Vec.Dot(d, dir)
        let denom = dd - dk * dk
        let t =
            if denom <= 1e-18 then 0.0
            else clamp 0.0 1.0 ((dk * Vec.Dot(r, dir) - Vec.Dot(r, d)) / denom)
        a + d * t

    /// The point on `polylines` nearest to the picked `hit` as seen from `eye`, if it lies within
    /// `radius` (an angle, radians) of it and within `reach` (metres) of the hit in 3D.
    let tryPoint (eye : V3d) (radius : float) (reach : float) (polylines : V3d[][]) (hit : V3d) : Option<V3d> =
        let toHit = hit - eye
        let len = toHit.Length
        if not (len > 0.0) then None
        else
            let dir = toHit / len
            let mutable best = None
            let mutable bestAngle = radius
            for pl in polylines do
                for i in 0 .. pl.Length - 2 do
                    let a = pl.[i]
                    let b = pl.[i + 1]
                    // cheap reject: the whole segment far from the hit
                    let mid = (a + b) * 0.5
                    if Vec.Distance(mid, hit) <= reach + 0.5 * Vec.Distance(a, b) then
                        let q = closestToRay eye dir a b
                        let v = q - eye
                        let l = v.Length
                        if l > 0.0 && Vec.Distance(q, hit) <= reach then
                            let angle = atan2 (Vec.Cross(v, dir).Length) (Vec.Dot(v, dir))
                            if angle <= bestAngle then
                                bestAngle <- angle
                                best <- Some q
            best

    /// The snap radius as an angle: `pixels` at the view's horizontal field of view `hfov`
    /// (radians) over a viewport `width` pixels wide.
    let radiusOf (pixels : float) (hfov : float) (width : int) =
        pixels * hfov / float (max 1 width)
