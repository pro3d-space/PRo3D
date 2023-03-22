namespace PRo3D.Viewer

open System

open FSharp.Data.Adaptive
open Adaptify

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.Core
open PRo3D.Base
open Chiron

module FsPickler =
    open MBrace.FsPickler
    open MBrace.FsPickler.Json
        
    let serializer = JsonSerializer()


[<RequireQualifiedAccess>]
type PMessage = 
    | SetCameraView of CameraView
    | FinishAnnotation of System.Guid
    | DrawingMessage of Drawing.DrawingAction
    | Branch
    | CreateNode of string
    | LoadScene of string

    with
        static member ToJson (n : PMessage) =
            json {
                do! Json.write "version" 1
                match n with
                | SetCameraView c -> 
                    do! Json.write "kind" "SetCameraView"
                    do! Json.writeWith Ext.toJson<CameraView,Ext> "cameraView" c
                | FinishAnnotation a -> 
                    do! Json.write "kind" "FinishAnnotation"
                    do! Json.write "id" a
                | DrawingMessage m -> 
                    do! Json.write "kind" "DrawingMessage"
                    let serialized = FsPickler.serializer.PickleToString(m)
                    do! Json.write "serialized" serialized
            }

module PMessage =
    let toHumanReadable (s : PMessage) =
        match s with
        | PMessage.SetCameraView _ -> "Set Camera"
        | PMessage.FinishAnnotation _ -> "Finish Annotation"
        | PMessage.DrawingMessage _ -> "Drawing"
        | PMessage.Branch -> "(branch)"
        | PMessage.CreateNode s -> sprintf "user node: %s" s
        | PMessage.LoadScene s -> sprintf "load scene: %s" s


type NodeId = string
type EdgeId = string


type PModel =
    {
        cameraView  : CameraView
        annotations : Drawing.DrawingModel
    } 

module PModel =

    module Thoth =
        
        open Thoth.Json.Net

        module Decode =

            let v3d : Decoder<V3d> =
                Decode.array Decode.float |> Decode.map (fun a -> V3d(a[0],a[1],a[2]))

            let cameraView : Decoder<CameraView> =
                Decode.object (fun get -> 
                    let camView = get.Required.Field "cv" (Decode.array v3d)
                    CameraView(camView[0], camView[1], camView[2], camView[3], camView[4])
                )

        module Encode =

            let v3d (v : V3d) =
                [| v.X; v.Y; v.Z |] |> Array.map Encode.float |> Encode.array 

            let cameraView (x : CameraView) =
                let camView = 
                    [| x.Sky; x.Location; x.Forward; x.Up; x.Right |] |> Array.map v3d

                Encode.object [
                    "cv", Encode.array camView
                ]
                

        module Payload =

            let decoder : Decoder<PModel> =
                Decode.object (fun get -> 
                    let cameraView = get.Required.Field "cameraView" Decode.cameraView
                    let drawingJson = get.Required.Field "drawingModel" Decode.string
                    let d = PRo3D.Core.Drawing.DrawingUtilities.IO.loadAnnotationsFromJson drawingJson
                    { cameraView = cameraView; annotations = { PRo3D.Core.Drawing.DrawingModel.initialdrawing with annotations = d.annotations } }
                )

            let encoder (p : PModel) : JsonValue =
                let drawingAsJson = PRo3D.Core.Drawing.IO.getSerialized p.annotations
                Encode.object [
                    "cameraView", Encode.string "jsonSerialized"
                    "drawingJson", Encode.string drawingAsJson
                ]



type PInput =
    | Label  of input : NodeId


type PNode = 
    {
        id     : NodeId
        model  : Option<PModel>
    } 

type PEdge =
    {
        sourceId : PInput
        targetId : NodeId
        message : PMessage
        id : string
    }  

type Payload =
    | JsonSerialized of string
    | BlobId of string

type PPersistence =
    abstract member GetPayloadForModel : string * PModel -> Payload
    abstract member GetPayloadForMessage : string * PMessage -> Payload
    abstract member ReadModel  : Payload -> PModel
    abstract member ReadMessage : Payload -> PMessage

[<ModelType>]
type ProvenanceModel = 
    { 
        nodes : HashMap<NodeId, PNode> 
        edges : HashMap<EdgeId, PEdge>
        initialNode : Option<NodeId>
        lastEdge : Option<EdgeId> 
        selectedNode : Option<PNode>
        

        automaticRecording : bool

        currentTrail : list<PMessage> 
    } 

