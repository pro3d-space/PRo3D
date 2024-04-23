namespace Aardvark.Fake

open System
open System.Reflection
open System.IO
open System.IO.Compression
open Fake
open Fake.Core
open Fake.IO
open System.Runtime.InteropServices

[<AutoOpen>]
module PathHelpersAssembly =
    type Path with
        static member ChangeFilename(path : string, newName : string -> string) =
            let dir = Path.GetDirectoryName(path)
            let name = Path.GetFileNameWithoutExtension path
            let ext = Path.GetExtension(path)
            Path.Combine(dir, (newName name) + ext)


module AssemblyResources =
    open System
    open Mono.Cecil
    open System.IO
    open System.IO.Compression
    open System.Collections.Generic

    let rec addFolderToArchive (path : string) (folder : string) (archive : ZipArchive) =
        let files = Directory.GetFiles(folder)
        for f in files do
            printfn "adding file: %A" f
            archive.CreateEntryFromFile(f, Path.Combine(path, Path.GetFileName f)) |> ignore
            ()

        let sd = Directory.GetDirectories(folder)
        for d in sd do
            let p = Path.Combine(path, Path.GetFileName d)
            addFolderToArchive p d archive

    let useDir d f =
        let old = System.Environment.CurrentDirectory
        System.Environment.CurrentDirectory <- d
        try
            let r = f ()
            r
        finally
            System.Environment.CurrentDirectory <- old

    let addFolder' (folder : string) (assemblyPath : string) (symbols : bool) =
        
        useDir (Path.Combine("bin","Release")) (fun () -> 
            let pdbPath = Path.ChangeExtension(assemblyPath, "pdb")

            let symbols = 
                // only process symbols if they exist and we are on not on unix like systems (they use mono symbols). 
                // this means: at the moment only windows packages support pdb debugging.
                File.Exists (pdbPath) && System.Environment.OSVersion.Platform <> PlatformID.Unix && symbols

            let bytes = new MemoryStream(File.ReadAllBytes assemblyPath)

            let pdbStream =
                if symbols then
                    new MemoryStream(File.ReadAllBytes pdbPath)
                else
                    null


            let r = ReaderParameters()
            if symbols then
                r.SymbolReaderProvider <- Mono.Cecil.Pdb.PdbReaderProvider()
                r.SymbolStream <- pdbStream
                r.ReadSymbols <- symbols
            let a = AssemblyDefinition.ReadAssembly(bytes,r)


            //let a = AssemblyDefinition.ReadAssembly(assemblyPath,ReaderParameters(ReadSymbols=symbols))
            // remove the old resource (if any)
            let res = a.MainModule.Resources |> Seq.tryFind (fun r -> r.Name = "native.zip")
            match res with
                | Some res -> a.MainModule.Resources.Remove res |> ignore
                | None -> ()

            let temp = System.IO.Path.GetTempFileName()
            let data =
                try
                    let mem = File.OpenWrite(temp)
                    let archive = new ZipArchive(mem, ZipArchiveMode.Create, true)
                    addFolderToArchive "" folder archive

                    // create and add the new resource
                    archive.Dispose()
                    mem.Close()
                    Trace.logfn "archive size: %d bytes" (FileInfo(temp).Length)
                    let b = File.ReadAllBytes(temp) //mem.ToArray()
                    Trace.logfn "archived native dependencies with size: %d bytes" b.Length
                    b
                finally
                    File.Delete(temp)


            let r = EmbeddedResource("native.zip", ManifestResourceAttributes.Public, data)
    
            a.MainModule.Resources.Add(r)
            a.Write(assemblyPath, WriterParameters(WriteSymbols = symbols))
            //a.Write(WriterParameters(WriteSymbols=symbols))
            a.Dispose()
//
//            let pdbPath = Path.ChangeExtension(assemblyPath, ".pdb")
//            let tempPath = Path.ChangeFilename(assemblyPath, fun a -> a + "Tmp")
//            let tempPdb = Path.ChangeExtension(tempPath, ".pdb")
//
//            a.Write( tempPath, WriterParameters(WriteSymbols=symbols))
//            a.Dispose()
//
//            File.Delete assemblyPath
//            File.Move(tempPath, assemblyPath)
//
//            if File.Exists tempPdb then
//                File.Delete pdbPath
//                File.Move(tempPdb, pdbPath)

            Trace.logfn "added native resources to %A" (Path.GetFileName assemblyPath)

        )
        
    let addFolder (folder : string) (assemblyPath : string) = 
        addFolder' folder assemblyPath true

    let getFilesAndFolders (folder : string) =
        if Directory.Exists folder then Directory.GetFileSystemEntries folder
        else [||]

    let copy (dstFolder : string) (source : string) =
        let f = FileInfo source
        if f.Exists then 
            if Directory.Exists dstFolder |> not then Directory.CreateDirectory dstFolder |> ignore
            Shell.copyFile dstFolder source
        else 
            let di = DirectoryInfo source
            if di.Exists then
                let dst = Path.Combine(dstFolder, di.Name)
                if Directory.Exists dst |> not then Directory.CreateDirectory dst |> ignore
                Shell.copyRecursive source dst true |> ignore
                ()

    let copyDependencies (folder : string) (targets : seq<string>) =
        let arch = 
            match RuntimeInformation.OSArchitecture with
            | Architecture.X64 -> "AMD64"
            | Architecture.X86 -> "x86"
            | _ -> "unknown"
        let targets = targets |> Seq.toArray

        let platform =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "windows"
            elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then "mac"
            elif RuntimeInformation.IsOSPlatform OSPlatform.Linux then "linux"
            else "windows"

        for t in targets do
            getFilesAndFolders(Path.Combine(folder, platform, arch)) 
                |> Seq.iter (copy t)

            getFilesAndFolders(Path.Combine(folder, platform)) 
                |> Array.filter (fun f -> 
                    let n = Path.GetFileName(f) 
                    n <> "x86" && n <> "AMD64"
                    )
                |> Seq.iter (copy t)

            getFilesAndFolders(Path.Combine(folder, arch)) 
                |> Seq.iter (copy t)

            getFilesAndFolders(folder) 
                |> Array.filter (fun f -> 
                    let n = Path.GetFileName(f) 
                    n <> "x86" && n <> "AMD64" && n <> "windows" && n <> "linux" && n <> "mac"
                    )
                |> Seq.iter (copy t)




