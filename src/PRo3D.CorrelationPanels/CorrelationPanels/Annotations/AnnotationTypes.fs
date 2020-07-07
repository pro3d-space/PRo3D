namespace CorrelationDrawing.AnnotationTypes

open System

open Aardvark.Base
open Aardvark.Base.Incremental
open CorrelationDrawing.SemanticTypes
open PRo3D.Base.Annotation

type ContactId = ContactId of Guid

module ContactId = 
    let invalid = Guid.Empty |> ContactId
    let value id = 
        let (ContactId v) = id
        v

[<DomainType>]
type ContactPoint = {
    [<NonIncremental>]
    point     : V3d
    selected  : bool
}

[<DomainType>]
type Contact = {     
    [<NonIncremental;PrimaryKey>]
    id                    : ContactId
    
    [<NonIncremental>]
    geometry              : Geometry

    [<NonIncremental>]
    projection            : Projection

    [<NonIncremental>]
    semanticType          : SemanticType

    [<NonIncremental>]
    elevation             : V3d -> float

    selected              : bool //TODO TO move to outer model id list
    hovered               : bool

    semanticId            : CorrelationSemanticId
    points                : plist<ContactPoint>    
    visible               : bool
    text                  : string
//    overrideStyle         : option<int>
    //overrideLevel         : option<int>
}

type ContactsTable  = hmap<ContactId, Contact>