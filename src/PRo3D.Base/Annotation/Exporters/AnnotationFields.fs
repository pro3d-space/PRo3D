namespace PRo3D.Base.Annotation

open System

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base

/// Annotation-level attributes offered in the export window.
///
/// Values are persisted in nothing (the export settings are session-only), but
/// they are referenced by name from the presets, so keep the numbering stable
/// to avoid churn if the settings ever do become persistent.
type AnnotationField =
    // identity / categorical
    | Key               = 0
    | Text              = 1
    | GroupName         = 2
    | SurfaceName       = 3
    | Geometry          = 4
    | Semantic          = 5
    | Projection        = 6
    | Color             = 7
    | Visible           = 8
    | PointCount        = 9
    // measurements (AnnotationResults + Calculations)
    | Height            = 10
    | HeightDelta       = 11
    | AvgAltitude       = 12
    | Length            = 13
    | WayLength         = 14
    | Bearing           = 15
    | Slope             = 16
    | TrueThickness     = 17
    | VerticalThickness = 18
    | Area              = 19
    | VerticalDelta     = 20
    | HorizontalDelta   = 21
    | Thickness         = 22
    | ManualDipAngle    = 23
    // ellipse
    | MajorDiameter     = 24
    | MinorDiameter     = 25
    // dip & strike
    | DipAngle          = 26
    | DipAzimuth        = 27
    | StrikeAzimuth     = 28
    | Rake              = 29
    // planar-fit error measures
    | ErrorAvg          = 30
    | ErrorMin          = 31
    | ErrorMax          = 32
    | ErrorStd          = 33
    | SumOfSquares      = 34
    | MinAngularError   = 35
    | MaxAngularError   = 36

/// Per-point attributes, available when the export granularity is "one record
/// per point". Which of `Cartesian` / `Geographic` actually produce columns is
/// additionally gated by the chosen `CoordinateMode`.
type PointField =
    /// running index of the point within its annotation
    | Index              = 0
    /// x, y, z in body-fixed cartesian coordinates
    | Cartesian          = 1
    /// latitude, longitude, altitude
    | Geographic         = 2
    /// index of the Annotation.segments entry this point belongs to
    | SegmentIndex       = 3
    /// distance to the previously exported point
    | StepLength         = 4
    /// length of the whole segment this point belongs to
    | SegmentLength      = 5
    /// running length from the annotation's first point
    | CumulativeDistance = 6

/// Coarse grouping, used only to lay the checkboxes out in sections.
type AnnotationFieldGroup =
    | Identity
    | Measurements
    | Ellipse
    | DipAndStrike
    | ErrorMeasures

