(* This file contains all REST top level entry points for PRo3D remote control interface. 

   The main app needs to be configured to attach the entrypoints to the app using --remoteApi flag.
   To enable also provenance features, the --enableProvenance flag needs to specified
*)


namespace PRo3D.Viewer

open System
open PRo3D
open PRo3D.Viewer
open PRo3D.Core
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open PRo3D.Base.AnnotationQuery


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

        member x.SetSceneFromCheckpoint(
                sceneAsJson     : string, 
                drawingAsJson   : string, 
                p               : Option<ProvenanceModel.Thoth.CyDescription>, 
                activeNode      : Option<string>) : unit =
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

        member x.FindAnnotation(partOfId : Option<string>) =
            let map = x.FullModel.drawing.annotations.flat.Content.GetValue()
            match partOfId with
            | None -> map |> HashMap.toSeq |> Seq.map (string << fst) |> Seq.toArray
            | Some partOfId -> 
                map 
                |> HashMap.toSeq 
                |> Seq.choose (fun (k,v) -> 
                    let s = string k
                    if s.ToLower().Contains(partOfId.ToLower()) then 
                        Some s 
                    else 
                        None
                )
                |> Seq.toArray
        
        member x.QueryAnnotation(
                queryAnnotationId    : string, 
                attributeNames       : list<string>, 
                heightRange          : Range1d,
                outputReferenceFrame : OutputReferenceFrame) =

            let annotations = x.FullModel.drawing.annotations.flat.Content.GetValue()

            match HashMap.tryFind (Guid.Parse(queryAnnotationId)) annotations with
            | Some (AdaptiveAnnotations queryAnnotation) -> 
                let anno = queryAnnotation.Current.GetValue()
                let sgSurfaces = x.FullModel.scene.surfacesModel.sgSurfaces.Content.GetValue()

                let opcs = 
                    sgSurfaces 
                    |> Seq.choose (fun (_,s) -> s.opcScene.GetValue())

                let patchHierarchies = 
                    opcs 
                    |> Seq.collect (fun scene -> 
                        scene.patchHierarchies
                        |> Seq.map Aardvark.Prinziple.Prinziple.registerIfZipped
                        |> Seq.map (fun x ->
                            Aardvark.SceneGraph.Opc.PatchHierarchy.load 
                                PRo3D.Base.Serialization.binarySerializer.Pickle 
                                PRo3D.Base.Serialization.binarySerializer.UnPickle
                                (Aardvark.SceneGraph.Opc.OpcPaths x), x
                        )
                    )
                    |> Seq.toList

                let queryResults = 
                    PRo3D.Base.AnnotationQuery.clipToRegion 
                        patchHierarchies 
                        attributeNames 
                        heightRange 
                        ignore 
                        anno

                Some queryResults
            | _ -> 
                None

        member x.ApplyGraphAndGetCheckpointState(
                sceneAsJson   : string, 
                drawingAsJson : string, 
                p             : Option<ProvenanceModel.Thoth.CyDescription>, 
                activeNode    : Option<string>) : Model * ViewerIO.SerializedModel =


            let nopSendQueue = new System.Collections.Concurrent.BlockingCollection<_>()
            let nopMailbox = new MessagingMailbox(fun _ -> async { return () })
            let mutable currentModel = x.FullModel.Current.GetValue()
            let emitTopLevel (msg : ViewerAnimationAction) =
                currentModel <- ViewerApp.updateInternal Unchecked.defaultof<_> Unchecked.defaultof<_> nopSendQueue nopMailbox currentModel msg
            let emit (msg : ViewerAction) = emitTopLevel (ViewerAnimationAction.ViewerMessage msg)

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

            let serializedModel = ViewerIO.getSerializedModel currentModel
            currentModel, serializedModel

        member x.ImportDrawingModel(drawingAsJson : string, source : string) : unit =
            let setDrawing = ViewerAction.ImportSerializedDrawingModel(drawingAsJson, source)
            setDrawing |> emit

        member x.ImportDrawingModel(drawing : GroupsModel, source : string) : unit =
            let setDrawing = ViewerAction.ImportDrawingModel(drawing, source)
            setDrawing |> emit    
            
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

        let serializeCheckpoint (fullModel : ViewerIO.SerializedModel) =
            let str = 
                checkpointTemplate
                    .Replace("__SCENE__", fullModel.sceneAsJson)
                    .Replace("__DRAWING__", fullModel.drawingAsJson)
            str

        let captureSnapshot (api : Api) (r : HttpRequest) = 
            let str = r.rawForm |> getUTF8
            let command : SaveCheckpointRequest = str |> JsonSerializer.Deserialize
            let fullModel = api.GetCheckpointState(api.FullModel.Current.GetValue(), command.virtualFileName)

            Successful.OK (serializeCheckpoint fullModel)
             

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

        let importAnnotations (api : Api) (r : HttpRequest) =
            let str = r.rawForm |> getUTF8

            let d = JsonDocument.Parse(str)
            let scene = d.RootElement.GetProperty("scene")
            let drawingAsJson = scene.GetProperty("drawingAsJson").ToString()
            let source = 
                match d.RootElement.TryGetProperty("source") with
                | (true, v) -> v.GetString()
                | _ -> ""

            api.ImportDrawingModel(drawingAsJson, source)
            Successful.OK ""

        let getFullStateFor (api : Api) (importAnnotations : bool) (r : HttpRequest) =
            let str = r.rawForm |> getUTF8

            let d = JsonDocument.Parse(str)
            let scene = d.RootElement.GetProperty("scene")
            let sceneAsJson = scene.GetProperty("sceneAsJson").ToString()
            let drawingAsJson = scene.GetProperty("drawingAsJson").ToString()
            let source = 
                match d.RootElement.TryGetProperty("source") with
                | (true, v) -> v.GetString()
                | _ -> ""

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
                let model, fullModel = api.ApplyGraphAndGetCheckpointState(sceneAsJson, drawingAsJson, Some graph, selectedNodeId)
                if importAnnotations then api.ImportDrawingModel(model.drawing.annotations, source)
                Successful.OK (serializeCheckpoint fullModel)
            | None -> 
                let  model, fullModel = api.ApplyGraphAndGetCheckpointState(sceneAsJson, drawingAsJson, None, selectedNodeId)
                if importAnnotations then api.ImportDrawingModel(model.drawing.annotations, source)
                Successful.OK (serializeCheckpoint fullModel)
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

                System.Threading.Thread.Sleep(2000)
                socket {
                    if hackDoNotSendInitialState then
                        // clear all previous state (a bit unclean, inbetween changes could have ben swallowed)
                        // this way only changes after subscribing will be visible in the websocket.
                        // the protocol could be changed, s.t. initial values are tagged
                        addDeltas()  // for sure adds into changes
                        changes.Take() |> ignore // will not block therefore.

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
        
        let parseCoordinateSpace (value: string) : Option<OutputReferenceFrame> =
            match value.ToLower() with
            | "local" -> Some OutputReferenceFrame.Local
            | "global" -> Some OutputReferenceFrame.Global
            | _ -> None // or handle as an error case

        let parseGeometryType (value: string) : Option<OutputGeometryType> =
            match value.ToLower() with
            | "pointcloud" -> Some OutputGeometryType.PointCloud
            | "mesh" -> Some OutputGeometryType.Mesh
            | _ -> None // or handle as an error case

        type QueryResults = System.Collections.Generic.List<Base.QueryResult>

        let queryAnnotation (api : Api) (f : OutputReferenceFrame -> OutputGeometryType -> QueryResults -> WebPart) (httpRequest : HttpRequest) =
            let input =  httpRequest.rawForm |> getUTF8 |> PRo3D.Base.QueryApi.parseRequest
            match input with
            | Result.Ok input -> 
                match ((parseCoordinateSpace input.outputReferenceFrame), parseGeometryType(input.outputGeometryType)) with
                | (Some outputReferenceFrame, Some outputGeometryType) ->
                    //here we can go from primitive types to real types
                    match api.QueryAnnotation(
                        input.annotationId, 
                        input.queryAttributes, 
                        Range1d.FromCenterAndSize(0, input.distanceToPlane), 
                        outputReferenceFrame) with
                    | None -> RequestErrors.BAD_REQUEST "Oops, something went wrong here!"
                    | Some queryResults -> 
                        f  outputReferenceFrame outputGeometryType queryResults                
                | _ -> RequestErrors.BAD_REQUEST "could not parse outputReferenceFrame and/or outputGeometryType"
            | _ -> RequestErrors.BAD_REQUEST "could not parse command"

        let queryAnnotationAsObj (api : Api) = 

            let toResult (frame : OutputReferenceFrame) (geometryType : OutputGeometryType) (results : QueryResults) = 
                let s = PRo3D.Base.AnnotationQuery.queryResultsToObj frame geometryType results
                Successful.OK s

            queryAnnotation api toResult

        let queryAnnotationAsJson (api : Api) = 
            
            let toJson (_ : OutputReferenceFrame) (_ : OutputGeometryType) (results : QueryResults)  = 
                let s = PRo3D.Base.QueryApi.hitsToJson results //todo: also add frame
                Successful.OK s

            queryAnnotation api toJson

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
                        path "/importAnnotations"  >=> request (SuaveV2.importAnnotations api)
                        path "/getFullStateFor"  >=> request (SuaveV2.getFullStateFor api false)
                        path "/importAnnotationsFromGraph"  >=> request (SuaveV2.getFullStateFor api true)
                        path "/provenanceGraph" >=> (fun ctx -> 
                            Log.line "connect to ws with initial state..."
                            provenanceGraphWebSocket false storage api ctx
                        )
                        path "/provenanceGraphChanges" >=> (fun ctx -> 
                            Log.line "connect to ws without initial state..."
                            provenanceGraphWebSocket true storage api ctx
                        ) 
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
                prefix "/queries" >=> (
                    choose [
                        path "/findAnnotation"  >=> Suave.Writers.setMimeType "application/json; charset=utf-8" >=> (
                            request (fun r -> 
                                match r.queryParam "id" with
                                | Choice1Of2 v -> 
                                    let a = api.FindAnnotation(Some v)
                                    let json = Thoth.Json.Net.Encode.Auto.toString a
                                    Successful.OK json
                                | Choice2Of2 _ -> 
                                    let a = api.FindAnnotation(None)
                                    let json = Thoth.Json.Net.Encode.Auto.toString a
                                    Successful.OK json
                            )
                        )
                        path "/queryAnnotationAsJson" >=> 
                            Suave.Writers.setMimeType "application/json; charset=utf-8" 
                                >=> request (queryAnnotationAsJson api)
                        path "/queryAnnotationAsObj" >=> request (queryAnnotationAsObj api)
                    ]
                )
            ]


