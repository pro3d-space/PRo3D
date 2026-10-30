namespace PRo3D.Core.Surface

open System
open System.IO
open System.Collections.Generic

open Aardvark.Base
open Aardvark.Data.Opc
open ICSharpCode.SharpZipLib.Zip

/// A surface packed into one stored (uncompressed) zip archive, `<Name>.opc` or `<Name>.zip`.
///
/// Reads go through Prinziple (Aardvark.Data.Opc): the archive is registered at the root
/// `<dir>\<Name>` and every path below that root resolves to an archive entry. A surface folder
/// `<Name>/` packed into the archive therefore appears at the virtual path `<dir>\<Name>\<Name>`;
/// an archive holding the surface content at its top level appears at `<dir>\<Name>`.
///
/// Archives are read-only: PRo3D writes nothing into them or next to them, so the hierarchy
/// caches and KdTrees have to be packed with the surface. See docs/PackedSurfaces.md.
module SurfaceArchive =

    type SurfaceArchive = {
        /// the archive file on disk
        archivePath : string
        /// surface name, the archive's file name without extension
        name        : string
        /// Prinziple root, the archive path without extension
        root        : string
        /// virtual surface folder: `root`, or `root\<folder>` when the archive wraps the surface in a folder
        surfacePath : string
        /// OPC folder names below `surfacePath`
        opcNames    : list<string>
        /// problems that do not prevent loading, e.g. missing KdTrees
        warnings    : list<string>
    }

    type ArchiveError =
        /// no surface archive: not a zip, or no OPC inside. Discovery skips these silently.
        | NotASurfaceArchive of string
        /// a surface archive that cannot be loaded
        | Rejected of string

    module ArchiveError =
        let message (e : ArchiveError) =
            match e with
            | NotASurfaceArchive m
            | Rejected m -> m

    /// file extensions of surface archives
    let extensions = [ ".opc"; ".zip" ]

    /// extensions Prinziple probes when registering a root, in its probing order
    let private prinzipleExtensions = [ ".opc"; ".opcz"; ".zip" ]

    let private ignoreCase = StringComparison.OrdinalIgnoreCase
    let private sep = string Path.DirectorySeparatorChar

    let private hasArchiveExtension (path : string) =
        let ext = Path.GetExtension path
        extensions |> List.exists (fun e -> String.Equals(ext, e, ignoreCase))

    /// true if `path` is an existing file with a surface archive extension
    let isArchiveFile (path : string) =
        not (String.IsNullOrWhiteSpace path) && hasArchiveExtension path && File.Exists path

    let private isUnder (root : string) (path : string) =
        String.Equals(path, root, ignoreCase) || path.StartsWith(root + sep, ignoreCase)

    // archives opened by this module, keyed by root
    let private opened = Dictionary<string, SurfaceArchive>(StringComparer.OrdinalIgnoreCase)

    /// the opened surface archive `path` lies in
    let tryFind (path : string) : Option<SurfaceArchive> =
        lock opened (fun () ->
            opened.Values |> Seq.tryFind (fun a -> isUnder a.root path)
        )

    /// true if `path` lies in an opened surface archive, i.e. is read-only
    let isInArchive (path : string) = (tryFind path).IsSome

    /// Prinziple.directoryExists, which is false for an archive root itself (its entry name is empty)
    let directoryExists (path : string) =
        let isRoot =
            lock opened (fun () -> opened.ContainsKey(path.TrimEnd(Path.DirectorySeparatorChar)))
        isRoot || Prinziple.directoryExists path

    /// The registered Prinziple root that would redirect reads of the real path `path` into an
    /// archive. Prinziple matches roots by plain string prefix, so `<dir>\Foo` also captures `<dir>\Foo2`.
    let tryFindCapturingRoot (path : string) : Option<string> =
        let path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        Prinziple.getRegistered () |> List.tryFind (fun r -> path.StartsWith r)

    type private Layout = {
        prefix   : string
        opcNames : list<string>
        warnings : list<string>
    }

    let private patchHierarchyXml = "patches/" + OpcPaths.PatchHierarchy_FileName.ToLowerInvariant()
    let private hierarchyCache    = "patches/" + OpcPaths.PatchHierarchyCache_FileName.ToLowerInvariant()
    let private kdTreeSuffix      = "." + OpcPaths.KdTree_Ext

    /// Reads the entry list and finds the OPC folders: folders holding `Patches/patchhierarchy.xml`
    /// and an `Images` folder, the same rule Files.isOpcFolder applies on disk.
    let private readLayout (archivePath : string) (name : string) : Result<Layout, ArchiveError> =
        try
            use zip = new ZipFile(archivePath)

            let entries = Array.init (int zip.Count) (fun i -> zip.EntryByIndex i)
            let files   = entries |> Array.filter (fun e -> not e.IsDirectory)
            let lower   = HashSet<string>(files |> Array.map (fun e -> e.Name.ToLowerInvariant()))

            // every folder that holds an entry, lower case with trailing '/'
            let folders = HashSet<string>()
            for e in entries do
                let n = e.Name.ToLowerInvariant()
                let mutable i = n.IndexOf '/'
                while i >= 0 do
                    folders.Add(n.Substring(0, i + 1)) |> ignore
                    i <- n.IndexOf('/', i + 1)

            let opcDirs =
                files |> Array.choose (fun e ->
                    let n = e.Name
                    if n.EndsWith(patchHierarchyXml, ignoreCase) then
                        let opcDir = n.Substring(0, n.Length - patchHierarchyXml.Length)
                        let hasImages =
                            OpcPaths.Images_DirNames
                            |> List.exists (fun i -> folders.Contains((opcDir + i + "/").ToLowerInvariant()))
                        if opcDir <> "" && hasImages then Some opcDir else None
                    else None
                )

            let split (opcDir : string) =
                let trimmed = opcDir.TrimEnd '/'
                let i = trimmed.LastIndexOf '/'
                trimmed.Substring(0, i + 1), trimmed.Substring(i + 1)

            let prefixes =
                opcDirs |> Array.map (split >> fst) |> Array.distinctBy (fun p -> p.ToLowerInvariant())

            let compressed =
                files |> Array.filter (fun e -> e.CompressionMethod <> CompressionMethod.Stored || e.IsCrypted)

            match prefixes with
            | [||] ->
                Result.Error (NotASurfaceArchive $"{archivePath} contains no OPC (no Patches/patchhierarchy.xml next to an Images folder).")
            | [| _ |] when compressed.Length > 0 ->
                Result.Error (Rejected $"{name}: {compressed.Length} of {files.Length} archive entries are compressed or encrypted. Repack it without compression (stored).")
            | [| prefix |] ->
                let missingCache =
                    opcDirs |> Array.filter (fun d -> not (lower.Contains((d + hierarchyCache).ToLowerInvariant())))

                if missingCache.Length > 0 then
                    let dirs = missingCache |> Array.map (fun d -> d.TrimEnd '/') |> String.concat ", "
                    Result.Error (Rejected $"{name}: no Patches/{OpcPaths.PatchHierarchyCache_FileName} in {dirs}. Archives are read-only, so the hierarchy cache must be packed with the surface.")
                else
                    let withoutKdTrees =
                        opcDirs |> Array.filter (fun d ->
                            let patches = (d + "patches/").ToLowerInvariant()
                            lower |> Seq.exists (fun n -> n.StartsWith(patches, StringComparison.Ordinal) && n.EndsWith(kdTreeSuffix, StringComparison.Ordinal)) |> not
                        )
                    let warnings =
                        if withoutKdTrees.Length > 0 then
                            [ $"{name}: {withoutKdTrees.Length} of {opcDirs.Length} OPCs have no KdTrees (*.{OpcPaths.KdTree_Ext}), picking is disabled for them. Build KdTrees before packing." ]
                        else []
                    Result.Ok { prefix = prefix; opcNames = opcDirs |> Array.map (split >> snd) |> Array.toList; warnings = warnings }
            | _ ->
                Result.Error (Rejected $"""{name}: the OPCs lie in different folders of the archive ({prefixes |> String.concat ", "}). All OPCs of a surface must share one folder.""")
        with e ->
            Result.Error (NotASurfaceArchive $"{archivePath} could not be read as a zip archive: {e.Message}")

    /// Reasons the archive cannot be registered without breaking reads of other data.
    let private tryFindConflict (root : string) (name : string) : Option<string> =
        let dir = Path.GetDirectoryName root
        let variants = prinzipleExtensions |> List.map (fun e -> root + e) |> List.filter File.Exists
        let otherRoots =
            Prinziple.getRegistered ()
            |> List.filter (fun r -> not (String.Equals(r, root, ignoreCase)) && (r.StartsWith(root, ignoreCase) || root.StartsWith(r, ignoreCase)))
        let similarDirs =
            if Directory.Exists dir then
                Directory.EnumerateDirectories(dir, name + "*") |> Seq.truncate 3 |> Seq.toList
            else []

        if Directory.Exists root then
            Some $"{name}: the unpacked folder {root} lies next to the archive. Keep only one of them, the archive would hide the folder."
        elif variants.Length > 1 then
            Some $"""{name}: there are several archives of that name ({variants |> List.map Path.GetFileName |> String.concat ", "}). Keep only one."""
        elif not (List.isEmpty similarDirs) then
            Some $"""{name}: folders starting with the archive name lie next to it ({similarDirs |> List.map Path.GetFileName |> String.concat ", "}). The archive would hide them, rename one of them."""
        elif not (List.isEmpty otherRoots) then
            Some $"""{name}: the name clashes with the already loaded archive {otherRoots |> String.concat ", "}. Rename one of them."""
        else
            None

    /// Opens a surface archive and registers it with Prinziple. Opening an archive again returns
    /// the record of the first opening.
    let tryOpen (archivePath : string) : Result<SurfaceArchive, ArchiveError> =
        let archivePath = Path.GetFullPath archivePath
        let root = Path.ChangeExtension(archivePath, null).TrimEnd(Path.DirectorySeparatorChar)
        let name = Path.GetFileName root

        lock opened (fun () ->
            match opened.TryGetValue root with
            | true, a -> Result.Ok a
            | _ when not (isArchiveFile archivePath) ->
                Result.Error (NotASurfaceArchive $"""{archivePath} is no surface archive ({extensions |> String.concat ", "}).""")
            | _ ->
                match readLayout archivePath name with
                | Result.Error e -> Result.Error e
                | Result.Ok layout ->
                    match tryFindConflict root name with
                    | Some conflict -> Result.Error (Rejected conflict)
                    | None when not (Prinziple.tryRegister root) ->
                        Result.Error (Rejected $"{name}: Prinziple could not open {archivePath}.")
                    | None ->
                        let surfacePath =
                            if layout.prefix = "" then root
                            else Path.Combine(root, layout.prefix.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar))
                        let archive = {
                            archivePath = archivePath
                            name        = name
                            root        = root
                            surfacePath = surfacePath
                            opcNames    = layout.opcNames
                            warnings    = layout.warnings
                        }
                        Log.line "[SurfaceArchive] opened %s as %s (%d OPCs)" archivePath surfacePath archive.opcNames.Length
                        opened.[root] <- archive
                        Result.Ok archive
        )

    /// For a path that does not exist on disk, e.g. a surface folder stored in a scene, opens the
    /// surface archive it lies in: the closest ancestor `a` with an archive file `a.opc` or `a.zip`.
    /// None if the path exists on disk or no archive is found.
    let tryOpenForPath (path : string) : Option<Result<SurfaceArchive, ArchiveError>> =
        if String.IsNullOrWhiteSpace path || Directory.Exists path then None
        else
            let rec go (p : string) =
                if String.IsNullOrEmpty p || Directory.Exists p then None
                else
                    match extensions |> List.tryPick (fun e -> let f = p + e in if File.Exists f then Some f else None) with
                    | Some archive -> Some (tryOpen archive)
                    | None -> go (Path.GetDirectoryName p)
            go (Path.GetFullPath path)
