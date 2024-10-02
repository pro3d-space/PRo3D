namespace PRo3D.Core

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify

open Aardvark.VRVis
open Aardvark.Geometry
open Aardvark.Data.Opc

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Surface
open Chiron

#nowarn "0044"

[<ModelType>]
type Leaf =     
    | Surfaces     of value : Surface
    | Bookmarks    of value : Bookmark
    | Annotations  of value : Annotation //
    with     
      member x.id =
          match x with          
          | Surfaces    s -> s.guid
          | Bookmarks   b -> b.key
          | Annotations a -> a.key

      member x.visible =
          match x with          
          | Surfaces    s -> s.isVisible
          | Bookmarks   b -> true
          | Annotations a -> a.visible

      member x.setVisible (v:bool) =
          match x with          
          | Annotations  a -> Leaf.Annotations { a with visible = v }          
          | Surfaces     s -> Leaf.Surfaces    { s with isVisible = v }
          | Bookmarks   _ -> x

      member x.active =
          match x with
          | Annotations a -> false
          | Surfaces    s -> s.isActive
          | Bookmarks   b -> false

type Leaf with
    static member FromJson(_ : Leaf) =
        json {
            let! (surface : option<Surface>) = Json.tryRead "Surfaces"
            match surface with
            | Some s -> return Leaf.Surfaces s
            | None -> 
                let! (bookmark : option<Bookmark>) = Json.tryRead "Bookmarks"
                match bookmark with
                | Some b -> return Leaf.Bookmarks b
                | None -> 
                    let! (annotation : option<Annotation>) = Json.tryRead "Annotations"
                    match annotation with
                    | Some a -> return Leaf.Annotations a
                    | None -> 
                        return! Json.error "[VersionedGroups: unknown Leaf' type]"
        }
    
    static member ToJson(x : Leaf) =
        json {
            match x with
            | Surfaces s    -> do! Json.write "Surfaces"    s
            | Bookmarks b   -> do! Json.write "Bookmarks"   b
            | Annotations a -> do! Json.write "Annotations" a
        }
                
[<ModelType;ReferenceEquality>]
type Node = {
    version     : int
    key         : Guid
    name        : string
    leaves      : IndexList<Guid>
    subNodes    : IndexList<Node>
    visible     : bool
    expanded    : bool
}

type Node with
    static member current = 0
    static member private read0 =
        json {
            let! key      = Json.read "key"     
            let! name     = Json.read "name"    
            let! leaves   = Json.read "leaves"  
            let! subNodes = Json.read "subNodes"
            let! visible  = Json.read "visible" 
            let! expanded = Json.read "expanded"

            return 
                {
                    version  = Node.current
                    key      = key     
                    name     = name    
                    leaves   = leaves   |> IndexList.ofList
                    subNodes = subNodes |> IndexList.ofList
                    visible  = visible 
                    expanded = expanded
                }
        }

    static member FromJson (_  : Node) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Node.read0
            | _ -> return! v |> sprintf "don't know version %d of Node" |> Json.error
        }
    static member ToJson (x : Node) =
        json {
            do! Json.write "version"  x.version
            do! Json.write "key"      x.key     
            do! Json.write "name"     x.name    
            do! Json.write "leaves"   (x.leaves   |> IndexList.toList)
            do! Json.write "subNodes" (x.subNodes |> IndexList.toList)
            do! Json.write "visible"  x.visible 
            do! Json.write "expanded" x.expanded
        }

