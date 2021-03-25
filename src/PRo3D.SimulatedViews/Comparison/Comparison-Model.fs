namespace PRo3D.Comparison

open Aardvark.Base
open Aardvark.UI
open Adaptify

[<ModelType>]
type AxesDirections = {
    xDir : V3d
    yDir : V3d
    zDir : V3d
}

[<ModelType>]
type SurfaceMeasurements =  {
    /// the dimensions of this surface along x/y/z axes
    dimensions : V3d
    /// the directions of x/y/z axes of the model in worldspace
    axesDirections : AxesDirections
}

[<ModelType>]
type ComparisonApp = {
    surface1 : System.Guid
    surface2 : System.Guid
    measurements : SurfaceMeasurements    
}

type ComparisonAction =
    | SelectSurface1
    | SelectSurface2