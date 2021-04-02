namespace PRo3D.Comparison

open Aardvark.Base
open Aardvark.UI
open Adaptify



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

[<ModelType>]
type ComparisonApp = {
    showMeasurementsSg    : bool
    surface1              : option<string>
    surface2              : option<string>
    measurements1         : option<SurfaceMeasurements>
    measurements2         : option<SurfaceMeasurements>
    comparedMeasurements  : option<SurfaceMeasurements>
}

type ComparisonAction =
    | SelectSurface1 of string
    | SelectSurface2 of string
    | Update
    | MeasurementMessage