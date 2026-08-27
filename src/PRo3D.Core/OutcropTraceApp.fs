namespace PRo3D.Core

open System

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives

open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation

/// Shape of the pole distribution of a selection, from the eigenvalue spectrum of its
/// orientation tensor (see `OutcropTrace.meanAttitude`).
type AttitudeShape =
    /// One dominant attitude; a mean plane is meaningful.
    | Cluster
    /// No dominant attitude at all - the poles are scattered.
    | NoDominantAttitude
    /// The poles lie on a great circle: the selection is folded, so no single attitude
    /// represents it. Carries the fold axis (the pi-axis), the eigenvector of the
    /// *smallest* eigenvalue.
    | Girdle of foldAxis : V3d

/// The combined attitude of a set of annotations, in world space.
type MeanAttitude = {
    /// Mean plane through `anchor`. Only meaningful when `shape = Cluster`.
    plane   : Plane3d
    /// Mean centre of mass of the contributing annotations. For a bedding *sequence* this
    /// is only the phase of the family - which offsets its planes land on.
    anchor  : V3d
    /// Normalised eigenvalues S1 >= S2 >= S3 of the orientation tensor, summing to 1.
    s       : V3d
    /// Max distance from `anchor` to a contributing centre of mass. Zero for one annotation.
    spread  : float
    count   : int
    shape   : AttitudeShape
}

