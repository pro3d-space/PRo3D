namespace PRo3D.Core.SequencedBookmarks


open System
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base
open PRo3D.Core

open Chiron
open Aether
open Aether.Operators

/// used to set whether all recorded frames should be used
/// for batch rendering, or half
type FPSSetting =
    | Full = 0
    | Half = 1

type SequencedBookmarkAction =
    | SetName of string
    | SetDelay       of Numeric.Action
    | SetDuration    of Numeric.Action

[<Struct>]
type AnimationLoopMode = 
    | NoLoop = 0
    | Repeat = 1
    | Mirror = 2

type AnimationSettingsAction =
    /// sets the time the whole animation takes
    | SetGlobalDuration  of Numeric.Action
    | ToggleGlobalAnimation
    | SetLoopMode of AnimationLoopMode
    | ToggleApplyStateOnSelect
    | ToggleUseEasing
    | ToggleUseSmoothing
    | SetSmoothingFactor of Numeric.Action

type SequencedBookmarksAction =
    | SequencedBookmarkMessage of (Guid * SequencedBookmarkAction)
    | AnimationSettingsMessage of AnimationSettingsAction
    | FlyToSBM       of Guid
    | RemoveSBM      of Guid
    | SelectSBM      of Guid
    | MoveUp         of Guid
    | MoveDown       of Guid
    | SetSceneState of Guid
    | AddSBookmark  
    | Play
    | Pause
    | Stop
    | StepForward
    | StepBackward
    | StartRecording
    | StopRecording
    | GenerateSnapshots
    | CancelSnapshots
    | ToggleGenerateOnStop
    | ToggleDebug
    | SetResolutionX of Numeric.Action
    | SetResolutionY of Numeric.Action
    | SetOutputPath of list<string>
    | SetFpsSetting of FPSSetting
    | CheckSnapshotsProcess of string
    | UpdateJson
    | ToggleUpdateJsonBeforeRendering
    | SaveAnimation

/// state of various scene elements for use with animations
type SceneState =
    {
        stateAnnoatations     : GroupsModel
        stateSurfaces         : GroupsModel
        stateSceneObjects     : SceneObjectsModel
        stateScaleBars        : ScaleBarsModel
        stateGeologicSurfaces : GeologicSurfacesModel
        stateConfig           : ViewConfigModel
        stateReferenceSystem  : ReferenceSystem  
    } with
    static member FromJson( _ : SceneState) =
        json {
            let! stateAnnoatations       = Json.read "stateAnnoatations"    
            let! stateSurfaces           = Json.read "stateSurfaces"        
            let! stateSceneObjects       = Json.read "stateSceneObjects"    
            let! stateScaleBars          = Json.read "stateScaleBars"    
            let! stateGeologicSurfaces   = Json.read "stateGeologicSurfaces"
            let! stateConfig             = Json.read "stateConfig"
            let! stateReferenceSystem    = Json.read "stateReferenceSystem"

            return {
                stateAnnoatations       = stateAnnoatations    
                stateSurfaces           = stateSurfaces        
                stateSceneObjects       = stateSceneObjects    
                stateScaleBars          = stateScaleBars
                stateGeologicSurfaces   = stateGeologicSurfaces
                stateConfig             = stateConfig
                stateReferenceSystem    = stateReferenceSystem
            }
        }

    static member ToJson(x : SceneState) =
        json {
            do! Json.write "stateAnnoatations"     x.stateAnnoatations    
            do! Json.write "stateSurfaces"         x.stateSurfaces        
            do! Json.write "stateSceneObjects"     x.stateSceneObjects    
            do! Json.write "stateScaleBars"        x.stateScaleBars    
            do! Json.write "stateGeologicSurfaces" x.stateGeologicSurfaces
            do! Json.write "stateConfig"           x.stateConfig
            do! Json.write "stateReferenceSystem"  x.stateReferenceSystem
        }

/// An extended Bookmark for use with animations
/// allows saving and restoring of annotation/surface/sceneObjects/geologicObject states
[<ModelType>]
type SequencedBookmark = { //WIP RNO
    [<NonAdaptive>]
    version             : int

    bookmark            : Bookmark
    /// the scene state is set during sequenced bookmark animation
    sceneState          : option<SceneState>

    ///how long an animation rests on this bookmark before proceeding to the next one
    delay               : NumericInput
    duration            : NumericInput
} with member this.key =
        this.bookmark.key
       member this.cameraView =
        this.bookmark.cameraView
       member this.name =
        this.bookmark.name

