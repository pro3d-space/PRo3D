namespace PRo3D.Core.SequencedBookmarks


open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base
open PRo3D.Core

open Chiron
open System.Text.Json
open Aether
open Aether.Operators
open System.Text.RegularExpressions

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
    | LoadBookmarks
    | FlyToSBM       of Guid
    | RemoveSBM      of Guid
    | SelectSBM      of Guid
    | MoveUp         of Guid
    | MoveDown       of Guid
    | SetSceneState  of Guid
    | SaveSceneState
    | RestoreSceneState
    | AddSBookmark  
    | AddGisBookmark
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
    | GeneratePanoramaDepthImages
    | CancelPanoramas
    | SetOutputPathPanoramas of list<string>
    | CheckDepthPanoramaProcess of string


/// a reduced version of SceneConfigModel that is saved and restore with sequenced bookmarks
type SceneStateViewConfig = 
    {
        nearPlane               : float
        farPlane                : float
        frustumModel            : FrustumModel    
        arrowLength             : float
        arrowThickness          : float
        dnsPlaneSize            : float
        lodColoring             : bool
        drawOrientationCube     : bool
    } with 
    static member frustumModel_ =
        (fun c -> c.frustumModel), 
        (fun (value : FrustumModel) (c : SceneStateViewConfig) -> 
            { c with frustumModel = value })
    static member fromViewConfigModel (config : ViewConfigModel) =
        {
            nearPlane           = config.nearPlane.value          
            farPlane            = config.farPlane.value           
            frustumModel        = config.frustumModel       
            arrowLength         = config.arrowLength.value        
            arrowThickness      = config.arrowThickness.value     
            dnsPlaneSize        = config.dnsPlaneSize.value       
            lodColoring         = config.lodColoring        
            drawOrientationCube = config.drawOrientationCube
        }

    static member FromJson(_ : SceneStateViewConfig) = 
        json {
            let! nearPlane           = Json.read "nearPlane"          
            let! farPlane            = Json.read "farPlane"           
            let! frustumModel        = Json.read "frustumModel"       
            let! arrowLength         = Json.read "arrowLength"        
            let! arrowThickness      = Json.read "arrowThickness"     
            let! dnsPlaneSize        = Json.read "dnsPlaneSize"       
            let! lodColoring         = Json.read "lodColoring"        
            let! drawOrientationCube = Json.read "drawOrientationCube"

            return {
                nearPlane           = nearPlane          
                farPlane            = farPlane           
                frustumModel        = frustumModel       
                arrowLength         = arrowLength        
                arrowThickness      = arrowThickness     
                dnsPlaneSize        = dnsPlaneSize       
                lodColoring         = lodColoring        
                drawOrientationCube = drawOrientationCube
            }
        }
    static member ToJson (x : SceneStateViewConfig) =
        json {  
            do! Json.write "nearPlane"           x.nearPlane          
            do! Json.write "farPlane"            x.farPlane           
            do! Json.write "frustumModel"        x.frustumModel       
            do! Json.write "arrowLength"         x.arrowLength        
            do! Json.write "arrowThickness"      x.arrowThickness     
            do! Json.write "dnsPlaneSize"        x.dnsPlaneSize       
            do! Json.write "lodColoring"         x.lodColoring        
            do! Json.write "drawOrientationCube" x.drawOrientationCube
        }

type SceneStateReferenceSystem =
    {
        origin        : V3d
        isVisible     : bool
        size          : float
        selectedScale : string
        textsize      : float
        textcolor     : C4b
    } with
    static member fromReferenceSystem (refSystem : ReferenceSystem) 
        : SceneStateReferenceSystem =
        {
            origin        = refSystem.origin       
            isVisible     = refSystem.isVisible    
            size          = refSystem.size.value         
            selectedScale = refSystem.selectedScale
            textsize      = refSystem.textsize.value
            textcolor     = refSystem.textcolor.c
        }
    static member FromJson(_ : SceneStateReferenceSystem) = 
        json {
            let! origin        = Json.read "origin"       
            let! isVisible     = Json.read "isVisible"    
            let! size          = Json.read "size"         
            let! selectedScale = Json.read "selectedScale"
            let! textSize      = Json.tryRead "textsize"
            let! textColor     = Json.tryRead "textcolor"

            return {
                    origin        = origin |> V3d.Parse       
                    isVisible     = isVisible    
                    size          = size         
                    selectedScale = selectedScale 
                    textsize      = match textSize with
                                    | Some t -> t
                                    | None -> 0.05
                    textcolor     = match textColor with
                                    | Some tc -> tc |> C4b.Parse
                                    | None -> C4b.White 
            }
        }
    static member ToJson(x : SceneStateReferenceSystem) =
        json {  
            do! Json.write "origin"         (string x.origin)
            do! Json.write "isVisible"      x.isVisible          
            do! Json.write "size"           x.size               
            do! Json.write "selectedScale"  x.selectedScale  
            do! Json.write "textsize"       x.textsize
            do! Json.write "textcolor"      (x.textcolor.ToString())
        }

