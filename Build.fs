open Aardvark.Fake
open Aardvark.Fake.Helpers
open System
open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.Tools
open Fake.IO.Globbing.Operators

open Fake.IO
open Fake.Api
open Fake.Tools.Git

open System.IO.Compression
open System.Runtime.InteropServices
open System.Text.RegularExpressions

let ctx = initializeContext()

do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let notes = 
    if System.Environment.GetCommandLineArgs() |> Array.contains "--test" then 
        printfn "USING TEST RELEASE"
        ReleaseNotes.load "TEST_RELEASE_NOTES.md"
    else    
        ReleaseNotes.load "PRODUCT_RELEASE_NOTES.md"

printfn "%A" notes

let solutionName = "src/PRo3D.sln"
let framework = "net9.0"
let aardiumPath = Path.Combine("aardium", "Aardium")
let dotnetOutputPath = Path.Combine(aardiumPath, "build", "dotnet")

// detect the running architecture and turn it into the matching macOS RID
let osxArch =
    match RuntimeInformation.ProcessArchitecture with
    | Architecture.Arm64 -> Architecture.Arm64, "osx-arm64"
    | _                  -> Architecture.X64,   "osx-x64"

// copies the macOS JR.Wrappers native libs for the given arch into targetDir.
// x64 uses mac/x64/ and falls back to the legacy flat mac/ layout. arm64 uses
// only mac/arm64/ (never the flat x64 libs) — if it is missing we log and
// continue so an arm64 build does not crash before libCooTransformation.dylib
// is ported; the arm64 dylib gets dropped into lib/Native/JR.Wrappers/mac/arm64/.
let copyMacNativeLibs (arch : Architecture) (targetDir : string) =
    let archName = if arch = Architecture.Arm64 then "arm64" else "x64"
    let candidates =
        match arch with
        | Architecture.Arm64 -> [ "lib/Native/JR.Wrappers/mac/arm64" ]
        | _                  -> [ "lib/Native/JR.Wrappers/mac/x64"; "lib/Native/JR.Wrappers/mac" ]
    match candidates |> List.tryFind Directory.Exists with
    | Some src ->
        for f in Directory.GetFiles(src) do
            try File.Copy(f, Path.Combine(targetDir, Path.GetFileName f), true)
            with e -> Trace.tracefn "skipping native lib %s: %A" f e
    | None ->
        Trace.tracefn "no mac native libs for arch %s — continuing" archName

// keeps aardium/package.json's top-level "version" in sync with the release
// notes, so electron-builder's release tag (v{version}) matches the FAKE
// GitHubRelease tag and both publishers land in the same draft.
let patchAardiumVersion (version : string) =
    let path = Path.Combine(aardiumPath, "package.json")
    let text = File.ReadAllText path
    // only the first "version": "..." is the top-level field (before "build");
    // nested ones (buildVersion ${version}, deps) must stay untouched.
    let rx = Regex("\"version\"\\s*:\\s*\"[^\"]*\"")
    let patched = rx.Replace(text, sprintf "\"version\": \"%s\"" version, 1)
    File.WriteAllText(path, patched)


//Target.create "Compile" (fun _ ->
//    run dotnet "build" "src"
//)


Target.create "Compile" (fun _ ->
    let debug = false
    let cfg = if debug then "Debug" else "Release"
    
    let tag = 
        try 
            let tag = NugetInfo.getGitTag()
            let assemblyVersion = NugetInfo.assemblyVersion tag
            Some (tag, assemblyVersion)
        with _ -> None

    let props =
        [
            yield "Configuration", cfg
            match tag with
            | Some (tag, assemblyVersion) -> 
                yield "AssemblyVersion", assemblyVersion
                yield "AssemblyFileVersion", assemblyVersion
                yield "InformationalVersion", assemblyVersion
                yield "ProductVersion", assemblyVersion
                yield "PackageVersion", tag
            | _ -> ()
        ]

    "src/PRo3D.sln" |> DotNet.build (fun o ->
        { o with
            NoRestore = false 
            Configuration = if debug then DotNet.BuildConfiguration.Debug else DotNet.BuildConfiguration.Release
            MSBuildParams =
                { o.MSBuildParams with
                    Properties = props
                    DisableInternalBinLog = true
                }
        }
    )
)

