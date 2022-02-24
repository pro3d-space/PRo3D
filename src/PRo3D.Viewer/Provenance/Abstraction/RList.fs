namespace PRo3D.Provenance.Abstraction

open Aardvark.Base

open FSharp.Data.Adaptive

// We want to compare the raw content of lists with the (=) operator; plist contains
// indices that might change by removing and adding elements, while the elements themselves remain
// the same.
[<CustomEquality; NoComparison>]
type rlist<'a when 'a : equality> =
    { inner : 'a IndexList }

    override x.GetHashCode () =
        x.inner |> IndexList.toList |> hash

    override x.Equals y =
        match y with 
        | :? rlist<'a> as y -> IndexList.toList x.inner = IndexList.toList y.inner
        | _ -> false