/// state of various scene elements for use with animations
type SceneState =
    {
        version               : int
        isValid               : bool
        timestamp             : DateTime
        stateAnnoatations     : GroupsModel
        stateSurfaces         : GroupsModel
        stateSceneObjects     : SceneObjectsModel
        stateScaleBars        : ScaleBarsModel
        stateGeologicSurfaces : GeologicSurfacesModel
        stateConfig           : SceneStateViewConfig
        stateReferenceSystem  : SceneStateReferenceSystem  
        stateTraverses        : option<TraverseModel>
    } with
    static member stateConfig_ =
        (
            (fun (self : SceneState) -> self.stateConfig), 
            (fun (value) (self : SceneState) -> { self with stateConfig = value })
        )
    static member surfaces_ =
        (
            (fun (self : SceneState) -> self.stateSurfaces), 
            (fun (value) (self : SceneState) -> { self with stateSurfaces = value })
        )
    static member frustum_ =
        SceneState.stateConfig_
            >-> SceneStateViewConfig.frustumModel_ 
            >-> FrustumModel.frustum_ 
    static member focalLength_ =
        SceneState.stateConfig_
            >-> SceneStateViewConfig.frustumModel_ 
            >-> FrustumModel.focal_ 
            >-> NumericInput.value_

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SceneState =
    let currentVersion = 1   
    let read0 =
            json {
                let isValid = true
                let! timestamp               = Json.tryRead "timestamp"    
                let timestamp = 
                    match timestamp with
                    | Some timestamp -> timestamp
                    | None -> DateTime.Now
                let! stateAnnoatations       = Json.read "stateAnnoatations"    
                let! stateSurfaces           = Json.read "stateSurfaces"        
                let! stateSceneObjects       = Json.read "stateSceneObjects"    
                let! stateScaleBars          = Json.read "stateScaleBars"    
                let! stateGeologicSurfaces   = Json.read "stateGeologicSurfaces"
                let! stateConfig             = Json.read "stateConfig"
                let! stateReferenceSystem    = Json.read "stateReferenceSystem"
                let! stateTraverse           = Json.tryRead "stateTraverse"

                return {
                    version                 = currentVersion
                    isValid                 = isValid
                    timestamp               = timestamp
                    stateAnnoatations       = stateAnnoatations    
                    stateSurfaces           = stateSurfaces        
                    stateSceneObjects       = stateSceneObjects    
                    stateScaleBars          = stateScaleBars
                    stateGeologicSurfaces   = stateGeologicSurfaces
                    stateConfig             = 
                        SceneStateViewConfig.fromViewConfigModel stateConfig
                    stateReferenceSystem    = 
                        SceneStateReferenceSystem.fromReferenceSystem stateReferenceSystem
                    stateTraverses          = stateTraverse
                }
            }

    let read1 = 
        json {
            let isValid = true
            let! timestamp               = Json.tryRead "timestamp"    
            let timestamp = 
                match timestamp with
                | Some timestamp -> timestamp
                | None -> DateTime.Now
            let! stateAnnoatations       = Json.read "stateAnnoatations"    
            let! stateSurfaces           = Json.read "stateSurfaces"        
            let! stateSceneObjects       = Json.read "stateSceneObjects"    
            let! stateScaleBars          = Json.read "stateScaleBars"    
            let! stateGeologicSurfaces   = Json.read "stateGeologicSurfaces"
            let! stateConfig             = Json.read "stateConfig"
            let! stateReferenceSystem    = Json.read "stateReferenceSystem"
            let! stateTraverse           = Json.tryRead "stateTraverse"

            return {
                version                 = currentVersion
                isValid                 = isValid
                timestamp               = timestamp
                stateAnnoatations       = stateAnnoatations    
                stateSurfaces           = stateSurfaces        
                stateSceneObjects       = stateSceneObjects    
                stateScaleBars          = stateScaleBars
                stateGeologicSurfaces   = stateGeologicSurfaces
                stateConfig             = stateConfig
                stateReferenceSystem    = stateReferenceSystem
                stateTraverses          = stateTraverse
            }
        }