Target.create "AddNativeResources" (fun _ ->
        let dir =
            if Directory.Exists "libs/Native" then Some "libs/Native"
            elif Directory.Exists "lib/Native" then Some "lib/Native"
            else None

        let dirs (dir : string) (pat : string) (o : SearchOption) =
            if Directory.Exists dir then
                let rx = System.Text.RegularExpressions.Regex pat
                Directory.GetDirectories(dir, "*", o) 
                |> Array.filter (Path.GetFileName >> rx.IsMatch)
                |> Array.map Path.GetFullPath
            else
                [||]   

        let files (dir : string) (pat : string) (o : SearchOption) =
            if Directory.Exists dir then
                let rx = System.Text.RegularExpressions.Regex pat
                Directory.GetFiles(dir, "*", o) 
                |> Array.filter (Path.GetFileName >> rx.IsMatch)
                |> Array.map Path.GetFullPath
            else
                [||]                


        let binDirs =
            (
                dirs "bin" "(^netcoreapp.*$)|(^net[0-9]+\.[0-9]+$)|^Debug$|^Release$" SearchOption.AllDirectories
                |> Array.toList
            )



        match dir with
            | Some dir ->
                for d in Directory.GetDirectories dir do
                    let n = Path.GetFileName d
                    let d = d |> Path.GetFullPath

                    let paths = 
                        Array.concat [
                            files "bin/Release" (@"^.*\.(dll|exe)$") SearchOption.AllDirectories
                            files "bin/Debug" (@"^.*\.(dll|exe)$") SearchOption.AllDirectories
                        ]                        
                        |> Array.filter (fun p -> 
                            Path.GetFileNameWithoutExtension(p).ToLower() = n.ToLower()
                        )

                    AssemblyResources.copyDependencies d binDirs

                    for p in paths do
                        if File.Exists p then
                            try 
                                Trace.logfn "adding folder %A to %A p" d p
                                AssemblyResources.addFolder d p
                            with e -> 
                                Trace.logfn "could not add folder  %A to assembly %A with %A, retrying without symbols" d p e
                                AssemblyResources.addFolder' d p false 
            | None ->
                ()
    )

