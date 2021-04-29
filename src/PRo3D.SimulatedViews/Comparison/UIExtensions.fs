namespace Aardvark.UI

open Aardvark.Base
open FSharp.Data.Adaptive

module Incremental =
    let always (k : string) (v : aval<string>) =
        let att = v |> AVal.map (fun v -> attribute k v)
        let v = att |> AVal.map (fun x -> x |> snd |> Some)
        k, v

