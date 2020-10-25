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

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.SimulatedViews
open PRo3D.Core.Surface
open PRo3D.Navigation2

open PRo3D.SimulatedViews

open Chiron

open Adaptify

open Aether
open Aether.Operators
open PRo3D.Minerva
open CorrelationDrawing.Model
open CorrelationDrawing
open UIPlus

open PRo3D.Correlations
open PRo3D.Correlations.Model
open PRo3D.Bookmarkings


#nowarn "0686"

type TabMenu = 
    | Surfaces    = 0
    | Annotations = 1
    | Viewplanner = 2
    | Bookmarks   = 3
    | Config      = 4

type PropertyActions =
    | DrawingMessage    of DrawingAction
    | AnnotationMessage of AnnotationProperties.Action


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
    | RoverMessage                    of RoverApp.Action
    | ViewPlanMessage                 of ViewPlanApp.Action
    | DnSColorLegendMessage           of FalseColorLegendApp.Action
    | SetCamera                       of CameraView        
    | SetCameraAndFrustum             of CameraView * double * double        
    | SetCameraAndFrustum2            of CameraView * Frustum
    | ImportSurface                   of list<string>
    | ImportDiscoveredSurfaces        of list<string>
    | ImportDiscoveredSurfacesThreads of list<string>
    | ImportObject                    of list<string>
    | ImportPRo3Dv1Annotations        of list<string>
    | ImportSurfaceTrafo              of list<string>
    | ImportRoverPlacement            of list<string>
    | SwitchViewerMode                of ViewerMode
    | DnSProperties                   of PropertyActions
    | ConfigPropertiesMessage         of ConfigProperties.Action
    | DeleteLast
    | AddSg                           of ISg
    | PickSurface                     of SceneHit*string
    | PickObject                      of V3d*Guid
    | SaveScene                       of string
    | SaveAs                          of string
    | OpenScene                       of list<string>
    | LoadScene                       of string
    | NewScene
    | KeyDown                         of key : Aardvark.Application.Keys
    | KeyUp                           of key : Aardvark.Application.Keys      
    | SetKind                         of TrafoKind
    | SetInteraction                  of Interactions        
    | SetMode                         of TrafoMode
    | TransforAdaptiveSurface                of System.Guid * Trafo3d
    | ImportTrafo                     of list<string>
    //| TransformAllSurfaces            of list<SnapshotSurfaceUpdate>
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
    | AddPage                         of DockElement    
    | ToggleOrientationCube
    | UpdateUserFeedback              of string
    | StartImportMessaging            of list<string>
    | Logging                         of string * ViewerAction
    | ThreadsDone                     of string    
    | SnapshotThreadsDone             of string
    | OnResize                        of V2i
    | StartDragging                   of V2i * MouseButtons
    | Dragging                        of V2i
    | EndDragging                     of V2i * MouseButtons
    | CorrelationPanelMessage         of CorrelationPanelsMessage
    | MakeSnapshot                    of int*int*string
    | ImportSnapshotData              of list<string>
    | SetTextureFiltering             of bool // TODO move to versioned ViewConfigModel in V3
    //| UpdateShatterCones              of list<SnapshotShattercone> // TODO snapshots and shattercone things should be in own apps
    | TestHaltonRayCasting            //of list<string>
    | HeightValidation               of HeightValidatorAction
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

    viewPlans         : ViewPlanModel
    dockConfig        : DockConfig
    closedPages       : list<DockElement>
    firstImport       : bool
    userFeedback      : string
    feedbackThreads   : ThreadPool<ViewerAction> 
}

module Scene =
        
    let current = 0    
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
                }
        }



type Scene with
    static member FromJson (_ : Scene) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Scene.read0
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

            do! Json.write "dockConfig" (x.dockConfig |> Serialization.jsonSerializer.PickleToString)                   
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
    viewPortSize     : V2i
    overlayFrustum   : Option<Frustum>
    
    minervaModel     : PRo3D.Minerva.MinervaModel
    linkingModel     : PRo3D.Linking.LinkingModel

    correlationPlot : CorrelationPanelModel
    pastCorrelation : Option<CorrelationPanelModel>
            
    [<TreatAsValue>]
    past : Option<Drawing.DrawingModel> 

    [<TreatAsValue>]
    future               : Option<Drawing.DrawingModel> 
    footPrint            : FootPrint 
    //viewPlans            : ViewPlanModel
 
    arnoldSnapshotThreads: ThreadPool<ViewerAction>
    showExplorationPoint : bool
    filterTexture        : bool // TODO move to versioned ViewConfigModel in V3

    heighValidation      : HeightValidatorModel
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

                    referenceSystem = ReferenceSystem.initial                    
                    bookmarks       = GroupsModel.initial
                    dockConfig      = DockConfigs.core
                    closedPages     = list.Empty 
                    firstImport     = true
                    userFeedback    = ""
                    feedbackThreads = ThreadPool.empty
                    viewPlans       = ViewPlanModel.initial
                }

            navigation      = navInit

            startupArgs     = startupArgs            
            drawing         = Drawing.DrawingModel.initialdrawing
            properties      = NoProperties
            interaction     = Interactions.PlaceValidator
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
            
            correlationPlot = CorrelationPanelModel.initial
            pastCorrelation = None
            //instrumentCamera = { CameraController.initial with view = CameraView.lookAt V3d.Zero V3d.One V3d.OOI }        
            //instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0
            viewerMode = ViewerMode.Standard                
            footPrint = ViewPlanModel.initFootPrint
            viewPortSize = V2i.One

            arnoldSnapshotThreads = ThreadPool.empty
            showExplorationPoint = startupArgs.showExplorationPoint
            filterTexture = startupArgs.magnificationFilter

            heighValidation = HeightValidatorModel.init()
    }