/// Combining a selection of fitted planes into one attitude, and putting that attitude into
/// view space for the shader.
///
/// Everything here is pure and free of adaptive plumbing so the tests can reach it without a
/// GL context - the same split `RoseDiagram.includes` makes for the rose diagram.
module OutcropTrace =

    /// Below this largest normalised eigenvalue there is no dominant attitude at all.
    /// Calibrated against synthetic populations (tools/analysis): uniformly random poles
    /// reach S1 = 0.43 +- 0.035 and topped out at 0.55 over 200 trials, while a noisy but
    /// usable field set (pole scatter sigma 30-40 deg) sits at 0.70-0.79. This sits in the gap.
    ///
    /// Deliberately NOT the rose diagram's `minResultant`: that guards a circular mean of
    /// azimuths, a different quantity. Two beds dipping 5 deg in opposite directions have an
    /// azimuth resultant of exactly zero and a perfectly well defined mean plane.
    [<Literal>]
    let minS1 = 0.65

    /// Ratio threshold used twice, for the two ways a spectrum can fail to be a cluster.
    ///
    /// `S2/S1 < this` (with S1 above `minS1`) is a cluster: one dominant attitude.
    /// Otherwise `S3/S2 < this` means the poles are confined to a plane - a girdle, i.e. a
    /// fold - as opposed to being scattered over the sphere.
    ///
    /// Checking the girdle *before* the S1 floor matters and is not obvious: a girdle
    /// necessarily has a low S1, because its poles are spread around a great circle. A
    /// two-limb fold 180 deg apart sits at S1 = 0.59, below `minS1`, and would be reported
    /// as "no dominant attitude" if S1 were tested first - true, but far less useful than
    /// naming the fold. S3/S2 is what separates the two: 0.001 for that fold, 0.64 for
    /// uniformly random poles.
    [<Literal>]
    let maxGirdleRatio = 0.3

    /// One annotation's contribution: its fitted plane and where that fit sits.
    type Contribution = {
        plane  : Plane3d
        center : V3d
    }

    /// Whether an annotation contributes to the combined attitude. Mirrors
    /// `RoseDiagram.includes`, so the two features agree on what "the selection" means.
    let includes (usePolyline : bool) (useDnS : bool) (geometry : Geometry) (plane : Plane3d) =
        let n = plane.Normal
        (not n.IsNaN) && n.LengthSquared > 0.0 &&
        ((geometry = Geometry.Polyline && usePolyline) ||
         (geometry = Geometry.DnS      && useDnS))

    /// Orientation tensor T = (1/N) * sum(n n^T) of a set of unit normals.
    ///
    /// The outer product is what makes this work on *axial* data: a pole and its antipode
    /// describe the same plane, and n n^T = (-n)(-n)^T, so the sign of each fitted normal is
    /// irrelevant. Summing the normals themselves instead would cancel measurements of one
    /// near-vertical bed against each other - `DipAndStrikeResults.plane` is stored exactly
    /// as the regression produced it, uncorrected.
    let orientationTensor (normals : array<V3d>) =
        let mutable t = M33d.Zero
        for n in normals do
            let n = n.Normalized
            t <- t + M33d(n.X*n.X, n.X*n.Y, n.X*n.Z,
                          n.Y*n.X, n.Y*n.Y, n.Y*n.Z,
                          n.Z*n.X, n.Z*n.Y, n.Z*n.Z)
        t * (1.0 / float normals.Length)

    /// Combine a selection into one attitude. `None` when there is nothing to combine.
    ///
    /// The result always carries its `shape`; callers decide whether to draw. `plane` is
    /// only meaningful for `Cluster`.
    let meanAttitude (contributions : array<Contribution>) : Option<MeanAttitude> =
        if contributions.Length = 0 then None
        else
            let normals = contributions |> Array.map (fun c -> c.plane.Normal)
            let t = orientationTensor normals

            // T is symmetric positive semi-definite, so its SVD is its eigendecomposition:
            // u.C0 is the principal eigenvector and s.Diagonal holds the eigenvalues in
            // descending order. Same call LinearRegression3d.TryGetRegressionInfo makes.
            match SVD.Decompose t with
            | None -> None
            | Some (u, s, _) ->
                let ev = s.Diagonal
                let anchor =
                    let sum = contributions |> Array.fold (fun acc c -> acc + c.center) V3d.Zero
                    sum / float contributions.Length
                let spread =
                    contributions
                    |> Array.fold (fun acc c -> max acc (Vec.distance anchor c.center)) 0.0

                let normal = u.C0.Normalized
                // guard against a degenerate spectrum before dividing
                let s1 = max ev.X 1e-12
                let s2 = max ev.Y 1e-12
                let shape =
                    if ev.X > minS1 && ev.Y / s1 < maxGirdleRatio then Cluster
                    elif ev.Z / s2 < maxGirdleRatio then Girdle u.C2.Normalized
                    else NoDominantAttitude

                Some {
                    plane  = Plane3d(normal, anchor)
                    anchor = anchor
                    s      = ev
                    spread = spread
                    count  = contributions.Length
                    shape  = shape
                }

    /// Minimum radius a *Fit to selection* will produce, in metres. What sizes a single
    /// annotation, whose spread is zero.
    [<Literal>]
    let fitMinimumRadius = 25.0

    /// Headroom a *Fit to selection* leaves beyond the selection's own footprint, so the
    /// traces reach a little past the outermost measurement rather than stopping on it.
    [<Literal>]
    let fitHeadroom = 1.5

    /// The radius a *Fit to selection* proposes for this attitude. The user then owns the
    /// value: extrapolating further than the measurements support is a judgement call, and
    /// making it explicit is the point (see docs/OutcropTraces.md).
    let fitProjectionRadius (attitude : MeanAttitude) =
        (max attitude.spread fitMinimumRadius) * fitHeadroom

    /// Plane and extent in *view space*, ready to upload as `float32` uniforms.
    ///
    /// Composed here on the CPU in double precision. A world-space test in the shader would
    /// be a float32 dot product against coordinates of ~3.4e6 m on Mars - about 0.25 m of
    /// representable resolution, which is noise next to a 0.25 m trace width. View space is
    /// camera-relative, so at 10 km the resolution is ~1 mm. See ai/CONVENTIONS.md.
    ///
    /// Returns (V4f(normal, d), V4f(anchor, radius)); the signed distance of a view-space
    /// point x is `dot(normal, x) - d`. Takes the view trafo rather than a `CameraView` so
    /// callers pass whichever view is actually rendering the pass.
    let viewSpaceAttitude (viewTrafo : Trafo3d) (radius : float) (attitude : MeanAttitude) =
        let mv = viewTrafo.Forward
        let nView : V3d = mv.TransformDir attitude.plane.Normal |> Vec.normalize
        // any point on the plane gives the same d; the projected anchor keeps the
        // intermediate near the data rather than near the body centre
        let p0 : V3d = attitude.anchor - attitude.plane.Normal * attitude.plane.Height attitude.anchor
        let dView : float = Vec.dot nView (mv.TransformPos p0)
        let aView : V3d = mv.TransformPos attitude.anchor
        V4f(V3f nView, float32 dView), V4f(V3f aView, float32 radius)

    /// Dip angle and dip direction of the combined attitude, in the given frame. Shared with
    /// the Dip&Strike panel via `DipAndStrike.attitudeFromNormal`, so the two cannot disagree.
    let dipAndDipDirection (up : V3d) (north : V3d) (attitude : MeanAttitude) =
        let a = DipAndStrike.attitudeFromNormal up north attitude.plane.Normal
        a.dipAngle, a.dipAzimuth

    /// Trend/plunge of a fold axis, for the girdle message.
    let trendAndPlunge (up : V3d) (north : V3d) (axis : V3d) =
        let axis = if Vec.dot axis up < 0.0 then -axis else axis
        let plunge = Math.Asin(Fun.Clamp(Vec.dot axis up, -1.0, 1.0)).DegreesFromRadians()
        let trend = Calculations.computeAzimuth axis north up
        trend, plunge

