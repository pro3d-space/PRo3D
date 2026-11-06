/// `pro3d-tool render-view`: the command line front of PRo3D.SceneRender's OpcRenderer.
module PRo3D.Tool.RenderViewVerb

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application.Slim

open PRo3D.SceneRender

let private nonEmpty (s : string) = if String.IsNullOrWhiteSpace s then None else Some s

let private overrides (o : RenderViewOptions) : ViewJobOverrides =
    {
        out      = nonEmpty o.out |> Option.map IO.Path.GetFullPath
        width    = o.width
        height   = o.height
        lodLevel = o.lodLevel
        noSun    = o.noSun
        views    =
            match o.views with
            | null -> None
            | vs -> match List.ofSeq vs with [] -> None | l -> Some l
    }

let run (o : RenderViewOptions) : int =
    match ViewJob.load o.job |> Result.bind (fun (file, path) -> ViewJob.resolve (overrides o) path file) with
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
        for x in job.opcs do
            Log.line "[render-view] OPC %s%s" x.path (if x.trafo.IsSome then " (transformed)" else "")
        job.coordinatesAreIn |> Option.iter (Log.line "[render-view] coordinates are in %s")
        use renderer = new OpcRenderer(runtime, job.opcs)
        match ViewJob.run (Log.line "[render-view] %s") renderer job with
        | Ok images ->
            Log.line "[render-view] wrote %d image(s) and %s to %s" images.Length ViewJob.manifestName job.out
            0
        | Result.Error e -> Log.error "%s" e; 1
    with e ->
        Log.error "unhandled: %s" e.Message
        Log.line "details for a bug report: %s" (string e)
        1
