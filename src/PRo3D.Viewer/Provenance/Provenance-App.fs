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

[<AutoOpen>]
module private Helpers =

    type Node = PRo3D.Provenance.Node

    // Updates the provenance based on the id of a node and the given function; if the node
    // does not exist, the identity function is applied instead
    let updateById (f : Node ztree -> Provenance -> Provenance) (id : NodeId) (p : Provenance) =
        match p.tree |> ZTree.root |> ZTree.tryFind (fun n -> n.id = id) with
            | None ->
                Log.error "Provenance node with id '%A' does not exist!" id
                p
            | Some x ->
                p |> f x

    module Rules =

        let checkMessage (msg : Message) (input : Decision<Node ztree>) =
            input |> Decision.map (fun tree ->
                    if msg = Unknown then
                        Decided tree
                    else
                        Undecided tree 
            )

        let checkStateChanged (state : State) (input : Decision<ZTree<Node>>) =
            input |> Decision.map (fun tree ->
                    if tree.Value.state = state then
                        Decided tree
                    else
                        Undecided tree 
            )

        let checkParent (state : State) (input : Decision<Node ztree>) =
            input |> Decision.map (fun tree ->
                match tree.Parent with
                    | Some p when (p.Value.state = state) ->
                        Decided p
                    | _ ->
                        Undecided tree
            ) 

        let checkChildren (state : State) (input : Decision<Node ztree>) =
            input |> Decision.map (fun tree ->
                let c = tree |> ZTree.filterChildren (fun n -> n.state = state)
            
                if List.length c > 1 then
                    Log.warn "Multiple identical children in provenance graph."

                match c with
                    | [] -> Undecided tree
                    | t::_ -> Decided t
            ) 

        //let coalesceWithCurrent (story : Story) (bookmarks : Bookmarks) (state : State) (msg : Message) (input : Decision<Node ztree>) =
        let coalesceWithCurrent (state : State) (msg : Message) (input : Decision<Node ztree>) =
            input |> Decision.map (fun tree ->
                let node = tree.Value

                let coal = 
                    node |> Node.message
                         |> Option.map (fun m -> m = msg && Message.coalesce m)
                         |> Option.defaultValue false
                         //|> (&&) (story |> Story.isNodeReferenced node true |> not)
                         //|> (&&) (bookmarks |> Bookmarks.isNodeReferenced node |> not)
                         |> (&&) (ZTree.isLeaf tree)                

                match coal with
                    | true ->
                        let v = { node with state = state }
                        Decided (tree |> ZTree.set v)
                    | false ->
                        Undecided tree
            )

       // let coalesceWithChild (story : Story) (bookmarks : Bookmarks) (state : State) (msg : Message) (input : Decision<Node ztree>) =
        let coalesceWithChild (state : State) (msg : Message) (input : Decision<Node ztree>) =
            input |> Decision.map (fun tree ->
                let c =
                    tree |> ZTree.filterChildren (fun n ->
                                Message.coalesce msg &&
                                n.message = Some msg 
                                //&& 
                                //story |> Story.isNodeReferenced n true |> not &&
                                //bookmarks |> Bookmarks.isNodeReferenced n |> not
                            )
                            |> List.filter ZTree.isLeaf

                if List.length c > 1 then
                    Log.warn "Multiple leaf children to coalesce in provenance graph."

                match c with
                | [] -> 
                    Undecided tree
                | t::_ ->
                    let v = { t.Value with state = state }
                    Decided (t |> ZTree.set v)
            )

        let appendNew (state : State) (msg : Message) (input : Decision<Node ztree>)=
            input |> Decision.map (fun tree ->
                tree |> ZTree.insert (Node.create state (Some msg)) |> Decided
            )

