namespace PRo3D.Viewer

open System
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Primitives.Golden
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.UI.Trafos
open Aardvark.UI.Animation.Deprecated
open Aardvark.Rendering
open Aardvark.UI.Animation

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

// ---------------------------------------------------------------------------
// Chiron serialization for GoldenLayout types
// These augmentations must precede Scene.ToJson / FromJson.
// ---------------------------------------------------------------------------

type Element with
    static member ToJson (x : Element) : Json<unit> =
        json {
            do! Json.write "id"        x.Id
            do! Json.write "title"     x.Title
            do! Json.write "closable"  x.Closable
            do! Json.write "header"    (x.Header  |> Option.map int)
            do! Json.write "buttons"   (x.Buttons |> Option.map int)
            do! Json.write "minSize"   x.MinSize
            match x.Size with
            | Size.Weight n     -> do! Json.write "sizeUnit" "fr"; do! Json.write "size" n
            | Size.Percentage n -> do! Json.write "sizeUnit" "%";  do! Json.write "size" n
            do! Json.write "keepAlive" x.KeepAlive
        }
    static member FromJson (_ : Element) : Json<Element> =
        json {
            let! id        = Json.read          "id"
            let! title     = Json.read          "title"
            let! closable  = Json.read          "closable"
            let! header    = Json.tryRead<int>  "header"
            let! buttons   = Json.tryRead<int>  "buttons"
            let! minSize   = Json.tryRead<int>  "minSize"
            let! sizeUnit  = Json.read<string>  "sizeUnit"
            let! size      = Json.read<int>     "size"
            let! keepAlive = Json.read          "keepAlive"
            return {
                Id        = id
                Title     = title
                Closable  = closable
                Header    = header  |> Option.map enum<Header>
                Buttons   = buttons |> Option.map enum<Buttons>
                MinSize   = minSize
                Size      = match sizeUnit with "%" -> Size.Percentage size | _ -> Size.Weight size
                KeepAlive = keepAlive
            }
        }

type Stack with
    static member ToJson (x : Stack) : Json<unit> =
        json {
            do! Json.write "header"  (int x.Header)
            do! Json.write "buttons" (x.Buttons |> Option.map int)
            match x.Size with
            | Size.Weight n     -> do! Json.write "sizeUnit" "fr"; do! Json.write "size" n
            | Size.Percentage n -> do! Json.write "sizeUnit" "%";  do! Json.write "size" n
            do! Json.write "content" x.Content
        }
    static member FromJson (_ : Stack) : Json<Stack> =
        json {
            let! header   = Json.read<int>          "header"
            let! buttons  = Json.tryRead<int>       "buttons"
            let! sizeUnit = Json.read<string>       "sizeUnit"
            let! size     = Json.read<int>          "size"
            let! content  = Json.read<Element list> "content"
            return {
                Header  = enum<Header> header
                Buttons = buttons |> Option.map enum<Buttons>
                Size    = match sizeUnit with "%" -> Size.Percentage size | _ -> Size.Weight size
                Content = content
            }
        }

type RowOrColumn with
    static member ToJson (x : RowOrColumn) : Json<unit> =
        json {
            do! Json.write "isRow" x.IsRow
            match x.Size with
            | Size.Weight n     -> do! Json.write "sizeUnit" "fr"; do! Json.write "size" n
            | Size.Percentage n -> do! Json.write "sizeUnit" "%";  do! Json.write "size" n
            do! Json.write "content" x.Content
        }
    static member FromJson (_ : RowOrColumn) : Json<RowOrColumn> =
        json {
            let! isRow    = Json.read<bool>        "isRow"
            let! sizeUnit = Json.read<string>      "sizeUnit"
            let! size     = Json.read<int>         "size"
            let! content  = Json.read<Layout list> "content"
            return {
                IsRow   = isRow
                Size    = match sizeUnit with "%" -> Size.Percentage size | _ -> Size.Weight size
                Content = content
            }
        }

and Layout with
    static member ToJson (x : Layout) : Json<unit> =
        match x with
        | Layout.Element e     -> Json.write "Element"     e
        | Layout.Stack s       -> Json.write "Stack"       s
        | Layout.RowOrColumn r -> Json.write "RowOrColumn" r
    static member FromJson (_ : Layout) : Json<Layout> =
        json {
            let! elem = Json.tryRead<Element>     "Element"
            match elem with
            | Some e -> return Layout.Element e
            | None ->
                let! stk = Json.tryRead<Stack>    "Stack"
                match stk with
                | Some s -> return Layout.Stack s
                | None ->
                    let! rc = Json.read<RowOrColumn> "RowOrColumn"
                    return Layout.RowOrColumn rc
        }

