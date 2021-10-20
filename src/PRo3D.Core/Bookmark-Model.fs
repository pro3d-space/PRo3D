namespace PRo3D.Base

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
                        if queue.IsEmptyOrNull () then None
                        else 
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
    | SetDelay       of Guid * Numeric.Action
    | SetAnimationSpeed       of Numeric.Action
    | StartRecording
    | StopRecording
    | GenerateSnapshots
    | CancelSnapshots
    | ToggleGenerateOnStop
    | ToggleRenderStillFrames
    | ToggleUseSpeed
    | SetResolutionX of Numeric.Action
    | SetResolutionY of Numeric.Action
    | SetOutputPath of list<string>

[<ModelType>]
type BookmarkAnimationInfo =
    {
        bookmark : Guid
        delay    : NumericInput
    }
    with 
       static member FromJson( _ : BookmarkAnimationInfo) =
           json {
               let! bookmark = Json.read "bookmark"
               let! delay    = Json.readWith Ext.fromJson<NumericInput,Ext> "delay"

               return {
                    bookmark = bookmark
                    delay    = delay
               }
           }

       static member ToJson(x : BookmarkAnimationInfo) =
           json {
               do! Json.write "bookmark"    x.bookmark
               do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "delay" x.delay
           }

[<ModelType>]
type SequencedBookmarks = {
    version          : int
    bookmarks        : HashMap<Guid,Bookmark>
    animationInfo    : HashMap<Guid, BookmarkAnimationInfo>
    orderList        : List<Guid>
    selectedBookmark : Option<Guid> 

    [<NonAdaptive>]
    animationThreads : ThreadPool<SequencedBookmarksAction>
    [<NonAdaptive>]
    stopAnimation    : bool
    [<NonAdaptive>]
    blockingCollection : HarriSchirchWrongBlockingCollection<SequencedBookmarksAction>

   //delay            : NumericInput
    animationSpeed   : NumericInput

    isRecording      : bool
    generateOnStop   : bool
    isGenerating     : bool
    isCancelled      : bool
    resolutionX      : NumericInput
    resolutionY      : NumericInput
    renderStillFrames : bool
    currentFps       : Option<int>
    outputPath       : string
    useSpeed         : bool
  //  snapshotProcess  : option<System.Diagnostics.Process>
  }
//} with interface IDisposable with 
//            member this.Dispose () = 
//                match this.snapshotProcess with
//                | Some p -> 
//                    do printfn "disposing process"
//                    p.Kill ()
//                | None -> ()

type FrameRepetition =
    {
        index       : int
        repetitions : int
    }
                

module SequencedBookmarks =
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

    let initResolution =
        {
            value   = 1024.0
            min     = 1.0
            max     = 5000.0
            step    = 1.0
            format  = "{0:0.0}"
        }
    
    let current = 0    
    let read0 = 
        json {
            let! bookmarks          = Json.read "bookmarks"
            let bookmarks           = bookmarks |> List.map(fun (a : Bookmark) -> (a.key, a)) |> HashMap.ofList
            let! (animationInfo : option<list<BookmarkAnimationInfo>>) = Json.tryRead "animationInfo"
            let animationInfo =
                match animationInfo with
                | Some animationInfo ->
                    animationInfo 
                        |> List.map (fun (a : BookmarkAnimationInfo) -> (a.bookmark, a))
                        |> HashMap.ofList
                        
                | None -> bookmarks |> HashMap.map (fun id bm -> {bookmark = id; delay = initDelay})
            let! orderList          = Json.read "orderList"
            let! selected           = Json.read "selectedBookmark"
           // let! delay              = Json.readWith Ext.fromJson<NumericInput,Ext> "delay"
            let! animationSpeed     = Json.readWith Ext.fromJson<NumericInput,Ext> "animationSpeed"
            let! generateOnStop     = Json.tryRead "generateOnStop"
            let generateOnStop =
                match generateOnStop with
                | Some g -> g
                | None -> false
                
            let! resolution = Json.parseOption (Json.tryRead "resolution") V2i.Parse 
            let resolution =
                match resolution with
                | Some r -> r
                | None -> V2i (initResolution.value)
            let! (outputPath : option<string>) = Json.tryRead "outputPath"
            let outputPath = 
                match outputPath with
                | Some p -> 
                    if p = "" then PlatformIndependent.getPathBesideExecutable () else p
                | None -> PlatformIndependent.getPathBesideExecutable ()
            let! renderStillFrames = Json.tryRead "renderStillFrames"
            let renderStillFrames =
                match renderStillFrames with
                | Some b -> b
                | None   -> false
            let! useSpeed = Json.tryRead "useSpeed"
            let useSpeed =
                match useSpeed with
                | Some b -> b
                | None   -> true

                
            return 
                {
                    version             = current
                    bookmarks           = bookmarks
                    animationInfo       = animationInfo
                    orderList           = orderList
                    selectedBookmark    = selected
                    animationThreads    = ThreadPool.Empty
                    stopAnimation       = true
                    blockingCollection  = new HarriSchirchWrongBlockingCollection<_>()
                    //delay               = delay
                    animationSpeed      = animationSpeed
                    isRecording         = false
                    isCancelled         = false
                    isGenerating        = false
                    generateOnStop      = generateOnStop
                    resolutionX         = {initResolution with value = float resolution.X}
                    resolutionY         = {initResolution with value = float resolution.Y}
                    outputPath          = outputPath
                    renderStillFrames   = renderStillFrames
                    currentFps          = None
                    useSpeed            = useSpeed
                    //snapshotProcess     = None
                }
        }  



    let initial =
        {
            version             = current
            bookmarks           = HashMap.Empty
            animationInfo       = HashMap.Empty
            orderList           = List.empty
            selectedBookmark    = None
            animationThreads    = ThreadPool.Empty
            stopAnimation       = true
            blockingCollection  = new HarriSchirchWrongBlockingCollection<_>()
            //delay               = initDelay
            animationSpeed      = initSpeed
            isRecording         = false
            isCancelled         = false
            isGenerating        = false
            generateOnStop      = false
            resolutionX         = initResolution
            resolutionY         = initResolution
            outputPath          = ""
            renderStillFrames   = false
            currentFps          = None
            useSpeed            = true
            //snapshotProcess     = None
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
        let resolution = (V2i(x.resolutionX.value, x.resolutionY.value))
        json {
            do! Json.write "version"                                            x.version
            do! Json.write "bookmarks"                                          (x.bookmarks |> HashMap.toList |> List.map snd)
            do! Json.write "orderList"                                          x.orderList
            do! Json.write "selectedBookmark"                                   x.selectedBookmark
            do! Json.write "animationInfo"                                      (x.animationInfo |> HashMap.toList |> List.map snd)
            //do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "delay"           x.delay
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "animationSpeed"  x.animationSpeed
            do! Json.write "generateOnStop"                                     x.generateOnStop
            do! Json.write "resolution"                                         (resolution.ToString ())
            do! Json.write "outputPath"                                         x.outputPath
            do! Json.write "renderStillFrames"                                  x.renderStillFrames
            do! Json.write "useSpeed"                                           x.useSpeed
        }   
