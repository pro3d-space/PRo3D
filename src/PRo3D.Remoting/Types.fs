#if INTERACTIVE
#r "nuget: Aardvark.Base"
#r "nuget: Thoth.Json.Net"
#else
namespace PRo3D
#endif
#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif

open Aardvark.Base

module Remoting = 

    type Camera = 
        {
            location : V3d
            forward  : V3d
            up       : V3d
        }

    type V3d with   
        static member Encode (v : V3d)= Encode.tuple3 Encode.float Encode.float Encode.float (v.X,v.Y,v.Z)
        static member Decoder : Decoder<V3d> = Decode.tuple3 Decode.float Decode.float Decode.float |> Decode.map (fun (x,y,z) -> V3d(x,y,z))

    let baseCoders =
        Extra.empty
        |> Extra.withCustom V3d.Encode V3d.Decoder

    let inline encoder<'T> = Encode.Auto.generateEncoderCached<'T>(caseStrategy = CamelCase, extra = baseCoders)
    let inline decoder<'T> = Decode.Auto.generateDecoderCached<'T>(caseStrategy = CamelCase, extra = baseCoders)

    module Camera =
        let toJson (v : Camera) = v |> encoder |> Encode.toString 4
        let fromJson (s : string) : Camera = Decode.unsafeFromString decoder s