namespace DS

open System
open FSharp.Data.Adaptive
open Aardvark.Base

module HMap =
    
    let toSortedPlist (input : HashMap<'k,'a>) (projection : ('a -> 'b)) : IndexList<'a> =
        input 
        |> HashMap.toSeq 
        |> Seq.map snd
        |> Seq.sortBy projection
        |> IndexList.ofSeq
    
    let filterNone (input : HashMap<'k,option<'a>>) =
        input 
        |> HashMap.filter (fun k v -> v.IsSome)
        |> HashMap.map (fun k v -> v.Value)
    
    let toPList (input : HashMap<_,'a>)  : IndexList<'a> =
        input 
        |> HashMap.toSeq 
        |> Seq.map snd
        |> IndexList.ofSeq
    
    let values  (input : HashMap<'k,'a>)  : list<'a> =
        input 
        |> HashMap.toSeq 
        |> Seq.map snd
        |> List.ofSeq
    
    let keys  (input : HashMap<'k,'a>)  : list<'k> =
        input 
        |> HashMap.toSeq 
        |> Seq.map fst
        |> List.ofSeq
    
    let toPairList (input : HashMap<'k,'a>)  : list<'k * 'a> =
        let keys = (keys input)
        let values = (values input)
        List.zip  keys values
    
    let toSwappedPairList (input : HashMap<'k,'a>)  : list<'a * 'k> =
        let keys = (keys input)
        let values = (values input)
        List.zip values keys
    
    let inline negate2 (f : 'a -> 'b -> bool) (a : 'a) (b : 'b) =
        not (f a b)
    
    let split (input : HashMap<'k,'a>) (f : 'k -> 'a -> bool) =
        let trueMap = HashMap.filter f input
        let falseMap = HashMap.filter (negate2 f) input
        (trueMap, falseMap)

    let pivot input =
        input
        |> HashMap.toList                    
        |> List.fold (fun acc (v,k) -> //switching keys and values
            acc 
            |> HashMap.update k (fun x ->
                match x with
                | Some values -> v :: values
                | None -> [v]
            )
        ) HashMap.empty