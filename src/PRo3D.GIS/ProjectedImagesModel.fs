namespace PRo3D.Base


open Aardvark.Base
open Aardvark.Rendering


module ProjectedImages =

    type Extrinsics = 
        | Plain of CameraView

    type Intrinsics = 
        | Plain of Frustum

    type ImageData = 
        | FilePath of string

    type ProjectedImage =
        {
            intrinsics : Intrinsics
            extrinsics : Extrinsics
            image      : ImageData 
        }