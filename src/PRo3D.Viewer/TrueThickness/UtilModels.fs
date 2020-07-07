namespace Utils

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI

[<DomainType>]
type PickPointModel =
    {
        pos : V3d

        [<NonIncremental>]
        id  : string
    }

[<DomainType>]
type PickPointsModel =
    {
        points : plist<PickPointModel>
         // for precision, we need to a stable anchor to be used in as modeltrafo. 
         // so we compute pretrafo given the first point and all further points get pretransformed
        preTrafo : Option<Trafo3d>
    }