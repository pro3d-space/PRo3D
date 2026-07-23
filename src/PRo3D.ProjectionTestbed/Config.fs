namespace PRo3D.ProjectionTestbed

open System
open System.IO

open Aardvark.Base

open PRo3D.ImageMapping

/// Where the render camera sits.
type CameraMode =
    /// View/proj are the instrument's own -- the projected image must then land 1:1 on
    /// the framebuffer, so any flip or axis swap is immediately visible. This is the
    /// mode that actually validates the projection.
    | FromInstrument
    /// Pulled back along the boresight, showing the body and the projection footprint on
    /// it. Useful for eyeballing whether the footprint lands in a plausible place at all.
    | ThirdPerson

type RunMode =
    | Interactive
    | Screenshot

type Scenario =
    {
        opcPath          : string
        /// SPICE body name of the OPC, e.g. "DIDYMOS".
        body             : string
        /// Body-fixed frame, e.g. "DIDYMOS_FIXED". Note IAU_DIDYMOS is a retired name
        /// that no current Hera kernel defines (see GisModels.fs).
        referenceFrame   : string
        /// Observing spacecraft, e.g. "MILANI".
        observer         : string

        imageFolder      : string
        /// None -> first image discovered in imageFolder.
        imageFile        : string option
        channel          : int

        /// Explicit metakernel. None -> resolve from the sidecar's SPICE_MK field, then
        /// fall back to hera_plan.tm.
        spiceKernel      : string option
        /// Root to search for the sidecar-declared kernel.
        kernelRoot       : string

        projectionMethod : ProjectionMethod
        /// Explicit depth range; None -> derived from the observation distance.
        nearFar          : (float * float) option

        /// 0 -> use the source image's native dimensions. Rendering into a viewport
        /// whose aspect differs from the instrument frustum's stretches the result, so
        /// matching the source is both the correct default and the only way the
        /// from-instrument comparison is pixel-meaningful.
        width            : int
        height           : int
        outputDir        : string
        mode             : RunMode
        cameraMode       : CameraMode
        /// Render all four flip combinations and rank them against the reference image.
        flipSweep        : bool
        /// Additional bodies to place in the scene alongside the primary:
        /// (SPICE body name, its body-fixed frame, OPC path). Each is positioned via
        /// SPICE relative to the primary body.
        extraBodies      : (string * string * string) list
        /// Metres per SPICE position unit. See Setup.secondaryBodyTrafo.
        spicePositionScale : float
        /// Negate the generated face normals. generateNormal builds them as
        /// `cross edge2 edge1` (reversed operand order), so which way they point depends
        /// on the source geometry's winding -- TestViewer's sphere path pairs it with
        /// flipNormals, the OPC path does not. Wrong sign means the `normal.Z < 0`
        /// front-facing test rejects every fragment and nothing is projected at all.
        flipNormals      : bool
        /// Interactive mode only: show the sun-lit model instead of the projected image.
        /// Screenshot mode always renders both, so this does not affect it.
        shaded           : bool
        /// Rigid image-plane correction applied to the rendered body, in pixels
        /// (+x right, +y up). Converted to a world translation and applied to the geometry
        /// so a measured registration offset can be nulled out. Zero = no correction.
        modelOffsetPx    : V2d
        /// Seconds added to the observation epoch before every SPICE call. For testing
        /// whether a timing error explains a registration offset. Zero = use the sidecar
        /// epoch as-is.
        timeOffsetSec    : float
    }

