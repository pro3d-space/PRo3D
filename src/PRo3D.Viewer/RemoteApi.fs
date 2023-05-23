namespace PRo3D.Viewer

open System
open PRo3D
open PRo3D.Viewer
open PRo3D.Core
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive


module RemoteApi =

    module GeoJsonExport =

        open PRo3D.Base.Annotation

        type T = Guid * ElementOperation<GeoJSON.GeoJsonGeometry>

        let rec chironToThoth (v : Chiron.Json) : Thoth.Json.Net.JsonValue =
            match v with
            | Chiron.Json.Array xs -> Thoth.Json.Net.Encode.array (xs |> List.toArray |> Array.map chironToThoth)
            | Chiron.Json.Object o -> 
                o |> Map.map (fun k v -> chironToThoth v) |> Map.toList |> Thoth.Json.Net.Encode.object
            | Chiron.Json.Bool b -> 
                Thoth.Json.Net.Encode.bool b
            | Chiron.Json.Number n -> 
                Thoth.Json.Net.Encode.decimal n
            | Chiron.Json.String s -> 
                Thoth.Json.Net.Encode.string s

        module Operations =

            open Thoth.Json.Net

            let encoder ((k,v) : T) : JsonValue =

                Encode.object [
                    yield "key", Encode.guid k
                    match v with
                    | ElementOperation.Set v -> 
                        let valueAsJson = GeoJSONExport.geoJsonGeometryToJson v |> chironToThoth
                        yield "operation", Encode.string "set"
                        yield "value", valueAsJson
                    | ElementOperation.Remove -> 
                        yield "operation", Encode.string "remove"
                ]

            let operationsToJson (ops : array<T>) =
                ops |> Array.map encoder |> Encode.array |> Encode.toString 4


        module GeoJson =
            open Thoth.Json.Net

            let encodeAnnotations (planet : Base.Planet) (annotations : list<Annotation>) =
                Encode.object [
                    "type", Encode.string "FeatureCollection"
                    "features", 
                        Encode.array [|
                            for a in annotations do
                                let points = a.points |> IndexList.toArray
                                let geometryType = if points.Length >= 2 then "Polygon" else "Point"
                                yield Encode.object [
                                    "type", Encode.string "Feature"
                                    "geometry", Encode.object [
                                        "type", Encode.string geometryType
                                        "coordinates", 
                                            Encode.array [|
                                                for p in a.points |> IndexList.toArray do
                                                    let latLonAlt = PRo3D.Base.CooTransformation.getLatLonAlt planet p
                                                    yield 
                                                        Encode.array [|
                                                            Encode.float -latLonAlt.longitude
                                                            Encode.float latLonAlt.latitude
                                                            Encode.float latLonAlt.altitude
                                                        |]
                                            |]
                                    ]
                                ]
                        |]
                    ]

            let toJson (planet : Base.Planet) (annotations : list<Annotation>) =
                encodeAnnotations planet annotations |> Encode.toString 4
               



    module ProvenanceGraph =
        
        open Thoth.Json.Net
            
        open PRo3D.Viewer.ProvenanceModel.Thoth

        type GraphElement =
            | NodeElement of CyNode
            | EdgeElement of CyEdge


        //type Graph = { edges : array<CyEdge>; nodes : array<CyNode> }


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

    type Api(emitTopLevel : ViewerAnimationAction -> unit, p : AdaptiveProvenanceModel, m : AdaptiveModel, storage : PPersistence) = 

        let emit s = emitTopLevel (ViewerAnimationAction.ViewerMessage s)

        member x.Storage = storage

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
        member x.FullModel = m

        member x.GetProvenanceGraphJson() =
            let v = p.Current.GetValue()
            ProvenanceModel.Thoth.toJs storage v
            
        // gets the current state of the model (including model and scene serialization)
        // virtualScenePath is displayed in the top menu (normally it shows  path to the scene)
        member x.GetCheckpointState(model : Model, virtualScenePath : string) : ViewerIO.SerializedModel =
            let serializedModel = ViewerIO.getSerializedModel model
            ViewerAction.SetScenePath virtualScenePath |> emit
            serializedModel

        member x.SetSceneFromCheckpoint(sceneAsJson : string, drawingAsJson : string, 
                                        p : Option<ProvenanceModel.Thoth.CyDescription>, activeNode : Option<string>) : unit =
            let setScene = ViewerAction.LoadSerializedScene sceneAsJson
            let setDrawing = ViewerAction.LoadSerializedDrawingModel drawingAsJson
            setScene |> emit
            setDrawing |> emit

            
            match activeNode, p with
            | Some nodeId, Some graph -> 
                ProvenanceMessage (ProvenanceApp.ProvenanceMessage.SetGraph(graph, storage)) |> emitTopLevel
                ProvenanceMessage (ProvenanceApp.ProvenanceMessage.ActivateNode nodeId) |> emitTopLevel
            | _ -> 
                ()



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

    module SuaveHelpers =

       let getUTF8 (str: byte []) = System.Text.Encoding.UTF8.GetString(str)

    module SuaveV2 =
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

        open SuaveHelpers

        type SaveCheckpointRequest =
            {
                // displayed in pro3d as "file name"
                virtualFileName : string
            }

        
        let checkpointTemplate = """ { "sceneAsJson": __SCENE__, "drawingAsJson": __DRAWING__, "version": 1 } """

        let captureSnapshot (api : Api) (r : HttpRequest) = 
            let str = r.rawForm |> getUTF8
            let command : SaveCheckpointRequest = str |> JsonSerializer.Deserialize
            let fullModel = api.GetCheckpointState(api.FullModel.Current.GetValue(), command.virtualFileName)
     
            let str = 
                checkpointTemplate
                    .Replace("__SCENE__", fullModel.sceneAsJson)
                    .Replace("__DRAWING__", fullModel.drawingAsJson)

            Successful.OK str
             

        type SerializedGraph = { cyGraph : string }

        type SetScene = {
            scene : ViewerIO.SerializedModel
            graph : Option<ProvenanceModel.Thoth.CyDescription> 
            selectedNode : Option<string>
        }

        //module SetScene =
        //    open Thoth.Json.Net
        //    open ViewerIO

        //    let serializedModel  : Decoder<ViewerIO.SerializedModel> =
        //        Decode.object (fun get -> 
        //            {
        //                sceneAsJson = get.Required.Field "sceneAsJson" Decode.string
        //                drawingAsJson = get.Required.Field "drawingAsJson" Decode.string
        //                version = get.Required.Field "version" Decode.string
        //            }
        //        )

        //    let decoder : Decoder<SetScene> =
        //        Decode.object (fun get -> 
        //            {
        //                scene = get.Required.Field "scene" serializedModel
        //                graph = get.Optional.Field "graph" ProvenanceModel.Thoth.CyDescription.decoder 
        //                selectedNode = get.Optional.Field "selectedNode" Decode.string
        //            }
        //        )


        let activateSnapshot (api : Api) (r : HttpRequest) =
            let str = r.rawForm |> getUTF8

            let d = JsonDocument.Parse(str)
            let scene = d.RootElement.GetProperty("scene")
            let sceneAsJson = scene.GetProperty("sceneAsJson").ToString()
            let drawingAsJson = scene.GetProperty("drawingAsJson").ToString()
            let version = scene.GetProperty("version").GetInt32()
            let graph = 
                match d.RootElement.TryGetProperty "graph" with
                | (true,v) ->
                    v.ToString() 
                    |> Thoth.Json.Net.Decode.fromString ProvenanceModel.Thoth.CyDescription.decoder 
                    |> Some
                | _ -> 
                    None

            let selectedNodeId = 
                match d.RootElement.TryGetProperty("selectedNodeId") with
                | (true, v) -> v.GetString() |> Some
                | _ -> None

            match graph with
            | Some (Result.Ok graph) -> 
                api.SetSceneFromCheckpoint(sceneAsJson, drawingAsJson, Some graph, selectedNodeId)
                Successful.OK ""
            | None -> 
                api.SetSceneFromCheckpoint(sceneAsJson, drawingAsJson, None, selectedNodeId)
                Successful.OK ""
            | Some (Result.Error e) -> 
                ServerErrors.INTERNAL_ERROR e


             

        let getProvenanceGraph (api : Api) (r : HttpRequest) =
            let graphJson = api.GetProvenanceGraphJson()
            Successful.OK graphJson 
             


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

        open SuaveHelpers
        open ProvenanceGraph


   
        let loadScene (api : Api) (r : HttpRequest)= 
            let str = r.rawForm |> getUTF8
            let command : LoadScene = str |> JsonSerializer.Deserialize
            if File.Exists command.sceneFile then
                api.LoadScene command.sceneFile 
                Successful.OK "done"
            else
                RequestErrors.BAD_REQUEST "Oops, something went wrong here!"
            

        let discoverSurfaces (api : Api) (r : HttpRequest) = 
            let str = r.rawForm |> getUTF8
            let command : ChangeImportDirectories = str |> JsonSerializer.Deserialize
            api.LocateSurfaces(command.folders)
            Successful.OK "done"
            

        let saveScene (api : Api) (r : HttpRequest)= 
            let str = r.rawForm |> getUTF8
            let command : SaveScene = str |> JsonSerializer.Deserialize
            api.SaveScene command.sceneFile 
            Successful.OK "done"
            

        let importOpc (api : Api) (r : HttpRequest) = 
            let str = r.rawForm |> getUTF8
            let command : ImportOpc = str |> JsonSerializer.Deserialize
            api.ImportOpc command.folders 
            Successful.OK "done"
            


        let provenanceGraphWebSocket (hackDoNotSendInitialState : bool) (storage : PPersistence) (api : Api) =
            WebSocket.handShake (fun webSocket ctx -> 
                let nodes = 
                    api.ProvenanceModel.nodes 
                    |> AMap.toASetValues 
                    |> ASet.map (PRo3D.Viewer.ProvenanceModel.Thoth.CyNode.fromPNode storage)
                    |> ASet.map GraphElement.NodeElement

                let edges =
                    api.ProvenanceModel.edges 
                    |> AMap.toASetValues 
                    |> ASet.map (PRo3D.Viewer.ProvenanceModel.Thoth.CyEdge.fromPEdge)
                    |> ASet.map GraphElement.EdgeElement

                let elements = ASet.union nodes edges

                let elementsReader = elements.GetReader()
                let changes = new BlockingCollection<_>(ConcurrentQueue<_>())
                let addDeltas () = 
                    let deltas = 
                        elementsReader.GetChanges()
                        |> HashSetDelta.toArray
                    changes.Add (Operations.operationsToJson deltas )

                let nodeSub = elements.AddCallback(fun _ _ -> addDeltas()) 

                if hackDoNotSendInitialState then
                    // clear all previous state (a bit unclean, inbetween changes could have ben swallowed)
                    // this way only changes after subscribing will be visible in the websocket.
                    // the protocol could be changed, s.t. initial values are tagged
                    addDeltas()  // for sure adds into changes
                    changes.Take() |> ignore // will not block therefore.
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
        

        let annotationsGeoJsonWebSocket (planet : Option<Base.Planet>) (api : Api) =
            
            WebSocket.handShake (fun webSocket ctx -> 
                let geoJsonGeometries = 
                    api.FullModel.drawing.annotations.flat 
                    |> AMap.chooseA (fun k l ->
                        match PRo3D.Core.Drawing.DrawingApp.tryToAnnotation l with
                        | None -> AVal.constant None
                        | Some annotation -> 
                            annotation.Current 
                            |> AVal.map (Base.Annotation.GeoJSONExport.annotationToGeoJsonGeometry planet >> Some)
                    ) 

                let elementsReader = geoJsonGeometries.GetReader()
                let changes = new BlockingCollection<_>(ConcurrentQueue<_>())
                let addDeltas () = 
                    let deltas = 
                        elementsReader.GetChanges()
                        |> HashMapDelta.toArray
                        |> GeoJsonExport.Operations.operationsToJson
                    changes.Add deltas

                let geometriesSub = geoJsonGeometries.AddCallback(fun _ _ -> addDeltas()) 

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
                            geometriesSub.Dispose()

                        | _ -> ()
                }
            )

        let webPart (storage : PPersistence) (api : Api) = 
            choose [
                path "/loadScene" >=> request (loadScene api)
                path "/importOpc" >=> request (importOpc api)
                path "/saveScene" >=> request (saveScene api)
                path "/discoverSurfaces" >=> request (discoverSurfaces api)
                prefix "/v2" >=> (
                    choose [
                        path "/captureSnapshot"    >=> request (SuaveV2.captureSnapshot api)
                        path "/activateSnapshot"   >=> request (SuaveV2.activateSnapshot api)
                        path "/getProvenanceGraph" >=> request (SuaveV2.getProvenanceGraph api)
                        prefix "/provenanceGraph"  >=> provenanceGraphWebSocket false storage api
                        prefix "/provenanceGraphChanges" >=> provenanceGraphWebSocket true storage api
                    ]
                )
                prefix "/integration" >=> (
                    choose [
                        prefix "/ws/geojson_xyz"  >=> annotationsGeoJsonWebSocket None api
                        prefix "/geojson_latlon" >=> request (fun r -> 
                            let model = api.FullModel.drawing.annotations.Current |> AVal.force
                            let annotations = 
                                model.flat 
                                |> HashMap.values
                                |> Seq.choose (function Leaf.Annotations s -> Some s | _ -> None)
                                |> Seq.toList

                            let json = GeoJsonExport.GeoJson.toJson Base.Planet.Mars annotations
                            //let json = Base.Annotation.GeoJSONExport.toGeoJsonString (Base.Planet.Mars |> Some) annotations
                            Successful.OK json
                        )
                    ]
                )
            ]


