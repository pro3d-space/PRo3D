open System
open System.Threading.Tasks
open System.IO
open System.Threading
open Aardvark.Base
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis.Opc
open PRo3D.Core.Surface
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.PatchLod
open Aardvark.Prinziple
open CommandLine


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

    let isZipped path = 
        let split = path |> Path.GetFullPath |> Prinziple.splitPath
        split.IsSome

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
                        use stream =
                            if texturePath |> isZipped then                  
                                Prinziple.openRead (texturePath |> Path.GetFullPath) 
                            else
                                File.Open(texturePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                        
                        ImageLoading.loadImageFromStream stream extension

                    match mip.Images |> Seq.tryHead with
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


        let kdTrees =
            KdTrees.loadKdTrees' h Trafo3d.Identity true ViewerModality.XYZ serializer forceKdTreeRebuild ignoreMasterKdTree PRo3D.Core.Surface.DebugKdTreesX.loadTriangles' false Aardvark.VRVis.Opc.KdTrees.KdTreeParameters.legacyDefault

        for (bb,kdTree) in kdTrees do
            match kdTree with
            | KdTrees.Level0KdTree.InCoreKdTree inCore -> ()
            | KdTrees.Level0KdTree.LazyKdTree l -> 
                ()
            ()
        
        Log.stop()
 
    patchHierarchies 
    |> Seq.toList
    |> List.iter createKdTreesForHierarchy

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

        Aardvark.Init()
        PixImageDevil.InitDevil()

        runForDirectories None parsed.Value.forcekdtreerebuild  parsed.Value.generatedds parsed.Value.overwritedds parsed.Value.ignoreMasterKdTree parsed.Value.skipPatchValidation directories
        0
    | :? NotParsed<options> as notParsed -> 
        ()
        -1
    | _ -> 
        -1



