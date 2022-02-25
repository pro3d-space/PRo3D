namespace PRo3D.Provenance

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Application

open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis
open Aardvark.Rendering

open PRo3D.Base
open PRo3D.Core
open PRo3D.Base.Annotation

open PRo3D.Provenance.Abstraction

type NodeId = 
    private NodeId of Guid with

    static member generate () =
        NodeId (Guid.NewGuid ())

    static member ofGuid (v : Guid) =
        NodeId v

    static member parse (s : string) =
        s |> Guid.Parse |> NodeId

    static member tryParse (s : string) =
        try
            Some (s |> Guid.Parse |> NodeId)
        with
            | _ -> None

    override x.ToString () =
        let (NodeId v) = x in string v

[<ModelType>]
type Node = {    
    id      : NodeId
    state   : State
    message : Option<Message>
}

type NodeReferenceSpace =
    | Story
    | Bookmarks

[<ModelType>]
type Provenance = {
    tree        : ZTree<Node>
    highlight   : NodeId option
    hovered     : ZTree<Node> option
    reference   : NodeReferenceSpace option   // The active node reference space determines how nodes are highlighted
}                                           // E.g. nodes part of a story are highlighted if reference is set accordingly

type ProvenanceAction =
    | Update                of State * Message
    | Goto                  of NodeId
    | SetHighlight          of NodeId
    | RemoveHighlight
    | MouseEnter            of NodeId
    | MouseLeave
    | SetNodeReferenceSpace of NodeReferenceSpace option

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Node =

    let create (s : State) (m : Message option) =
        { id = NodeId.generate ()
          state = s
          message = m }

    let state (n : Node) = n.state

    let message (n : Node) = n.message

    let id (n : Node) = n.id

    let properties (n : Node) = [ 
        let (NodeId id) = n.id
        yield "id", (string id)

        match n.message with
        | Some m -> yield "msg", (string m)
        | None -> ()
    ]

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Provenance =

    let current (p : Provenance) =
        ZTree.value p.tree

    let state =
        current >> Node.state