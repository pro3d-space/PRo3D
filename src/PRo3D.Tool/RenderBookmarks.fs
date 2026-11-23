/// `pro3d-tool render-bookmarks`: the command line front of PRo3D.SceneRender.
module PRo3D.Tool.RenderBookmarksVerb

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application.Slim

open PRo3D.SceneRender

let private nonEmpty (s : string) = if String.IsNullOrWhiteSpace s then None else Some s

/// The command line as a job file layer, to merge over the --job file.
let private fromCommandLine (o : RenderBookmarksOptions) : JobFile =
    let bookmarks =
        match o.bookmarks with
        | null -> None
        | bs -> match List.ofSeq bs with [] -> None | l -> Some l
    {
        scene     = nonEmpty o.scene |> Option.map IO.Path.GetFullPath
        out       = nonEmpty o.out |> Option.map IO.Path.GetFullPath
        bookmarks = bookmarks
        width     = o.width
        height    = o.height
        lodLevel  = o.lodLevel
        passes    = None
    }

let run (o : RenderBookmarksOptions) : int =
    let file =
        match nonEmpty o.job with
        | Some path -> JobFile.load path
        | None -> Ok JobFile.empty

    match file |> Result.bind (JobFile.merge (fromCommandLine o) >> JobFile.toJob) with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok job ->

    Aardvark.Init()
    // Created here, never at startup: the kdtree verb must keep working on machines with
    // no GPU and no display (see Program.fs).
    use app = new OpenGlApplication()
    let runtime = app.Runtime :> IRuntime

    // An unexpected exception (corrupt OPC, driver failure) should surface as a clean
    // error and exit code, not a raw stack trace.
    try
        Log.line "[render-bookmarks] opening %s" job.scene
        use renderer = new SceneRenderer(runtime, job.scene)

        if o.list then
            match renderer.Bookmarks with
            | [] -> Console.WriteLine "the scene has no bookmarks"
            | bookmarks -> for b in bookmarks do Console.WriteLine(sprintf "%s\t%O" b.name b.key)
            0
        else
            match RenderJob.run (Log.line "[render-bookmarks] %s") renderer job with
            | Ok images ->
                Log.line "[render-bookmarks] wrote %d image(s) and %s to %s" images.Length RenderJob.manifestName job.out
                0
            | Result.Error e -> Log.error "%s" e; 1
    with e ->
        Log.error "unhandled: %s" e.Message
        Log.line "details for a bug report: %s" (string e)
        1
