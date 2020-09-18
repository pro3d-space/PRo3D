namespace FSharp.Data.Adaptive


[<AutoOpen>]
module MissingFunctionality = 


    open FSharp.Data.Adaptive
    open Adaptify.FSharp.Core


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
        let bindAdaptiveOption (m : aval<AdaptiveOptionCase<_,_,_>>) (defaultValue : 'b) (project : 'a -> aval<'b>)  : aval<'b> =
            m |> AVal.bind (function 
                | AdaptiveNone   -> AVal.constant defaultValue       
                | AdaptiveSome v -> project v
            )


namespace Aether

[<AutoOpen>]
module Conversion = 
    open Aether
    open Aether.Operators
    module Aether = 
        let toBase (l : Lens<'a,'c>) =
            { new Aardvark.Base.Lens<_,_>() with
                override x.Get s = Optic.get l s
                override x.Set(s,v) = Optic.set l v s
            }
        let ofBase (l : Aardvark.Base.Lens<'s,'a>) : Lens<_,_> = 
            l.Get, (flip << curry) l.Set
            

namespace Adaptify.FSharp.Core


[<AutoOpen>]
module Missing = 
    open Adaptify.FSharp.Core
    module AdaptiveOption =
        let toOption (a : AdaptiveOptionCase<_,_,_>) =
            match a with
            | AdaptiveSome a -> Some a
            | AdaptiveNone -> None


namespace Aardvark.Rendering.Text

module Font =
    
    let create name style =
        FontSquirrel.Hack.Regular