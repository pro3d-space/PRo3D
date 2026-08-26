module PRo3D.Tool.Spice

open System
open System.IO

open Aardvark.Base

/// Environment variable naming a SPICE kernel tree.
///
/// PRO3D_SPICE_KERNELS matches the repo's existing PRO3D_AARA_OPC / PRO3D_BDS_OPC convention
/// and says what it holds.
let kernelRootVar = "PRO3D_SPICE_KERNELS"

/// Where to look for SPICE kernels: `--kernel-root`, else $PRO3D_SPICE_KERNELS.
///
/// There is deliberately **no implicit default**. Falling back to some repo-relative tree means
/// the geometry in the output came from kernels the caller never named, which is
/// indistinguishable in the result from the kernels they meant -- and the output would look
/// perfectly fine. Better to refuse.
///
/// The variable conventionally points at a clone of https://spiftp.esac.esa.int/git/hera.git,
/// whose kernels sit in a `kernels` subdirectory -- but a setup may point straight at that
/// subdirectory instead. Accept either rather than making the caller know which.
let resolveKernelRoot (explicit : string) : Result<string, string> =
    let asKernelRoot (root : string) =
        if Directory.Exists(Path.Combine(root, "mk")) then Some root
        elif Directory.Exists(Path.Combine(root, "kernels", "mk")) then Some (Path.Combine(root, "kernels"))
        else None

    let malformed source value =
        sprintf "%s=%s contains neither 'mk' nor 'kernels/mk'" source value

    if not (String.IsNullOrWhiteSpace explicit) then
        match asKernelRoot explicit with
        | Some root -> Ok root
        | None -> Result.Error (malformed "--kernel-root" explicit)
    else
        let value = Environment.GetEnvironmentVariable kernelRootVar
        if String.IsNullOrWhiteSpace value then
            Result.Error (sprintf "no SPICE kernel tree given: pass --kernel-root <dir> or set %s" kernelRootVar)
        else
            match asKernelRoot value with
            | Some root ->
                Log.line "[spice] %s=%s -> kernel root %s" kernelRootVar value root
                Ok root
            | None -> Result.Error (malformed kernelRootVar value)

/// The full failure message for a missing kernel tree, including where to get one. Shared so
/// both verbs say the same thing.
let reportMissingKernelRoot (e : string) =
    Log.error "%s" e
    Log.error "SPICE kernels are published separately by ESA and are not part of the PRo3D test data:"
    Log.error "  git clone https://spiftp.esac.esa.int/git/hera.git"
    Log.error "then set %s to that clone (or to its 'kernels' subdirectory)." kernelRootVar
