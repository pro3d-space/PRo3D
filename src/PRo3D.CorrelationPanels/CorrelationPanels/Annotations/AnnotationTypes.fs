namespace CorrelationDrawing.AnnotationTypes

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open CorrelationDrawing.SemanticTypes
open PRo3D.Base.Annotation

type ContactId = ContactId of Guid

module ContactId = 
    let invalid = Guid.Empty |> ContactId
    let value id = 
        let (ContactId v) = id
        v

[<ModelType>]
type ContactPoint = {
    [<NonAdaptive>]
    point     : V3d
    selected  : bool
}

[<ModelType>]
type Contact = {     
    [<NonIncremental;PrimaryKey>]
    id                    : ContactId
    
    [<NonAdaptive>]
    geometry              : Geometry

    [<NonAdaptive>]
    projection            : Projection

    [<NonAdaptive>]
    semanticType          : SemanticType

    [<NonAdaptive>]
    elevation             : V3d -> float

    selected              : bool //TODO TO move to outer model id list
    hovered               : bool

    semanticId            : CorrelationSemanticId
    points                : IndexList<ContactPoint>    
    visible               : bool
    text                  : string
//    overrideStyle         : option<int>
    //overrideLevel         : option<int>
}

type ContactsTable  = HashMap<ContactId, Contact>