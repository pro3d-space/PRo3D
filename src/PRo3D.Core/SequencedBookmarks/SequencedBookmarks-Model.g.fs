//6d8cd910-4dda-ff00-b5b9-3124d680763e
//bdd63ce1-08dc-4e25-a5d5-c96ce1265846
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Core.SequencedBookmarks

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Core.SequencedBookmarks
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSequencedBookmarkModel(value : SequencedBookmarkModel) =
    let mutable _path_ = FSharp.Data.Adaptive.cval(value.path)
    let mutable _name_ = FSharp.Data.Adaptive.cval(value.name)
    let mutable _cameraView_ = FSharp.Data.Adaptive.cval(value.cameraView)
    let mutable _filename_ = FSharp.Data.Adaptive.cval(value.filename)
    let mutable _key_ = FSharp.Data.Adaptive.cval(value.key)
    let _bookmark_ = PRo3D.Core.AdaptiveBookmark(value.bookmark)
    let _metadata_ = FSharp.Data.Adaptive.cval(value.metadata)
    let _frustumParameters_ = FSharp.Data.Adaptive.cval(value.frustumParameters)
    let _poseDataPath_ = FSharp.Data.Adaptive.cval(value.poseDataPath)
    let _basePath_ = FSharp.Data.Adaptive.cval(value.basePath)
    let _sceneState_ = FSharp.Data.Adaptive.cval(value.sceneState)
    let _delay_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.delay)
    let _duration_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.duration)
    let _observationInfo_ =
        let inline __arg2 (o : System.Object) (v : PRo3D.Core.Gis.ObservationInfo) =
            (unbox<PRo3D.Core.Gis.AdaptiveObservationInfo> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : PRo3D.Core.Gis.ObservationInfo) =
            (unbox<PRo3D.Core.Gis.AdaptiveObservationInfo> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Core.Gis.ObservationInfo, PRo3D.Core.Gis.AdaptiveObservationInfo, PRo3D.Core.Gis.AdaptiveObservationInfo>(value.observationInfo, (fun (v : PRo3D.Core.Gis.ObservationInfo) -> PRo3D.Core.Gis.AdaptiveObservationInfo(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<PRo3D.Core.Gis.AdaptiveObservationInfo> o), (fun (v : PRo3D.Core.Gis.ObservationInfo) -> PRo3D.Core.Gis.AdaptiveObservationInfo(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<PRo3D.Core.Gis.AdaptiveObservationInfo> o))
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SequencedBookmarkModel) = AdaptiveSequencedBookmarkModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SequencedBookmarkModel) -> AdaptiveSequencedBookmarkModel(value)) (fun (adaptive : AdaptiveSequencedBookmarkModel) (value : SequencedBookmarkModel) -> adaptive.Update(value))
    member __.Update(value : SequencedBookmarkModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SequencedBookmarkModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _path_.Value <- value.path
            _name_.Value <- value.name
            _cameraView_.Value <- value.cameraView
            _filename_.Value <- value.filename
            _key_.Value <- value.key
            _bookmark_.Update(value.bookmark)
            _metadata_.Value <- value.metadata
            _frustumParameters_.Value <- value.frustumParameters
            _poseDataPath_.Value <- value.poseDataPath
            _basePath_.Value <- value.basePath
            _sceneState_.Value <- value.sceneState
            _delay_.Update(value.delay)
            _duration_.Update(value.duration)
            _observationInfo_.Update(value.observationInfo)
    member __.Current = __adaptive
    member __.path = _path_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.cameraView = _cameraView_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.filename = _filename_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.key = _key_ :> FSharp.Data.Adaptive.aval<System.Guid>
    member __.version = __value.version
    member __.bookmark = _bookmark_
    member __.metadata = _metadata_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.frustumParameters = _frustumParameters_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<FrustumParameters>>
    member __.poseDataPath = _poseDataPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.basePath = _basePath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.sceneState = _sceneState_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<SceneState>>
    member __.delay = _delay_
    member __.duration = _duration_
    member __.observationInfo = _observationInfo_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<PRo3D.Core.Gis.ObservationInfo, PRo3D.Core.Gis.AdaptiveObservationInfo, PRo3D.Core.Gis.AdaptiveObservationInfo>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SequencedBookmarkModelLenses = 
    type SequencedBookmarkModel with
        static member version_ = ((fun (self : SequencedBookmarkModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : SequencedBookmarkModel) -> { self with version = value }))
        static member bookmark_ = ((fun (self : SequencedBookmarkModel) -> self.bookmark), (fun (value : PRo3D.Core.Bookmark) (self : SequencedBookmarkModel) -> { self with bookmark = value }))
        static member metadata_ = ((fun (self : SequencedBookmarkModel) -> self.metadata), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : SequencedBookmarkModel) -> { self with metadata = value }))
        static member frustumParameters_ = ((fun (self : SequencedBookmarkModel) -> self.frustumParameters), (fun (value : Microsoft.FSharp.Core.option<FrustumParameters>) (self : SequencedBookmarkModel) -> { self with frustumParameters = value }))
        static member poseDataPath_ = ((fun (self : SequencedBookmarkModel) -> self.poseDataPath), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : SequencedBookmarkModel) -> { self with poseDataPath = value }))
        static member basePath_ = ((fun (self : SequencedBookmarkModel) -> self.basePath), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : SequencedBookmarkModel) -> { self with basePath = value }))
        static member sceneState_ = ((fun (self : SequencedBookmarkModel) -> self.sceneState), (fun (value : Microsoft.FSharp.Core.option<SceneState>) (self : SequencedBookmarkModel) -> { self with sceneState = value }))
        static member delay_ = ((fun (self : SequencedBookmarkModel) -> self.delay), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : SequencedBookmarkModel) -> { self with delay = value }))
        static member duration_ = ((fun (self : SequencedBookmarkModel) -> self.duration), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : SequencedBookmarkModel) -> { self with duration = value }))
        static member observationInfo_ = ((fun (self : SequencedBookmarkModel) -> self.observationInfo), (fun (value : Microsoft.FSharp.Core.option<PRo3D.Core.Gis.ObservationInfo>) (self : SequencedBookmarkModel) -> { self with observationInfo = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveUnloadedSequencedBookmark(value : UnloadedSequencedBookmark) =
    let _path_ = FSharp.Data.Adaptive.cval(value.path)
    let _key_ = FSharp.Data.Adaptive.cval(value.key)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : UnloadedSequencedBookmark) = AdaptiveUnloadedSequencedBookmark(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : UnloadedSequencedBookmark) -> AdaptiveUnloadedSequencedBookmark(value)) (fun (adaptive : AdaptiveUnloadedSequencedBookmark) (value : UnloadedSequencedBookmark) -> adaptive.Update(value))
    member __.Update(value : UnloadedSequencedBookmark) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<UnloadedSequencedBookmark>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _path_.Value <- value.path
            _key_.Value <- value.key
    member __.Current = __adaptive
    member __.path = _path_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.key = _key_ :> FSharp.Data.Adaptive.aval<System.Guid>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module UnloadedSequencedBookmarkLenses = 
    type UnloadedSequencedBookmark with
        static member path_ = ((fun (self : UnloadedSequencedBookmark) -> self.path), (fun (value : Microsoft.FSharp.Core.string) (self : UnloadedSequencedBookmark) -> { self with path = value }))
        static member key_ = ((fun (self : UnloadedSequencedBookmark) -> self.key), (fun (value : System.Guid) (self : UnloadedSequencedBookmark) -> { self with key = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSequencedBookmarkCase =
    abstract member Update : SequencedBookmark -> AdaptiveSequencedBookmarkCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type private AdaptiveSequencedBookmarkLoadedBookmark(Item : SequencedBookmarkModel) =
    let _Item_ = AdaptiveSequencedBookmarkModel(Item)
    let mutable __Item = Item
    member __.Update(Item : SequencedBookmarkModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SequencedBookmarkModel>.ShallowEquals(Item, __Item))) then
            __Item <- Item
            _Item_.Update(Item)
    member __.Item = _Item_
    interface AdaptiveSequencedBookmarkCase with
        member x.Update(value : SequencedBookmark) =
            match value with
            | SequencedBookmark.LoadedBookmark(Item) ->
                x.Update(Item)
                x :> AdaptiveSequencedBookmarkCase
            | SequencedBookmark.NotYetLoaded(Item) -> AdaptiveSequencedBookmarkNotYetLoaded(Item) :> AdaptiveSequencedBookmarkCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type private AdaptiveSequencedBookmarkNotYetLoaded(Item : UnloadedSequencedBookmark) =
    let _Item_ = AdaptiveUnloadedSequencedBookmark(Item)
    let mutable __Item = Item
    member __.Update(Item : UnloadedSequencedBookmark) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<UnloadedSequencedBookmark>.ShallowEquals(Item, __Item))) then
            __Item <- Item
            _Item_.Update(Item)
    member __.Item = _Item_
    interface AdaptiveSequencedBookmarkCase with
        member x.Update(value : SequencedBookmark) =
            match value with
            | SequencedBookmark.LoadedBookmark(Item) -> AdaptiveSequencedBookmarkLoadedBookmark(Item) :> AdaptiveSequencedBookmarkCase
            | SequencedBookmark.NotYetLoaded(Item) ->
                x.Update(Item)
                x :> AdaptiveSequencedBookmarkCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSequencedBookmark(value : SequencedBookmark) =
    inherit Adaptify.AdaptiveValue<AdaptiveSequencedBookmarkCase>()
    let mutable __value = value
    let mutable __current =
        match value with
        | SequencedBookmark.LoadedBookmark(Item) -> AdaptiveSequencedBookmarkLoadedBookmark(Item) :> AdaptiveSequencedBookmarkCase
        | SequencedBookmark.NotYetLoaded(Item) -> AdaptiveSequencedBookmarkNotYetLoaded(Item) :> AdaptiveSequencedBookmarkCase
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (t : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member CreateAdaptiveCase(value : SequencedBookmark) =
        match value with
        | SequencedBookmark.LoadedBookmark(Item) -> AdaptiveSequencedBookmarkLoadedBookmark(Item) :> AdaptiveSequencedBookmarkCase
        | SequencedBookmark.NotYetLoaded(Item) -> AdaptiveSequencedBookmarkNotYetLoaded(Item) :> AdaptiveSequencedBookmarkCase
    static member Create(value : SequencedBookmark) = AdaptiveSequencedBookmark(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SequencedBookmark) -> AdaptiveSequencedBookmark(value)) (fun (adaptive : AdaptiveSequencedBookmark) (value : SequencedBookmark) -> adaptive.Update(value))
    member __.Current = __adaptive
    member __.Update(value : SequencedBookmark) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SequencedBookmark>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            let __n = __current.Update(value)
            if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AdaptiveSequencedBookmarkCase>.ShallowEquals(__n, __current))) then
                __current <- __n
                __.MarkOutdated()
    override __.Compute(t : FSharp.Data.Adaptive.AdaptiveToken) = __current
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AdaptiveSequencedBookmark = 
    let (|AdaptiveLoadedBookmark|AdaptiveNotYetLoaded|) (value : AdaptiveSequencedBookmarkCase) =
        match value with
        | (:? AdaptiveSequencedBookmarkLoadedBookmark as loadedbookmark) -> AdaptiveLoadedBookmark(loadedbookmark.Item)
        | (:? AdaptiveSequencedBookmarkNotYetLoaded as notyetloaded) -> AdaptiveNotYetLoaded(notyetloaded.Item)
        | _ -> failwith "unreachable"
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveAnimationSettings(value : AnimationSettings) =
    let _globalDuration_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.globalDuration)
    let _useGlobalAnimation_ = FSharp.Data.Adaptive.cval(value.useGlobalAnimation)
    let _loopMode_ = FSharp.Data.Adaptive.cval(value.loopMode)
    let _useEasing_ = FSharp.Data.Adaptive.cval(value.useEasing)
    let _applyStateOnSelect_ = FSharp.Data.Adaptive.cval(value.applyStateOnSelect)
    let _smoothPath_ = FSharp.Data.Adaptive.cval(value.smoothPath)
    let _smoothingFactor_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.smoothingFactor)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : AnimationSettings) = AdaptiveAnimationSettings(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : AnimationSettings) -> AdaptiveAnimationSettings(value)) (fun (adaptive : AdaptiveAnimationSettings) (value : AnimationSettings) -> adaptive.Update(value))
    member __.Update(value : AnimationSettings) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AnimationSettings>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _globalDuration_.Update(value.globalDuration)
            _useGlobalAnimation_.Value <- value.useGlobalAnimation
            _loopMode_.Value <- value.loopMode
            _useEasing_.Value <- value.useEasing
            _applyStateOnSelect_.Value <- value.applyStateOnSelect
            _smoothPath_.Value <- value.smoothPath
            _smoothingFactor_.Update(value.smoothingFactor)
    member __.Current = __adaptive
    member __.globalDuration = _globalDuration_
    member __.useGlobalAnimation = _useGlobalAnimation_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.loopMode = _loopMode_ :> FSharp.Data.Adaptive.aval<AnimationLoopMode>
    member __.useEasing = _useEasing_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.applyStateOnSelect = _applyStateOnSelect_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.smoothPath = _smoothPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.smoothingFactor = _smoothingFactor_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AnimationSettingsLenses = 
    type AnimationSettings with
        static member globalDuration_ = ((fun (self : AnimationSettings) -> self.globalDuration), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : AnimationSettings) -> { self with globalDuration = value }))
        static member useGlobalAnimation_ = ((fun (self : AnimationSettings) -> self.useGlobalAnimation), (fun (value : Microsoft.FSharp.Core.bool) (self : AnimationSettings) -> { self with useGlobalAnimation = value }))
        static member loopMode_ = ((fun (self : AnimationSettings) -> self.loopMode), (fun (value : AnimationLoopMode) (self : AnimationSettings) -> { self with loopMode = value }))
        static member useEasing_ = ((fun (self : AnimationSettings) -> self.useEasing), (fun (value : Microsoft.FSharp.Core.bool) (self : AnimationSettings) -> { self with useEasing = value }))
        static member applyStateOnSelect_ = ((fun (self : AnimationSettings) -> self.applyStateOnSelect), (fun (value : Microsoft.FSharp.Core.bool) (self : AnimationSettings) -> { self with applyStateOnSelect = value }))
        static member smoothPath_ = ((fun (self : AnimationSettings) -> self.smoothPath), (fun (value : Microsoft.FSharp.Core.bool) (self : AnimationSettings) -> { self with smoothPath = value }))
        static member smoothingFactor_ = ((fun (self : AnimationSettings) -> self.smoothingFactor), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : AnimationSettings) -> { self with smoothingFactor = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSequencedBookmarks(value : SequencedBookmarks) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _bookmarks_ = FSharp.Data.Traceable.ChangeableModelMap(value.bookmarks, (fun (v : SequencedBookmark) -> AdaptiveSequencedBookmark.CreateAdaptiveCase(v)), (fun (m : AdaptiveSequencedBookmarkCase) (v : SequencedBookmark) -> m.Update(v)), (fun (m : AdaptiveSequencedBookmarkCase) -> m))
    let _poseDataPath_ = FSharp.Data.Adaptive.cval(value.poseDataPath)
    let _savedSceneState_ = FSharp.Data.Adaptive.cval(value.savedSceneState)
    let _orderList_ = FSharp.Data.Adaptive.cval(value.orderList)
    let _selectedBookmark_ = FSharp.Data.Adaptive.cval(value.selectedBookmark)
    let _animationSettings_ = AdaptiveAnimationSettings(value.animationSettings)
    let _lastSavedBookmark_ = FSharp.Data.Adaptive.cval(value.lastSavedBookmark)
    let _savedTimeSteps_ = FSharp.Data.Adaptive.cval(value.savedTimeSteps)
    let _isRecording_ = FSharp.Data.Adaptive.cval(value.isRecording)
    let _generateOnStop_ = FSharp.Data.Adaptive.cval(value.generateOnStop)
    let _isGenerating_ = FSharp.Data.Adaptive.cval(value.isGenerating)
    let _isCancelled_ = FSharp.Data.Adaptive.cval(value.isCancelled)
    let _resolutionX_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.resolutionX)
    let _resolutionY_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.resolutionY)
    let _debug_ = FSharp.Data.Adaptive.cval(value.debug)
    let _currentFps_ = FSharp.Data.Adaptive.cval(value.currentFps)
    let _lastStart_ = FSharp.Data.Adaptive.cval(value.lastStart)
    let _outputPath_ = FSharp.Data.Adaptive.cval(value.outputPath)
    let _fpsSetting_ = FSharp.Data.Adaptive.cval(value.fpsSetting)
    let _updateJsonBeforeRendering_ = FSharp.Data.Adaptive.cval(value.updateJsonBeforeRendering)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SequencedBookmarks) = AdaptiveSequencedBookmarks(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SequencedBookmarks) -> AdaptiveSequencedBookmarks(value)) (fun (adaptive : AdaptiveSequencedBookmarks) (value : SequencedBookmarks) -> adaptive.Update(value))
    member __.Update(value : SequencedBookmarks) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SequencedBookmarks>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _bookmarks_.Update(value.bookmarks)
            _poseDataPath_.Value <- value.poseDataPath
            _savedSceneState_.Value <- value.savedSceneState
            _orderList_.Value <- value.orderList
            _selectedBookmark_.Value <- value.selectedBookmark
            _animationSettings_.Update(value.animationSettings)
            _lastSavedBookmark_.Value <- value.lastSavedBookmark
            _savedTimeSteps_.Value <- value.savedTimeSteps
            _isRecording_.Value <- value.isRecording
            _generateOnStop_.Value <- value.generateOnStop
            _isGenerating_.Value <- value.isGenerating
            _isCancelled_.Value <- value.isCancelled
            _resolutionX_.Update(value.resolutionX)
            _resolutionY_.Update(value.resolutionY)
            _debug_.Value <- value.debug
            _currentFps_.Value <- value.currentFps
            _lastStart_.Value <- value.lastStart
            _outputPath_.Value <- value.outputPath
            _fpsSetting_.Value <- value.fpsSetting
            _updateJsonBeforeRendering_.Value <- value.updateJsonBeforeRendering
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.bookmarks = _bookmarks_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveSequencedBookmarkCase>
    member __.poseDataPath = _poseDataPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.savedSceneState = _savedSceneState_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<SceneState>>
    member __.orderList = _orderList_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<System.Guid>>
    member __.selectedBookmark = _selectedBookmark_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
    member __.animationSettings = _animationSettings_
    member __.lastSavedBookmark = _lastSavedBookmark_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.Guid>>
    member __.savedTimeSteps = _savedTimeSteps_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<AnimationTimeStep>>
    member __.isRecording = _isRecording_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.generateOnStop = _generateOnStop_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isGenerating = _isGenerating_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isCancelled = _isCancelled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.resolutionX = _resolutionX_
    member __.resolutionY = _resolutionY_
    member __.debug = _debug_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.currentFps = _currentFps_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>>
    member __.lastStart = _lastStart_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.TimeSpan>>
    member __.outputPath = _outputPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.fpsSetting = _fpsSetting_ :> FSharp.Data.Adaptive.aval<FPSSetting>
    member __.snapshotThreads = __value.snapshotThreads
    member __.updateJsonBeforeRendering = _updateJsonBeforeRendering_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SequencedBookmarksLenses = 
    type SequencedBookmarks with
        static member version_ = ((fun (self : SequencedBookmarks) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : SequencedBookmarks) -> { self with version = value }))
        static member bookmarks_ = ((fun (self : SequencedBookmarks) -> self.bookmarks), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, SequencedBookmark>) (self : SequencedBookmarks) -> { self with bookmarks = value }))
        static member poseDataPath_ = ((fun (self : SequencedBookmarks) -> self.poseDataPath), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : SequencedBookmarks) -> { self with poseDataPath = value }))
        static member savedSceneState_ = ((fun (self : SequencedBookmarks) -> self.savedSceneState), (fun (value : Microsoft.FSharp.Core.Option<SceneState>) (self : SequencedBookmarks) -> { self with savedSceneState = value }))
        static member orderList_ = ((fun (self : SequencedBookmarks) -> self.orderList), (fun (value : Microsoft.FSharp.Collections.List<System.Guid>) (self : SequencedBookmarks) -> { self with orderList = value }))
        static member selectedBookmark_ = ((fun (self : SequencedBookmarks) -> self.selectedBookmark), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : SequencedBookmarks) -> { self with selectedBookmark = value }))
        static member animationSettings_ = ((fun (self : SequencedBookmarks) -> self.animationSettings), (fun (value : AnimationSettings) (self : SequencedBookmarks) -> { self with animationSettings = value }))
        static member lastSavedBookmark_ = ((fun (self : SequencedBookmarks) -> self.lastSavedBookmark), (fun (value : Microsoft.FSharp.Core.option<System.Guid>) (self : SequencedBookmarks) -> { self with lastSavedBookmark = value }))
        static member savedTimeSteps_ = ((fun (self : SequencedBookmarks) -> self.savedTimeSteps), (fun (value : Microsoft.FSharp.Collections.list<AnimationTimeStep>) (self : SequencedBookmarks) -> { self with savedTimeSteps = value }))
        static member isRecording_ = ((fun (self : SequencedBookmarks) -> self.isRecording), (fun (value : Microsoft.FSharp.Core.bool) (self : SequencedBookmarks) -> { self with isRecording = value }))
        static member generateOnStop_ = ((fun (self : SequencedBookmarks) -> self.generateOnStop), (fun (value : Microsoft.FSharp.Core.bool) (self : SequencedBookmarks) -> { self with generateOnStop = value }))
        static member isGenerating_ = ((fun (self : SequencedBookmarks) -> self.isGenerating), (fun (value : Microsoft.FSharp.Core.bool) (self : SequencedBookmarks) -> { self with isGenerating = value }))
        static member isCancelled_ = ((fun (self : SequencedBookmarks) -> self.isCancelled), (fun (value : Microsoft.FSharp.Core.bool) (self : SequencedBookmarks) -> { self with isCancelled = value }))
        static member resolutionX_ = ((fun (self : SequencedBookmarks) -> self.resolutionX), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : SequencedBookmarks) -> { self with resolutionX = value }))
        static member resolutionY_ = ((fun (self : SequencedBookmarks) -> self.resolutionY), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : SequencedBookmarks) -> { self with resolutionY = value }))
        static member debug_ = ((fun (self : SequencedBookmarks) -> self.debug), (fun (value : Microsoft.FSharp.Core.bool) (self : SequencedBookmarks) -> { self with debug = value }))
        static member currentFps_ = ((fun (self : SequencedBookmarks) -> self.currentFps), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>) (self : SequencedBookmarks) -> { self with currentFps = value }))
        static member lastStart_ = ((fun (self : SequencedBookmarks) -> self.lastStart), (fun (value : Microsoft.FSharp.Core.option<System.TimeSpan>) (self : SequencedBookmarks) -> { self with lastStart = value }))
        static member outputPath_ = ((fun (self : SequencedBookmarks) -> self.outputPath), (fun (value : Microsoft.FSharp.Core.string) (self : SequencedBookmarks) -> { self with outputPath = value }))
        static member fpsSetting_ = ((fun (self : SequencedBookmarks) -> self.fpsSetting), (fun (value : FPSSetting) (self : SequencedBookmarks) -> { self with fpsSetting = value }))
        static member snapshotThreads_ = ((fun (self : SequencedBookmarks) -> self.snapshotThreads), (fun (value : FSharp.Data.Adaptive.ThreadPool<SequencedBookmarksAction>) (self : SequencedBookmarks) -> { self with snapshotThreads = value }))
        static member updateJsonBeforeRendering_ = ((fun (self : SequencedBookmarks) -> self.updateJsonBeforeRendering), (fun (value : Microsoft.FSharp.Core.bool) (self : SequencedBookmarks) -> { self with updateJsonBeforeRendering = value }))

