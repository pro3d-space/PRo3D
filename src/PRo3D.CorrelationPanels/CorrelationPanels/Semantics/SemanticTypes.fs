namespace CorrelationDrawing.SemanticTypes

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open UIPlus
open CorrelationDrawing.Types
open Chiron
open Aardvark.UI
open PRo3D.Base

#nowarn "0686"

open PRo3D.Base.Annotation

//type GeometryType = Point = 0  | Line = 1      | Polyline = 2     | Polygon = 3 | DnS = 4       | Undefined = 5
//type SemanticType = Metric = 0 | Angular = 1   | Hierarchical = 2 | Undefined = 3

type CorrelationSemanticId = CorrelationSemanticId of string

module CorrelationSemanticId = 

    let invalid = String.Empty |> CorrelationSemanticId
    let create name  = name |> CorrelationSemanticId
    let value semanticId = 
        let (CorrelationSemanticId name) = semanticId
        name

[<DomainType>]
type CorrelationSemantic = {
    version : int

    [<NonIncremental;PrimaryKey>]
    id                : CorrelationSemanticId
    
    [<NonIncremental>]
    timestamp         : string
    
    state             : State
    label             : TextInput
    color             : ColorInput
    thickness         : NumericInput
    semanticType      : SemanticType
    geometryType      : Geometry
    level             : NodeLevel
}
with 
    static member current = 0
    static member private readV0 =
        json {
            let! id           = Json.read "id"
            let! timestamp    = Json.read "timestamp"
           
            let! label        = Json.read "label"
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            let! semanticType = Json.read "semanticType"
            let! geometryType = Json.read "geometryType"                                    
            let! level        = Json.read "level"                                                   

            return { 
                version      = CorrelationSemantic.current
                id           = (id |> CorrelationSemanticId)
                timestamp    = timestamp
                state        = State.Display
                label        = label
                color        = color
                thickness    = thickness
                semanticType = semanticType |> enum<SemanticType>
                geometryType = geometryType |> enum<Geometry>
                level        = (NodeLevel level)
            }
        }
    static member FromJson(_:CorrelationSemantic) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! CorrelationSemantic.readV0
            | _ -> return! v |> sprintf "don't know version %d of MyType" |> Json.error
        }

    static member ToJson (x : CorrelationSemantic) =
        json {
            do! Json.write "version" x.version

            let (CorrelationSemanticId id) = x.id 
            do! Json.write "id" id

            do! Json.write "timestamp" x.timestamp
            do! Json.write "label" x.label
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>)   "color"     x.color
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "thickness" x.thickness
            do! Json.write "semanticType" (x.semanticType |> int)
            do! Json.write "geometryType" (x.geometryType |> int)

            let (NodeLevel l) = x.level
            do! Json.write "level" l            
        }    

type SemanticsSortingOption = 
    | Label        = 0 
    | Level        = 1 
    | GeometryType = 2 
    | SemanticType = 3 
    | SemanticId   = 4 
    | Timestamp    = 5

[<DomainType>]
type SemanticsModel = {
    version             : int
    semantics           : hmap<CorrelationSemanticId, CorrelationSemantic>
    semanticsList       : plist<CorrelationSemantic>
    selectedSemantic    : CorrelationSemanticId
    sortBy              : SemanticsSortingOption
    creatingNew         : bool
}
with 
    static member current = 0
    static member private readV0 =
        json {
            let! semantics = Json.read "semantics" 
            let! semanticsList = Json.read "semanticsList"            
            let! sortBy = Json.read "sortBy"
            
            let semantics = semantics |> List.map(fun (x:CorrelationSemantic) -> x.id, x)

            return { 
                version          = SemanticsModel.current    
                semantics        = semantics |> HMap.ofList
                semanticsList    = semanticsList |> PList.ofList
                selectedSemantic = CorrelationSemanticId.invalid
                sortBy           = sortBy |> enum<SemanticsSortingOption>
                creatingNew      = false
            }
        }
    static member FromJson(_:SemanticsModel) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! SemanticsModel.readV0
            | _ -> return! v |> sprintf "don't know version %d of MyType" |> Json.error
        }
    static member ToJson (x : SemanticsModel) =
        json {
            do! Json.write "version"        x.version
            do! Json.write "semantics"     (x.semantics |> HMap.values |> Seq.toList)
            do! Json.write "semanticsList" (x.semanticsList |> PList.toList)
            do! Json.write "sortBy"        (x.sortBy |> int)                     
        }

module SemanticsModel =    

    let initial : SemanticsModel = {
        version           = SemanticsModel.current
        semantics         = hmap.Empty
        selectedSemantic  = CorrelationSemanticId.invalid
        semanticsList     = plist.Empty
        sortBy            = SemanticsSortingOption.Level
        creatingNew       = false
    }    