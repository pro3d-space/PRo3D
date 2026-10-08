namespace PRo3D.Composition

open Aardvark.Base

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Drawing

/// `.pro3d.ann` annotation files - the format and sidecar convention full PRo3D uses, so an
/// annotation file written by one host opens in the other.
module AnnotationFiles =

    /// The annotation sidecar of a scene: `<scene>.pro3d.ann`.
    let sidecarOf (scenePath : string) =
        scenePath |> Serialization.changeExtension ".pro3d.ann"

    /// Reading throws on malformed JSON; this boundary turns it into a value.
    let tryLoad (path : string) : Result<Annotations, string> =
        try
            DrawingUtilities.IO.loadAnnotationsFromFile path |> Ok
        with e ->
            Log.error "[AnnotationFiles] couldn't load %s: %s" path e.Message
            Result.Error e.Message

    /// Writes the drawing model's annotations (groups, DnS legend, colour-by-category).
    let save (path : string) (drawing : DrawingModel) : Result<unit, string> =
        try
            PRo3D.Core.Drawing.IO.saveVersioned drawing path |> ignore |> Ok
        with e ->
            Log.error "[AnnotationFiles] couldn't save %s: %s" path e.Message
            Result.Error e.Message

    /// Replaces the annotations of a drawing model with loaded ones.
    let applyTo (drawing : DrawingModel) (annotations : Annotations) : DrawingModel =
        { drawing with
            annotations     = annotations.annotations
            dnsColorLegend  = annotations.dnsColorLegend
            colorByCategory = annotations.colorByCategory }
