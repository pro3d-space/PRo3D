namespace PRo3D.Provenance

open System
open System.Collections.Generic

module ZTreeInner = 
    type Node<'a> = 
        Node of 'a * Node<'a> list with

        member x.Value =
            let (Node (v, _)) = x in v

        member x.Children =
            let (Node (_, c)) = x in c

    // Path type for the zipper
    type Path<'a> =
        | Top of 'a
        | Inner of 'a * Node<'a> list * Path<'a> * Node<'a> list

        member x.Value =
            match x with
            | Top v
            | Inner (v, _, _, _) -> v
           
open ZTreeInner

type ZTree<'a> =
    | Empty
    | Zipped of Node<'a> * Path<'a> with

    static member Single (value : 'a) =
        Zipped (Node (value, []), Top value)

    member x.TryValue =
        match x with
            | Empty -> None
            | Zipped (n, _) -> Some n.Value

    member x.Value =
        match x.TryValue with
            | None -> raise (ArgumentException ("Tree is empty"))
            | Some v -> v

    member x.IsRoot =
        match x with
            | Zipped (_, Top _) -> true
            | _ -> false

    member x.Left =
        match x with
            | Empty
            | Zipped (_, Top _) 
            | Zipped (_, Inner (_, [], _, _)) -> 
                None
            | Zipped (t, Inner (_, l::left, up, right)) -> 
                Some (Zipped (l, Inner (l.Value, left, up, t::right)))

    member x.Right =
        match x with
            | Empty
            | Zipped (_, Top _)
            | Zipped (_, Inner (_, _, _, [])) -> 
                None
            | Zipped (t, Inner (_, left, up, r::right)) -> 
                Some (Zipped (r, Inner (r.Value, t::left, up, right)))

    member x.Parent =
        match x with
            | Empty
            | Zipped (_, Top _) -> 
                None
            | Zipped (t, Inner (_, left, up, right)) ->
                Some (Zipped (Node (up.Value, List.rev left @ t::right), up))

    member x.Child =
        match x with
            | Empty
            | Zipped (Node (_, []), _) ->
                None
            | Zipped (Node (_, x::xs), p) ->
                Some (Zipped (x, Inner(x.Value, [], p, xs)))

    member x.Children =
        let rec sib accum (t : ZTree<'a>) =
            match t.Right with
                | None -> t::accum
                | Some r -> sib (t::accum) r

        match x.Child with
            | None -> []
            | Some t -> sib [] t

    member x.Filter (predicate : 'a -> bool) = 
        match x with
            | Empty -> []
            | _ -> [
                if predicate x.Value then yield x
                yield! x.Children 
                            |> List.collect (fun t -> t.Filter predicate)
            ]

    member x.Map (f : 'a -> 'b) =
        let rec mapNode = function
            | Node (value, children) -> Node (f value, List.map mapNode children)

        let rec mapPath = function
            | Top value ->
                Top (f value)
            | Inner (value, left, up, right) ->
                Inner (f value, List.map mapNode left, mapPath up, List.map mapNode right)

        match x with
            | Empty -> Empty
            | Zipped (n, p) -> Zipped (mapNode n, mapPath p)

    // Find methods use filter, may be optimized
    member x.TryFind (predicate : 'a -> bool) =
        match x.Filter predicate with
            | t::_ -> Some t
            | _ -> None
    
    member x.Find (predicate : 'a -> bool) =
        match x.TryFind predicate with
            | Some t -> t
            | None -> raise (KeyNotFoundException ())

    member x.FilterChildren (predicate : 'a -> bool) =
        let rec filter path accum left = function
            | [] -> accum
            | (x : Node<'a>)::xs ->
                let n = [ if (predicate x.Value) then 
                            yield Zipped (x, Inner (x.Value, left, path, xs)) ]
                    
                filter path (n @ accum) (x::left) xs            

        match x with
            | Empty -> []
            | Zipped (n, p) -> filter p [] [] n.Children 

    member x.IsLeaf =
        match x with
            | Zipped (Node (_, []), _) -> true
            | _ -> false

    member x.HasChildren =
        x.IsLeaf |> not

    member x.Root =
        match x.Parent with
            | None -> x
            | Some p -> p.Root

    member x.Insert (value : 'a) =
        match x with
            | Empty ->
                ZTree.Single value
            | Zipped (t, p) ->
                let n = Node (value, [])
                Zipped (n, Inner (value, [], p, t.Children))

    member x.Set (value : 'a) =
        let set = function
            | Top _ -> Top value
            | Inner (_, left, up, right) -> Inner (value, left, up, right)

        match x with
            | Empty ->
                ZTree.Single value
            | Zipped (t, p) ->
                let n = Node (value, t.Children)
                Zipped (n, set p)

    member x.Update (f : 'a -> 'a) =
        x.Set (f x.Value)

    member x.Count =
        let rec cnt (t : Node<'a>) =
            t.Children |> List.fold (fun c t -> c + cnt t) 1

        match x with
            | Empty -> 0
            | Zipped (t, _) -> cnt t

    member x.BranchingFactor =
        let rec cnt (t : Node<'a>) =
            match t.Children with
                | [] -> (0, 0)
                | c -> c |> List.fold (fun (n, b) t ->
                                        let (i, j) = cnt t in (n + i, b + j)
                                       ) (1, List.length t.Children)

        match x with
            | Empty -> float 0
            | Zipped (t, _) ->
                let (n, b) = cnt t in float b / float n

    member x.ToJson (f : 'a -> (string * string) list) =

        let printProperty x = sprintf @"""%s"" : ""%s""" (fst x) (snd x)

        let rec printProperties = function
            | x::xs -> printProperty x + ", " + printProperties xs
            | [] -> ""

        let rec printNodes = function
            | x::[] -> printNode x
            | x::xs -> printNode x + ", " + printNodes xs
            | [] -> ""

        and printNode (t : Node<'a>) =
            let props = f t.Value
            sprintf @"{ %s ""children"" : [ %s ] }" (printProperties props) (printNodes t.Children)
            
        match x with
            | Empty -> ""
            | Zipped (t, _) -> printNode t
            
and 'a ztree = ZTree<'a>

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ZTree =

    let single value = ZTree<_>.Single value

    let tryValue (tree : ZTree<'a>) = tree.TryValue

    let value (tree : ZTree<'a>) = tree.Value

    let isRoot (tree : ZTree<'a>) = tree.IsRoot

    let left (tree : ZTree<'a>) = tree.Left

    let right (tree : ZTree<'a>) = tree.Right

    let parent (tree : ZTree<'a>) = tree.Parent

    let child (tree : ZTree<'a>) = tree.Child

    let children (tree : ZTree<'a>) = tree.Children

    let filter (predicate : 'a -> bool) (tree : ZTree<'a>) = tree.Filter predicate

    let map (f : 'a -> 'b) (tree : ZTree<'a>) = tree.Map f

    let tryFind (predicate : 'a -> bool) (tree : ZTree<'a>) = tree.TryFind predicate

    let find (predicate : 'a -> bool) (tree : ZTree<'a>) = tree.Find predicate
   
    let filterChildren (predicate : 'a -> bool) (tree : ZTree<'a>) = tree.FilterChildren predicate

    let hasChildren (tree : ZTree<'a>) = tree.HasChildren

    let isLeaf (tree : ZTree<'a>) = tree.IsLeaf

    let root (tree : ZTree<'a>) = tree.Root

    let insert (value : 'a) (tree : ZTree<'a>) = tree.Insert value

    let set (value : 'a) (tree : ZTree<'a>) = tree.Set value

    let update (f : 'a -> 'a) (tree : ZTree<'a>) = tree.Update f

    let count (tree : ZTree<'a>) = tree.Count

    let branchingFactor (tree : ZTree<'a>) = tree.BranchingFactor

    let toJson (f : 'a -> (string * string) list) (tree : ZTree<'a>) = tree.ToJson f