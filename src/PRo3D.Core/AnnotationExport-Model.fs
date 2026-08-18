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
    | ToggleSurfaceProperties
    | ToggleAnnotationField  of AnnotationField
    | TogglePointField       of PointField
    /// select / deselect every annotation-level attribute at once
    | SetAllAnnotationFields of bool
    /// select / deselect every point-level attribute at once
    | SetAllPointFields      of bool
    /// the file path comes from the save dialog; handled at viewer level
    /// because the export needs the surface model
    | Export                 of string
    /// stop the running background export; handled at viewer level because the
    /// state lives on DrawingModel
    | StopContinuous

/// Live state of the background GeoJSON export, for the window to display. It
/// lives on `DrawingModel`, which is compiled after this file, so the viewer
/// hands it in rather than the window reaching for it.
type ContinuousExportState = {
    isRunning : aval<bool>
    target    : aval<Option<string>>
}

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
    sampleSurfaceProperties : bool

    annotationFields  : HashSet<AnnotationField>
    pointFields       : HashSet<PointField>

    /// Why the last export attempt produced nothing. Shown in the window's
    /// header, which stays open so the user can correct the settings — a log
    /// line alone goes unnoticed. Cleared by the next interaction.
    warning           : Option<string>
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
        sampleSurfaceProperties = s.sampleSurfaceProperties
        annotationFields  = HashSet.ofList s.annotationFields
        pointFields       = HashSet.ofList s.pointFields
        warning           = None
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
        sampleSurfaceProperties = m.sampleSurfaceProperties
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
