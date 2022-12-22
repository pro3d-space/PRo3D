//1d5dbc83-05d2-4d9a-34d8-2f645a959c1e
//cc6402bb-34b7-6c1e-dc39-276db6ebfbbe
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Core

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Core
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveLeafCase =
    abstract member Update : Leaf -> AdaptiveLeafCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type private AdaptiveLeafSurfaces(value : PRo3D.Core.Surface.Surface) =
    let _value_ = PRo3D.Core.Surface.AdaptiveSurface(value)
    let mutable __value = value
    member __.Update(value : PRo3D.Core.Surface.Surface) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<PRo3D.Core.Surface.Surface>.ShallowEquals(value, __value))) then
            __value <- value
            _value_.Update(value)
    member __.value = _value_
    interface AdaptiveLeafCase with
        member x.Update(value : Leaf) =
            match value with
            | Leaf.Surfaces(value) ->
                x.Update(value)
                x :> AdaptiveLeafCase
            | Leaf.Bookmarks(value) -> AdaptiveLeafBookmarks(value) :> AdaptiveLeafCase
            | Leaf.Annotations(value) -> AdaptiveLeafAnnotations(value) :> AdaptiveLeafCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type private AdaptiveLeafBookmarks(value : Bookmark) =
    let _value_ = AdaptiveBookmark(value)
    let mutable __value = value
    member __.Update(value : Bookmark) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Bookmark>.ShallowEquals(value, __value))) then
            __value <- value
            _value_.Update(value)
    member __.value = _value_
    interface AdaptiveLeafCase with
        member x.Update(value : Leaf) =
            match value with
            | Leaf.Surfaces(value) -> AdaptiveLeafSurfaces(value) :> AdaptiveLeafCase
            | Leaf.Bookmarks(value) ->
                x.Update(value)
                x :> AdaptiveLeafCase
            | Leaf.Annotations(value) -> AdaptiveLeafAnnotations(value) :> AdaptiveLeafCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type private AdaptiveLeafAnnotations(value : PRo3D.Base.Annotation.Annotation) =
    let _value_ = PRo3D.Base.Annotation.AdaptiveAnnotation(value)
    let mutable __value = value
    member __.Update(value : PRo3D.Base.Annotation.Annotation) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<PRo3D.Base.Annotation.Annotation>.ShallowEquals(value, __value))) then
            __value <- value
            _value_.Update(value)
    member __.value = _value_
    interface AdaptiveLeafCase with
        member x.Update(value : Leaf) =
            match value with
            | Leaf.Surfaces(value) -> AdaptiveLeafSurfaces(value) :> AdaptiveLeafCase
            | Leaf.Bookmarks(value) -> AdaptiveLeafBookmarks(value) :> AdaptiveLeafCase
            | Leaf.Annotations(value) ->
                x.Update(value)
                x :> AdaptiveLeafCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveLeaf(value : Leaf) =
    inherit Adaptify.AdaptiveValue<AdaptiveLeafCase>()
    let mutable __value = value
    let mutable __current =
        match value with
        | Leaf.Surfaces(value) -> AdaptiveLeafSurfaces(value) :> AdaptiveLeafCase
        | Leaf.Bookmarks(value) -> AdaptiveLeafBookmarks(value) :> AdaptiveLeafCase
        | Leaf.Annotations(value) -> AdaptiveLeafAnnotations(value) :> AdaptiveLeafCase
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (t : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member CreateAdaptiveCase(value : Leaf) =
        match value with
        | Leaf.Surfaces(value) -> AdaptiveLeafSurfaces(value) :> AdaptiveLeafCase
        | Leaf.Bookmarks(value) -> AdaptiveLeafBookmarks(value) :> AdaptiveLeafCase
        | Leaf.Annotations(value) -> AdaptiveLeafAnnotations(value) :> AdaptiveLeafCase
    static member Create(value : Leaf) = AdaptiveLeaf(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Leaf) -> AdaptiveLeaf(value)) (fun (adaptive : AdaptiveLeaf) (value : Leaf) -> adaptive.Update(value))
    member __.Current = __adaptive
    member __.Update(value : Leaf) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Leaf>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            let __n = __current.Update(value)
            if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AdaptiveLeafCase>.ShallowEquals(__n, __current))) then
                __current <- __n
                __.MarkOutdated()
    override __.Compute(t : FSharp.Data.Adaptive.AdaptiveToken) = __current
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AdaptiveLeaf = 
    let (|AdaptiveSurfaces|AdaptiveBookmarks|AdaptiveAnnotations|) (value : AdaptiveLeafCase) =
        match value with
        | (:? AdaptiveLeafSurfaces as surfaces) -> AdaptiveSurfaces(surfaces.value)
        | (:? AdaptiveLeafBookmarks as bookmarks) -> AdaptiveBookmarks(bookmarks.value)
        | (:? AdaptiveLeafAnnotations as annotations) -> AdaptiveAnnotations(annotations.value)
        | _ -> failwith "unreachable"
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveNode(value : Node) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _key_ = FSharp.Data.Adaptive.cval(value.key)
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _leaves_ = FSharp.Data.Adaptive.clist(value.leaves)
    let _subNodes_ =
        let inline __arg2 (m : AdaptiveNode) (v : Node) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelList(value.subNodes, (fun (v : Node) -> AdaptiveNode(v)), __arg2, (fun (m : AdaptiveNode) -> m))
    let _visible_ = FSharp.Data.Adaptive.cval(value.visible)
    let _expanded_ = FSharp.Data.Adaptive.cval(value.expanded)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Node) = AdaptiveNode(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Node) -> AdaptiveNode(value)) (fun (adaptive : AdaptiveNode) (value : Node) -> adaptive.Update(value))
    member __.Update(value : Node) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Node>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _key_.Value <- value.key
            _name_.Value <- value.name
            _leaves_.Value <- value.leaves
            _subNodes_.Update(value.subNodes)
            _visible_.Value <- value.visible
            _expanded_.Value <- value.expanded
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.key = _key_ :> FSharp.Data.Adaptive.aval<System.Guid>
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.leaves = _leaves_ :> FSharp.Data.Adaptive.alist<System.Guid>
    member __.subNodes = _subNodes_ :> FSharp.Data.Adaptive.alist<AdaptiveNode>
    member __.visible = _visible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.expanded = _expanded_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module NodeLenses = 
    type Node with
        static member version_ = ((fun (self : Node) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Node) -> { self with version = value }))
        static member key_ = ((fun (self : Node) -> self.key), (fun (value : System.Guid) (self : Node) -> { self with key = value }))
        static member name_ = ((fun (self : Node) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : Node) -> { self with name = value }))
        static member leaves_ = ((fun (self : Node) -> self.leaves), (fun (value : FSharp.Data.Adaptive.IndexList<System.Guid>) (self : Node) -> { self with leaves = value }))
        static member subNodes_ = ((fun (self : Node) -> self.subNodes), (fun (value : FSharp.Data.Adaptive.IndexList<Node>) (self : Node) -> { self with subNodes = value }))
        static member visible_ = ((fun (self : Node) -> self.visible), (fun (value : Microsoft.FSharp.Core.bool) (self : Node) -> { self with visible = value }))
        static member expanded_ = ((fun (self : Node) -> self.expanded), (fun (value : Microsoft.FSharp.Core.bool) (self : Node) -> { self with expanded = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveGroupsModel(value : GroupsModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _rootGroup_ = AdaptiveNode(value.rootGroup)
    let _activeGroup_ = FSharp.Data.Adaptive.cval(value.activeGroup)
    let _activeChild_ = FSharp.Data.Adaptive.cval(value.activeChild)
    let _flat_ = FSharp.Data.Traceable.ChangeableModelMap(value.flat, (fun (v : Leaf) -> AdaptiveLeaf.CreateAdaptiveCase(v)), (fun (m : AdaptiveLeafCase) (v : Leaf) -> m.Update(v)), (fun (m : AdaptiveLeafCase) -> m))
    let _groupsLookup_ = FSharp.Data.Adaptive.cmap(value.groupsLookup)
    let _lastSelectedItem_ = FSharp.Data.Adaptive.cval(value.lastSelectedItem)
    let _selectedLeaves_ = FSharp.Data.Adaptive.cset(value.selectedLeaves)
    let _singleSelectLeaf_ = FSharp.Data.Adaptive.cval(value.singleSelectLeaf)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : GroupsModel) = AdaptiveGroupsModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : GroupsModel) -> AdaptiveGroupsModel(value)) (fun (adaptive : AdaptiveGroupsModel) (value : GroupsModel) -> adaptive.Update(value))
    member __.Update(value : GroupsModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<GroupsModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _rootGroup_.Update(value.rootGroup)
            _activeGroup_.Value <- value.activeGroup
            _activeChild_.Value <- value.activeChild
            _flat_.Update(value.flat)
            _groupsLookup_.Value <- value.groupsLookup
            _lastSelectedItem_.Value <- value.lastSelectedItem
            _selectedLeaves_.Value <- value.selectedLeaves
            _singleSelectLeaf_.Value <- value.singleSelectLeaf
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.rootGroup = _rootGroup_
    member __.activeGroup = _activeGroup_ :> FSharp.Data.Adaptive.aval<TreeSelection>
    member __.activeChild = _activeChild_ :> FSharp.Data.Adaptive.aval<TreeSelection>
    member __.flat = _flat_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveLeafCase>
    member __.groupsLookup = _groupsLookup_ :> FSharp.Data.Adaptive.amap<System.Guid, Microsoft.FSharp.Core.string>
    member __.lastSelectedItem = _lastSelectedItem_ :> FSharp.Data.Adaptive.aval<SelectedItem>
    member __.selectedLeaves = _selectedLeaves_ :> FSharp.Data.Adaptive.aset<TreeSelection>
    member __.singleSelectLeaf = _singleSelectLeaf_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.Guid>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module GroupsModelLenses = 
    type GroupsModel with
        static member version_ = ((fun (self : GroupsModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : GroupsModel) -> { self with version = value }))
        static member rootGroup_ = ((fun (self : GroupsModel) -> self.rootGroup), (fun (value : Node) (self : GroupsModel) -> { self with rootGroup = value }))
        static member activeGroup_ = ((fun (self : GroupsModel) -> self.activeGroup), (fun (value : TreeSelection) (self : GroupsModel) -> { self with activeGroup = value }))
        static member activeChild_ = ((fun (self : GroupsModel) -> self.activeChild), (fun (value : TreeSelection) (self : GroupsModel) -> { self with activeChild = value }))
        static member flat_ = ((fun (self : GroupsModel) -> self.flat), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Leaf>) (self : GroupsModel) -> { self with flat = value }))
        static member groupsLookup_ = ((fun (self : GroupsModel) -> self.groupsLookup), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Microsoft.FSharp.Core.string>) (self : GroupsModel) -> { self with groupsLookup = value }))
        static member lastSelectedItem_ = ((fun (self : GroupsModel) -> self.lastSelectedItem), (fun (value : SelectedItem) (self : GroupsModel) -> { self with lastSelectedItem = value }))
        static member selectedLeaves_ = ((fun (self : GroupsModel) -> self.selectedLeaves), (fun (value : FSharp.Data.Adaptive.HashSet<TreeSelection>) (self : GroupsModel) -> { self with selectedLeaves = value }))
        static member singleSelectLeaf_ = ((fun (self : GroupsModel) -> self.singleSelectLeaf), (fun (value : Microsoft.FSharp.Core.option<System.Guid>) (self : GroupsModel) -> { self with singleSelectLeaf = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveAnnotationGroupsImporterModel(value : AnnotationGroupsImporterModel) =
    let _rootGroupI_ =
        let inline __arg2 (m : AdaptiveNode) (v : Node) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelList(value.rootGroupI, (fun (v : Node) -> AdaptiveNode(v)), __arg2, (fun (m : AdaptiveNode) -> m))
    let _flatI_ = FSharp.Data.Traceable.ChangeableModelMap(value.flatI, (fun (v : Leaf) -> AdaptiveLeaf.CreateAdaptiveCase(v)), (fun (m : AdaptiveLeafCase) (v : Leaf) -> m.Update(v)), (fun (m : AdaptiveLeafCase) -> m))
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : AnnotationGroupsImporterModel) = AdaptiveAnnotationGroupsImporterModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : AnnotationGroupsImporterModel) -> AdaptiveAnnotationGroupsImporterModel(value)) (fun (adaptive : AdaptiveAnnotationGroupsImporterModel) (value : AnnotationGroupsImporterModel) -> adaptive.Update(value))
    member __.Update(value : AnnotationGroupsImporterModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AnnotationGroupsImporterModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _rootGroupI_.Update(value.rootGroupI)
            _flatI_.Update(value.flatI)
    member __.Current = __adaptive
    member __.rootGroupI = _rootGroupI_ :> FSharp.Data.Adaptive.alist<AdaptiveNode>
    member __.flatI = _flatI_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveLeafCase>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AnnotationGroupsImporterModelLenses = 
    type AnnotationGroupsImporterModel with
        static member rootGroupI_ = ((fun (self : AnnotationGroupsImporterModel) -> self.rootGroupI), (fun (value : FSharp.Data.Adaptive.IndexList<Node>) (self : AnnotationGroupsImporterModel) -> { self with rootGroupI = value }))
        static member flatI_ = ((fun (self : AnnotationGroupsImporterModel) -> self.flatI), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Leaf>) (self : AnnotationGroupsImporterModel) -> { self with flatI = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSurfaceModel(value : SurfaceModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _surfaces_ = AdaptiveGroupsModel(value.surfaces)
    let _sgSurfaces_ =
        let inline __arg2 (m : PRo3D.Core.Surface.AdaptiveSgSurface) (v : PRo3D.Core.Surface.SgSurface) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.sgSurfaces, (fun (v : PRo3D.Core.Surface.SgSurface) -> PRo3D.Core.Surface.AdaptiveSgSurface(v)), __arg2, (fun (m : PRo3D.Core.Surface.AdaptiveSgSurface) -> m))
    let _sgGrouped_ =
        let inline __arg1 (v : FSharp.Data.Adaptive.HashMap<System.Guid, PRo3D.Core.Surface.SgSurface>) =
            let inline __arg2 (m : PRo3D.Core.Surface.AdaptiveSgSurface) (v : PRo3D.Core.Surface.SgSurface) =
                m.Update(v)
                m
            FSharp.Data.Traceable.ChangeableModelMap(v, (fun (v : PRo3D.Core.Surface.SgSurface) -> PRo3D.Core.Surface.AdaptiveSgSurface(v)), __arg2, (fun (m : PRo3D.Core.Surface.AdaptiveSgSurface) -> m))
        let inline __arg2 (m : FSharp.Data.Traceable.ChangeableModelMap<System.Guid, PRo3D.Core.Surface.SgSurface, PRo3D.Core.Surface.AdaptiveSgSurface, PRo3D.Core.Surface.AdaptiveSgSurface>) (v : FSharp.Data.Adaptive.HashMap<System.Guid, PRo3D.Core.Surface.SgSurface>) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelList(value.sgGrouped, __arg1, __arg2, (fun (m : FSharp.Data.Traceable.ChangeableModelMap<System.Guid, PRo3D.Core.Surface.SgSurface, PRo3D.Core.Surface.AdaptiveSgSurface, PRo3D.Core.Surface.AdaptiveSgSurface>) -> m :> FSharp.Data.Adaptive.amap<System.Guid, PRo3D.Core.Surface.AdaptiveSgSurface>))
    let _kdTreeCache_ = FSharp.Data.Adaptive.cmap(value.kdTreeCache)
    let _debugPreTrafo_ = FSharp.Data.Adaptive.cval(value.debugPreTrafo)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SurfaceModel) = AdaptiveSurfaceModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SurfaceModel) -> AdaptiveSurfaceModel(value)) (fun (adaptive : AdaptiveSurfaceModel) (value : SurfaceModel) -> adaptive.Update(value))
    member __.Update(value : SurfaceModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SurfaceModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _surfaces_.Update(value.surfaces)
            _sgSurfaces_.Update(value.sgSurfaces)
            _sgGrouped_.Update(value.sgGrouped)
            _kdTreeCache_.Value <- value.kdTreeCache
            _debugPreTrafo_.Value <- value.debugPreTrafo
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.surfaces = _surfaces_
    member __.sgSurfaces = _sgSurfaces_ :> FSharp.Data.Adaptive.amap<System.Guid, PRo3D.Core.Surface.AdaptiveSgSurface>
    member __.sgGrouped = _sgGrouped_ :> FSharp.Data.Adaptive.alist<FSharp.Data.Adaptive.amap<System.Guid, PRo3D.Core.Surface.AdaptiveSgSurface>>
    member __.kdTreeCache = _kdTreeCache_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, Aardvark.Geometry.ConcreteKdIntersectionTree>
    member __.debugPreTrafo = _debugPreTrafo_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SurfaceModelLenses = 
    type SurfaceModel with
        static member version_ = ((fun (self : SurfaceModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : SurfaceModel) -> { self with version = value }))
        static member surfaces_ = ((fun (self : SurfaceModel) -> self.surfaces), (fun (value : GroupsModel) (self : SurfaceModel) -> { self with surfaces = value }))
        static member sgSurfaces_ = ((fun (self : SurfaceModel) -> self.sgSurfaces), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, PRo3D.Core.Surface.SgSurface>) (self : SurfaceModel) -> { self with sgSurfaces = value }))
        static member sgGrouped_ = ((fun (self : SurfaceModel) -> self.sgGrouped), (fun (value : FSharp.Data.Adaptive.IndexList<FSharp.Data.Adaptive.HashMap<System.Guid, PRo3D.Core.Surface.SgSurface>>) (self : SurfaceModel) -> { self with sgGrouped = value }))
        static member kdTreeCache_ = ((fun (self : SurfaceModel) -> self.kdTreeCache), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Aardvark.Geometry.ConcreteKdIntersectionTree>) (self : SurfaceModel) -> { self with kdTreeCache = value }))
        static member debugPreTrafo_ = ((fun (self : SurfaceModel) -> self.debugPreTrafo), (fun (value : Microsoft.FSharp.Core.string) (self : SurfaceModel) -> { self with debugPreTrafo = value }))

