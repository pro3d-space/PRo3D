namespace Svgplus.Correlations2

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Svgplus
open Svgplus.RectangleType
open UIPlus
open Svgplus.DiagramItemType
open Chiron

type CorrelationId = CorrelationId of Guid

module CorrelationId =
    let create() =
        Guid.NewGuid() |> CorrelationId
    let getValue (CorrelationId a) =
        a
        
type CorrelationsAction =
    | Create  of hmap<RectangleBorderId, BorderContactId>
    | Edit    of (CorrelationId * hmap<RectangleBorderId, BorderContactId>)
    | Delete  of CorrelationId
    | Select  of CorrelationId
    | FlattenHorizon of CorrelationId 
    | DefaultHorizon
    //| SetName of string

[<DomainType>]
type Correlation =  {
    [<NonIncremental>]
    version  : int
    [<NonIncremental>]
    id       : CorrelationId
    contacts : hmap<RectangleBorderId, BorderContactId>
}
with
    static member current = 0
    static member private readV0 : Json<Correlation>=
        json {
            let! id       = Json.read "id"
            let! contacts = Json.read "contacts"            

            let contacts =
                contacts 
                |> List.map(fun (a,b) ->
                    (RectangleBorderId a), (BorderContactId b)
                )
                |> HMap.ofList

            return {
                version  = Correlation.current
                id       = id |> CorrelationId
                contacts = contacts
            }        
        }
    static member FromJson(_:Correlation) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Correlation.readV0
            | _ -> return! v |> sprintf "don't know version %d of Correlation" |> Json.error
        }
    static member ToJson (x : Correlation) =
        let contacts =
            x.contacts 
            |> HMap.toList 
            |> List.map(fun (a,b) -> 
                a |> RectangleBorderId.getValue, b |> BorderContactId.getValue
            )

        json {
            do! Json.write "version"   x.version
            do! Json.write "id"       (x.id |> CorrelationId.getValue)
            do! Json.write "contacts"  contacts
        }


[<DomainType>]
type CorrelationsModel =  {
    version             : int

    selectedCorrelation : option<CorrelationId>
    correlations        : hmap<CorrelationId, Correlation>
    alignedBy           : option<CorrelationId>
}
with
    static member init =
        {
            version      = CorrelationsModel.current
            correlations = HMap.empty
            selectedCorrelation = None
            alignedBy = None
        }
    static member current = 0
    static member private readV0 : Json<CorrelationsModel>=
        json {            
            let! correlations = Json.read "correlations"

            return {
                version             = CorrelationsModel.current
                correlations        = correlations |> List.map(fun x -> x.id, x) |> HMap.ofList
                selectedCorrelation = None
                alignedBy           = None
            }        
        }
    static member FromJson(_:CorrelationsModel) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! CorrelationsModel.readV0
            | _ -> return! v |> sprintf "don't know version %d of CorrelationsModel" |> Json.error
        }
    static member ToJson (x : CorrelationsModel) =
        json {
             do! Json.write "version"        x.version
             do! Json.write "correlations"   (x.correlations |> HMap.toList |> List.map snd)
        }