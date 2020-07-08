namespace FSharp.Data.Adaptive


[<AutoOpen>]
module MissingFunctionality = 


    open FSharp.Data.Adaptive


    module HashMap =
        let values (v : HashMap<_,_>) = v |> HashMap.toSeq |> Seq.map snd

        let pivot _ = failwith "" // TODO v5: what was this


    module ASet =
        let ofAValSingle (v : aval<'a>) : aset<'a> = 
            v |> AVal.map Seq.singleton |> ASet.ofAVal

        let union' (xs : seq<aset<'a>>) : aset<'a> = ASet.unionMany (ASet.ofSeq xs)


    module AList = 
        let ofAValSingle (v : aval<'a>) : alist<'a> = 
            v |> AVal.map Seq.singleton |> AList.ofAVal

    module AVal = 
        let bindOption (m : aval<Option<'a>>) (defaultValue : 'b) (project : 'a -> aval<'b>)  : aval<'b> =
            m |> AVal.bind (function 
                | None   -> AVal.constant defaultValue       
                | Some v -> project v
            )

namespace Adaptivy.FSharp.Core


[<AutoOpen>]
module Missing = 
    open Adaptify.FSharp.Core
    module AdaptiveOption =
        let toOption (a : AdaptiveOptionCase<_,_,_>) =
            match a with
            | AdaptiveSome a -> Some a
            | AdaptiveNone -> None
