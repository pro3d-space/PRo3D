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
