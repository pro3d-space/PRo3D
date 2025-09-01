#if INTERACTIVE
#r "nuget: Mono.Cecil, 0.11.6"
#else
module UnpackNativeDependencies
#endif

open System.IO
open System.IO.Compression
open System.Runtime.InteropServices

open Mono.Cecil

let extractNativeDependenciesForAssembly (trace : string -> unit) (os : OSPlatform) 
                                         (arch : Architecture) (assembly : string) 
                                         (dryRun : bool) (targetDir : string) =
        
    let platformByConvention =
        if os = OSPlatform.Windows then "windows"
        elif os = OSPlatform.OSX then "mac"
        elif os = OSPlatform.Linux then "linux"
        else "windows"

    let archByConvention = 
        if arch = Architecture.X64 then "AMD64"
        elif arch = Architecture.X86 then "x86"
        elif arch = Architecture.Arm64 then "ARM64"
        else "unknown"
    
    let folderStructureMatchesArchPlatform (fullName : string) = 
        let normalizedPathRev = fullName.Replace('/', '\\').Split('\\') |> Array.map _.ToLowerInvariant() |> Array.rev
        match normalizedPathRev with
        | [| file; arch; os |] when os = platformByConvention.ToLowerInvariant() && arch = archByConvention.ToLowerInvariant() -> 
            Some file
        | _ -> 
            None
            
    try
        use ass = AssemblyDefinition.ReadAssembly(assembly, Mono.Cecil.ReaderParameters())

        let res = ass.MainModule.Resources |> Seq.tryFind (fun r -> r.Name = "native.zip")
        match res with
        | Some (:? EmbeddedResource as res) -> 
            use s = res.GetResourceStream()
            use a = new ZipArchive(s, ZipArchiveMode.Read)
            trace $"processing {Path.GetFileName assembly} ({a.Entries.Count} entries)" 

            for u in a.Entries do
                try 
                    match folderStructureMatchesArchPlatform u.FullName with
                    | None -> 
                        trace $"skipping: {assembly}/{u.FullName}"
                    | Some fileName -> 
                        trace $"extract: {assembly}/{u.FullName} => {targetDir}"
                        if not dryRun then
                            u.ExtractToFile(Path.Combine(targetDir, u.Name), true)
                with e -> 
                    trace $"failed to extract entry: {u.FullName}. (exn = {e.Message}, continuing with other elements)"
        | _ -> 
            trace $"no native.zip resource found in assembly {assembly}"
    with e -> 
        trace $"could not process assembly {assembly} (exn = {e.Message})"