module AnnotationFields =

    let all =
        Enum.GetValues(typeof<AnnotationField>)
        :?> array<AnnotationField>
        |> Array.toList

    let allPointFields =
        Enum.GetValues(typeof<PointField>)
        :?> array<PointField>
        |> Array.toList

    let groupOf (field : AnnotationField) =
        match field with
        | AnnotationField.Key | AnnotationField.Text | AnnotationField.GroupName
        | AnnotationField.SurfaceName | AnnotationField.Geometry | AnnotationField.Semantic
        | AnnotationField.Projection | AnnotationField.Color | AnnotationField.Visible
        | AnnotationField.PointCount -> Identity
        | AnnotationField.MajorDiameter | AnnotationField.MinorDiameter -> Ellipse
        | AnnotationField.DipAngle | AnnotationField.DipAzimuth
        | AnnotationField.StrikeAzimuth | AnnotationField.Rake -> DipAndStrike
        | AnnotationField.ErrorAvg | AnnotationField.ErrorMin | AnnotationField.ErrorMax
        | AnnotationField.ErrorStd | AnnotationField.SumOfSquares
        | AnnotationField.MinAngularError | AnnotationField.MaxAngularError -> ErrorMeasures
        | _ -> Measurements

    /// Stable machine name — the CSV header cell and the GeoJSON property key.
    /// These deliberately match the column names of the CSV export this
    /// replaces, so existing downstream scripts keep working.
    let columnName (field : AnnotationField) =
        match field with
        | AnnotationField.Key               -> "key"
        | AnnotationField.Text              -> "text"
        | AnnotationField.GroupName         -> "groupName"
        | AnnotationField.SurfaceName       -> "surfaceName"
        | AnnotationField.Geometry          -> "geometry"
        | AnnotationField.Semantic          -> "semantic"
        | AnnotationField.Projection        -> "projection"
        | AnnotationField.Color             -> "color"
        | AnnotationField.Visible           -> "visible"
        | AnnotationField.PointCount        -> "points"
        | AnnotationField.Height            -> "height"
        | AnnotationField.HeightDelta       -> "heightDelta"
        | AnnotationField.AvgAltitude       -> "avgAltitude"
        | AnnotationField.Length            -> "length"
        | AnnotationField.WayLength         -> "wayLength"
        | AnnotationField.Bearing           -> "bearing"
        | AnnotationField.Slope             -> "slope"
        | AnnotationField.TrueThickness     -> "trueThickness"
        | AnnotationField.VerticalThickness -> "verticalThickness"
        | AnnotationField.Area              -> "area"
        | AnnotationField.VerticalDelta     -> "verticalDelta"
        | AnnotationField.HorizontalDelta   -> "horizontalDelta"
        | AnnotationField.Thickness         -> "thickness"
        | AnnotationField.ManualDipAngle    -> "manualDip"
        | AnnotationField.MajorDiameter     -> "majorDiameter"
        | AnnotationField.MinorDiameter     -> "minorDiameter"
        | AnnotationField.DipAngle          -> "dipAngle"
        | AnnotationField.DipAzimuth        -> "dipAzimuth"
        | AnnotationField.StrikeAzimuth     -> "strikeAzimuth"
        | AnnotationField.Rake              -> "rake"
        | AnnotationField.ErrorAvg          -> "errorAvg"
        | AnnotationField.ErrorMin          -> "errorMin"
        | AnnotationField.ErrorMax          -> "errorMax"
        | AnnotationField.ErrorStd          -> "errorStd"
        | AnnotationField.SumOfSquares      -> "sumOfSquares"
        | AnnotationField.MinAngularError   -> "minAngularError"
        | AnnotationField.MaxAngularError   -> "maxAngularError"
        | _                                 -> sprintf "%A" field

    /// Human-readable label with unit, shown next to the checkbox.
    let label (field : AnnotationField) =
        match field with
        | AnnotationField.Key               -> "Id"
        | AnnotationField.Text              -> "Label"
        | AnnotationField.GroupName         -> "Group"
        | AnnotationField.SurfaceName       -> "Surface"
        | AnnotationField.Geometry          -> "Annotation type"
        | AnnotationField.Semantic          -> "Semantic"
        | AnnotationField.Projection        -> "Projection"
        | AnnotationField.Color             -> "Colour"
        | AnnotationField.Visible           -> "Visible"
        | AnnotationField.PointCount        -> "Point count"
        | AnnotationField.Height            -> "Height (m)"
        | AnnotationField.HeightDelta       -> "Height delta (m)"
        | AnnotationField.AvgAltitude       -> "Avg altitude (m)"
        | AnnotationField.Length            -> "Length, straight (m)"
        | AnnotationField.WayLength         -> "Length, total along surface (m)"
        | AnnotationField.Bearing           -> "Bearing (deg)"
        | AnnotationField.Slope             -> "Slope (deg)"
        | AnnotationField.TrueThickness     -> "True thickness (m)"
        | AnnotationField.VerticalThickness -> "Vertical thickness (m)"
        | AnnotationField.Area              -> "Area (m2)"
        | AnnotationField.VerticalDelta     -> "Vertical delta (m)"
        | AnnotationField.HorizontalDelta   -> "Horizontal delta (m)"
        | AnnotationField.Thickness         -> "Line thickness"
        | AnnotationField.ManualDipAngle    -> "Manual dip angle (deg)"
        | AnnotationField.MajorDiameter     -> "Major diameter (m)"
        | AnnotationField.MinorDiameter     -> "Minor diameter (m)"
        | AnnotationField.DipAngle          -> "Dip angle (deg)"
        | AnnotationField.DipAzimuth        -> "Dip azimuth (deg)"
        | AnnotationField.StrikeAzimuth     -> "Strike azimuth (deg)"
        | AnnotationField.Rake              -> "Rake (rad)"
        | AnnotationField.ErrorAvg          -> "Error, average"
        | AnnotationField.ErrorMin          -> "Error, min"
        | AnnotationField.ErrorMax          -> "Error, max"
        | AnnotationField.ErrorStd          -> "Error, std deviation"
        | AnnotationField.SumOfSquares      -> "Sum of squares"
        | AnnotationField.MinAngularError   -> "Min angular error (deg)"
        | AnnotationField.MaxAngularError   -> "Max angular error (deg)"
        | _                                 -> sprintf "%A" field

    let pointColumnName (field : PointField) =
        match field with
        | PointField.Index              -> "pointIndex"
        | PointField.SegmentIndex       -> "segmentIndex"
        | PointField.StepLength         -> "stepLength"
        | PointField.SegmentLength      -> "segmentLength"
        | PointField.CumulativeDistance -> "distance"
        | PointField.Cartesian          -> "cartesian"
        | PointField.Geographic         -> "geographic"
        | _                             -> sprintf "%A" field

    let pointLabel (field : PointField) =
        match field with
        | PointField.Index              -> "Point index"
        | PointField.SegmentIndex       -> "Segment index"
        | PointField.StepLength         -> "Step length, to previous point (m)"
        | PointField.SegmentLength      -> "Segment length (m)"
        | PointField.CumulativeDistance -> "Cumulative distance (m)"
        | PointField.Cartesian          -> "Cartesian x / y / z"
        | PointField.Geographic         -> "Geographic lat / lon / alt"
        | _                             -> sprintf "%A" field

    // ------------------------------------------------------------ values ---

    /// Dip & strike values, flattened with an explicit "not available" case.
    /// Mirrors what the CSV exporter used to compute inline; `regressionInfo`
    /// is optional even when `dnsResults` is present, hence the two levels.
    let private dnsValue (up : V3d) (field : AnnotationField) (a : Annotation) =
        match a.dnsResults with
        | None -> VMissing
        | Some dns ->
            match field with
            | AnnotationField.DipAngle      -> ExportValue.ofFloat dns.dipAngle
            | AnnotationField.DipAzimuth    -> ExportValue.ofFloat dns.dipAzimuth
            | AnnotationField.StrikeAzimuth -> ExportValue.ofFloat dns.strikeAzimuth
            | AnnotationField.ErrorAvg      -> ExportValue.ofFloat dns.error.average
            | AnnotationField.ErrorMin      -> ExportValue.ofFloat dns.error.min
            | AnnotationField.ErrorMax      -> ExportValue.ofFloat dns.error.max
            | AnnotationField.ErrorStd      -> ExportValue.ofFloat dns.error.stdev
            | AnnotationField.SumOfSquares  -> ExportValue.ofFloat dns.error.sumOfSquares
            | AnnotationField.Rake ->
                match dns.regressionInfo with
                | Some regInfo -> ExportValue.ofFloat (Calculations.rake up regInfo)
                | None         -> VMissing
            | AnnotationField.MinAngularError ->
                match dns.regressionInfo with
                | Some regInfo -> ExportValue.ofFloat (Constant.DegreesPerRadian * regInfo.AngularErrors.X)
                | None         -> VMissing
            | AnnotationField.MaxAngularError ->
                match dns.regressionInfo with
                | Some regInfo -> ExportValue.ofFloat (Constant.DegreesPerRadian * regInfo.AngularErrors.Y)
                | None         -> VMissing
            | _ -> VMissing

    /// Semi-axes are stored as vectors, so a diameter is twice their length.
    let private ellipseValue (field : AnnotationField) (a : Annotation) =
        match a.ellipticResults with
        | None -> VMissing
        | Some e ->
            match field with
            | AnnotationField.MajorDiameter -> ExportValue.ofFloat (2.0 * e.geographicalEllipse.Axis0.Length)
            | AnnotationField.MinorDiameter -> ExportValue.ofFloat (2.0 * e.geographicalEllipse.Axis1.Length)
            | _ -> VMissing

    /// `lookUp` maps annotation key -> containing group name (GroupsApp.updateGroupsLookup).
    /// `up` is the reference system's up vector, needed for the deltas and the rake.
    let valueOf
        (lookUp : HashMap<Guid, string>)
        (up     : V3d)
        (field  : AnnotationField)
        (a      : Annotation)
        : ExportValue =

        let results = a.results |> Option.defaultValue AnnotationResults.initial

        match field with
        | AnnotationField.Key         -> VText (a.key.ToString())
        | AnnotationField.Text        -> VText a.text
        | AnnotationField.GroupName   -> VText (lookUp |> HashMap.tryFind a.key |> Option.defaultValue "")
        | AnnotationField.SurfaceName -> VText a.surfaceName
        | AnnotationField.Geometry    -> VText (string a.geometry)
        | AnnotationField.Semantic    -> VText (string a.semantic)
        | AnnotationField.Projection  -> VText (string a.projection)
        | AnnotationField.Color       -> VText (a.color.c.ToString())
        | AnnotationField.Visible     -> VBool a.visible
        | AnnotationField.PointCount  -> VInt a.points.Count

        | AnnotationField.Height            -> ExportValue.ofFloat results.height
        | AnnotationField.HeightDelta       -> ExportValue.ofFloat results.heightDelta
        | AnnotationField.AvgAltitude       -> ExportValue.ofFloat results.avgAltitude
        | AnnotationField.Length            -> ExportValue.ofFloat results.length
        | AnnotationField.WayLength         -> ExportValue.ofFloat results.wayLength
        | AnnotationField.Bearing           -> ExportValue.ofFloat results.bearing
        | AnnotationField.Slope             -> ExportValue.ofFloat results.slope
        | AnnotationField.TrueThickness     -> ExportValue.ofFloat results.trueThickness
        | AnnotationField.VerticalThickness -> ExportValue.ofFloat results.verticalThickness
        | AnnotationField.Area              -> ExportValue.ofFloat results.area
        | AnnotationField.Thickness         -> ExportValue.ofFloat a.thickness.value
        | AnnotationField.ManualDipAngle    -> ExportValue.ofFloat a.manualDipAngle.value

        | AnnotationField.VerticalDelta ->
            ExportValue.ofFloat (Calculations.verticalDelta (a.points |> IndexList.toList) up)
        | AnnotationField.HorizontalDelta ->
            ExportValue.ofFloat (Calculations.horizontalDelta (a.points |> IndexList.toList) up)

        | AnnotationField.MajorDiameter
        | AnnotationField.MinorDiameter -> ellipseValue field a

        | AnnotationField.DipAngle | AnnotationField.DipAzimuth | AnnotationField.StrikeAzimuth
        | AnnotationField.Rake | AnnotationField.ErrorAvg | AnnotationField.ErrorMin
        | AnnotationField.ErrorMax | AnnotationField.ErrorStd | AnnotationField.SumOfSquares
        | AnnotationField.MinAngularError | AnnotationField.MaxAngularError -> dnsValue up field a

        | _ -> VMissing
