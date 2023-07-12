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
open Aardvark.UI.Anewmation

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Core.SequencedBookmarks
open PRo3D.SimulatedViews
open PRo3D.Core.Surface
open PRo3D.Navigation2

open PRo3D.Comparison

open Chiron

open Adaptify

open Aether
open Aether.Operators
//open PRo3D.Minerva

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

type PickPivot =
    | SurfacePivot      = 0
    | SceneObjectPivot  = 1
   // | ScaleBarPivot     = 2
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
| AnimationMessage                of AnimationAction // SequencedBookmarkId that corresponds to this AnimationAction
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
| SetFrustum                      of Frustum
| SetRenderViewportSize           of V2i
| ImportSurface                   of list<string>
| DiscoverAndImportOpcs        of list<string>
| ImportDiscoveredSurfacesThreads of list<string>
| ImportObject                    of preferredLoader : MeshLoaderType * filePaths : list<string>
| ImportSceneObject               of list<string>
| ImportPRo3Dv1Annotations        of list<string>
| ImportSurfaceTrafo              of list<string>
| ImportRoverPlacement            of list<string>
| ImportTraverse                  of list<string>
| SwitchViewerMode                of ViewerMode
| DnSProperties                   of PropertyActions
| ConfigPropertiesMessage         of ConfigProperties.Action
| DeleteLast
| AddSg                           of ISg
| PickSurface                     of SceneHit*string*bool
| PickObject                      of V3d*Guid
| SaveScene                       of string
| SaveAs                          of string
| SetScenePath                    of string // used to set hint path in scene (e.g. to be used in top menu bar)
| OpenScene                       of list<string>
| LoadScene                       of string // path to the scene file

// fine grained loading for provex provenance tracking and PRo3D api
| LoadSerializedScene             of string // serialized scene file (content of .pro3d)
| LoadSerializedDrawingModel      of string
| ImportSerializedDrawingModel    of drawingAsJson : string * source : string

| NewScene
| KeyDown                         of key : Aardvark.Application.Keys
| KeyUp                           of key : Aardvark.Application.Keys      
| ResizeMainControl               of V2i * string
| ResizeInstrumentControl         of V2i * string
| SetKind                         of TrafoKind
| SetInteraction                  of Interactions        
| SetMode                         of TrafoMode
| TransforAdaptiveSurface         of System.Guid * Trafo3d
| ImportTrafo                     of list<string>
| TransformAllSurfaces            of list<SnapshotSurfaceUpdate>
| RecalculateFarPlane
| RecalculateNearFarPlane      
| Translate                       of string * TrafoController.Action
| Rotate                          of string * TrafoController.Action
| SurfaceActions                  of SurfaceAppAction
//| MinervaActions                  of PRo3D.Minerva.MinervaAction
//| ScaleToolAction                 of ScaleToolAction
//| LinkingActions                  of PRo3D.Linking.LinkingAction    
| SetTabMenu                      of TabMenu
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
| SnapshotThreadDone              of string
| OnResize                        of V2i * string
| StartDragging                   of V2i * MouseButtons
| Dragging                        of V2i
| EndDragging                     of V2i * MouseButtons
//| CorrelationPanelMessage         of CorrelationPanelsMessage
| MakeSnapshot                    of int*int*string
| ImportSnapshotData              of list<string>
| TestHaltonRayCasting            //of list<string>
| HeightValidation               of HeightValidatorAction
| ComparisonMessage              of ComparisonAction
| ScaleBarsDrawingMessage        of ScaleBarDrawingAction
| ScaleBarsMessage               of ScaleBarsAction
| GeologicSurfacesMessage        of GeologicSurfaceAction
| ScreenshotMessage              of ScreenshotAction
| TraverseMessage                of TraverseAction
| SetSceneState                  of SceneState
| WriteBookmarkMetadata          of string * SequencedBookmarkModel
| WriteCameraMetadata            of string * SnapshotCamera
| StopGeoJsonAutoExport        
| SetPivotType                   of PickPivot
| LoadPoseDefinitionFile         of list<string>
| SBookmarksToPoseDefinition
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

    traverses          : TraverseModel

    viewPlans         : ViewPlanModel
    dockConfig        : DockConfig
    closedPages       : list<DockElement>
    firstImport       : bool
    userFeedback      : string
    feedbackThreads   : ThreadPool<ViewerAction> 
    comparisonApp     : PRo3D.Comparison.ComparisonApp
    sceneObjectsModel : SceneObjectsModel

    geologicSurfacesModel : GeologicSurfacesModel
    sequencedBookmarks    : SequencedBookmarks
    screenshotModel       : ScreenshotModel

}