type SceneState with
    static member FromJson( _ : SceneState) =
        json {
            let! version = Json.tryRead "version"
            match version with
            | None ->
                return! SceneState.read0
            | Some v  when v = 1 ->
                return! SceneState.read1
            | _ ->
                return! version 
                |> sprintf "don't know version %A  of SceneState"
                |> Json.error
        }

    static member ToJson(x : SceneState) =
        json {
            do! Json.write "version"               SceneState.currentVersion
            do! Json.write "timestamp"             x.timestamp
            do! Json.write "stateAnnoatations"     x.stateAnnoatations    
            do! Json.write "stateSurfaces"         x.stateSurfaces        
            do! Json.write "stateSceneObjects"     x.stateSceneObjects    
            do! Json.write "stateScaleBars"        x.stateScaleBars    
            do! Json.write "stateGeologicSurfaces" x.stateGeologicSurfaces
            do! Json.write "stateConfig"           x.stateConfig
            do! Json.write "stateReferenceSystem"  x.stateReferenceSystem
            do! Json.write "stateTraverse"         x.stateTraverses
        }

type FrustumParameters = {
    resolution  : V2i
    fieldOfView : float
    nearplane   : float
    farplane    : float
} with
     member this.perspective =
        Aardvark.Rendering.Frustum.perspective 
                this.fieldOfView 
                this.nearplane 
                this.farplane
                ((float this.resolution.X) / (float this.resolution.Y))

module FrustumParameters =
    let dummyData id =
        {
            farplane         = 100000.0
            nearplane        = 0.0002
            fieldOfView      = 6.543
            resolution       = V2i (1648, 1200)
        }

type FrustumParameters with
    static member FromJson( _ : FrustumParameters) =
        json {
            let! farplane       = Json.read "farplane"      
            let! nearplane      = Json.read "nearplane"   
            let! fieldOfView   = Json.read "fieldOfView"
            let! resolution    = Json.read "resolution" 

            return
                {
                    farplane       = farplane      
                    nearplane      = nearplane     
                    fieldOfView    = fieldOfView
                    resolution     = resolution |> V2i.Parse
                }
        }

    static member ToJson(x : FrustumParameters) =
        json {
            do! Json.write "farplane"         x.farplane
            do! Json.write "nearplane"        x.nearplane
            do! Json.write "fieldOfView"      x.fieldOfView
            do! Json.write "resolution"       (x.resolution.ToString ())
        }

/// An extended Bookmark for use with animations
/// allows saving and restoring of annotation/surface/sceneObjects/geologicObject states
[<ModelType>]
type SequencedBookmarkModel = { 
    [<NonAdaptive>]
    version             : int
    bookmark            : Bookmark
    metadata            : option<string> // TODO RNO refactor
    frustumParameters   : option<FrustumParameters>
    poseDataPath        : option<string>

    // path for saving this bookmark
    basePath            : option<string>

    /// the scene state is set during sequenced bookmark animation
    sceneState          : option<SceneState>

    ///how long an animation rests on this bookmark before proceeding to the next one
    delay               : NumericInput
    duration            : NumericInput

    observationInfo     : option<Gis.ObservationInfo>
} with 
    static member _sceneState =
        (
            (fun (self : SequencedBookmarkModel) -> self.sceneState), 
            (fun (value) (self : SequencedBookmarkModel) -> { self with sceneState = Some value })
        )
    static member _frustum =
        SequencedBookmarkModel._sceneState >?> SceneState.frustum_
    static member _focalLength =
        SequencedBookmarkModel._sceneState >?> SceneState.focalLength_
    static member _stateConfig =
        SequencedBookmarkModel._sceneState >?> SceneState.stateConfig_
    static member _surfaces =
        SequencedBookmarkModel._sceneState >?> SceneState.surfaces_
    static member _bookmark =
        (
            (fun (self : SequencedBookmarkModel) -> self.bookmark), 
            (fun (value) (self : SequencedBookmarkModel) -> { self with bookmark = value})
        )
    static member _cameraView =
        SequencedBookmarkModel._bookmark >-> Bookmark.cameraView_
    member this.key =
        this.bookmark.key
    member this.cameraView =
        this.bookmark.cameraView
    member this.name =
        this.bookmark.name

