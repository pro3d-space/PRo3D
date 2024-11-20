//5e9b730c-ff82-bdea-b0ea-a5fd4c53699a
//234c4de0-52e5-c626-82b0-6ea7d3273981
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Viewer

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Viewer
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveProvenanceModel(value : ProvenanceModel) =
    let _nodes_ = FSharp.Data.Adaptive.cmap(value.nodes)
    let _edges_ = FSharp.Data.Adaptive.cmap(value.edges)
    let _initialNode_ = FSharp.Data.Adaptive.cval(value.initialNode)
    let _lastEdge_ = FSharp.Data.Adaptive.cval(value.lastEdge)
    let _selectedNode_ = FSharp.Data.Adaptive.cval(value.selectedNode)
    let _automaticRecording_ = FSharp.Data.Adaptive.cval(value.automaticRecording)
    let _currentTrail_ = FSharp.Data.Adaptive.cval(value.currentTrail)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ProvenanceModel) = AdaptiveProvenanceModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ProvenanceModel) -> AdaptiveProvenanceModel(value)) (fun (adaptive : AdaptiveProvenanceModel) (value : ProvenanceModel) -> adaptive.Update(value))
    member __.Update(value : ProvenanceModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ProvenanceModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _nodes_.Value <- value.nodes
            _edges_.Value <- value.edges
            _initialNode_.Value <- value.initialNode
            _lastEdge_.Value <- value.lastEdge
            _selectedNode_.Value <- value.selectedNode
            _automaticRecording_.Value <- value.automaticRecording
            _currentTrail_.Value <- value.currentTrail
    member __.Current = __adaptive
    member __.nodes = _nodes_ :> FSharp.Data.Adaptive.amap<NodeId, PNode>
    member __.edges = _edges_ :> FSharp.Data.Adaptive.amap<EdgeId, PEdge>
    member __.initialNode = _initialNode_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<NodeId>>
    member __.lastEdge = _lastEdge_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<EdgeId>>
    member __.selectedNode = _selectedNode_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<PNode>>
    member __.automaticRecording = _automaticRecording_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.currentTrail = _currentTrail_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<PMessage>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ProvenanceModelLenses = 
    type ProvenanceModel with
        static member nodes_ = ((fun (self : ProvenanceModel) -> self.nodes), (fun (value : FSharp.Data.Adaptive.HashMap<NodeId, PNode>) (self : ProvenanceModel) -> { self with nodes = value }))
        static member edges_ = ((fun (self : ProvenanceModel) -> self.edges), (fun (value : FSharp.Data.Adaptive.HashMap<EdgeId, PEdge>) (self : ProvenanceModel) -> { self with edges = value }))
        static member initialNode_ = ((fun (self : ProvenanceModel) -> self.initialNode), (fun (value : Microsoft.FSharp.Core.Option<NodeId>) (self : ProvenanceModel) -> { self with initialNode = value }))
        static member lastEdge_ = ((fun (self : ProvenanceModel) -> self.lastEdge), (fun (value : Microsoft.FSharp.Core.Option<EdgeId>) (self : ProvenanceModel) -> { self with lastEdge = value }))
        static member selectedNode_ = ((fun (self : ProvenanceModel) -> self.selectedNode), (fun (value : Microsoft.FSharp.Core.Option<PNode>) (self : ProvenanceModel) -> { self with selectedNode = value }))
        static member automaticRecording_ = ((fun (self : ProvenanceModel) -> self.automaticRecording), (fun (value : Microsoft.FSharp.Core.bool) (self : ProvenanceModel) -> { self with automaticRecording = value }))
        static member currentTrail_ = ((fun (self : ProvenanceModel) -> self.currentTrail), (fun (value : Microsoft.FSharp.Collections.list<PMessage>) (self : ProvenanceModel) -> { self with currentTrail = value }))

