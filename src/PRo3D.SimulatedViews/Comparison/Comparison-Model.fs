namespace PRo3D.Comparison

open Aardvark.Base
open Aardvark.UI
open Adaptify

[<ModelType>]
type AxesDirections = {
    xDir : V3d
    yDir : V3d
    zDir : V3d
} with
    static member init =
        {
            xDir = V3d.OOO
            yDir = V3d.OOO
            zDir = V3d.OOO
        }

[<ModelType>]
type SurfaceMeasurements =  {
    /// the dimensions of this surface along x/y/z axes
    dimensions      : V3d
    /// the directions of x/y/z axes of the model in worldspace
    axesDirections : AxesDirections
} with
    static member init = {
        dimensions     = V3d.OOO
        axesDirections = AxesDirections.init
    }

[<ModelType>]
type ComparisonApp = {
    showMeasurementsSg : bool
    surface1           : option<string>
    surface2           : option<string>
    measurements1      : SurfaceMeasurements    
    measurements2      : SurfaceMeasurements    
}

type ComparisonAction =
    | SelectSurface1 of string
    | SelectSurface2 of string
    | Update
    | MeasurementMessage