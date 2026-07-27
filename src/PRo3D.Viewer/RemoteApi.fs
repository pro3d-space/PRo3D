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
open Aardvark.Data.Opc
open PRo3D.Base.AnnotationQuery
open Aardvark.UI
open Aardvark.UI.Giraffe

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
            | Chiron.Json.Null _ -> 
                Thoth.Json.Net.Encode.nil

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
                let encodePoint (p : V3d) =
                    match PRo3D.Base.CooTransformation.tryGetLatLonAlt planet p with
                    | None ->
                        failwithf "[RemoteApi] could not convert point %A to lat/lon on planet %A" p planet
                    | Some sc ->
                        Encode.array [|
                            Encode.float -sc.longitude
                            Encode.float sc.latitude
                            Encode.float sc.altitude
                        |]

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
                                        "coordinates", Encode.array (points |> Array.map encodePoint)
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

    
    open Thoth.Json.Net

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

        member x.getSelectedAnnotationId() : option<Guid> =
            x.FullModel.drawing.annotations.singleSelectLeaf.GetValue()

        member x.getAnnotationById (id : string) : option<Base.Annotation.Annotation> =
            let map = 
                x.FullModel.drawing.annotations.flat.Content.GetValue()

            match HashMap.tryFind (Guid.Parse(id)) map with
            | Some (AdaptiveAnnotations annotation) ->
                annotation.Current.GetValue()
                |> Some                
            | _ -> None

        member x.getAnnotationPointsById(id : string) =
            match x.getAnnotationById id with
            | Some a ->
                Encode.array [|
                    for p in a.points |> IndexList.toArray do                        
                        Encode.array [|
                            Encode.float p.X
                            Encode.float p.Y
                            Encode.float p.Z
                        |]
                |] |> Some                
            | _ -> None

        member x.getSelectedSurfaceId() : option<Guid> =
            x.FullModel.scene.surfacesModel.surfaces.singleSelectLeaf.GetValue()

         member x.getSurfaceById(id : string) : option<Surface.Surface> =
            let map = 
                x.FullModel.scene.surfacesModel.surfaces.flat.Content.GetValue()
            
            match HashMap.tryFind (Guid.Parse(id)) map with
            | Some (AdaptiveSurfaces surface) ->
                surface.Current.GetValue() |> Some
            | _ -> 
                None

        member x.setSurfaceTransform(id, forward : M44d) =
            let trafo = Trafo3d(forward, forward.Inverse)

            Log.line "[remoteApi] setting trafo"
            Log.line "[remoteApi] %A" trafo

            let action = 
                Surface.SurfaceAppAction.SetPreTrafoById(id, trafo)
                |> ViewerAction.SurfaceActions

            action |> emit
            ()
        
        member x.QueryAnnotation(                
                attributeNames       : list<string>, 
                heightRange          : Range1d,
                outputReferenceFrame : OutputReferenceFrame) =

            let queryAnnotationId = x.getSelectedAnnotationId()
            let annotations = x.FullModel.drawing.annotations.flat.Content.GetValue()
            let anno = 
                queryAnnotationId 
                |> Option.bind(fun x -> HashMap.tryFind x annotations)
                |> Option.bind(fun x ->
                    match x with
                    | AdaptiveAnnotations a -> Some a
                    | _ -> None
                )

            let cutoutSurfaceId = x.getSelectedSurfaceId()
            let surfaces = x.FullModel.scene.surfacesModel.surfaces.flat.Content.GetValue()
            let surf = 
                cutoutSurfaceId 
                |> Option.bind(fun x -> HashMap.tryFind x surfaces) 
                |> Option.bind(fun x ->
                    match x with
                    | AdaptiveSurfaces s -> Some s
                    | _ -> None
                )

            match (anno, surf) with
            |  (Some queryAnnotation, Some cutoutSurface) -> 
                let anno = queryAnnotation.Current.GetValue()
                // let sgSurfaces = 
                //     x.FullModel.scene.surfacesModel.sgSurfaces.Content.GetValue()

                let patchHierarchies = 
                    cutoutSurface.opcPaths.GetValue()
                    |> Seq.map Prinziple.register
                    |> Seq.map (fun x -> 
                        PatchHierarchy.load 
                            PRo3D.Base.Serialization.binarySerializer.Pickle 
                            PRo3D.Base.Serialization.binarySerializer.UnPickle
                            (OpcPaths x), x
                    ) 
                    |> Seq.toList

                // let opcs = 
                //     sgSurfaces 
                //     |> Seq.choose (fun (_,s) -> s.opcScene.GetValue())

                // let patchHierarchies = 
                //     opcs 
                //     |> Seq.collect (fun scene -> 
                //         scene.patchHierarchies
                //         |> Seq.map Prinziple.register
                //         |> Seq.map (fun x -> 
                //             Aardvark.Data.Opc.PatchHierarchy.load 
                //                 PRo3D.Base.Serialization.binarySerializer.Pickle 
                //                 PRo3D.Base.Serialization.binarySerializer.UnPickle
                //                 (Aardvark.Data.Opc.OpcPaths x), x
                //         )
                //     )
                //     |> Seq.toList

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

    module HttpV2 =
        open System
        open System.IO

        open System.Text.Json
        open System.Collections.Concurrent

        let http = HttpBackend.Instance
        let (>=>) x y = http.compose x y

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

        let captureSnapshot (api : Api) (r : IHttpRequest) =
            http.bindBody (fun (data : byte[]) ->
                let command : SaveCheckpointRequest = data |> JsonSerializer.Deserialize
                let fullModel = api.GetCheckpointState(api.FullModel.Current.GetValue(), command.virtualFileName)
                http.ok (serializeCheckpoint fullModel)
            )

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


        let activateSnapshot (api : Api) (r : IHttpRequest) =
            http.bindBody (fun (data : byte[]) ->
                let d = JsonDocument.Parse(data)
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
                    http.ok ""
                | None -> 
                    api.SetSceneFromCheckpoint(sceneAsJson, drawingAsJson, None, selectedNodeId)
                    http.ok ""
                | Some (Result.Error e) -> 
                    http.internalError e
            )

        let importAnnotations (api : Api) (r : IHttpRequest) =
            http.bindBody (fun (data : byte[]) ->
                let d = JsonDocument.Parse(data)
                let scene = d.RootElement.GetProperty("scene")
                let drawingAsJson = scene.GetProperty("drawingAsJson").ToString()
                let source = 
                    match d.RootElement.TryGetProperty("source") with
                    | (true, v) -> v.GetString()
                    | _ -> ""

                api.ImportDrawingModel(drawingAsJson, source)
                http.ok ""
            )

        let getFullStateFor (api : Api) (importAnnotations : bool) (r : IHttpRequest) =
            http.bindBody (fun (data : byte[]) ->
                let d = JsonDocument.Parse(data)
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
                    http.ok (serializeCheckpoint fullModel)
                | None -> 
                    let  model, fullModel = api.ApplyGraphAndGetCheckpointState(sceneAsJson, drawingAsJson, None, selectedNodeId)
                    if importAnnotations then api.ImportDrawingModel(model.drawing.annotations, source)
                    http.ok (serializeCheckpoint fullModel)
                | Some (Result.Error e) -> 
                    http.internalError e
            )

        let getProvenanceGraph (api : Api) (r : IHttpRequest) =
            let graphJson = api.GetProvenanceGraphJson()
            http.ok graphJson 
             
    module Http =
        open System
        open System.IO
        open System.Threading

        open System.Text.Json
        open System.Collections.Concurrent

        open ProvenanceGraph
        open Newtonsoft.Json.Linq

        let http = HttpBackend.Instance
        let (>=>) x y = http.compose x y

        let loadScene (api : Api) (r : IHttpRequest) =
            http.bindBody (fun (data : byte[]) ->
                let command : LoadScene = data |> JsonSerializer.Deserialize
                if File.Exists command.sceneFile then
                    api.LoadScene command.sceneFile 
                    http.ok "done"
                else
                    http.badRequest "Oops, something went wrong here!"
            )
            
        let discoverSurfaces (api : Api) (r : IHttpRequest) =
            http.bindBody (fun (data : byte[]) ->
                let command : ChangeImportDirectories = data |> JsonSerializer.Deserialize
                api.LocateSurfaces(command.folders)
                http.ok "done"
            )
            
        let saveScene (api : Api) (r : IHttpRequest) =
            http.bindBody (fun (data : byte[]) ->
                let command : SaveScene = data |> JsonSerializer.Deserialize
                api.SaveScene command.sceneFile 
                http.ok "done"
            )
            
        let importOpc (api : Api) (r : IHttpRequest) =
            http.bindBody (fun (data : byte[]) ->
                let command : ImportOpc = data |> JsonSerializer.Deserialize
                api.ImportOpc command.folders 
                http.ok "done"
            )
            
        let provenanceGraphWebSocket (cancellationToken: CancellationToken) (hackDoNotSendInitialState : bool) (storage : PPersistence) (api : Api) =
           http.handShake (fun webSocket ctx -> 
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

                let buffer = SocketBuffer(128)

                System.Threading.Thread.Sleep(2000)
                task {
                    if hackDoNotSendInitialState then
                        // clear all previous state (a bit unclean, inbetween changes could have ben swallowed)
                        // this way only changes after subscribing will be visible in the websocket.
                        // the protocol could be changed, s.t. initial values are tagged
                        addDeltas()  // for sure adds into changes
                        changes.Take() |> ignore // will not block therefore.

                    let mutable loop = true

                    while loop && not <| cancellationToken.IsCancellationRequested do
                        let jsonMessage = changes.Take(cancellationToken)
                        do! webSocket.SendText(jsonMessage, cancellationToken)
                        let! msg = webSocket.Receive(buffer, cancellationToken)

                        match msg with
                        | WebSocketOpCode.Text ->
                            ()

                        | WebSocketOpCode.Close ->
                            do! webSocket.Close cancellationToken
                            loop <- false
                            nodeSub.Dispose()

                        | _ -> ()
                }
            )
        
        let annotationsGeoJsonWebSocket (cancellationToken: CancellationToken) (planet : Option<Base.Planet>) (api : Api) =
            http.handShake (fun webSocket ctx -> 
                let geoJsonGeometries = 
                    api.FullModel.drawing.annotations.flat 
                    |> AMap.chooseA (fun k l ->
                        match PRo3D.Core.Drawing.DrawingApp.tryToAnnotation l with
                        | None -> AVal.constant None
                        | Some annotation -> 
                            annotation.Current 
                            |> AVal.map (Base.Annotation.GeoJSONExport.annotationToGeoJsonGeometry (fun _ -> false) planet >> Some)
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
                let buffer = SocketBuffer(128)

                task {
                    let mutable loop = true

                    while loop && not cancellationToken.IsCancellationRequested do
                        let jsonMessage = changes.Take(cancellationToken)
                        do! webSocket.SendText(jsonMessage, cancellationToken)
                        let! msg = webSocket.Receive(buffer, cancellationToken)

                        match msg with
                        | WebSocketOpCode.Text ->
                            ()

                        | WebSocketOpCode.Close ->
                            do! webSocket.Close cancellationToken
                            loop <- false
                            geometriesSub.Dispose()

                        | _ -> ()
                }
            )

        module QueryAnnotation =
            let parseCoordinateSpace (value: string) : Option<OutputReferenceFrame> =
                match value.ToLower() with
                | "local" -> Some OutputReferenceFrame.Local
                | "global" -> Some OutputReferenceFrame.Global
                | "centered" -> Some OutputReferenceFrame.Centered
                | _ -> None // or handle as an error case

            let parseGeometryType (value: string) : Option<OutputGeometryType> =
                match value.ToLower() with
                | "pointcloud" -> Some OutputGeometryType.PointCloud
                | "mesh" -> Some OutputGeometryType.Mesh
                | _ -> None // or handle as an error case

            type QueryResults = System.Collections.Generic.List<Base.QueryResult>

            let queryAnnotation
                (api : Api) 
                (f : OutputReferenceFrame -> OutputGeometryType -> QueryResults -> WebPart) 
                (httpRequest : IHttpRequest) =

                http.bindBody (fun body ->
                    let input = body |> PRo3D.Base.QueryApi.parseRequest

                    match input with
                    | Result.Ok input -> 
                        match ((parseCoordinateSpace input.outputReferenceFrame), parseGeometryType(input.outputGeometryType)) with
                        | (Some outputReferenceFrame, Some outputGeometryType) ->
                            //here we can go from primitive types to real types
                            match api.QueryAnnotation(                            
                                input.queryAttributes, 
                                Range1d.FromCenterAndSize(0, input.distanceToPlane), 
                                outputReferenceFrame) with
                            | None -> http.badRequest "Oops, something went wrong here!"
                            | Some queryResults -> 
                                f  outputReferenceFrame outputGeometryType queryResults
                        | _ -> http.badRequest "could not parse outputReferenceFrame and/or outputGeometryType"
                    | _ -> http.badRequest "could not parse command"
                )

            let queryAnnotationAsObj (api : Api) = 

                let toResult 
                    (frame        : OutputReferenceFrame) 
                    (geometryType : OutputGeometryType) 
                    (results      : QueryResults) = 

                    let s = 
                        if geometryType = OutputGeometryType.PointCloud then
                            PRo3D.Base.AnnotationQuery.queryResultsToCoordinatesSet frame results
                        else
                            PRo3D.Base.AnnotationQuery.queryResultsToObj frame geometryType results
                            
                    http.ok s

                queryAnnotation api toResult

            let queryAnnotationAsJson (api : Api) = 

                let toJson (_ : OutputReferenceFrame) (_ : OutputGeometryType) (results : QueryResults) = 
                    let s = PRo3D.Base.QueryApi.hitsToJson results //todo: also add frame
                    http.ok s

                queryAnnotation api toJson
        
        module Surfaces =
            let transform (surfaceId : option<string>) (api : Api) (req : IHttpRequest) = 
                match surfaceId with 
                | Some id ->
                    match api.getSurfaceById(id) with
                    | Some _ ->
                        http.bindBody (function
                            | "" -> 
                                http.badRequest "No payload"
                            | validPayload -> 
                                let parsedJson = JObject.Parse(validPayload)
                                let forward = parsedJson.["forward"].ToString()

                                let forward = forward |> M44d.Parse

                                api.setSurfaceTransform(id |> Guid, forward)

                                http.ok (sprintf "Received payload %s" (forward.ToString()))
                        )
                    | None ->                                             
                        http.notFound "Surface not found"
                | None -> 
                    match api.getSelectedSurfaceId() with
                    | Some id ->
                        match api.getSurfaceById(id.ToString()) with
                        | Some _ ->
                            http.bindBody (function
                                | "" -> 
                                    http.badRequest "No payload"
                                | validPayload -> 
                                    let parsedJson = JObject.Parse(validPayload)
                                    let forward = parsedJson.["forward"].ToString()
                                    let forward = forward |> M44d.Parse
                                    api.setSurfaceTransform(id, forward)
                                    http.ok (sprintf "Received payload %s" (forward.ToString()))
                            )
                        | None ->                                             
                            http.notFound "Selected surface does not exist - really bad"
                    | None ->
                        http.notFound $"no surface selected"

        module Annotations = 
            let getPoints (annotationId : option<string>) (api : Api) =
                match annotationId with
                | Some id ->
                    match api.getAnnotationPointsById(id) with
                    | Some s -> 
                        s 
                        |> Encode.toString 4 
                        |> http.ok
                    | None -> 
                        http.notFound $"Annotation of {id} not found"
                | None ->
                    match api.getSelectedAnnotationId() with
                    | Some id ->
                        match api.getAnnotationPointsById(id.ToString()) with
                        | Some s -> 
                            s 
                            |> Encode.toString 4 
                            |> http.ok
                        | None -> 
                            http.notFound $"Selected annotation does not exist - really bad"
                    | None ->
                        http.notFound $"no annotation selected"

        let webPart (cancellationToken: CancellationToken) (storage : PPersistence) (api : Api) = 
            http.choose [
                http.route "/loadScene" >=> http.request (loadScene api)
                http.route "/importOpc" >=> http.request (importOpc api)
                http.route "/saveScene" >=> http.request (saveScene api)
                http.route "/discoverSurfaces" >=> http.request (discoverSurfaces api)
                http.subRoute "/v2" (
                    http.choose [
                        http.route "/captureSnapshot"    >=> http.request (HttpV2.captureSnapshot api)
                        http.route "/activateSnapshot"   >=> http.request (HttpV2.activateSnapshot api)
                        http.route "/getProvenanceGraph" >=> http.request (HttpV2.getProvenanceGraph api)
                        http.route "/importAnnotations"  >=> http.request (HttpV2.importAnnotations api)
                        http.route "/getFullStateFor"    >=> http.request (HttpV2.getFullStateFor api false)
                        http.route "/importAnnotationsFromGraph"  >=> http.request (HttpV2.getFullStateFor api true)
                        http.route "/provenanceGraph" >=> (fun ctx -> 
                            Log.line "connect to ws with initial state..."
                            provenanceGraphWebSocket cancellationToken false storage api ctx
                        )
                        http.route "/provenanceGraphChanges" >=> (fun ctx -> 
                            Log.line "connect to ws without initial state..."
                            provenanceGraphWebSocket cancellationToken true storage api ctx
                        ) 
                    ]
                )
                http.subRoute "/integration" (
                    http.choose [
                        http.route "/ws/geojson_xyz" >=> annotationsGeoJsonWebSocket cancellationToken None api
                        http.route "/geojson_latlon" >=> http.request (fun r ->
                            let model = api.FullModel.drawing.annotations.Current |> AVal.force
                            let annotations = 
                                model.flat 
                                |> HashMap.values
                                |> Seq.choose (function Leaf.Annotations s -> Some s | _ -> None)
                                |> Seq.toList

                            let json = GeoJsonExport.GeoJson.toJson Base.Planet.Mars annotations
                            //let json = Base.Annotation.GeoJSONExport.toGeoJsonString (Base.Planet.Mars |> Some) annotations
                            http.ok json
                        )
                    ]
                )
                http.subRoute "/queries" (
                    http.choose [
                        http.route "/findAnnotation"  >=> http.mimeType "application/json; charset=utf-8" >=> (
                            http.request (fun r -> 
                                match r.QueryParam "id" with
                                | Some v -> 
                                    let a = api.FindAnnotation(Some v)
                                    let json = Thoth.Json.Net.Encode.Auto.toString a
                                    http.ok json
                                | _ -> 
                                    let a = api.FindAnnotation(None)
                                    let json = Thoth.Json.Net.Encode.Auto.toString a
                                    http.ok json
                            )
                        )
                        http.route "/queryAnnotationAsJson" >=> 
                            http.mimeType "application/json; charset=utf-8" 
                                >=> http.request (QueryAnnotation.queryAnnotationAsJson api)
                        http.route "/queryAnnotationAsObj" >=> http.request (QueryAnnotation.queryAnnotationAsObj api)
                    ]
                )                
                http.subRoute "/annotations" ( 
                    http.choose [
                        // GET
                        //     >=> path "/selected/points" 
                        //     >=> Annotations.getPoints None api
                        http.method HttpMethod.Get
                            >=> http.routef "/%s/points" (fun id ->
                                match id with 
                                | "selected" -> 
                                    Log.line "retrieving selected"
                                    Annotations.getPoints None api
                                | _ -> 
                                    Log.line "retrieving by id"
                                    Annotations.getPoints (Some id) api
                        )
                    ]
                )
                http.subRoute "/surfaces" (
                    http.choose [                        
                        // PUT
                        //     >=> path "/selected/transformation" 
                        //     >=> request (fun (req: HttpRequest) -> Surfaces.transform None api req)
                        http.method HttpMethod.Put
                            >=> http.routef "/%s/transformation" (fun id ->
                                match id with 
                                | "selected" -> 
                                    http.request (Surfaces.transform None api)
                                | _ ->
                                    http.request (Surfaces.transform (Some id) api)
                            )
                        http.notFound "Endpoint not found"
                    ]
                )
            ]


