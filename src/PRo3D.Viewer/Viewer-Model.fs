namespace PRo3D.Viewer

open System
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.UI.Trafos
open Aardvark.UI.Animation
open Aardvark.Rendering

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.SimulatedViews
open PRo3D.Core.Surface
open PRo3D.Navigation2

open PRo3D.Comparison

open Chiron

open Adaptify

open Aether
open Aether.Operators
open PRo3D.Minerva

#nowarn "0686"

type TabMenu = 
    | Surfaces    = 0
    | Annotations = 1
    | Viewplanner = 2
    | Bookmarks   = 3
    | Config      = 4

type BookmarkAction =
    | AddBookmark 
    | ImportBookmarks of list<string>
    | ExportBookmarks of string
    | GroupsMessage   of GroupsAppAction
    | PrintViewParameters of Guid

type PropertyActions =
    | DrawingMessage    of DrawingAction
    | AnnotationMessage of AnnotationProperties.Action

//type CorrelationPanelsMessage = 
//| CorrPlotMessage               of CorrelationPlotAction
//| SemanticAppMessage            of SemanticAction
//| ColourMapMessage              of ColourMap.Action
//| LogPickReferencePlane         of Guid
//| LogAddSelectedPoint           of Guid * V3d
//| LogAddPointToSelected         of Guid * V3d
//| LogCancel
//| LogConfirm
//| LogAssignCrossbeds            of HashSet<Guid>
//| UpdateAnnotations             of HashMap<Guid, PRo3D.Groups.Leaf>
//| ExportLogs                    of string
//| RemoveLastPoint
//| SetContactOfInterest          of HashSet<CorrelationDrawing.AnnotationTypes.ContactId>
//| Nop
//type ScaleToolAction = 
//    | PlaneExtrudeAction of PlaneExtrude.App.Action