let outDirs = [ @"bin\Debug\" + framework; @"bin\Release" + framework ]
let resources = 
    [
        //"lib\Dependencies\PRo3D.Base\windows"; // currently handled by native dependency injection mechanism 
        //"lib/groupmappings"
        //"./lib/Native/JR.Wrappers/mac/"
    ]


let copyResources outDirs = 
    for r in resources do
        for outDir in outDirs do
            if Directory.Exists outDir then
                if Path.isDirectory r then 
                    printfn "copying dir %s => %s" r outDir
                    Shell.copyDir outDir r (fun _ -> true)
                else 
                    printfn "copying file %s => %s" r outDir
                    Shell.copyFile outDir r

Target.create "CopyResources" (fun _ -> 
    copyResources outDirs
)

"Compile" ==> "CopyResources" ==> "AddNativeResources" |> ignore

Target.create "Credits" (fun _ -> 
    let allLicences = 
        seq {
            yield! Directory.EnumerateFiles("3rdPartyLICENSES/","*.txt") 
            yield! Directory.EnumerateFiles("3rdPartyLICENSES/","*.md") 
        }
    let template = File.ReadAllText "3rdPartyLICENSES/CreditsTemplate"

    let summary = allLicences |> Seq.map Path.GetFileNameWithoutExtension 

    let normalizeName (s : string) = s.Replace("-LICENSE","").Replace("_LICENSE","").Replace("_",".")
   
    let credits = 
        template.Replace("__PACKAGES__", summary 
        |> Seq.map (normalizeName >> sprintf " - %s")
        |> String.concat Environment.NewLine)
    printfn "%s" credits

    let licences = 
        allLicences 
        |> Seq.map (fun file -> 
            if Path.GetExtension file = ".md" then
                sprintf "## %s\n\n\n```%s\n```\n" (file |> Path.GetFileNameWithoutExtension |> normalizeName) (File.ReadAllText file)
            else
                sprintf "## %s\n\n\n```%s\n```\n" (file |> Path.GetFileNameWithoutExtension |> normalizeName) (File.ReadAllText file))
        |> String.concat System.Environment.NewLine

    let credits = credits.Replace("__LICENCES__", licences)

    File.WriteAllText("CREDITS.MD", credits)
)


let r = System.Text.RegularExpressions.Regex("let viewerVersion.*=.*\"(.*)\"")
let test = """let viewerVersion       = "3.1.3" """

(*let getInstalledPackageVersions() =
    //Build Fake.DotNet.Cli - 5.19.1
    let regex = Regex @"^([a-zA-Z_0-9]+)[ \t]*([^ ]+)[ \t]*-[ \t]*(.+)$"

    let paketPath = 
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then @".paket/paket.exe"
        else ".paket/paket"

    let paketPath = System.IO.Path.GetFullPath paketPath
    let startInfo = new ProcessStartInfo()
    startInfo.FileName <- paketPath
    startInfo.Arguments <- "show-installed-packages"
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true

    let proc = Process.Start(startInfo)
    proc.WaitForExit()

    let mutable res = Map.empty

    if proc.ExitCode = 0 then
        while not proc.StandardOutput.EndOfStream do
            let line = proc.StandardOutput.ReadLine()
            let m = regex.Match line
            if m.Success then
                let g = m.Groups.[1].Value.Trim().ToLower()
                match g with
                | "main" -> 
                    let n = m.Groups.[2].Value
                    let v = m.Groups.[3].Value |> SemVer.parse
                    res <- Map.add n v res
                | _ ->
                    ()

    res
    *)

let aardiumVersion = "3.0.2"
    //let versions = getInstalledPackageVersions()
    //match Map.tryFind "Aardium" versions with
    //| Some v -> v
    //| None -> failwith "no aardium version found"
    
    
Target.create "Tests" (fun _ -> 
    DotNet.test (fun o -> 
        { o with
            NoRestore = false 
            MSBuildParams =
                { o.MSBuildParams with
                    DisableInternalBinLog = true
                }
        }
    ) "./src/Tests/Tests.fsproj"
)

let npmName =
    if Environment.OSVersion.Platform = PlatformID.Unix || Environment.OSVersion.Platform = PlatformID.MacOSX then "npm"
    else "npm.cmd"

let npm (args : string list) =
    let npm =
        match ProcessUtils.tryFindFileOnPath npmName with
            | Some path -> path
            | None -> failwith "could not locate npm"

    let ret : ProcessResult<_> = 
        Command.RawCommand(npm, Arguments.ofList args)
        |> CreateProcess.fromCommand
        |> CreateProcess.withWorkingDirectory aardiumPath
        |> Proc.run

    if ret.ExitCode <> 0 then
        failwith "npm failed"

Target.create "PublishToElectron" (fun _ ->
    npm ["install"]
    if RuntimeInformation.IsOSPlatform OSPlatform.Windows then 
        npm ["run"; "publish"]
        //File.WriteAllBytes("Aardium/dist/Aardium-Linux-x64.tar.gz", [||]) |> ignore
        //File.WriteAllBytes("Aardium/dist/Aardium-Darwin-x64.tar.gz", [||]) |> ignore
    if RuntimeInformation.IsOSPlatform OSPlatform.Linux then
        npm ["run"; "publish"]
        //Directory.CreateDirectory "Aardium/dist/Aardium-win32-x64" |> ignore
        //File.WriteAllBytes("Aardium/dist/Aardium-Darwin-x64.tar.gz", [||]) |> ignore
    if RuntimeInformation.IsOSPlatform OSPlatform.OSX then
        npm ["run"; "signbuild"]
        npm ["run"; "publish"]
        //File.WriteAllBytes("Aardium/dist/Aardium-Linux-x64.tar.gz", [||]) |> ignore
        //Directory.CreateDirectory "Aardium/dist/Aardium-win32-x64" |> ignore
)


let extractNativeDependenciesInFolder (os : OSPlatform) (arch : Architecture) (dir : string) =
    for assembly in Directory.EnumerateFiles(dir, "*.dll") do
        try
            UnpackNativeDependencies.extractNativeDependenciesForAssembly Trace.trace os arch assembly false dir
        with e -> 
            Trace.tracefn "could not add native dependencies to %s: %A" assembly e
 
Target.create "CopyToElectron" (fun _ -> 

    if Directory.Exists dotnetOutputPath then
        Directory.Delete(dotnetOutputPath, true)

    // 0.0 copy version over into source code...
    let programFs = File.ReadAllLines "src/PRo3D.Viewer/Program.fs"
    let patched =
        programFs
        |> Array.map (fun line ->
            if line.StartsWith "let viewerVersion" then
                sprintf "let viewerVersion       = \"%s\"" notes.NugetVersion
            else line
        )
    File.WriteAllLines("src/PRo3D.Viewer/Program.fs", patched)

    // 0.1 keep aardium/package.json version in sync so electron-builder's
    // release tag (v{version}) matches the FAKE GitHubRelease draft tag.
    patchAardiumVersion notes.NugetVersion

    if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) then
         let arch, rid = osxArch
         "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
             { o with
                 Framework = Some framework
                 Runtime = Some rid
                 Common = { o.Common with CustomParams = Some "-p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
                 //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
                 Configuration = DotNet.BuildConfiguration.Release
                 VersionSuffix = Some notes.NugetVersion
                 OutputPath = Some dotnetOutputPath
                 MSBuildParams = { o.MSBuildParams with DisableInternalBinLog = true }
             }
         )
         copyMacNativeLibs arch dotnetOutputPath

         extractNativeDependenciesInFolder OSPlatform.OSX arch dotnetOutputPath

    elif System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) then
        "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
                { o with
                    Framework = Some framework
                    Runtime = Some "linux-x64"
                    Common = { o.Common with CustomParams = Some "-p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
                    //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
                    Configuration = DotNet.BuildConfiguration.Release
                    VersionSuffix = Some notes.NugetVersion
                    OutputPath = Some dotnetOutputPath
                    MSBuildParams = { o.MSBuildParams with DisableInternalBinLog = true } 
                }
        )
        for f in System.IO.Directory.GetFiles("./lib/Native/JR.Wrappers/linux/AMD64") do    
            let target = Path.Combine(dotnetOutputPath, Path.GetFileName f)
            printfn "copy: %s => %s" f target
            File.Copy(f, target)

        extractNativeDependenciesInFolder OSPlatform.Linux Architecture.X64 dotnetOutputPath

    else
        "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
            { o with
                Framework = Some framework
                Runtime = Some "win-x64" 
                Common = { o.Common with CustomParams = Some "-p:PublishSingleFile=false -p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
                Configuration = DotNet.BuildConfiguration.Release
                VersionSuffix = Some notes.NugetVersion
                OutputPath = Some dotnetOutputPath
                MSBuildParams = { o.MSBuildParams with DisableInternalBinLog = true } 
            }
        )
        extractNativeDependenciesInFolder OSPlatform.Windows Architecture.X64 dotnetOutputPath

        File.Copy("data/runtime/vcruntime140.dll", Path.Combine(dotnetOutputPath, "vcruntime140.dll"))
        File.Copy("data/runtime/vcruntime140_1.dll", Path.Combine(dotnetOutputPath, "vcruntime140_1.dll"))
        File.Copy("data/runtime/msvcp140.dll", Path.Combine(dotnetOutputPath, "msvcp140.dll"))




    File.Copy("CREDITS.MD", Path.Combine(dotnetOutputPath, "CREDITS.MD"), true)
    File.Copy("CREDITS.MD", Path.Combine(aardiumPath, "CREDITS.MD"), true)

)

