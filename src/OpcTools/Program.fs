
open System
open System.IO
open Aardvark.Base
open Aardvark.Application.Slim
open Aardvark.Opc
open Aardvark.SceneGraph.Opc
open MBrace.FsPickler

let printUsage() =
    printfn "OpcTools estimateGpuMemory <surfacesFolder>"

[<EntryPoint>]
let main argv =

    Aardvark.Init()

    use app = new OpenGlApplication()
    let runtime = app.Runtime

    
    let serializer = FsPickler.CreateBinarySerializer()


    let estimateGpuMemory (surfacesFolder : string) = 

        let hierarchies = 
            Directory.GetDirectories(surfacesFolder) 
            |> Seq.collect System.IO.Directory.GetDirectories

        let loadedHierarchies = 
            hierarchies |> Seq.toList |> List.map (fun basePath -> 
                let path = OpcPaths.OpcPaths basePath
                let hierarchy = PatchHierarchy.load serializer.Pickle serializer.UnPickle path
                hierarchy, basePath
            )

        let bytes = 
            loadedHierarchies 
            |> List.sumBy (fun (hierarchy, basePath) -> 
                EstimateGpuMemory.estimateHierarchy runtime (OpcPaths.OpcPaths basePath) hierarchy
            )

        let mem = Aardvark.Base.Mem(bytes)

        printfn "highest detail would demand for %A gpu memory." mem


    match argv |> Array.toList with
    | "estimateGpuMemory" :: folder :: [] -> 
        estimateGpuMemory folder
        exit 0
    | _ -> 
        printUsage()
        exit -1
