// Learn more about F# at http://fsharp.org

open System
open System.IO

open Aardvark.Base
open Aardvark.GeoSpatial.Opc
open Aardvark.Opc

type Kind = Scene | Annotations | Solarsystem | MultiTexturing

/// Legacy hard-coded scenes. Kept because the older viewers below still reference them;
/// new work should go through `--opc` on the command line instead.
module LegacyScenes =

    let shaler =
        {
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies =
                    Seq.delay (fun _ ->
                        System.IO.Directory.GetDirectories(@"K:\PRo3D Data\Shaler_OPCs_2019\Shaler_Navcam")
                        |> Seq.collect System.IO.Directory.GetDirectories
                    )
            boundingBox      = Box3d.Parse("[[-2490137.664354247, 2285874.562728135, -271408.476700304], [-2490136.248131170, 2285875.658034266, -271406.605430601]]")
            near             = 0.1
            far              = 10000.0
            speed            = 5.0
            lodDecider       =  DefaultMetrics.mars2
        }

    let mola =
        {
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies =
                    Seq.delay (fun _ ->
                        System.IO.Directory.GetDirectories(@"I:\MOLA")
                    )
            boundingBox      = Box3d.Parse("[[-432.863518980, 2190669.974376967, -2354936.901768766], [1492041.915577915, 3396466.232556264, -231.471982595]]")
            near             = 1000.1
            far              = 100000000000.0
            speed            = 15.0
            lodDecider       =  DefaultMetrics.mars2
        }

    let dimorphos =
        {
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies =
                    Seq.delay (fun _ ->
                        System.IO.Directory.GetDirectories(@"C:\pro3ddata\HERA\Workshop2\OPC\Dimorphos_DRACO1\Dimorphos_DRACO1")
                    )
            boundingBox      = Box3d.Parse("[[-89.180763245, -87.157432556, -56.789569855], [87.699211121, 86.719993591, 58.670009613]]")
            near             = 0.1
            far              = 1000.0
            speed            = 1.0
            lodDecider       =  DefaultMetrics.mars2
        }

    let annotationScene =
        {
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies =
                    Seq.delay (fun _ ->
                        System.IO.Directory.GetDirectories(@"C:\pro3ddata\Shaler_OPCs_2019\Shaler_Navcam")
                        |> Seq.collect System.IO.Directory.GetDirectories
                    )
            boundingBox      = Box3d.Parse("[[-2490137.664354247, 2285874.562728135, -271408.476700304], [-2490136.248131170, 2285875.658034266, -271406.605430601]]")
            near             = 0.1
            far              = 10000.0
            speed            = 5.0
            lodDecider       =  DefaultMetrics.mars2
        }

    let annotationFile = @"C:\pro3ddata\Shaler_OPCs_2019\Shaler_v2_Mastcam_w_Navcam_v18_merged_measurementsV2.pro3d.ann"


/// Named repro cases. A preset is a dataset path relative to `--data-root` plus the
/// camera the artefact is visible from, so a bug report turns into `--dataset <name>`
/// rather than a paragraph of coordinates.
module Presets =

    type Preset =
        {
            /// Relative to --data-root. May be an OPC hierarchy or a folder of them.
            relPath : string
            camera  : ScreenshotViewer.Camera
            near    : float
            far     : float
            speed   : float
            notes   : string
        }

    /// HiRISE Victoria Crater, from the camera in the Apple Silicon artefact report
    /// (github issue: regular dark quads across the surface, Apple Silicon only).
    /// Position/bearing/pitch are exactly what PRo3D's on-screen readout showed.
    /// near/far are PRo3D's own defaults (ViewConfigModel.initNearPlane/initFarPlane) --
    /// a 5,000,000:1 depth range, which is itself worth varying with --near/--far.
    let victoria =
        {
            relPath = "HiRISE_VictoriaCrater"
            camera  = ScreenshotViewer.BearingPitch(V3d(3376474.17, -324507.68, -121181.49), 206.72, -11.74)
            near    = 0.1
            far     = 500000.0
            speed   = 20.0
            notes   = "Apple Silicon surface artefact repro"
        }

    let victoriaSuperRes =
        { victoria with
            relPath = "HiRISE_VictoriaCrater_SuperResolution"
            notes   = "same camera, super-resolution variant" }

    let capeDesire =
        { victoria with
            relPath = "MER-B_CapeDesire_wbs"
            notes   = "MER-B ground-level dataset" }

    let byName =
        Map.ofList [
            "victoria",    victoria
            "victoria-sr", victoriaSuperRes
            "capedesire",  capeDesire
        ]


