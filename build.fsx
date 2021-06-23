#r "paket: groupref Build //"
#load ".fake/build.fsx/intellisense.fsx"
#load @"paket-files/build/aardvark-platform/aardvark.fake/DefaultSetup.fsx"

open System
open System.IO
open System.Diagnostics
open Aardvark.Fake
open Fake.Core
open Fake.Core.TargetOperators
open Fake.Tools
open Fake.IO.Globbing.Operators
open System.Runtime.InteropServices
open Fake.DotNet

open Fake.IO
open Fake.Api
open Fake.Tools.Git



do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let notes = ReleaseNotes.load "RELEASE_NOTES.md"
printfn "%A" notes

let outDirs = [ @"bin\Debug\netcoreapp3.1"; @"bin\Release\netcoreapp3.1"]
let resources = 
    [
        //"lib\Dependencies\PRo3D.Base\windows"; // currently handled by native dependency injection mechanism 
        //"lib/groupmappings"
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

let solutionName = "src/PRo3D.sln"
DefaultSetup.install [solutionName]

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

let aardiumVersion = 
    let versions = getInstalledPackageVersions()
    match Map.tryFind "Aardium" versions with
    | Some v -> v
    | None -> failwith "no aardium version found"

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
            Framework = Some "netcoreapp3.1"
            Runtime = Some "win10-x64"
            Common = { o.Common with CustomParams = Some "-p:PublishSingleFile=true -p:InPublish=True -p:DebugType=None -p:DebugSymbols=false -p:BuildInParallel=false"  }
            //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
            Configuration = DotNet.BuildConfiguration.Release
            VersionSuffix = Some notes.NugetVersion
            OutputPath = Some "bin/publish"
        }
    )


    // 1.1, copy most likely missing c++ libs, currently no reports of missing runtime libs
    //for dll in Directory.EnumerateFiles("data/runtime", "*.dll") do 
    //    let fileName = Path.GetFileName dll
    //    let target = Path.Combine("bin/publish/",fileName)
    //    Fake.Core.Trace.logfn "copying: %s -> %s" dll target
    //    File.Copy(dll, Path.Combine("bin/publish/",fileName))

    // 2, copy licences
    File.Copy("CREDITS.MD", "bin/publish/CREDITS.MD", true)

    // 3, resources (currently everything included)
    // copyResources ["bin/publish"] 
    
    do
        let url = sprintf "https://www.nuget.org/api/v2/package/Aardium-Win32-x64/%A/" aardiumVersion
        let tempFile = Path.GetTempFileName()
        use c = new System.Net.WebClient()
        c.DownloadFile(url, tempFile)
        use a = new ZipArchive(File.OpenRead tempFile)
        a.ExtractToDirectory(Path.Combine("bin", "publish")

    File.Move("bin/publish/PRo3D.Viewer.exe", sprintf "bin/publish/PRo3D.Viewer.%s.exe" notes.NugetVersion)
)

"Credits" ==> "Publish"


Target.create "CompileDebug" (fun _ ->
    let cfg = "Debug" //if config.debug then "Debug" else "Release"
    
    let tag = 
        try 
            let tag = getGitTag()
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
            let tag = getGitTag()
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



"CompileInstruments" ==> "AddNativeResources"
"AddNativeResources" ==> "CopyJRWrapper" ==> "Publish"



Target.create "GitHubRelease" (fun _ ->
    let newVersion = notes.NugetVersion
    try
        Branches.tag "." newVersion
        let token =
            match Environment.environVarOrDefault "github_token" "" with
            | s when not (System.String.IsNullOrWhiteSpace s) -> s
            | _ -> failwith "please set the github_token environment variable to a github personal access token with repro access."

        let files = System.IO.Directory.EnumerateFiles("bin/publish") 

        GitHub.createClientWithToken token
        |> GitHub.draftNewRelease "vrvis" "PRo3D" notes.NugetVersion (notes.SemVer.PreRelease <> None) notes.Notes
        |> GitHub.uploadFiles files
        |> GitHub.publishDraft
        |> Async.RunSynchronously
    finally
        ()
        //Branches.pushTag "." "origin" newVersion
        
)



#if DEBUG
do System.Diagnostics.Debugger.Launch() |> ignore
#endif

"Publish" ==> "GithubRelease"

entry()