type ViewerAction =                
    | DrawingMessage                  of DrawingAction
    | AnnotationGroupsMessageViewer   of GroupsAppAction
    | NavigationMessage               of Navigation.Action
    | AnimationMessage                of AnimationAction
    | ReferenceSystemMessage          of ReferenceSystemAction
    | AnnotationMessage               of AnnotationProperties.Action
    | BookmarkMessage                 of BookmarkAction
    | BookmarkUIMessage               of GroupsAppAction
    | SequencedBookmarkMessage        of SequencedBookmarksAction
    | RoverMessage                    of RoverApp.Action
    | ViewPlanMessage                 of ViewPlanApp.Action
    | DnSColorLegendMessage           of FalseColorLegendApp.Action
    | SceneObjectsMessage             of SceneObjectAction
    | FrustumMessage                  of FrustumProperties.Action
    | SetCamera                       of CameraView        
    | SetCameraAndFrustum             of CameraView * double * double        
    | SetCameraAndFrustum2            of CameraView * Frustum
    | SetRenderViewportSize           of V2i
    | ImportSurface                   of list<string>
    | ImportDiscoveredSurfaces        of list<string>
    | ImportDiscoveredSurfacesThreads of list<string>
    | ImportObject                    of list<string>
    | ImportSceneObject               of list<string>
    | ImportPRo3Dv1Annotations        of list<string>
    | ImportSurfaceTrafo              of list<string>
    | ImportRoverPlacement            of list<string>
    | SwitchViewerMode                of ViewerMode
    | DnSProperties                   of PropertyActions
    | ConfigPropertiesMessage         of ConfigProperties.Action
    | DeleteLast
    | AddSg                           of ISg
    | PickSurface                     of SceneHit*string*bool
    | PickObject                      of V3d*Guid
    | SaveScene                       of string
    | SaveAs                          of string
    | OpenScene                       of list<string>
    | LoadScene                       of string
    | NewScene
    | KeyDown                         of key : Aardvark.Application.Keys
    | KeyUp                           of key : Aardvark.Application.Keys      
    | ResizeMainControl               of V2i * string
    | SetKind                         of TrafoKind
    | SetInteraction                  of Interactions        
    | SetMode                         of TrafoMode
    | TransforAdaptiveSurface                of System.Guid * Trafo3d
    | ImportTrafo                     of list<string>
    | TransformAllSurfaces            of list<SnapshotSurfaceUpdate>
    | RecalculateFarPlane
    | RecalculateNearFarPlane      
    | Translate                       of string * TrafoController.Action
    | Rotate                          of string * TrafoController.Action
    | SurfaceActions                  of SurfaceAppAction
    | MinervaActions                  of PRo3D.Minerva.MinervaAction
    //| ScaleToolAction                 of ScaleToolAction
    | LinkingActions                  of PRo3D.Linking.LinkingAction    
    | SetTabMenu                      of TabMenu
    | OpenSceneFileLocation           of string
    | NoAction                        of string
    | OrientationCube                 of ISg
    | UpdateDockConfig                of DockConfig
    | ChangeDashboardMode             of DashboardMode
    | AddPage                         of DockElement    
    | ToggleOrientationCube
    | UpdateUserFeedback              of string
    | StartImportMessaging            of list<string>
    | Logging                         of string * ViewerAction
    | ThreadsDone                     of string    
    | SnapshotThreadDone             of string
    | OnResize                        of V2i * string
    | StartDragging                   of V2i * MouseButtons
    | Dragging                        of V2i
    | EndDragging                     of V2i * MouseButtons
    //| CorrelationPanelMessage         of CorrelationPanelsMessage
    | MakeSnapshot                    of int*int*string
    | ImportSnapshotData              of list<string>
    | CheckSnapshotsProcess          of string
    | TestHaltonRayCasting            //of list<string>
    | HeightValidation               of HeightValidatorAction
    | ComparisonMessage              of ComparisonAction
    | ScaleBarsDrawingMessage        of ScaleBarDrawingAction
    | ScaleBarsMessage               of ScaleBarsAction
    | GeologicSurfacesMessage        of GeologicSurfaceAction
    | Nop

and MailboxState = {
  events  : list<MailboxAction>
  update  : seq<ViewerAction> -> unit
}
and MailboxAction =
| ViewerAction  of ViewerAction
| InitMailboxState of MailboxState  
| DrawingAction of PRo3D.Core.Drawing.DrawingAction 

[<ModelType>] 
type Scene = {
    version           : int

    cameraView        : CameraView
    navigationMode    : NavigationMode
    exploreCenter     : V3d

    interaction       : InteractionMode
    surfacesModel     : SurfaceModel
    config            : ViewConfigModel
    scenePath         : Option<string>
    referenceSystem   : ReferenceSystem    
    bookmarks         : GroupsModel
    scaleBars         : ScaleBarsModel

    viewPlans         : ViewPlanModel
    dockConfig        : DockConfig
    closedPages       : list<DockElement>
    firstImport       : bool
    userFeedback      : string
    feedbackThreads   : ThreadPool<ViewerAction> 
    comparisonApp     : PRo3D.Comparison.ComparisonApp
    sceneObjectsModel : SceneObjectsModel
    geologicSurfacesModel : GeologicSurfacesModel
    sequencedBookmarks : SequencedBookmarks
}

