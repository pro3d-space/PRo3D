namespace PRo3D.Bookmarkings

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI.Primitives
open PRo3D
open PRo3D.Base
open PRo3D.ReferenceSystem
open Chiron

#nowarn "0686"

[<DomainType>]
type Bookmark = {
    version         : int
    key             : Guid
    name            : string
    cameraView      : CameraView
    exploreCenter   : V3d
    navigationMode  : NavigationMode
}

module Bookmark =

    let current = 0   

    let read0 = 
        json {
            let! key            = Json.read "key"
            let! name           = Json.read "name"
            let! cameraView     = Json.readWith Ext.fromJson<CameraView,Ext> "cameraView"
            let! exploreCenter  = Json.read "exploreCenter"
            let! navigationMode = Json.read "navigationMode"
            
            return
                {
                    version        = current
                    key            = key
                    name           = name
                    cameraView     = cameraView
                    exploreCenter  = exploreCenter |> V3d.Parse
                    navigationMode = navigationMode |> enum<NavigationMode>
                }
        }

type Bookmark with 
    static member FromJson( _ : Bookmark) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Bookmark.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of Bookmark" 
                |> Json.error
        }

    static member ToJson(x : Bookmark) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "key"        x.key
            do! Json.write "name"       x.name
            do! Json.writeWith Ext.toJson<CameraView,Ext> "cameraView" x.cameraView
            do! Json.write "exploreCenter"  (x.exploreCenter.ToString())
            do! Json.write "navigationMode" (x.navigationMode |> int)
        }
