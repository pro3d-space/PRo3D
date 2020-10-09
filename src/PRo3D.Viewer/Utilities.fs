namespace PRo3D

open System
open System.IO

open Aardvark.Base
open Aardvark.UI
open PRo3D
open OpcViewer.Base

open PRo3D
open PRo3D.Base.Annotation

module Dialogs =    
        
    let onChooseFiles (chosen : list<string> -> 'msg) =
        let cb xs =
            match xs with
            | [] -> chosen []
            | x::[] when x <> null -> 
                x 
                |> Aardvark.Service.Pickler.json.UnPickleOfString 
                |> List.map Aardvark.Service.PathUtils.ofUnixStyle 
                |> chosen
            | _ -> 
                chosen []
        onEvent "onchoose" [] cb   

    let onChooseDirectory (id:Guid) (chosen : Guid * string -> 'msg) =
        let cb xs =
            match xs with
            | [] -> chosen (id, String.Empty)
            | x::[] when x <> null -> 
                let id = id
                let path = 
                    x 
                    |> Aardvark.Service.Pickler.json.UnPickleOfString 
                    |> List.map Aardvark.Service.PathUtils.ofUnixStyle 
                    |> List.tryHead
                match path with
                | Some p -> 
                  chosen (id, p)
                | None -> chosen (id,String.Empty)
            | _ -> 
                chosen (id,String.Empty)
        onEvent "onchoose" [] cb   

    let onSaveFile (chosen : string -> 'msg) =
        let cb xs =
            match xs with
            | x::[] when x <> null -> 
                x 
                |> Aardvark.Service.Pickler.json.UnPickleOfString 
                |> Aardvark.Service.PathUtils.ofUnixStyle 
                |> chosen
            | _ -> 
                chosen String.Empty //failwithf "onSaveFile: %A" xs
        onEvent "onsave" [] cb

    let onSaveFile1 (chosen : string -> 'msg) (path : Option<string>) =
        let cb xs =
            match path with
            | Some p-> p |> chosen
            | None ->
                match xs with
                | x::[] when x <> null -> 
                    x |> Aardvark.Service.Pickler.json.UnPickleOfString |> Aardvark.Service.PathUtils.ofUnixStyle |> chosen
                | _ -> 
                    String.Empty |> chosen
        onEvent "onsave" [] cb
          
module Box3d =
    let extendBy (box:Box3d) (b:Box3d) =
        box.ExtendBy(b)
        box

    let ofSeq (bs:seq<Box3d>) =
        let box = 
            match bs |> Seq.tryHead with
            | Some b -> b
            | None -> failwith "box sequence must not be empty"
                    
        for b in bs do
            box.ExtendBy(b)
        box

module Mod =
    open FSharp.Data.Adaptive

    let bindOption (m : aval<Option<'a>>) (defaultValue : 'b) (project : 'a -> aval<'b>)  : aval<'b> =
        m
        |> AVal.bind (
            function | None -> AVal.constant defaultValue | Some v -> project v
        )

module Copy =
    let rec copyAll' (source : DirectoryInfo) (target : DirectoryInfo) skipExisting =
        
        // Check if the target directory exists, if not, create it.
        if not(Directory.Exists target.FullName) then
            Directory.CreateDirectory target.FullName |> ignore

        // Copy each file into it's new directory.
        for fi in source.GetFiles() do
             let sourceFile = fi.FullName
             let targetFile = Path.Combine(target.FullName, fi.Name)

             if ((sourceFile.ToLower() == targetFile.ToLower()) || (skipExisting && File.Exists(targetFile))) then
                Log.warn "Skipping %s, already exists" targetFile
             else
                Log.line "Copying to %s" targetFile
                fi.CopyTo(Path.Combine((target.ToString()), fi.Name), true) |> ignore      
                
        // Copy each subdirectory using recursion.
        let bla = source.GetDirectories()
        for srcSubDir in bla do
            let nextTgtSubDir = target.CreateSubdirectory(srcSubDir.Name)
            copyAll' srcSubDir nextTgtSubDir skipExisting

    let copyAll source target skipExisting=
        let s = DirectoryInfo(source)
        let t = DirectoryInfo(target)

        copyAll' s t skipExisting

module Console =    

    let print (x:'a) : 'a =
        printfn "%A" x
        x

module Net =
    open System.Threading
    open Aardvark.UI
    let getClient () =
        use cancelToken = new CancellationTokenSource()
        let waitForClient =
            async {
                for i in 1..100 do
                    let wc = new System.Net.WebClient()
                    try
                        let lst = wc.DownloadString("http://localhost:54321/rendering/stats.json")
                        match String.length lst > 3 with
                        | true -> cancelToken.Cancel ()
                        | false -> do! Async.Sleep 1000
                    with ex -> do! Async.Sleep 1000
            }
        try Async.RunSynchronously (waitForClient, -1, cancelToken.Token) with e -> ()
        let wc = new System.Net.WebClient()
        let jsonString = wc.DownloadString("http://localhost:54321/rendering/stats.json")
        let clientStats : list<PRo3D.Base.Utilities.ClientStatistics> =
            Pickler.unpickleOfJson jsonString
        (wc, clientStats)