"CopyToElectron" ==> "PublishToElectron" |> ignore

Target.create "TestUnpack" (fun _ -> 
    extractNativeDependenciesInFolder OSPlatform.Windows Architecture.X64 dotnetOutputPath
)

Target.create "Publish" (fun _ ->

    // 0.0 copy version over into source code...
    let programFs = File.ReadAllLines "src/PRo3D.Viewer/Program.fs"
    let patched = 
        programFs 
        |> Array.map (fun line -> 
            if line.StartsWith "let viewerVersion" then 
                sprintf "let viewerVersion       = \"%s\"" notes.NugetVersion 
            else line
        )
    File.WriteAllLines("src/PRo3D.Viewer/Program.fs", patched)

    if Directory.Exists "bin/publish" then 
        Directory.Delete("bin/publish", true)

    // vuewer
    "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
        { o with
            Framework = Some framework
            Runtime = Some "win-x64" //-p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
            Common = { o.Common with CustomParams = Some "-p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
            //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
            Configuration = DotNet.BuildConfiguration.Release
            VersionSuffix = Some notes.NugetVersion
            OutputPath = Some "bin/publish/win-x64"
            MSBuildParams = { o.MSBuildParams with DisableInternalBinLog = true } 
        }
    )

    //// snapshots
    "src/PRo3D.Snapshots/PRo3D.Snapshots.fsproj" |> DotNet.publish (fun o ->
        { o with
            Framework = Some framework
            Runtime = Some "win-x64" //-p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
            Common = { o.Common with CustomParams = Some "-p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
            //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
            Configuration = DotNet.BuildConfiguration.Release
            VersionSuffix = Some notes.NugetVersion
            OutputPath = Some "bin/publish/win-x64"
            MSBuildParams = { o.MSBuildParams with DisableInternalBinLog = true } 
            
        }
    )

    // mac (x64)
    "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
        { o with
            Framework = Some framework
            Runtime = Some "osx-x64"
            Common = { o.Common with CustomParams = Some "-p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
            //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
            Configuration = DotNet.BuildConfiguration.Release
            VersionSuffix = Some notes.NugetVersion
            OutputPath = Some "bin/publish/mac-x64"
            MSBuildParams = { o.MSBuildParams with DisableInternalBinLog = true }
        }
    )
    copyMacNativeLibs Architecture.X64 "bin/publish/mac-x64"
    extractNativeDependenciesInFolder OSPlatform.OSX Architecture.X64 "bin/publish/mac-x64"

    // mac (arm64)
    "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
        { o with
            Framework = Some framework
            Runtime = Some "osx-arm64"
            Common = { o.Common with CustomParams = Some "-p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
            //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
            Configuration = DotNet.BuildConfiguration.Release
            VersionSuffix = Some notes.NugetVersion
            OutputPath = Some "bin/publish/mac-arm64"
            MSBuildParams = { o.MSBuildParams with DisableInternalBinLog = true }
        }
    )
    copyMacNativeLibs Architecture.Arm64 "bin/publish/mac-arm64"
    extractNativeDependenciesInFolder OSPlatform.OSX Architecture.Arm64 "bin/publish/mac-arm64"


    // 1.1, copy most likely missing c++ libs, currently no reports of missing runtime libs
    //for dll in Directory.EnumerateFiles("data/runtime", "*.dll") do 
    //    let fileName = Path.GetFileName dll
    //    let target = Path.Combine("bin/publish/",fileName)
    //    Fake.Core.Trace.logfn "copying: %s -> %s" dll target
    //    File.Copy(dll, Path.Combine("bin/publish/",fileName))

    // 2, copy licences
    File.Copy("CREDITS.MD", "bin/publish/win-x64/CREDITS.MD", true)
    File.Copy("CREDITS.MD", "bin/publish/mac-x64/CREDITS.MD", true)
    File.Copy("CREDITS.MD", "bin/publish/mac-arm64/CREDITS.MD", true)

    File.Copy("data/runtime/vcruntime140.dll", "bin/publish/win-x64/vcruntime140.dll")
    File.Copy("data/runtime/vcruntime140_1.dll", "bin/publish/win-x64/vcruntime140_1.dll")
    File.Copy("data/runtime/msvcp140.dll", "bin/publish/win-x64/msvcp140.dll")

    // 3, resources (currently everything included)
    // copyResources ["bin/publish"] 
    
    if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)   then ()
    else
        // downloads the Aardium package matching each publish target and copies its
        // 'tools' (the platform-specific Electron host) next to the published viewer.
        // skips gracefully if the package is not published for that platform/arch.
        let copyAardiumTools (package : string) (publishDir : string) =
            let url = sprintf "https://www.nuget.org/api/v2/package/%s/%s" package aardiumVersion
            printfn "url: %s" url
            try
                let tempFile = Path.GetTempFileName()
                use c = new System.Net.WebClient()
                c.DownloadFile(url, tempFile)
                use a = new ZipArchive(File.OpenRead tempFile)
                let tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
                a.ExtractToDirectory(tempPath)
                Shell.copyDir (Path.Combine("bin", "publish", publishDir, "tools")) (Path.Combine(tempPath, "tools")) (fun _ -> true)
            with e ->
                Trace.tracefn "could not fetch %s for %s: %A — continuing" package publishDir e

        copyAardiumTools "Aardium-Win32-x64"   "win-x64"
        copyAardiumTools "Aardium-Darwin-x64"  "mac-x64"
        copyAardiumTools "Aardium-Darwin-arm64" "mac-arm64"

        //File.Move("bin/publish/win-x64/PRo3D.Viewer.exe", sprintf "bin/publish/win-x64/PRo3D.Viewer.%s.exe" notes.NugetVersion)
)

