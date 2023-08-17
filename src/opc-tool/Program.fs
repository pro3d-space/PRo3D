open System
open System.IO
open Aardvark.Base
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis.Opc
open PRo3D.Core.Surface
open CommandLine


let generateKdTrees (forceRebuild : bool) (pathHierarchies: seq<string>) : unit =
    
    let serializer = PRo3D.Base.Serialization.binarySerializer

    let _ =
        pathHierarchies
        |> Seq.toList
        |> List.map (fun basePath ->
            let h =
                PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
            let kdTrees =
                KdTrees.loadKdTrees' h Trafo3d.Identity forceRebuild ViewerModality.XYZ serializer true

            kdTrees
        )

    Log.line "Done."


let runForDirectories (forceRebuild : bool) (opcHierarchyDirectories : array<string>) =
    PRo3D.Base.Serialization.init()
  
    PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
    PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> PRo3D.Core.Surface.Init.incorePickler)

    generateKdTrees forceRebuild opcHierarchyDirectories

type options = {
  [<Option(HelpText = "Prints all messages to standard output.")>] verbose : bool;
  [<Option(HelpText = "Forces rebuild and overwrites existing KdTrees")>] force : bool;
  [<CommandLine.Value(0, HelpText = "Surface Directory")>] surfaceDirectory: string;
}


[<EntryPoint>]
let main args =
    let printUsage() = 
        printfn "opc-tool <dir-to-opc-directories>"
        printfn "dir-to-opc-directories points to a directory which contains OPC directories."
        printfn "According to OPC spec, each OPC dir contains a Patches and an Images subdirectory."

    let result = CommandLine.Parser.Default.ParseArguments<options>(args)
    match result with
    | :? Parsed<options> as parsed -> 
        let directories = Directory.GetDirectories(parsed.Value.surfaceDirectory )
        let opcDirectories =
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
        runForDirectories parsed.Value.force opcDirectories
        0
    | :? NotParsed<options> as notParsed -> 
        printfn "%A" notParsed.Errors
        printUsage()
        -1
    | _ -> 
        printUsage()
        -1



