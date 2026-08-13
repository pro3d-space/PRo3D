namespace PRo3D.Base.Annotation

open System

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base

type ExportFormat =
    | Csv     = 0
    | GeoJson = 1
    /// fixed schema dictated by the external structural-geology tool
    | Attitude = 2

/// How many records one annotation produces.
type ExportGranularity =
    /// one row / feature per annotation. Carries a single coordinate — the
    /// bounding-box centre — so the individual vertices are NOT in the output.
    | PerAnnotation = 0
    /// one row / feature per point of every exported annotation
    | PerPoint      = 1

type ExportScope =
    | All      = 0
    | Visible  = 1
    | Selected = 2

type CoordinateMode =
    | Cartesian  = 0
    | Geographic = 1
    | Both       = 2

/// Longitude handling. The bodies PRo3D deals with do not agree on a single
/// convention, and the exporters this replaces each picked a different one
/// silently, so it is now an explicit choice.
type LongitudeConvention =
    /// exactly what CooTransformation returns for the body
    | Native  = 0
    /// 360 - longitude, wrapped to [0, 360) — what the CSV and GeoJSON exports did
    | Flipped = 1
    /// like Flipped, but wrapped to (-180, 180]
    | Signed  = 2

type ExportPreset =
    | Custom          = 0
    | QgisFeatures    = 1
    | AnnotationTable = 2
    | Profile         = 3
    | AttitudePlanes  = 4

/// Immutable snapshot handed to the record builder and the writers. Contains no
/// adaptive types and no reference to the surface model, so it can live in
/// PRo3D.Base alongside the writers.
type AnnotationExportSettings = {
    format            : ExportFormat
    granularity       : ExportGranularity
    scope             : ExportScope
    coordinates       : CoordinateMode
    longitude         : LongitudeConvention
    /// export the densely sampled, surface-following polyline
    /// (Annotation.retrievePoints) instead of only the picked control points
    useSampledPoints  : bool
    annotationFields  : list<AnnotationField>
    pointFields       : list<PointField>
}

module ExportPreset =

    let all =
        [ ExportPreset.Custom; ExportPreset.QgisFeatures; ExportPreset.AnnotationTable
          ExportPreset.Profile; ExportPreset.AttitudePlanes ]

    let label (preset : ExportPreset) =
        match preset with
        | ExportPreset.Custom          -> "Custom"
        | ExportPreset.QgisFeatures    -> "GIS / QGIS"
        | ExportPreset.AnnotationTable -> "Annotation table"
        | ExportPreset.Profile         -> "Profile"
        | ExportPreset.AttitudePlanes  -> "Attitude planes"
        | _                            -> string preset

    let description (preset : ExportPreset) =
        match preset with
        | ExportPreset.Custom ->
            "Compose the export yourself."
        | ExportPreset.QgisFeatures ->
            "Spec-shaped GeoJSON FeatureCollection, one feature per annotation, geographic coordinates."
        | ExportPreset.AnnotationTable ->
            "One CSV row per annotation with its measurements and its centre coordinate."
        | ExportPreset.Profile ->
            "One CSV row per point of the selected annotation, with distances and surface attributes."
        | ExportPreset.AttitudePlanes ->
            "Dip & strike planes for external structural-geology tools. Fixed schema."
        | _ -> ""

module AnnotationExportSettings =

    /// Everything except the identity fields, which would bloat a first-time
    /// export; the user can switch them on.
    let private measurementFields =
        AnnotationFields.all
        |> List.filter (fun f ->
            match AnnotationFields.groupOf f with
            | Identity -> false
            | _        -> true)

    let initial = {
        format            = ExportFormat.Csv
        granularity       = ExportGranularity.PerAnnotation
        scope             = ExportScope.Visible
        coordinates       = CoordinateMode.Both
        longitude         = LongitudeConvention.Flipped
        useSampledPoints  = true
        annotationFields  =
            [ AnnotationField.Key; AnnotationField.Text; AnnotationField.GroupName
              AnnotationField.SurfaceName; AnnotationField.Geometry; AnnotationField.Semantic ]
            @ measurementFields
        pointFields       = AnnotationFields.allPointFields
    }

    /// A preset only pre-fills the settings; every individual control stays
    /// editable afterwards (which flips the preset back to `Custom`).
    let applyPreset (preset : ExportPreset) (settings : AnnotationExportSettings) =
        match preset with
        | ExportPreset.QgisFeatures ->
            { settings with
                format           = ExportFormat.GeoJson
                granularity      = ExportGranularity.PerAnnotation
                coordinates      = CoordinateMode.Geographic
                annotationFields =
                    [ AnnotationField.Key; AnnotationField.Text; AnnotationField.GroupName
                      AnnotationField.SurfaceName; AnnotationField.Geometry; AnnotationField.Semantic
                      AnnotationField.Color; AnnotationField.Length; AnnotationField.WayLength
                      AnnotationField.Area; AnnotationField.DipAngle; AnnotationField.DipAzimuth
                      AnnotationField.StrikeAzimuth ] }
        | ExportPreset.AnnotationTable ->
            { settings with
                format           = ExportFormat.Csv
                granularity      = ExportGranularity.PerAnnotation
                coordinates      = CoordinateMode.Both
                annotationFields = initial.annotationFields }
        | ExportPreset.Profile ->
            { settings with
                format           = ExportFormat.Csv
                granularity      = ExportGranularity.PerPoint
                scope            = ExportScope.Selected
                coordinates      = CoordinateMode.Both
                useSampledPoints = true
                annotationFields = [ AnnotationField.Key; AnnotationField.Text; AnnotationField.SurfaceName ]
                pointFields      = AnnotationFields.allPointFields }
        | ExportPreset.AttitudePlanes ->
            { settings with format = ExportFormat.Attitude }
        | _ -> settings

    let fileExtension (format : ExportFormat) =
        match format with
        | ExportFormat.Csv -> "csv"
        | _                -> "json"

    let formatLabel (format : ExportFormat) =
        match format with
        | ExportFormat.Csv      -> "CSV table (*.csv)"
        | ExportFormat.GeoJson  -> "GeoJSON (*.json)"
        | ExportFormat.Attitude -> "Attitude planes (*.json)"
        | _                     -> string format

    let granularityLabel (granularity : ExportGranularity) =
        match granularity with
        | ExportGranularity.PerAnnotation -> "one record per annotation"
        | _                               -> "one record per point"

    let scopeLabel (scope : ExportScope) =
        match scope with
        | ExportScope.All      -> "All annotations"
        | ExportScope.Visible  -> "Visible annotations only"
        | _                    -> "Selected annotations only"

    let coordinateLabel (mode : CoordinateMode) =
        match mode with
        | CoordinateMode.Cartesian  -> "Cartesian (x, y, z)"
        | CoordinateMode.Geographic -> "Geographic (lat, lon, alt)"
        | _                         -> "Both"

    let longitudeLabel (convention : LongitudeConvention) =
        match convention with
        | LongitudeConvention.Native  -> "Native (as returned for the body)"
        | LongitudeConvention.Flipped -> "Flipped, 360 - lon, [0, 360)"
        | _                           -> "Flipped and signed, (-180, 180]"

    /// True when the format ignores every setting below the format dropdown.
    let hasFixedSchema (format : ExportFormat) =
        format = ExportFormat.Attitude
