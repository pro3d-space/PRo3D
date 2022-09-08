open Aardvark.Fake
open Aardvark.Fake.Helpers
open System
open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet

open Fake.IO
open Fake.Api
open Fake.Tools.Git

open System.IO.Compression
open System.Runtime.InteropServices

initializeContext()


do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let notes = ReleaseNotes.load "PRODUCT_RELEASE_NOTES.md"
printfn "%A" notes

let solutionName = "src/PRo3D.sln"


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
                dirs "bin" "(^netcoreapp.*$)|(^net4.*$)|(^net5.0$)|^Debug$|^Release$" SearchOption.AllDirectories
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

let outDirs = [ @"bin\Debug\netcoreapp3.1"; @"bin\Release\netcoreapp3.1";  @"bin\Release\net5.0";  @"bin\Debug\net5.0"; ]
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

let aardiumVersion = "2.0.5"
    //let versions = getInstalledPackageVersions()
    //match Map.tryFind "Aardium" versions with
    //| Some v -> v
    //| None -> failwith "no aardium version found"
    
    
Target.create "test" (fun _ -> 
    let url = sprintf "https://www.nuget.org/api/v2/package/Aardium-Win32-x64/%s" aardiumVersion
    printf "url: %s" url
    let tempFile = Path.GetTempFileName()
    use c = new System.Net.WebClient()
    c.DownloadFile(url, tempFile)
    use a = new ZipArchive(File.OpenRead tempFile)
    let t = Path.GetTempPath()
    let tempPath = Path.Combine(t, Guid.NewGuid().ToString())
    a.ExtractToDirectory(tempPath)
    let target = Path.Combine("bin", "publish")
    Shell.copyDir (Path.Combine(target,"tools")) (Path.Combine(tempPath,"tools")) (fun _ -> true)
)

let yarnName =
    if Environment.OSVersion.Platform = PlatformID.Unix || Environment.OSVersion.Platform = PlatformID.MacOSX then "yarn"
    else "yarn.cmd"

let npmName =
    if Environment.OSVersion.Platform = PlatformID.Unix || Environment.OSVersion.Platform = PlatformID.MacOSX then "npm"
    else "npm.cmd"

let yarn (args : list<string>) =
    let yarn =
        match ProcessUtils.tryFindFileOnPath yarnName with
            | Some path -> path
            | None -> failwith "could not locate yarn"

    let ret : ProcessResult<_> = 
        Command.RawCommand(yarn, Arguments.ofList args)
        |> CreateProcess.fromCommand
        |> CreateProcess.setEnvironmentVariable  "BUILD_VERSION" notes.NugetVersion
        |> CreateProcess.withWorkingDirectory "aardium"
        |> Proc.run
        //ProcessHelper.ExecProcess (fun info ->
        //     info.FileName <- yarn
        //     info.WorkingDirectory <- "Aardium"
        //     info.Arguments <- String.concat " " args
        //     ()
        // ) TimeSpan.MaxValue

    if ret.ExitCode <> 0 then
        failwith "yarn failed"
 

Target.create "InstallYarn" (fun _ ->

    match ProcessUtils.tryFindFileOnPath yarnName with
        | None ->
    
            match ProcessUtils.tryFindFileOnPath npmName with
                | Some npm ->
                    
                    let ret = 
                        Command.RawCommand(npm, Arguments.ofList ["install -g yarn"])
                        |> CreateProcess.fromCommand
                        |> Proc.run

                    if ret.ExitCode <> 0 then
                        failwith "npm install failed"
                | None ->
                    failwith "could not locate npm"   
        | _ ->
            Trace.tracefn "yarn already installed"
)

Target.create "Yarn" (fun _ ->
    yarn []
)

Target.create "PublishToElectron" (fun _ ->
    yarn ["install"]
    if RuntimeInformation.IsOSPlatform OSPlatform.Windows then 
        yarn ["dist"]
        //File.WriteAllBytes("Aardium/dist/Aardium-Linux-x64.tar.gz", [||]) |> ignore
        //File.WriteAllBytes("Aardium/dist/Aardium-Darwin-x64.tar.gz", [||]) |> ignore
    if RuntimeInformation.IsOSPlatform OSPlatform.Linux then 
        yarn ["dist"]
        //Directory.CreateDirectory "Aardium/dist/Aardium-win32-x64" |> ignore
        //File.WriteAllBytes("Aardium/dist/Aardium-Darwin-x64.tar.gz", [||]) |> ignore
    if RuntimeInformation.IsOSPlatform OSPlatform.OSX then 
        yarn ["dist"]
        //File.WriteAllBytes("Aardium/dist/Aardium-Linux-x64.tar.gz", [||]) |> ignore
        //Directory.CreateDirectory "Aardium/dist/Aardium-win32-x64" |> ignore
)
 
Target.create "CopyToElectron" (fun _ -> 

    if Directory.Exists "./aardium/build/build" then 
        Directory.Delete("./aardium/build/build", true)

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

    if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) then
         "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
             { o with
                 Framework = Some "net5.0"
                 Runtime = Some "osx-x64"
                 Common = { o.Common with CustomParams = Some "-p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
                 //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
                 Configuration = DotNet.BuildConfiguration.Release
                 VersionSuffix = Some notes.NugetVersion
                 OutputPath = Some "aardium/build/build"
             }
         )
         for f in System.IO.Directory.GetFiles("./lib/Native/JR.Wrappers/mac/") do    
            File.Copy(f, Path.Combine("aardium/build/build", Path.GetFileName f))
    else
        "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
            { o with
                Framework = Some "net5.0"
                Runtime = Some "win10-x64" 
                Common = { o.Common with CustomParams = Some "-p:PublishSingleFile=false -p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
                Configuration = DotNet.BuildConfiguration.Release
                VersionSuffix = Some notes.NugetVersion
                OutputPath = Some "aardium/build/build"
            }
        )
        File.Copy("data/runtime/vcruntime140.dll", "aardium/build/build/vcruntime140.dll")
        File.Copy("data/runtime/vcruntime140_1.dll", "aardium/build/build/vcruntime140_1.dll")
        File.Copy("data/runtime/msvcp140.dll", "aardium/build/build/msvcp140.dll")


    File.Copy("CREDITS.MD", "aardium/build/build/CREDITS.MD", true)
    File.Copy("CREDITS.MD", "aardium/CREDITS.MD", true)

)

