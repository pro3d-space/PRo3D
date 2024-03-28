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
    referenceFrame : option<FrameSpiceName>
} with
    /// returns target, observer and referenceFrame if they are all Some
    member this.valuesIfComplete =
        match this.observer, this.target, this.referenceFrame with
        | Some o, Some t, Some r ->
            Some (t, o, r)
        | _ -> // TODO rno extend to provide nice log messages
            None
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
    | SetReferenceFrame of option<FrameSpiceName>

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
        newFrame               : option<ReferenceFrame>
        referenceFrames        : HashMap<FrameSpiceName, ReferenceFrame>
        gisSurfaces            : HashMap<SurfaceId, GisSurface>
        spiceKernel            : option<string>
        spiceKernelLoadSuccess : bool
        cameraInObserver       : bool
    } 
with
    static member current = 0

module GisAppJson =
    let read0 =
        json {
            let! defaultObservationInfo = Json.read "defaultObservationInfo"
            let! entities  = Json.read "entities"         
            let entities =
                entities 
                |> List.map (fun (x : Entity) -> x.spiceName, x)
            let! referenceFrames = Json.read "referenceFrames"       
            let referenceFrames =
                referenceFrames 
                |> List.map (fun (x : ReferenceFrame) -> x.spiceName, x)     
            let! gisSurfaces = Json.tryRead "gisSurfaces"     
            let gisSurfaces =
                match gisSurfaces with
                | Some gisSurfaces ->
                    gisSurfaces
                    |> List.map (fun (x : GisSurface) -> x.surfaceId, x)                     
                | None -> List.empty
            let! (spiceKernel : option<string>) = Json.tryRead "spiceKernel"

            let! cameraInObserver = Json.tryRead "cameraInObserver"
            
            return {
                version                = ReferenceFrame.current
                defaultObservationInfo = defaultObservationInfo
                referenceFrames        = HashMap.ofList referenceFrames               
                entities               = HashMap.ofList entities          
                newEntity              = None
                newFrame               = None
                gisSurfaces            = HashMap.ofList gisSurfaces
                spiceKernel            = spiceKernel
                cameraInObserver       = Option.defaultValue false cameraInObserver
                spiceKernelLoadSuccess = false
            }
        }
    
type GisApp with 
    static member ToJson (x : GisApp) =
        json {              
            do! Json.write "version"                 GisApp.current
            do! Json.write "defaultObservationInfo"  x.defaultObservationInfo               
            do! Json.write "referenceFrames"         (x.referenceFrames |> HashMap.toList |> List.map snd)           
            do! Json.write "entities"                (x.entities |> HashMap.toList |> List.map snd)   
            do! Json.write "gisSurfaces"             (x.gisSurfaces |> HashMap.toList |> List.map snd)
            do! Json.write "spiceKernel"             (x.spiceKernel)
        }
    static member FromJson (_ : GisApp) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! GisAppJson.read0
            | _ ->
                return! v 
                |> sprintf "don't know version %A  of Scene" 
                |> Json.error
        }

type EntityAction =
    | SetLabel          of string
    | SetSpiceName      of string
    | SetSpiceNameText  of string
    | ToggleDraw        
    | SetTextureName    of string
    | SetRadius         of float
    | SetGeometryPath   of string
    | SetReferenceFrame of option<FrameSpiceName>
    | Delete            of EntitySpiceName
    | Edit              of EntitySpiceName
    | Cancel            of EntitySpiceName
    | Save              of EntitySpiceName
    | Close             of EntitySpiceName
    | FlyTo             of EntitySpiceName

type ReferenceFrameAction = 
    | SetLabel          of string
    | SetSpiceName      of string
    | SetSpiceNameText  of string
    | SetEntity         of option<EntitySpiceName>
    | Delete            of FrameSpiceName
    | Cancel
    | Save      

type GisAppAction =
    | Observe
    | AssignBody                of (SurfaceId * option<EntitySpiceName>)
    | AssignReferenceFrame      of (SurfaceId * option<FrameSpiceName>) 
    | SurfacesMessage           of SurfaceAppAction
    | ObservationInfoMessage    of ObservationInfoAction
    | BookmarkObservationInfoMessage of (BookmarkId * ObservationInfoAction)
    | EntityMessage             of (EntitySpiceName * EntityAction)
    | FrameMessage              of (FrameSpiceName * ReferenceFrameAction)
    | SetSpiceKernel            of string
    | ToggleCameraInObserver    
    | NewEntity
    | NewFrame


    