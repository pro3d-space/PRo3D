namespace CorrelationDrawing
  open FSharp.Data.Adaptive

  module Option =
    open Aardvark.Base.MultimethodTest

    let flatten2 (a : option<option<option<'a>>>) =
      a |> Option.flatten
        |> Option.flatten

    let mapDefault (a : option<'a>) (f : 'a -> 'b) (def : 'b) =
      match a with
        | Some a -> f a
        | None   -> def
    

    let flattenModOpt (a : option<aval<option<'a>>>) =
      adaptive {
        match a with
          | None -> return None
          | Some b -> 
              let! c = b
              return c
      }

    let modMap (a : aval<Option<'a>>) (f : 'a -> aval<Option<'b>>) =
      adaptive {
        let! a = a
        return! match a with
                  | None -> AVal.constant None
                  | Some a -> (f a)
      }

    let extractOrDefault (a : aval<Option<'a>>) (f : 'a -> aval<'b>) (def : 'b) =
      adaptive {
        let! a = a
        return! match a with
                  | None -> AVal.constant def
                  | Some a -> f a
      }

    let modIsSome (a : aval<Option<'a>>) =
      adaptive {
        let! a = a
        return a.IsSome
      }