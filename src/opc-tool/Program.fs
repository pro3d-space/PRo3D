open System
open System.Threading
open System.Threading.Tasks
open System.IO
open System.Threading
open Aardvark.Base
open Aardvark.Data.Opc
open OpcViewer.Base
open PRo3D.Core.Surface
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.PatchLod
open Aardvark.Data.Opc
open CommandLine
open Aardvark.Data
open OpcViewer.Base.KdTrees
open Aardvark.Geometry


let logo = """                    
.--. .--.     .--. .--. 
|   )|   )        )|   :
|--' |--' .-.  --: |   |
|    |  \(   )    )|   ;
'    '   ``-' `--' '--'   opc-tool by pro3d-space.

* validates OPC directories.
* generates KdTrees.

Examples: opc-tool --forcekdtreerebuild --generatedds --overwritedds --ignoremasterkdtree "K:\PRo3D Data\SAIIL_02_01-v3-opc\SAIIL_02_01"
"""

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
                        | ".dds" -> Some ImageLoading.DDS, 0
                        | ".tiff" | ".tif" -> Some ImageLoading.TIFF, 0
                        | _ -> 
                            None, 1

                    let mip = 
                        use stream = Prinziple.openRead texturePath
                        
                        ImageLoading.loadImageFromStream stream extension

                    match mip.ImageArray |> Seq.tryHead with
                    | Some i -> 
                        let greaterZero = i.Size.AllGreater(V2i.OO) 
                        let smallerHuge = i.Size.AllSmallerOrEqual(32768)
                        if greaterZero && smallerHuge then 
                            let writeDDS =
                                match extension with
                                | Some ImageLoading.DDS  -> overwriteDdds
                                | Some ImageLoading.TIFF -> true
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

        if skipPatchValidation && not generateDds then
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

        for (bb,kdTree) in kdTrees do
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

type options = {
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

  [<CommandLine.Value(0, HelpText = "Surface Directory")>] 
  surfaceDirectory: string
}


[<EntryPoint>]
let main args =

    Console.Write(logo)
    Console.WriteLine()


    let result = Parser.Default.ParseArguments<options>(args)
    match result with
    | :? Parsed<options> as parsed -> 
        let directories = 
            if Files.isOpcFolder parsed.Value.surfaceDirectory then
                [| parsed.Value.surfaceDirectory |]
            else
                let directories = Directory.GetDirectories(parsed.Value.surfaceDirectory)
                directories 
                |> Array.filter (fun d -> 
                    let isOpc = Files.isOpcFolder d
                    if isOpc then
                        printfn $"directory {d} is a valid OPC and will be used for KdTree generation."
                        true
                    else
                        printfn $"directory {d} is not a valid OPC directory. Skipping this one."
                        false
                )
        Log.line ""
        Log.line ""
        Log.line "arguments: %A" parsed.Value
        Log.line "directories: %A" directories

        let degresOfParallelism = if parsed.Value.degreesOfParallelism = 0 then None else Some parsed.Value.degreesOfParallelism
        Log.line "degrees of parallelism: %A" parsed.Value.degreesOfParallelism

        Aardvark.Init()
        PixImageDevil.InitDevil()

        runForDirectories degresOfParallelism parsed.Value.forcekdtreerebuild  parsed.Value.generatedds parsed.Value.overwritedds parsed.Value.ignoreMasterKdTree parsed.Value.skipPatchValidation directories
        0
    | :? NotParsed<options> as notParsed -> 
        ()
        -1
    | _ -> 
        -1



