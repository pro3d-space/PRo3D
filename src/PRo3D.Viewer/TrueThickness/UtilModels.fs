namespace Utils

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI

[<ModelType>]
type PickPointModel =
    {
        pos : V3d

        [<NonAdaptive>]
        id  : string
    }

[<ModelType>]
type PickPointsModel =
    {
        points : IndexList<PickPointModel>
         // for precision, we need to a stable anchor to be used in as modeltrafo. 
         // so we compute pretrafo given the first point and all further points get pretransformed
        preTrafo : Option<Trafo3d>
    }