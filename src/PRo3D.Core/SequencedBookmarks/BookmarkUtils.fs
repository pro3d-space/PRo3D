namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Animation
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open Aardvark.Rendering
open Aardvark.UI.Anewmation

open System
open System.IO
open PRo3D.Base
open PRo3D.Core.SequencedBookmarks

open Aether
open Aether.Operators

module BookmarkUtils =

    let getNewSBookmark (navigation : NavigationModel) 
                        (sceneState : SceneState)
                        (bookmarkCount:int) =
         
        let name = sprintf "Bookmark_%d" bookmarkCount //todo to make useful unique names
        let bookmark = 
            {
                version        = Bookmark.current
                key            = System.Guid.NewGuid()
                name           = name
                cameraView     = navigation.camera.view 
                navigationMode = navigation.navigationMode
                exploreCenter  = navigation.exploreCenter
            }
        let sequencedBookmark =
            {
                version    = SequencedBookmark.current
                bookmark   = bookmark
                sceneState = Some sceneState
                duration   = SequencedBookmark.initDuration 3.0
                delay      = SequencedBookmark.initDelay 0.0
            }
        sequencedBookmark

    let insertGuid (id: Guid) (index : int) (orderList: List<Guid>) =

        let rec insert v i l =
            match i, l with
            | 0, xs -> v::xs
            | i, x::xs -> x::insert v (i - 1) xs
            | i, [] -> failwith "index out of range"
        insert id index orderList

    let removeGuid (index : int) (orderList: List<Guid>) =

        let rec remove i l =
            match i, l with
            | 0, x::xs -> xs
            | i, x::xs -> x::remove (i - 1) xs
            | i, [] -> failwith "index out of range"
        remove index orderList

    let selectSBookmark (m : SequencedBookmarks) (id : Guid) =
        let sbm = m.bookmarks |> HashMap.tryFind id
        match sbm, m.selectedBookmark with
        | Some a, Some b ->
            if a.key = b then 
                { m with selectedBookmark = None }
            else 
                { m with selectedBookmark = Some a.key }
        | Some a, None -> 
            { m with selectedBookmark = Some a.key }
        | None, _ -> m

    let orderedBookmarks  (m : SequencedBookmarks) =
        m.orderList
            |> List.map (fun x -> HashMap.find x m.bookmarks)

    let moveCursor (f : int -> int) (m : SequencedBookmarks) =
        match m.selectedBookmark with
        | Some key -> 
            let index = List.findIndex (fun x -> x = key) m.orderList
            let nextIndex =
                let x = (f index)
                let x = x % (List.length m.orderList)
                if x < 0 then
                    List.length m.orderList + x
                else x
            HashMap.tryFind m.orderList.[nextIndex] m.bookmarks
        | None -> None

    let next (m : SequencedBookmarks) =
        moveCursor (fun x -> x + 1) m

    let previous (m : SequencedBookmarks) =
        moveCursor (fun x -> x - 1) m

    let selected (m : SequencedBookmarks) =
        match m.selectedBookmark with
        | Some sel ->
            HashMap.tryFind sel m.bookmarks
        | None -> None

    let find (id : Guid) (m : SequencedBookmarks) =
        HashMap.tryFind id m.bookmarks