module Cli =

    type Options =
        {
            opcPaths   : list<string>
            dataRoot   : string
            preset     : Option<Presets.Preset>
            eye        : Option<V3d>
            bearing    : Option<float>
            pitch      : Option<float>
            lookAt     : Option<V3d>
            near       : Option<float>
            far        : Option<float>
            stacks     : list<ScreenshotViewer.EffectStack>
            size       : V2i
            outputDir  : string
            name       : string
            wireframe  : bool
            cull       : Aardvark.Rendering.CullMode
            interactive : bool
            maxFrames  : int
            compressed : bool
            dumpGlsl   : bool
            triangleFilter : Option<float>
            sun        : Option<V3d>
            samples    : int
            legacy     : Option<Kind>
        }

    let defaults =
        {
            opcPaths = []
            dataRoot = Environment.GetEnvironmentVariable "PRO3D_DATA" |> Option.ofObj |> Option.defaultValue "."
            preset = None
            eye = None
            bearing = None
            pitch = None
            lookAt = None
            near = None
            far = None
            stacks = []
            size = V2i(1280, 800)
            outputDir = Path.Combine(".", "opcviewer-output")
            name = "opc"
            wireframe = false
            cull = Aardvark.Rendering.CullMode.None
            interactive = false
            maxFrames = 24
            compressed = false
            dumpGlsl = false
            triangleFilter = None
            sun = None
            samples = 1
            legacy = None
        }

    let usage = """
OpcViewer -- headless / interactive OPC renderer for reproducing surface artefacts.

  --dataset <name>       preset repro case: victoria | victoria-sr | capedesire
  --data-root <dir>      root the preset's path is relative to
                         (default: $PRO3D_DATA, else the working directory)
  --opc <dir>            OPC hierarchy, or a folder containing them. Repeatable;
                         overrides the preset's path.

  --eye X Y Z            camera position, body-fixed metres
  --bearing <deg>        clockwise from north, as PRo3D's readout prints it
  --pitch <deg>          above the local horizon, likewise
  --look-at X Y Z        explicit target instead of --bearing/--pitch
  --near <m> --far <m>   depth range (preset default: PRo3D's own 0.1 / 500000)

  --stack <name>         minimal | filter | normals | lit | color. Repeatable.
                         Default: render all of them, which is the point -- the rung
                         where the artefact appears is the stage that causes it.
  --width <n> --height <n>
  --out <dir>            output directory      (default ./opcviewer-output)
  --name <prefix>        output file prefix; the stack name is appended
  --triangle-filter <m>  switch the view-space triangle filter on with this
                         MaxTriangleSize (viewer default: off)
  --sun X Y Z            enable solar lighting from this world direction
  --samples <n>          MSAA sample count (viewer's main control uses 4)
  --wireframe            render lines instead of filled triangles
  --cull none|front|back
  --max-frames <n>       cap on render passes while the LOD tree settles (default 24)
  --compressed           expect .dds textures instead of the source images
  --dump-glsl            log the generated GLSL for each rung
  --interactive          open a window and fly around instead of writing PNGs

  --legacy <kind>        run an old hard-coded viewer:
                         scene | annotations | solarsystem | multitexturing
  --help
"""

    let parse (argv : string[]) : Result<Options, string> =
        let rec go (o : Options) (args : list<string>) =
            match args with
            | [] -> Ok o
            | "--help" :: _ -> Result.Error usage
            | "--opc" :: v :: rest        -> go { o with opcPaths = o.opcPaths @ [ v ] } rest
            | "--data-root" :: v :: rest  -> go { o with dataRoot = v } rest
            | "--dataset" :: v :: rest ->
                match Map.tryFind (v.ToLowerInvariant()) Presets.byName with
                | Some p -> go { o with preset = Some p } rest
                | None ->
                    let known = Presets.byName |> Map.toList |> List.map fst |> String.concat " | "
                    Result.Error (sprintf "unknown --dataset '%s' (known: %s)" v known)
            | "--eye" :: x :: y :: z :: rest ->
                go { o with eye = Some (V3d(float x, float y, float z)) } rest
            | "--look-at" :: x :: y :: z :: rest ->
                go { o with lookAt = Some (V3d(float x, float y, float z)) } rest
            | "--bearing" :: v :: rest    -> go { o with bearing = Some (float v) } rest
            | "--pitch" :: v :: rest      -> go { o with pitch = Some (float v) } rest
            | "--near" :: v :: rest       -> go { o with near = Some (float v) } rest
            | "--far" :: v :: rest        -> go { o with far = Some (float v) } rest
            | "--width" :: v :: rest      -> go { o with size = V2i(int v, o.size.Y) } rest
            | "--height" :: v :: rest     -> go { o with size = V2i(o.size.X, int v) } rest
            | "--out" :: v :: rest        -> go { o with outputDir = v } rest
            | "--name" :: v :: rest       -> go { o with name = v } rest
            | "--triangle-filter" :: v :: rest -> go { o with triangleFilter = Some (float v) } rest
            | "--samples" :: v :: rest    -> go { o with samples = int v } rest
            | "--sun" :: x :: y :: z :: rest ->
                go { o with sun = Some (V3d(float x, float y, float z)) } rest
            | "--wireframe" :: rest       -> go { o with wireframe = true } rest
            | "--max-frames" :: v :: rest -> go { o with maxFrames = int v } rest
            | "--compressed" :: rest      -> go { o with compressed = true } rest
            | "--dump-glsl" :: rest       -> go { o with dumpGlsl = true } rest
            | "--interactive" :: rest     -> go { o with interactive = true } rest
            | "--stack" :: v :: rest ->
                match ScreenshotViewer.EffectStack.parse v with
                | Some s -> go { o with stacks = o.stacks @ [ s ] } rest
                | None -> Result.Error (sprintf "unknown --stack '%s' (minimal|filter|normals|lit|color)" v)
            | "--cull" :: v :: rest ->
                match v.ToLowerInvariant() with
                | "none"  -> go { o with cull = Aardvark.Rendering.CullMode.None } rest
                | "front" -> go { o with cull = Aardvark.Rendering.CullMode.Front } rest
                | "back"  -> go { o with cull = Aardvark.Rendering.CullMode.Back } rest
                | other   -> Result.Error (sprintf "unknown --cull '%s' (none|front|back)" other)
            | "--legacy" :: v :: rest ->
                match v.ToLowerInvariant() with
                | "scene"          -> go { o with legacy = Some Scene } rest
                | "annotations"    -> go { o with legacy = Some Annotations } rest
                | "solarsystem"    -> go { o with legacy = Some Solarsystem } rest
                | "multitexturing" -> go { o with legacy = Some MultiTexturing } rest
                | other -> Result.Error (sprintf "unknown --legacy '%s'" other)
            | unknown :: _ -> Result.Error (sprintf "unknown argument '%s'\n%s" unknown usage)
        go defaults (List.ofArray argv)

    /// An OPC hierarchy is a folder with a `patches` (or `Patches`) subfolder. Datasets
    /// ship both ways -- one hierarchy per folder, or a parent holding several -- and
    /// handing a parent to PatchHierarchy.load fails with a bare "patches dir not
    /// found", so resolve it here rather than making the caller know which shape they
    /// have.
    let expandHierarchies (root : string) : list<string> =
        let isHierarchy (dir : string) =
            [ "patches"; "Patches" ] |> List.exists (fun p -> Directory.Exists(Path.Combine(dir, p)))
        if not (Directory.Exists root) then []
        elif isHierarchy root then [ root ]
        else Directory.GetDirectories root |> Array.filter isHierarchy |> Array.toList