"Credits" ==> "Publish" |> ignore


Target.create "CompileDebug" (fun _ ->
    let cfg = "Debug" //if config.debug then "Debug" else "Release"
    
    let tag = 
        try 
            let tag = NugetInfo.getGitTag()
            let assemblyVersion = NugetInfo.assemblyVersion tag
            Some (tag, assemblyVersion)
        with _ -> None

    let props =
        [
            yield "Configuration", cfg
            match tag with
            | Some (tag, assemblyVersion) -> 
                yield "AssemblyVersion", assemblyVersion
                yield "AssemblyFileVersion", assemblyVersion
                yield "InformationalVersion", assemblyVersion
                yield "ProductVersion", assemblyVersion
                yield "PackageVersion", tag
            | _ -> ()
        ]

    solutionName |> DotNet.build (fun o ->
        { o with
            NoRestore = true 
            Configuration = DotNet.BuildConfiguration.Debug
            MSBuildParams =
                { o.MSBuildParams with
                    Properties = props
                    DisableInternalBinLog = true
                }
        }
    )
)

Target.create "CompileInstruments" (fun _ ->
    let cfg = "Debug" //if config.debug then "Debug" else "Release"
    
    let tag = 
        try 
            let tag = NugetInfo.getGitTag()
            let assemblyVersion = NugetInfo.assemblyVersion tag
            Some (tag, assemblyVersion)
        with _ -> None

    let props =
        [
            yield "Configuration", cfg
            match tag with
            | Some (tag, assemblyVersion) -> 
                yield "AssemblyVersion", assemblyVersion
                yield "AssemblyFileVersion", assemblyVersion
                yield "InformationalVersion", assemblyVersion
                yield "ProductVersion", assemblyVersion
                yield "PackageVersion", tag
            | _ -> ()
        ]

    "src/InstrumentPlatforms/JR.Wrappers.sln"|> DotNet.build (fun o ->
        { o with
            NoRestore = false 
            Configuration = DotNet.BuildConfiguration.Debug
            MSBuildParams =
                { o.MSBuildParams with
                    Properties = props
                    DisableInternalBinLog = true
                }
        }
    )

    "src/InstrumentPlatforms/JR.Wrappers.sln"|> DotNet.build (fun o ->
        { o with
            NoRestore = false 
            Configuration = DotNet.BuildConfiguration.Release
            MSBuildParams =
                { o.MSBuildParams with
                    Properties = props
                    DisableInternalBinLog = true
                }
        }
    )

)


