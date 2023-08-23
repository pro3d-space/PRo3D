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
open Chiron
open Aardvark.UI.Anewmation
open Aether
open Aether.Operators

module BookmarkUtils =
    let basePathFromScenePath (scenePath : string) =
        Path.combine [Path.GetDirectoryName scenePath; 
                        Path.GetFileNameWithoutExtension scenePath]

    /// Links the animation to a field in the model by registering
    /// a callback that uses the given lens to modify its value as the animation progresses.
    let linkPrism (lens : Prism<'Model, 'Value>) (animation : IAnimation<'Model, 'Value>) =
        animation |> AnimationCallbackExtensions.Animation.onProgress (fun _ -> Optic.set lens)


    /// tries to find a bookmark and also tries to
    /// load it if it is not loaded
    let tryFind (id : Guid) (m : SequencedBookmarks) =
        HashMap.tryFind id m.bookmarks
        |> Option.bind SequencedBookmark.tryLoad

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
                version             = SequencedBookmarkModel.current
                bookmark            = bookmark
                metadata            = None
                sceneState          = Some sceneState
                frustumParameters   = None
                poseDataPath        = None
                duration            = SequencedBookmarkDefaults.initDuration 3.0
                delay               = SequencedBookmarkDefaults.initDelay 0.0
                basePath            = None
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
        let sbm = tryFind id m
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

    let orderedLoadedBookmarks  (m : SequencedBookmarks) =
        m.orderList
        |> List.map (fun x -> 
                        let b = HashMap.find x m.bookmarks
                        let b = SequencedBookmark.tryLoad b
                        b
                    )
        |> List.filter (fun x -> Option.isSome x)
        |> List.map (fun x -> x.Value)

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
            tryFind m.orderList.[nextIndex] m
        | None -> None

    let updateOne (m : SequencedBookmarks) (key : Guid) 
                  (f : SequencedBookmark -> SequencedBookmark) =
        let bookmarks = 
            HashMap.alter 
                key (fun x -> 
                    match x with
                    | Some x -> Some (f x )
                    | None -> None) m.bookmarks
        {m with bookmarks = bookmarks}

    /// loads the bookmark from the filesystem if it is not loaded already,
    /// updates the loaded bookmark and returns it
    let loadAndUpdate (m : SequencedBookmarks) (key : Guid) 
                      (f : SequencedBookmarkModel -> SequencedBookmarkModel) =
        let updState original =
            match original with
            | SequencedBookmark.LoadedBookmark bm -> 
                f bm |> SequencedBookmark.LoadedBookmark
            | SequencedBookmark.NotYetLoaded notLoadedBm ->
                let bm = SequencedBookmark.tryLoad original
                match bm with
                | Some bm -> 
                    f bm |> SequencedBookmark.LoadedBookmark
                | None ->
                    original
        let m = updateOne m key updState        
        m

    /// tries to load all bookmarks from the filesystem, 
    /// if a bookmark cannot be loaded an error is printed to log,
    /// and the bookmark is returned as SequencedBookmark.NotYetLoaded
    let loadAll (m : SequencedBookmarks) =
        let bookmarks = 
            m.bookmarks
            |> HashMap.map (fun guid bm -> SequencedBookmark.tryLoad' bm)
        {m with bookmarks = bookmarks}

    /// after unloading, all bookmarks are if the type
    /// SequencedBookmark.NotYetLoaded (UnloadedSequencedBookmark)
    let unloadBookmarks (m : SequencedBookmarks) =
        let bookmarks = 
            m.bookmarks
            |> HashMap.map (fun g bm ->SequencedBookmark.unload bm)
        {m with bookmarks = bookmarks}

    /// updates the path of the bookmark with the given basePath
    /// loaded AND unloaded bookmarks are updated
    let updatePath (basePath : string) (bm : SequencedBookmark) =
        match bm with
        | SequencedBookmark.LoadedBookmark loaded ->
            Log.line "Updating sequenced bookmark base path from %s to %s"
                (string loaded.basePath) basePath
            SequencedBookmark.LoadedBookmark {loaded with basePath = Some basePath}
        | SequencedBookmarks.NotYetLoaded notLoaded ->
            let newPath = Path.combine [basePath;Path.GetFileName notLoaded.path]
            if String.equals notLoaded.path newPath then
                SequencedBookmarks.NotYetLoaded notLoaded
            else
                Log.line "Updating sequenced bookmark path from %s to %s" notLoaded.path newPath
                SequencedBookmarks.NotYetLoaded {notLoaded with path = newPath}

    /// updates the path of each bookmark with the given basePath
    /// loaded AND unloaded bookmarks are updated
    let updatePaths (basePath : string) (m : SequencedBookmarks) =
        let bookmarks = 
            HashMap.map (fun g bm -> updatePath basePath bm) m.bookmarks
        {m with bookmarks = bookmarks}

    /// updates the paths of all bookmarks and
    /// saves bookmarks to file system
    let saveSequencedBookmarks (basePath:string) 
                               (bookmarks : SequencedBookmarks) =      
        let bookmarks = updatePaths basePath bookmarks
        if not (Directory.Exists basePath) then
            Directory.CreateDirectory basePath |> ignore

        for guid, bm in bookmarks.bookmarks do
            match bm with
            | SequencedBookmarks.SequencedBookmark.LoadedBookmark bm ->
                bm
                |> Json.serialize
                |> Json.formatWith JsonFormattingOptions.SingleLine
                |> Serialization.Chiron.writeToFile bm.path
            | SequencedBookmarks.SequencedBookmark.NotYetLoaded bm ->
                () // no need to write bookmark if not loaded (no changes)
        bookmarks

    let next (m : SequencedBookmarks) =
        moveCursor (fun x -> x + 1) m

    let previous (m : SequencedBookmarks) =
        moveCursor (fun x -> x - 1) m

    let selected (m : SequencedBookmarks) =
        match m.selectedBookmark with
        | Some sel ->
            tryFind sel m
        | None -> None
