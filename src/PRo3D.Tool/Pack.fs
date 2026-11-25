/// The `pack` verb: writes a copy of a surface whose OPCs are packed, one stored zip
/// `<Opc>.opc` per OPC, ready for the viewer to load read-only (docs/Pro3DTool-Pack.md,
/// docs/PackedSurfaces.md).
///
/// The source is never written: an OPC that lacks its hierarchy cache or KdTrees is copied to
/// a staging folder next to the output, completed there with the kdtree step, packed from
/// there, and the copy is removed.
module PRo3D.Tool.Pack

open System
open System.IO

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Data.Opc
open OpcViewer.Base
open PRo3D.Core.Surface
open Aardvark.GeoSpatial.Opc
// re-opened after Aardvark.GeoSpatial.Opc, whose modules would shadow these (see KdTree.fs)
open Aardvark.Data.Opc
open Aardvark.Data
open OpcViewer.Base.KdTrees
open Aardvark.Geometry
open ICSharpCode.SharpZipLib.Zip

let private ignoreCase = StringComparison.OrdinalIgnoreCase

// read when used: Serialization.init sets it up
let private serializer () = PRo3D.Base.Serialization.binarySerializer

/// true if `inner` is `outer` or lies below it
let private isWithin (outer : string) (inner : string) =
    let o = OpcArchive.normalize outer
    let i = OpcArchive.normalize inner
    String.Equals(o, i, ignoreCase) || i.StartsWith(o + string Path.DirectorySeparatorChar, ignoreCase)

/// Loads the hierarchy from its hierarchy.cache, the way the viewer loads a packed OPC: a
/// missing or unreadable cache is an error, it is never written.
let private tryLoadHierarchy (opcPath : string) : Result<PatchHierarchy, string> =
    let noWrite (_ : QTree<Patch>) : byte[] =
        failwithf "no readable %s" OpcPaths.PatchHierarchyCache_FileName
    try
        Result.Ok (PatchHierarchy.load noWrite (serializer ()).UnPickle (OpcPaths.OpcPaths opcPath))
    with e ->
        Result.Error e.Message

let private leavesOf (h : PatchHierarchy) = h.tree |> QTree.getLeaves |> Seq.toArray

let private lazyCacheOf (h : PatchHierarchy) = Path.ChangeExtension(h.kdTreeAggZero_FileAbsPath, ".cache")

/// What an OPC folder lacks to be packed: its hierarchy cache, per-patch KdTrees, the lazy
/// KdTree cache. Empty if it is complete.
let missing (opcPath : string) : list<string> =
    match tryLoadHierarchy opcPath with
    | Result.Error _ -> [ $"Patches/{OpcPaths.PatchHierarchyCache_FileName}" ]
    | Result.Ok h ->
        let leaves = leavesOf h
        let missingKdTrees =
            leaves |> Array.filter (fun p -> not (File.Exists (h.kdTree_FileAbsPath p.info.Name 0 ViewerModality.XYZ)))
        [
            if missingKdTrees.Length > 0 then
                $"KdTrees of {missingKdTrees.Length} of {leaves.Length} patches"
            if not (File.Exists (lazyCacheOf h)) then
                "the lazy KdTree cache"
        ]

let private copyDirectory (source : string) (target : string) =
    Directory.CreateDirectory target |> ignore
    for d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories) do
        Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, d))) |> ignore
    for f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories) do
        File.Copy(f, Path.Combine(target, Path.GetRelativePath(source, f)))

let private copyEntry (source : string) (targetDir : string) =
    let target = Path.Combine(targetDir, Path.GetFileName source)
    if Directory.Exists source then copyDirectory source target
    else File.Copy(source, target)

/// Writes the content of an OPC folder as a stored zip, its entries (`Images/…`, `Patches/…`)
/// at the top level. Returns the number of entries.
let writeArchive (opcFolder : string) (archivePath : string) : int =
    let files =
        Directory.GetFiles(opcFolder, "*", SearchOption.AllDirectories)
        |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
    use file = File.Create archivePath
    use zip = new ZipOutputStream(file)
    zip.SetLevel 0
    zip.UseZip64 <- UseZip64.Dynamic
    let buffer = Array.zeroCreate<byte> (1 <<< 20)
    for f in files do
        let info = FileInfo f
        let name = Path.GetRelativePath(opcFolder, f).Replace('\\', '/')
        let entry =
            ZipEntry(ZipEntry.CleanName name,
                     CompressionMethod = CompressionMethod.Stored,
                     Size = info.Length,
                     DateTime = info.LastWriteTime,
                     IsUnicodeText = true)
        zip.PutNextEntry entry
        use input = File.OpenRead f
        ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(input, zip, buffer)
        zip.CloseEntry()
    zip.Finish()
    files.Length

