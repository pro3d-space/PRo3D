namespace DS


  module Seq =
    let properPairwiseOpt (f : 'a -> 'a -> 'b) (neutral : 'b) (s : seq<Option<'a>>) =
      s
        |> Seq.chunkBySize 2
        |> Seq.map 
          (fun arr -> match arr with
                        | [| a;b |] -> match a, b with
                                        | Some c, Some d -> Some (f c d)
                                        | _ -> None
                        | _ -> None)

    let properPairwise (f : 'a -> 'a -> 'b) (neutral : 'b) (s : seq<'a>) =
      s
        |> Seq.chunkBySize 2
        |> Seq.map 
          (fun arr -> match arr with
                        | [| a;b |] -> (f a b)
                        | _ -> neutral)