module SequencedBookmark =
    let current = 0   

    let initSmoothing value =
        {
            value   = value
            min     = 0.0
            max     = 1.0
            step    = 0.1
            format  = "{0:0.0}"
        }

    let initDelay value =
        {
            value   = value
            min     = 0.0
            max     = 1000.0
            step    = 0.1
            format  = "{0:0.0}"
        }

    let initDuration value =
        {
            value   = value
            min     = 0.1
            max     = 10000.0
            step    = 0.1
            format  = "{0:0.0}"
        }

    let init bookmark = 
        {
            version = current
            bookmark = bookmark
            sceneState = None
            delay = initDelay 0.0
            duration = initDuration 5.0
        }

    let read0 = 
        json {
            let! bookmark   = Json.read "bookmark"
            
            let! sceneState = Json.read "sceneState"

            let! delay      = Json.read "delay"
            let! duration   = Json.read "duration"

            return {
                version                 = 0              
                bookmark                = bookmark             
                sceneState              = sceneState
                delay                   = initDelay delay                
                duration                = initDuration duration             
            }
        }


type SequencedBookmark with
    static member FromJson( _ : SequencedBookmark) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! SequencedBookmark.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of SequencedBookmark" 
                |> Json.error
        }

    static member ToJson(x : SequencedBookmark) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "bookmark"   x.bookmark
            do! Json.write "sceneState" x.sceneState
            do! Json.write "delay"      x.delay.value
            do! Json.write "duration"   x.duration.value
        }



[<ModelType>]
type AnimationSettings = {
    /// duration of the whole animation sequence (all bookmarks)
    /// cannot be combined with duration settings for individual bookmarks
    globalDuration     : NumericInput
    useGlobalAnimation : bool
    loopMode           : AnimationLoopMode
    useEasing          : bool
    applyStateOnSelect : bool
    /// animate along a spline if true
    smoothPath         : bool
    smoothingFactor    : NumericInput
} with     
    static member init = 
        {
            useGlobalAnimation  = false
            globalDuration      = SequencedBookmark.initDuration 20.0
            loopMode            = AnimationLoopMode.NoLoop
            useEasing           = true
            applyStateOnSelect  = false
            smoothPath          = true
            smoothingFactor     = SequencedBookmark.initSmoothing 0.1
        }
    static member FromJson( _ : AnimationSettings) =
        json {
            let! (useGlobalAnimation : option<bool>) = Json.tryRead "useGlobalAnimation"
            let useGlobalAnimation =
                match useGlobalAnimation with
                | Some true -> true
                | Some false -> false
                | None -> false

            let! (globalDuration : option<float>) = Json.tryRead "globalDuration"
            let globalDuration =
                match globalDuration with
                | Some x -> SequencedBookmark.initDuration x
                | None   -> SequencedBookmark.initDuration 10.0

            let! (loopMode : option<int32>) = Json.tryRead "globalDuration"
            let loopMode =
                match loopMode with
                | Some x -> x |> enum 
                | None   -> AnimationLoopMode.NoLoop

            let! (applyStateOnSelect : option<bool>) = Json.tryRead "applyStateOnSelect"
            let applyStateOnSelect =
                match applyStateOnSelect with
                | Some x -> x
                | None   -> false

            let! (useEasing : option<bool>) = Json.tryRead "useEasing"
            let useEasing =
                match useEasing with
                | Some x -> x
                | None   -> false

            let! (smoothPath : option<bool>) = Json.tryRead "smoothPath"
            let smoothPath =
                match smoothPath with
                | Some x -> x
                | None   -> false

            let! (smoothingFactor : option<float>) = Json.tryRead "smoothingFactor"
            let smoothingFactor =
                match smoothingFactor with
                | Some x -> SequencedBookmark.initSmoothing x
                | None   -> SequencedBookmark.initSmoothing 0.1

            return {
                useGlobalAnimation = useGlobalAnimation
                globalDuration     = globalDuration
                loopMode           = loopMode
                applyStateOnSelect = applyStateOnSelect
                useEasing          = useEasing
                smoothPath         = smoothPath     
                smoothingFactor    = smoothingFactor
            }
        }

    static member ToJson(x : AnimationSettings) =
        json {
            do! Json.write "useGlobalAnimation" x.useGlobalAnimation
            do! Json.write "globalDuration"     x.globalDuration.value
            do! Json.write "loopMode"           (x.loopMode |> int)
            do! Json.write "applyStateOnSelect" x.applyStateOnSelect
            do! Json.write "useEasing"          x.useEasing
            do! Json.write "smoothPath"         x.smoothPath
            do! Json.write "smoothingFactor"    x.smoothingFactor.value
        }

type AnimationTimeStepContent =
    | Bookmark of SequencedBookmark
    | Camera   of Aardvark.Rendering.CameraView

type AnimationTimeStep =
    {
        filename : string
        content  : AnimationTimeStepContent
    }

