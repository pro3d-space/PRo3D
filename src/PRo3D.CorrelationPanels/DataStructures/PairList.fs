namespace DS

  module PairList =
    let filterNone (lst : List<'a * option<'b>>) =
      lst 
        |> List.filter (fun (a, opt) -> opt.IsSome)
        |> List.map (fun (a, opt) -> (a, opt.Value))