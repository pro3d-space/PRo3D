namespace CorrelationDrawing

  module String =     
    open Aardvark.Base

    let fromV3d (v : V3d) =
      sprintf "(%.2f,%.2f,%.2f)" v.X v.Y v.Z

    let trimSharp (str : string) =
      match (str.StartsWith "#") with 
        | true  -> (str.TrimStart '#')
        | false -> str
  
    let hexToInt (hex : char) =
      match hex with
        | c when hex >= '0' && hex <= '9'  -> Some ((int hex) - (int '0'))
        | c when hex >= 'A' && hex <= 'F'  -> Some ((int c) - (int 'A') + 10)
        | c when hex >= 'a' && hex <= 'f'  -> Some ((int c) - (int 'a') + 10)
        | _ -> None

    let explode (str : string) =
      seq {
        for i in 0..(str.Length - 1) do
          yield str.Chars i
      }


    let hex2StrToInt (str : string) =
      let hexSeq =
        str
          |> trimSharp
          |> explode
          |> (Seq.map hexToInt)
      
      let check =
        hexSeq
          |> Seq.map (fun x-> x.IsSome)
          |> Seq.reduce (fun x y -> x && y)

      let ans = 
        match check with
          | true  -> (hexSeq
                        |> Seq.filter (fun x -> x.IsSome)
                        |> Seq.map (fun x -> x.Value)
                        |> DS.Seq.properPairwise (fun x y -> x + y) 0)  
          | false -> Seq.empty
      ans


