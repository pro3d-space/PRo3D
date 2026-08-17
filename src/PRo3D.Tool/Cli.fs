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

        [<Option(HelpText = "Degree of paralellism (0 for single threaded)", Required = false)>]
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