module SequencedBookmarkDefaults =
    let filenamePrefix = "SBookmark_"
    let filenamePostfix = ".pro3d"
    let filenameExtension = ".sbm"

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

module SequencedBookmarkModel =
    let current = 0

    let init bookmark = 
        {
            version             = current
            bookmark            = bookmark
            metadata            = None
            frustumParameters   = None
            poseDataPath        = None
            sceneState          = None
            delay               = SequencedBookmarkDefaults.initDelay 0.0
            duration            = SequencedBookmarkDefaults.initDuration 5.0
            basePath            = None
            observationInfo     = None
        }

    let init' bookmark sceneState frustumParameters 
              poseDataPath metadata =
        {
            version             = current
            bookmark            = bookmark
            metadata            = metadata
            frustumParameters   = frustumParameters
            poseDataPath        = poseDataPath
            sceneState          = sceneState
            delay               = SequencedBookmarkDefaults.initDelay 0.0
            duration            = SequencedBookmarkDefaults.initDuration 5.0
            basePath            = None
            observationInfo     = None
        }

    let read0 = 
        json {
            let! bookmark   = Json.read "bookmark"
            let! frustumParameters = Json.tryRead "frustumParameters"
            let! poseDataPath      = Json.tryRead "poseDataPath"
            let! sceneState = Json.read "sceneState"

            let! delay      = Json.read "delay"
            let! duration   = Json.read "duration"
            let! metadata   = Json.tryRead "metadata"
            let! observationInfo = Json.tryRead "observationInfo"

            return {
                version                 = 0              
                bookmark                = bookmark             
                frustumParameters       = frustumParameters
                metadata                = metadata
                poseDataPath            = poseDataPath
                sceneState              = sceneState
                delay                   = SequencedBookmarkDefaults.initDelay delay                
                duration                = SequencedBookmarkDefaults.initDuration duration             
                basePath                = None
                observationInfo         = observationInfo
            }
        }

type SequencedBookmarkModel with
    member this.filename =
        sprintf "%s%s%s%s" 
            SequencedBookmarkDefaults.filenamePrefix
            (this.key |> string) 
            SequencedBookmarkDefaults.filenamePostfix
            SequencedBookmarkDefaults.filenameExtension
    member this.path =
        match this.basePath with
        | Some basePath ->
            Path.combine [basePath;this.filename]
        | None ->
            this.filename
    static member FromJson( _ : SequencedBookmarkModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! SequencedBookmarkModel.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of SequencedBookmark" 
                |> Json.error
        }

    static member ToJson(x : SequencedBookmarkModel) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "bookmark"   x.bookmark
            do! Json.write "sceneState" x.sceneState
            do! Json.write "delay"      x.delay.value
            do! Json.write "duration"   x.duration.value
            if x.poseDataPath.IsSome then
                do! Json.write "poseDataPath" x.poseDataPath
            if x.frustumParameters.IsSome then
                do! Json.write "frustumParameters" x.frustumParameters
            if x.metadata.IsSome then
                do! Json.write "metadata" x.metadata
        }

[<ModelType>]
type UnloadedSequencedBookmark =
    {
        path     : string
        key      : Guid
    }
    static member FromJson( _ : UnloadedSequencedBookmark) =
        json {
            let! path = Json.read "path"
            let! key   = Json.read "key"
            return {
                path = path
                key  = key
            }
        }

    static member ToJson(x : UnloadedSequencedBookmark) =
        json {
            do! Json.write "path" x.path
            do! Json.write "key"   x.key
        }

