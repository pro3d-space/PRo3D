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
    /// Line-delimited GeoJSON, rewritten automatically whenever the annotations
    /// change. Picking it arms the background export rather than writing once;
    /// its schema is fixed because downstream tools poll the file.
    | ContinuousGeoJson = 3

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
/// convention — planetographic longitude is west-positive for prograde bodies
/// while most basemaps are east-positive, and prime meridians differ — and the
/// exporters this replaces each picked a different one silently. So it is an
/// explicit choice, and the result is always wrapped into [0, 360) before the
/// separate range setting decides the notation.
type LongitudeConvention =
    /// exactly what CooTransformation returns for the body
    | Native         = 0
    /// 360 - longitude: mirrors east against west.
    /// What the CSV and plain GeoJSON exports did.
    | Flipped        = 1
    // 2 was `Shifted` (lon + 180). Removed once the signed range turned out to
    // be what the QGIS output actually needed; the value is left unused rather
    // than reassigned, so nothing silently changes meaning.
    /// 180 - longitude: mirrored *and* shifted
    | FlippedShifted = 3

type ExportPreset =
    | Custom            = 0
    | QgisFeatures      = 1
    | AnnotationTable   = 2
    | Profile           = 3
    | AttitudePlanes    = 4
    | ContinuousGeoJson = 5

/// Immutable snapshot handed to the record builder and the writers. Contains no
/// adaptive types and no reference to the surface model, so it can live in
/// PRo3D.Base alongside the writers.
type AnnotationExportSettings = {
    format            : ExportFormat
    granularity       : ExportGranularity
    scope             : ExportScope
    coordinates       : CoordinateMode
    longitude         : LongitudeConvention
    /// write longitudes as (-180, 180] instead of [0, 360)
    signedLongitude   : bool
    /// export the densely sampled, surface-following polyline
    /// (Annotation.retrievePoints) instead of only the picked control points
    useSampledPoints  : bool
    /// sample the surface properties — the OPC scalar / texture layers — under
    /// every exported point and add one column per layer. Per-point exports
    /// only. Costly (a ray cast plus a texture lookup per point), which is why
    /// it is off by default and no preset switches it on.
    sampleSurfaceProperties : bool
    annotationFields  : list<AnnotationField>
    pointFields       : list<PointField>
}

module ExportPreset =

    let all =
        [ ExportPreset.Custom; ExportPreset.QgisFeatures; ExportPreset.AnnotationTable
          ExportPreset.Profile; ExportPreset.AttitudePlanes; ExportPreset.ContinuousGeoJson ]

    let label (preset : ExportPreset) =
        match preset with
        | ExportPreset.Custom            -> "Custom"
        | ExportPreset.QgisFeatures      -> "GIS / QGIS"
        | ExportPreset.AnnotationTable   -> "Annotation table"
        | ExportPreset.Profile           -> "Profile"
        | ExportPreset.AttitudePlanes    -> "Attitude planes"
        | ExportPreset.ContinuousGeoJson -> "Continuous GeoJSON"
        | _                              -> string preset

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
        signedLongitude   = false
        useSampledPoints  = true
        sampleSurfaceProperties = false
        annotationFields  =
            [ AnnotationField.Key; AnnotationField.Text; AnnotationField.GroupName
              AnnotationField.GroupPath; AnnotationField.SurfaceName
              AnnotationField.Geometry; AnnotationField.Semantic ]
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
                // Determined against real Mars data in QGIS: the raw longitude
                // is already oriented correctly, so no transform. `Flipped` (the
                // general default, inherited from the old CSV export) comes out
                // mirror-inverted there.
                longitude        = LongitudeConvention.Native
                // QGIS expects -180...180, not 0...360
                signedLongitude  = true
                annotationFields =
                    // colorHex so QGIS can bind it to the symbol colour, groupPath
                    // so the group tree survives as a categorisable attribute
                    [ AnnotationField.Key; AnnotationField.Text; AnnotationField.GroupName
                      AnnotationField.GroupPath; AnnotationField.SurfaceName
                      AnnotationField.Geometry; AnnotationField.Semantic
                      AnnotationField.ColorHex; AnnotationField.Length; AnnotationField.WayLength
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
                // geographic so `alt` is present — that column is what the old
                // profile export called `elevation`, and ground distance is what
                // it called `distance`
                coordinates      = CoordinateMode.Both
                useSampledPoints = true
                annotationFields = [ AnnotationField.Key; AnnotationField.Text; AnnotationField.SurfaceName ]
                pointFields      = AnnotationFields.allPointFields }
        | ExportPreset.AttitudePlanes ->
            { settings with format = ExportFormat.Attitude }
        | ExportPreset.ContinuousGeoJson ->
            { settings with format = ExportFormat.ContinuousGeoJson }
        | _ -> settings

    let fileExtension (format : ExportFormat) =
        match format with
        | ExportFormat.Csv -> "csv"
        | _                -> "json"

    let formatLabel (format : ExportFormat) =
        match format with
        | ExportFormat.Csv              -> "CSV table (*.csv)"
        | ExportFormat.GeoJson          -> "GeoJSON (*.json)"
        | ExportFormat.Attitude         -> "Attitude planes (*.json)"
        | ExportFormat.ContinuousGeoJson -> "Continuous GeoJSON (*.json)"
        | _                             -> string format

    let allFormats =
        [ ExportFormat.Csv; ExportFormat.GeoJson
          ExportFormat.Attitude; ExportFormat.ContinuousGeoJson ]

    /// True when picking the format arms a background export instead of writing
    /// a file once.
    let isContinuous (format : ExportFormat) =
        format = ExportFormat.ContinuousGeoJson

    let granularityLabel (granularity : ExportGranularity) =
        match granularity with
        | ExportGranularity.PerAnnotation -> "one record per annotation"
        | _                               -> "one record per point"

    let scopeLabel (scope : ExportScope) =
        match scope with
        | ExportScope.All      -> "All annotations"
        | ExportScope.Visible  -> "Visible annotations only"
        | _                    -> "Selected annotations only"

    /// A GeoJSON geometry is written in one coordinate system, never two, so
    /// offering "Both" there suggests a choice the format cannot express. CSV
    /// has no geometry and just emits both sets of columns, so it keeps it.
    ///
    /// A UI constraint only: the record builder still honours whatever it is
    /// given, so a programmatic caller setting `Both` is unaffected.
    let coordinateModesFor (format : ExportFormat) =
        match format with
        | ExportFormat.GeoJson -> [ CoordinateMode.Cartesian; CoordinateMode.Geographic ]
        | _                    -> [ CoordinateMode.Cartesian; CoordinateMode.Geographic; CoordinateMode.Both ]

    let coordinateLabel (mode : CoordinateMode) =
        match mode with
        | CoordinateMode.Cartesian  -> "Cartesian (x, y, z)"
        | CoordinateMode.Geographic -> "Geographic (lat, lon, alt)"
        | _                         -> "Both"

    let longitudeLabel (convention : LongitudeConvention) =
        match convention with
        | LongitudeConvention.Native         -> "Native (as returned for the body)"
        | LongitudeConvention.Flipped        -> "Flipped (360 - lon)"
        | LongitudeConvention.FlippedShifted -> "Flipped and shifted (180 - lon)"
        | _                                  -> string convention

    let allLongitudeConventions =
        [ LongitudeConvention.Native; LongitudeConvention.Flipped
          LongitudeConvention.FlippedShifted ]

    /// True when the format ignores every setting below the format dropdown.
    let hasFixedSchema (format : ExportFormat) =
        format = ExportFormat.Attitude || format = ExportFormat.ContinuousGeoJson
