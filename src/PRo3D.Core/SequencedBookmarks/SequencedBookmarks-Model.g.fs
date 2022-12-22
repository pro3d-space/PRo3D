//8847bf29-731b-1aa9-a4ab-296f596cc199
//79609f7d-e143-1efc-f63c-8bfab5e84c71
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
type AdaptiveSequencedBookmark(value : SequencedBookmark) =
    let mutable _cameraView_ = FSharp.Data.Adaptive.cval(value.cameraView)
    let mutable _name_ = FSharp.Data.Adaptive.cval(value.name)
    let mutable _key_ = FSharp.Data.Adaptive.cval(value.key)
    let _bookmark_ = PRo3D.Core.AdaptiveBookmark(value.bookmark)
    let _sceneState_ = FSharp.Data.Adaptive.cval(value.sceneState)
    let _delay_ = Aardvark.UI.AdaptiveNumericInput(value.delay)
    let _duration_ = Aardvark.UI.AdaptiveNumericInput(value.duration)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SequencedBookmark) = AdaptiveSequencedBookmark(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SequencedBookmark) -> AdaptiveSequencedBookmark(value)) (fun (adaptive : AdaptiveSequencedBookmark) (value : SequencedBookmark) -> adaptive.Update(value))
    member __.Update(value : SequencedBookmark) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SequencedBookmark>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _cameraView_.Value <- value.cameraView
            _name_.Value <- value.name
            _key_.Value <- value.key
            _bookmark_.Update(value.bookmark)
            _sceneState_.Value <- value.sceneState
            _delay_.Update(value.delay)
            _duration_.Update(value.duration)
    member __.Current = __adaptive
    member __.cameraView = _cameraView_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.key = _key_ :> FSharp.Data.Adaptive.aval<System.Guid>
    member __.version = __value.version
    member __.bookmark = _bookmark_
    member __.sceneState = _sceneState_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<SceneState>>
    member __.delay = _delay_
    member __.duration = _duration_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SequencedBookmarkLenses = 
    type SequencedBookmark with
        static member version_ = ((fun (self : SequencedBookmark) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : SequencedBookmark) -> { self with version = value }))
        static member bookmark_ = ((fun (self : SequencedBookmark) -> self.bookmark), (fun (value : PRo3D.Core.Bookmark) (self : SequencedBookmark) -> { self with bookmark = value }))
        static member sceneState_ = ((fun (self : SequencedBookmark) -> self.sceneState), (fun (value : Microsoft.FSharp.Core.option<SceneState>) (self : SequencedBookmark) -> { self with sceneState = value }))
        static member delay_ = ((fun (self : SequencedBookmark) -> self.delay), (fun (value : Aardvark.UI.NumericInput) (self : SequencedBookmark) -> { self with delay = value }))
        static member duration_ = ((fun (self : SequencedBookmark) -> self.duration), (fun (value : Aardvark.UI.NumericInput) (self : SequencedBookmark) -> { self with duration = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveAnimationSettings(value : AnimationSettings) =
    let _globalDuration_ = Aardvark.UI.AdaptiveNumericInput(value.globalDuration)
    let _useGlobalAnimation_ = FSharp.Data.Adaptive.cval(value.useGlobalAnimation)
    let _loopMode_ = FSharp.Data.Adaptive.cval(value.loopMode)
    let _useEasing_ = FSharp.Data.Adaptive.cval(value.useEasing)
    let _applyStateOnSelect_ = FSharp.Data.Adaptive.cval(value.applyStateOnSelect)
    let _smoothPath_ = FSharp.Data.Adaptive.cval(value.smoothPath)
    let _smoothingFactor_ = Aardvark.UI.AdaptiveNumericInput(value.smoothingFactor)
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
        static member globalDuration_ = ((fun (self : AnimationSettings) -> self.globalDuration), (fun (value : Aardvark.UI.NumericInput) (self : AnimationSettings) -> { self with globalDuration = value }))
        static member useGlobalAnimation_ = ((fun (self : AnimationSettings) -> self.useGlobalAnimation), (fun (value : Microsoft.FSharp.Core.bool) (self : AnimationSettings) -> { self with useGlobalAnimation = value }))
        static member loopMode_ = ((fun (self : AnimationSettings) -> self.loopMode), (fun (value : AnimationLoopMode) (self : AnimationSettings) -> { self with loopMode = value }))
        static member useEasing_ = ((fun (self : AnimationSettings) -> self.useEasing), (fun (value : Microsoft.FSharp.Core.bool) (self : AnimationSettings) -> { self with useEasing = value }))
        static member applyStateOnSelect_ = ((fun (self : AnimationSettings) -> self.applyStateOnSelect), (fun (value : Microsoft.FSharp.Core.bool) (self : AnimationSettings) -> { self with applyStateOnSelect = value }))
        static member smoothPath_ = ((fun (self : AnimationSettings) -> self.smoothPath), (fun (value : Microsoft.FSharp.Core.bool) (self : AnimationSettings) -> { self with smoothPath = value }))
        static member smoothingFactor_ = ((fun (self : AnimationSettings) -> self.smoothingFactor), (fun (value : Aardvark.UI.NumericInput) (self : AnimationSettings) -> { self with smoothingFactor = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSequencedBookmarks(value : SequencedBookmarks) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _bookmarks_ =
        let inline __arg2 (m : AdaptiveSequencedBookmark) (v : SequencedBookmark) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.bookmarks, (fun (v : SequencedBookmark) -> AdaptiveSequencedBookmark(v)), __arg2, (fun (m : AdaptiveSequencedBookmark) -> m))
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
    let _resolutionX_ = Aardvark.UI.AdaptiveNumericInput(value.resolutionX)
    let _resolutionY_ = Aardvark.UI.AdaptiveNumericInput(value.resolutionY)
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
    member __.bookmarks = _bookmarks_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveSequencedBookmark>
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
        static member resolutionX_ = ((fun (self : SequencedBookmarks) -> self.resolutionX), (fun (value : Aardvark.UI.NumericInput) (self : SequencedBookmarks) -> { self with resolutionX = value }))
        static member resolutionY_ = ((fun (self : SequencedBookmarks) -> self.resolutionY), (fun (value : Aardvark.UI.NumericInput) (self : SequencedBookmarks) -> { self with resolutionY = value }))
        static member debug_ = ((fun (self : SequencedBookmarks) -> self.debug), (fun (value : Microsoft.FSharp.Core.bool) (self : SequencedBookmarks) -> { self with debug = value }))
        static member currentFps_ = ((fun (self : SequencedBookmarks) -> self.currentFps), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>) (self : SequencedBookmarks) -> { self with currentFps = value }))
        static member lastStart_ = ((fun (self : SequencedBookmarks) -> self.lastStart), (fun (value : Microsoft.FSharp.Core.option<System.TimeSpan>) (self : SequencedBookmarks) -> { self with lastStart = value }))
        static member outputPath_ = ((fun (self : SequencedBookmarks) -> self.outputPath), (fun (value : Microsoft.FSharp.Core.string) (self : SequencedBookmarks) -> { self with outputPath = value }))
        static member fpsSetting_ = ((fun (self : SequencedBookmarks) -> self.fpsSetting), (fun (value : FPSSetting) (self : SequencedBookmarks) -> { self with fpsSetting = value }))
        static member snapshotThreads_ = ((fun (self : SequencedBookmarks) -> self.snapshotThreads), (fun (value : FSharp.Data.Adaptive.ThreadPool<SequencedBookmarksAction>) (self : SequencedBookmarks) -> { self with snapshotThreads = value }))
        static member updateJsonBeforeRendering_ = ((fun (self : SequencedBookmarks) -> self.updateJsonBeforeRendering), (fun (value : Microsoft.FSharp.Core.bool) (self : SequencedBookmarks) -> { self with updateJsonBeforeRendering = value }))