[<EntryPoint>]
let main argv =

    match Cli.parse argv with
    | Result.Error msg ->
        printfn "%s" msg
        if argv |> Array.contains "--help" then 0 else 1
    | Ok o ->

    match o.legacy with
    | Some Solarsystem    -> Solarsytsem.run [ LegacyScenes.mola ]
    | Some Scene          -> TestViewer.run LegacyScenes.mola
    | Some MultiTexturing -> MultiTexturingViewer.run LegacyScenes.dimorphos
    | Some Annotations ->
        let annotations =
            PRo3D.Core.Drawing.DrawingUtilities.IO.loadAnnotationsFromFile LegacyScenes.annotationFile
        FSharp.Data.Adaptive.ShallowEqualityComparer.Set {
            new System.Collections.Generic.IEqualityComparer<Trafo3d> with
                member x.GetHashCode _ = 0
                member x.Equals(_, _) = false
            }
        AnnotationViewer.run LegacyScenes.annotationScene annotations
    | None ->

    // Explicit --opc wins over the preset's path, so a preset's camera can be reused on
    // a different copy of the data.
    let roots =
        match o.opcPaths, o.preset with
        | [], None -> []
        | [], Some p -> [ Path.Combine(o.dataRoot, p.relPath) ]
        | paths, _ -> paths

    let hierarchies = roots |> List.collect Cli.expandHierarchies

    if List.isEmpty hierarchies then
        printfn "no OPC hierarchy found (looked in: %s)" (String.concat ", " roots)
        printfn "%s" Cli.usage
        1
    else

    for h in hierarchies do Log.line "[opc] %s" h

    let near = o.near |> Option.orElse (o.preset |> Option.map (fun p -> p.near)) |> Option.defaultValue 0.1
    let far  = o.far  |> Option.orElse (o.preset |> Option.map (fun p -> p.far))  |> Option.defaultValue 100000.0

    let camera =
        match o.eye, o.lookAt, o.bearing, o.pitch with
        | Some eye, Some target, _, _ -> ScreenshotViewer.LookAt(eye, target)
        | Some eye, None, b, p ->
            ScreenshotViewer.BearingPitch(eye, defaultArg b 0.0, defaultArg p 0.0)
        | None, _, _, _ ->
            match o.preset with
            | Some p -> p.camera
            | None -> ScreenshotViewer.FromBoundingBox

    let scene =
        {
            useCompressedTextures = o.compressed
            preTransform     = Trafo3d.Identity
            patchHierarchies = hierarchies
            // Only used by callers that frame from it; ScreenshotViewer computes the
            // real extents from the hierarchies themselves.
            boundingBox      = Box3d.Invalid
            near             = near
            far              = far
            speed            = o.preset |> Option.map (fun p -> p.speed) |> Option.defaultValue 10.0
            lodDecider       = DefaultMetrics.mars2
        }

    ScreenshotViewer.run {
        scene       = scene
        camera      = camera
        size        = o.size
        outputDir   = o.outputDir
        name        = o.name
        stacks      = o.stacks
        fillMode    = (if o.wireframe then Aardvark.Rendering.FillMode.Line else Aardvark.Rendering.FillMode.Fill)
        cullMode    = o.cull
        interactive = o.interactive
        maxFrames   = o.maxFrames
        dumpGlsl    = o.dumpGlsl
        triangleFilter = o.triangleFilter
        sun         = o.sun
        samples     = o.samples
    }
