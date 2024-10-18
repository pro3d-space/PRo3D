namespace FSharp.Data.Adaptive

open Aardvark.FontProvider
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core

[<AutoOpen>]
module MissingFunctionality = 

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

    module List =
        let rec updateIf (p : 'a -> bool) (f : 'a -> 'a) (xs : list<'a>) = 
            match xs with
            | x :: xs ->
                if(p x) then (f x) :: updateIf p f xs
                else x :: updateIf p f xs
            | [] -> []
    
        let addWithoutDup e f list =
            match List.exists f list with
            | true -> list
            | false -> e :: list
    
    module PList =
        let append' (a : IndexList<_>) (b : IndexList<_>) =
            let rec doIt xs =
                match xs with
                | x::xs -> IndexList.prepend x (doIt xs)
                | [] -> b
            doIt (IndexList.toList a)
    
        let tryHead (a: IndexList<_>) =
            a |> IndexList.tryAt 0
    
        let rev (a: IndexList<_>) =
            a |> IndexList.toList |> List.rev |> IndexList.ofList
    
        let applyNonEmpty (func : IndexList<_> -> IndexList<_>) (a : IndexList<_>) =
            if IndexList.count a > 0 then (a |> func) else a
    
        let remove' (v : 'a) (list: IndexList<'a>) : IndexList<'a> =
            list |> IndexList.filter(fun x -> x <> v)
    
    module Option = 
        let fromBool v b =
            match b with 
            | true  -> Some v
            | false -> None


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
    
    type HackRegular = Aardvark.FontProvider.FontSquirrelProvider<Family = "Hack">

    let create name style =
        HackRegular.Font

namespace Aardvark.Base

[<AutoOpen>]
module Result =

    open System


    type ResultBuilder() =
        member x.Return(v :'v) : Result<'v,_> = Ok v
        member x.ReturnFrom(v : Result<_,_>) = v
        member x.Bind(m : Result<'a,'e>, f : 'a -> Result<'b,'e>) =
            match m with
            | Result.Ok v -> f v
            | Result.Error e -> Result.Error e

    let result = ResultBuilder()

    module Double =
        
        let tryParse (v : string) : Result<float, unit> =
            match Double.TryParse(v, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
            | (true, v) -> Result.Ok v
            | _ -> Result.Error ()

    module Int =
        
        let tryParse (v : string) : Result<int, unit> =
            match Int32.TryParse(v) with
            | (true, v) -> Result.Ok v
            | _ -> Result.Error()

    let mapError (f : 'e0 -> 'e1)  (v : Result<'ok,'e0>) =
        match v with
        | Result.Error e -> Result.Error (f e)
        | Result.Ok ok -> Result.Ok ok

    let error (e : 'e) = Result.Error e

    let defaultValue' (fallback : 'e -> 'a) (r : Result<'a,'e>)  =
        match r with
        | Result.Ok v -> Result.Ok v
        | Result.Error e -> Result.Ok (fallback e)