let private filesBelow (dir : string) =
    Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories)
    |> Array.map (fun p -> p, (if File.Exists p then FileInfo(p).Length else -1L))
    |> Array.sort

/// Opens a written archive the way the viewer does and checks it: the layout and rules of
/// OpcArchive.tryOpen, every entry's checksum, the hierarchy from its packed cache, and a
/// KdTree for every patch. Nothing may be written while doing so.
let verify (opcPath : string) : Result<list<string>, string> =
    let name = Path.GetFileName opcPath
    let dir = Path.GetDirectoryName opcPath
    let before = filesBelow dir
    let result =
        match OpcArchive.tryOpen opcPath with
        | Result.Error e -> Result.Error (OpcArchive.ArchiveError.message e)
        | Result.Ok archive ->
            let intact =
                use zip = new ZipFile(archive.archivePath)
                zip.TestArchive true
            if not intact then
                Result.Error $"{name}: the archive fails its checksum test."
            else
                match tryLoadHierarchy archive.opcPath with
                | Result.Error e -> Result.Error $"{name}: the hierarchy does not load from the archive: {e}"
                | Result.Ok h ->
                    let leaves = (leavesOf h).Length
                    let kdTrees =
                        KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ (serializer ()) false false PRo3D.Core.Surface.DebugKdTreesX.loadTriangles' true
                        |> HashMap.count
                    if kdTrees <> leaves then
                        Result.Error $"{name}: {kdTrees} KdTrees load from the archive for {leaves} patches."
                    else
                        Result.Ok archive.warnings
    if filesBelow dir <> before then
        Result.Error $"{name}: loading the archive wrote into {dir}."
    else
        result

/// What the source surface folder holds
type private SurfaceContent = {
    /// plain OPC folders
    opcFolders : list<string>
    /// OPCs already packed in the source
    archives   : list<string>
    /// everything else: .opcx, .opcx.json, metadata/, ...
    sidecars   : list<string>
}

let private contentOf (surface : string) : SurfaceContent =
    let entries = Directory.GetFileSystemEntries surface |> Array.sort |> Array.toList
    let isOpcFolder p = Directory.Exists p && Files.isOpcFolder p
    {
        opcFolders = entries |> List.filter isOpcFolder
        archives   = entries |> List.filter OpcArchive.isOpcArchiveFile
        sidecars   = entries |> List.filter (fun p -> not (isOpcFolder p || OpcArchive.isOpcArchiveFile p))
    }

let private initSerialization =
    lazy (
        PRo3D.Base.Serialization.init()
        PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
        PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> PRo3D.Core.Surface.Init.incorePickler)
    )

