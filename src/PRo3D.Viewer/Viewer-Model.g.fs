//8151f96a-5dbc-13a5-2c65-d841064239f1
//3d22f19c-4cf3-5039-c9bf-8c00877e0281
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Viewer

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Viewer
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveScene(value : Scene) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _cameraView_ = FSharp.Data.Adaptive.cval(value.cameraView)
    let _navigationMode_ = FSharp.Data.Adaptive.cval(value.navigationMode)
    let _exploreCenter_ = FSharp.Data.Adaptive.cval(value.exploreCenter)
    let _interaction_ = FSharp.Data.Adaptive.cval(value.interaction)
    let _surfacesModel_ = PRo3D.Core.AdaptiveSurfaceModel(value.surfacesModel)
    let _config_ = PRo3D.Core.AdaptiveViewConfigModel(value.config)
    let _scenePath_ = FSharp.Data.Adaptive.cval(value.scenePath)
    let _referenceSystem_ = PRo3D.Core.AdaptiveReferenceSystem(value.referenceSystem)
    let _bookmarks_ = PRo3D.Core.AdaptiveGroupsModel(value.bookmarks)
    let _scaleBars_ = PRo3D.Core.AdaptiveScaleBarsModel(value.scaleBars)
    let _traverses_ = PRo3D.Core.AdaptiveTraverseModel(value.traverses)
    let _viewPlans_ = PRo3D.SimulatedViews.AdaptiveViewPlanModel(value.viewPlans)
    let _dockConfig_ = FSharp.Data.Adaptive.cval(value.dockConfig)
    let _closedPages_ = FSharp.Data.Adaptive.cval(value.closedPages)
    let _firstImport_ = FSharp.Data.Adaptive.cval(value.firstImport)
    let _userFeedback_ = FSharp.Data.Adaptive.cval(value.userFeedback)
    let _feedbackThreads_ = FSharp.Data.Adaptive.cval(value.feedbackThreads)
    let _comparisonApp_ = PRo3D.Comparison.AdaptiveComparisonApp(value.comparisonApp)
    let _sceneObjectsModel_ = PRo3D.Core.AdaptiveSceneObjectsModel(value.sceneObjectsModel)
    let _geologicSurfacesModel_ = PRo3D.Core.AdaptiveGeologicSurfacesModel(value.geologicSurfacesModel)
    let _sequencedBookmarks_ = PRo3D.Core.SequencedBookmarks.AdaptiveSequencedBookmarks(value.sequencedBookmarks)
    let _screenshotModel_ = PRo3D.SimulatedViews.AdaptiveScreenshotModel(value.screenshotModel)
    let _gisApp_ = PRo3D.Core.Gis.AdaptiveGisApp(value.gisApp)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Scene) = AdaptiveScene(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Scene) -> AdaptiveScene(value)) (fun (adaptive : AdaptiveScene) (value : Scene) -> adaptive.Update(value))
    member __.Update(value : Scene) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Scene>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _cameraView_.Value <- value.cameraView
            _navigationMode_.Value <- value.navigationMode
            _exploreCenter_.Value <- value.exploreCenter
            _interaction_.Value <- value.interaction
            _surfacesModel_.Update(value.surfacesModel)
            _config_.Update(value.config)
            _scenePath_.Value <- value.scenePath
            _referenceSystem_.Update(value.referenceSystem)
            _bookmarks_.Update(value.bookmarks)
            _scaleBars_.Update(value.scaleBars)
            _traverses_.Update(value.traverses)
            _viewPlans_.Update(value.viewPlans)
            _dockConfig_.Value <- value.dockConfig
            _closedPages_.Value <- value.closedPages
            _firstImport_.Value <- value.firstImport
            _userFeedback_.Value <- value.userFeedback
            _feedbackThreads_.Value <- value.feedbackThreads
            _comparisonApp_.Update(value.comparisonApp)
            _sceneObjectsModel_.Update(value.sceneObjectsModel)
            _geologicSurfacesModel_.Update(value.geologicSurfacesModel)
            _sequencedBookmarks_.Update(value.sequencedBookmarks)
            _screenshotModel_.Update(value.screenshotModel)
            _gisApp_.Update(value.gisApp)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.cameraView = _cameraView_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.navigationMode = _navigationMode_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.NavigationMode>
    member __.exploreCenter = _exploreCenter_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.interaction = _interaction_ :> FSharp.Data.Adaptive.aval<PRo3D.InteractionMode>
    member __.surfacesModel = _surfacesModel_
    member __.config = _config_
    member __.scenePath = _scenePath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>>
    member __.referenceSystem = _referenceSystem_
    member __.bookmarks = _bookmarks_
    member __.scaleBars = _scaleBars_
    member __.traverses = _traverses_
    member __.viewPlans = _viewPlans_
    member __.dockConfig = _dockConfig_ :> FSharp.Data.Adaptive.aval<Aardvark.UI.Primitives.DockConfig>
    member __.closedPages = _closedPages_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Aardvark.UI.Primitives.DockElement>>
    member __.firstImport = _firstImport_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.userFeedback = _userFeedback_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.feedbackThreads = _feedbackThreads_ :> FSharp.Data.Adaptive.aval<FSharp.Data.Adaptive.ThreadPool<ViewerAction>>
    member __.comparisonApp = _comparisonApp_
    member __.sceneObjectsModel = _sceneObjectsModel_
    member __.geologicSurfacesModel = _geologicSurfacesModel_
    member __.sequencedBookmarks = _sequencedBookmarks_
    member __.screenshotModel = _screenshotModel_
    member __.gisApp = _gisApp_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SceneLenses = 
    type Scene with
        static member version_ = ((fun (self : Scene) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Scene) -> { self with version = value }))
        static member cameraView_ = ((fun (self : Scene) -> self.cameraView), (fun (value : Aardvark.Rendering.CameraView) (self : Scene) -> { self with cameraView = value }))
        static member navigationMode_ = ((fun (self : Scene) -> self.navigationMode), (fun (value : PRo3D.Base.NavigationMode) (self : Scene) -> { self with navigationMode = value }))
        static member exploreCenter_ = ((fun (self : Scene) -> self.exploreCenter), (fun (value : Aardvark.Base.V3d) (self : Scene) -> { self with exploreCenter = value }))
        static member interaction_ = ((fun (self : Scene) -> self.interaction), (fun (value : PRo3D.InteractionMode) (self : Scene) -> { self with interaction = value }))
        static member surfacesModel_ = ((fun (self : Scene) -> self.surfacesModel), (fun (value : PRo3D.Core.SurfaceModel) (self : Scene) -> { self with surfacesModel = value }))
        static member config_ = ((fun (self : Scene) -> self.config), (fun (value : PRo3D.Core.ViewConfigModel) (self : Scene) -> { self with config = value }))
        static member scenePath_ = ((fun (self : Scene) -> self.scenePath), (fun (value : Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>) (self : Scene) -> { self with scenePath = value }))
        static member referenceSystem_ = ((fun (self : Scene) -> self.referenceSystem), (fun (value : PRo3D.Core.ReferenceSystem) (self : Scene) -> { self with referenceSystem = value }))
        static member bookmarks_ = ((fun (self : Scene) -> self.bookmarks), (fun (value : PRo3D.Core.GroupsModel) (self : Scene) -> { self with bookmarks = value }))
        static member scaleBars_ = ((fun (self : Scene) -> self.scaleBars), (fun (value : PRo3D.Core.ScaleBarsModel) (self : Scene) -> { self with scaleBars = value }))
        static member traverses_ = ((fun (self : Scene) -> self.traverses), (fun (value : PRo3D.Core.TraverseModel) (self : Scene) -> { self with traverses = value }))
        static member viewPlans_ = ((fun (self : Scene) -> self.viewPlans), (fun (value : PRo3D.SimulatedViews.ViewPlanModel) (self : Scene) -> { self with viewPlans = value }))
        static member dockConfig_ = ((fun (self : Scene) -> self.dockConfig), (fun (value : Aardvark.UI.Primitives.DockConfig) (self : Scene) -> { self with dockConfig = value }))
        static member closedPages_ = ((fun (self : Scene) -> self.closedPages), (fun (value : Microsoft.FSharp.Collections.list<Aardvark.UI.Primitives.DockElement>) (self : Scene) -> { self with closedPages = value }))
        static member firstImport_ = ((fun (self : Scene) -> self.firstImport), (fun (value : Microsoft.FSharp.Core.bool) (self : Scene) -> { self with firstImport = value }))
        static member userFeedback_ = ((fun (self : Scene) -> self.userFeedback), (fun (value : Microsoft.FSharp.Core.string) (self : Scene) -> { self with userFeedback = value }))
        static member feedbackThreads_ = ((fun (self : Scene) -> self.feedbackThreads), (fun (value : FSharp.Data.Adaptive.ThreadPool<ViewerAction>) (self : Scene) -> { self with feedbackThreads = value }))
        static member comparisonApp_ = ((fun (self : Scene) -> self.comparisonApp), (fun (value : PRo3D.Comparison.ComparisonApp) (self : Scene) -> { self with comparisonApp = value }))
        static member sceneObjectsModel_ = ((fun (self : Scene) -> self.sceneObjectsModel), (fun (value : PRo3D.Core.SceneObjectsModel) (self : Scene) -> { self with sceneObjectsModel = value }))
        static member geologicSurfacesModel_ = ((fun (self : Scene) -> self.geologicSurfacesModel), (fun (value : PRo3D.Core.GeologicSurfacesModel) (self : Scene) -> { self with geologicSurfacesModel = value }))
        static member sequencedBookmarks_ = ((fun (self : Scene) -> self.sequencedBookmarks), (fun (value : PRo3D.Core.SequencedBookmarks.SequencedBookmarks) (self : Scene) -> { self with sequencedBookmarks = value }))
        static member screenshotModel_ = ((fun (self : Scene) -> self.screenshotModel), (fun (value : PRo3D.SimulatedViews.ScreenshotModel) (self : Scene) -> { self with screenshotModel = value }))
        static member gisApp_ = ((fun (self : Scene) -> self.gisApp), (fun (value : PRo3D.Core.Gis.GisApp) (self : Scene) -> { self with gisApp = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRecent(value : Recent) =
    let _recentScenes_ = FSharp.Data.Adaptive.cval(value.recentScenes)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Recent) = AdaptiveRecent(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Recent) -> AdaptiveRecent(value)) (fun (adaptive : AdaptiveRecent) (value : Recent) -> adaptive.Update(value))
    member __.Update(value : Recent) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Recent>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _recentScenes_.Value <- value.recentScenes
    member __.Current = __adaptive
    member __.recentScenes = _recentScenes_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<SceneHandle>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RecentLenses = 
    type Recent with
        static member recentScenes_ = ((fun (self : Recent) -> self.recentScenes), (fun (value : Microsoft.FSharp.Collections.list<SceneHandle>) (self : Recent) -> { self with recentScenes = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveModel(value : Model) =
    let _viewerVersion_ = FSharp.Data.Adaptive.cval(value.viewerVersion)
    let _startupArgs_ = FSharp.Data.Adaptive.cval(value.startupArgs)
    let _dashboardMode_ = FSharp.Data.Adaptive.cval(value.dashboardMode)
    let _scene_ = AdaptiveScene(value.scene)
    let _drawing_ = PRo3D.Core.Drawing.AdaptiveDrawingModel(value.drawing)
    let _interaction_ = FSharp.Data.Adaptive.cval(value.interaction)
    let _recent_ = AdaptiveRecent(value.recent)
    let _waypoints_ = FSharp.Data.Adaptive.clist(value.waypoints)
    let _aspect_ = FSharp.Data.Adaptive.cval(value.aspect)
    let _trafoKind_ = FSharp.Data.Adaptive.cval(value.trafoKind)
    let _trafoMode_ = FSharp.Data.Adaptive.cval(value.trafoMode)
    let _tabMenu_ = FSharp.Data.Adaptive.cval(value.tabMenu)
    let _viewerMode_ = FSharp.Data.Adaptive.cval(value.viewerMode)
    let _animations_ = Aardvark.UI.Animation.Deprecated.AdaptiveAnimationModel(value.animations)
    let _messagingMailbox_ = FSharp.Data.Adaptive.cval(value.messagingMailbox)
    let _mailboxState_ = FSharp.Data.Adaptive.cval(value.mailboxState)
    let _navigation_ = PRo3D.Base.AdaptiveNavigationModel(value.navigation)
    let _properties_ = FSharp.Data.Adaptive.cval(value.properties)
    let _multiSelectBox_ = FSharp.Data.Adaptive.cval(value.multiSelectBox)
    let _shiftFlag_ = FSharp.Data.Adaptive.cval(value.shiftFlag)
    let _picking_ = FSharp.Data.Adaptive.cval(value.picking)
    let _pivotType_ = FSharp.Data.Adaptive.cval(value.pivotType)
    let _ctrlFlag_ = FSharp.Data.Adaptive.cval(value.ctrlFlag)
    let _frustum_ = FSharp.Data.Adaptive.cval(value.frustum)
    let _viewPortSizes_ = FSharp.Data.Adaptive.cmap(value.viewPortSizes)
    let _overlayFrustum_ = FSharp.Data.Adaptive.cval(value.overlayFrustum)
    let _scaleBarsDrawing_ = PRo3D.Core.AdaptiveScaleBarDrawing(value.scaleBarsDrawing)
    let _past_ = FSharp.Data.Adaptive.cval(value.past)
    let _future_ = FSharp.Data.Adaptive.cval(value.future)
    let _footPrint_ = PRo3D.SimulatedViews.AdaptiveFootPrint(value.footPrint)
    let _snapshotThreads_ = FSharp.Data.Adaptive.cval(value.snapshotThreads)
    let _showExplorationPoint_ = FSharp.Data.Adaptive.cval(value.showExplorationPoint)
    let _heighValidation_ = PRo3D.Core.AdaptiveHeightValidatorModel(value.heighValidation)
    let _filterTexture_ = FSharp.Data.Adaptive.cval(value.filterTexture)
    let _numberOfSamples_ = FSharp.Data.Adaptive.cval(value.numberOfSamples)
    let _renderingUrl_ = FSharp.Data.Adaptive.cval(value.renderingUrl)
    let _screenshotDirectory_ = FSharp.Data.Adaptive.cval(value.screenshotDirectory)
    let _provenanceModel_ = AdaptiveProvenanceModel(value.provenanceModel)
    let _surfaceIntersection_ = FSharp.Data.Adaptive.cval(value.surfaceIntersection)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Model) = AdaptiveModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Model) -> AdaptiveModel(value)) (fun (adaptive : AdaptiveModel) (value : Model) -> adaptive.Update(value))
    member __.Update(value : Model) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Model>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _viewerVersion_.Value <- value.viewerVersion
            _startupArgs_.Value <- value.startupArgs
            _dashboardMode_.Value <- value.dashboardMode
            _scene_.Update(value.scene)
            _drawing_.Update(value.drawing)
            _interaction_.Value <- value.interaction
            _recent_.Update(value.recent)
            _waypoints_.Value <- value.waypoints
            _aspect_.Value <- value.aspect
            _trafoKind_.Value <- value.trafoKind
            _trafoMode_.Value <- value.trafoMode
            _tabMenu_.Value <- value.tabMenu
            _viewerMode_.Value <- value.viewerMode
            _animations_.Update(value.animations)
            _messagingMailbox_.Value <- value.messagingMailbox
            _mailboxState_.Value <- value.mailboxState
            _navigation_.Update(value.navigation)
            _properties_.Value <- value.properties
            _multiSelectBox_.Value <- value.multiSelectBox
            _shiftFlag_.Value <- value.shiftFlag
            _picking_.Value <- value.picking
            _pivotType_.Value <- value.pivotType
            _ctrlFlag_.Value <- value.ctrlFlag
            _frustum_.Value <- value.frustum
            _viewPortSizes_.Value <- value.viewPortSizes
            _overlayFrustum_.Value <- value.overlayFrustum
            _scaleBarsDrawing_.Update(value.scaleBarsDrawing)
            _past_.Value <- value.past
            _future_.Value <- value.future
            _footPrint_.Update(value.footPrint)
            _snapshotThreads_.Value <- value.snapshotThreads
            _showExplorationPoint_.Value <- value.showExplorationPoint
            _heighValidation_.Update(value.heighValidation)
            _filterTexture_.Value <- value.filterTexture
            _numberOfSamples_.Value <- value.numberOfSamples
            _renderingUrl_.Value <- value.renderingUrl
            _screenshotDirectory_.Value <- value.screenshotDirectory
            _provenanceModel_.Update(value.provenanceModel)
            _surfaceIntersection_.Value <- value.surfaceIntersection
    member __.Current = __adaptive
    member __.viewerVersion = _viewerVersion_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.startupArgs = _startupArgs_ :> FSharp.Data.Adaptive.aval<PRo3D.StartupArgs>
    member __.dashboardMode = _dashboardMode_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.scene = _scene_
    member __.drawing = _drawing_
    member __.interaction = _interaction_ :> FSharp.Data.Adaptive.aval<PRo3D.Core.Interactions>
    member __.recent = _recent_
    member __.waypoints = _waypoints_ :> FSharp.Data.Adaptive.alist<WayPoint>
    member __.aspect = _aspect_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.double>
    member __.trafoKind = _trafoKind_ :> FSharp.Data.Adaptive.aval<Aardvark.UI.Trafos.TrafoKind>
    member __.trafoMode = _trafoMode_ :> FSharp.Data.Adaptive.aval<Aardvark.UI.Trafos.TrafoMode>
    member __.tabMenu = _tabMenu_ :> FSharp.Data.Adaptive.aval<TabMenu>
    member __.viewerMode = _viewerMode_ :> FSharp.Data.Adaptive.aval<PRo3D.ViewerMode>
    member __.animations = _animations_
    member __.messagingMailbox = _messagingMailbox_ :> FSharp.Data.Adaptive.aval<MessagingMailbox>
    member __.mailboxState = _mailboxState_ :> FSharp.Data.Adaptive.aval<MailboxState>
    member __.navigation = _navigation_
    member __.properties = _properties_ :> FSharp.Data.Adaptive.aval<Properties>
    member __.multiSelectBox = _multiSelectBox_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<MultiSelectionBox>>
    member __.shiftFlag = _shiftFlag_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.picking = _picking_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.pivotType = _pivotType_ :> FSharp.Data.Adaptive.aval<PickPivot>
    member __.ctrlFlag = _ctrlFlag_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.frustum = _frustum_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.Frustum>
    member __.viewPortSizes = _viewPortSizes_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, Aardvark.Base.V2i>
    member __.overlayFrustum = _overlayFrustum_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Rendering.Frustum>>
    member __.scaleBarsDrawing = _scaleBarsDrawing_
    member __.past = _past_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<PRo3D.Core.Drawing.DrawingModel>>
    member __.future = _future_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<PRo3D.Core.Drawing.DrawingModel>>
    member __.footPrint = _footPrint_
    member __.snapshotThreads = _snapshotThreads_ :> FSharp.Data.Adaptive.aval<FSharp.Data.Adaptive.ThreadPool<ViewerAction>>
    member __.showExplorationPoint = _showExplorationPoint_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.heighValidation = _heighValidation_
    member __.filterTexture = _filterTexture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.numberOfSamples = _numberOfSamples_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.renderingUrl = _renderingUrl_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.screenshotDirectory = _screenshotDirectory_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.animator = __value.animator
    member __.provenanceModel = _provenanceModel_
    member __.surfaceIntersection = _surfaceIntersection_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<SurfaceIntersection>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ModelLenses = 
    type Model with
        static member viewerVersion_ = ((fun (self : Model) -> self.viewerVersion), (fun (value : Microsoft.FSharp.Core.string) (self : Model) -> { self with viewerVersion = value }))
        static member startupArgs_ = ((fun (self : Model) -> self.startupArgs), (fun (value : PRo3D.StartupArgs) (self : Model) -> { self with startupArgs = value }))
        static member dashboardMode_ = ((fun (self : Model) -> self.dashboardMode), (fun (value : Microsoft.FSharp.Core.string) (self : Model) -> { self with dashboardMode = value }))
        static member scene_ = ((fun (self : Model) -> self.scene), (fun (value : Scene) (self : Model) -> { self with scene = value }))
        static member drawing_ = ((fun (self : Model) -> self.drawing), (fun (value : PRo3D.Core.Drawing.DrawingModel) (self : Model) -> { self with drawing = value }))
        static member interaction_ = ((fun (self : Model) -> self.interaction), (fun (value : PRo3D.Core.Interactions) (self : Model) -> { self with interaction = value }))
        static member recent_ = ((fun (self : Model) -> self.recent), (fun (value : Recent) (self : Model) -> { self with recent = value }))
        static member waypoints_ = ((fun (self : Model) -> self.waypoints), (fun (value : FSharp.Data.Adaptive.IndexList<WayPoint>) (self : Model) -> { self with waypoints = value }))
        static member aspect_ = ((fun (self : Model) -> self.aspect), (fun (value : Microsoft.FSharp.Core.double) (self : Model) -> { self with aspect = value }))
        static member trafoKind_ = ((fun (self : Model) -> self.trafoKind), (fun (value : Aardvark.UI.Trafos.TrafoKind) (self : Model) -> { self with trafoKind = value }))
        static member trafoMode_ = ((fun (self : Model) -> self.trafoMode), (fun (value : Aardvark.UI.Trafos.TrafoMode) (self : Model) -> { self with trafoMode = value }))
        static member tabMenu_ = ((fun (self : Model) -> self.tabMenu), (fun (value : TabMenu) (self : Model) -> { self with tabMenu = value }))
        static member viewerMode_ = ((fun (self : Model) -> self.viewerMode), (fun (value : PRo3D.ViewerMode) (self : Model) -> { self with viewerMode = value }))
        static member animations_ = ((fun (self : Model) -> self.animations), (fun (value : Aardvark.UI.Animation.Deprecated.AnimationModel) (self : Model) -> { self with animations = value }))
        static member messagingMailbox_ = ((fun (self : Model) -> self.messagingMailbox), (fun (value : MessagingMailbox) (self : Model) -> { self with messagingMailbox = value }))
        static member mailboxState_ = ((fun (self : Model) -> self.mailboxState), (fun (value : MailboxState) (self : Model) -> { self with mailboxState = value }))
        static member navigation_ = ((fun (self : Model) -> self.navigation), (fun (value : PRo3D.Base.NavigationModel) (self : Model) -> { self with navigation = value }))
        static member properties_ = ((fun (self : Model) -> self.properties), (fun (value : Properties) (self : Model) -> { self with properties = value }))
        static member multiSelectBox_ = ((fun (self : Model) -> self.multiSelectBox), (fun (value : Microsoft.FSharp.Core.Option<MultiSelectionBox>) (self : Model) -> { self with multiSelectBox = value }))
        static member shiftFlag_ = ((fun (self : Model) -> self.shiftFlag), (fun (value : Microsoft.FSharp.Core.bool) (self : Model) -> { self with shiftFlag = value }))
        static member picking_ = ((fun (self : Model) -> self.picking), (fun (value : Microsoft.FSharp.Core.bool) (self : Model) -> { self with picking = value }))
        static member pivotType_ = ((fun (self : Model) -> self.pivotType), (fun (value : PickPivot) (self : Model) -> { self with pivotType = value }))
        static member ctrlFlag_ = ((fun (self : Model) -> self.ctrlFlag), (fun (value : Microsoft.FSharp.Core.bool) (self : Model) -> { self with ctrlFlag = value }))
        static member frustum_ = ((fun (self : Model) -> self.frustum), (fun (value : Aardvark.Rendering.Frustum) (self : Model) -> { self with frustum = value }))
        static member viewPortSizes_ = ((fun (self : Model) -> self.viewPortSizes), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Aardvark.Base.V2i>) (self : Model) -> { self with viewPortSizes = value }))
        static member overlayFrustum_ = ((fun (self : Model) -> self.overlayFrustum), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Rendering.Frustum>) (self : Model) -> { self with overlayFrustum = value }))
        static member scaleBarsDrawing_ = ((fun (self : Model) -> self.scaleBarsDrawing), (fun (value : PRo3D.Core.ScaleBarDrawing) (self : Model) -> { self with scaleBarsDrawing = value }))
        static member past_ = ((fun (self : Model) -> self.past), (fun (value : Microsoft.FSharp.Core.Option<PRo3D.Core.Drawing.DrawingModel>) (self : Model) -> { self with past = value }))
        static member future_ = ((fun (self : Model) -> self.future), (fun (value : Microsoft.FSharp.Core.Option<PRo3D.Core.Drawing.DrawingModel>) (self : Model) -> { self with future = value }))
        static member footPrint_ = ((fun (self : Model) -> self.footPrint), (fun (value : PRo3D.SimulatedViews.FootPrint) (self : Model) -> { self with footPrint = value }))
        static member snapshotThreads_ = ((fun (self : Model) -> self.snapshotThreads), (fun (value : FSharp.Data.Adaptive.ThreadPool<ViewerAction>) (self : Model) -> { self with snapshotThreads = value }))
        static member showExplorationPoint_ = ((fun (self : Model) -> self.showExplorationPoint), (fun (value : Microsoft.FSharp.Core.bool) (self : Model) -> { self with showExplorationPoint = value }))
        static member heighValidation_ = ((fun (self : Model) -> self.heighValidation), (fun (value : PRo3D.Core.HeightValidatorModel) (self : Model) -> { self with heighValidation = value }))
        static member filterTexture_ = ((fun (self : Model) -> self.filterTexture), (fun (value : Microsoft.FSharp.Core.bool) (self : Model) -> { self with filterTexture = value }))
        static member numberOfSamples_ = ((fun (self : Model) -> self.numberOfSamples), (fun (value : Microsoft.FSharp.Core.int) (self : Model) -> { self with numberOfSamples = value }))
        static member renderingUrl_ = ((fun (self : Model) -> self.renderingUrl), (fun (value : Microsoft.FSharp.Core.string) (self : Model) -> { self with renderingUrl = value }))
        static member screenshotDirectory_ = ((fun (self : Model) -> self.screenshotDirectory), (fun (value : Microsoft.FSharp.Core.string) (self : Model) -> { self with screenshotDirectory = value }))
        static member animator_ = ((fun (self : Model) -> self.animator), (fun (value : Aardvark.UI.Animation.Animator<Model>) (self : Model) -> { self with animator = value }))
        static member provenanceModel_ = ((fun (self : Model) -> self.provenanceModel), (fun (value : ProvenanceModel) (self : Model) -> { self with provenanceModel = value }))
        static member surfaceIntersection_ = ((fun (self : Model) -> self.surfaceIntersection), (fun (value : Microsoft.FSharp.Core.Option<SurfaceIntersection>) (self : Model) -> { self with surfaceIntersection = value }))

