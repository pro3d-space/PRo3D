namespace PRo3D.Core

open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI.Primitives
open PRo3D
open PRo3D.Base

open Chiron

#nowarn "0686"

[<ModelType>]
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

type HarriSchirchWrongBlockingCollection<'a>() =
    let sema = new System.Threading.SemaphoreSlim(0)
    let l = obj()
    let queue = System.Collections.Generic.Queue<'a>()
    let mutable finished = false

    member x.TakeAsync() =
        async {
            do! sema.WaitAsync() |> Async.AwaitTask
            if finished then return None
            else
                return 
                    lock l (fun _ -> 
                        queue.Dequeue() |> Some
                    )
        }

    member x.Enqueue(v) =
        lock l (fun _ -> 
            queue.Enqueue(v)
        )
        sema.Release() |> ignore

    member x.CompleteAdding() =
        finished <- true
        sema.Release() |> ignore

    member x.Restart() =
        finished <- false
        sema.Release() |> ignore

    member x.Start() =
        finished <- false
        queue.Clear()
        sema.Release() |> ignore

    member x.IsCompleted = finished

type SequencedBookmarksPropertiesAction =
    | SetName           of string

type SequencedBookmarksAction =
    
    | FlyToSBM       of Guid
    | RemoveSBM      of Guid
    | SelectSBM      of Guid
    | IsVisible      of Guid
    | MoveUp         of Guid
    | MoveDown       of Guid
    | AddSBookmark  
    | PropertiesMessage     of SequencedBookmarksPropertiesAction
    | Play
    | Pause
    | Stop
    | StepForward
    | StepBackward
    | AnimationThreadsDone  of string
    | AnimStep       of Guid
    | SetDelay       of Numeric.Action
    | SetAnimationSpeed       of Numeric.Action


[<ModelType>]
type SequencedBookmarks = {
    version          : int
    bookmarks        : HashMap<Guid,Bookmark>
    orderList        : List<Guid>
    selectedBookmark : Option<Guid> 

    [<NonAdaptive>]
    animationThreads : ThreadPool<SequencedBookmarksAction>
    [<NonAdaptive>]
    stopAnimation    : bool
    [<NonAdaptive>]
    blockingCollection : HarriSchirchWrongBlockingCollection<SequencedBookmarksAction>

    delay            : NumericInput
    animationSpeed   : NumericInput
}

module SequencedBookmarks =
    
    let current = 0    
    let read0 = 
        json {
            let! bookmarks          = Json.read "bookmarks"
            let bookmarks           = bookmarks |> List.map(fun (a : Bookmark) -> (a.key, a)) |> HashMap.ofList
            let! orderList          = Json.read "orderList"
            let! selected           = Json.read "selectedBookmark"
            let! delay              = Json.readWith Ext.fromJson<NumericInput,Ext> "delay"
            let! animationSpeed     = Json.readWith Ext.fromJson<NumericInput,Ext> "animationSpeed"
            return 
                {
                    version             = current
                    bookmarks           = bookmarks
                    orderList           = orderList
                    selectedBookmark    = selected
                    animationThreads    = ThreadPool.Empty
                    stopAnimation       = true
                    blockingCollection  = new HarriSchirchWrongBlockingCollection<_>()
                    delay               = delay
                    animationSpeed      = animationSpeed
                }
        }  

    let initDelay =
        {
            value   = 3.0
            min     = 0.5
            max     = 10.0
            step    = 0.1
            format  = "{0:0.0}"
        }

    let initSpeed =
        {
            value   = 2.0
            min     = 0.1
            max     = 10.0
            step    = 0.1
            format  = "{0:0.0}"
        }

    let initial =
        {
            version             = current
            bookmarks           = HashMap.Empty
            orderList           = List.empty
            selectedBookmark    = None
            animationThreads    = ThreadPool.Empty
            stopAnimation       = true
            blockingCollection  = new HarriSchirchWrongBlockingCollection<_>()
            delay               = initDelay
            animationSpeed      = initSpeed
        }

type SequencedBookmarks with
    static member FromJson (_ : SequencedBookmarks) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! SequencedBookmarks.read0
            | _ ->
                return! v
                |> sprintf "don't know version %A  of SequencedBookmarks"
                |> Json.error
        }

    static member ToJson (x : SequencedBookmarks) =
        json {
            do! Json.write "version"                                            x.version
            do! Json.write "bookmarks"                                          (x.bookmarks |> HashMap.toList |> List.map snd)
            do! Json.write "orderList"                                          x.orderList
            do! Json.write "selectedBookmark"                                   x.selectedBookmark
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "delay"           x.delay
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "animationSpeed"  x.animationSpeed
        }   