module Scene =
         
    let current = 2   
    let read0 = 
        json {            
            let! cameraView      = Json.readWith Ext.fromJson<CameraView,Ext> "cameraView"
            let! navigationMode  = Json.read "navigationMode"
            let! exploreCenter   = Json.read "exploreCenter" 

            let! interactionMode = Json.read "interactionMode"
            let! surfaceModel    = Json.read "surfaceModel"
            let! config          = Json.read "config"
            let! scenePath       = Json.read "scenePath"
            let! referenceSystem = Json.read "referenceSystem"
            let! bookmarks       = Json.read "bookmarks"
            let! dockConfig      = Json.read "dockConfig"            

            return 
                {
                    version         = current

                    cameraView      = cameraView
                    navigationMode  = navigationMode |> enum<NavigationMode>
                    exploreCenter   = exploreCenter  |> V3d.Parse
                    
                    interaction     = interactionMode |> enum<InteractionMode>
                    surfacesModel   = surfaceModel
                    config          = config
                    scenePath       = scenePath
                    referenceSystem = referenceSystem
                    bookmarks       = bookmarks

                    viewPlans       = ViewPlanModel.initial
                    dockConfig      = dockConfig |> Serialization.jsonSerializer.UnPickleOfString
                    closedPages     = List.empty
                    firstImport     = false
                    userFeedback    = String.Empty
                    feedbackThreads = ThreadPool.empty
                    comparisonApp    = PRo3D.ComparisonApp.init
                    scaleBars       = ScaleBarsModel.initial
                    sceneObjectsModel   = SceneObjectsModel.initial
                    geologicSurfacesModel = GeologicSurfacesModel.initial
                    sequencedBookmarks = SequencedBookmarks.initial
                }
        }

    let read1 = 
        json {            
            let! cameraView      = Json.readWith Ext.fromJson<CameraView,Ext> "cameraView"
            let! navigationMode  = Json.read "navigationMode"
            let! exploreCenter   = Json.read "exploreCenter" 

            let! interactionMode = Json.read "interactionMode"
            let! surfaceModel    = Json.read "surfaceModel"
            let! config          = Json.read "config"
            let! scenePath       = Json.read "scenePath"
            let! referenceSystem = Json.read "referenceSystem"
            let! bookmarks       = Json.read "bookmarks"
            let! dockConfig      = Json.read "dockConfig"  
            let! (comparisonApp : option<ComparisonApp>) = Json.tryRead "comparisonApp"
            let! scaleBars       = Json.read "scaleBars" 
            let! sceneObjectsModel      = Json.read "sceneObjectsModel"  
            let! geologicSurfacesModel  = Json.read "geologicSurfacesModel"

            return 
                {
                    version                 = current

                    cameraView              = cameraView
                    navigationMode          = navigationMode |> enum<NavigationMode>
                    exploreCenter           = exploreCenter  |> V3d.Parse
            
                    interaction             = interactionMode |> enum<InteractionMode>
                    surfacesModel           = surfaceModel
                    config                  = config
                    scenePath               = scenePath
                    referenceSystem         = referenceSystem
                    bookmarks               = bookmarks

                    viewPlans               = ViewPlanModel.initial
                    dockConfig              = dockConfig |> Serialization.jsonSerializer.UnPickleOfString
                    closedPages             = List.empty
                    firstImport             = false
                    userFeedback            = String.Empty
                    feedbackThreads         = ThreadPool.empty
                    comparisonApp    = if comparisonApp.IsSome then comparisonApp.Value
                                       else ComparisonApp.init                    
                    scaleBars               = scaleBars
                    sceneObjectsModel       = sceneObjectsModel
                    geologicSurfacesModel   = geologicSurfacesModel
                    sequencedBookmarks = SequencedBookmarks.initial
                }
        }

    let read2 = 
        json {            
            let! cameraView      = Json.readWith Ext.fromJson<CameraView,Ext> "cameraView"
            let! navigationMode  = Json.read "navigationMode"
            let! exploreCenter   = Json.read "exploreCenter" 

            let! interactionMode = Json.read "interactionMode"
            let! surfaceModel    = Json.read "surfaceModel"
            let! config          = Json.read "config"
            let! scenePath       = Json.read "scenePath"
            let! referenceSystem = Json.read "referenceSystem"
            let! bookmarks       = Json.read "bookmarks"
            let! dockConfig      = Json.read "dockConfig"  
            let! (comparisonApp : option<ComparisonApp>) = Json.tryRead "comparisonApp"
            let! scaleBars       = Json.read "scaleBars" 
            let! sceneObjectsModel      = Json.read "sceneObjectsModel"  
            let! geologicSurfacesModel  = Json.read "geologicSurfacesModel"
            let! sequencedBookmarks     = Json.read "sequencedBookmarks"

            return 
                {
                    version                 = current

                    cameraView              = cameraView
                    navigationMode          = navigationMode |> enum<NavigationMode>
                    exploreCenter           = exploreCenter  |> V3d.Parse
            
                    interaction             = interactionMode |> enum<InteractionMode>
                    surfacesModel           = surfaceModel
                    config                  = config
                    scenePath               = scenePath
                    referenceSystem         = referenceSystem
                    bookmarks               = bookmarks

                    viewPlans               = ViewPlanModel.initial
                    dockConfig              = dockConfig |> Serialization.jsonSerializer.UnPickleOfString
                    closedPages             = List.empty
                    firstImport             = false
                    userFeedback            = String.Empty
                    feedbackThreads         = ThreadPool.empty
                    comparisonApp    = if comparisonApp.IsSome then comparisonApp.Value
                                       else ComparisonApp.init                                   
                    scaleBars               = scaleBars
                    sceneObjectsModel       = sceneObjectsModel
                    geologicSurfacesModel   = geologicSurfacesModel
                    sequencedBookmarks      = sequencedBookmarks
                }
        }



