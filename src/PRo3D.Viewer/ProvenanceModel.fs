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

type PInput =
    | Source of source : PModel
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
    abstract member GetPayloadForModel : PModel -> Payload
    abstract member GetPayloadForEdge : PEdge -> Payload
    abstract member ReadModel : Payload -> PModel
    abstract member ReadEdge : Payload -> PEdge

[<ModelType>]
type ProvenanceModel = 
    { 
        nodes : HashMap<NodeId, PNode>; 
        edges : HashMap<EdgeId, PEdge>; 
        lastEdge : Option<EdgeId> 
        selectedNode : Option<PNode>
        

        automaticRecording : bool

        currentTrail : list<PMessage> 

        storage : PPersistence
    } 

module ProvenanceApp =

    type ProvenanceMessage = 
    | ActivateNode of nodeId : string
    | ToggleAutomaticRecording
    | CreateNode 
    

module ProvenanceModel = 
    open System.Threading
    
    let invalid = 
        { storage = failwith ""; nodes = HashMap.empty; edges = HashMap.empty; 
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
                Source newPModel
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
                        payload = Option.map persistence.GetPayloadForModel p.model
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
                    | Source _ -> "input"
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
            }

        module CyDescription =

            let decoder : Decoder<CyDescription> =
                Decode.object (fun get -> 
                    {
                        elements = get.Required.Field "elements" CyElements.decoder
                    }
                )

            let encoder (d : CyDescription) =
                Encode.object [
                    "elements", CyElements.encode d.elements
                ]



        let toCy (storage : PPersistence) (e : ProvenanceModel) =
            let input : CyNode = { data = { id = "input"; payload = failwith "" } }
            let nodes : seq<CyNode> = e.nodes |> HashMap.toSeq |> Seq.map (fun (id,n) -> { data = { id = id; payload = failwith "" } } )
            let edges = 
                e.edges
                |> Seq.map (fun (edgeId, edge) -> 
                    let source = match edge.sourceId with | Label l -> l | Source s -> "input"
                    { data = { id = edge.id; sourceId = source; targetId = edge.targetId; label = PMessage.toHumanReadable edge.message; payload = None } }
                )
            {
                elements = 
                    {
                        nodes = Seq.append [ input ] nodes |> Seq.toArray 
                        edges = edges |> Seq.toArray
                    }

            }

        let toJs (storage : PPersistence) (e : ProvenanceModel) =
            let js = e |> toCy |> CyDescription.encoder |> Encode.toString 3 
            js


        let fromCyJson (storage : PPersistence) (json : string) : Result<ProvenanceModel, string> =
            let description = Decode.fromString CyDescription.decoder json
            
            match description with
            | Result.Ok d -> 
                let nodes = 
                    d.elements.nodes |> Array.map (fun n -> 
                        let payload = n.data.payload
                        //match payload with
                        //| Thoth.Payload.
                        failwith ""
                    )
                failwith ""
            | Result.Error err -> 
                Result.Error err