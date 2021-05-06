namespace PRo3D.Comparison

open Adaptify


open Aardvark.Base
open PRo3D.Base.Annotation
open Aardvark.UI
open PRo3D.Core
open PRo3D.Base
open Chiron

type OriginMode =
    | ModelOrigin       = 0
    | BoundingBoxCentre = 1

type SurfaceMeasurements =  {
    /// the dimensions of this surface along x/y/z axes
    dimensions      : V3d
    /// the angles of x/y/z axes of the model in worldspace
    rollPitchYaw    : V3d
} with
    static member init = {
        dimensions     = V3d.OOO
        rollPitchYaw   = V3d.OOO
    }
    static member ToJson (x:SurfaceMeasurements) =
        json {
            do! Json.write "dimensions"   (x.dimensions |> string)
            do! Json.write "rollPitchYaw" (x.rollPitchYaw |> string)
        }


type AnnotationMeasurement = {
    annotationKey : System.Guid
    text          : string
    length        : float
} with
    static member ToJson (x:AnnotationMeasurement) =
        json {
            if x.text.Length > 0 then
              do! Json.write "text" x.text
            do! Json.write "length" (sprintf "%f" x.length)
        }

/// Used to compare the way length of two annotations on two different surfaces.
/// The annotations need to be drawn using Bookmark projection.
/// To compare two annotations they need to be drawn with the same bookmark selected.
type AnnotationComparison = {
    bookmarkName      : string
    bookmarkId        : System.Guid
    measurement1      : option<AnnotationMeasurement>
    measurement2      : option<AnnotationMeasurement>
    difference        : option<float>
} with
    static member ToJson (x:AnnotationComparison) =
        json {
            do! Json.write "bookmark" x.bookmarkName
            //do! Json.write "bookmarkId" (x.bookmarkId.ToString ())
            if x.measurement1.IsSome then 
                do! Json.write "measurements1"  x.measurement1.Value
            if x.measurement2.IsSome then 
                do! Json.write "measurements2"  x.measurement2.Value
            if x.difference.IsSome then 
                do! Json.write "difference"   x.difference.Value
        }
      
type SurfaceComparison = {
    measurements1         : option<SurfaceMeasurements>
    measurements2         : option<SurfaceMeasurements>
    comparedMeasurements  : option<SurfaceMeasurements>
} with
    static member ToJson (x:SurfaceComparison) =
        json {
            if x.measurements1.IsSome then 
                do! Json.write "measurements1"  x.measurements1.Value
            if x.measurements2.IsSome then 
                do! Json.write "measurements2"  x.measurements2.Value
            if x.comparedMeasurements.IsSome then 
                do! Json.write "difference"   x.comparedMeasurements.Value
        }

/// Used to compare different attributes of two surfaces.
[<ModelType>]
type ComparisonApp = {
    showMeasurementsSg           : bool
    originMode                   : OriginMode
    surface1                     : option<string>
    surface2                     : option<string>
    surfaceMeasurements          : SurfaceComparison
    annotationMeasurements       : list<AnnotationComparison>
} with
    static member ToJson (x:ComparisonApp) =
        json {
            if x.surface1.IsSome then 
                do! Json.write "surface1"       x.surface1.Value
            if x.surface2.IsSome then 
                do! Json.write "surface2"       x.surface2.Value 
            do! Json.write "originMode"         (x.originMode.ToString ())
            do! Json.write "surfaceMeasurements" x.surfaceMeasurements
            do! Json.write "annotationMeasurements" x.annotationMeasurements
        }

type ComparisonAction =
    | SelectSurface1 of string
    | SelectSurface2 of string
    | Update
    | ExportMeasurements of string
    | ToggleVisible
    | AddBookmarkReference of System.Guid
    | SetOriginMode of OriginMode