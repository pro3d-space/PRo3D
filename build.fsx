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

//do MSBuildDefaults <- { MSBuildDefaults with Verbosity = Some Minimal }
do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__


open Fake.IO


let outDirs = [ @"bin\Debug\netcoreapp3.1"; @"bin\Release\netcoreapp3.1"]
let resources = 
    [
        //"lib\Dependencies\PRo3D.Base\windows"; // currently handled by native dependency injection mechanism 
        "lib/groupmappings"
    ]


Target.create "CopyResources" (fun _ -> 
    for r in resources do
        for outDir in outDirs do
            if Directory.Exists outDir then
                if Path.isDirectory r then 
                    printfn "copying dir %s => %s" r outDir
                    Shell.copyDir outDir r (fun _ -> true)
                else 
                    printfn "copying file %s => %s" r outDir
                    Shell.copyFile outDir r
)

DefaultSetup.install ["src/PRo3D.sln"]

"Compile" ==> "CopyResources" ==> "AddNativeResources" |> ignore


#if DEBUG
do System.Diagnostics.Debugger.Launch() |> ignore
#endif

entry()