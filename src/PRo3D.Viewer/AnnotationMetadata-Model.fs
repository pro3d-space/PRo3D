namespace PRo3D.Viewer

open System

/// The comment dialog floating next to an annotation (#887, docs/AnnotationMetadata.md).
/// The text being typed stays in the browser; it reaches the model only when the dialog is
/// confirmed with Enter.
type CommentEditor =
    {
        annotation : Guid
        /// the comment when the dialog opened, shown pre-filled
        initial    : string
        /// render-space point the dialog floats next to: the centre of the annotation's points
        anchor     : Aardvark.Base.V3d
    }

/// State of the annotation-metadata sub-app (AnnotationMetadataApp). Session state, not
/// persisted: the metadata itself lives on each annotation.
type AnnotationMetadataModel =
    {
        comment              : Option<CommentEditor>
        /// "C2"-style labels next to rated annotations, for all annotations at once
        showConfidenceLabels : bool
    }

type AnnotationMetadataAction =
    /// keys 1-4: rate the selected annotations
    | SetConfidence          of int
    /// remove the rating of the selected annotations
    | ClearConfidence
    /// key C: open the comment dialog for the selected annotation
    | OpenComment
    | CommitComment          of string
    | CancelComment
    | ToggleConfidenceLabels

module AnnotationMetadataModel =
    let initial =
        {
            comment              = None
            showConfidenceLabels = true
        }