type TreeSelection = {
    id : Guid
    path : list<Index>
    name : string
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Group =
    let initRoot = {
        version  = Node.current
        name     = "root"
        key      =  Guid.NewGuid()
        leaves   = IndexList.Empty
        subNodes = IndexList.Empty 
        visible  = true
        expanded = true
    }

    let initChildSelection =  { 
        id   = Guid.Empty
        path = list.Empty
        name = "" 
    }

    let initGroupSelection =  { 
        id   = initRoot.key
        path = list.Empty
        name = initRoot.name 
    }

    let tryGet' (ans:HashSet<Leaf>) id = 
        ans |> Seq.tryFind(fun x -> x.id = id)

    let rec flatten (g:Node) : HashSet<Guid> =
        match ((IndexList.toList g.leaves), (IndexList.toList g.subNodes)) with
        | ([]    , []) -> HashSet.empty
        | (leaves, []) -> leaves |> HashSet.ofList
        | ([]    , subNodes) -> subNodes |> HashSet.ofList |> HashSet.collect flatten
        | (leaves, subNodes) ->
            let sga = subNodes |> HashSet.ofList |> HashSet.collect flatten
            HashSet.union (leaves |> HashSet.ofList) sga

    let rec flatNodes (g:Node) : list<Node> =
        match (IndexList.toList g.subNodes) with
        | [] -> [g]
        | subNodes -> subNodes |> List.collect(flatNodes)
        //| (leaves, subNodes) ->
        //    let sga = subNodes |> HashSet.ofList |> HashSet.collect flatten
        //    HashSet.union (leaves |> HashSet.ofList) sga

type SelectedItem = Child = 0 | Group = 1


[<ModelType; ReferenceEquality>]
type GroupsModel = {
    version              : int
    rootGroup            : Node
    activeGroup          : TreeSelection
    activeChild          : TreeSelection
    flat                 : HashMap<Guid,Leaf>
    groupsLookup         : HashMap<Guid,string>
    lastSelectedItem     : SelectedItem
    selectedLeaves       : HashSet<TreeSelection> 
    singleSelectLeaf     : option<Guid>
}

module Leaf =
    open Aardvark.UI.Incremental

    let toggleVisibility (c : Leaf) =
        match c with        
        | Surfaces    s -> 
            Log.warn "[Groups-Model] surface visible %A" s.isVisible
            Surfaces    { s with isVisible = not s.isVisible }
        | Annotations a -> Annotations { a with visible   = not a.visible }
        | _ -> c

    let setId (g : Guid) (c : Leaf) =
        match c with
        | Surfaces s -> Surfaces { s with guid = g}
        | Bookmarks m -> Bookmarks { m with key = g }
        | Annotations a -> Annotations { a with key = g }

    let setName (c: Leaf) (name: string) =
        match c with
        | Surfaces s  -> Surfaces    { s with name = name }
        | Bookmarks b -> Bookmarks   { b with name = name }
        | _ -> c

    let toSurfaces surfaces =
        surfaces
        |> HashMap.choose(fun _ x -> 
            match x with
            | Leaf.Surfaces s -> Some s
            | _ -> None )

    let toSurface leaf =
        match leaf with 
        | Leaf.Surfaces s -> s
        | _ -> leaf |> sprintf "wrong type %A" |> failwith

    let toBookmark leaf =
        match leaf with 
        | Leaf.Bookmarks s -> s
        | _ -> leaf |> sprintf "wrong type %A" |> failwith

    let toSurfaces' surfaces =
        surfaces |> IndexList.choose(fun x -> match x with | Leaf.Surfaces s -> Some s| _-> None)

    let toAnnotation leaf =
        match leaf with 
        | Leaf.Annotations a -> a         
        | _ -> leaf |> sprintf "wrong type %A; expected Annotations'" |> failwith

    let toAnnotations annotations =
        annotations
        |> HashMap.choose(fun _ x ->
            match x with
            | Leaf.Annotations s -> Some s                
            | _ -> None)

    let childrenToAnnotations annos =
        annos
        |> HashSet.choose(fun x -> 
            match x with
            | Leaf.Annotations a -> Some a            
            | _ -> None)

    let childrenToBookmarks bms =
        bms
        |> HashSet.choose(fun x -> 
            match x with
            | Leaf.Bookmarks b -> Some b
            | _ -> None)

    let mapToBookmarks bms =
        bms
        |> HashMap.choose(fun g x -> 
            match x with
            | Leaf.Bookmarks b -> Some b
            | _ -> None)

module GroupsModel =

    let current = 0    

    let tryGetSelectedAnnotation (model : GroupsModel) =        

        model.singleSelectLeaf 
        |> Option.bind(fun id ->
            model.flat 
            |> HashMap.tryFind id
        )
        |> Option.bind(fun l ->
            match l with 
            | Leaf.Annotations a -> Some a
            | _ -> None
        )
        
    let read0 = 
        json {              
            let! rootGroup = Json.read "rootGroup"

            let! flat = Json.read "flat"            
            let flat = flat |> List.map(fun (a : Leaf) -> (a.id, a)) |> HashMap.ofList
            
            let! groupsLookup = Json.read "groupsLookup"
            let groupsLookup  = groupsLookup |> HashMap.ofList //TODO TO: check if it is possible to create generic hmap from/to json            
                                    
            return {
                version             = current         
                rootGroup           = rootGroup 
                activeGroup         = Group.initGroupSelection 
                activeChild         = Group.initChildSelection 
                flat                = flat
                groupsLookup        = groupsLookup
                lastSelectedItem    = SelectedItem.Child
                selectedLeaves      = HashSet.Empty 
                singleSelectLeaf    = None
            }
        }

    let initial = {
        version          = current
        rootGroup        = Group.initRoot
        activeGroup      = Group.initGroupSelection
        activeChild      = Group.initChildSelection
        flat             = HashMap.Empty
        groupsLookup     = HashMap.Empty        
        lastSelectedItem = SelectedItem.Child
        selectedLeaves   = HashSet.Empty
        singleSelectLeaf = None
    }

    
    // selectedLeaves, singleSelectLeaf, activeGroup, activeChild are not patched
    let patchNames (g : Guid -> Guid)  (m : GroupsModel) =
        let leafNewNames, flat = 
            let m =
                m.flat 
                |> HashMap.toList |> List.map (fun (k,v) -> 
                    let newKey = g k
                    (k, newKey), (newKey, Leaf.setId newKey v)
                )
            let flat = m |> Seq.map snd |> HashMap.ofSeq
            let newNames = m |> Seq.map fst |> Map.ofSeq
            newNames, flat
        
        let rec getNodes (n : Node) =
            n :: (List.collect getNodes (IndexList.toList n.subNodes))
            
        let nodes = getNodes m.rootGroup
        let newNodeKeys = 
            nodes 
            |> List.map (fun n -> 
                let newKey = g n.key
                n.key, newKey
            )
            |> Map.ofList
        
        let rec mapNodeNames (n : Node) =
            { n with 
                key = Map.find n.key newNodeKeys; 
                leaves = n.leaves |> IndexList.map (fun l -> Map.find l leafNewNames)
                subNodes = IndexList.map mapNodeNames n.subNodes 
            }

        let mapAnyName (n : Guid) =
            match Map.tryFind n newNodeKeys, Map.tryFind n leafNewNames with
            | Some n, _ -> n
            | _, Some n -> n
            | _ -> failwith "[Groups] cannot map name"

        { m with 
            flat = flat; 
            rootGroup = mapNodeNames m.rootGroup; 
            groupsLookup = m.groupsLookup |> HashMap.toSeq |> Seq.map (fun (id, name) -> mapAnyName id, name) |> HashMap.ofSeq
            singleSelectLeaf = None
        }

type GroupsModel with  
    static member FromJson( _ : GroupsModel) =
        json {
            let! version = Json.read "version"
            match version with
            | 0 -> return! GroupsModel.read0
            | _ -> 
                return! version 
                |> sprintf "don't know version %d of Groupsmodel" 
                |> Json.error
        }

    static member ToJson (x:GroupsModel) = 
        json {            
            do! Json.write "version"      x.version
            do! Json.write "rootGroup"    x.rootGroup
            do! Json.write "flat"         (x.flat |> HashMap.toList |> List.map snd)
            do! Json.write "groupsLookup" (x.groupsLookup |> HashMap.toList)
        }


        
module Groups = 
    let updateLeaf' id f model =
        let update = 
            (fun (d:option<Leaf>) ->
                match d with 
                | Some k -> Some (f k)
                | None   -> None )
          
        HashMap.alter id update model.flat    
        
    let updateLeaf id f model =
        let flat' = updateLeaf' id f model
        { model with flat = flat' }
    
    let replaceLeaf (l:Leaf) (m:GroupsModel) : GroupsModel =
        let f = (fun _ -> l)
        updateLeaf l.id f m

[<ModelType>]
type AnnotationGroupsImporterModel = {
    rootGroupI : IndexList<Node>
    flatI      : HashMap<Guid,Leaf>
}

type Annotations = {
    version        : int
    annotations    : GroupsModel
    dnsColorLegend : FalseColorsModel
}

type Annotations with 
    static member current = 0
    static member private readV0 = 
        json {            
            let! annotations    = Json.read "annotations"
            let! dnsColorLegend = Json.read "dnsColorLegend"

            return {
                version        = Annotations.current
                annotations    = annotations
                dnsColorLegend = dnsColorLegend
            }
        }

    static member FromJson(_ : Annotations) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Annotations.readV0
            | _ -> return! v |> sprintf "don't know version %d  of Annotations" |> Json.error
        }

    static member ToJson (x : Annotations) =
        json {
            do! Json.write "version"        x.version
            do! Json.write "annotations"    x.annotations
            do! Json.write "dnsColorLegend" x.dnsColorLegend
        }

[<ModelType>]
type SurfaceModel = {
    version         : int
    surfaces        : GroupsModel
    sgSurfaces      : HashMap<Guid,SgSurface>
    sgGrouped       : IndexList<HashMap<Guid,SgSurface>>
    kdTreeCache     : HashMap<string, ConcreteKdIntersectionTree>
    debugPreTrafo   : string
}

module SurfaceModel =
    
    let current = 0    
    let read0 = 
        json {
            let! surfaces = Json.read "surfaces"
            return 
                {
                    version     = current
                    surfaces    = surfaces
                    sgSurfaces  = HashMap.empty
                    sgGrouped   = IndexList.empty
                    kdTreeCache = HashMap.empty
                    debugPreTrafo = ""
                }
        }    
 
    let withSgSurfaces sgSurfaces m =
        { m with sgSurfaces = sgSurfaces }
    
    let initSurfaceGroups group flat selected = 
        let surfaces = GroupsModel.initial
        { surfaces with rootGroup = group; singleSelectLeaf = selected; flat = flat }

    let initSurfaceModel group flat selected  = //sgs
        let surfaces = initSurfaceGroups group flat selected
        {
            version     = current
            surfaces    = surfaces
            sgSurfaces  = HashMap.Empty 
            sgGrouped   = IndexList.Empty
            kdTreeCache = HashMap.Empty
            debugPreTrafo = ""
        }
   
    //let surfaceModelPickler : Pickler<SurfaceModel> =
    //    Pickler.product initSurfaceModel
    //    ^+ Pickler.field (fun s -> s.surfaces.rootGroup)             Pickler.auto<Node>
    //    ^+ Pickler.field (fun s -> s.surfaces.flat)                  Pickler.auto<HashMap<Guid,Leaf>>
    //    ^. Pickler.field (fun s -> s.surfaces.singleSelectLeaf)      Pickler.auto<option<Guid>>

    let initial = initSurfaceModel Group.initRoot HashMap.Empty None
    
    let getSurface model guid =
        model.surfaces.flat |> HashMap.tryFind guid

    let groupSurfaces (sgSurfaces : HashMap<Guid, SgSurface>) (surfaces : HashMap<Guid, Surface>) =
        let debug = 
            sgSurfaces
            |> HashMap.toList
            |> List.groupBy(fun (_,x) -> 
                let surf = HashMap.find x.surface surfaces 
                surf.priority.value)
            |> List.map(fun (p,k) -> (p, k |> HashMap.ofList))
            |> List.sortBy fst
            |> List.map snd
            |> IndexList.ofList
        debug

    let triggerSgGrouping (model:SurfaceModel) =
        { model with sgGrouped = (groupSurfaces model.sgSurfaces (model.surfaces.flat |> Leaf.toSurfaces))}
   
    let updateSingleSurface (s': Surface) (model:SurfaceModel) =
        let surfaces' = 
            Groups.replaceLeaf (Leaf.Surfaces s') model.surfaces

        { model with surfaces = surfaces' }

    let updateSingleSurface' (model:SurfaceModel) (s': Surface) =
        updateSingleSurface s' model

type SurfaceModel with
    static member FromJson (_ : SurfaceModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! SurfaceModel.read0
            | _ ->
                return! v
                |> sprintf "don't know version %A  of ViewConfigModel"
                |> Json.error
        }

    static member ToJson (x : SurfaceModel) =
        json {
            do! Json.write "version"  x.version
            do! Json.write "surfaces" x.surfaces
        }
