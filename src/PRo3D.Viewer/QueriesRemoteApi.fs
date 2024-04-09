namespace PRo3D.Base

open Aardvark.Base


module QueryApi =

    open Thoth.Json.Net

    type Request =
        {
            annotationId : string
            queryAttributes : list<string>
            distanceToPlane : float
        }

    let v3d (v : V3d) =
        [| v.X; v.Y; v.Z |] |> Array.map Encode.float |> Encode.array 

    let encodeAttribute (q : QueryAttribute) =
        if q.channels <> 1 then failwith "not implemented"
        match q.array with
        | :? array<float> as arr -> 
            arr |> Array.map Encode.float |> Encode.array
        | _ -> 
            failwith "not implemented."


    let encodeQueryResult (h : QueryResult) =
        Encode.object [
            "verticesWorldSpace", Encode.array (h.globalPositions |> Seq.map v3d |> Seq.toArray)
            "indices", Encode.array (h.indices |> Seq.toArray |> Array.map Encode.int)
            for (n, a) in h.attributes |> Map.toSeq do
                n, encodeAttribute a
        ]

    let hitsToJson (hits : seq<QueryResult>) =
        Encode.object [
            "filteredPatches", Encode.array [|
                for h in hits do
                    yield encodeQueryResult h
            |]
        ] |> Encode.toString 2

    let parseRequest (s : string) : Result<Request, _> =
        Decode.Auto.fromString s