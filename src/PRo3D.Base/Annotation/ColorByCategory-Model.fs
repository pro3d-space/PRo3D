namespace PRo3D.Base.Annotation

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D
open PRo3D.Base

open Chiron
open Adaptify

#nowarn "0686"

/// Attribute the annotation set can be colored by. Values are persisted, so existing
/// cases must keep their numbers; new ones get appended.
type ColorCategoryAttribute =
    // categorical
    | AnnotationType    = 0
    | Semantic          = 1
    | SurfaceName       = 2
    // numeric, from AnnotationResults
    | Slope             = 3
    | Bearing           = 4
    | Length            = 5
    | WayLength         = 6
    | Height            = 7
    | HeightDelta       = 8
    | AvgAltitude       = 9
    | Area              = 10
    // set on the annotation itself
    | Thickness         = 11
    // numeric, from DipAndStrikeResults
    | DipAngle          = 12
    | DipAzimuth        = 13
    | StrikeAzimuth     = 14

/// Whether the annotation set is colored by one of its own measurements or by a scalar
/// attribute (AARA layer) of the surface, sampled at the clicked points. Persisted as `int`.
type ColorAttributeKind =
    | AnnotationMeasurement = 0
    | SurfaceAttribute      = 1

/// How a sampled surface attribute paints an annotation. Persisted as `int`.
type SurfaceColoringMode =
    /// whole annotation (line + fill + dots) = the ramp color of the mean point value
    | Annotation = 0
    /// each control point by its own sampled value; line and fill drawn neutral gray
    | Pointwise  = 1
    /// dots per-point (as Pointwise) + line/fill = the mean color (as Annotation)
    | Both       = 2

/// One annotation's sampled surface-attribute values: one entry per control point
/// (`nan` where the ray missed or the layer had no value), plus their mean.
type SurfaceSampleEntry = { values : float[]; mean : float }

/// The result of a surface-attribute sampling pass over the whole annotation set. Transient
/// (never persisted): derived from surface geometry × annotation points, stale on any edit,
/// and not computable at load until surfaces have finished loading. `stamp` is the hash of
/// the inputs it was computed from (see `ColorByCategory.stampOf`); the panel compares a live
/// stamp against it to flag the "resample" button when the colors are out of date.
type SurfaceSampleStore = {
    layer   : string
    stamp   : int
    entries : HashMap<System.Guid, SurfaceSampleEntry>
}

module SurfaceSampleStore =
    let empty = { layer = ""; stamp = 0; entries = HashMap.empty }

/// Action lives here rather than in the app module because DrawingAction
/// (Drawing-Model.fs) has to reference it and that file compiles first —
/// same arrangement as FalseColorLegendApp.Action.
type ColorByCategoryAction =
    | ToggleEnabled
    | SetAttribute      of ColorCategoryAttribute
    | LegendMessage     of FalseColorLegendApp.Action
    | SetCategoryColor  of string * ColorPicker.Action
    | SetNoValueColor   of ColorPicker.Action
    | FitRangeToData
    | ResetCategoryColors
    | SetAttributeKind   of ColorAttributeKind
    | SetSurfaceLayer    of string
    | SetSurfaceColoring of SurfaceColoringMode
    | SetSurfaceSamples  of SurfaceSampleStore   // written by the Viewer after a sampling pass
    | ResampleSurface                            // pure request, intercepted by the Viewer