Target.create "CopyJRWRapper" (fun _ -> 
    File.Copy("bin/Debug/netstandard2.1/JR.Wrappers.dll", "lib/JR.Wrappers.dll", true)
)



"CompileInstruments" ==> "AddNativeResources" |> ignore
"AddNativeResources" ==> "CopyJRWrapper" ==> "Publish" |> ignore



Target.create "GitHubRelease" (fun _ ->
    let newVersion = notes.NugetVersion
    let tagName = "v" + newVersion
    try
        try
            try Branches.tag "." tagName with e -> Trace.logf "could not create tag: %A" e
            let token =
                match Environment.environVarOrDefault "GH_TOKEN" "" with
                | s when not (System.String.IsNullOrWhiteSpace s) -> s
                | _ -> failwith "please set the github_token environment variable to a github personal access token with repro access."

            //let files = System.IO.Directory.EnumerateFiles("bin/publish")
            let release = sprintf "bin/PRo3D.Viewer-%s-win-x64-standalone.zip" notes.NugetVersion
            let z =
                if File.Exists release then
                    File.Delete(release)
                System.IO.Compression.ZipFile.CreateFromDirectory("bin/publish/win-x64", release)

            // record where the draft came from: append the commit + tag so the
            // release always states its source (the tag itself anchors the
            // published release to this commit once pushed below).
            let commit =
                match Environment.environVarOrNone "GITHUB_SHA" with
                | Some s when not (String.IsNullOrWhiteSpace s) -> s
                | _ -> try Information.getCurrentSHA1 "." with _ -> "unknown"
            let body = Seq.append notes.Notes [ ""; sprintf "_release %s — built from commit %s_" tagName commit ]

            // use the v-prefixed tag as the release tag_name so it matches both
            // the pushed git tag and electron-builder's default (v{version});
            // this is what makes the standalone zip and the electron artifacts
            // share one draft instead of two.
            let release =
                GitHub.createClientWithToken token
                |> GitHub.draftNewRelease "pro3d-space" "PRo3D" tagName (notes.SemVer.PreRelease <> None) body
                |> GitHub.uploadFiles (Seq.singleton release)
                //|> GitHub.publishDraft
                |> Async.RunSynchronously

            try Branches.pushTag "." "origin" tagName with e -> Trace.logf "could not create tag: %A" e

        with e -> 
            Trace.logf "failed to create github release: %A" e
            Branches.deleteTag "." tagName
    finally
        ()
        
)


