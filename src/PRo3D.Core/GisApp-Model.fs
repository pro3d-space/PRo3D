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
open Aether

[<ModelType>]
type GisBookmark =
    {
        bookmarkId      : BookmarkId
        target          : Body
        oberserver      : Body
        observationTime : DateTime
        referenceFrame  : ReferenceFrame
    }

type GisSurface = {
    surfaceId       : SurfaceId
    body            : option<BodySpiceName>
    referenceFrame  : option<FrameSpiceName>
}

type Entity =
    | EntitySurface     of GisSurface
    | EntitySpaccecraft of Spacecraft

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
        defaultObservationInfo : ObservationInfo

        bodies          : HashMap<BodySpiceName, Body>
        referenceFrames : HashMap<FrameSpiceName, ReferenceFrame>
        spacecraft      : HashMap<SpacecraftId, Spacecraft>
        entities        : HashMap<Guid, Entity>
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


    