[<ModelType>]
type ColorByCategoryModel = {
    version        : int
    enabled        : bool
    attribute      : ColorCategoryAttribute

    /// ramp for numeric attributes — reuses the existing false-color machinery.
    /// Its `useFalseColors` field doubles as the panel's single "show legend" toggle,
    /// for the numeric bar and the categorical swatches alike.
    numericLegend  : FalseColorsModel

    /// explicit per-category overrides, keyed by `"<attribute>/<label>"`. Absent keys
    /// fall back to a deterministic palette pick, so this starts empty and never needs
    /// a pre-population pass.
    /// TreatAsValue: edits replace the whole map, which is exactly the invalidation a
    /// global recolor wants, and it keeps the token-based renderer path a single read.
    [<TreatAsValue>]
    categoryColors : HashMap<string, ColorInput>

    /// used when an annotation has no value for the attribute (NaN result, a
    /// non-ellipse asked for a diameter, an annotation with no planar fit, …)
    noValueColor   : ColorInput

    /// measurement of the annotation itself, or a scalar attribute of the surface under it
    attributeKind   : ColorAttributeKind

    /// label of the surface scalar layer picked in `SurfaceAttribute` mode (`""` = none)
    surfaceLayer    : string

    /// how a sampled surface attribute paints the annotation
    surfaceColoring : SurfaceColoringMode

    /// sampled surface-attribute values, filled by a Viewer-side sampling pass. Transient
    /// (not persisted). TreatAsValue: replaced wholesale on every resample, which is exactly
    /// the invalidation a global recolor wants and keeps the renderer path a single read.
    [<TreatAsValue>]
    surfaceSamples  : SurfaceSampleStore
}

module ColorByCategoryModel =

    let current = 0

    let initial = {
        version        = current
        enabled        = false
        attribute      = ColorCategoryAttribute.AnnotationType
        numericLegend  = FalseColorsModel.initDefinedScalarsLegend (Range1d(0.0, 1.0))
        categoryColors = HashMap.empty
        noValueColor   = { c = C4b.Gray }
        attributeKind   = ColorAttributeKind.AnnotationMeasurement
        surfaceLayer    = ""
        surfaceColoring = SurfaceColoringMode.Annotation
        surfaceSamples  = SurfaceSampleStore.empty
    }

    let readV0 =
        json {
            let! enabled        = Json.read "enabled"
            let! attribute      = Json.read "attribute"
            let! numericLegend  = Json.read "numericLegend"
            let! categoryColors = Json.read "categoryColors"
            let! noValueColor   = Json.read "noValueColor"
            // additive fields — absent in scenes written before surface-attribute coloring
            let! attributeKind   = Json.tryRead "attributeKind"
            let! surfaceLayer    = Json.tryRead "surfaceLayer"
            let! surfaceColoring = Json.tryRead "surfaceColoring"

            return {
                version        = current
                enabled        = enabled
                attribute      = attribute |> enum<ColorCategoryAttribute>
                numericLegend  = numericLegend
                categoryColors =
                    categoryColors
                    |> List.map (fun (k, c) -> k, ({ c = C4b.Parse c } : ColorInput))
                    |> HashMap.ofList
                noValueColor   = { c = C4b.Parse noValueColor }
                attributeKind   =
                    attributeKind
                    |> Option.map enum<ColorAttributeKind>
                    |> Option.defaultValue ColorAttributeKind.AnnotationMeasurement
                surfaceLayer    = surfaceLayer |> Option.defaultValue ""
                surfaceColoring =
                    surfaceColoring
                    |> Option.map enum<SurfaceColoringMode>
                    |> Option.defaultValue SurfaceColoringMode.Annotation
                surfaceSamples  = SurfaceSampleStore.empty
            }
        }

type ColorByCategoryModel with
    static member FromJson(_ : ColorByCategoryModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ColorByCategoryModel.readV0
            | _ ->
                return! v
                |> sprintf "don't know version %A of ColorByCategoryModel"
                |> Json.error
        }

    static member ToJson (x : ColorByCategoryModel) =
        json {
            do! Json.write "version"        x.version
            do! Json.write "enabled"        x.enabled
            do! Json.write "attribute"      (int x.attribute)
            do! Json.write "numericLegend"  x.numericLegend
            do! Json.write "categoryColors"
                    (x.categoryColors
                     |> HashMap.toList
                     |> List.map (fun (k, (c : ColorInput)) -> k, c.c.ToString()))
            do! Json.write "noValueColor"   (x.noValueColor.c.ToString())
            do! Json.write "attributeKind"   (int x.attributeKind)
            do! Json.write "surfaceLayer"    x.surfaceLayer
            do! Json.write "surfaceColoring" (int x.surfaceColoring)
            // surfaceSamples is transient — deliberately not written
        }
