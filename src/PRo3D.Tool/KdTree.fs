module PRo3D.Tool.KdTree

open System
open System.Threading
open System.Threading.Tasks
open System.IO

open Aardvark.Base
open Aardvark.Data.Opc
open OpcViewer.Base
open PRo3D.Core.Surface
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.PatchLod
// NOTE: re-opened deliberately. Aardvark.GeoSpatial.Opc also defines a `Patch` module,
// and it shadows the one below if this open does not come after it. Reordering these
// makes `Patch.load` resolve to a different overload that takes an extra argument.
open Aardvark.Data.Opc
open Aardvark.Data
open OpcViewer.Base.KdTrees
open Aardvark.Geometry

let validateAndConvertTextures (generateDds : bool) (overwriteDdds : bool) (patchHierarchy : PatchHierarchy) =

    let mutable validationErrors = 0

    let m (level : int) (d : Patch) =
        try
            let pfi = PatchFileInfo.load patchHierarchy.opcPaths d.info.Name
            let vertices, _ = Patch.load patchHierarchy.opcPaths ViewerModality.XYZ pfi
            let textureFailures =
                match TexturePaths.tryGetLayer d.info.Textures 0 with
                | Some texture, _ ->
                    let texturePath = TexturePaths.extractTexturePath patchHierarchy.opcPaths texture
                    let extension, errors =
                        match Path.GetExtension(texturePath).ToLower() with
                        | ".dds" -> Some TextureLoading.DDS, 0
                        | ".tiff" | ".tif" -> Some TextureLoading.TIFF, 0
                        | _ ->
                            None, 1

                    let mip =
                        use stream = Prinziple.openRead texturePath

                        TextureLoading.loadImageFromStream stream ChannelReference.NoChannelSelection extension

                    match mip.ImageArray |> Seq.tryHead with
                    | Some i ->
                        let greaterZero = i.Size.AllGreater(V2i.OO)
                        let smallerHuge = i.Size.AllSmallerOrEqual(32768)
                        if greaterZero && smallerHuge then
                            let writeDDS =
                                match extension with
                                | Some TextureLoading.DDS  -> overwriteDdds
                                | Some TextureLoading.TIFF -> true
                                | _ -> false
                            if generateDds && writeDDS then
                                try
                                    Log.startTimed "Converting texture to DDS %s" texturePath
                                    let img = DevILSharp.Image.Load(texturePath)
                                    let tmp = Path.ChangeExtension(Path.GetTempFileName(), ".dds")
                                    try
                                        img.Save(tmp, DevILSharp.ImageType.Dds)
                                        File.Move(tmp, texturePath, true)
                                    finally
                                        if File.Exists tmp then File.Delete tmp
                                finally
                                    Log.stop()
                            errors
                        else
                            Log.line "texture dimensions for image %s in patch %s could not be verified (%A)" texturePath d.info.Name i.Size
                            errors + 1
                    | _ ->
                        Log.line "validation for image %s in patch %s failed, no texture." texturePath d.info.Name
                        errors + 1
                | _ -> Log.line "no texture for %s" d.info.Name
                       1
            Interlocked.Add(&validationErrors, textureFailures) |> ignore
            ()
        with e ->
            Log.warn "validation failed for %s, %A" d.info.Name e
            Interlocked.Increment(&validationErrors) |> ignore
            ()

    QTree.mapLevel m patchHierarchy.tree |> ignore

    validationErrors

