namespace PRo3D.Groups

open System
open Aardvark.Base
open Aardvark.Base.Incremental

open Aardvark.VRVis
open Aardvark.Geometry
open Aardvark.SceneGraph.Opc 

open PRo3D
open PRo3D.Base.Annotation
open PRo3D.Surfaces
open PRo3D.Bookmarkings
open PRo3D.Navigation2
open Chiron

#nowarn "0044"

[<DomainType>]
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
                
[<DomainType;ReferenceEquality>]
type Node = {
    version     : int
    key         : Guid
    name        : string
    leaves      : plist<Guid>
    subNodes    : plist<Node>
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
                    leaves   = leaves   |> PList.ofList
                    subNodes = subNodes |> PList.ofList
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
            do! Json.write "leaves"   (x.leaves   |> PList.toList)
            do! Json.write "subNodes" (x.subNodes |> PList.toList)
            do! Json.write "visible"  x.visible 
            do! Json.write "expanded" x.expanded
        }

type TreeSelection = {
    [<PrimaryKey>]
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
        leaves   = plist.Empty
        subNodes = plist.Empty 
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

    let tryGet' (ans:hset<Leaf>) id = 
        ans |> Seq.tryFind(fun x -> x.id = id)

    let rec flatten (g:Node) : hset<Guid> =
        match ((PList.toList g.leaves), (PList.toList g.subNodes)) with
        | ([]    , []) -> HSet.empty
        | (leaves, []) -> leaves |> HSet.ofList
        | ([]    , subNodes) -> subNodes |> HSet.ofList |> HSet.collect flatten
        | (leaves, subNodes) ->
            let sga = subNodes |> HSet.ofList |> HSet.collect flatten
            HSet.union (leaves |> HSet.ofList) sga

    let rec flatNodes (g:Node) : list<Node> =
        match (PList.toList g.subNodes) with
        | [] -> [g]
        | subNodes -> subNodes |> List.collect(flatNodes)
        //| (leaves, subNodes) ->
        //    let sga = subNodes |> HSet.ofList |> HSet.collect flatten
        //    HSet.union (leaves |> HSet.ofList) sga

type SelectedItem = Child = 0 | Group = 1


[<DomainType; ReferenceEquality>]
type GroupsModel = {
    version              : int
    rootGroup            : Node
    activeGroup          : TreeSelection
    activeChild          : TreeSelection
    flat                 : hmap<Guid,Leaf>
    groupsLookup         : hmap<Guid,string>
    lastSelectedItem     : SelectedItem
    selectedLeaves       : hset<TreeSelection> 
    singleSelectLeaf     : option<Guid>
}

module GroupsModel =

    let current = 0    

    let read0 = 
        json {              
            let! rootGroup = Json.read "rootGroup"

            let! flat = Json.read "flat"            
            let flat = flat |> List.map(fun (a : Leaf) -> (a.id, a)) |> HMap.ofList
            
            let! groupsLookup = Json.read "groupsLookup"
            let groupsLookup  = groupsLookup |> HMap.ofList //TODO TO: check if it is possible to create generic hmap from/to json            
                                    
            return {
                version             = current         
                rootGroup           = rootGroup 
                activeGroup         = Group.initGroupSelection 
                activeChild         = Group.initChildSelection 
                flat                = flat
                groupsLookup        = groupsLookup
                lastSelectedItem    = SelectedItem.Child
                selectedLeaves      = hset.Empty 
                singleSelectLeaf    = None
            }
        }

    let initial = {
        version          = current
        rootGroup        = Group.initRoot
        activeGroup      = Group.initGroupSelection
        activeChild      = Group.initChildSelection
        flat             = hmap.Empty
        groupsLookup     = hmap.Empty        
        lastSelectedItem = SelectedItem.Child
        selectedLeaves   = hset.Empty
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
            do! Json.write "flat"         (x.flat |> HMap.toList |> List.map snd)
            do! Json.write "groupsLookup" (x.groupsLookup |> HMap.toList)
        }

module Leaf =
    open Aardvark.UI.Incremental

    let toggleVisibility (c : Leaf) =
        match c with        
        | Surfaces    s -> Surfaces    { s with isVisible = not s.isVisible }
        | Annotations a -> Annotations { a with visible   = not a.visible }
        | _ -> c

    let setName (c: Leaf) (name: string) =
        match c with
        | Surfaces s  -> Surfaces    { s with name = name }
        | Bookmarks b -> Bookmarks   { b with name = name }
        | _ -> c

    let toSurfaces surfaces =
        surfaces
        |> HMap.choose(fun _ x -> 
            match x with
            | Leaf.Surfaces s -> Some s
            | _ -> None )

    let toSurface leaf =
        match leaf with 
        | Leaf.Surfaces s -> s
        | _ -> leaf |> sprintf "wrong type %A" |> failwith

    let toSurfaces' surfaces =
        surfaces |> PList.choose(fun x -> match x with | Leaf.Surfaces s -> Some s| _-> None)

    let toAnnotation leaf =
        match leaf with 
        | Leaf.Annotations a -> a         
        | _ -> leaf |> sprintf "wrong type %A; expected Annotations'" |> failwith

    let toAnnotations annotations =
        annotations
        |> HMap.choose(fun _ x ->
            match x with
            | Leaf.Annotations s -> Some s                
            | _ -> None)

    let childrenToAnnotations annos =
        annos
        |> HSet.choose(fun x -> 
            match x with
            | Leaf.Annotations a -> Some a            
            | _ -> None)

    let childrenToBookmarks bms =
        bms
        |> HSet.choose(fun x -> 
            match x with
            | Leaf.Bookmarks b -> Some b
            | _ -> None)
        
module Groups = 
    let updateLeaf' id f model =
        let update = 
            (fun (d:option<Leaf>) ->
                match d with 
                | Some k -> Some (f k)
                | None   -> None )
          
        HMap.alter id update model.flat    
        
    let updateLeaf id f model =
        let flat' = updateLeaf' id f model
        { model with flat = flat' }
    
    let replaceLeaf (l:Leaf) (m:GroupsModel) : GroupsModel =
        let f = (fun _ -> l)
        updateLeaf l.id f m

[<DomainType>]
type AnnotationGroupsImporterModel = {
    rootGroupI : plist<Node>
    flatI      : hmap<Guid,Leaf>
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

[<DomainType>]
type SurfaceModel = {
    version         : int
    surfaces        : GroupsModel
    sgSurfaces      : hmap<Guid,SgSurface>
    sgGrouped       : plist<hmap<Guid,SgSurface>>
    kdTreeCache     : hmap<string, ConcreteKdIntersectionTree>
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
                    sgSurfaces  = HMap.empty
                    sgGrouped   = PList.empty
                    kdTreeCache = HMap.empty
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
            sgSurfaces  = hmap.Empty //sgs //
            //sgSurfaceObjs = hmap.Empty
            sgGrouped   = plist.Empty
            kdTreeCache = hmap.Empty
        }
   
    //let surfaceModelPickler : Pickler<SurfaceModel> =
    //    Pickler.product initSurfaceModel
    //    ^+ Pickler.field (fun s -> s.surfaces.rootGroup)             Pickler.auto<Node>
    //    ^+ Pickler.field (fun s -> s.surfaces.flat)                  Pickler.auto<hmap<Guid,Leaf>>
    //    ^. Pickler.field (fun s -> s.surfaces.singleSelectLeaf)      Pickler.auto<option<Guid>>

    let initial = initSurfaceModel Group.initRoot hmap.Empty None
    
    let getSurface model guid =
        model.surfaces.flat |> HMap.tryFind guid

    let groupSurfaces (sgSurfaces : hmap<Guid, SgSurface>) (surfaces : hmap<Guid, Surface>) =
      sgSurfaces
        |> HMap.toList
        |> List.groupBy(fun (_,x) -> 
            let surf = HMap.find x.surface surfaces 
            surf.priority.value)
        |> List.map(fun (p,k) -> (p, k |> HMap.ofList))
        |> List.sortBy fst
        |> List.map snd
        |> PList.ofList

    let triggerSgGrouping (model:SurfaceModel) =
        { model with sgGrouped = (groupSurfaces model.sgSurfaces (model.surfaces.flat |> Leaf.toSurfaces))}
   
    let updateSingleSurface (s':PRo3D.Surfaces.Surface) (model:SurfaceModel) =
        let surfaces' = 
            Groups.replaceLeaf (Leaf.Surfaces s') model.surfaces

        { model with surfaces = surfaces' }

    let updateSingleSurface' (model:SurfaceModel) (s':PRo3D.Surfaces.Surface) =
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

//[<DomainType>]
//type Grouping<'a> = {
//  root                 : Node
//  flat                 : hmap<Guid,'a>
//  activeGroup          : TreeSelection
//  activeChild          : TreeSelection  
//  groupsLookup         : hmap<Guid,string>
//  lastSelectedItem     : SelectedItem
//  selectedLeaves       : hset<TreeSelection> 
//  singleSelectLeaf     : option<Guid>
//}

//type SaveAnnotations = {
//    annotations: Grouping<Annotation>
//    colorLegend : FalseColorsModel
//}

    //FsPickler.RegisterPicklerFactory (fun _ -> surfaceModelPickler)
