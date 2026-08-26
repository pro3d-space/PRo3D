namespace PRo3D.ProjectionTestbed

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.SPICE
open PRo3D.Core
open PRo3D.Core.InstrumentMetadata

/// The instrument's camera for one observation. Moved to PRo3D.Core so that pro3d-tool
/// shares it; aliased here so testbed code keeps referring to it unqualified.
type ProjectorCamera = PRo3D.Core.ProjectorCamera
type ResolvedImage = PRo3D.Core.ResolvedImage

/// Scenario-shaped wrappers over PRo3D.Core.InstrumentObservation.
///
/// The observation-resolving logic is shared with pro3d-tool and lives in PRo3D.GIS.
/// These wrappers only unpack a Scenario into its arguments -- keeping the shared code in
/// one place matters most for `projectorCamera`, which encodes two failure modes (missing
/// CK coverage producing a non-finite matrix, and a w = 0 perspective divide) that were
/// found empirically and would be easy to lose in a second copy.
module Setup =

    let resolveImage (s : Scenario) : Result<ResolvedImage, string> =
        InstrumentObservation.resolveImage s.imageFolder s.imageFile

    let resolveKernel (s : Scenario) (img : ResolvedImage) : Result<string, string> =
        InstrumentObservation.resolveKernel s.spiceKernel s.kernelRoot img

    /// Place a secondary body's shape model into the primary body's frame.
    ///
    /// The Dimorphos OPC is centred on its own body (its bbox maxes out at ~88 m, whereas
    /// in Didymos-fixed coordinates it would sit ~1.2 km off origin), so it needs both the
    /// orientation of its body-fixed frame and its offset relative to the primary.
    ///
    /// `scale` converts SPICE's position units to the scene's. SPICE reports km natively
    /// and the OPCs are in metres, but wrappers vary -- the caller logs the magnitude so
    /// the factor is chosen from what the data actually says rather than assumed.
    ///
    /// Testbed-only: the tool renders a single body, so this stayed here.
    let secondaryBodyTrafo (renderFrame : string) (primary : string)
                           (bodyName : string) (bodyFrame : string)
                           (time : DateTime) (scale : float) =
        match CooTransformation.getRelState bodyName "SUN" primary time renderFrame,
              CooTransformation.getRotationTrafo bodyFrame renderFrame time with
        | Some rel, Some rot ->
            let pos = rel.pos * scale
            Ok (rot * Trafo3d.Translation pos, rel.pos)
        | None, _ ->
            Result.Error (sprintf "no ephemeris for %s relative to %s in %s at %s"
                              bodyName primary renderFrame (time.ToString "o"))
        | _, None ->
            Result.Error (sprintf "no orientation for frame %s -> %s at %s"
                              bodyFrame renderFrame (time.ToString "o"))

    let sunDirection (renderFrame : string) (primary : string) (time : DateTime) : Result<V3d, string> =
        InstrumentObservation.sunDirection renderFrame primary time

    let projectorCamera (s : Scenario) (img : ResolvedImage) : Result<ProjectorCamera, string> =
        InstrumentObservation.projectorCamera
            s.nearFar s.observer s.referenceFrame s.body s.projectionMethod img