module Scenario =

    /// Didymos / Milani-ASPECT. The scenario this tool was built for.
    let didymosAspect =
        {
            opcPath          = @"C:\pro3ddata\HERA\Didymos_SK_OPC_KT\Didymos_ASPECT\Didymos_ASPECT"
            body             = "DIDYMOS"
            referenceFrame   = "DIDYMOS_FIXED"
            observer         = "MILANI"
            imageFolder      = @"C:\pro3ddata\HERA\ASPECT-Data\2B\MBI"
            imageFile        = None
            channel          = 0
            spiceKernel      = None
            kernelRoot       = PRo3D.Core.SpiceBoot.defaultKernelRoot
            projectionMethod = ProjectionMethod.MbiBased
            nearFar          = None
            width            = 0
            height           = 0
            outputDir        = Path.Combine(".", "testbed-output")
            mode             = Screenshot
            cameraMode       = FromInstrument
            flipSweep        = false
            flipNormals      = false
            shaded           = false
            modelOffsetPx    = V2d.Zero
            timeOffsetSec    = 0.0
            extraBodies      =
                [ "DIMORPHOS", "DIMORPHOS_FIXED",
                  @"C:\pro3ddata\HERA\Workshop2\OPC\Dimorphos_DRACO1\Dimorphos_DRACO1" ]
            spicePositionScale = 1.0
        }

    let private usage = """
PRo3D projection testbed -- projects an instrument image onto an OPC and screenshots it.

  --opc <dir>            OPC directory
  --body <name>          SPICE body of the OPC            (default DIDYMOS)
  --frame <name>         body-fixed frame                 (default DIDYMOS_FIXED)
  --observer <name>      observing spacecraft             (default MILANI)
  --images <dir>         folder containing .tif + .mbi.json
  --image <file>         specific image; default: first found
  --channel <n>          band index                       (default 0)
  --kernel <file>        explicit SPICE metakernel
  --kernel-root <dir>    where to search for the sidecar's declared kernel
  --method spice|mbi     projection method                (default mbi)
  --near <m> --far <m>   explicit depth range; default derived from target distance
  --width <n> --height <n>   default: the source image's native size
  --out <dir>            output directory
  --interactive          open a window instead of rendering offscreen
  --third-person         render from outside instead of from the instrument
  --flip-sweep           render all flip combinations and rank against the reference
  --flip-normals         negate generated face normals (front-facing test sign)
  --no-extra-bodies      render only the primary body (skip Dimorphos)
  --spice-scale <f>      metres per SPICE position unit (default 1000 = km->m)
  --help
"""

    /// Apply CLI overrides on top of a base scenario. Unknown arguments are an error --
    /// silently ignoring a misspelled flag in a validation tool is how you end up
    /// trusting a run that did not test what you thought.
    let parse (baseScenario : Scenario) (argv : string[]) : Result<Scenario, string> =
        let rec go (s : Scenario) (args : string list) =
            match args with
            | [] -> Ok s
            | "--help" :: _ -> Result.Error usage
            | "--opc" :: v :: rest            -> go { s with opcPath = v } rest
            | "--body" :: v :: rest           -> go { s with body = v } rest
            | "--frame" :: v :: rest          -> go { s with referenceFrame = v } rest
            | "--observer" :: v :: rest       -> go { s with observer = v } rest
            | "--images" :: v :: rest         -> go { s with imageFolder = v } rest
            | "--image" :: v :: rest          -> go { s with imageFile = Some v } rest
            | "--channel" :: v :: rest        -> go { s with channel = int v } rest
            | "--kernel" :: v :: rest         -> go { s with spiceKernel = Some v } rest
            | "--kernel-root" :: v :: rest    -> go { s with kernelRoot = v } rest
            | "--width" :: v :: rest          -> go { s with width = int v } rest
            | "--height" :: v :: rest         -> go { s with height = int v } rest
            | "--out" :: v :: rest            -> go { s with outputDir = v } rest
            | "--interactive" :: rest         -> go { s with mode = Interactive } rest
            | "--third-person" :: rest        -> go { s with cameraMode = ThirdPerson } rest
            | "--flip-sweep" :: rest          -> go { s with flipSweep = true } rest
            | "--flip-normals" :: rest        -> go { s with flipNormals = true } rest
            | "--shaded" :: rest              -> go { s with shaded = true } rest
            | "--model-offset-px" :: dx :: dy :: rest -> go { s with modelOffsetPx = V2d(float dx, float dy) } rest
            | "--time-offset-sec" :: v :: rest -> go { s with timeOffsetSec = float v } rest
            | "--no-extra-bodies" :: rest     -> go { s with extraBodies = [] } rest
            | "--spice-scale" :: v :: rest    -> go { s with spicePositionScale = float v } rest
            | "--method" :: v :: rest ->
                match v.ToLowerInvariant() with
                | "spice" -> go { s with projectionMethod = ProjectionMethod.Spice } rest
                | "mbi"   -> go { s with projectionMethod = ProjectionMethod.MbiBased } rest
                | other   -> Result.Error (sprintf "unknown --method '%s' (expected spice|mbi)" other)
            | "--near" :: v :: rest ->
                let far = s.nearFar |> Option.map snd |> Option.defaultValue infinity
                go { s with nearFar = Some (float v, far) } rest
            | "--far" :: v :: rest ->
                let near = s.nearFar |> Option.map fst |> Option.defaultValue 0.0
                go { s with nearFar = Some (near, float v) } rest
            | unknown :: _ -> Result.Error (sprintf "unknown argument '%s'\n%s" unknown usage)
        go baseScenario (List.ofArray argv)

    /// Catch the half-specified depth range that --near without --far leaves behind,
    /// plus the paths that must exist before we bother initialising a GL context.
    let validate (s : Scenario) : Result<Scenario, string> =
        match s.nearFar with
        | Some (n, f) when Double.IsInfinity f -> Result.Error "--near given without --far"
        | Some (n, f) when n <= 0.0            -> Result.Error "--far given without --near"
        | Some (n, f) when n >= f              -> Result.Error (sprintf "near (%f) must be < far (%f)" n f)
        | _ ->
            if not (Directory.Exists s.opcPath) then Result.Error (sprintf "OPC directory not found: %s" s.opcPath)
            elif not (Directory.Exists s.imageFolder) then Result.Error (sprintf "image folder not found: %s" s.imageFolder)
            else Ok s