[<ModelType>]
type SequencedBookmark =
    | LoadedBookmark of SequencedBookmarkModel
    | NotYetLoaded   of UnloadedSequencedBookmark
    static member FromJson( _ : SequencedBookmark) =
        json {
            let! (unloaded : option<UnloadedSequencedBookmark>)
                    = Json.tryRead "unloadedBookmark"
            match unloaded with
            | Some unloaded -> 
                return SequencedBookmark.NotYetLoaded unloaded
            | None ->
                let! (loaded : SequencedBookmarkModel) =
                        Json.read "loadedBookmark"
                return SequencedBookmark.LoadedBookmark loaded
        }

    static member ToJson(x : SequencedBookmark) =
        json {
            match x with
            | LoadedBookmark b ->  
                do! Json.write "loadedBookmark" {path = b.path; key = b.key}
            | NotYetLoaded b -> 
                do! Json.write "unloadedBookmark" b
        }
    member this.key =
        match this with
        | LoadedBookmark b -> b.key
        | NotYetLoaded b   -> b.key
    member this.isLoaded =
        match this with
        | LoadedBookmark _ -> true
        | NotYetLoaded _   -> false

module SequencedBookmark =        
    let isLoaded m =
        match m with
        | LoadedBookmark _ -> true
        | NotYetLoaded _   -> false

    /// tries to load a bookmark are prints errors to log if it fails
    let tryLoad (bookmark : SequencedBookmark) =
        match bookmark with
        | SequencedBookmark.NotYetLoaded m ->
            try
                let json =
                    m.path
                        |> Serialization.readFromFile
                        |> Json.parse 
                let (loadedBookmark : SequencedBookmarkModel) =
                        json
                        |> Json.deserialize
                Some loadedBookmark
            with e ->
                Log.error "[SequencedBookmarks] Error Loading Bookmark %s. When copying or moving a scene in the file system, make sure to also copy the folder with the same name as your scene. The sequenced bookmarks are stored in that folder." m.path
                Log.error "%s" e.Message
                None
        | SequencedBookmark.LoadedBookmark m ->
            Some m
            
    let tryLoad' (bookmark : SequencedBookmark) =
        let optLoaded = tryLoad bookmark
        match optLoaded with
        | Some loaded -> SequencedBookmark.LoadedBookmark loaded
        | None -> bookmark

    let unload (m : SequencedBookmark) =
        match m with
        | SequencedBookmark.LoadedBookmark bm ->       
            SequencedBookmark.NotYetLoaded {path = bm.path; key = bm.key}
        | SequencedBookmark.NotYetLoaded bm ->
            m

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
            globalDuration      = SequencedBookmarkDefaults.initDuration 20.0
            loopMode            = AnimationLoopMode.NoLoop
            useEasing           = true
            applyStateOnSelect  = false
            smoothPath          = true
            smoothingFactor     = SequencedBookmarkDefaults.initSmoothing 0.1
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
                | Some x -> SequencedBookmarkDefaults.initDuration x
                | None   -> SequencedBookmarkDefaults.initDuration 10.0

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
                | Some x -> SequencedBookmarkDefaults.initSmoothing x
                | None   -> SequencedBookmarkDefaults.initSmoothing 0.1

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
    | Bookmark      of SequencedBookmarkModel
    | Camera        of Aardvark.Rendering.CameraView
    | Configuration of (Aardvark.Rendering.CameraView 
                        * Aardvark.Rendering.Frustum)

type AnimationTimeStep =
    {
        filename : string
        content  : AnimationTimeStepContent
    }

[<ModelType>]
type SequencedBookmarks = {
    version           : int
    bookmarks         : HashMap<Guid,SequencedBookmark>
    poseDataPath      : option<string>
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

    // panorama depth images
    isGeneratingDepthImages      : bool
    isCancelledDepthImages       : bool
    outputPathDepthImages        : string

    [<NonAdaptive>]
    panoramaThreads   : ThreadPool<SequencedBookmarksAction>
  }

