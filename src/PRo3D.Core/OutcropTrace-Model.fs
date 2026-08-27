namespace PRo3D.Core

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives

open Adaptify

/// Outcrop traces: the lines where a modelled bedding sequence meets the terrain.
///
/// The sequence is defined by one *attitude* (dip + dip direction), derived from the current
/// annotation selection, plus a constant bed thickness. The renderer marks every surface
/// fragment lying within `traceWidth` of any plane of that sequence, so what appears on the
/// terrain is the outcrop pattern the sequence would make.
///
/// `traceWidth`, `traceSmoothing` and `projectionRadius` are conceptually
/// *scene* properties - how a particular outcrop should be read - and belong on `Scene` when
/// persistence is wanted. They are deliberately NOT user preferences: an outcrop with
/// decimetre bedding wants different numbers from one with ten-metre units, and those numbers
/// belong to the outcrop rather than to whoever opens it. The whole model is transient for
/// now, because the feature is driven by the annotation selection and that selection is not
/// persisted either (see Groups-Model.fs).
[<ModelType>]
type OutcropTraceModel = {
    enabled          : bool
    /// Contribute polyline annotations' fitted planes. Off by default: a polyline's plane
    /// fit is poorly conditioned about the line axis.
    usePolyline      : bool
    /// Contribute dip-and-strike annotations' fitted planes.
    useDnS           : bool
    /// True (stratigraphic) thickness between successive beds, in metres.
    /// Zero collapses the sequence to a single plane through the reference point.
    bedThickness     : NumericInput
    /// Full width of the drawn band, in metres, measured perpendicular to the plane.
    traceWidth       : NumericInput
    /// Smoothstep falloff either side of the band, in metres.
    traceSmoothing   : NumericInput
    /// How far the measured attitude is extrapolated, in metres, measured from the centre
    /// of the selection. Set directly; `FitProjectionRadius` seeds it from the selection's
    /// own footprint.
    projectionRadius : NumericInput
    color            : ColorInput
}

module OutcropTraceModel =

    let private numeric v mi ma st fmt : NumericInput =
        { value = v; min = mi; max = ma; step = st; format = fmt }

    let initial : OutcropTraceModel = {
        enabled          = false
        usePolyline      = false
        useDnS           = true
        bedThickness     = numeric   1.0   0.0 10000.0 0.1  "{0:0.00}"
        traceWidth       = numeric   0.25  0.01  100.0 0.05 "{0:0.00}"
        traceSmoothing   = numeric   0.1   0.0   100.0 0.05 "{0:0.00}"
        projectionRadius = numeric  50.0   1.0 100000.0 5.0 "{0:0}"
        color            = { c = C4b.Red }
    }