type Scene with
    static member FromJson (_ : Scene) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Scene.read0
            | 1 -> return! Scene.read1
            | 2 -> return! Scene.read2
            | _ ->
                return! v 
                |> sprintf "don't know version %A  of Scene" 
                |> Json.error
        }
    static member ToJson (x : Scene) =
        json {
            do! Json.write "version" x.version

            do! Json.writeWith Ext.toJson<CameraView,Ext> "cameraView" x.cameraView
            do! Json.write "navigationMode" (x.navigationMode |> int)
            do! Json.write "exploreCenter"  (x.exploreCenter.ToString())
            
            do! Json.write "interactionMode" (x.interaction |> int)
            do! Json.write "surfaceModel" x.surfacesModel
            do! Json.write "config" x.config
            do! Json.write "scenePath" x.scenePath
            do! Json.write "referenceSystem" x.referenceSystem
            do! Json.write "bookmarks" x.bookmarks    
            do! Json.write "comparisonApp" (x.comparisonApp)
            do! Json.write "dockConfig" (x.dockConfig |> Serialization.jsonSerializer.PickleToString) 
            do! Json.write "scaleBars" x.scaleBars
            do! Json.write "sceneObjectsModel" x.sceneObjectsModel
            do! Json.write "geologicSurfacesModel" x.geologicSurfacesModel
            do! Json.write "sequencedBookmarks" x.sequencedBookmarks
        }

[<ModelType>] 
type SceneHandle = {
    path        : string
    name        : string
    writeDate   : DateTime
}

[<ModelType>] 
type Recent = {
    recentScenes : list<SceneHandle> //HashMap<string,SceneHandle>
}

type Properties = 
    | AnnotationProperties of Annotation
    | NoProperties

type WayPoint = {
    name : string
    cv   : CameraView
}


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module MailboxState = 
  let empty = 
    {
      events = list.Empty
      update = fun _ -> ()
    }

type MessagingMailbox = MailboxProcessor<MailboxAction>

type MultiSelectionBox =
    {
        startPoint  : V2i
        renderBox   : Box2i
        selectionBox: Box3d
    }