module Scene =
        
    //let current = 2 //20211611 ... added traverse and sequenced bookmarks and comparison app
    let current = 3 //20220306 ... added viewPlans
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
                    version               = current
                                          
                    cameraView            = cameraView
                    navigationMode        = navigationMode |> enum<NavigationMode>
                    exploreCenter         = exploreCenter  |> V3d.Parse
                                          
                    interaction           = interactionMode |> enum<InteractionMode>
                    surfacesModel         = surfaceModel
                    config                = config
                    scenePath             = scenePath
                    referenceSystem       = referenceSystem
                    bookmarks             = bookmarks
                                          
                    viewPlans             = ViewPlanModel.initial
                    dockConfig            = dockConfig |> Serialization.jsonSerializer.UnPickleOfString
                    closedPages           = List.empty
                    firstImport           = false
                    userFeedback          = String.Empty
                    feedbackThreads       = ThreadPool.empty
                    scaleBars             = ScaleBarsModel.initial
                    sceneObjectsModel     = SceneObjectsModel.initial
                    geologicSurfacesModel = GeologicSurfacesModel.initial

                    traverses             = TraverseModel.initial
                    sequencedBookmarks    = SequencedBookmarks.initial

                    comparisonApp         = ComparisonApp.init
                    screenshotModel       = ScreenshotModel.initial
                }
        }

    let read1 = 
        json {            
            let! cameraView             = Json.readWith Ext.fromJson<CameraView,Ext> "cameraView"
            let! navigationMode         = Json.read "navigationMode"
            let! exploreCenter          = Json.read "exploreCenter" 
                                        
            let! interactionMode        = Json.read "interactionMode"
            let! surfaceModel           = Json.read "surfaceModel"
            let! config                 = Json.read "config"
            let! scenePath              = Json.read "scenePath"
            let! referenceSystem        = Json.read "referenceSystem"
            let! bookmarks              = Json.read "bookmarks"
            let! dockConfig             = Json.read "dockConfig"  
            let! comparisonApp          = Json.tryRead "comparisonApp"
            let! scaleBars              = Json.read "scaleBars" 
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
                    comparisonApp           = if comparisonApp.IsSome then comparisonApp.Value else ComparisonApp.init
                    scaleBars               = scaleBars
                    sceneObjectsModel       = sceneObjectsModel
                    geologicSurfacesModel   = geologicSurfacesModel

                    traverses                = TraverseModel.initial

                    sequencedBookmarks      = SequencedBookmarks.initial
                    screenshotModel         = ScreenshotModel.initial
                }
        }

    let read2 = 
        json {            
            let! cameraView             = Json.readWith Ext.fromJson<CameraView,Ext> "cameraView"
            let! navigationMode         = Json.read "navigationMode"
            let! exploreCenter          = Json.read "exploreCenter" 
                                        
            let! interactionMode        = Json.read "interactionMode"
            let! surfaceModel           = Json.read "surfaceModel"
            let! config                 = Json.read "config"
            let! scenePath              = Json.read "scenePath"
            let! referenceSystem        = Json.read "referenceSystem"
            let! bookmarks              = Json.read "bookmarks"
            let! dockConfig             = Json.read "dockConfig"  
            let! comparisonApp          = Json.tryRead "comparisonApp"
            let! scaleBars              = Json.read "scaleBars" 
            let! sceneObjectsModel      = Json.read "sceneObjectsModel"  
            let! geologicSurfacesModel  = Json.read "geologicSurfacesModel"
            let! sequencedBookmarks     = Json.tryRead "sequencedBookmarks"

            let! screenshotModel        = Json.tryRead "screenshotModel"
            let! traverse               = Json.tryRead "traverses"

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

                    viewPlans               = ViewPlanModel.initial //if viewplans.IsSome then viewplans.Value else ViewPlanModel.initial
                    dockConfig              = dockConfig |> Serialization.jsonSerializer.UnPickleOfString
                    closedPages             = List.empty
                    firstImport             = false
                    userFeedback            = String.Empty
                    feedbackThreads         = ThreadPool.empty
                    scaleBars               = scaleBars
                    sceneObjectsModel       = sceneObjectsModel
                    geologicSurfacesModel   = geologicSurfacesModel

                    traverses                = traverse |> Option.defaultValue(TraverseModel.initial)
                    sequencedBookmarks      = if sequencedBookmarks.IsSome then sequencedBookmarks.Value else SequencedBookmarks.initial
                    comparisonApp           = if comparisonApp.IsSome then comparisonApp.Value else ComparisonApp.init

                    screenshotModel         = screenshotModel |> Option.defaultValue(ScreenshotModel.initial)
                }
        }

    // added viewPlans
    let read3 = 
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
            let! viewPlans       = Json.read "viewPlans"
            let! dockConfig      = Json.read "dockConfig"  
            let! (comparisonApp : option<ComparisonApp>) = Json.tryRead "comparisonApp"
            let! scaleBars       = Json.read "scaleBars" 
            let! sceneObjectsModel      = Json.read "sceneObjectsModel"  
            let! geologicSurfacesModel  = Json.read "geologicSurfacesModel"
            let! sequencedBookmarks     = Json.tryRead "sequencedBookmarks"
            let! screenshotModel        = Json.tryRead "screenshotModel"
            let! traverse               = Json.tryRead "traverses"
            //let! viewplans     = Json.tryRead "viewplans"

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

                    viewPlans               = viewPlans
                    dockConfig              = dockConfig |> Serialization.jsonSerializer.UnPickleOfString
                    closedPages             = List.empty
                    firstImport             = false
                    userFeedback            = String.Empty
                    feedbackThreads         = ThreadPool.empty
                    scaleBars               = scaleBars
                    sceneObjectsModel       = sceneObjectsModel
                    geologicSurfacesModel   = geologicSurfacesModel

                    traverses                = traverse |> Option.defaultValue(TraverseModel.initial)
                    sequencedBookmarks      = if sequencedBookmarks.IsSome then sequencedBookmarks.Value else SequencedBookmarks.initial
                    comparisonApp           = if comparisonApp.IsSome then comparisonApp.Value else ComparisonApp.init

                    screenshotModel         = screenshotModel |> Option.defaultValue(ScreenshotModel.initial)
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
            | 3 -> return! Scene.read3
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
            do! Json.write "viewPlans" x.viewPlans    
            do! Json.write "comparisonApp" (x.comparisonApp)
            do! Json.write "dockConfig" (x.dockConfig |> Serialization.jsonSerializer.PickleToString) 
            do! Json.write "scaleBars" x.scaleBars
            do! Json.write "sceneObjectsModel" x.sceneObjectsModel
            do! Json.write "geologicSurfacesModel" x.geologicSurfacesModel

            do! Json.write "traverses" x.traverses
            do! Json.write "sequencedBookmarks" x.sequencedBookmarks
            do! Json.write "screenshotModel"    x.screenshotModel
        }

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
    viewerVersion        : string
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
    pivotType        : PickPivot
    ctrlFlag         : bool
    frustum          : Frustum
    viewPortSizes    : HashMap<string, V2i>
    overlayFrustum   : Option<Frustum>
    
    //minervaModel     : PRo3D.Minerva.MinervaModel
    //linkingModel     : PRo3D.Linking.LinkingModel
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

    filterTexture        : bool

    numberOfSamples      : int
    renderingUrl         : string
    screenshotDirectory  : string

    [<NonAdaptive>]
    animator             : Anewmation.Animator<Model>

    provenanceModel      : ProvenanceModel
} 

type ViewerAnimationAction =
    | ViewerMessage of ViewerAction
    | ProvenanceMessage of ProvenanceApp.ProvenanceMessage
    | AnewmationMessage of AnimatorMessage<Model>


