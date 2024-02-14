namespace PRo3D.Core.Gis


open System
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base
open PRo3D.Core

open PRo3D.Core.Surface
open PRo3D.Base.Gis
open Chiron

type GisSurface = {
    surfaceId       : SurfaceId
    body            : option<BodySpiceName>
    referenceFrame  : option<FrameSpiceName>
} with
    static member FromJson(_ : GisSurface) = 
        json {
            let! surfaceId          = Json.read    "surfaceId"        
            let! body               = Json.tryRead "body"             
            let! referenceFrame     = Json.tryRead "referenceFrame"
         
            return {
                surfaceId      = surfaceId     
                body           = body          
                referenceFrame = referenceFrame                   
            }
        }
    static member ToJson (x : GisSurface) =
        json {              
            do! Json.write      "surfaceId"           x.surfaceId     
            do! Json.write      "body"                x.body              
            do! Json.write      "referenceFrame"      x.referenceFrame
        }

type Entity =
    | EntitySurface     of GisSurface
    | EntitySpaccecraft of Spacecraft
 with
    static member ToJson x =
        match x with
        | Entity.EntitySurface x -> 
            Json.write "EntitySurface" x
        | Entity.EntitySpaccecraft x -> 
            Json.write "EntitySpaccecraft" x

    static member FromJson(_ : Entity) = 
        json { 
            let! entitySurface = Json.tryRead "EntitySurface"
            match entitySurface with
            | Some entitySurface -> 
                return Entity.EntitySurface entitySurface
            | None ->
                let! entitySpaccecraft = Json.read "EntitySpaccecraft"

                return Entity.EntitySpaccecraft entitySpaccecraft
        }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Entity =
    let spiceName (entity : Entity) =
        match entity with
        | EntitySurface surface ->
            match surface.body with
            | Some body ->
                Some body.Value
            | None ->
                Log.line "[GisApp-Model] Surface has no associated body!"
                None
        | EntitySpaccecraft spacecraft ->
            Some spacecraft.spiceName.Value

[<ModelType>]
type ObservationInfo = {
    target         : option<Entity>
    observer       : option<Entity>
    time           : Calendar
    referenceFrame : option<ReferenceFrame>
} with
    static member FromJson(_ : ObservationInfo) = 
        json {
            let! target         = Json.read    "target"        
            let! observer       = Json.read    "observer"      
            let! time           = Json.read    "time"     
            let success, time =
                DateTime.TryParse time
            let time = 
                if success then
                    Calendar.fromDate time
                else
                    Calendar.fromDate DateTime.Now
            let! referenceFrame = Json.tryRead "referenceFrame"
            
            return {
                target         = target        
                observer       = observer      
                time           = time          
                referenceFrame = referenceFrame
            }
        }
    static member ToJson (x : ObservationInfo) =
        json {              
            do! Json.write      "target"          x.target
            do! Json.write      "observer"        x.observer      
            do! Json.write      "time"            x.time.date
            do! Json.write      "referenceFrame"  x.referenceFrame  
        }

type ObservationInfoAction = 
    | CalendarMessage   of Calendar.CalendarAction
    | SetTarget         of option<Entity>
    | SetObserver       of option<Entity>
    | SetTime           of DateTime
    | SetReferenceFrame of option<ReferenceFrame>

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module GisSurface =
    let fromBody surfaceId body =
        {
            surfaceId = surfaceId
            body      = body
            referenceFrame = None
        }
    let fromFrame surfaceId frame =
        {
            surfaceId = surfaceId
            body      = None
            referenceFrame = frame
        }

[<ModelType>]
type GisApp = 
    {
        version                : int
        defaultObservationInfo : ObservationInfo
        bodies                 : HashMap<BodySpiceName, Body>
        referenceFrames        : HashMap<FrameSpiceName, ReferenceFrame>
        spacecraft             : HashMap<SpacecraftId, Spacecraft>
        entities               : HashMap<Guid, Entity>
    } 
with
    static member current = 0

module GisAppJson =
    let read0 =
        json {
            let! defaultObservationInfo = Json.read "defaultObservationInfo"
            let! bodies                 = Json.read "bodies"            
            let bodies =
                bodies |> List.map (fun (x : Body) -> x.spiceName, x)
            let! referenceFrames        = Json.read "referenceFrames"       
            let referenceFrames =
                referenceFrames |> List.map (fun (x : ReferenceFrame) -> x.spiceName, x)
            let! spacecraft             = Json.read "spacecraft"     
            let spacecraft =
                spacecraft |> List.map (fun (x : Spacecraft) -> x.id, x)            
            let! entities               = Json.read "entities"              
            
            return {
                version                = ReferenceFrame.current
                defaultObservationInfo = defaultObservationInfo
                bodies                 = HashMap.ofList bodies 
                referenceFrames        = HashMap.ofList referenceFrames        
                spacecraft             = HashMap.ofList spacecraft            
                entities               = HashMap.ofList entities              
            }
        }
    
type GisApp with 
    static member ToJson (x : GisApp) =
        json {              
            do! Json.write "version"                 GisApp.current
            do! Json.write "defaultObservationInfo"  x.defaultObservationInfo
            do! Json.write "bodies"                  (x.bodies |> HashMap.toList |> List.map snd)                    
            do! Json.write "referenceFrames"         (x.referenceFrames |> HashMap.toList |> List.map snd)           
            do! Json.write "spacecraft"              (x.spacecraft |> HashMap.toList |> List.map snd)                
            do! Json.write "entities"                (x.entities |> HashMap.toList |> List.map snd)                  
        }
    static member FromJson (_ : GisApp) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! "TODO!" |> Json.error
            | _ ->
                return! v 
                |> sprintf "don't know version %A  of Scene" 
                |> Json.error
        }

type SpacecraftAction =
    | SetLabel of string
    | SetSpiceName of string
    | SetReferenceFrame of option<FrameSpiceName>
    | Delete of SpacecraftId

type GisAppAction =
    | Observe
    | AssignBody of (SurfaceId * option<BodySpiceName>)
    | AssignReferenceFrame of (SurfaceId * option<FrameSpiceName>) 
    | SurfacesMessage of SurfaceAppAction
    | ObservationInfoMessage of ObservationInfoAction
    | SpacecraftMessage of (SpacecraftId * SpacecraftAction)
    | NewSpacecraft


    