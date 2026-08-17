namespace PRo3D.Core

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify

open PRo3D.Base
open PRo3D.Base.Annotation

type AnnotationExportAction =
    | Open
    | Close
    | SetPreset              of ExportPreset
    | SetFormat              of ExportFormat
    | SetGranularity         of ExportGranularity
    | SetScope               of ExportScope
    | SetCoordinates         of CoordinateMode
    | SetLongitude           of LongitudeConvention
    | ToggleSignedLongitude
    | ToggleSampledPoints
    | ToggleAnnotationField  of AnnotationField
    | TogglePointField       of PointField
    /// select / deselect every annotation-level attribute at once
    | SetAllAnnotationFields of bool
    /// select / deselect every point-level attribute at once
    | SetAllPointFields      of bool
    /// the file path comes from the save dialog; handled at viewer level
    /// because the export needs the surface model
    | Export                 of string

/// Settings of the annotation export window. Deliberately session-only — it
/// lives on the root `Model`, not on `Scene`, so nothing here is serialised and
/// no scene version has to be bumped.
[<ModelType>]
type AnnotationExportModel = {
    isOpen            : bool
    preset            : ExportPreset
    format            : ExportFormat
    granularity       : ExportGranularity
    scope             : ExportScope
    coordinates       : CoordinateMode
    longitude         : LongitudeConvention
    signedLongitude   : bool
    useSampledPoints  : bool

    annotationFields  : HashSet<AnnotationField>
    pointFields       : HashSet<PointField>
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AnnotationExportModel =

    let ofSettings (isOpen : bool) (preset : ExportPreset) (s : AnnotationExportSettings) = {
        isOpen            = isOpen
        preset            = preset
        format            = s.format
        granularity       = s.granularity
        scope             = s.scope
        coordinates       = s.coordinates
        longitude         = s.longitude
        signedLongitude   = s.signedLongitude
        useSampledPoints  = s.useSampledPoints
        annotationFields  = HashSet.ofList s.annotationFields
        pointFields       = HashSet.ofList s.pointFields
    }

    /// Flattens the model into the ordered snapshot the writers consume. Field
    /// order follows the enum declaration order, so a given settings
    /// combination always produces the same columns in the same places.
    let toSettings (m : AnnotationExportModel) : AnnotationExportSettings = {
        format            = m.format
        granularity       = m.granularity
        scope             = m.scope
        coordinates       = m.coordinates
        longitude         = m.longitude
        signedLongitude   = m.signedLongitude
        useSampledPoints  = m.useSampledPoints
        // `Key` is the annotation's Guid and the only stable handle a GIS round
        // trip has for matching a feature back to its annotation, so it is
        // exported whether or not it is ticked.
        annotationFields  =
            AnnotationFields.all
            |> List.filter (fun f ->
                f = AnnotationField.Key || m.annotationFields |> HashSet.contains f)
        pointFields       = AnnotationFields.allPointFields |> List.filter (fun f -> m.pointFields |> HashSet.contains f)
    }

    let initial = ofSettings false ExportPreset.Custom AnnotationExportSettings.initial
