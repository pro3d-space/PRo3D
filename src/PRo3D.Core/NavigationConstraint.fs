namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Rendering
open PRo3D.Base

/// Reference-frame helpers shared by the navigation gizmo (`NavigationGizmo.fs`)
/// and the axis-lock camera constraint applied in `Navigation.update`.
///
/// This lives in `PRo3D.Core` because `PRo3D.Viewer` compiles `Navigation.fs`
/// *before* `NavigationGizmo.fs`, so the gizmo cannot own code the navigation
/// dispatcher needs. The gizmo `open`s this module and delegates to it, so the
/// six gizmo dots and the locked axle are always the same directions.
module NavigationConstraint =

    /// Small bodies are navigated from outside and their frame is the body-fixed
    /// one - the same predicate and rationale as `ReferenceSystem.bodyAwareSky`
    /// and the gizmo's own `usesBodyFixedFrame`.
    let usesBodyFixedFrame (planet : Planet) = CooTransformation.isSmallBody planet

    /// ENU basis from the reference-system up/north. `east = north x up`, matching
    /// `PRo3D.Core.Sg.getOrientationSystem` and the in-scene reference cross.
    let enuBasis (up : V3d) (north : V3d) : V3d * V3d * V3d =
        let u = up.Normalized
        let n = north.Normalized
        let e = (Vec.cross n u).Normalized
        e, n, u

    /// The (east, north, up) triple the lockable axes are built from: body-fixed
    /// (X = north, Y = east, Z = up) for the small bodies, the local tangent frame
    /// otherwise. Identical to `NavigationGizmo`'s private `frameOf`.
    let frameOf (planet : Planet) (up : V3d) (north : V3d) : V3d * V3d * V3d =
        if usesBodyFixedFrame planet then V3d.OIO, V3d.IOO, V3d.OOI
        else enuBasis up north

    /// World-space unit direction of the `+` side of a lockable axis, from raw
    /// planet / up / north values. `Navigation.update` is generic over the config
    /// record and only holds the up/north/planet lenses, so it cannot build a
    /// `ReferenceSystem`; this is the form it calls.
    let axisWorldDirection (planet : Planet) (up : V3d) (north : V3d) (axis : NavigationAxis) : V3d =
        let e, n, u = frameOf planet up north
        (match axis with
         | NavigationAxis.NorthSouth -> n
         | NavigationAxis.EastWest   -> e
         | NavigationAxis.UpDown     -> u).Normalized

    /// Same, from a `ReferenceSystem`. Uses `northO` (the north-offset-applied
    /// direction), matching `NavigationGizmo.resolveAxisWorldDir`.
    let getAxisWorldDirection (rs : ReferenceSystem) (axis : NavigationAxis) : V3d =
        axisWorldDirection rs.planet rs.up.value rs.northO axis

    /// Collapse the camera rotation `before -> after` to its component about
    /// `axis` (swing-twist decomposition: keep the twist about `axis`, drop the
    /// swing). Position is placed back on the ray from `pivot` through the twisted
    /// pre-update position, at the post-update distance from `pivot`, so dolly /
    /// zoom done in the same step is preserved.
    ///
    /// Degenerate cases: `|axis|` ~ 0 -> `after` unchanged; `before` ~ `after` ->
    /// twist ~ identity and the radius still comes from `after` (a pure dolly step
    /// survives); a pure swing perpendicular to `axis` (the user dragging exactly
    /// along the frozen direction, including a ~180 deg step) -> twist frozen to
    /// identity, i.e. the camera does not move; camera sitting on `pivot` ->
    /// keep `after`'s location.
    let constrainRotationToAxis
        (before : CameraView) (after : CameraView) (axis : V3d) (pivot : V3d) : CameraView =

        let eps = 1e-9
        let axisLen = Vec.length axis
        if axisLen < eps then after
        else
            let k = axis / axisLen

            // Relative rotation of the camera frame, before -> after, as a unit
            // quaternion. Built from two RotateInto steps (align the view
            // direction, then null the residual roll about it) so it never needs
            // an exactly-orthonormal basis; navigation deltas are small, so
            // neither step hits its antiparallel singularity.
            let f0 = Vec.normalize before.Forward
            let f1 = Vec.normalize after.Forward
            let q1 = Rot3d.RotateInto(f0, f1)
            let u0 = Vec.normalize (q1.Transform (Vec.normalize before.Up))
            let u1 = Vec.normalize after.Up
            let q2 = Rot3d.RotateInto(u0, u1)
            let delta = (q2 * q1).Normalized

            // Swing-twist: the twist quaternion about k is (w, (v . k) k)
            // normalised; its rotation angle is 2 * atan2(v . k, w).
            let proj = Vec.dot delta.V k
            let twist =
                if delta.W * delta.W + proj * proj < eps then Rot3d.Identity
                else Rot3d.Rotation(k, 2.0 * atan2 proj delta.W)

            let fwd'   = twist.Transform f0
            let up'    = twist.Transform (Vec.normalize before.Up)
            let right' = twist.Transform (Vec.normalize before.Right)

            let offBefore = before.Location - pivot
            let rB = Vec.length offBefore
            let rA = Vec.length (after.Location - pivot)
            let loc' =
                if rB < eps then after.Location
                else pivot + Vec.normalize (twist.Transform offBefore) * rA

            CameraView(before.Sky, loc', fwd', up', right')
