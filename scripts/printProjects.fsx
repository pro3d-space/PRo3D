open System
open System.IO

let root = Path.Combine(__SOURCE_DIRECTORY__, "..")
let src = Path.Combine(root, "src")

let projectFiles = 
    Directory.EnumerateFiles(src, "*.fsproj", SearchOption.AllDirectories)
    |> Seq.toArray
    |> Array.map (fun projFile -> 
        sprintf "dotnet adaptify --local --force %s" (Path.GetRelativePath(root, projFile))
    )

let genRunAdaptifyScript () =
    let runAdaptify = projectFiles |> String.concat Environment.NewLine
    File.WriteAllText(Path.Combine(root, "runAdaptify.cmd"), runAdaptify)

