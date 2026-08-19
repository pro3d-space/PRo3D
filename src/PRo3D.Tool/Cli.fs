namespace PRo3D.Tool

open CommandLine

/// Options for the `kdtree` verb.
///
/// Carried over verbatim from the standalone `opc-tool`, so that a migrating user only
/// has to prepend the verb: `opc-tool --forcekdtreerebuild <dir>` becomes
/// `pro3d-tool kdtree --forcekdtreerebuild <dir>`.
[<Verb("kdtree", HelpText = "Validate OPC directories and generate KdTrees.")>]
type KdTreeOptions =
    {
        [<Option(HelpText = "Prints all messages to standard output.")>]
        verbose : bool

        [<Option(HelpText = "Forces rebuild and overwrites existing kd-trees")>]
        forcekdtreerebuild : bool

        [<Option(HelpText = "Ignores master kd-trees and load or creates per-patch kd-trees as well as the lazy kd-tree cache")>]
        ignoreMasterKdTree : bool

        [<Option(HelpText = "Generate DDS")>]
        generatedds : bool

        [<Option(HelpText = "Skip patch validation (textures, aara files)")>]
        skipPatchValidation : bool

        [<Option(HelpText = "Overwrite DDS")>]
        overwritedds : bool

        [<Option(HelpText = "Hierarchies to process concurrently: 1 is sequential, 0 (default) or -1 uses all available cores", Required = false)>]
        degreesOfParallelism : int

        [<Value(0, HelpText = "Surface Directory", Required = true)>]
        surfaceDirectory : string
    }

/// Options for the `sun-angles` verb.
///
/// Defaults for the string options are applied in code rather than through the attribute,
/// because an unsupplied string field arrives as null.
[<Verb("sun-angles", HelpText = "Render per-pixel illumination geometry (incidence, emission, phase) for instrument images.")>]
type SunAnglesOptions =
    {
        [<Option("opc", HelpText = "OPC directory of the body", Required = true)>]
        opc : string

        [<Option("images", HelpText = "Folder containing instrument images with .mbi.json sidecars", Required = true)>]
        images : string

        [<Option("image", HelpText = "Process only this image; default: every image in the folder")>]
        image : string

        [<Option("out", HelpText = "Output directory (default: ./sun-angles)")>]
        out : string

        [<Option("body", HelpText = "SPICE body name of the OPC (default DIDYMOS)")>]
        body : string

        [<Option("frame", HelpText = "Body-fixed reference frame (default DIDYMOS_FIXED)")>]
        frame : string

        [<Option("observer", HelpText = "Observing spacecraft (default MILANI)")>]
        observer : string

        [<Option("kernel", HelpText = "Explicit SPICE metakernel; overrides the sidecar")>]
        kernel : string

        [<Option("kernel-root", HelpText = "SPICE kernel tree: a clone of https://spiftp.esac.esa.int/git/hera.git or its 'kernels' dir. Defaults to $PRO3D_SPICE_KERNELS.")>]
        kernelRoot : string

        [<Option("method", HelpText = "Projection method: spice or mbi (default mbi)")>]
        method : string

        [<Option("false-color", HelpText = "Also write one false-colour PNG per angle (blue=low to red=high), using the same colour ramp as the projection testbed")>]
        falseColor : bool

        [<Option("width", HelpText = "Output width; 0 (default) uses the source image's native width")>]
        width : int

        [<Option("height", HelpText = "Output height; 0 (default) uses the source image's native height")>]
        height : int
    }

/// Options for the `simulate-image` verb.
///
/// Defaults for the string options are applied in code rather than through the attribute,
/// because an unsupplied string field arrives as null. Numeric defaults use the attribute.
[<Verb("simulate-image", HelpText = "Render a simulated instrument image of a body: OPC geometry, Lommel-Seeliger sun lighting, de-shaded texture albedo, procedural micro-structure, cast shadows.")>]
type SimulateImageOptions =
    {
        [<Option("opc", HelpText = "OPC directory of the body", Required = true)>]
        opc : string

        [<Option("time", HelpText = "Observation time, ISO-8601 UTC (e.g. 2027-03-15T12:00:00Z)", Required = true)>]
        time : string

        [<Option("out", HelpText = "Output PNG path (default: ./simulated.png)")>]
        out : string

        [<Option("instrument", HelpText = "SPICE instrument frame whose frustum to render with (default HERA_AFC-1)")>]
        instrument : string

        [<Option("observer", HelpText = "Spacecraft carrying the instrument (default HERA)")>]
        observer : string

        [<Option("body", HelpText = "SPICE body name of the OPC (default DIMORPHOS)")>]
        body : string

        [<Option("frame", HelpText = "Body-fixed reference frame (default DIMORPHOS_FIXED)")>]
        frame : string

        [<Option("kernel", HelpText = "Explicit SPICE metakernel; default: <kernel-root>/mk/hera_plan.tm")>]
        kernel : string

        [<Option("kernel-root", HelpText = "SPICE kernel tree: a clone of https://spiftp.esac.esa.int/git/hera.git or its 'kernels' dir. Defaults to $PRO3D_SPICE_KERNELS.")>]
        kernelRoot : string

        [<Option("distance", Default = 0.0, HelpText = "Camera distance in metres, along the direction SPICE puts the spacecraft; 0 (default) uses the spacecraft's real distance")>]
        distance : float

        [<Option("width", HelpText = "Output width; 0 (default) uses the instrument's native width")>]
        width : int

        [<Option("height", HelpText = "Output height; 0 (default) uses the instrument's native height")>]
        height : int

        [<Option("albedo", Default = 0.16, HelpText = "Normal reflectance of the surface (default 0.16, the measured Dimorphos value)")>]
        albedo : float

        [<Option("no-deshade", HelpText = "Do not divide baked-in illumination out of the OPC texture; use the constant --albedo instead")>]
        noDeshade : bool

        [<Option("deshade-layer", HelpText = "Per-vertex attribute layer carrying the texture brightness, for the de-shading fit (default DRACO)")>]
        deshadeLayer : string

        [<Option("micro-scale", Default = 0.5, HelpText = "Feature size of the procedural micro-structure in metres (default 0.5)")>]
        microScale : float

        [<Option("micro-amplitude", Default = 0.3, HelpText = "Strength of the procedural normal perturbation, 0 disables (default 0.3)")>]
        microAmplitude : float

        [<Option("ambient", Default = 0.02, HelpText = "Ambient floor so the night side is distinguishable from space (default 0.02)")>]
        ambient : float

        [<Option("gain", Default = 0.0, HelpText = "Linear I/F -> DN gain; 0 (default) auto-exposes the 99.5th percentile to DN 245")>]
        gain : float

        [<Option("no-shadows", HelpText = "Skip the sun shadow map; shading then comes from the local sun angle alone")>]
        noShadows : bool

        [<Option("shadow-bias", Default = 0.002, HelpText = "Shadow-map depth bias in normalized depth (default 0.002); raise against acne, lower against peter-panning")>]
        shadowBias : float
    }