type OutcropTraceAction =
    | ToggleEnabled
    | SetUsePolyline      of bool
    | SetUseDnS           of bool
    | SetBedThickness     of Numeric.Action
    | SetTraceWidth       of Numeric.Action
    | SetTraceSmoothing   of Numeric.Action
    | SetProjectionRadius of Numeric.Action
    | SetColor            of ColorPicker.Action
    /// Re-seed the bed thickness from the current projection radius.
    | FitBedThickness     of float
    /// Re-seed the projection radius from the current selection's footprint.
    | FitProjectionRadius of float

module OutcropTraceApp =

    /// Roughly this many traces across the projection radius when the thickness is fitted.
    [<Literal>]
    let private tracesWhenFitted = 8.0

    let update (model : OutcropTraceModel) (action : OutcropTraceAction) =
        match action with
        | ToggleEnabled ->
            { model with enabled = not model.enabled }
        | SetUsePolyline v ->
            { model with usePolyline = v }
        | SetUseDnS v ->
            { model with useDnS = v }
        | SetBedThickness a ->
            { model with bedThickness = Numeric.update model.bedThickness a }
        | SetTraceWidth a ->
            { model with traceWidth = Numeric.update model.traceWidth a }
        | SetTraceSmoothing a ->
            { model with traceSmoothing = Numeric.update model.traceSmoothing a }
        | SetProjectionRadius a ->
            { model with projectionRadius = Numeric.update model.projectionRadius a }
        | SetColor a ->
            { model with color = ColorPicker.update model.color a }
        | FitBedThickness radius ->
            let v = Fun.Clamp(radius * 2.0 / tracesWhenFitted, model.bedThickness.min, model.bedThickness.max)
            { model with bedThickness = { model.bedThickness with value = v } }
        | FitProjectionRadius radius ->
            let v = Fun.Clamp(radius, model.projectionRadius.min, model.projectionRadius.max)
            { model with projectionRadius = { model.projectionRadius with value = v } }

    /// Scene-level appearance settings, shown on the config page next to Frustum and
    /// Coordinate System - which are scene state too.
    let viewSettings (model : AdaptiveOutcropTraceModel) =
        require GuiEx.semui (
            Html.table [
                Html.row "Trace Width (m):"     [Numeric.view' [InputBox] model.traceWidth       |> UI.map SetTraceWidth]
                Html.row "Trace Smoothing (m):" [Numeric.view' [InputBox] model.traceSmoothing   |> UI.map SetTraceSmoothing]

            ]
        )