Target.create "Pack" (fun _ ->
    let args = 
        [
            "paket"
            "pack"
            "--version"
            notes.NugetVersion
            "--interproject-references"
            "fix"
            "--release-notes"
            sprintf "\"%s\"" (String.concat "\\n" notes.Notes)
            "--project-url"
            "\"https://github.com/pro3d-space/PRo3D\""
            sprintf "\"%s\"" (Path.Combine(__SOURCE_DIRECTORY__, "bin"))
        ]
    let ret = 
        Process.shellExec {
            ExecParams.Program = "dotnet"
            WorkingDir = __SOURCE_DIRECTORY__
            CommandLine = String.concat " " args
            Args = []
        }
    if ret <> 0 then failwithf "paket failed with exit code %d" ret

    "./src/opc-tool/opc-tool.fsproj" |> DotNet.pack (fun o -> 
        { o with        
            NoRestore = true
            NoBuild = true
            MSBuildParams = { o.MSBuildParams with DisableInternalBinLog = true; Properties = ["Version", notes.NugetVersion] }
        }
    )
)

Target.create "Push" (fun _ ->
    let packageNameRx = Regex @"^(?<name>[a-zA-Z_0-9\.-]+?)\.(?<version>([0-9]+\.)*[0-9]+.*?)\.nupkg$"
    
    //if not (Git.Information.isCleanWorkingCopy ".") then
    //    Git.Information.showStatus "."
    //    failwith "repo not clean"

    
    if File.exists "deploy.targets" then
        let packages =
            !!"bin/*.nupkg"
            |> Seq.filter (fun path ->
                let name = Path.GetFileName path
                let m = packageNameRx.Match name
                if m.Success then
                    m.Groups.["version"].Value = notes.NugetVersion
                else
                    false
            )
            |> Seq.toList

        let targetsAndKeys =
            File.ReadAllLines "deploy.targets"
            |> Array.map (fun l -> l.Split(' '))
            |> Array.choose (function [|dst; key|] -> Some (dst, key) | _ -> None)
            |> Array.choose (fun (dst, key) ->
                let path = 
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".ssh",
                        key
                    )
                printfn "exists? %s" path
                if File.exists path then
                    let key = File.ReadAllText(path).Trim()
                    Some (dst, key)
                else
                    None
            )
            |> Map.ofArray
            
        
        //Git.CommandHelper.directRunGitCommandAndFail "." "fetch --tags"
        //Git.Branches.tag "." notes.NugetVersion

        //let branch = Git.Information.getBranchName "."
        //Git.Branches.pushBranch "." "origin" branch

        if List.isEmpty packages then
            failwith "no packages produced"

        if Map.isEmpty targetsAndKeys then
            failwith "no deploy targets"
            
        for (dst, key) in Map.toSeq targetsAndKeys do
            Trace.tracefn "pushing to '%s'." dst 
            let options (o : Paket.PaketPushParams) =
                { o with 
                    PublishUrl = dst
                    ApiKey = key 
                    WorkingDir = "bin"
                }

            Paket.pushFiles options packages

        //Git.Branches.pushTag "." "origin" notes.NugetVersion
    ()
)

"Publish" ==> "GithubRelease" |> ignore

Target.create "Run" (fun _ -> 
    Target.run 1 "AddNativeResources" []
)

"CompileInstruments" ==> "AddNativeResources" |> ignore
"AddNativeResources" ==> "CopyJRWrapper" ==> "Publish" |> ignore
"AddNativeResources" ==> "PublishToElectron" |> ignore
"Credits" ==> "PublishToElectron" |> ignore
"Compile" ==> "Pack" |> ignore
"Pack" ==> "Push" |> ignore

[<EntryPoint>]
let main args = 
    printfn "args %A" args
    runOrDefault args