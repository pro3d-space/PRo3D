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

//do MSBuildDefaults <- { MSBuildDefaults with Verbosity = Some Minimal }
do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__


open Fake.IO


let outDirs = [ @"bin\Debug\netcoreapp3.1"; @"bin\Release\netcoreapp3.1"]
let resources = 
    [
        //"lib\Dependencies\PRo3D.Base\windows"; // currently handled by native dependency injection mechanism 
        "lib/groupmappings"
        "data/CooTransformationConfig"
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


Target.create "Publish" (fun _ ->
    if Directory.Exists "bin/publish" then 
        Directory.Delete("bin/publish", true)

    // 1. publish exe
    "src/PRo3D.Viewer/PRo3D.Viewer.fsproj" |> DotNet.publish (fun o ->
        { o with
            Framework = Some "netcoreapp3.1"
            Runtime = Some "win10-x64"
            Common = { o.Common with CustomParams = Some "-p:PublishSingleFile=true -p:InPublish=True -p:DebugType=None -p:DebugSymbols=false"  }
            //SelfContained = Some true // https://github.com/dotnet/sdk/issues/10566#issuecomment-602111314
            Configuration = DotNet.BuildConfiguration.Release
            OutputPath = Some "bin/publish"
            
        }
    )


    // 1.1, copy most likely missing c++ libs
    //for dll in Directory.EnumerateFiles("data/runtime", "*.dll") do 
    //    let fileName = Path.GetFileName dll
    //    let target = Path.Combine("bin/publish/",fileName)
    //    Fake.Core.Trace.logfn "copying: %s -> %s" dll target
    //    File.Copy(dll, Path.Combine("bin/publish/",fileName))

    // 2, copy licences
    File.Copy("CREDITS.MD", "bin/publish/CREDITS.MD")

    // 3, resources
    copyResources ["bin/publish"] 

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
            Configuration = if config.debug then DotNet.BuildConfiguration.Debug else DotNet.BuildConfiguration.Release
            MSBuildParams =
                { o.MSBuildParams with
                    Properties = props
                    DisableInternalBinLog = true
                }
            OutputPath = Some "bin/Debug"
        }
    )
)

"CompileDebug" ==> "Default"

#if DEBUG
do System.Diagnostics.Debugger.Launch() |> ignore
#endif


entry()

