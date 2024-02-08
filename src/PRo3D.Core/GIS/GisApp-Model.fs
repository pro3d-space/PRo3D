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

type Entity =
    | EntitySurface     of (SurfaceId * SpiceName)
    | EntitySpaccecraft of Spacecraft
    | EntityBookmark    of BookmarkId 

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
        bodies          : HashMap<SpiceName, Body>
        referenceFrames : HashMap<SpiceName, ReferenceFrame>
        spacecraft      : HashMap<SpiceName, Spacecraft>
        entities        : HashMap<Guid, Entity>
    }

type GisAppAction =
    | Observe
    | AssignBody of SpiceName
    | AssignReferenceFrame of SpiceName
    | SurfacesMessage of SurfaceAppAction


    