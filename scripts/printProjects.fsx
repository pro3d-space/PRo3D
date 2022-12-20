//#r "nuget: Microsoft.Build.Locator"

//open Microsoft.Build.Locator
//do MSBuildLocator.RegisterInstance(MSBuildLocator.QueryVisualStudioInstances() |> Seq.sortByDescending (fun i -> i.Version) |> Seq.head)

#r "nuget: Microsoft.Build, 17.4.0"
#r "nuget: Microsoft.Build.Utilities.Core, 17.4.0"
open System
open System.IO


let root = Path.Combine(__SOURCE_DIRECTORY__, "..")
let src = Path.Combine(root, "src")

let projectFiles = 
    Directory.EnumerateFiles(src, "*.fsproj", SearchOption.AllDirectories)
    |> Seq.toArray




module MsBuild =
    open Microsoft.Build
    open Microsoft.Build.Evaluation

    let test () = 
        let options = Microsoft.Build.Construction.ProjectRootElement.Open("C:\Users\steinlechner\Desktop\PRo3D-nomsbuild\src\PRo3D.2D3DLinking\PRo3D.Linking.fsproj")
        ()


let genRunAdaptifyScript () =
    let adaptifyCalls =
        projectFiles
        |> Array.map (fun projFile -> 
            sprintf "dotnet adaptify --local --force %s" (Path.GetRelativePath(root, projFile))
        )
    let runAdaptify = adaptifyCalls |> String.concat Environment.NewLine
    File.WriteAllText(Path.Combine(root, "runAdaptify.cmd"), runAdaptify)
