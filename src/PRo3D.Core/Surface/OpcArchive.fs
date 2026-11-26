namespace PRo3D.Core.Surface

open System
open System.IO
open System.Collections.Generic

open Aardvark.Base
open Aardvark.Data.Opc
open ICSharpCode.SharpZipLib.Zip

/// An OPC packed into one stored (uncompressed) zip archive: `<Opc>.opc`, `<Opc>.opcz` or
/// `<Opc>.zip` in place of the OPC folder `<Opc>/`, holding `Images/` and `Patches/` at its top level.
///
/// The surface stays a plain folder: its sidecars (`.opcx`, `.opcx.json`, `metadata/`) describe all
/// OPCs in it, packed or not, and are never packed. Reads go through Prinziple (Aardvark.Data.Opc):
/// the archive is registered at the OPC path `<surface>\<Opc>`, and every path below it resolves
/// to an archive entry, so the rest of PRo3D sees an ordinary OPC folder.
///
/// Archives are read-only: PRo3D writes nothing into them or next to them, so the hierarchy cache
/// and KdTrees have to be packed with the OPC. See docs/PackedSurfaces.md.
module OpcArchive =

    type OpcArchive = {
        /// the archive file on disk
        archivePath : string
        /// the OPC path the archive stands in for, its Prinziple root: the archive path without extension
        opcPath     : string
        /// problems that do not prevent loading, e.g. missing KdTrees
        warnings    : list<string>
    }

    type ArchiveError =
        /// no OPC archive: no archive file, not a zip, or no OPC inside. Discovery skips these silently.
        | NotAnOpcArchive of string
        /// an OPC archive that cannot be loaded
        | Rejected of string

    module ArchiveError =
        let message (e : ArchiveError) =
            match e with
            | NotAnOpcArchive m
            | Rejected m -> m

    /// file extensions of OPC archives, in the order Prinziple probes them when registering an OPC path
    let extensions = [ ".opc"; ".opcz"; ".zip" ]

    let private ignoreCase = StringComparison.OrdinalIgnoreCase
    let private sep = string Path.DirectorySeparatorChar

    let private hasArchiveExtension (path : string) =
        let ext = Path.GetExtension path
        extensions |> List.exists (fun e -> String.Equals(ext, e, ignoreCase))

    /// true if `path` is an existing file with an OPC archive extension (its content is not checked)
    let isArchiveFile (path : string) =
        not (String.IsNullOrWhiteSpace path) && hasArchiveExtension path && File.Exists path

    /// the OPC path an archive file stands in for: the full path without extension
    let opcPathOf (archivePath : string) =
        Path.ChangeExtension(Path.GetFullPath archivePath, null).TrimEnd(Path.DirectorySeparatorChar)

    /// The path form Prinziple registers an archive under (full path, 8.3 short names expanded, no
    /// trailing separator). Prinziple matches registered paths by plain string prefix, so an OPC in
    /// an archive must be read through exactly this form.
    let normalize (path : string) =
        (Path.GetFullPath path).TrimEnd(Path.DirectorySeparatorChar)

    /// the existing archive files for the OPC path `opcPath`, in Prinziple's probing order
    let archiveFilesOf (opcPath : string) : list<string> =
        if String.IsNullOrWhiteSpace opcPath then []
        else
            let p = normalize opcPath
            extensions |> List.map (fun e -> p + e) |> List.filter File.Exists

    /// true if an archive file exists for the OPC path `opcPath`
    let hasArchive (opcPath : string) = not (List.isEmpty (archiveFilesOf opcPath))

    let private isUnder (root : string) (path : string) =
        String.Equals(path, root, ignoreCase) || path.StartsWith(root + sep, ignoreCase)

    // archives opened by this module, keyed by OPC path
    let private opened = Dictionary<string, OpcArchive>(StringComparer.OrdinalIgnoreCase)

    /// the opened OPC archive `path` lies in
    let tryFind (path : string) : Option<OpcArchive> =
        if String.IsNullOrWhiteSpace path then None
        else
            let path = path.TrimEnd(Path.DirectorySeparatorChar)
            lock opened (fun () ->
                opened.Values |> Seq.tryFind (fun a -> isUnder a.opcPath path)
            )

    /// true if `path` lies in an opened OPC archive, i.e. is read-only
    let isInArchive (path : string) = (tryFind path).IsSome

    let private patchHierarchyXml = "patches/" + OpcPaths.PatchHierarchy_FileName.ToLowerInvariant()
    let private hierarchyCache    = "patches/" + OpcPaths.PatchHierarchyCache_FileName.ToLowerInvariant()
    let private kdTreeSuffix      = "." + OpcPaths.KdTree_Ext

    /// Reads the entry list: an OPC archive holds `Patches/patchhierarchy.xml` and an `Images`
    /// folder at its top level, the rule Files.isOpcFolder applies to folders on disk. Returns the
    /// warnings of a loadable archive.
    let private readLayoutUncached (archivePath : string) : Result<list<string>, ArchiveError> =
        let name = Path.GetFileName archivePath
        try
            use zip = new ZipFile(archivePath)

            let entries = Array.init (int zip.Count) (fun i -> zip.EntryByIndex i)
            let files   = entries |> Array.filter (fun e -> not e.IsDirectory)
            let lower   = HashSet<string>(files |> Array.map (fun e -> e.Name.ToLowerInvariant()))

            let hasHierarchy = lower.Contains patchHierarchyXml
            let hasImages =
                entries |> Array.exists (fun e ->
                    OpcPaths.Images_DirNames |> List.exists (fun i -> e.Name.StartsWith(i + "/", ignoreCase))
                )

            let compressed =
                files |> Array.filter (fun e -> e.CompressionMethod <> CompressionMethod.Stored || e.IsCrypted)

            if not (hasHierarchy && hasImages) then
                Result.Error (NotAnOpcArchive $"{archivePath} holds no OPC (Patches/{OpcPaths.PatchHierarchy_FileName} and an Images folder at its top level).")
            elif compressed.Length > 0 then
                Result.Error (Rejected $"{name}: {compressed.Length} of {files.Length} archive entries are compressed or encrypted. Repack it without compression (stored).")
            elif not (lower.Contains hierarchyCache) then
                Result.Error (Rejected $"{name}: no Patches/{OpcPaths.PatchHierarchyCache_FileName}. Archives are read-only, so the hierarchy cache must be packed with the OPC (pro3d-tool kdtree writes it).")
            else
                let hasKdTrees =
                    lower |> Seq.exists (fun n -> n.StartsWith("patches/", StringComparison.Ordinal) && n.EndsWith(kdTreeSuffix, StringComparison.Ordinal))
                if hasKdTrees then Result.Ok []
                else Result.Ok [ $"{name}: no KdTrees (*.{OpcPaths.KdTree_Ext}), picking is disabled for this OPC. Build them with pro3d-tool kdtree before packing." ]
        with e ->
            Result.Error (NotAnOpcArchive $"{archivePath} could not be read as a zip archive: {e.Message}")

    // discovery asks about the same archives several times; keyed by path, size and write time
    let private layouts = Dictionary<string, struct (int64 * DateTime * Result<list<string>, ArchiveError>)>(StringComparer.OrdinalIgnoreCase)

    let private readLayout (archivePath : string) =
        let info = FileInfo archivePath
        let size, written = info.Length, info.LastWriteTimeUtc
        lock layouts (fun () ->
            match layouts.TryGetValue archivePath with
            | true, struct (s, w, r) when s = size && w = written -> r
            | _ ->
                let r = readLayoutUncached archivePath
                layouts.[archivePath] <- struct (size, written, r)
                r
        )

    /// true if `path` is an archive file holding an OPC; it may still be rejected when opened
    /// (compressed, no hierarchy cache, name clash), which is reported then
    let isOpcArchiveFile (path : string) =
        isArchiveFile path &&
            match readLayout (Path.GetFullPath path) with
            | Result.Error (NotAnOpcArchive _) -> false
            | _ -> true

    /// Reasons the archive cannot be registered without mixing its content with folders on disk.
    /// Prinziple matches its roots by plain string prefix and reads a folder of the OPC's name
    /// before the archive (it is where Prinziple would write).
    let private tryFindConflict (opcPath : string) : Option<string> =
        let name = Path.GetFileName opcPath
        let dir = Path.GetDirectoryName opcPath
        let variants = archiveFilesOf opcPath
        let similarDirs =
            if Directory.Exists dir then
                Directory.EnumerateDirectories(dir, name + "*")
                |> Seq.filter (fun d -> not (String.Equals(d, opcPath, ignoreCase)))
                |> Seq.truncate 3
                |> Seq.toList
            else []
        let otherRoots =
            Prinziple.getRegistered ()
            |> List.filter (fun r -> not (String.Equals(r, opcPath, ignoreCase)) && (r.StartsWith(opcPath, ignoreCase) || opcPath.StartsWith(r, ignoreCase)))

        if Directory.Exists opcPath then
            Some $"{name}: the unpacked OPC folder {opcPath} lies next to its archive. Keep only one of them, the folder would be mixed into the archive."
        elif variants.Length > 1 then
            Some $"""{name}: there are several archives of that OPC ({variants |> List.map Path.GetFileName |> String.concat ", "}). Keep only one."""
        elif not (List.isEmpty similarDirs) then
            Some $"""{name}: folders starting with the OPC name lie next to it ({similarDirs |> List.map Path.GetFileName |> String.concat ", "}). The archive would hide them, rename one of them."""
        elif not (List.isEmpty otherRoots) then
            Some $"""{name}: the name clashes with the already loaded OPC archive {otherRoots |> String.concat ", "}. Rename one of them."""
        else
            None

    /// Opens the archive of the OPC path `opcPath` (`<opcPath>.opc`, `.opcz` or `.zip`) and registers
    /// it with Prinziple. Opening it again returns the record of the first opening.
    let tryOpen (opcPath : string) : Result<OpcArchive, ArchiveError> =
        let opcPath = normalize opcPath
        lock opened (fun () ->
            match opened.TryGetValue opcPath with
            | true, a -> Result.Ok a
            | _ ->
                match archiveFilesOf opcPath with
                | [] ->
                    Result.Error (NotAnOpcArchive $"""no OPC archive for {opcPath} ({extensions |> String.concat ", "}).""")
                | archivePath :: _ ->
                    match readLayout archivePath with
                    | Result.Error e -> Result.Error e
                    | Result.Ok warnings ->
                        match tryFindConflict opcPath with
                        | Some conflict -> Result.Error (Rejected conflict)
                        | None when not (Prinziple.tryRegister opcPath) ->
                            Result.Error (Rejected $"{Path.GetFileName opcPath}: Prinziple could not open {archivePath}.")
                        | None ->
                            let archive = { archivePath = archivePath; opcPath = opcPath; warnings = warnings }
                            Log.line "[OpcArchive] opened %s as %s" archivePath opcPath
                            warnings |> List.iter (Log.warn "[OpcArchive] %s")
                            opened.[opcPath] <- archive
                            Result.Ok archive
        )

    /// For loading an OPC: opens its archive if it has one, and returns the path to read the OPC
    /// through. That is `opcPath` for an OPC folder, and the path the archive is registered under
    /// for a packed OPC (see `normalize`). None if its archive is rejected, which is logged.
    let prepare (opcPath : string) : Option<string> =
        if not (hasArchive opcPath) then Some opcPath
        else
            match tryOpen opcPath with
            | Result.Ok a -> Some a.opcPath
            | Result.Error e ->
                Log.error "[OpcArchive] %s" (ArchiveError.message e)
                None