[<ModelType>]
type SequencedBookmarks = {
    version           : int
    bookmarks         : HashMap<Guid,SequencedBookmark>
    /// currently not in use, could be used to save and resotre a certain state independantly of bookmarks
    savedSceneState   : Option<SceneState>
    orderList         : List<Guid>
    selectedBookmark  : Option<Guid> 
    animationSettings : AnimationSettings
    lastSavedBookmark : option<Guid>
    /// used for batch rendering
    savedTimeSteps    : list<AnimationTimeStep>
    isRecording       : bool
    generateOnStop    : bool
    isGenerating      : bool
    isCancelled       : bool
    // X resolution of output image of batch rendering
    resolutionX       : NumericInput
    // Y resolution of output image of batch rendering
    resolutionY       : NumericInput
    debug             : bool
    currentFps        : option<int>
    /// time of start of last animation
    /// used to calculate fps of recorded animation
    lastStart         : option<System.TimeSpan>
    outputPath        : string
    fpsSetting        : FPSSetting
   
    [<NonAdaptive>]
    snapshotThreads   : ThreadPool<SequencedBookmarksAction>
    updateJsonBeforeRendering : bool
  }

type BookmarkLenses<'a> = {
    navigationModel_    : Lens<'a,NavigationModel>
    sceneState_         : Lens<'a, SceneState>
    sequencedBookmarks_ : Lens<'a, SequencedBookmarks>
    setModel_           : Lens<'a, SequencedBookmark>
    selectedBookmark_   : Lens<'a, option<Guid>>
    savedTimeSteps_     : Lens<'a, list<AnimationTimeStep>>
    lastStart_          : Lens<'a, option<TimeSpan>>
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

            let! (seqBookmarks : option<list<SequencedBookmark>>)  = Json.tryRead "sequencedBookmarks"
            let! (bookmarks    : option<list<Bookmark>>)           = Json.tryRead "bookmarks"
            let bookmarks = 
                match seqBookmarks, bookmarks with
                | Some sb, _ ->
                    sb |> List.map (fun (a : SequencedBookmark) -> (a.bookmark.key, a)) 
                       |> HashMap.ofList
                | _, Some b ->
                    b |> List.map(fun (a : Bookmark) -> (a.key, SequencedBookmark.init a)) 
                      |> HashMap.ofList
                | _,_ -> HashMap.empty

            let! orderList          = Json.read "orderList"
            let! selected           = Json.read "selectedBookmark"
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
            let! (debug : option<bool>) = Json.tryRead "debug"
            let debug =
                match debug with
                | Some true -> true
                | Some false -> false
                | None -> false

            let! animationSettings = Json.tryRead "animationSettings"
            let animationSettings =
                match animationSettings with
                | Some animationSettings -> animationSettings
                | None ->
                    AnimationSettings.init

            let! (sceneState : option<SceneState>) = Json.tryRead "originalSceneState"
                
            return 
                {
                    version             = current
                    bookmarks           = bookmarks
                    savedSceneState     = sceneState
                    orderList           = orderList
                    selectedBookmark    = selected
                    snapshotThreads     = ThreadPool.Empty
                    animationSettings   = animationSettings
                    lastSavedBookmark   = None
                    savedTimeSteps      = []
                    isRecording         = false
                    isCancelled         = false
                    isGenerating        = false
                    debug               = debug
                    generateOnStop      = generateOnStop
                    resolutionX         = {initResolution with value = float resolution.X}
                    resolutionY         = {initResolution with value = float resolution.Y}
                    outputPath          = outputPath
                    currentFps          = None
                    lastStart           = None
                    fpsSetting          = fpsSetting
                    updateJsonBeforeRendering = updateJsonBeforeRendering
                }
        }  



    let initial =
        {
            version             = current
            bookmarks           = HashMap.Empty
            savedSceneState  = None
            orderList           = List.empty
            selectedBookmark    = None
            snapshotThreads     = ThreadPool.Empty
            animationSettings   = AnimationSettings.init
            lastSavedBookmark   = None
            savedTimeSteps      = []
            isRecording         = false
            isCancelled         = false
            isGenerating        = false
            generateOnStop      = false
            debug               = false
            resolutionX         = initResolution
            resolutionY         = initResolution
            outputPath          = defaultOutputPath () 
            currentFps          = None
            lastStart           = None
            fpsSetting          = FPSSetting.Full
            updateJsonBeforeRendering = true
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
            do! Json.write "version"                    x.version
            do! Json.write "sequencedBookmarks"         (x.bookmarks |> HashMap.toList |> List.map snd)
            do! Json.write "orderList"                  x.orderList
            do! Json.write "selectedBookmark"           x.selectedBookmark
            if x.savedSceneState.IsSome then
                do! Json.write "originalSceneState"     x.savedSceneState.Value
            do! Json.write "animationSettings"          x.animationSettings
            do! Json.write "debug"                      x.debug
            do! Json.write "generateOnStop"             x.generateOnStop
            do! Json.write "resolution"                 (resolution.ToString ())
            do! Json.write "outputPath"                 x.outputPath
            do! Json.write "updateJsonBeforeRendering"  x.updateJsonBeforeRendering
            do! Json.write "fpsSetting"                 (x.fpsSetting.ToString ())
        }   