"InstallYarn" ==> "CopyToElectron" ==> "PublishToElectron" |> ignore


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

    // 1. publish exe
    "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
        { o with
            Framework = Some "net5.0"
            Runtime = Some "win10-x64" //-p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
            Common = { o.Common with CustomParams = Some "-p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
            //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
            Configuration = DotNet.BuildConfiguration.Release
            VersionSuffix = Some notes.NugetVersion
            OutputPath = Some "bin/publish/win-x64"
        }
    )

    // 1. publish exe
    "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
        { o with
            Framework = Some "net5.0"
            Runtime = Some "osx-x64"
            Common = { o.Common with CustomParams = Some "-p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
            //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
            Configuration = DotNet.BuildConfiguration.Release
            VersionSuffix = Some notes.NugetVersion
            OutputPath = Some "bin/publish/mac-x64"
        }
    )


    // 1.1, copy most likely missing c++ libs, currently no reports of missing runtime libs
    //for dll in Directory.EnumerateFiles("data/runtime", "*.dll") do 
    //    let fileName = Path.GetFileName dll
    //    let target = Path.Combine("bin/publish/",fileName)
    //    Fake.Core.Trace.logfn "copying: %s -> %s" dll target
    //    File.Copy(dll, Path.Combine("bin/publish/",fileName))

    // 2, copy licences
    File.Copy("CREDITS.MD", "bin/publish/win-x64/CREDITS.MD", true)
    File.Copy("CREDITS.MD", "bin/publish/mac-x64/CREDITS.MD", true)

    File.Copy("data/runtime/vcruntime140.dll", "bin/publish/win-x64/vcruntime140.dll")
    File.Copy("data/runtime/vcruntime140_1.dll", "bin/publish/win-x64/vcruntime140_1.dll")
    File.Copy("data/runtime/msvcp140.dll", "bin/publish/win-x64/msvcp140.dll")

    // 3, resources (currently everything included)
    // copyResources ["bin/publish"] 
    
    if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)   then ()
    else
        let url = sprintf "https://www.nuget.org/api/v2/package/Aardium-Win32-x64/%s" aardiumVersion
        printf "url: %s" url
        let tempFile = Path.GetTempFileName()
        use c = new System.Net.WebClient()
        c.DownloadFile(url, tempFile)
        use a = new ZipArchive(File.OpenRead tempFile)
        let t = Path.GetTempPath()
        let tempPath = Path.Combine(t, Guid.NewGuid().ToString())
        a.ExtractToDirectory(tempPath)
        let target = Path.Combine("bin", "publish")
        Shell.copyDir (Path.Combine(target, "mac-x64", "tools")) (Path.Combine(tempPath, "tools")) (fun _ -> true)
        Shell.copyDir (Path.Combine(target, "win-x64", "tools")) (Path.Combine(tempPath, "tools")) (fun _ -> true)

        File.Move("bin/publish/win-x64/PRo3D.Viewer.exe", sprintf "bin/publish/win-x64/PRo3D.Viewer.%s.exe" notes.NugetVersion)
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
    File.Copy("bin/Debug/netstandard2.0/JR.Wrappers.dll", "lib/JR.Wrappers.dll", true)
)



"CompileInstruments" ==> "AddNativeResources" |> ignore
"AddNativeResources" ==> "CopyJRWrapper" ==> "Publish" |> ignore



Target.create "GitHubRelease" (fun _ ->
    let newVersion = notes.NugetVersion
    try
        try
            Branches.tag "." newVersion
            let token =
                match Environment.environVarOrDefault "github_token" "" with
                | s when not (System.String.IsNullOrWhiteSpace s) -> s
                | _ -> failwith "please set the github_token environment variable to a github personal access token with repro access."

            //let files = System.IO.Directory.EnumerateFiles("bin/publish") 
            let release = sprintf "bin/PRo3D.Viewer.%s.zip" notes.NugetVersion
            let z = System.IO.Compression.ZipFile.CreateFromDirectory("bin/publish/win-x64", release)

            GitHub.createClientWithToken token
            |> GitHub.draftNewRelease "vrvis" "PRo3D" notes.NugetVersion (notes.SemVer.PreRelease <> None) notes.Notes
            |> GitHub.uploadFiles (Seq.singleton release)
            //|> GitHub.publishDraft
            |> Async.RunSynchronously
            |> ignore
        with e -> 
            Branches.deleteTag "." newVersion
    finally
        ()
        //Branches.pushTag "." "origin" newVersion
        
)


"Publish" ==> "GithubRelease" |> ignore

Target.create "Run" (fun _ -> 
    Target.run 1 "AddNativeResources" []
)

"CompileInstruments" ==> "AddNativeResources" |> ignore
"AddNativeResources" ==> "CopyJRWrapper" ==> "Publish" |> ignore
"AddNativeResources" ==> "PublishToElectron" |> ignore
"Credits" ==> "PublishToElectron" |> ignore

[<EntryPoint>]
let main args = 
    printfn "args %A" args
    runOrDefault args