let generateKdTrees (degreeOfParallelism : Option<int>) (forceKdTreeRebuild : bool)
                    (generateDds : bool) (overwriteDds : bool) (ignoreMasterKdTree : bool) (skipPatchValidation : bool)
                    (patchHierarchies: seq<string>) : unit =

    let serializer = PRo3D.Base.Serialization.binarySerializer

    let createKdTreesForHierarchy (basePath : string) =
        Log.startTimed "Checking KdTrees for hierarchy: %s" basePath
        let h =
            PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)

        // NOTE: opc-tool guarded this with `skipPatchValidation && not generateDds`, which
        // inverted both conditions: validation ran only when the user asked to *skip* it,
        // and because this same call is what performs the DDS conversion, `--generatedds`
        // could never convert anything. Run it when validation is wanted, or when DDS
        // generation was requested (the conversion lives inside validateAndConvertTextures).
        if not skipPatchValidation || generateDds then
            Log.startTimed "validating: %s" h.opcPaths.ShortName
            let errors = validateAndConvertTextures generateDds overwriteDds h
            Log.line "validation returned %d errors." errors
            Log.stop()

        let parameters =
            {
                // retrieved from 2016 TextureConverter tool
                flags =
                    KdIntersectionTree.BuildFlags.Hierarchical
                    ||| KdIntersectionTree.BuildFlags.FastBuild
                    ||| KdIntersectionTree.BuildFlags.SlowIntersection
                    //||| KdIntersectionTree.BuildFlags.NoMultithreading
                relativeMinCellSize = OpcViewer.Base.KdTrees.KdTreeParameters.legacyDefault.relativeMinCellSize
                splitPlaneEpsilon = 1E-07
                setObjectSetToNull = true
            }

        let kdTrees =
            KdTrees.loadKdTrees' h Trafo3d.Identity true ViewerModality.XYZ serializer forceKdTreeRebuild ignoreMasterKdTree PRo3D.Core.Surface.DebugKdTreesX.loadTriangles' false false parameters

        for (bb, kdTree) in kdTrees do
            match kdTree with
            | Aardvark.VRVis.Opc.KdTrees.Level0KdTree.InCoreKdTree inCore -> ()
            | Aardvark.VRVis.Opc.KdTrees.Level0KdTree.LazyKdTree l ->
                ()
            ()

        Log.stop()

    match degreeOfParallelism with
    | None ->
        patchHierarchies
        |> Seq.toList
        |> List.iter createKdTreesForHierarchy
    | Some degreeOfParallelism ->
        let options = ParallelOptions(MaxDegreeOfParallelism = degreeOfParallelism)
        let r = Parallel.ForEach(patchHierarchies, options, createKdTreesForHierarchy)
        ()
    Log.line "Done."

let runForDirectories (degreeOfParallelism : Option<int>) (forceKdTreeRebuild : bool)
                      (generateDds : bool) (overwriteDds : bool) (ignoreMasterKdTree : bool)
                      (skipPatchValidation : bool)
                      (opcHierarchyDirectories : array<string>) =

    PRo3D.Base.Serialization.init()

    PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
    PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> PRo3D.Core.Surface.Init.incorePickler)

    generateKdTrees degreeOfParallelism forceKdTreeRebuild generateDds overwriteDds ignoreMasterKdTree skipPatchValidation opcHierarchyDirectories

/// Entry point for the `kdtree` verb.
///
/// Deliberately does *not* create a render runtime: KdTree generation runs on machines
/// with no GPU and no display, which is how it gets used in data-prep pipelines. Only
/// verbs that actually rasterise may initialise a runtime, and they must do it in their
/// own branch.
let run (o : KdTreeOptions) : int =
    if isNull o.surfaceDirectory || not (Directory.Exists o.surfaceDirectory) then
        Log.error "surface directory not found: %s" o.surfaceDirectory
        1
    else

    let directories =
        if Files.isOpcFolder o.surfaceDirectory then
            [| o.surfaceDirectory |]
        else
            Directory.GetDirectories(o.surfaceDirectory)
            |> Array.filter (fun d ->
                let isOpc = Files.isOpcFolder d
                if isOpc then
                    printfn $"directory {d} is a valid OPC and will be used for KdTree generation."
                    true
                else
                    printfn $"directory {d} is not a valid OPC directory. Skipping this one."
                    false
            )

    if directories.Length = 0 then
        Log.error "no valid OPC directories found under: %s" o.surfaceDirectory
        1
    else
        Log.line ""
        Log.line ""
        Log.line "arguments: %A" o
        Log.line "directories: %A" directories

        let degreesOfParallelism =
            if o.degreesOfParallelism = 0 then None else Some o.degreesOfParallelism
        Log.line "degrees of parallelism: %A" o.degreesOfParallelism

        Aardvark.Init()
        PixImageDevil.InitDevil()

        runForDirectories degreesOfParallelism o.forcekdtreerebuild o.generatedds o.overwritedds o.ignoreMasterKdTree o.skipPatchValidation directories
        0