[<ModelType>]
type Model = { 
    startupArgs          : StartupArgs
    dashboardMode        : string
    scene                : Scene
    drawing              : PRo3D.Core.Drawing.DrawingModel
    interaction          : Interactions    
    recent               : Recent
    waypoints            : IndexList<WayPoint>

    aspect               : double    
                         
    trafoKind            : TrafoKind
    trafoMode            : TrafoMode
                             
    tabMenu              : TabMenu
                         
    viewerMode           : ViewerMode
                         
    animations           : AnimationModel
                         
    messagingMailbox     : MessagingMailbox
    mailboxState         : MailboxState

    //scaleTools       : ScaleTools   // TODO horror, clean scale tools integration

    navigation       : NavigationModel

    properties       : Properties
    multiSelectBox   : Option<MultiSelectionBox>
    shiftFlag        : bool
    picking          : bool
    ctrlFlag         : bool
    frustum          : Frustum
    viewPortSize     : HashMap<string, V2i>
    overlayFrustum   : Option<Frustum>
    
    minervaModel     : PRo3D.Minerva.MinervaModel
    linkingModel     : PRo3D.Linking.LinkingModel
    //correlationPlot : CorrelationPanelModel
    //pastCorrelation : Option<CorrelationPanelModel>

    scaleBarsDrawing     : ScaleBarDrawing
            
    [<TreatAsValue>]
    past : Option<Drawing.DrawingModel> 

    [<TreatAsValue>]
    future               : Option<Drawing.DrawingModel> 
    footPrint            : FootPrint 
    //viewPlans            : ViewPlanModel
 
    snapshotThreads      : ThreadPool<ViewerAction>
    showExplorationPoint : bool

    heighValidation      : HeightValidatorModel

    frustumModel         : FrustumModel
}