/// Writes `<outDir>\<surface name>`: the sidecars of `surface` copied, each OPC folder packed
/// into `<Opc>.opc`, OPCs already packed copied, and the surface's `<name>.trafo` next to it.
/// Returns the written surface folder. The source is not written.
let pack (surface : string) (outDir : string) : Result<string, string> =
    if String.IsNullOrWhiteSpace surface || not (Directory.Exists surface) then
        Result.Error $"surface directory not found: {surface}"
    elif String.IsNullOrWhiteSpace outDir then
        Result.Error "no output directory given (--out)"
    else

    let surface = OpcArchive.normalize surface
    let name = Path.GetFileName surface
    let outDir = OpcArchive.normalize outDir
    let target = Path.Combine(outDir, name)
    let staging = Path.Combine(outDir, $".{name}.pack-staging")
    let content = contentOf surface

    let clashes =
        content.archives
        |> List.map OpcArchive.opcPathOf
        |> List.filter (fun o -> content.opcFolders |> List.exists (fun f -> String.Equals(f, o, ignoreCase)))

    if Files.isOpcFolder surface then
        Result.Error $"{surface} is an OPC, not a surface folder. Give the folder it lies in, which holds its sidecars."
    elif List.isEmpty content.opcFolders && List.isEmpty content.archives then
        Result.Error $"{surface} holds no OPC (a folder with Images and Patches/{OpcPaths.PatchHierarchy_FileName})."
    elif not (List.isEmpty clashes) then
        Result.Error $"""{name}: OPCs exist both as a folder and as an archive: {clashes |> List.map Path.GetFileName |> String.concat ", "}."""
    elif isWithin surface outDir then
        Result.Error $"the output directory {outDir} lies inside the surface {surface}; the source is never written."
    elif Directory.Exists target || File.Exists target then
        Result.Error $"{target} exists already. Remove it or choose another output directory."
    elif Directory.Exists staging then
        Result.Error $"the staging folder {staging} exists, left over from an interrupted run. Remove it."
    else

    initSerialization.Force()
    Directory.CreateDirectory target |> ignore

    for s in content.sidecars do
        Log.line "[pack] sidecar %s" (Path.GetFileName s)
        copyEntry s target

    let trafo = surface + ".trafo"
    if File.Exists trafo then
        Log.line "[pack] trafo %s" (Path.GetFileName trafo)
        File.Copy(trafo, target + ".trafo")

    for a in content.archives do
        Log.line "[pack] %s is packed already, copied" (Path.GetFileName a)
        File.Copy(a, Path.Combine(target, Path.GetFileName a))

    let packOne (opcFolder : string) : Result<string, string> =
        let opcName = Path.GetFileName opcFolder
        let archivePath = Path.Combine(target, opcName + ".opc")
        let write (from : string) =
            Log.startTimed "[pack] packing %s" opcName
            let n = writeArchive from archivePath
            Log.stop()
            Log.line "[pack] %s: %d entries, %s" (Path.GetFileName archivePath) n (string (Mem (FileInfo(archivePath).Length)))
            Result.Ok (Path.Combine(target, opcName))
        match missing opcFolder with
        | [] -> write opcFolder
        | lacks ->
            Log.line "[pack] %s lacks %s: completing it in a staging copy, the source is left as it is" opcName (String.concat ", " lacks)
            let copy = Path.Combine(staging, opcName)
            copyDirectory opcFolder copy
            try
                // a lazy KdTree cache is loaded as it is, so missing per-patch KdTrees would not be
                // built: drop it, the kdtree step writes it again (existing KdTrees are kept)
                match tryLoadHierarchy copy with
                | Result.Ok h when File.Exists (lazyCacheOf h) -> File.Delete (lazyCacheOf h)
                | _ -> ()
                KdTree.generateKdTrees None false false false true true [ copy ]
                match missing copy with
                | [] -> write copy
                | still -> Result.Error $"""{opcName}: the kdtree step left it without {String.concat ", " still}."""
            finally
                Directory.Delete(copy, true)

    let packed =
        try
            content.opcFolders |> List.map packOne
        finally
            if Directory.Exists staging then Directory.Delete(staging, true)

    match packed |> List.choose (function Result.Error e -> Some e | Result.Ok _ -> None) with
    | _ :: _ as errors -> Result.Error (String.concat Environment.NewLine errors)
    | [] ->
        let opcPaths =
            (packed |> List.choose (function Result.Ok p -> Some p | Result.Error _ -> None))
            @ (content.archives |> List.map (fun a -> Path.Combine(target, Path.GetFileNameWithoutExtension a)))
        let results = opcPaths |> List.map (fun o -> Path.GetFileName o, verify o)
        for (n, r) in results do
            match r with
            | Result.Ok [] -> Log.line "[pack] verified %s" n
            | Result.Ok warnings -> warnings |> List.iter (Log.warn "[pack] %s")
            | Result.Error _ -> ()
        match results |> List.choose (fun (_, r) -> match r with Result.Error e -> Some e | Result.Ok _ -> None) with
        | [] -> Result.Ok target
        | errors -> Result.Error (String.concat Environment.NewLine errors)

/// Entry point for the `pack` verb. Like `kdtree`, it creates no render runtime.
let run (o : PackOptions) : int =
    Aardvark.Init()
    PixImageDevil.InitDevil()
    match pack o.surfaceDirectory o.out with
    | Result.Ok target ->
        Log.line "[pack] done: %s" target
        Log.line "[pack] import this surface folder in PRo3D; it loads read-only."
        0
    | Result.Error e ->
        Log.error "[pack] %s" e
        1
