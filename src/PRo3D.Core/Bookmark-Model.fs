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
                          let item = queue.Dequeue() |> Some
                          if queue.IsEmptyOrNull () then finished <- true
                          item
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

type FPSSetting =
    | Full = 0
    | Half = 1

type SequencedBookmarksPropertiesAction =
    | SetName           of string

type SequencedBookmarksAction =
    
    | FlyToSBM       of Guid
    | RemoveSBM      of Guid
    | SelectSBM      of Guid
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
    | SetDuration    of Guid * Numeric.Action
    | StartRecording
    | StopRecording
    | GenerateSnapshots
    | CancelSnapshots
    | ToggleGenerateOnStop
    | ToggleRenderStillFrames
    | ToggleDebug
    | SetResolutionX of Numeric.Action
    | SetResolutionY of Numeric.Action
    | SetOutputPath of list<string>
    | SetFpsSetting of FPSSetting
    | CheckSnapshotsProcess of string
    | UpdateJson
    | ToggleUpdateJsonBeforeRendering



[<ModelType>]
type BookmarkAnimationInfo =
    {
        bookmark : Guid
        delay    : NumericInput
        duration : NumericInput
    }
    with 
       static member FromJson( _ : BookmarkAnimationInfo) =
           json {
               let! bookmark = Json.read "bookmark"
               let! delay    = Json.readWith Ext.fromJson<NumericInput,Ext> "delay"
               let! duration = Json.readWith Ext.fromJson<NumericInput,Ext> "duration"

               return {
                    bookmark = bookmark
                    delay    = delay
                    duration = duration
               }
           }

       static member ToJson(x : BookmarkAnimationInfo) =
           json {
               do! Json.write "bookmark"    x.bookmark
               do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "delay" x.delay
               do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "duration" x.duration
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
    //animationSpeed   : NumericInput
    isRecording      : bool
    generateOnStop   : bool
    isGenerating     : bool
    isCancelled      : bool
    resolutionX      : NumericInput
    resolutionY      : NumericInput
    renderStillFrames : bool
    debug            : bool
    currentFps       : Option<int>
    outputPath       : string
    fpsSetting       : FPSSetting
    updateJsonBeforeRendering : bool
    [<NonAdaptive>]
    snapshotThreads  : ThreadPool<SequencedBookmarksAction>
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
    let defaultOutputPath () = 
        let exePath = PlatformIndependent.getPathBesideExecutable ()
        let folderPath =
            match System.IO.Path.HasExtension exePath with
            | true -> System.IO.Path.GetDirectoryName exePath
            | false -> exePath
        let p = 
            Path.combine [folderPath;"images"]
        p

    let initDelay =
        {
            value   = 3.0
            min     = 0.0
            max     = 10.0
            step    = 0.1
            format  = "{0:0.0}"
        }

    let initDuration =
        {
            value   = 2.0
            min     = 0.1
            max     = 20.0
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
                        
                | None -> 
                    bookmarks 
                        |> HashMap.map (fun id bm -> 
                                                {bookmark = id
                                                 delay = initDelay
                                                 duration = initDuration}
                                       )
            let! orderList          = Json.read "orderList"
            let! selected           = Json.read "selectedBookmark"
           // let! delay              = Json.readWith Ext.fromJson<NumericInput,Ext> "delay"
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
                    if p = "" then defaultOutputPath () else p
                | None -> defaultOutputPath ()
                    
            let! renderStillFrames = Json.tryRead "renderStillFrames"
            let renderStillFrames =
                match renderStillFrames with
                | Some b -> b
                | None   -> false
            let! updateJsonBeforeRendering = Json.tryRead "updateJsonBeforeRendering"
            let updateJsonBeforeRendering =
                match updateJsonBeforeRendering with
                | Some b -> b
                | None   -> false
            let! (fpsSetting : option<string>) = Json.tryRead "fpsSetting"
            let fpsSetting =
                match fpsSetting with
                | Some s -> FPSSetting.Parse (s)
                | None   -> FPSSetting.Full

                
            return 
                {
                    version             = current
                    bookmarks           = bookmarks
                    animationInfo       = animationInfo
                    orderList           = orderList
                    selectedBookmark    = selected
                    animationThreads    = ThreadPool.Empty
                    snapshotThreads     = ThreadPool.Empty
                    stopAnimation       = true
                    blockingCollection  = new HarriSchirchWrongBlockingCollection<_>()
                    isRecording         = false
                    isCancelled         = false
                    isGenerating        = false
                    debug               = false
                    generateOnStop      = generateOnStop
                    resolutionX         = {initResolution with value = float resolution.X}
                    resolutionY         = {initResolution with value = float resolution.Y}
                    outputPath          = outputPath
                    renderStillFrames   = renderStillFrames
                    currentFps          = None
                    fpsSetting          = fpsSetting
                    updateJsonBeforeRendering = updateJsonBeforeRendering
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
            snapshotThreads     = ThreadPool.Empty
            stopAnimation       = true
            blockingCollection  = new HarriSchirchWrongBlockingCollection<_>()
            //delay               = initDelay
            isRecording         = false
            isCancelled         = false
            isGenerating        = false
            generateOnStop      = false
            debug               = false
            resolutionX         = initResolution
            resolutionY         = initResolution
            outputPath          = defaultOutputPath () 
            renderStillFrames   = false
            currentFps          = None
            fpsSetting          = FPSSetting.Full
            updateJsonBeforeRendering = true
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
            //do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "animationSpeed"  x.animationSpeed
            do! Json.write "generateOnStop"                                     x.generateOnStop
            do! Json.write "resolution"                                         (resolution.ToString ())
            do! Json.write "outputPath"                                         x.outputPath
            do! Json.write "renderStillFrames"                                  x.renderStillFrames
            do! Json.write "updateJsonBeforeRendering"                          x.updateJsonBeforeRendering
            do! Json.write "fpsSetting"                                         (x.fpsSetting.ToString ())
        }   
