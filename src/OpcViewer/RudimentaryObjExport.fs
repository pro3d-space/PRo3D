namespace Aardvark.Base

open System
open System.IO
open System.Text


type WavefrontGeometry =
    {
        vertices : V3d[]
        indices  : int[]
        colors   : Option<C3b[]>
    }



module RudimentaryObjExport =

    let private writeV3d (v : V3d) (s : StringBuilder)  =
        s.Append(v.X.ToString(System.Globalization.CultureInfo.InvariantCulture))
         .Append(" ")
         .Append(v.Y.ToString(System.Globalization.CultureInfo.InvariantCulture))
         .Append(" ")
         .Append(v.Z.ToString(System.Globalization.CultureInfo.InvariantCulture))

    let private writeC3b (v : C3b) (s : StringBuilder) =
        let c = v.ToC3f()
        s.Append(c.R.ToString(System.Globalization.CultureInfo.InvariantCulture))
         .Append(" ")
         .Append(c.G.ToString(System.Globalization.CultureInfo.InvariantCulture))
         .Append(" ")
         .Append(c.B.ToString(System.Globalization.CultureInfo.InvariantCulture))


    let writeToBuilder (s : StringBuilder) (geometry : WavefrontGeometry) =
        geometry.colors |> Option.iter (fun c -> assert (geometry.vertices.Length = c.Length))
        s.AppendLine($"o {System.Guid.NewGuid()}") |> ignore
        for i in 0 .. geometry.vertices.Length - 1 do
            let v = geometry.vertices[i]
            s.Append("v ") |> ignore
            writeV3d v s |> ignore
            geometry.colors |> Option.iter (fun c -> s.Append(" ") |> writeC3b (c.[i]) |> ignore)
            s.AppendLine() |> ignore
        s.AppendLine("usemtl Material") |> ignore
        for i in 0 .. 3 .. geometry.indices.Length - 3 do
            s.Append("f ") |> ignore 
            s.AppendLine($"{geometry.indices[i]+1} {geometry.indices[i+1]+1} {geometry.indices[i+2]+1}") |> ignore
        s

    let writeToString (objects : seq<WavefrontGeometry>) =
        let s = Seq.fold writeToBuilder (StringBuilder()) objects
        s.ToString()