module ProvenanceApp =

    type ProvenanceMessage = 
    | ActivateNode of nodeId : string
    | ToggleAutomaticRecording
    | CreateNode 
    

module ProvenanceModel = 
    open System.Threading
    
    let invalid = 
        { nodes = HashMap.empty; edges = HashMap.empty; initialNode = None
          lastEdge = None; automaticRecording = false; currentTrail = []; selectedNode = None 
        }

    let newNodeId = 
        let mutable id = 1
        fun () -> 
            //Interlocked.Increment(&id) |> sprintf "n%d" 
            Guid.NewGuid() |> string
    let newEdgeId = 
        let mutable id = 1
        fun () -> //Interlocked.Increment(&id) |> sprintf "e%d" 
            Guid.NewGuid() |> string


    let afterNode (input : PInput) (pm : ProvenanceModel) (newPModel : PModel) (msg : PMessage) : ProvenanceModel =
        
        let newNodeId = newNodeId()
        let newNode = { id = newNodeId; model = Some newPModel }
        let edge = { message = msg; id = newEdgeId(); sourceId = input; targetId = newNodeId }
        let edges = HashMap.add edge.id edge pm.edges

        { pm with nodes = HashMap.add newNode.id newNode pm.nodes; edges = edges; lastEdge = Some edge.id; selectedNode = Some newNode }

    let newNode (pm : ProvenanceModel) (newPModel : PModel) (msg : PMessage) : ProvenanceModel =
        let input = 
            match pm.lastEdge with
            | None -> 
                match pm.initialNode with
                | None -> failwith "no start node"
                | Some s -> Label s
            | Some e -> 
                match HashMap.tryFind e pm.edges with
                | None -> failwith "edge of tip not found"
                | Some e -> Label e.targetId


        afterNode input pm newPModel msg

    let updateTip (msg : PMessage) (newPModel : PModel) (pm : ProvenanceModel) = 
        match pm.lastEdge with
        | None -> failwith "should not update tip if there is none"
        | Some eId ->
            match HashMap.tryFind eId pm.edges with
            | Some e -> 
                let input = e.sourceId
                let newNode = { id = newNodeId(); model = Some newPModel }
                let newEdge = { id = newEdgeId(); sourceId = input; targetId = newNode.id; message = msg; }
                let edges = HashMap.add newEdge.id newEdge (HashMap.remove eId pm.edges)
                let nodes = HashMap.add newNode.id newNode (HashMap.remove e.targetId pm.nodes)  

                { pm with edges = edges; nodes = nodes; lastEdge = Some newEdge.id; selectedNode = Some newNode }
            | _ -> failwith "wrong tip"


    let tryTip (m : ProvenanceModel) =
        match m.lastEdge with
        | Some eId -> 
            match HashMap.tryFind eId m.edges with
            | Some es -> Some es.message
            | _ -> None
        | _ -> None

    module Thoth =
        
        open Thoth.Json.Net

        module Payload =

            let decoder : Decoder<Payload> =
                Decode.object (fun get -> 
                    let kind = get.Required.Field "kind" Decode.string
                    match kind with
                    | "jsonSerialized" -> 
                        let data = get.Required.Field "jsonSerialized" Decode.string
                        Payload.JsonSerialized data
                    | "blobId" ->
                        let id = get.Required.Field "blobId" Decode.string 
                        Payload.BlobId id
                    | _ -> 
                        failwith "unknown tag: %A" kind
                )

            let encoder (p : Payload) : JsonValue =
                match p with
                | Payload.JsonSerialized value -> 
                    Encode.object [
                        "kind", Encode.string "jsonSerialized"
                        "jsonSerialized", Encode.string value
                    ]
                | Payload.BlobId id -> 
                    Encode.object [
                        "kind", Encode.string "blobId"
                        "blobId", Encode.string id
                    ]
    

        type CyNodeData =  
            {
                id : string
                payload : Option<Payload>
            }

        module CyNodeData =
    
            let decoder : Decoder<CyNodeData> =
                Decode.object (fun get -> 
                    {
                        id = get.Required.Field "id" Decode.string
                        payload = get.Required.Field "payload" (Decode.option Payload.decoder)
                    }
                )
    
            let encoder (node : CyNodeData) : JsonValue =
                Encode.object [
                    "id", Encode.string node.id
                    "payload", Encode.option Payload.encoder node.payload
                ]
    


        type CyEdgeData =
            {
                id : string
                sourceId : string
                targetId : string
                label : string
                payload : Option<Payload>
            }

        module CyEdgeData =
    
            let decoder : Decoder<CyEdgeData> =
                Decode.object (fun get -> 
                    {
                        id = get.Required.Field "id" Decode.string
                        sourceId = get.Required.Field "source" Decode.string
                        targetId = get.Required.Field "target" Decode.string
                        label = get.Required.Field "label" Decode.string
                        payload = get.Optional.Field "payload" Payload.decoder
                    }
                )
    
            let encoder (node : CyEdgeData) : JsonValue =
                Encode.object [
                    "id", Encode.string node.id
                    "source", Encode.string node.sourceId
                    "target", Encode.string node.targetId
                    "label", Encode.string node.label
                    "payload", Encode.option Payload.encoder node.payload
                ]


        type CyNode = { data : CyNodeData }
        type CyEdge = { data : CyEdgeData }

        let e (s : string) = s

        module CyNode =
            let decoder : Decoder<CyNode> =
                Decode.object (fun get -> 
                    { data = get.Required.Field "data" CyNodeData.decoder }
                )
            let encode (n : CyNode) =
                Encode.object [
                    "data", CyNodeData.encoder n.data
                ]
            let toJs (n : CyNode) =
                n |> encode |> Encode.toString 1 |> e

            let fromPNode (persistence : PPersistence) (p : PNode) : CyNode =
                { 
                    data = { 
                        id = p.id; 
                        payload = Option.map (fun m -> persistence.GetPayloadForModel(p.id, m)) p.model
                    }
                }

        module CyEdge =
            let decoder : Decoder<CyEdge> =
                Decode.object (fun get -> 
                    { data = get.Required.Field "data" CyEdgeData.decoder }
                )
            let encode (n : CyEdge) =
                Encode.object [
                    "data", CyEdgeData.encoder n.data
                ]
            let toJs (n : CyEdge) =
                n |> encode |> Encode.toString 1 |> e

            let fromPEdge (p : PEdge) : CyEdge =
                let id = 
                    match p.sourceId with
                    | Label s -> s
                { data = { id = p.id; payload = None; sourceId = id; targetId = p.targetId; label = PMessage.toHumanReadable p.message }}

        type CyElements = 
            {
                nodes : array<CyNode>
                edges : array<CyEdge>
            }

        module CyElements =

            let decoder : Decoder<CyElements> =
                Decode.object (fun get -> 
                    {
                        nodes = get.Required.Field "nodes" (Thoth.Json.Net.Decode.array (CyNode.decoder))
                        edges =  get.Required.Field "edges" (Thoth.Json.Net.Decode.array (CyEdge.decoder))
                     }
                )

            let encode (c : CyElements) =
                Encode.object [
                    "nodes", c.nodes |> Array.map CyNode.encode |> Encode.array
                    "edges", c.edges |> Array.map CyEdge.encode |> Encode.array
                ]

        type CyDescription =
            { 
                elements : CyElements 
                startNode : string
            }

        module CyDescription =

            let decoder : Decoder<CyDescription> =
                Decode.object (fun get -> 
                    {
                        elements = get.Required.Field "elements" CyElements.decoder
                        startNode = get.Required.Field "startNode" Decode.string
                    }
                )

            let encoder (d : CyDescription) =
                Encode.object [
                    "elements", CyElements.encode d.elements
                    "startNode", Encode.string d.startNode
                ]



        let toCy (storage : PPersistence) (e : ProvenanceModel) =
            let nodes : seq<CyNode> = 
                e.nodes 
                |> HashMap.toList 
                |> Seq.map (fun (id,n) -> 
                        { 
                            data = { id = id; payload = n.model |> Option.map (fun m -> storage.GetPayloadForModel(id, m))  } 
                        } 
                )
            let edges = 
                e.edges
                |> Seq.map (fun (edgeId, edge) -> 
                    let source = match edge.sourceId with | Label l -> l 
                    { data = 
                        { 
                            id = edge.id; sourceId = source; 
                            targetId = edge.targetId; 
                            label = PMessage.toHumanReadable edge.message; 
                            payload = storage.GetPayloadForMessage(edge.id, edge.message) |> Some
                        } 
                    }
                )
            {
                elements = 
                    {
                        nodes = nodes |> Seq.toArray 
                        edges = edges |> Seq.toArray
                    }
                startNode = 
                    match e.initialNode with
                    | None -> failwith "provenance graphs in PRo3D need a startNode"
                    | Some n -> n
            }

        let toJs (storage : PPersistence) (e : ProvenanceModel) =
            let js = e |> toCy storage |> CyDescription.encoder |> Encode.toString 3 
            js


        let fromCyJson (storage : PPersistence) (json : string) : Result<ProvenanceModel, string> =
            let description = Decode.fromString CyDescription.decoder json
            
            match description with
            | Result.Ok d -> 
                let nodes : array<PNode> = 
                    d.elements.nodes |> Array.map (fun n -> 
                        let payload = n.data.payload
                        match payload with
                        | None -> { id = n.data.id; model = None }
                        | Some p -> 
                            { id = n.data.id; model = Some (storage.ReadModel p)}
                    )
                let edges : array<PEdge> =
                    d.elements.edges |> Array.map (fun n -> 
                        let message  = 
                            match n.data.payload with
                            | None -> PMessage.Branch
                            | Some p -> 
                                storage.ReadMessage p 
                        { sourceId = Label n.data.sourceId; targetId = n.data.targetId; message = message; id = n.data.id }
                    )
                let startNode = d.startNode

                let pm : ProvenanceModel = {
                    invalid with
                        nodes = nodes |> Array.map (fun n -> n.id, n) |> HashMap.ofArray
                        edges = edges |> Array.map (fun n -> n.id, n) |> HashMap.ofArray
                        initialNode = Some startNode
                        lastEdge = None
                        selectedNode = None
                }

                Result.Ok pm
            | Result.Error err -> 
                Result.Error err


    open System.IO
    open Thoth.Json.Net

    type LocalDirectoryStorage(subDir : string) =

        do if Directory.Exists subDir then () else Directory.CreateDirectory subDir |> ignore

        let getPath (id : string) = Path.ChangeExtension(Path.combine [subDir; id], ".json")
        let save (id : string) (json : string)= File.writeAllText (getPath id) json

        member x.GetPayloadForModel (id : string, pm : PModel) =
            let json = PModel.Thoth.Payload.encoder pm |> Encode.toString 0 
            save id json
            Payload.BlobId id

        member x.GetPayloadForMessage (id : string, m : PMessage) =
            Payload.BlobId "not implemented"

        member x.ReadModel(pl : Payload) : PModel =
            match pl with
            | Payload.BlobId id ->
                let json = File.ReadAllText(getPath id)
                match Decode.fromString PModel.Thoth.Payload.decoder json with
                | Result.Ok r -> r
                |_ -> 
                    failwithf "could not parse payload: %A" id
            | _ -> failwith "not implemented"

        member x.ReadMessage(p : Payload) : PMessage =
            PMessage.Branch

        interface PPersistence with

            member x.GetPayloadForModel (id : string, pm : PModel) = x.GetPayloadForModel(id, pm)
            member x.GetPayloadForMessage (id : string, m : PMessage) = x.GetPayloadForMessage(id, m)
            member x.ReadModel(pl : Payload) : PModel = x.ReadModel pl
            member x.ReadMessage(p : Payload) : PMessage = x.ReadMessage p



    type NopStorage() =

        member x.GetPayloadForModel (id : string, pm : PModel) =
            let json = PModel.Thoth.Payload.encoder pm |> Encode.toString 0 
            Payload.JsonSerialized json

        member x.GetPayloadForMessage (id : string, m : PMessage) =
            Payload.BlobId "not implemented"

        member x.ReadModel(pl : Payload) : PModel =
            match pl with
            | Payload.JsonSerialized s -> 
                match Decode.fromString PModel.Thoth.Payload.decoder s with
                | Result.Ok r -> r
                | _ -> failwithf "could not parse payload: %A" id
            | _ -> failwithf "could not parse payload: %A" id

        member x.ReadMessage(p : Payload) : PMessage =
            PMessage.Branch

        interface PPersistence with

            member x.GetPayloadForModel (id : string, pm : PModel) = x.GetPayloadForModel(id, pm)
            member x.GetPayloadForMessage (id : string, m : PMessage) = x.GetPayloadForMessage(id, m)
            member x.ReadModel(pl : Payload) : PModel = x.ReadModel pl
            member x.ReadMessage(p : Payload) : PMessage = x.ReadMessage p



    let localDirectory (subDir : string) =
        LocalDirectoryStorage(subDir) :> PPersistence

    let nopStorage () = 
        NopStorage() :> PPersistence