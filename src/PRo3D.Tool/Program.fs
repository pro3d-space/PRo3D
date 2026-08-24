module PRo3D.Tool.Program

open System

open CommandLine

let logo = """
.--. .--.     .--. .--.
|   )|   )        )|   :
|--' |--' .-.  --: |   |
|    |  \(   )    )|   ;
'    '   ``-' `--' '--'   pro3d-tool by pro3d-space.

Command line tools for PRo3D data.

  kdtree       validate OPC directories and generate KdTrees
  sun-angles   render per-pixel illumination geometry for instrument images
  unproject    convert image pixel coordinates to body-fixed surface coordinates

Run `pro3d-tool <verb> --help` for the options of a verb.

Example: pro3d-tool kdtree --forcekdtreerebuild "K:\PRo3D Data\SAIIL_02_01-v3-opc\SAIIL_02_01"
"""

/// Verbs are registered here. `ParseArguments(argv, types)` takes the verb set as an
/// array, so adding a verb is a single entry plus a case in `dispatch` -- no change to
/// the generic arity of the call.
let private verbs : Type[] =
    [|
        typeof<KdTreeOptions>
        typeof<SunAnglesOptions>
        typeof<UnprojectOptions>
    |]

let private dispatch (parsed : obj) : int =
    match parsed with
    | :? KdTreeOptions as o -> KdTree.run o
    | :? SunAnglesOptions as o -> SunAnglesVerb.run o
    | :? UnprojectOptions as o -> UnprojectVerb.run o
    | other ->
        eprintfn "unhandled verb: %s" (other.GetType().Name)
        -1

[<EntryPoint>]
let main argv =

    // A bare invocation should explain itself rather than print a parser error.
    if argv.Length = 0 then
        Console.Write(logo)
        Console.WriteLine()
        0
    else

    let result = Parser.Default.ParseArguments(argv, verbs)

    match result with
    | :? Parsed<obj> as parsed -> dispatch parsed.Value
    | :? NotParsed<obj> ->
        // CommandLineParser has already written the error and help text to stderr.
        -1
    | _ ->
        -1
