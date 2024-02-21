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
    entity          : option<EntitySpiceName>
    referenceFrame  : option<FrameSpiceName>
} with
    static member FromJson(_ : GisSurface) = 
        json {
            let! surfaceId          = Json.read    "surfaceId"        
            let! entity             = Json.tryRead "entity"             
            let! referenceFrame     = Json.tryRead "referenceFrame"
         
            return {
                surfaceId      = surfaceId     
                entity         = entity          
                referenceFrame = referenceFrame                   
            }
        }
    static member ToJson (x : GisSurface) =
        json {              
            do! Json.write      "surfaceId"           x.surfaceId     
            do! Json.write      "entity"              x.entity         
            do! Json.write      "referenceFrame"      x.referenceFrame
        }


[<ModelType>]
type ObservationInfo = {
    target         : option<EntitySpiceName>
    observer       : option<EntitySpiceName>
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
    | SetTarget         of option<EntitySpiceName>
    | SetObserver       of option<EntitySpiceName>
    | SetTime           of DateTime
    | SetReferenceFrame of option<ReferenceFrame>

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module GisSurface =
    let fromBody surfaceId entity =
        {
            surfaceId = surfaceId
            entity    = entity
            referenceFrame = None
        }
    let fromFrame surfaceId frame =
        {
            surfaceId = surfaceId
            entity    = None
            referenceFrame = frame
        }

[<ModelType>]
type GisApp = 
    {
        version                : int
        defaultObservationInfo : ObservationInfo
        entities               : HashMap<EntitySpiceName, Entity>
        newEntity              : option<Entity>
        referenceFrames        : HashMap<FrameSpiceName, ReferenceFrame>
        gisSurfaces            : HashMap<SurfaceId, GisSurface>
    } 
with
    static member current = 0

module GisAppJson =
    let read0 =
        json {
            let! defaultObservationInfo = Json.read "defaultObservationInfo"
            let! entities  = Json.read "entities"         
            let entities =
                entities |> List.map (fun (x : Entity) -> x.spiceName, x)
            let! referenceFrames        = Json.read "referenceFrames"       
            let referenceFrames =
                referenceFrames |> List.map (fun (x : ReferenceFrame) -> x.spiceName, x)     
            let! gisSurfaces             = Json.read "gisSurfaces"     
            let gisSurfaces =
                gisSurfaces |> List.map (fun (x : GisSurface) -> x.surfaceId, x)                     
            
            return {
                version                = ReferenceFrame.current
                defaultObservationInfo = defaultObservationInfo
                referenceFrames        = HashMap.ofList referenceFrames               
                entities               = HashMap.ofList entities          
                newEntity              = None
                gisSurfaces            = HashMap.ofList gisSurfaces
            }
        }
    
type GisApp with 
    static member ToJson (x : GisApp) =
        json {              
            do! Json.write "version"                 GisApp.current
            do! Json.write "defaultObservationInfo"  x.defaultObservationInfo               
            do! Json.write "referenceFrames"         (x.referenceFrames |> HashMap.toList |> List.map snd)           
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

type EntityAction =
    | SetLabel          of string
    | SetSpiceName      of string
    | SetSpiceNameText  of string
    | SetReferenceFrame of option<FrameSpiceName>
    | Delete            of EntitySpiceName
    | Cancel
    | Save              

type GisAppAction =
    | Observe
    | AssignBody                of (SurfaceId * option<EntitySpiceName>)
    | AssignReferenceFrame      of (SurfaceId * option<FrameSpiceName>) 
    | SurfacesMessage           of SurfaceAppAction
    | ObservationInfoMessage    of ObservationInfoAction
    | EntityMessage             of (EntitySpiceName * EntityAction)
    | NewEntity


    