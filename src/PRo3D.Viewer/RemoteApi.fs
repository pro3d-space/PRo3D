namespace PRo3D.Viewer

open System
open PRo3D.Viewer
open PRo3D.Core
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive


module RemoteApi =

    type Api(emit : ViewerAction -> unit, p : AdaptiveProvenanceModel) = 

        member x.LoadScene(fullPath : string) = 
            ViewerAction.LoadScene fullPath |> emit

        member x.SaveScene(fullPath : string) = 
            let dirName = Path.GetDirectoryName(fullPath)
            if Directory.Exists dirName |> not then Directory.CreateDirectory dirName |> ignore
            ViewerAction.SaveAs fullPath |> emit

        member x.ImportOpc(folders : array<string>) =
            List.ofArray folders |> ViewerAction.DiscoverAndImportOpcs |> emit

        member x.LocateSurfaces(fullPaths : array<string>) =
            ViewerAction.SurfaceActions (fullPaths |> Array.toList |> Surface.ChangeImportDirectories) |> emit

        member x.ProvenanceModel = p

        member x.GetProvenanceGraphJson() =
            let v = p.Current.GetValue()
            ProvenanceModel.Thoth.toJs v




    type LoadScene = 
        {
            // absolute path
            sceneFile : string
        }

    type SaveScene = 
        {
            // absolute path
            sceneFile : string
        }

    type ImportOpc = 
        {
            // absolute path
            folders : array<string>
        }

    type ChangeImportDirectories = 
        {
            // absolute path
            folders : array<string>
        }



    module Suave = 

        open Suave
        open Suave.Filters
        open Suave.Operators

        open Suave.Sockets.Control
        open Suave.WebSocket
        open Suave.Sockets

        open System
        open System.IO

        open System.Text.Json
        open System.Collections.Concurrent

        let getUTF8 (str: byte []) = System.Text.Encoding.UTF8.GetString(str)

        let loadScene (api : Api) = 
            path "/loadScene" >=> request (fun r -> 
                let str = r.rawForm |> getUTF8
                let command : LoadScene = str |> JsonSerializer.Deserialize
                if File.Exists command.sceneFile then
                    api.LoadScene command.sceneFile 
                    Successful.OK "done"
                else
                    RequestErrors.BAD_REQUEST "Oops, something went wrong here!"
            )

        let discoverSurfaces (api : Api) = 
            path "/discoverSurfaces" >=> request (fun r -> 
                let str = r.rawForm |> getUTF8
                let command : ChangeImportDirectories = str |> JsonSerializer.Deserialize
                api.LocateSurfaces(command.folders)
                Successful.OK "done"
            )

        let saveScene (api : Api) = 
            path "/saveScene" >=> request (fun r -> 
                let str = r.rawForm |> getUTF8
                let command : SaveScene = str |> JsonSerializer.Deserialize
                api.SaveScene command.sceneFile 
                Successful.OK "done"
            )

        let importOpc (api : Api) = 
            path "/importOpc" >=> request (fun r -> 
                let str = r.rawForm |> getUTF8
                let command : ImportOpc = str |> JsonSerializer.Deserialize
                api.ImportOpc command.folders 
                Successful.OK "done"
            )

        let getProvenanceGraph (api : Api) = 
            path "/getProvenanceGraph" >=> request (fun r -> 
                let json = api.GetProvenanceGraphJson()
                Successful.OK json
            )

        module Thoth =
        
            open Thoth.Json.Net
            
            open PRo3D.Viewer.ProvenanceModel.Thoth

            type GraphElement =
                | NodeElement of CyNode
                | EdgeElement of CyEdge


            type GraphChange = { edges : array<CyEdge>; nodes : array<CyNode> }


            module Node =

                let encoder (op : SetOperation<CyNode>) : JsonValue =
                    Encode.object [
                        "count", Encode.int op.Count
                        "element", PRo3D.Viewer.ProvenanceModel.Thoth.CyNode.encode op.Value
                    ]

            module Edge = 
                
                let encoder (op : SetOperation<CyEdge>) : JsonValue =
                    Encode.object [
                        "count", Encode.int op.Count
                        "element", PRo3D.Viewer.ProvenanceModel.Thoth.CyEdge.encode op.Value
                    ]

            module Operations =

                let encodeSetOperation (op : SetOperation<GraphElement>) : JsonValue =
                    let e = 
                        match op.Value with
                        | GraphElement.NodeElement e -> PRo3D.Viewer.ProvenanceModel.Thoth.CyNode.encode e
                        | GraphElement.EdgeElement e -> PRo3D.Viewer.ProvenanceModel.Thoth.CyEdge.encode e
                    Encode.object [
                        "count", Encode.int op.Count
                        "element", e
                    ]

                let operationsToJson (ops : array<SetOperation<GraphElement>>) =
                    ops |> Array.map encodeSetOperation |> Encode.array |> Encode.toString 4
    

        let provenanceGraphWebSocket (api : Api) =

            let nodes = 
                api.ProvenanceModel.nodes 
                |> AMap.toASetValues 
                |> ASet.map PRo3D.Viewer.ProvenanceModel.Thoth.CyNode.fromPNode
                |> ASet.map Thoth.GraphElement.NodeElement

            let edges =
                api.ProvenanceModel.edges 
                |> AMap.toASetValues 
                |> ASet.map PRo3D.Viewer.ProvenanceModel.Thoth.CyEdge.fromPNode
                |> ASet.map Thoth.GraphElement.EdgeElement

            let elements = ASet.union nodes edges

            let elementsReader = elements.GetReader()
            let changes = new BlockingCollection<_>(ConcurrentQueue<_>())
            let addDeltas () = 
                let deltas = 
                    elementsReader.GetChanges()
                    |> HashSetDelta.toArray
                changes.Add (Thoth.Operations.operationsToJson deltas )

            let nodeSub = elements.AddCallback(fun _ _ -> addDeltas()) 

            WebSocket.handShake (fun webSocket ctx -> 
                socket {
                    let mutable loop = true

                    while loop do
                        let! ct = SocketOp.ofAsync Async.CancellationToken
                        let jsonMessage = changes.Take(ct)

                        let byteResponse =
                            jsonMessage
                            |> System.Text.Encoding.ASCII.GetBytes
                            |> ByteSegment

                        do! webSocket.send Text byteResponse true

                        let! msg = webSocket.read()

                        match msg with
                        | (Text, data, true) ->
                            ()

                        | (Close, _, _) ->
                            let emptyResponse = [||] |> ByteSegment
                            do! webSocket.send Close emptyResponse true
                            loop <- false
                            nodeSub.Dispose()

                        | _ -> ()
                }
            )
        
        let webPart (api : Api) = 
            choose [
                loadScene api
                importOpc api
                saveScene api
                discoverSurfaces api
                getProvenanceGraph api
                prefix "/provenanceGraph" >=> provenanceGraphWebSocket api
            ]