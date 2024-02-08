namespace PRo3D.Core.Gis


open System
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base
open PRo3D.Core

open PRo3D.Core.Surface
open GisModels
open Aether

[<ModelType>]
type GisBookmark =
    {
        bookmarkId      : BookmarkId
        target          : Body
        oberserver      : Body
        observationTime : DateTime
        referenceFrame  : GisModels.ReferenceFrame
    }

type GisSurface = {
    surfaceId       : SurfaceId
    body            : option<BodySpiceName>
    referenceFrame  : option<FrameSpiceName>
}

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

type Entity =
    | EntitySurface     of GisSurface
    | EntitySpaccecraft of Spacecraft
    | EntityBookmark    of GisBookmark 

type GisAppLenses<'viewer> = 
    {
        surfacesModel   : Lens<'viewer, SurfaceModel>
        bookmarks       : Lens<'viewer, SequencedBookmarks.SequencedBookmarks>
        scenePath       : Lens<'viewer, option<string>> 
        navigation      : Lens<'viewer, NavigationModel>
        referenceSystem : Lens<'viewer, ReferenceSystem>
    }

[<ModelType>]
type GisApp = 
    {
        bodies          : HashMap<BodySpiceName, Body>
        referenceFrames : HashMap<FrameSpiceName, ReferenceFrame>
        spacecraft      : HashMap<SpacecraftSpiceName, Spacecraft>
        entities        : HashMap<Guid, Entity>
    }

type GisAppAction =
    | Observe
    | AssignBody of (SurfaceId * option<BodySpiceName>)
    | AssignReferenceFrame of (SurfaceId * option<FrameSpiceName>) 
    | SurfacesMessage of SurfaceAppAction


    