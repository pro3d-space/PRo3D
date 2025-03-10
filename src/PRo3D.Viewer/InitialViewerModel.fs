namespace PRo3D.Viewer

open Aardvark.Base
open FSharp.Data.Adaptive

open Aether
open Aether.Operators

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.SimulatedViews
open PRo3D.Core.SequencedBookmarks

open PRo3D.Navigation2
open Aardvark.UI
open Aardvark.UI.Animation
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open Aardvark.UI.Animation.Deprecated
open Aardvark.Rendering

module Viewer =
    open System.Threading

    let processMailboxAction (state:MailboxState) (cancelTokenSrc:CancellationTokenSource) (inbox:MessagingMailbox) (action : MailboxAction) =
        match action with
        | MailboxAction.InitMailboxState s -> s
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

    let initial
        (msgBox              : MessagingMailbox)
        (startupArgs         : StartupArgs)
        (renderingUrl        : string)     
        (numberOfSamples     : int)   
        (screenshotDirectory : string)
        (animatorLens        : Lens<Model, Animator<Model>>)
        (viewerVerson        : string)
        : Model = 

        let defaultDashboard =  DashboardModes.defaultDashboard //DashboardModes.defaultDashboard
        // use this one for PROVEX workflows if needed.
        //let defaultDashboard = DashboardModes.provenance
        let defaultDockConfig = defaultDashboard.dockConfig //DockConfigs.m2020    
        let viewConfigModel = ViewConfigModel.initial 
        {     
            scene = 
                {
                    version           = Scene.current
                    cameraView        = CameraView.ofTrafo Trafo3d.Identity
                    navigationMode    = NavigationMode.FreeFly
                    exploreCenter     = V3d.Zero
                        
                    interaction     = InteractionMode.PickOrbitCenter
                    surfacesModel   = SurfaceModel.initial
                    config          = viewConfigModel
                    scenePath       = None

                    referenceSystem       = ReferenceSystem.initial                    
                    bookmarks             = GroupsModel.initial
                    scaleBars             = ScaleBarsModel.initial
                    dockConfig            = defaultDockConfig                
                    closedPages           = list.Empty 
                    firstImport           = true
                    userFeedback          = ""
                    feedbackThreads       = ThreadPool.empty
                    comparisonApp         = PRo3D.ComparisonApp.init                    
                    viewPlans             = ViewPlanModel.initial
                    sceneObjectsModel     = SceneObjectsModel.initial
                    geologicSurfacesModel = GeologicSurfacesModel.initial

                    traverses             = TraverseModel.initial
                    sequencedBookmarks    = SequencedBookmarks.initial //with outputPath = Config.besideExecuteable}
                    screenshotModel       = ScreenshotModel.initial
                    gisApp                = Gis.GisApp.initial startupArgs.defaultSpiceKernelPath
                }

            viewerVersion   = viewerVerson
            dashboardMode   = defaultDashboard.name
            navigation      = navInit

            startupArgs     = startupArgs            
            drawing         = Drawing.DrawingModel.initialdrawing
            properties      = NoProperties
            interaction     = Interactions.DrawAnnotation
            multiSelectBox  = None
            shiftFlag       = false
            picking         = false
            pivotType       = PickPivot.SurfacePivot
            ctrlFlag        = false

            messagingMailbox = msgBox
            mailboxState     = MailboxState.empty

            frustum         = viewConfigModel.frustumModel.frustum //Frustum.perspective 60.0 0.1 10000.0 1.0
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
           
            //minervaModel = MinervaModel.initial // CHECK-merge PRo3D.Minerva.Initial.model msgBox2

            //scaleTools = 
            //    {
            //         planeExtrude = PlaneExtrude.App.initial
            //    }
            //linkingModel = PRo3D.Linking.LinkingModel.initial
            
           // correlationPlot = CorrelationPanelModel.initial
            //pastCorrelation = None
            //instrumentCamera = { CameraController.initial with view = CameraView.lookAt V3d.Zero V3d.One V3d.OOI }        
            //instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0
            viewerMode      = ViewerMode.Standard
            footPrint       = FootPrint.initFootPrint
            viewPortSizes   = HashMap.empty

            snapshotThreads      = ThreadPool.empty
            showExplorationPoint = startupArgs.showExplorationPoint
            heighValidation      = HeightValidatorModel.init()
            
            filterTexture = false

            renderingUrl        = renderingUrl       
            numberOfSamples     = numberOfSamples    
            screenshotDirectory = screenshotDirectory
            animator            = Animation.Animator.initial animatorLens

            provenanceModel = ProvenanceModel.invalid
        } |> ProvenanceApp.emptyWithModel