module Helpers =

    open Fake.Core
    open Fake.Tools.Git

    let initializeContext () =
        let execContext = Context.FakeExecutionContext.Create false "build.fsx" (System.Environment.GetCommandLineArgs() |> Array.toList)
        Context.setExecutionContext (Context.RuntimeContext.Fake execContext)
        execContext

    module Proc =
        module Parallel =
            open System

            let locker = obj()

            let colors =
                [| ConsoleColor.Blue
                   ConsoleColor.Yellow
                   ConsoleColor.Magenta
                   ConsoleColor.Cyan
                   ConsoleColor.DarkBlue
                   ConsoleColor.DarkYellow
                   ConsoleColor.DarkMagenta
                   ConsoleColor.DarkCyan |]

            let print color (colored: string) (line: string) =
                lock locker
                    (fun () ->
                        let currentColor = Console.ForegroundColor
                        Console.ForegroundColor <- color
                        Console.Write colored
                        Console.ForegroundColor <- currentColor
                        Console.WriteLine line)

            let onStdout index name (line: string) =
                let color = colors.[index % colors.Length]
                if isNull line then
                    print color $"{name}: --- END ---" ""
                else if String.isNotNullOrEmpty line then
                    print color $"{name}: " line

            let onStderr name (line: string) =
                let color = ConsoleColor.Red
                if isNull line |> not then
                    print color $"{name}: " line

            let redirect (index, (name, createProcess)) =
                createProcess
                |> CreateProcess.redirectOutputIfNotRedirected
                |> CreateProcess.withOutputEvents (onStdout index name) (onStderr name)

            let printStarting indexed =
                for (index, (name, c: CreateProcess<_>)) in indexed do
                    let color = colors.[index % colors.Length]
                    let wd =
                        c.WorkingDirectory
                        |> Option.defaultValue ""
                    let exe = c.Command.Executable
                    let args = c.Command.Arguments.ToStartInfo
                    print color $"{name}: {wd}> {exe} {args}" ""

            let run cs =
                cs
                |> Seq.toArray
                |> Array.indexed
                |> fun x -> printStarting x; x
                |> Array.map redirect
                |> Array.Parallel.map Proc.run

    let createProcess exe arg dir =
        CreateProcess.fromRawCommandLine exe arg
        |> CreateProcess.withWorkingDirectory dir
        |> CreateProcess.ensureExitCode

    let dotnet = createProcess "dotnet"
    let npm =
        let npmPath =
            match ProcessUtils.tryFindFileOnPath "npm" with
            | Some path -> path
            | None ->
                "npm was not found in path. Please install it and make sure it's available from your path. " +
                "See https://safe-stack.github.io/docs/quickstart/#install-pre-requisites for more info"
                |> failwith

        createProcess npmPath

    let run proc arg dir =
        proc arg dir
        |> Proc.run
        |> ignore

    let runParallel processes =
        processes
        |> Proc.Parallel.run
        |> ignore

    let runOrDefault args =
        try
            match args with
            | [| target |] -> Target.runOrDefaultWithArguments target
            | _ -> Target.runOrDefaultWithArguments "Run"
            0
        with e ->
            printfn "%A" e
            1




    module NugetInfo = 
        let defaultValue (fallback : 'a) (o : Option<'a>) =
            match o with    
                | Some o -> o
                | None -> fallback

        let private adjust (v : PreRelease) =
            let o = 
                let number = v.Values |> List.tryPick  (function PreReleaseSegment.Numeric n -> Some n | _ -> None)
                match number with
                    | Some n -> sprintf "%s%04d" v.Name (int n)
                    | None -> v.Name
            { v with
                Origin = o
                Values = [AlphaNumeric o]
            }

        let nextVersion (major : bool) (prerelease : bool) (v : string) =
            let v : SemVerInfo = SemVer.parse v

            let version = 
                match v.PreRelease with
                    | Some _ when prerelease -> { v with Original = None }
                    | Some _ -> { v with PreRelease = None; Original = None }
                    | _ ->
                        match major with
                            | false -> { v with Patch = v.Patch + 1u; Original = None }
                            | true -> { v with Minor = v.Minor + 1u; Patch = 0u; Original = None }


            if prerelease then
                let incrementPreRelease (s : PreReleaseSegment) =
                    let prefix = "prerelease"

                    let increment (number : string) =
                        match System.Int32.TryParse number with
                        | true, n -> Some <| bigint (n + 1)
                        | _ -> None

                    match s with
                    | Numeric n -> Numeric (n + bigint 1)
                    | AlphaNumeric str as o ->
                        if str.StartsWith prefix then
                            increment (str.Substring prefix.Length)
                            |> Option.map Numeric
                            |> Option.defaultValue o
                        else
                            o

                let pre = 
                    version.PreRelease |> Option.map (fun p ->
                        { p with Values = p.Values |> List.map incrementPreRelease }
                    )

                let def =
                    {
                        Origin = "prerelease1"
                        Name = "prerelease"
                        Values = [ AlphaNumeric "prerelease"; Numeric (bigint 1) ]
                    }
                { version with PreRelease = pre |> defaultValue def |> adjust |> Some  }.ToString()
            else
                { version with PreRelease = None}.ToString()

        let assemblyVersion (vstr : string) =
            let v : SemVerInfo = SemVer.parse vstr
            sprintf "%d.%d.0.0" v.Major v.Minor

    
        let getGitTag() =
            let ok,msg,errors = CommandHelper.runGitCommand "." "describe --abbrev=0"
            if ok && msg.Length >= 1 then
                let tag = msg.[0]
                tag
            else
                let err = sprintf "no tag: %A" errors
                Trace.traceError err
                failwith err