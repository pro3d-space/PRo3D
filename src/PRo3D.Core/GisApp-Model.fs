namespace PRo3D.Core.Gis


open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base
open PRo3D.Core
open PRo3D.ImageMapping

open PRo3D.Core.Surface
open PRo3D.Base.Gis
open Chiron
open Aardvark.UI.Primitives

open Aardvark.Base


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
    | Reset

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
type MissionTimeEntry = {
    [<NonAdaptive>]
    minDate : DateTime
    [<NonAdaptive>]
    maxDate : DateTime
    [<NonAdaptive>]
    name    : string

    value : NumericInput
}


[<ModelType>]
type GisApp = 
    {
        version                : int
        defaultObservationInfo : ObservationInfo
        entities               : HashMap<EntitySpiceName, Entity>
        newEntity              : Option<Entity>
        newFrame               : Option<ReferenceFrame>
        referenceFrames        : HashMap<FrameSpiceName, ReferenceFrame>
        gisSurfaces            : HashMap<SurfaceId, GisSurface>
        spiceKernel            : Option<CooTransformation.SPICEKernel>
        spiceKernelLoadSuccess : bool
        cameraInObserver       : bool
        projectedImageList     : ProjectedImageListModel
        showMarkers            : bool // whether line + text markers are displayed (for known planets)

        selectedMissionTimeRow : Option<Index>
        missionTimesEntries    : Option<IndexList<MissionTimeEntry>>
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

            let! showMarkers = Json.tryRead "showMarkers"

            // Additive field (tryRead + default), so scenes from before it existed load
            // unchanged. Only the mode is persisted, not the whole image list -- the list
            // staying session-local is existing behaviour.
            let! (lightingMode : Option<int>) = Json.tryRead "lightingMode"
            let lightingMode =
                lightingMode |> Option.map enum<LightingMode> |> Option.defaultValue LightingMode.Off

            return {
                version                = GisApp.current
                defaultObservationInfo = defaultObservationInfo
                referenceFrames        = HashMap.ofList referenceFrames
                entities               = HashMap.ofList entities
                newEntity              = None
                newFrame               = None
                gisSurfaces            = HashMap.ofList gisSurfaces
                spiceKernel            = Option.map CooTransformation.SPICEKernel.ofPath spiceKernel
                cameraInObserver       = Option.defaultValue false cameraInObserver
                spiceKernelLoadSuccess = false
                projectedImageList        = { ProjectedImageListModel.initial with lightingMode = lightingMode }
                showMarkers            = Option.defaultValue false showMarkers

                selectedMissionTimeRow = None
                missionTimesEntries    = None
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
            do! Json.write "spiceKernel"             (Option.map CooTransformation.SPICEKernel.toPath x.spiceKernel)
            do! Json.write "showMarkers"             x.showMarkers
            // The sun/lighting mode must survive save/load: PRo3D.Snapshots.exe restores
            // the scene through this codec, so an unserialized mode would silently reset
            // to Off in every batch render.
            do! Json.write "lightingMode"            (int x.projectedImageList.lightingMode)
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
    | ToggleTrajectory
    | SetTextureName    of string
    | SetRadius         of float
    | SetTrajectoryLength of float
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
    | ProjectedImageListMessage of ProjectedImageListMessage
    | ToggleDrawMarkers
    | SetMissionTimesRowAndSetDate of (MissionTimeEntry * Index)
    | InitializeMissionTimeEntries
    | SetTime                   of (MissionTimeEntry * Index * float)
    | Empty

