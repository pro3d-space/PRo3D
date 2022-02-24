namespace PRo3D.Provenance.Abstraction

open System

open Aardvark.Base
open Aardvark.UI

open FSharp.Data.Adaptive

open Adaptify

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

type OSegment = Segment

[<ModelType>]
type Segment = {
    startPoint  : V3d
    endPoint    : V3d
    points      : V3d rlist 
}

type OAnnotation = Annotation

[<ModelType; CustomEquality; NoComparison>]
type Annotation =
    {
        version     : int

        [<NonAdaptive>]
        key         : Guid

        modelTrafo  : Trafo3d

        geometry    : Geometry
        projection  : Projection
        semantic    : Semantic

        points      : V3d rlist
        segments    : Segment rlist 
        color       : ColorInput
        thickness   : NumericInput
        results     : AnnotationResults option
        dnsResults  : DipAndStrikeResults option

        visible     : bool
        showDns     : bool
        text        : string
        textsize    : NumericInput

        surfaceName : string
        view        : CameraView

        semanticId   : SemanticId
        semanticType : SemanticType
    }

    override x.Equals (y : obj) =
        let cmp a b =
            a.version      = b.version     &&
            a.key          = b.key         &&
            a.modelTrafo   = b.modelTrafo  &&
            a.geometry     = b.geometry    &&
            a.projection   = b.projection  &&
            a.points       = b.points      &&
            a.segments     = b.segments    &&
            a.color        = b.color       &&
            a.thickness    = b.thickness   &&
            a.visible      = b.visible     &&
            a.showDns      = b.showDns     &&
            a.text         = b.text        &&
            a.textsize     = b.textsize    &&
            a.surfaceName  = b.surfaceName &&
            a.view         = b.view

        match y with
        | :? Annotation as y -> cmp x y
        | _ -> false

    override x.GetHashCode() =
        hash (x.version,
              x.key,
              x.modelTrafo,
              x.geometry,
              x.projection,
              x.points,
              x.segments,
              x.color,
              x.thickness,
              x.visible,
              x.showDns,
              x.text,
              x.textsize,
              x.surfaceName,
              x.view)

type OAnnotationGroup = Node

[<ModelType>]
type AnnotationGroupState = {
    key         : Guid
    name        : string
    leaves      : Guid rlist
    subNodes    : AnnotationGroup rlist
    visible     : bool
}

and [<ModelType; CustomEquality; NoComparison>] AnnotationGroup = 
    { state     : AnnotationGroupState
      expanded  : bool }

    override x.GetHashCode () =
        hash x.state

    override x.Equals y =
        match y with 
        | :? AnnotationGroup as y -> x.state = y.state
        | _ -> false

type OAnnotations = GroupsModel

[<ModelType>]
type Annotations = {
    flat : HashMap<Guid, Annotation>
    root : AnnotationGroup
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Segment =

    let create (s : OSegment) = { 
        startPoint = s.startPoint
        endPoint = s.endPoint
        points = { inner = s.points }
    }

    let restore (s : Segment) : OSegment = {
        startPoint = s.startPoint
        endPoint = s.endPoint
        points = s.points.inner
    }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Annotation =

    let create (a : OAnnotation) : Annotation = {
        version      = a.version        
        key          = a.key
        modelTrafo   = a.modelTrafo
        geometry     = a.geometry
        projection   = a.projection
        semantic     = a.semantic
        points       = { inner = a.points }
        segments     = { inner = a.segments |> IndexList.map Segment.create }
        color        = a.color
        thickness    = a.thickness
        results      = a.results
        dnsResults   = a.dnsResults
        visible      = a.visible
        showDns      = a.showDns
        text         = a.text
        textsize     = a.textsize
        surfaceName  = a.surfaceName
        view         = a.view |> CameraView.create
        semanticId   = a.semanticId
        semanticType = a.semanticType
    }

    let restore (a : Annotation) : OAnnotation = {
        version          = a.version
        key              = a.key
        modelTrafo       = a.modelTrafo
        geometry         = a.geometry
        projection       = a.projection
        semantic         = a.semantic
        points           = a.points.inner
        segments         = a.segments.inner |> IndexList.map Segment.restore
        color            = a.color
        thickness        = a.thickness
        results          = a.results
        dnsResults       = a.dnsResults
        visible          = a.visible
        showDns          = a.showDns
        text             = a.text
        textsize         = a.textsize
        surfaceName      = a.surfaceName
        view             = a.view |> CameraView.restore
        semanticId       = a.semanticId
        semanticType     = a.semanticType
        bookmarkId       = None
        manualDipAngle   = Annotation.initialManualDipAngle
        manualDipAzimuth = Annotation.initialmanualDipAzimuth
        showText         = false
    }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AnnotationGroup =

    let rec create (a : OAnnotations) (g : OAnnotationGroup) =
        let s = {
            key         = g.key
            name        = g.name
            leaves      = { inner = g.leaves }
            subNodes    = { inner = g.subNodes |> IndexList.map (create a) }
            visible     = g.visible
        }
        
        {
            state = s
            expanded = g.expanded
        }

    let rec restore (g : AnnotationGroup) : OAnnotationGroup = {
        version     = OAnnotationGroup.current
        key         = g.state.key
        name        = g.state.name
        leaves      = g.state.leaves.inner
        subNodes    = g.state.subNodes.inner |> IndexList.map restore
        visible     = g.state.visible
        expanded    = g.expanded
    }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Annotations =

    let create (a : OAnnotations) =
        let getAnnotation = 
            function
            | Annotations a -> Annotation.create a
            | _ -> raise (ArgumentException ("Expected an annotation"))
    
        {
            flat = a.flat |> HashMap.map (fun _ l -> getAnnotation l)
            root = a.rootGroup |> AnnotationGroup.create a
        }
        
    let restore (current : OAnnotations) (a : Annotations) : OAnnotations =

        let checkSelection (contains : Guid -> 'a -> bool) (collection : 'a) (def : TreeSelection) (s : TreeSelection) =
            if contains s.id collection then s else def
            
        let groups =
            let rec get (g : AnnotationGroup) : HashSet<Guid> =
                g.state.subNodes.inner
                    |> IndexList.map get
                    |> IndexList.toList
                    |> fun xs -> HashSet.ofList [g.state.key] :: xs
                    |> HashSet.unionMany
        
            get a.root

        {
            current with 
                rootGroup        = AnnotationGroup.restore a.root
                flat             = a.flat |> HashMap.map (fun _ a -> a |> Annotation.restore |> Annotations)
                singleSelectLeaf = current.singleSelectLeaf |> Option.filter (fun x -> a.flat |> HashMap.containsKey x)

                // TODO: The following checks are not really sufficient, since we would
                // also have to check the validity of the path; these checks are better implemented in the
                // groups app anyway, so I won't bother too much with it here.
                selectedLeaves   = current.selectedLeaves |> HashSet.filter (fun x -> a.flat |> HashMap.containsKey x.id)
                activeChild      = current.activeChild |> (checkSelection HashMap.containsKey a.flat Group.initChildSelection)
                activeGroup      = current.activeGroup |> (checkSelection HashSet.contains groups Group.initGroupSelection)
        }

    // Computes the difference (i.e. the change set) between two given
    // annotations records; this does only consider the annotations themselves, the
    // group structure is ignored
    let difference (a : Annotations) (b : Annotations) =
        let cmp x y = Option.map2 (=) x y |> Option.defaultValue false

        HashMap.map2 (fun _ x y -> cmp x y) a.flat b.flat
        |> HashMap.filter (fun _ v -> not v)
        |> HashMap.keys 
        |> HashSet.toList