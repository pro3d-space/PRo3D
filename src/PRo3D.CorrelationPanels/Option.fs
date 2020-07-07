namespace CorrelationDrawing
  open Aardvark.Base.Incremental

  module Option =
    open Aardvark.Base.MultimethodTest

    let flatten2 (a : option<option<option<'a>>>) =
      a |> Option.flatten
        |> Option.flatten

    let mapDefault (a : option<'a>) (f : 'a -> 'b) (def : 'b) =
      match a with
        | Some a -> f a
        | None   -> def
    

    let flattenModOpt (a : option<IMod<option<'a>>>) =
      adaptive {
        match a with
          | None -> return None
          | Some b -> 
              let! c = b
              return c
      }

    let modMap (a : IMod<Option<'a>>) (f : 'a -> IMod<Option<'b>>) =
      adaptive {
        let! a = a
        return! match a with
                  | None -> Mod.constant None
                  | Some a -> (f a)
      }

    let extractOrDefault (a : IMod<Option<'a>>) (f : 'a -> IMod<'b>) (def : 'b) =
      adaptive {
        let! a = a
        return! match a with
                  | None -> Mod.constant def
                  | Some a -> f a
      }

    let modIsSome (a : IMod<Option<'a>>) =
      adaptive {
        let! a = a
        return a.IsSome
      }