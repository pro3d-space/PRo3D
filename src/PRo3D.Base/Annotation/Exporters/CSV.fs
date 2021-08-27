namespace PRo3D.Base.Annotation

open System
open System.IO
open Microsoft.FSharp.Reflection
open FSharp.Data.Adaptive

open Aardvark.Base

//TODO TO revise csv exporter, make baselib maybe

module CSV =

    // Code from http://www.fssnip.net/3U/title/CSV-writer ----------------------------------------------------------------
    type Array =
        static member join delimiter xs = 
            xs 
            |> Array.map (fun x -> x.ToString())
            |> String.concat delimiter

    type Seq =
        static member write (path:string) (data:seq<'a>)  = 
            use writer = new StreamWriter(path)
            data
            |> Seq.iter writer.WriteLine

        static member csv (separator:string) (useEnclosure:bool) (headerMapping:string -> string) ( data:seq<'a>) =
            seq {
                let dataType = typeof<'a>
                let stringSeqDataType = typeof<System.Collections.Generic.IEnumerable<string>>
                let inline enclose s =
                    match useEnclosure with
                    | true -> "\"" + (string s) + "\""
                    | false -> string s

                let header = 
                    match dataType with
                    | ty when FSharpType.IsRecord ty ->
                        FSharpType.GetRecordFields dataType
                        |> Array.map (fun info -> headerMapping info.Name)                    
                    | ty when FSharpType.IsTuple ty -> 
                        FSharpType.GetTupleElements dataType
                        |> Array.mapi (fun idx info -> headerMapping(string idx) )
                    | ty when ty.IsAssignableFrom stringSeqDataType ->
                        data :?> seq<seq<string>> |> Seq.head
                        |> Seq.toArray
                    | _ -> dataType.GetProperties()
                        |> Array.map (fun info -> headerMapping info.Name)

                yield header |> Array.map enclose |> Array.join separator
                                    
                let lines =
                    match dataType with 
                    | ty when FSharpType.IsRecord ty -> 
                        data |> Seq.map FSharpValue.GetRecordFields
                    | ty when FSharpType.IsTuple ty ->
                        data |> Seq.map FSharpValue.GetTupleFields
                    | ty when ty.IsAssignableFrom stringSeqDataType ->
                        data :?> seq<seq<string>> |> Seq.tail
                        |> Seq.map (fun ss -> Seq.toArray ss |> Array.map (fun s -> s :> obj) )
                    | _ -> 
                        let props = dataType.GetProperties()
                        data 
                        |> Seq.map ( fun line -> 
                            props 
                            |> Array.map (fun prop ->
                                prop.GetValue(line, null) 
                            )
                        )
                    |> Seq.map (Array.map enclose)
                yield! lines |> Seq.map (Array.join separator)
            }

