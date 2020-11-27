namespace PRo3D.SimulatedViews


open Aardvark.Base
open Chiron

module Json =
      let parseOption (x : Json<Option<'a>>) (f : 'a -> 'b) = 
          x |> Json.map (fun x -> x |> Option.map (fun y -> f y))
      let writeOption (name : string) (x : option<'a>) =
        match x with
        | Some a ->
            Json.write name (a.ToString ())
        | None ->
            Json.writeNone name
      let writeOptionList (name : string) 
                       (x : option<List<'a>>) 
                       (f : List<'a> -> string -> Json<unit>) = //when 'a : (static member ToJson : () -> () )>>) =
        match x with
        | Some a ->
            f a name
        | None ->
            Json.writeNone name
      let writeOptionFloat (name : string) (x : option<float>) =
        match x with
        | Some a ->
            Json.write name a
        | None ->
            Json.writeNone name

      let writeOptionInt (name : string) (x : option<int>) =
        match x with
        | Some a ->
            Json.write name a
        | None ->
            Json.writeNone name