[<AutoOpen>]
module private Events =
    // Fired when a node is clicked 
    let onNodeClick (cb : NodeId -> 'msg) =
        onEvent "onnodeclick" [] (List.head >> Pickler.unpickleOfJson >> NodeId.parse >> cb)

    // Fired when the mouse enters a node
    let onNodeMouseEnter (cb : NodeId -> 'msg) =
        onEvent "onnodemouseenter" [] (List.head >> Pickler.unpickleOfJson >> NodeId.parse >> cb)

    // Fired when the mouse leaves a node
    let onNodeMouseLeave (cb : unit -> 'msg) =
        onEvent "onnodemouseleave" [] (ignore >> cb)

module ProvenanceApp = 

    let init (state : State) =
        { tree = Node.create state None |> ZTree.single
          highlight = None
          hovered = None 
          reference = None }
    
    let rec update (msg : ProvenanceAction) (p : Provenance) =
        match msg with
            | Update (next, msg) ->
                let t =
                    Undecided p.tree
                        |> Rules.checkMessage msg
                        |> Rules.checkStateChanged next
                        |> Rules.checkParent next
                        |> Rules.checkChildren next
                        |> Rules.coalesceWithCurrent next msg
                        //|> Rules.coalesceWithChild story bookmarks next msg
                        |> Rules.appendNew next msg
                        |> Decision.get
    
                { p with tree = t }
    
            | Goto id ->
                p |> updateById (fun t p -> { p with tree = t }) id
    
            | MouseEnter id ->
                p |> updateById (fun t p -> { p with hovered = Some t }) id
    
            | MouseLeave ->
                { p with hovered = None }
    
            | SetHighlight id ->
                { p with highlight = Some id }
    
            | RemoveHighlight ->
                { p with highlight = None }
    
            | SetNodeReferenceSpace s ->
                { p with reference = s }
    
    let onBootInitial (name : string) (input : aval<'a>) (code : string) (node : DomNode<'msg>) =
        let init = code.Replace ("__DATA__", input |> AVal.force |> Pickler.jsonToString)
        let update = sprintf "%s.onmessage = function (data) { %s }" name (code.Replace ("__DATA__", "data"))

        onBoot init (
            onBoot' [name, input |> AVal.channel] update node
        )

    let view (p : AdaptiveProvenance) =
        let dependencies = [
            { kind = Script; name = "d3"; url = "http://d3js.org/d3.v5.min.js" }
            { kind = Stylesheet; name = "provenanceStyle"; url = "Provenance.css" }
            { kind = Script; name = "provenanceScript"; url = "Provenance.js" }
        ]
    
        let colorSelected = C3d (0.75, 0.95, 0.18);
        let colorHovered = C3d (0.2, 0.8, 0.99);
    
        let dropShadow (name : string) (color : C3d) =
            let colorMatrix =
                sprintf  "%f 0 0 0 0, 0 %f 0 0 0, 0 0 %f 0 0, 0 0 0 1 0" color.R color.G color.B
    
            Svg.filter [
                clazz name
                attribute "x" "-50%"
                attribute "y" "-50%"
                attribute "width" "200%"
                attribute "height" "200%"
            ] [
                Svg.feColorMatrix [
                    attribute "type" "matrix"
                    attribute "result" "whiteOut"
                    attribute "in" "SourceGraphic"
                    attribute "values" "0 0 0 0 1, 0 0 0 0 1, 0 0 0 0 1, 0 0 0 1 0"
                ]
    
                Svg.feColorMatrix [
                    attribute "type" "matrix"
                    attribute "result" "colorOut"
                    attribute "in" "whiteOut"
                    attribute "color-interpolation-filters" "sRGB"
                    attribute "values" colorMatrix
                ]
    
                Svg.feGaussianBlur [
                    attribute "result" "blurOut"
                    attribute "in" "colorOut"
                    attribute "stdDeviation" "2"
                ]
    
                Svg.feBlend [
                    attribute "in" "SourceGraphic"
                    attribute "in2" "blurOut"
                    attribute "mode" "normal"
                ]
            ]
            
        let provenanceData = adaptive {
            let! t = p.tree
            let! h = p.highlight |> AVal.map (Option.map string >> Option.defaultValue "")
            let! r = p.reference
    
            //// TODO: Dependency on the whole story struct is an overkill, can be optimized.
            //let! s = s.Current
            //let! b = b.Current
    
            let isReferenced n = false
                //match r with
                //    | None -> 
                //        false
                //    | Some Story ->
                //        s |> Story.isNodeReferenced n false
                //    | Some Bookmarks ->
                //        b |> Bookmarks.isNodeReferenced n
    
            let props n = 
                ("isReferenced", n |> isReferenced |> string) :: (PRo3D.Provenance.Node.properties n)
            
            // TODO: It might be possible to increase performance by handling
            // the highlight property with a separate channel that does not trigger the
            // recomputation of the whole tree
            let json = t.Root.ToJson props
            return sprintf @"{ ""current"" : ""%A"" ,  ""highlight"" : ""%s"" , ""tree"" : %s }" t.Value.id h json
        }
    
        let updateChart = "update(__DATA__)"
    
        div [ 
            clazz "provenanceView"
            onNodeClick Goto
            onNodeMouseEnter MouseEnter
            onNodeMouseLeave (fun _ -> MouseLeave)
        ] [
            require dependencies (
                onBootInitial "provenanceData" provenanceData updateChart (
                    div [] [
                        Svg.svg [ clazz "rootSvg" ] [
                            Svg.defs [] [
                                dropShadow "shadowSelected" colorSelected
                                dropShadow "shadowHovered" colorHovered
                            ]
    
                            Svg.g [ clazz "linkLayer" ] []
                            Svg.g [ clazz "nodeLayer" ] []
                        ]
                    ]
                )
            )
        ]