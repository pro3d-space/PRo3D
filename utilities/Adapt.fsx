// Generates the Adaptify *.g.fs files. They are local build products, not checked in -
// see docs/ModelTypes.md. Usually run through adapt.cmd / adapt.sh, build.cmd / build.sh
// or the runTests scripts, which all call this script.
//
//   dotnet fsi utilities/Adapt.fsx           regenerate projects with a missing or stale *.g.fs
//   dotnet fsi utilities/Adapt.fsx --all     regenerate every model project
//   dotnet fsi utilities/Adapt.fsx --check   only report; exit code 1 if anything is missing or stale
//
// Model projects are the .fsproj files under src/ that list a *.g.fs; they are processed in
// project-reference order. A *.g.fs is stale when the source hash Adaptify writes into its
// first line no longer matches its .fs (the same check Adaptify uses for its own cache).
//
// Adaptify type-checks a model file against the referenced assemblies, so before a project
// is generated its references are restored and built (Release, like build.cmd and runTests).
// Adaptify exits 0 even when it generates nothing, so the result is verified afterwards.

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Xml.Linq

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let flags = fsi.CommandLineArgs |> Array.filter (fun a -> a.StartsWith "-") |> Set.ofArray
let known = set [ "--all"; "--check" ]
let unknown = Set.difference flags known
if not unknown.IsEmpty then
    eprintfn "Adapt: unknown argument(s) %s (known: --all, --check)" (String.Join(", ", unknown))
    exit 2
let regenerateAll = flags.Contains "--all"
let checkOnly = flags.Contains "--check"

type Project =
    {
        path       : string
        references : list<string>
        generated  : list<string>
    }

let relative (path : string) = Path.GetRelativePath(root, path)

let readProject (path : string) =
    let dir = Path.GetDirectoryName path
    let doc = XDocument.Load path
    let includes (item : string) =
        doc.Descendants()
        |> Seq.choose (fun e ->
            match e.Attribute(XName.Get "Include") with
            | null -> None
            | a when e.Name.LocalName = item ->
                Some (Path.GetFullPath(Path.Combine(dir, a.Value.Replace('\\', Path.DirectorySeparatorChar))))
            | _ -> None
        )
        |> Seq.toList
    {
        path       = path
        references = includes "ProjectReference"
        generated  = includes "Compile" |> List.filter (fun f -> f.EndsWith ".g.fs")
    }

/// Model projects, every project after the model projects it (transitively) references.
let modelProjects () =
    let cache = Dictionary<string, Option<Project>>()
    let tryRead (path : string) =
        match cache.TryGetValue path with
        | true, p -> p
        | _ ->
            let p = if File.Exists path then Some (readProject path) else None
            cache.[path] <- p
            p
    let visited = HashSet<string>()
    let ordered = List<Project>()
    let rec visit (path : string) =
        if visited.Add path then
            match tryRead path with
            | Some p ->
                for r in p.references do visit r
                if not p.generated.IsEmpty then ordered.Add p
            | None -> ()
    for dir in Directory.GetDirectories(Path.Combine(root, "src")) |> Array.sort do
        for proj in Directory.GetFiles(dir, "*.fsproj") |> Array.sort do
            visit (Path.GetFullPath proj)
    Seq.toList ordered

type Status =
    | UpToDate
    | Missing
    | Stale
    | NoSource of source : string

/// Adaptify's hash of a model source: MD5 of the text as a Guid (Adaptify.Compiler/Runner.fs).
let sourceHash (file : string) =
    use md5 = MD5.Create()
    File.ReadAllText file |> Encoding.UTF8.GetBytes |> md5.ComputeHash |> Guid |> string

let status (generated : string) =
    let source = generated.Substring(0, generated.Length - ".g.fs".Length) + ".fs"
    if not (File.Exists source) then NoSource source
    elif not (File.Exists generated) then Missing
    else
        match File.ReadLines generated |> Seq.tryHead with
        | Some header when header = "//" + sourceHash source -> UpToDate
        | _ -> Stale

let describe (generated : string) (s : Status) =
    match s with
    | UpToDate -> sprintf "%s up to date" (relative generated)
    | Missing -> sprintf "%s missing" (relative generated)
    | Stale -> sprintf "%s stale" (relative generated)
    | NoSource source -> sprintf "%s has no source %s (fix the .fsproj)" (relative generated) (relative source)

let outdated (p : Project) =
    p.generated |> List.choose (fun g ->
        match status g with
        | UpToDate -> None
        | s -> Some (g, s)
    )

let run (args : list<string>) =
    printfn "> dotnet %s" (String.Join(" ", args))
    let info = ProcessStartInfo("dotnet", WorkingDirectory = root, UseShellExecute = false)
    for a in args do info.ArgumentList.Add a
    use proc = Process.Start info
    proc.WaitForExit()
    proc.ExitCode

let runOrFail (args : list<string>) =
    let code = run args
    if code <> 0 then
        eprintfn "Adapt: 'dotnet %s' failed with exit code %d" (String.Join(" ", args)) code
        exit 1

let projects = modelProjects ()

let todo =
    projects |> List.choose (fun p ->
        match outdated p with
        | [] when not regenerateAll -> None
        | files -> Some (p, files)
    )

for (p, files) in todo do
    for (g, s) in files do
        printfn "Adapt: %s" (describe g s)
    if files.IsEmpty then printfn "Adapt: %s (--all)" (relative p.path)

let broken =
    todo |> List.collect (fun (_, files) ->
        files |> List.choose (fun (g, s) -> match s with | NoSource _ -> Some (describe g s) | _ -> None)
    )
if not broken.IsEmpty then
    eprintfn "Adapt: cannot generate:"
    for b in broken do eprintfn "  %s" b
    exit 1

if todo.IsEmpty then
    let count = projects |> List.sumBy (fun p -> p.generated.Length)
    printfn "Adapt: all %d generated files in %d projects are up to date" count projects.Length
    exit 0

if checkOnly then
    eprintfn "Adapt: generated files are missing or stale - run adapt.cmd / adapt.sh"
    exit 1

// Paket.Restore.targets, which every project imports, only exists after a paket restore.
runOrFail [ "tool"; "restore" ]
runOrFail [ "paket"; "restore" ]

let failures = List<string>()
for (p, _) in todo do
    let proj = relative p.path
    printfn "Adapt: generating %s" proj
    // Builds the referenced projects only (not the project itself, whose *.g.fs are missing).
    // The restore also writes this project's assets file, without which Adaptify sees no
    // NuGet references and silently finds no model types.
    runOrFail [ "msbuild"; proj; "-restore"; "-t:ResolveProjectReferences"; "-p:Configuration=Release"; "-v:m"; "-nologo" ]
    runOrFail [ "adaptify"; "--lenses"; "--local"; "--force"; "--release"; proj ]
    for (g, s) in outdated p do
        failures.Add (describe g s)

if failures.Count > 0 then
    eprintfn "Adapt: Adaptify did not produce up-to-date output (it reports success regardless):"
    for f in failures do eprintfn "  %s" f
    eprintfn "Rerun the project with 'dotnet adaptify --lenses --local --force --release --verbose <fsproj>' to see why."
    exit 1

printfn "Adapt: generated %d project(s)" todo.Length
