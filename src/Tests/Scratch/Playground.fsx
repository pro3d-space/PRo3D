
#r "nuget: Newtonsoft.Json, 13.0.1"
#r "nuget: Cyjs.NET"
#r "nuget: Thoth.Json.Net"
#r "nuget: FSharp.Data.Adaptive"
#r "nuget: Chiron"


module Cyjs =
    open Cyjs.NET
    open Elements


    let myFirstStyledGraph =     
        CyGraph.initEmpty ()
        |> CyGraph.withElements [
                node "n1" [ CyParam.label "FsLab"  ]
                node "n2" [ CyParam.label "ML" ]
 
                edge  "e1" "n1" "n2" [ CyParam.label "gjsdf" ]
 
            ]
        |> CyGraph.withStyle "node"     
                [
                    CyParam.label =. CyParam.label
                    CyParam.color "#A00975"
                ]
        |> CyGraph.withStyle "edge"     
                [
                    CyParam.label =. CyParam.label
                    CyParam.color "#A00975"
                ]
        |> CyGraph.show

open FSharp.Data.Adaptive

type PModel = string
type NodeId = string
type PMessage = string

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
        message : PMessage
        id : string
    } 

type ProvenanceModel = 
    { 
        nodes : HashMap<NodeId, PNode>; 
        edgeMap : HashMap<PInput, HashMap<NodeId, PEdge>>; 
        lastEdge : Option<PInput * NodeId> 
    } 

open Thoth.Json.Net

type CyNodeData =  
    {
        id : string
        payload : string
    }

module CyNodeData =
    
    let decoder : Decoder<CyNodeData> =
        Decode.object (fun get -> 
            {
                id = get.Required.Field "id" Decode.string
                payload = get.Required.Field "payload" Decode.string
            }
        )
    
    let encoder (node : CyNodeData) : JsonValue =
        Encode.object [
            "id", Encode.string node.id
            "payload", Encode.string node.payload
        ]
    


type CyEdgeData =
    {
        id : string
        sourceId : string
        targetId : string
        label : string
        payload : Option<string>
    }

module CyEdgeData =
    
    let decoder : Decoder<CyEdgeData> =
        Decode.object (fun get -> 
            {
                id = get.Required.Field "id" Decode.string
                sourceId = get.Required.Field "source" Decode.string
                targetId = get.Required.Field "target" Decode.string
                label = get.Required.Field "label" Decode.string
                payload = get.Optional.Field "payload" Decode.string
            }
        )
    
    let encoder (node : CyEdgeData) : JsonValue =
        Encode.object [
            "id", Encode.string node.id
            "source", Encode.string node.sourceId
            "target", Encode.string node.targetId
            "label", Encode.string node.label
            "payload", Encode.option Encode.string node.payload
        ]


type CyNode = { data : CyNodeData }
type CyEdge = { data : CyEdgeData }

module CyNode =
    let decoder : Decoder<CyNode> =
        Decode.object (fun get -> 
            { data = get.Required.Field "data" CyNodeData.decoder }
        )
    let encode (n : CyNode) =
        Encode.object [
            "data", CyNodeData.encoder n.data
        ]

module CyEdge =
    let decoder : Decoder<CyEdge> =
        Decode.object (fun get -> 
            { data = get.Required.Field "data" CyEdgeData.decoder }
        )
    let encode (n : CyEdge) =
        Encode.object [
            "data", CyEdgeData.encoder n.data
        ]

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


let test = 
    { 
        lastEdge = None
        nodes = HashMap.ofList [
            "n0", { id = "n0"; model = Some "n0" }
            "n1", { id = "n1"; model = Some "n1" }
            "n2", { id = "n2"; model = Some "n2" }
        ]
        edgeMap = HashMap.ofList [
            PInput.Label "n0", HashMap.ofList [ "n1", { id = "e0";  message = "a" }]
            PInput.Label "n1", HashMap.ofList [ "n2", { id = "e1";  message = "a" }]
        ]
    }

let toCy (e : ProvenanceModel) =
    let input : CyNode = {data = { id = "input"; payload = "" } }
    let nodes : seq<CyNode> = e.nodes |> HashMap.toSeq |> Seq.map (fun (id,n) -> { data = { id = id; payload = n.id } } )
    let edges = 
        e.edgeMap
        |> Seq.collect (fun (sourceId, outgoing) -> 
            outgoing |> HashMap.toSeq |> Seq.map (fun (targetId, (e : PEdge)) -> 
                let source = match sourceId with | Label l -> l | Source s -> "input"
                { data = { id = e.id; sourceId = source; targetId = targetId; label = e.message; payload = None } }
            )
        )
    {
        elements = 
            {
                nodes = Seq.append [ input ] nodes |> Seq.toArray 
                edges = edges |> Seq.toArray
            }

    }

let js = test |> toCy |> CyDescription.encoder |> Encode.toString 3 
System.Console.WriteLine(js)
let back = js |> Decode.fromString CyDescription.decoder