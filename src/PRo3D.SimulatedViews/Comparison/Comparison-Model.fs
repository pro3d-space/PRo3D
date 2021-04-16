namespace PRo3D.Comparison

open Adaptify


open Aardvark.Base
open PRo3D.Base.Annotation
open Aardvark.UI
open PRo3D.Core
open PRo3D.Base
open Chiron



[<ModelType>]
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
    static member FromJson( _ : SurfaceMeasurements) = 
        json {
            let! dimensions   = Json.read "dimensions"
            let! rollPitchYaw = Json.read "rollPitchYaw"

            return 
              { 
                  dimensions   = dimensions |> V3d.Parse
                  rollPitchYaw = rollPitchYaw |> V3d.Parse
              }
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
}

type AnnotationMeasurements = {
    bookmarkName      : string
    bookmarkId        : System.Guid
    measurement1      : option<AnnotationMeasurement>
    measurement2      : option<AnnotationMeasurement>
    difference        : option<float>
}
      

[<ModelType>]
type ComparisonApp = {
    showMeasurementsSg    : bool
    surface1              : option<string>
    surface2              : option<string>
    measurements1         : option<SurfaceMeasurements>
    measurements2         : option<SurfaceMeasurements>
    comparedMeasurements  : option<SurfaceMeasurements>
    annotationMeasurements : list<AnnotationMeasurements>
} with
    static member FromJson( _ : ComparisonApp) = 
        json {
            let! surface1   = Json.tryRead "surface1"
            let! surface2   = Json.tryRead "surface2"
            let! measurements1   = Json.tryRead "measurements1"
            let! measurements2   = Json.tryRead "measurements2"
            let! comparedMeasurements   = Json.tryRead "comparedMeasurements"

            return 
              { 
                showMeasurementsSg    = true
                surface1              = surface1            
                surface2              = surface2            
                measurements1         = measurements1       
                measurements2         = measurements2       
                comparedMeasurements  = comparedMeasurements
                annotationMeasurements = []
              }
        }

    static member ToJson (x:ComparisonApp) =
        json {
            if x.surface1.IsSome then 
                do! Json.write "surface1"       x.surface1.Value
                do! Json.write "measurements1"  x.measurements1.Value
            if x.surface2.IsSome then 
                do! Json.write "surface2"       x.surface2.Value 
                do! Json.write "measurements2"  x.measurements2.Value
            if x.comparedMeasurements.IsSome then 
                do! Json.write "comparedMeasurements"   x.comparedMeasurements.Value
        }

type ComparisonAction =
    | SelectSurface1 of string
    | SelectSurface2 of string
    | Update
    | ExportMeasurements of string
    | ToggleVisible
    | AddBookmarkReference of System.Guid
    | MeasurementMessage