type BookmarkLenses<'a> = {
    navigationModel_    : Lens<'a,NavigationModel>
    sceneState_         : Lens<'a, SceneState>
    sequencedBookmarks_ : Lens<'a, SequencedBookmarks>
    setModel_           : Lens<'a, SequencedBookmark>
    selectedBookmark_   : Lens<'a, option<Guid>>
    savedTimeSteps_     : Lens<'a, list<AnimationTimeStep>>
    lastStart_          : Lens<'a, option<TimeSpan>>
    defaultObservationInfo_ : Lens<'a, Gis.ObservationInfo>
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
    
    let current = 1
    let read0 = 
        json {

            let! (seqBookmarks : option<list<SequencedBookmarkModel>>)  = 
                Json.tryRead "sequencedBookmarks"
            let! (bookmarks    : option<list<Bookmark>>)           = 
                Json.tryRead "bookmarks"
            let bookmarks = 
                match seqBookmarks, bookmarks with
                | Some sb, _ ->
                    sb 
                    |> List.map(fun (a : SequencedBookmarkModel) ->
                        (a.bookmark.key, SequencedBookmark.LoadedBookmark a)) 
                    |> HashMap.ofList
                | _, Some b ->
                    b 
                    |> List.map(fun (a : Bookmark) -> 
                        (a.key, SequencedBookmark.LoadedBookmark 
                                    (SequencedBookmarkModel.init a))
                        ) 
                    |> HashMap.ofList
                | _,_ -> HashMap.empty

            let! orderList          = Json.read "orderList"
            let! selected           = Json.read "selectedBookmark"
            let! generateOnStop     = Json.tryRead "generateOnStop"
            let! poseDataPath       = Json.tryRead "poseDataPath"
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

            let! (outputPathDepthImages : option<string>) = Json.tryRead "outputPathDepthImages"
            let outputPathDepthImages = 
                match outputPathDepthImages with
                | Some p -> 
                    if p = "" then defaultOutputPath () else p
                | None -> defaultOutputPath ()
                
            return 
                {
                    version             = current
                    bookmarks           = bookmarks
                    poseDataPath        = poseDataPath
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
                    isCancelledDepthImages  = false
                    isGeneratingDepthImages = false
                    outputPathDepthImages = outputPathDepthImages
                    panoramaThreads     = ThreadPool.Empty
                }
        }  

    let read1 = 
        json {
            let! (seqBookmarks : option<list<SequencedBookmark>>)  = Json.tryRead "sequencedBookmarks"
            let! (bookmarks    : option<list<Bookmark>>)           = Json.tryRead "bookmarks"
            let bookmarks = 
                match seqBookmarks, bookmarks with
                | Some sb, _ ->
                    sb |> List.map (fun (a : SequencedBookmark) -> (a.key, a)) 
                       |> HashMap.ofList
                | _, Some b ->
                    b |> List.map(fun (a : Bookmark) -> (a.key, SequencedBookmark.LoadedBookmark
                                                                    <| SequencedBookmarkModel.init a)) 
                      |> HashMap.ofList
                | _,_ -> HashMap.empty
            let! poseDataPath       = Json.tryRead "poseDataPath"
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

            let! (outputPathDepthImages : option<string>) = Json.tryRead "outputPathDepthImages"
            let outputPathDepthImages = 
                match outputPathDepthImages with
                | Some p -> 
                    if p = "" then defaultOutputPath () else p
                | None -> defaultOutputPath ()
                
            return 
                {
                    version             = current
                    bookmarks           = bookmarks
                    poseDataPath        = poseDataPath
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
                    isCancelledDepthImages  = false
                    isGeneratingDepthImages = false
                    outputPathDepthImages = outputPathDepthImages
                    panoramaThreads     = ThreadPool.Empty
                }
        }  



    let initial =
        {
            version             = current
            bookmarks           = HashMap.Empty
            poseDataPath        = None
            savedSceneState     = None
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
            isCancelledDepthImages  = false
            isGeneratingDepthImages = false
            outputPathDepthImages = defaultOutputPath ()
            panoramaThreads     = ThreadPool.Empty
        }

type SequencedBookmarks with
    static member FromJson (_ : SequencedBookmarks) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! SequencedBookmarks.read0
            | 1 -> return! SequencedBookmarks.read1
            | _ ->
                return! v
                |> sprintf "don't know version %A  of SequencedBookmarks"
                |> Json.error
        }

    static member ToJson (x : SequencedBookmarks) =
        let resolution = (V2i(x.resolutionX.value, x.resolutionY.value))
        json {
            do! Json.write "version"                    x.version
            do! Json.write "sequencedBookmarks"         (x.bookmarks 
                                                         |> HashMap.toList 
                                                         |> List.map snd)
            do! Json.write "orderList"                  x.orderList
            if x.poseDataPath.IsSome then
                do! Json.write "poseDataPath"           x.poseDataPath
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
