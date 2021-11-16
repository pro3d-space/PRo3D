namespace PRo3D.Viewer

open Aardvark.Base
open Adaptify

[<ModelType>]
type Traverse = 
    {
        positions : List<V3d>
    }

module Traverse =
    let initial = { positions = [] }