module Viewer =
    open System.Threading

    let processMailboxAction (state:MailboxState) (cancelTokenSrc:CancellationTokenSource) (inbox:MessagingMailbox) (action : MailboxAction) =
      match action with
      | MailboxAction.InitMailboxState s ->     
        s
      | MailboxAction.DrawingAction a ->        
        a |> ViewerAction.DrawingMessage |> Seq.singleton |> state.update 
        state
      | MailboxAction.ViewerAction a ->        
        a |> Seq.singleton |> state.update 
        state
        

    let initMessageLoop (cancelTokenSrc:CancellationTokenSource) (inbox:MessagingMailbox) =
        let rec messageLoop state = async {
            let! msg = inbox.Receive()
            
            let newState =
                try msg |> processMailboxAction state cancelTokenSrc inbox
                with
                | e ->
                    Log.line "ViewerMgmt Error: Dropping msg (Exception:  %O)" e
                    state
            
            return! messageLoop newState
        }
        messageLoop MailboxState.empty

    let navInit = 
        let init = NavigationModel.initial
        let init = Optic.set (NavigationModel.camera_ >-> CameraControllerState.sensitivity_) 3.0 init
        let init = Optic.set (NavigationModel.camera_ >-> CameraControllerState.panFactor_) 0.0008 init
        let init = Optic.set (NavigationModel.camera_ >-> CameraControllerState.zoomFactor_) 0.0008 init
        init        

    let sceneElm = {id = "scene"; title = (Some "Scene"); weight = 0.4; deleteInvisible = None; isCloseable = None }
    
    let dockConfigFull = 
      config {
          content (                    
              horizontal 1.0 [                                                        
                stack 0.7 None [
                    {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                    {id = "instrumentview"; title = Some " Instrument View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                ]                            
                vertical 0.3 [
                  stack 0.5 (Some "surfaces") [                    
                    {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "minerva"; title = Some " Minerva "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "scalebars"; title = Some " ScaleBars "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                  ]                          
                  stack 0.5 (Some "config") [
                    {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "viewplanner"; title = Some " ViewPlanner "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "corr_mappings"; title = Some " RockTypes "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "corr_semantics"; title = Some " Semantics "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                  ]
                ]
              ]              
          )
          appName "PRo3D"
          useCachedConfig false
      }

    let dockConfigCore = 
      config {
          content (                        
              horizontal 1.0 [                                                        
                stack 0.7 None [
                    {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}                       
                ]                            
                vertical 0.3 [
                  stack 0.5 None [                        
                    {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "scalebars"; title = Some " ScaleBars "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                  ]                          
                  stack 0.5 (Some "config") [
                    {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                    {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }  
                    {id = "scaletools"; title = Some " Scale Tools"; weight = 0.4; deleteInvisible = None; isCloseable = None }                       
                  ]
                ]
              ]                        
          )
          appName "PRo3D"
          useCachedConfig false
      }

    //let initFeedback = 
    //     {  loadScene = "loading Scene..."
    //        saveScene = "saveing Scene..."
    //        loadOpcs  = "load opcs..."
    //        noText    = ""
    //     }

    let initial msgBox (startupArgs : StartupArgs) : Model = 
        {     
            scene = 
                {
                    version           = Scene.current
                    cameraView        = CameraView.ofTrafo Trafo3d.Identity
                    navigationMode    = NavigationMode.FreeFly
                    exploreCenter     = V3d.Zero
                        
                    interaction     = InteractionMode.PickOrbitCenter
                    surfacesModel   = SurfaceModel.initial
                    config          = ViewConfigModel.initial 
                    scenePath       = None

                    referenceSystem       = ReferenceSystem.initial                    
                    bookmarks             = GroupsModel.initial
                    scaleBars             = ScaleBarsModel.initial
                    dockConfig            = DockConfigs.core
                    closedPages           = list.Empty 
                    firstImport           = true
                    userFeedback          = ""
                    feedbackThreads       = ThreadPool.empty
                    comparisonApp         = PRo3D.ComparisonApp.init                    
                    viewPlans             = ViewPlanModel.initial
                    sceneObjectsModel     = SceneObjectsModel.initial
                    geologicSurfacesModel = GeologicSurfacesModel.initial
                    sequencedBookmarks    = {SequencedBookmarks.initial with outputPath = Config.besideExecuteable}
                }
            dashboardMode   = DashboardModes.core.name
            navigation      = navInit

            startupArgs     = startupArgs            
            drawing         = Drawing.DrawingModel.initialdrawing
            properties      = NoProperties
            interaction     = Interactions.DrawAnnotation
            multiSelectBox  = None
            shiftFlag       = false
            picking         = false
            ctrlFlag        = false

            messagingMailbox = msgBox
            mailboxState     = MailboxState.empty

            frustum         = Frustum.perspective 60.0 0.1 10000.0 1.0
            overlayFrustum  = None
            aspect          = 1.6   // CHECK-merge

            recent          = { recentScenes = List.empty }
            waypoints       = IndexList.empty

            trafoKind       = TrafoKind.Rotate
            trafoMode       = TrafoMode.Local            

            scaleBarsDrawing = InitScaleBarsParams.initialScaleBarDrawing
            past            = None
            future          = None

            tabMenu = TabMenu.Surfaces
            animations = 
                { 
                    animations = IndexList.empty
                    animation  = Animate.On
                    cam        = CameraController.initial.view
                }

            minervaModel = MinervaModel.initial // CHECK-merge PRo3D.Minerva.Initial.model msgBox2

            //scaleTools = 
            //    {
            //         planeExtrude = PlaneExtrude.App.initial
            //    }
            linkingModel = PRo3D.Linking.LinkingModel.initial
            
           // correlationPlot = CorrelationPanelModel.initial
            //pastCorrelation = None
            //instrumentCamera = { CameraController.initial with view = CameraView.lookAt V3d.Zero V3d.One V3d.OOI }        
            //instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0
            viewerMode = ViewerMode.Standard                
            footPrint = ViewPlanModel.initFootPrint
            viewPortSize = HashMap.empty

            snapshotThreads = ThreadPool.empty
            showExplorationPoint = startupArgs.showExplorationPoint
            heighValidation = HeightValidatorModel.init()
            frustumModel = FrustumModel.init 0.1 10000.0
    }