type PopoutWindow with
    static member ToJson (x : PopoutWindow) : Json<unit> =
        json {
            do! Json.write "root"      x.Root
            do! Json.write "positionX" (x.Position |> Option.map (fun v -> v.X))
            do! Json.write "positionY" (x.Position |> Option.map (fun v -> v.Y))
            do! Json.write "sizeW"     (x.Size     |> Option.map (fun v -> v.X))
            do! Json.write "sizeH"     (x.Size     |> Option.map (fun v -> v.Y))
        }
    static member FromJson (_ : PopoutWindow) : Json<PopoutWindow> =
        json {
            let! root  = Json.read<Layout>  "root"
            let! posX  = Json.tryRead<int>  "positionX"
            let! posY  = Json.tryRead<int>  "positionY"
            let! sizeW = Json.tryRead<int>  "sizeW"
            let! sizeH = Json.tryRead<int>  "sizeH"
            return {
                Root     = root
                Position = match posX, posY  with Some x, Some y -> Some (V2i(x,y)) | _ -> None
                Size     = match sizeW, sizeH with Some w, Some h -> Some (V2i(w,h)) | _ -> None
            }
        }

type WindowLayout with
    static member ToJson (x : WindowLayout) : Json<unit> =
        json {
            do! Json.write "root"          x.Root
            do! Json.write "popoutWindows" x.PopoutWindows
        }
    static member FromJson (_ : WindowLayout) : Json<WindowLayout> =
        json {
            let! root    = Json.tryRead<Layout>            "root"
            let! popouts = Json.tryRead<PopoutWindow list> "popoutWindows"
            return {
                Root          = root
                PopoutWindows = popouts |> Option.defaultValue []
            }
        }

// ---------------------------------------------------------------------------

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
    | ImportGroupModel    of list<string>
    | ExportGroupModel    of string
    | ImportBookmarks     of list<string>
    | ExportBookmarks     of string
    | GroupsMessage       of GroupsAppAction
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
| InvertDrawing
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
| DiscoverAndImportOpcs           of list<string>
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

| PickSurface                     of SceneHit * string * bool
| PreviewPickSurface              of SceneHit * string * bool
| PreviewPickSurfaceFinished      of SceneHit * string * Option<Aardvark.Geometry.ObjectRayHit * V3d>


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
| ImportDrawingModel              of annotations : GroupsModel * source : string

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
| RecalculateNearFarPlane         of V2d  
| Translate                       of string * TrafoController.Action
| Rotate                          of string * TrafoController.Action
| SurfaceActions                  of SurfaceAppAction
//| MinervaActions                  of PRo3D.Minerva.MinervaAction
//| ScaleToolAction                 of ScaleToolAction
//| LinkingActions                  of PRo3D.Linking.LinkingAction    
| SetTabMenu                      of TabMenu
| NoAction                        of string
| OrientationCube                 of ISg
| GoldenLayoutMessage             of GoldenLayout.Message
| StoreCurrentLayout              of string
| ChangeDashboardMode             of DashboardMode
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
| MouseOut                        of V2i
| MouseIn                         of V2i
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
| GisAppMessage                  of Gis.GisAppAction
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

    traverses         : TraverseModel

    viewPlans         : ViewPlanModel
    goldenLayout      : GoldenLayout
    firstImport       : bool
    userFeedback      : string
    feedbackThreads   : ThreadPool<ViewerAction> 
    comparisonApp     : PRo3D.Comparison.ComparisonApp
    sceneObjectsModel : SceneObjectsModel

    geologicSurfacesModel : GeologicSurfacesModel
    sequencedBookmarks    : SequencedBookmarks
    screenshotModel       : ScreenshotModel
    gisApp                : PRo3D.Core.Gis.GisApp
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
            let! _               = Json.tryRead<string> "dockConfig"
            let! goldenLayoutJson = Json.tryRead<WindowLayout> "goldenLayout"

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
                    goldenLayout          =
                        GoldenLayout.create LayoutConfig.Default
                            (goldenLayoutJson |> Option.defaultValue DockConfigs.m2020)
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
                    gisApp                = Gis.GisApp.initial None
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
            let! _                      = Json.tryRead<string> "dockConfig"
            let! goldenLayoutJson       = Json.tryRead<WindowLayout> "goldenLayout"
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
                    goldenLayout            =
                        let layout = goldenLayoutJson |> Option.map GoldenLayout.Json.deserialize
                                                      |> Option.defaultValue DockConfigs.m2020
                        GoldenLayout.create LayoutConfig.Default layout
                    firstImport             = false
                    userFeedback            = String.Empty
                    feedbackThreads         = ThreadPool.empty
                    comparisonApp           = if comparisonApp.IsSome then comparisonApp.Value else ComparisonApp.init
                    scaleBars               = scaleBars
                    sceneObjectsModel       = sceneObjectsModel
                    geologicSurfacesModel   = geologicSurfacesModel

                    traverses               = TraverseModel.initial

                    sequencedBookmarks      = SequencedBookmarks.initial
                    screenshotModel         = ScreenshotModel.initial
                    gisApp                  = Gis.GisApp.initial None
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
            let! _                      = Json.tryRead<string> "dockConfig"
            let! goldenLayoutJson       = Json.tryRead<WindowLayout> "goldenLayout"
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

                    viewPlans               = ViewPlanModel.initial
                    goldenLayout            =
                        let layout = goldenLayoutJson |> Option.map GoldenLayout.Json.deserialize
                                                      |> Option.defaultValue DockConfigs.m2020
                        GoldenLayout.create LayoutConfig.Default layout
                    firstImport             = false
                    userFeedback            = String.Empty
                    feedbackThreads         = ThreadPool.empty
                    scaleBars               = scaleBars
                    sceneObjectsModel       = sceneObjectsModel
                    geologicSurfacesModel   = geologicSurfacesModel

                    traverses               = traverse |> Option.defaultValue(TraverseModel.initial)
                    sequencedBookmarks      = if sequencedBookmarks.IsSome then sequencedBookmarks.Value else SequencedBookmarks.initial
                    comparisonApp           = if comparisonApp.IsSome then comparisonApp.Value else ComparisonApp.init

                    screenshotModel         = screenshotModel |> Option.defaultValue(ScreenshotModel.initial)
                    gisApp                  = Gis.GisApp.initial None
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
            let! _               = Json.tryRead<string> "dockConfig"
            let! goldenLayoutJson = Json.tryRead<WindowLayout> "goldenLayout"
            let! (comparisonApp : option<ComparisonApp>) = Json.tryRead "comparisonApp"
            let! scaleBars       = Json.read "scaleBars" 
            let! sceneObjectsModel      = Json.read "sceneObjectsModel"  
            let! geologicSurfacesModel  = Json.read "geologicSurfacesModel"
            let! sequencedBookmarks     = Json.tryRead "sequencedBookmarks"
            let! screenshotModel        = Json.tryRead "screenshotModel"
            let! traverse               = Json.tryRead "traverses"
            let! gisApp                 = Json.tryRead "gisApp"
            let gisApp = 
                match gisApp with
                | Some gisApp -> gisApp
                | None -> Gis.GisApp.initial None
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
                    goldenLayout            =
                        let layout = goldenLayoutJson |> Option.map GoldenLayout.Json.deserialize
                                                      |> Option.defaultValue DockConfigs.m2020
                        GoldenLayout.create LayoutConfig.Default layout
                    firstImport             = false
                    userFeedback            = String.Empty
                    feedbackThreads         = ThreadPool.empty
                    scaleBars               = scaleBars
                    sceneObjectsModel       = sceneObjectsModel
                    geologicSurfacesModel   = geologicSurfacesModel

                    traverses               = traverse |> Option.defaultValue(TraverseModel.initial)
                    sequencedBookmarks      = if sequencedBookmarks.IsSome then sequencedBookmarks.Value else SequencedBookmarks.initial
                    comparisonApp           = if comparisonApp.IsSome then comparisonApp.Value else ComparisonApp.init

                    screenshotModel         = screenshotModel |> Option.defaultValue(ScreenshotModel.initial)
                    gisApp                  = gisApp
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
            do! Json.write "goldenLayout" x.goldenLayout.DefaultLayout
            do! Json.write "scaleBars" x.scaleBars
            do! Json.write "sceneObjectsModel" x.sceneObjectsModel
            do! Json.write "geologicSurfacesModel" x.geologicSurfacesModel

            do! Json.write "traverses" x.traverses
            do! Json.write "sequencedBookmarks" x.sequencedBookmarks
            do! Json.write "screenshotModel"    x.screenshotModel
            do! Json.write "gisApp"             x.gisApp
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

type SurfaceIntersection = { surfaceName : string; hitPoint : V3d; normal : Option<V3d> }

type ProjectedEllipse = 
    {
        surfaceProjectedPoints : Option<array<V3d>>
        approximatePoints : array<V3d>
        ellipse : Ellipse2d
    }

type EllipseType = 
    | BoundaryEllipse
    | ThreePointEllipse

[<ModelType>]
type EllipseModel = 
    {
        firstWorldPick : SurfaceIntersection
        currentWorldPos : Option<SurfaceIntersection>
        secondWorldPick : Option<SurfaceIntersection>
        boundaryVertices : Option<V3d[]>
        projectionPlane  : Option<Plane3d>
        projectedEllipse : Option<ProjectedEllipse>
    }

module EllipseModel = 
    let initial (p : SurfaceIntersection) = 
        {   firstWorldPick = p; currentWorldPos = None; secondWorldPick = None; 
            boundaryVertices = None; projectionPlane = None; projectedEllipse = None 
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
    inverseFlag      : bool
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
    
    heighValidation      : HeightValidatorModel

    filterTexture        : bool

    numberOfSamples      : int
    renderingUrl         : string
    screenshotDirectory  : string

    [<NonAdaptive>]
    animator             : Animation.Animator<Model>

    provenanceModel      : ProvenanceModel

    backgroundPicking    : ThreadPool<ViewerAction>

    surfaceIntersection : Option<SurfaceIntersection>
    ellipseModel        : Option<EllipseModel>
    pickPreviewRequested : ConsumableAsyncValue<Model * SceneHit * string>
} 

type ViewerAnimationAction =
    | ViewerMessage     of ViewerAction
    | ProvenanceMessage of ProvenanceApp.ProvenanceMessage
    | AnewmationMessage of AnimatorMessage<Model>


