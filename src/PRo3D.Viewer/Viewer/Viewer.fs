namespace PRo3D

open Aardvark.Service

open System
open System.Collections.Concurrent
open System.IO
open System.Diagnostics

open Adaptify.FSharp.Core

open Aardvark.Base
open Aardvark.Base.Geometry
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Rendering.Text
open Aardvark.UI
open Aardvark.UI.Operators
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open Aardvark.UI.Animation
open Aardvark.Application

open Aardvark.Data.Opc
open Aardvark.SceneGraph.SgPrimitives.Sg
open Aardvark.VRVis

open PRo3D
open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.SequencedBookmarks
open PRo3D.Core.Drawing
open PRo3D.Navigation2
open PRo3D.Bookmarkings

open PRo3D.Core.Surface
open PRo3D.Viewer

open PRo3D.SimulatedViews
//open PRo3D.Minerva
//open PRo3D.Linking
open PRo3D.ViewerLenses

 
open Aether
open Aether.Operators
open Chiron 
open PRo3D.Core.Surface
open Aardvark.UI.Animation.Deprecated

type UserFeedback<'a> = {
    id      : string
    text    : string
    timeout : int
    msg     : 'a
}

module UserFeedback =

    let create duration text =
        {
            id      = System.Guid.NewGuid().ToString()
            text    = text
            timeout = duration
            msg     = ViewerAction.NoAction ""
        }

    let createWorker (feedback: UserFeedback<ViewerAction>) =
        proclist {
            yield UpdateUserFeedback ""
            yield UpdateUserFeedback feedback.text
            yield feedback.msg
      
            do! Proc.Sleep feedback.timeout
            yield ThreadsDone feedback.id
        }

    let queueFeedback fb m =
        { m with scene = { m.scene with feedbackThreads = ThreadPool.add fb.id (fb |> createWorker) m.scene.feedbackThreads }}

module ViewerApp =         
    let dataSamples = 4

    let lookAtData (m: Model) =         
        let bb = m |> Optic.get _sgSurfaces |> HashMap.toSeq |> Seq.map(fun (_,x) -> x.globalBB) |> Box3d
        let view = CameraView.lookAt bb.Max bb.Center m.scene.referenceSystem.up.value             

        Optic.set _view view m

    let lookAtBoundingBox (bb: Box3d) (m: Model) =
        let view = CameraView.lookAt bb.Max bb.Center m.scene.referenceSystem.up.value                
        m |> Optic.set _view view
    
    let lookAtSurface (m: Model) id =
        let surf = m |> Optic.get _sgSurfaces |> HashMap.tryFind id
        match surf with
        | Some s ->
            let bb = s.globalBB
            m |> lookAtBoundingBox s.globalBB
        | None -> m

    let logScreen timeout m text = 
      let feedback = 
        {
          id      = System.Guid.NewGuid().ToString()
          text    = text
          timeout = timeout
          msg     = ViewerAction.NoAction ""
        }
      m |> UserFeedback.queueFeedback feedback

    let stash (model : Model) =
        { model with past = Some model.drawing; future = None }
           
    let mrefConfig : MInnerConfig<AdaptiveViewConfigModel> =
        {
            getArrowLength    = fun (x:AdaptiveViewConfigModel) -> x.arrowLength.value
            getArrowThickness = fun (x:AdaptiveViewConfigModel) -> x.arrowThickness.value
            getNearDistance   = fun (x:AdaptiveViewConfigModel) -> x.nearPlane.value
            getHorizontalFieldOfView = fun (x:AdaptiveViewConfigModel) -> 
                                            x.frustumModel.frustum 
                                            |> AVal.map Frustum.horizontalFieldOfViewInDegrees
        }
    
    let drawingConfig : DrawingApp.SmallConfig<ReferenceSystem> =
        { 
            up     = (ReferenceSystem.up_     >-> V3dInput.value_) |> Aether.toBase
            north  = (ReferenceSystem.northO_ |> Aether.toBase) //  |. V3dInput.Lens.value)
            planet = (ReferenceSystem.planet_ |> Aether.toBase)
        }

    let mdrawingConfig : DrawingApp.MSmallConfig<AdaptiveViewConfigModel> =
        {            
            getNearPlane        = fun x -> x.nearPlane.value
            getHfov             = fun (x:AdaptiveViewConfigModel) -> x.frustumModel.frustum 
                                                                     |> AVal.map Frustum.horizontalFieldOfViewInDegrees //((AVal.init 60.0) :> aval<float>)
            getArrowThickness   = fun (x:AdaptiveViewConfigModel) -> x.arrowThickness.value
            getArrowLength      = fun (x:AdaptiveViewConfigModel) -> x.arrowLength.value
            getDnsPlaneSize     = fun (x:AdaptiveViewConfigModel) -> x.dnsPlaneSize.value
            getOffset           = fun (x:AdaptiveViewConfigModel) -> AVal.constant(0.1)//x.offset.value
            getPickingTolerance = fun (x:AdaptiveViewConfigModel) -> x.pickingTolerance.value
        }

    let navConf : Navigation.smallConfig<ViewConfigModel, ReferenceSystem> =
        {
            navigationSensitivity = ViewConfigModel.navigationSensitivity_ >-> NumericInput.value_ |> Aether.toBase
            up                    = ReferenceSystem.up_ >-> V3dInput.value_  |> Aether.toBase
        }    
    
    let mutable cache = HashMap.Empty

    let updateSceneWithNewSurface (m: Model) =
        let sgSurfaces = 
            m.scene.surfacesModel.sgSurfaces 
            |> HashMap.toList 
            |> List.map snd
        
        match sgSurfaces |> List.tryHead with
        | Some v ->
            let fullBb = 
                sgSurfaces 
                |> List.map(fun x -> x.globalBB) 
                |> Box3d
                |> Box3d.extendBy(v.globalBB)                            

            // useful default viewpoint after 2nd import
            match m.scene.firstImport with                  
            | true -> 
                let refAction = ReferenceSystemAction.InferCoordSystem(fullBb.Center)
                let (refSystem',_)= 
                    ReferenceSystemApp.update 
                        m.scene.config 
                        LenseConfigs.referenceSystemConfig 
                        (m.scene.referenceSystem) 
                        refAction

                let navigation' =  { m.navigation with exploreCenter = fullBb.Center} 
                { m with 
                    navigation = navigation'
                    scene = { m.scene with referenceSystem = refSystem'; firstImport = false }
                } 
                |> SceneLoader.updateCameraUp 
                |> lookAtBoundingBox v.globalBB
            | _-> m     
        | None -> m

    let isGrabbed (model: Model) =
        let sel = Optic.get _selectedSurface model |> Option.bind(fun x -> Optic.get _sgSurfaces model |> HashMap.tryFind x)
        match sel with
        | Some s -> s.trafo.grabbed.IsSome
        | None -> false       

    let private createAnimation (pos: V3d) (forward: V3d) (up : V3d) (animationsOld: AnimationModel) : AnimationModel =                                    
        CameraAnimations.animateForwardAndLocation pos forward up 3.5 "ForwardAndLocation2s"
        |> AnimationAction.PushAnimation 
        |> AnimationApp.update animationsOld

    //TODO TO refactor ... move docking manipulation somewhere else... check what works and what doesn't
    let rec getAllDockElements (dnc: DockNodeConfig) : (list<DockElement>) = 
        match dnc with
        | DockNodeConfig.Vertical (weight,children) -> 
            let test = children |> List.map(fun x -> getAllDockElements x )
            test |> List.concat  
        | DockNodeConfig.Horizontal (weight,children) -> 
            let test = children |> List.map(fun x -> getAllDockElements x )
            test |> List.concat 
        | DockNodeConfig.Stack (weight,activeId,children) -> children 
        | DockNodeConfig.Element element -> [element] 
    
    let updateClosedPages (m: Model) (dncUpdated: DockNodeConfig) =
        let de = getAllDockElements m.scene.dockConfig.content
        let deUpdated = getAllDockElements dncUpdated
        let diff = ((Set.ofList de) - (Set.ofList deUpdated)) |> Set.toList
        // diff contains all changed elements (not only the deleted)
        match diff with
            | [] -> m.scene.closedPages
            | _ -> 
                let test = 
                    diff 
                    |> List.choose (fun x -> 
                        match deUpdated |> List.filter(fun y -> y.id = x.id) with
                        | [] -> Some x
                        | _  -> None)                
                List.append m.scene.closedPages test 
                   
    let private addDockElement (dnc: DockNodeConfig) (de: DockElement) = 
        match dnc with
        | DockNodeConfig.Vertical (weight,children) -> let add = List.append children [(Stack(weight, None, [de]))]
                                                       Horizontal(weight,add)
        | DockNodeConfig.Horizontal (weight,children) -> let add = List.append children [(Stack(weight, None, [de]))]
                                                         Horizontal(weight,add) 
        | DockNodeConfig.Stack (weight,activeId,children) -> Stack(weight, activeId, List.append [de] children)
        | DockNodeConfig.Element element ->  Stack(0.2, None, List.append [de] [element]) 

    let private createMultiSelectBox (startPoint: V2i) (viewPortSize: V2i) (currentPoint: V2i) =
        let clippingBox = Box2i.FromSize viewPortSize
        let newRenderBox = Box2i.FromPoints(clippingBox.Clamped(startPoint), clippingBox.Clamped(currentPoint)) // limited to rendercontrol-size!
                
        let ndc (v:V2i) = (((V2d v) / V2d viewPortSize) - V2d 0.5) * V2d(2.0, -2.0) // range [-1.0,1.0]
        let min = ndc newRenderBox.Min
        let max = ndc newRenderBox.Max
        let viewBox = Box3d.FromPoints(V3d(min, 0.0), V3d(max, 1.0))
        //Log.line "viewBox - min: %A\nviewBox - max:%A" viewBox.Min viewBox.Max

        (newRenderBox, viewBox)

    let private matchPickingInteraction (bc: BlockingCollection<string>) (p: V3d) (referenceSystem : Option<SpiceReferenceSystem>) (hitFunction:(V3d -> V3d option)) (surf: Surface) (m: Model) = 
        match m.interaction, m.viewerMode with
        | Interactions.DrawAnnotation, _ -> 
            let m = 
                match surf.surfaceType with
                | SurfaceType.Mesh -> { m with drawing = { m.drawing with projection = Projection.Linear } } //TODO LF ... why is this happening?
                | _ -> m
            
            let view = 
                match m.viewerMode with 
                | ViewerMode.Standard -> m.navigation.camera.view
                | ViewerMode.Instrument -> m.scene.viewPlans.instrumentCam 

            let msg = DrawingAction.AddPointAdv(p, hitFunction, surf.name, None)
            let drawing = DrawingApp.update m.scene.referenceSystem drawingConfig referenceSystem bc view m.shiftFlag m.drawing msg
            //Log.stop()
            { m with drawing = drawing } |> stash
        | Interactions.PlaceCoordinateSystem, ViewerMode.Standard ->                                   
            let (refSystem',_) = 
                p 
                |> ReferenceSystemAction.UpdateUpNorth //updates position
                |> ReferenceSystemApp.update 
                    m.scene.config 
                    LenseConfigs.referenceSystemConfig 
                    m.scene.referenceSystem
                                                 
            let m = { m with scene = { m.scene with referenceSystem = refSystem' }} 
            //update camera upvector
            SceneLoader.updateCameraUp m
        | Interactions.PickExploreCenter, ViewerMode.Standard ->
            let c   = m.scene.config
            let ref = m.scene.referenceSystem
            let navigation' = 
                Navigation.update c ref navConf true m.navigation (Navigation.Action.ArcBallAction(ArcBallController.Message.Pick p))
            { m with navigation = navigation' }
        | Interactions.PlaceRover, ViewerMode.Standard ->
            let ref = m.scene.referenceSystem 

            let addPointMsg = ViewPlanApp.Action.AddPoint(p, ref, cache, (Optic.get _surfacesModel m))

            let outerModel, viewPlans = 
                ViewPlanApp.update m.scene.viewPlans addPointMsg _navigation _footprint m.scene.scenePath ref m

            let m' = 
                { m with 
                    scene = { m.scene with viewPlans = viewPlans}  // CHECK-merge
                    footPrint = outerModel.footPrint 
                }
            match m.scene.viewPlans.working with
            | [] -> m'
            | _  -> { m' with tabMenu = TabMenu.Viewplanner }
        | Interactions.PlaceSurface, ViewerMode.Standard -> 
            let action = (SurfaceAppAction.PlaceSurface(p)) 
            let surfaceModel =
                SurfaceApp.update 
                    m.scene.surfacesModel action m.scene.scenePath m.navigation.camera.view m.scene.referenceSystem
            { m with scene = { m.scene with surfacesModel = surfaceModel } }
        | Interactions.PickSurface, ViewerMode.Standard -> 
            let action = SurfaceAppAction.GroupsMessage(GroupsAppAction.SingleSelectLeaf(list.Empty, surf.guid, ""))
            let surfaceModel' = 
                SurfaceApp.update
                   m.scene.surfacesModel action m.scene.scenePath m.navigation.camera.view m.scene.referenceSystem
            { m with scene = { m.scene with surfacesModel = surfaceModel' } }
        //| Interactions.PickMinervaFilter, ViewerMode.Standard ->
        //    let action = PRo3D.Minerva.QueryAction.SetFilterLocation p |> PRo3D.Minerva.MinervaAction.QueryMessage
        //    let minerva = MinervaApp.update m.navigation.camera.view m.frustum m.minervaModel action
        //    { m with minervaModel = minerva }
        //| Interactions.PickLinking, ViewerMode.Standard ->
        //    Log.startTimed "Pick Linking - filter"
        //    let filtered = m.minervaModel.session.filteredFeatures |> IndexList.map (fun f -> f.id) |> IndexList.toList |> HashSet.ofList
        //    Log.stop()

        //    Log.startTimed "Pick Linking - checkPoint"
        //    let linkingAction, minervaAction = LinkingApp.checkPoint p filtered m.linkingModel
        //    Log.stop()

        //    Log.startTimed "Pick Linking - update minerva"
        //    let minerva' = MinervaApp.update m.navigation.camera.view m.frustum m.minervaModel minervaAction
        //    Log.stop()

        //    Log.startTimed "Pick Linking - update linking"
        //    let linking' = LinkingApp.update m.linkingModel linkingAction
        //    Log.stop()

        //    { m with linkingModel = linking'; minervaModel = minerva' }
        | Interactions.TrueThickness, ViewerMode.Standard -> m
        //    let msg = PlaneExtrude.App.Action.PointsMsg(Utils.Picking.Action.AddPoint p)
        //    let pe = PlaneExtrude.App.update m.scene.referenceSystem m.scaleTools.planeExtrude msg
        //    { m with scaleTools = { m.scaleTools with planeExtrude = pe  } }
        | Interactions.PlaceValidator, ViewerMode.Standard -> 
            let heightVal = 
                HeightValidatorApp.update 
                    m.heighValidation 
                    m.scene.referenceSystem.up.value 
                    m.scene.referenceSystem.north.value
                    (HeightValidatorAction.PlaceValidator p)

            { m with heighValidation = heightVal }
        | Interactions.PlaceScaleBar, _ ->
            let msg = ScaleBarsAction.AddScaleBar(p, m.scaleBarsDrawing, m.navigation.camera.view)
            let scm = ScaleBarsApp.update m.scene.scaleBars msg m.scene.referenceSystem
            { m with scene = { m.scene with scaleBars = scm } }
        | Interactions.PlaceSceneObject, ViewerMode.Standard -> 
            //let action = (SceneObjectAction.PlaceSceneObject(p))
            let action = (SceneObjectAction.TranslationMessage( TransformationApp.Action.SetPickedTranslation(p)))
            let sobjs = SceneObjectsApp.update m.scene.sceneObjectsModel action m.scene.referenceSystem
            { m with scene = { m.scene with sceneObjectsModel = sobjs } }
        | Interactions.PickPivotPoint, ViewerMode.Standard -> 
            match m.pivotType with
            | PickPivot.SurfacePivot     -> 
                let action = (SurfaceAppAction.TranslationMessage( TransformationApp.Action.SetPickedPivotPoint p )) 
                let surfaceModel =
                    SurfaceApp.update m.scene.surfacesModel action m.scene.scenePath m.navigation.camera.view m.scene.referenceSystem
                { m with scene = { m.scene with surfacesModel = surfaceModel } }
            //| PickPivot.ScaleBarPivot    -> 
            //    let action = (ScaleBarsAction.TranslationMessage( TransformationApp.Action.SetPickedPivotPoint p )) 
            //    let scaleBars' =
            //        ScaleBarsApp.update m.scene.scaleBars action 
            //    { m with scene = { m.scene with scaleBars = scaleBars' } }
            | PickPivot.SceneObjectPivot -> 
                let action = (SceneObjectAction.TranslationMessage( TransformationApp.Action.SetPickedPivotPoint p )) 
                let so' =
                    SceneObjectsApp.update m.scene.sceneObjectsModel action m.scene.referenceSystem
                { m with scene = { m.scene with sceneObjectsModel = so' } }
            | _ -> m
        | Interactions.PickSurfaceRefSys, ViewerMode.Standard -> 
            match m.pivotType with
            | PickPivot.SurfacePivot     -> 
                let action = (SurfaceAppAction.TranslationMessage( TransformationApp.Action.SetPickedReferenceSystem p )) 
                let surfaceModel =
                    SurfaceApp.update m.scene.surfacesModel action m.scene.scenePath m.navigation.camera.view m.scene.referenceSystem
                { m with scene = { m.scene with surfacesModel = surfaceModel } }
            | PickPivot.SceneObjectPivot -> m
                //todo
            | _ -> m
        | Interactions.PickDistanePoint, _ ->
            let msg = ViewPlanApp.Action.AddDistancePoint(p)
            let outerModel, viewPlans = ViewPlanApp.update m.scene.viewPlans msg _navigation _footprint m.scene.scenePath m.scene.referenceSystem m
            { m with scene = { m.scene with viewPlans = viewPlans } }
        | _ -> m       

    let mutable lastHash = -1    
    let mutable rememberCam = FreeFlyController.initial.view

    let private shortFeedback (text: string) (m: Model) = 
        let feedback = {
            id      = System.Guid.NewGuid().ToString()
            text    = text
            timeout = 3000
            msg     = ViewerAction.NoAction ""
        }
        m |> UserFeedback.queueFeedback feedback

    let getDrawingActionForKey (interaction : Interactions) (k : Aardvark.Application.Keys) = 
        match k with
        | Aardvark.Application.Keys.Enter    -> DrawingAction.Finish
        | Aardvark.Application.Keys.Back     -> DrawingAction.RemoveLastPoint
        | Aardvark.Application.Keys.Escape   -> DrawingAction.ClearWorking
        | Keyboard.Modifier -> 
            match interaction with 
            | Interactions.DrawAnnotation -> DrawingAction.StartDrawing
            | Interactions.PickAnnotation -> DrawingAction.StartPicking
            | _ -> DrawingAction.Nop
        //| Aardvark.Application.Keys.LeftShift -> 
        //    match m.interaction with                     
        //    | Interactions.PickAnnotation -> DrawingAction.StartPickingMulti
        //    | _ -> DrawingAction.Nop
        | Aardvark.Application.Keys.D0 -> DrawingAction.SetSemantic Semantic.Horizon0
        | Aardvark.Application.Keys.D1 -> DrawingAction.SetSemantic Semantic.Horizon1
        | Aardvark.Application.Keys.D2 -> DrawingAction.SetSemantic Semantic.Horizon2
        | Aardvark.Application.Keys.D3 -> DrawingAction.SetSemantic Semantic.Horizon3 
        | _  -> DrawingAction.Nop

    let updateFrustumAspect m (windowSize : V2i) (viewPortSizeId : string) =
            let aspect = float windowSize.X / float windowSize.Y
            let m = 
                { m with frustum = m.frustum 
                                   |> FrustumUtils.withAspect(aspect); 
                         viewPortSizes = HashMap.add viewPortSizeId windowSize m.viewPortSizes 
                         aspect  = aspect}
            let m = // update FurstumModel to keep it consistent with Frustum in Model
                m |> Optic.map _frustumModel (fun fm -> 
                    {fm with frustum = FrustumUtils.withAspect aspect fm.frustum})
            m


    let addFlyToSurfaceAnimation (m : Model) (id : SurfaceId) =
        let surf = m |> Optic.get _sgSurfaces |> HashMap.tryFind id
        let surface = m.scene.surfacesModel.surfaces.flat |> HashMap.find id |> Leaf.toSurface 
        let observedSystem = Gis.GisApp.getSpiceReferenceSystem m.scene.gisApp id
        let observerSystem = Gis.GisApp.getObserverSystem m.scene.gisApp
        let fullTrafo = TransformationApp.fullTrafo' surface.transformation m.scene.referenceSystem observedSystem observerSystem //SurfaceTransformations.fullTrafo' surface m.scene.referenceSystem
        match surface.homePosition with
        | Some hp ->                        
            let animationMessage = 
                CameraAnimations.animateForwardAndLocation hp.Location hp.Forward hp.Up 2.0 "ForwardAndLocation2s"
            AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))
        | None ->
            match surf with
            | Some s ->
                let bb = s.globalBB.Transformed(fullTrafo.Forward)
                let view = CameraView.lookAt bb.Max bb.Center m.scene.referenceSystem.up.value    
                let animationMessage = 
                    CameraAnimations.animateForwardAndLocation view.Location view.Forward view.Up 2.0 "ForwardAndLocation2s"
                let a' = AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))
                a'
            | None -> m.animations

    let updateViewer 
        (runtime   : IRuntime) 
        (signature : IFramebufferSignature) 
        (sendQueue : BlockingCollection<string>) 
        (mailbox   : MessagingMailbox) 
        (m         : Model) 
        (msg       : ViewerAction) =
        //Log.line "[Viewer_update] %A inter:%A pick:%A" msg m.interaction m.picking
        match msg, m.interaction, m.ctrlFlag with
        | NavigationMessage  msg,_,false when (isGrabbed m |> not) && (not (AnimationApp.shouldAnimate m.animations)) ->                
            let c   = m.scene.config
            let ref = m.scene.referenceSystem
            let nav = Navigation.update c ref navConf true m.navigation msg               
             
            //m.scene.navigation.camera.view.Location.ToString() |> NoAction |> ViewerAction |> mailbox.Post
             
            m 
            |> Optic.set _navigation nav
            |> Optic.set _animationView nav.camera.view
        | NavigationMessage msg, _, _ ->
            m // cases where navigation is blocked by other operations (e.g. animation)
        | AnimationMessage msg,_,_ -> // belongs to deprecated animation
            let m = 
                match msg with
                | Tick t when AnimationApp.shouldAnimate m.animations -> 
                    match IndexList.tryAt 0 m.animations.animations with
                    | Some anim -> 
                        // initialize animation (if needed)
                        let (anim,localTime,state) = AnimationApp.updateAnimation m.animations t anim
                        match anim.sample(localTime, t) state with 
                        | None -> // animation stops
                            // do updates to model
                            m
                        | Some (s,cameraView) -> 
                            m
                    | None -> m
                | _ -> m
            let a = AnimationApp.update m.animations msg
            { m with animations = a } |> Optic.set _view a.cam
        | SetCamera cv,_,false -> Optic.set _view cv m
        | SetCameraAndFrustum (cv, hfov, _),_,false -> 
            Log.warn "[Viewer] SetCameraAndFrustum not implemented!"
            m
        | SetCameraAndFrustum2 (cv,frustum),_,false ->
            let m = Optic.set _view cv m
            { m with frustum = frustum }
        | SetFrustum frustum,_,_ -> 
            Log.line "[Viewer] Setting Frustum %s" (string frustum)
            let frustumModel = 
                Optic.get _frustumModel m
            let frustumModel =
                {frustumModel with frustum = frustum}
            { m with frustum = frustum}
            |> Optic.set _frustumModel frustumModel 
        | AnnotationGroupsMessageViewer msg,_,_ ->
            let ag = m.drawing.annotations 
                
            { m with drawing = { m.drawing with annotations = GroupsApp.update ag msg}}
        | DrawingMessage msg,_,_-> //Interactions.DrawAnnotation
            match msg with
            | Drawing.FlyToAnnotation id ->
                let _a = m |> Optic.get _flat |> HashMap.tryFind id |> Option.map Leaf.toAnnotation
                match _a with 
                | Some a ->
                    
                    //let animationMessage = 
                    //    animateFowardAndLocation hp.Location hp.Forward hp.Up 2.0 "ForwardAndLocation2s"
                    //AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))

                    let animationMessage = 
                        CameraAnimations.animateForwardAndLocation a.view.Location a.view.Forward a.view.Up 2.0 "ForwardAndLocation2s"
                    let a' = AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))
                    { m with  animations = a'}
                | None -> m
            | Drawing.PickAnnotation (hit,id) when m.interaction = Interactions.DrawLog && m.ctrlFlag ->
                match DrawingApp.intersectAnnotation hit id m.drawing.annotations.flat with
                | Some (anno, point) ->           
                    //let pickingAction, msg =
                    //    match m.correlationPlot.logginMode, m.correlationPlot.correlationPlot.selectedLogNuevo with
                    //    | LoggingMode.PickReferencePlane, None ->
                    //        (CorrelationPanelsMessage.LogPickReferencePlane anno.key), "pick reference plane"
                    //    | LoggingMode.PickLoggingPoints, None ->
                    //        (CorrelationPanelsMessage.LogAddSelectedPoint(anno.key, point)), "add points to log"
                    //    | _, Some _->
                    //        (CorrelationPanelsMessage.LogAddPointToSelected(anno.key, point)), "changed log point"
                    //    | _ -> 
                    //        CorrelationPanelsMessage.Nop, ""
                    //let cp = 
                    //    CorrelationPanelsApp.update
                    //        m.correlationPlot       
                    //        m.scene.referenceSystem
                    //        pickingAction
                                                        
                    //{ m with correlationPlot = cp } |> shortFeedback msg
                    m
                | None -> m
                
            | _ ->
                let view = 
                    match m.viewerMode with 
                    | ViewerMode.Standard -> m.navigation.camera.view
                    | ViewerMode.Instrument -> m.scene.viewPlans.instrumentCam

                let drawing = 
                    DrawingApp.update m.scene.referenceSystem drawingConfig None sendQueue view m.shiftFlag m.drawing msg

                { m with drawing = drawing; } |> stash
        | SurfaceActions msg,_,_ ->
            
            let view = m.navigation.camera.view
            let s = SurfaceApp.update m.scene.surfacesModel msg m.scene.scenePath view m.scene.referenceSystem
            let animation = 
                match msg with
                | SurfaceAppAction.FlyToSurface id -> 
                    addFlyToSurfaceAnimation m id
                | _-> m.animations
            let model = { m with scene = { m.scene with surfacesModel = s}; animations = animation}

            let model =
                match msg with
                | SurfaceAppAction.ChangeImportDirectories _ 
                | SurfaceAppAction.ChangeOBJImportDirectories _ ->
                    let surfacesModel = 
                        model.scene.surfacesModel 
                        |> SceneLoader.prepareSurfaceModel 
                            runtime signature model.scene.scenePath
                    let model =
                        Optic.set _surfacesModel surfacesModel model
                    let sequencedBookmarks = 
                        match m.scene.sequencedBookmarks.bookmarks.IsEmpty with
                        | true ->
                            m.scene.sequencedBookmarks
                        | false ->                            
                            SequencedBookmarksApp.withSurfaceModel 
                                surfacesModel model.scene.sequencedBookmarks
                    let model =
                        Optic.set _sequencedBookmarks sequencedBookmarks model
                    model
                | _ -> 
                    model
            model
            
        | AnnotationMessage msg,_,_ ->                
            match m.drawing.annotations.singleSelectLeaf with
            | Some selected ->                             
                let f = (fun x ->
                    let a = x |> Leaf.toAnnotation
                    let a = AnnotationProperties.update m.scene.referenceSystem a msg

                    //update true thickness computation on dip angle change
                    let a = 
                        if (a.geometry = Geometry.TT) then                                                         
                           let up = m.scene.referenceSystem.up.value
                           let north = m.scene.referenceSystem.north.value
                           let planet = m.scene.referenceSystem.planet
                           
                           let results = Calculations.calcResultsLine a up north planet |> Some
                           { a with results = results }
                        else
                            a                    
                    a |> Leaf.Annotations)

                let a = m.drawing.annotations |> Groups.updateLeaf selected f
                Optic.set _annotations a m
            | None -> m       
        | BookmarkMessage msg,_,_ ->  
            Log.warn "[Viewer] bookmarks animation %A" m.navigation.camera.view.Location

            let m', bm = Bookmarks.update m.scene.bookmarks m.scene.referenceSystem.planet msg _navigation m
            let animation = 
                match msg with
                | BookmarkAction.GroupsMessage k ->
                    match k with 
                    | GroupsAppAction.UpdateCam _->                      
                        createAnimation 
                            m'.navigation.camera.view.Location
                            m'.navigation.camera.view.Forward
                            m'.navigation.camera.view.Up
                            { m.animations with cam = m.navigation.camera.view } 
                    | _ -> m.animations
                | _ -> m.animations
            
            { m with scene = { m.scene with bookmarks = bm }; animations = animation} //; navigation = m'.scene.navigation }} 
        | BookmarkUIMessage msg,_,_ ->    
            let bm = GroupsApp.update m.scene.bookmarks msg
            { m with scene = { m.scene with bookmarks = bm }} 
        | SequencedBookmarkMessage msg,_,_ ->
            let m, bm = 
                SequencedBookmarksApp.update 
                    m.scene.sequencedBookmarks
                    msg bookmarkLenses m
            let m = 
                {m with scene = { m.scene with sequencedBookmarks = bm }}
            
            let jsonPathName = SequencedBookmarksApp.outputPath bm
            let generateJson () = 
                let snapshotAnimation = 
                    SnapshotAnimation.fromBookmarks 
                        bm
                        m.scene.cameraView 
                        (m.frustum |> Frustum.horizontalFieldOfViewInDegrees)
                        m.scene.config.nearPlane.value
                        m.scene.config.farPlane.value
                SnapshotAnimation.writeToFile snapshotAnimation jsonPathName   
            let generateSnapshots = 
                SequencedBookmarksApp.generateSnapshots m.scene.sequencedBookmarks
                                                        SnapshotUtils.runProcess
            let generateDepthPanoramaJson () = 
                let jsonDepthPanoramasPathName = Path.combine [bm.outputPathDepthImages;"panoramaInputFormat.json"]
                let parnoamaCollection = 
                    SnapshotAnimation.PanoramaCollection 
                        {
                            fieldOfView             = (m.frustum |> Frustum.horizontalFieldOfViewInDegrees)            
                            nearplane               = m.scene.config.nearPlane.value              
                            farplane                = m.scene.config.farPlane.value               
                            resolution              = V2i(bm.resolutionX.value, bm.resolutionY.value)             
                            panoramaKind            = PanoramaKind.Spherical         
                            renderRgbWithoutOverlay = false
                            renderDepth             = true
                            renderRgbWithOverlay    = true
                            snapshots               = 
                                bm.bookmarks
                                |> HashMap.toList
                                |> List.choose (fun s ->
                                    let id, sbm = s
                                    match sbm with
                                    | SequencedBookmark.LoadedBookmark sb ->
                                        let snapshotCam = 
                                                {   location = sb.cameraView.Location
                                                    forward  = sb.cameraView.Forward
                                                    up       = sb.cameraView.Up
                                                    }                         
                                        Some {
                                            filename = sb.filename
                                            camera   = snapshotCam
                                        }
                                    | SequencedBookmark.NotYetLoaded sb -> 
                                        None
                                )
                        }
                let serialised = 
                    parnoamaCollection
                        |> Json.serialize 
                        |> Json.formatWith JsonFormattingOptions.Pretty 
                try 
                    System.IO.File.WriteAllText(jsonDepthPanoramasPathName , serialised)
                with e ->
                    Log.warn "[JsonChiron] Could not save %s" "panoramaInputFormat.json" 
                    Log.warn "%s" e.Message

                Log.warn "Debug Saved json to %s" (jsonDepthPanoramasPathName)
            let generateDepthPanoramas =
                SequencedBookmarksApp.generateDepthPanoramas m.scene.sequencedBookmarks
                                                        SnapshotUtils.runProcess

            let save m =
                let scenePath = 
                    match m.scene.scenePath with
                    | Some scenePath -> scenePath
                    | _ -> "snapshotScene.pro3d" 
                let m = {m with scene = {m.scene with scenePath = scenePath |> Some}}
                Log.line "[Snapshots] Saving scene as %s." scenePath
                let m = m |> ViewerIO.saveEverything scenePath
                m, scenePath

            let m =
                Animation.Animator.update (Animation.AnimatorMessage.RealTimeTick) m
                
            match msg with
            | SequencedBookmarksAction.StopRecording -> 
                let m, scenePath = save m
                generateJson ()
                Log.line "[Viewer] Writing snapshot JSON file to %s" jsonPathName
                let m = shortFeedback "Saved snapshot JSON file." m
                match m.scene.sequencedBookmarks.generateOnStop with
                | true -> 
                    let m  = 
                        let bm = generateSnapshots scenePath
                        {m with scene = { m.scene with sequencedBookmarks = bm }}
                    let m = shortFeedback "Snapshot generation started." m
                    m
                | false -> m
                        
            | SequencedBookmarksAction.GenerateSnapshots -> 
                let m, scenePath = save m
                let m = shortFeedback "Snapshot generation started." m
                match m.scene.sequencedBookmarks.updateJsonBeforeRendering with
                | true -> generateJson () | false -> ()
                let bm = generateSnapshots scenePath
                {m with scene = { m.scene with sequencedBookmarks = bm }}
            | SequencedBookmarksAction.UpdateJson ->
                let m, scenePath = save m
                generateJson ()
                let m = shortFeedback "Saved snapshot JSON file." m
                m

            | SequencedBookmarksAction.GeneratePanoramaDepthImages -> 
                let m, scenePath = save m
                generateDepthPanoramaJson()
                let m = shortFeedback "Panorama Depthimages generation started." m
                let bm = generateDepthPanoramas scenePath
                {m with scene = { m.scene with sequencedBookmarks = bm }}
            | _ -> m
                
            
        | RoverMessage msg,_,_ ->
            let roverModel = RoverApp.update m.scene.viewPlans.roverModel msg
            { m with scene = { m.scene with viewPlans = {m.scene.viewPlans with roverModel = roverModel }}}
        | ViewPlanMessage msg,_,_ ->
            let model, viewPlanModel = ViewPlanApp.update m.scene.viewPlans msg _navigation _footprint m.scene.scenePath m.scene.referenceSystem m

            let animations = 
                match msg with
                | ViewPlanApp.Action.FlyToViewPlan vp ->

                    //TODO refactor, strange flyto approach
                    let view = model.navigation.camera.view
                    let animationMessage = 
                        CameraAnimations.animateForwardAndLocation view.Location view.Forward view.Up 2.0 "ForwardAndLocation2s"

                    AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))   
                | _ ->
                    m.animations             

            { model with 
                scene = { model.scene with viewPlans = viewPlanModel }
                footPrint = model.footPrint
                animations = animations
            } 
        | DnSColorLegendMessage msg,_,_ ->
            let cm = FalseColorLegendApp.update m.drawing.dnsColorLegend msg
            { m with drawing = { m.drawing with dnsColorLegend = cm } }
        | SceneObjectsMessage msg,_,_ -> 
            let sobjs = SceneObjectsApp.update m.scene.sceneObjectsModel msg m.scene.referenceSystem
            let animation = 
                match msg with
                | SceneObjectAction.FlyToSO id -> 
                    let sceneObj = sobjs.sceneObjects |> HashMap.find id
                    let observedSystem = Gis.GisApp.getSpiceReferenceSystem m.scene.gisApp id
                    let observerSystem = Gis.GisApp.getObserverSystem m.scene.gisApp
                    let superTrafo = TransformationApp.fullTrafo' sceneObj.transformation m.scene.referenceSystem observedSystem observerSystem //SceneObjectTransformations.fullTrafo' sceneObj.transformation m.scene.referenceSystem

                    let sgSo = sobjs.sgSceneObjects |> HashMap.find id
                    let bb = sgSo.globalBB.Transformed(sceneObj.preTransform.Forward * superTrafo.Forward)

                    let view = CameraView.lookAt bb.Max bb.Center m.scene.referenceSystem.up.value    
                    let animationMessage = 
                        CameraAnimations.animateForwardAndLocation view.Location view.Forward view.Up 2.0 "ForwardAndLocation2s"

                    AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))                    
                | _-> m.animations
                   
            { m with scene = { m.scene with sceneObjectsModel = sobjs}; animations = animation}
        | FrustumMessage msg,_,_ ->
            let frustumModel = FrustumProperties.update m.scene.config.frustumModel msg
            match msg with
            | FrustumProperties.Action.ToggleUseFocal ->
                if frustumModel.toggleFocal then
                    let fm = {frustumModel with oldFrustum = m.frustum}
                    let m = Optic.set _frustumModel fm m
                    { m with frustum = frustumModel.frustum}
                else
                    let m = Optic.set _frustumModel frustumModel m
                    { m with frustum = frustumModel.oldFrustum}
            | FrustumProperties.Action.UpdateFocal f ->
                if frustumModel.toggleFocal then
                    let frustum' = 
                        FrustumUtils.calculateFrustum' frustumModel.focal.value 
                                                       m.frustum.near 
                                                       m.frustum.far 
                                                       m.aspect
                    let m = Optic.set _frustumModel {frustumModel with frustum = frustum'} m
                    let m = { m with frustum = frustum'}
                    m
                else
                    Optic.set _frustumModel frustumModel m
            | _ -> 
                Optic.set _frustumModel frustumModel m
        | ImportSurface sl,_,_ ->                 
            match sl with
            | [] -> m
            | paths ->
                let surfaces = 
                    paths 
                    |> List.filter (fun x -> Files.isSurfaceFolder x || Files.isZippedOpcFolder x)
                    |> List.map (SurfaceUtils.mk SurfaceType.SurfaceOPC MeshLoaderType.Unkown m.scene.config.importTriangleSize.value)
                    |> IndexList.ofList

                let m = SceneLoader.import' runtime signature surfaces m 
                m
                |> ViewerIO.loadLastFootPrint
                |> updateSceneWithNewSurface    
        | DiscoverAndImportOpcs sl,_,_ -> 
            //"" |> UpdateUserFeedback |> ViewerAction |> mailbox.Post
            match sl with
            | [] -> m
            | paths ->
                //"Import OPCs..." |> UpdateUserFeedback |> ViewerAction |> mailbox.Post
                let selectedPaths = paths |> List.choose Files.tryDirectoryExists
                let surfacePaths = 
                    selectedPaths
                    |> List.map Files.superDiscoveryMultipleSurfaceFolder
                    |> List.concat

                let surfaces = 
                    surfacePaths 
                    |> List.filter (fun x -> Files.isSurfaceFolder x || Files.isZippedOpcFolder x)
                    |> List.map (SurfaceUtils.mk SurfaceType.SurfaceOPC MeshLoaderType.Unkown m.scene.config.importTriangleSize.value)
                    |> IndexList.ofList

                    
                //gale crater hook
                let surfaces = GaleCrater.hack surfaces
                let surfaces = Jezero.hack surfaces

                let m = SceneLoader.import' runtime signature surfaces m 
                //updateSceneWithNewSurface m
                m
                |> ViewerIO.loadLastFootPrint
                |> updateSceneWithNewSurface
        | ImportDiscoveredSurfacesThreads sl,_,_ -> 
            if sl.Length > 0 then
                let feedback = {
                    id      = System.Guid.NewGuid().ToString()
                    text    = "Importing OPCs..."
                    timeout = 5000
                    msg     = (DiscoverAndImportOpcs sl)
                }
                    
                m |> UserFeedback.queueFeedback feedback
            else 
                let feedback = {
                    id      = System.Guid.NewGuid().ToString()
                    text    = "cancelled"
                    timeout = 3000
                    msg     = ViewerAction.Nop
                }
                    
                m |> UserFeedback.queueFeedback feedback
        | ImportObject (preferredLoader, objPaths), _, _ -> 
            match objPaths  with
            | [path] ->

                let objects =                   
                    path 
                    |> SurfaceUtils.mk SurfaceType.Mesh preferredLoader m.scene.config.importTriangleSize.value
                    |> IndexList.single                                
                m 
                |> SceneLoader.importObj preferredLoader objects 
                |> ViewerIO.loadLastFootPrint
                |> updateSceneWithNewSurface     
            | _ -> 
                Log.line "[Viewer] can only import exactly one file, given: %d" (List.length objPaths)
                m     
        | ImportSceneObject sl,_,_ -> 
            match sl |> List.tryHead with
            | Some path ->  
                let sceneObjects =                   
                    path 
                    |> SceneObjectsUtils.mk 
                    |> IndexList.single                                
                m 
                |> SceneLoader.importSceneObj sceneObjects
            | None -> m              
        | ImportPRo3Dv1Annotations sl,_,_ ->
            match sl |> List.tryHead with
            | Some path -> 
                try 
                    let imported, flat, lookup = 
                        AnnotationGroupsImporter.import path m.scene.referenceSystem

                    let newGroups = 
                        m.drawing.annotations.rootGroup.subNodes
                        |> IndexList.append imported

                    let flat = 
                        flat 
                        |> HashMap.map(fun _ v ->
                            let a = v |> Leaf.toAnnotation
                            
                            (if a.geometry = Geometry.DnS then { a with showDns = true } else a)
                            |> Leaf.Annotations
                        )    
                                          
                    let newflat = m.drawing.annotations.flat |> HashMap.union flat

                    //let inline median input = 
                    //  let sorted = input |> Seq.toArray |> Array.sort
                    //  let m1,m2 = 
                    //      let len = sorted.Length-1 |> float
                    //      len/2. |> floor |> int, len/2. |> ceil |> int 
                    //  (sorted.[m1] + sorted.[m2] |> float)/2.

                    //let stuff = 
                    //  flat 
                    //    |> HashMap.toList 
                    //    |> List.map snd 
                    //    |> List.map Leaf.toAnnotation
                    //    |> List.filter(fun x -> x.geometry = Geometry.DnS)
                    //    |> List.map(fun x -> x.points |> DipAndStrike.calculateDnsErrors)
                    //    |> List.concat
                      
                    //let avg = stuff |> List.average
                    //let med = stuff |> List.toArray |> median
                    //let std = DipAndStrike.computeStandardDeviation avg stuff
                    //Log.line "%f %f %f" std avg med
                      
                    //let result = stuff |> List.map(fun x -> { error = x } )

                    //let csvTable = Csv.Seq.csv ";" true id result
                    //Csv.Seq.write ("./error.csv") csvTable |> ignore

                    m 
                    |> Optic.set _groups newGroups
                    |> Optic.set _lookUp lookup
                    |> Optic.set _flat newflat
                with 
                | e -> Log.error "[Viewer] %A" e; m
            | None -> m
        | ImportSurfaceTrafo sl,_,_ ->  
            match sl |> List.tryHead with
            | Some path ->
                let imported = 
                    SurfaceTrafoImporter.startImporter path
                    |> IndexList.toList

                let s = 
                    m.scene.surfacesModel 
                    |> SurfaceApp.updateSurfaceTrafos imported                

                m |> Optic.set SceneLoader._surfaceModelLens s  
            | None -> m
        | ImportRoverPlacement sl,_,_ ->  
            match sl |> List.tryHead with
            | Some path -> 
                let importedData = RoverPlacementImporter.startRPImporter path
                match m.scene.viewPlans.roverModel.selectedRover with
                | Some r -> 
                    let vp = ViewPlanApp.createViewPlanFromFile importedData m.scene.viewPlans r m.scene.referenceSystem m.navigation.camera.view
                    { m with scene = { m.scene with viewPlans = vp }}
                | None -> Log.error "no rover selected"; m
            | None -> m     
        | ImportTraverse traverseFiles,_,_ -> 
            let t = TraverseApp.update m.scene.traverses (TraverseAction.LoadTraverses traverseFiles)
            { m with scene = { m.scene with traverses = t }}
        | DeleteLast,_,_ -> 
            if File.Exists @".\last" then
                File.Delete(@".\last") |> ignore
                m
            else 
                m
        | ViewerAction.PickSurface (p,name,true), _ ,true ->
            let fray = p.globalRay.Ray
            let r = fray.Ray
            let rayHash = r.GetHashCode()              

            let computeExactPick = true // CHECK-merge

            if computeExactPick then    // CHECK-merge

                //// hack
                //let pp = m.navigation.exploreCenter
                //let navigation' = 
                //    Navigation.update m.scene.config m.scene.referenceSystem navConf true m.navigation (Navigation.Action.ArcBallAction(ArcBallController.Message.Pick pp))
                let fray = p.globalRay.Ray
                let r = fray.Ray
                let rayHash = r.GetHashCode()

                let observerSystem = Gis.GisApp.getObserverSystem m.scene.gisApp
                let observedSystem (v : SurfaceId) = Gis.GisApp.getSpiceReferenceSystem m.scene.gisApp v
                
                if rayHash = lastHash then
                    Log.line "ray hash took over"
                    m
                else          
                    Log.startTimed "[PickSurface] try intersect kdtree of %s" name       
                         
                    let onlyActive (id : Guid) (l : Leaf) (s : SgSurface) = l.active
                    let onlyVisible (id : Guid) (l : Leaf) (s : SgSurface) = l.visible
                    let visibleAndActive (id : Guid) (l : Leaf) (s : SgSurface) = l.visible && l.active

                    let surfaceFilter = 
                        match m.interaction with
                        | Interactions.PickSurface -> visibleAndActive
                        | _ -> onlyActive

                    let hitF (camLocation : V3d) (p : V3d) = 
                        let ray =
                            match m.drawing.projection with
                            | Projection.Viewpoint -> 
                                let dir = (p-camLocation).Normalized
                                FastRay3d(camLocation, dir)  
                            | Projection.Sky -> 
                                let up = m.scene.referenceSystem.up.value
                                FastRay3d(p + (up * 5000.0), -up)  
                            | _ -> Log.error "projection started without proj mode"; FastRay3d()
                   
                        match SurfaceIntersection.doKdTreeIntersection (Optic.get _surfacesModel m) m.scene.referenceSystem observedSystem observerSystem ray surfaceFilter cache with
                        | Some (t,surf), c ->                             
                            cache <- c; ray.Ray.GetPointOnRay t |> Some
                        | None, c ->
                            cache <- c; None
                                   
                    let result = 
                        match SurfaceIntersection.doKdTreeIntersection (Optic.get _surfacesModel m) m.scene.referenceSystem observedSystem observerSystem fray surfaceFilter cache with
                        | Some (t,surf), c ->                         
                            cache <- c
                            let hit = r.GetPointOnRay(t)

                            Log.line "[PickSurface] surface hit at %A" hit

                            let cameraLocation = m.navigation.camera.view.Location //navigation'.camera.view.Location 
                            let hitF = hitF cameraLocation
                   
                            lastHash <- rayHash

                            let observedSystem = observedSystem surf.guid
                            let spiceTrafo = 
                                match observedSystem, observerSystem with
                                | Some observedSystem, Some observerSystem -> 
                                    CooTransformation.transformBody observedSystem.body (Some observedSystem.referenceFrame) observerSystem.body observerSystem.referenceFrame observerSystem.time
                                    |> Option.map (fun t -> t.Trafo) 
                                    |> Option.defaultValue Trafo3d.Identity
                                | _ -> Trafo3d.Identity

                            let toLocal (v : V3d) = spiceTrafo.Backward.TransformPos(v)

                            matchPickingInteraction sendQueue hit observedSystem (hitF >> Option.map toLocal) surf m                                    
                        | None, _ -> 
                            Log.error "[PickSurface] no hit of %s" name
                            m

                    Log.stop()
                    Log.line "[PickSurface] done intersecting"
                     
                    result
            else m
        | PickObject (p,id), _ ,_ ->  
            match m.picking with
            | true ->
                let hitF _ = None
                let observedSystem = Gis.GisApp.getSpiceReferenceSystem m.scene.gisApp id
                match (m.scene.surfacesModel.surfaces.flat.TryFind id) with
                | Some x -> matchPickingInteraction sendQueue p observedSystem hitF (x |> Leaf.toSurface) m 
                | None -> m
            | false -> m
        | SaveScene s, _,_ ->                 
            let target = match m.scene.scenePath with | Some path -> path | None -> s
            m |> ViewerIO.saveEverything target
        | SaveAs s,_,_ ->
            ViewerIO.saveEverything s m
            |> ViewerIO.loadLastFootPrint
        | ViewerAction.SetScenePath s, _, _ -> 
            let scene = { m.scene with scenePath      = Some s }
            { m with scene = scene }

        | LoadScene path,_,_ ->                

            match SceneLoading.loadSceneFromFile m runtime signature path with
            | SceneLoading.SceneLoadResult.Loaded(newModel,converted,path) -> 
                Log.line "[PRo3D] loaded scene: %s" path
                newModel
            | SceneLoading.SceneLoadResult.Error(msg,exn) -> 
                Log.error "[PRo3D] could not load file: %s, error: %s" path msg
                m

            //|> ViewerIO.loadMinerva SceneLoader.Minerva.defaultDumpFile SceneLoader.Minerva.defaultCacheFile
            |> SceneLoader.addGeologicSurfaces     
            
        | LoadSerializedScene json, _, _ -> // serialized scene file (content of .pro3d)
            SceneLoading.loadSceneFromJson m runtime signature json

        | LoadSerializedDrawingModel json, _, _ -> 
            let annotations = DrawingUtilities.IO.loadAnnotationsFromJson json 
            ViewerIO.replaceAnnotations m annotations
        | ImportSerializedDrawingModel(json, source), _, _ -> 
            let drawing = DrawingUtilities.IO.loadAnnotationsFromJson json 
            let annotations = drawing.annotations |> GroupsModel.patchNames (fun n -> Guid.NewGuid())
            let importedGroup = { annotations.rootGroup with name = source }

            //let msg = GroupsAppAction.AddAndSelectGroup ([], importedGroup)
            let groups = 
                { m.drawing.annotations with 
                    flat = HashMap.union annotations.flat m.drawing.annotations.flat; 
                    rootGroup = { m.drawing.annotations.rootGroup with subNodes = IndexList.prepend importedGroup m.drawing.annotations.rootGroup.subNodes }}

            let model = { m with drawing = { m.drawing with annotations = groups } }
            model
        | ImportDrawingModel(drawing, source), _, _ -> 
            let annotations = drawing |> GroupsModel.patchNames (fun n -> Guid.NewGuid())
            let importedGroup = { annotations.rootGroup with name = source }

            //let msg = GroupsAppAction.AddAndSelectGroup ([], importedGroup)
            let groups = 
                { m.drawing.annotations with 
                    flat = HashMap.union annotations.flat m.drawing.annotations.flat; 
                    rootGroup = { m.drawing.annotations.rootGroup with subNodes = IndexList.prepend importedGroup m.drawing.annotations.rootGroup.subNodes }}

            let model = { m with drawing = { m.drawing with annotations = groups } }
            model
         

        | NewScene,_,_ ->
            let initialModel = 
                Viewer.initial
                    m.messagingMailbox 
                    StartupArgs.initArgs 
                    m.renderingUrl 
                    m.numberOfSamples 
                    m.screenshotDirectory
                    _animator
                    m.viewerVersion

            { initialModel with recent = m.recent} |> ViewerIO.loadRoverData

        | KeyDown k, _, _ ->
            let m =
                match k with
                | Aardvark.Application.Keys.LeftShift -> 
                    let m = { m with shiftFlag = true}
                    Log.line "[Viewer] ShiftFlag %A" m.shiftFlag
                    m
                | _ -> m
          

            let m =
                match k with 
                | Keyboard.Modifier ->
                    match m.interaction with
                    //| Interactions.PickMinervaProduct -> 
                    //    { m with minervaModel = { m.minervaModel with picking = true }; ctrlFlag = true}
                    |_ -> { m with ctrlFlag = true}
                | _ -> m

            let m =
                match (m.ctrlFlag, k, m.scene.scenePath) with
                | true, Aardvark.Application.Keys.S, Some path -> 
                    { (ViewerIO.saveEverything path m) with ctrlFlag = false } |> shortFeedback "scene saved"
                | true, Aardvark.Application.Keys.S, None ->         
                    { m with ctrlFlag = false } |> shortFeedback "please use \"save\" in the menu to save the scene" 
                    // (saveSceneAndAnnotations p m)
                |_-> m
                                   
            let sensitivity = m.scene.config.navigationSensitivity.value
          
            let configAction = 
                match k with 
                | Aardvark.Application.Keys.PageUp   -> ConfigProperties.Action.SetNavigationSensitivity (Numeric.Action.SetValue (sensitivity + 0.5))
                | Aardvark.Application.Keys.PageDown -> ConfigProperties.Action.SetNavigationSensitivity (Numeric.Action.SetValue (sensitivity - 0.5))
                | _ -> ConfigProperties.Action.SetNavigationSensitivity (Numeric.Action.SetValue (sensitivity))

            let c' = ConfigProperties.update m.scene.config configAction

            let kind = 
                match k with
                | Aardvark.Application.Keys.F1 -> TrafoKind.Translate
                | Aardvark.Application.Keys.F2 -> TrafoKind.Rotate
                | _ -> m.trafoKind

            let m = { m with trafoKind = kind }

            let interaction' = 
                match k with
                | Aardvark.Application.Keys.F1 -> Interactions.PickExploreCenter
                | Aardvark.Application.Keys.F2 -> Interactions.DrawAnnotation
                | Aardvark.Application.Keys.F3 -> Interactions.PickAnnotation
                | Aardvark.Application.Keys.F4 -> Interactions.PlaceCoordinateSystem                
                | _ -> m.interaction

            let m =
                match k with 
                | Aardvark.Application.Keys.F6 ->
                    let waypointPath = 
                        Path.combine [
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); 
                            "Pro3D"
                            "JR"
                            "CooTransformationConfig"
                            "M20_waypoints.json"
                        ]

                    let t = TraverseApp.update m.scene.traverses (TraverseAction.LoadTraverses [waypointPath])
                    { m with scene = { m.scene with traverses = t }}
                | _ -> m

            { m with scene = { m.scene with config = c' }; interaction = interaction'}
        | KeyUp k, _,_ ->               
            let m =
                match k with
                | Aardvark.Application.Keys.LeftShift -> 
                    let m = { m with shiftFlag = false}
                    Log.line "[Viewer] ShiftFlag %A" m.shiftFlag
                    m                    
                | _ -> m

            match k with
            | Keyboard.Modifier -> 
                match m.interaction with
                | Interactions.DrawAnnotation -> 
                    let view = m.navigation.camera.view
                    let d = DrawingApp.update m.scene.referenceSystem drawingConfig None sendQueue view m.shiftFlag m.drawing DrawingAction.StopDrawing
                    { m with drawing = d; ctrlFlag = false; picking = false }
                | Interactions.PickAnnotation -> 
                    let view = m.navigation.camera.view
                    let d = DrawingApp.update m.scene.referenceSystem drawingConfig None sendQueue view m.shiftFlag m.drawing DrawingAction.StopPicking 
                    { m with drawing = d; ctrlFlag = false; picking = false }
                //| Interactions.PickMinervaProduct -> { m with minervaModel = { m.minervaModel with picking = false }}
                |_-> { m with ctrlFlag = false; picking = false }
            | _ -> m                                  
        | SetInteraction t,_,_ -> 
                
            // let feedback = sprintf "pick refrence plane; confirm with ENTER" t |> UserFeedback.create 3000
            //let feedback = "pick refrence plane \n confirm with ENTER" |> UserFeedback.create 3000

            { m with interaction = t } //|> UserFeedback.queueFeedback feedback
        | ReferenceSystemMessage a,_,_ ->                                
            let refsystem',_ = 
                ReferenceSystemApp.update
                    m.scene.config 
                    LenseConfigs.referenceSystemConfig 
                    m.scene.referenceSystem 
                    a
                    
            let _refSystem = (Model.scene_ >-> Scene.referenceSystem_)
            let m = 
                m 
                |> Optic.set _refSystem refsystem'
                |> SceneLoader.updateCameraUp            

            //changing the reference system also requires adaptation of angular measurement values
            Log.startTimed "[Viewer.fs] recalculating angular values in annos"
            let flat = 
                m.drawing.annotations.flat
                |> HashMap.map(fun _ v ->
                    let a = v |> Leaf.toAnnotation
                    let results = Calculations.calculateAnnotationResults a refsystem'.up.value refsystem'.northO refsystem'.planet
                    
                    //Calculations.reCalcBearing a refsystem'.up.value refsystem'.northO                   
                    let dnsResults = DipAndStrike.reCalculateDipAndStrikeResults refsystem'.up.value refsystem'.northO a
                    { a with results = Some results; dnsResults = dnsResults } 
                    |> Leaf.Annotations
                )
            Log.stop()
            
            m
            |> Optic.set _flat flat            
            

            //match a with 
            //| ReferenceSystemAction.SetUp _ | ReferenceSystemAction.SetPlanet _ ->
            //    m' 
            //    |> SceneLoader.updateCameraUp
            //| ReferenceSystemAction.SetNOffset _ -> //update annotation results
            //    let flat = 
            //        m'.drawing.annotations.flat
            //        |> HashMap.map(fun _ v ->
            //            let a = v |> Leaf.toAnnotation
            //            let results    = Calculations.reCalcBearing a refsystem'.up.value refsystem'.northO                         
            //            let dnsResults = DipAndStrike.reCalculateDipAndStrikeResults refsystem'.up.value refsystem'.northO a
            //            { a with results = results; dnsResults = dnsResults } 
            //            |> Leaf.Annotations
            //        )
            //    m' 
            //    |> Optic.set _flat flat                     
            //| _ -> 
            //    m'
        | ConfigPropertiesMessage a,_,_ -> 
            //Log.line "config message %A" a
            let c' = ConfigProperties.update m.scene.config a
            let m = Optic.set (Model.scene_ >-> Scene.config_) c' m
    
            match a with                   
            | ConfigProperties.Action.SetNearPlane _ | ConfigProperties.Action.SetFarPlane _ ->
                let fov = m.frustum |> Frustum.horizontalFieldOfViewInDegrees
                let asp = m.frustum |> Frustum.aspect
                let f' = Frustum.perspective fov c'.nearPlane.value c'.farPlane.value asp                    

                { m with frustum = f' }
            | _ -> m
        | SetMode d,_,_ -> 
            { m with trafoMode = d }
        | SetKind d,_,_ -> 
            { m with trafoKind = d }
        | TransforAdaptiveSurface (guid, trafo),_,_ ->
            //transforAdaptiveSurface m guid trafo //TODO moved function?
            m
        //| TransformAllSurfaces surfaceUpdates,_,_ -> //TODO MarsDL Hera
        //    match surfaceUpdates.IsEmptyOrNull () with
        //    | false ->
        //        transformAllSurfaces m surfaceUpdates
        //    | true ->
        //        Log.line "[Viewer] No surface updates found."
        //        m
        //| TransformAllSurfaces (surfaceUpdates,scs),_,_ ->
        //    match surfaceUpdates.IsEmptyOrNull () with
        //    | false ->
        //       //transformAllSurfaces m surfaceUpdates
        //       let ts = m.scene.surfacesModel.surfaces.activeGroup
        //       let action = SurfaceApp.Action.GroupsMessage(Groups.Groups.Action.ClearGroup ts.path)
        //       let surfM = SurfaceApp.update m.scene.surfacesModel action m.scene.scenePath m.scene.navigation.camera.view m.scene.referenceSystem 
        //       let m' = { m with scene = { m.scene with surfacesModel = surfM }}
        //       ViewerUtils.placeMultipleOBJs2 m' scs
        //    | true ->
        //        Log.line "[Viewer] No surface updates found."
        //        m
        | Translate (_,b),_,_ ->
            m
            //match _selectedSurface.Get(m) with
            //  | Some selected ->
            //    let sgSurf = m |> Lenses.get _sgSurfaces |> HashMap.find selected.id
            //    let s' = { sgSurf with trafo = TranslateController.updateController sgSurf.trafo b }
                                        
            //    m 
            //    |> Lenses.get _sgSurfaces
            //    |> HashMap.update selected.id (fun x -> 
            //        match x with 
            //            | Some _ -> printfn "%A" s'.trafo.previewTrafo.Forward.C3.XYZ; s'
            //            | None   -> failwith "surface not found")
            //    |> Lenses.set' _sgSurfaces m
                    
            //  | None -> m                               
        | Rotate (_,b),_,_ -> m
                //match _selectedSurface.Get(m) with
                //  | Some selected ->
                //    let sgSurf = m |> Lenses.get _sgSurfaces |> HashMap.find selected.id
                //    let s' = { sgSurf with trafo = RotationController.updateController sgSurf.trafo b }

                //    m 
                //    |> Lenses.get _sgSurfaces
                //    |> HashMap.update selected.id (fun x -> 
                //         match x with 
                //           | Some _ -> s'
                //           | None   -> failwith "surface not found")
                //    |> Lenses.set' _sgSurfaces m
                //  | None -> m
        | SetTabMenu tab,_,_ ->
            { m with tabMenu = tab }
        | SwitchViewerMode  vm ,_,_ -> 
            { m with viewerMode = vm }
        | NoAction s,_,_ -> 
            if s.IsEmptyOrNull() |> not then 
                Log.line "[Viewer.fs] No Action %A" s
            m                   
        | UpdateDockConfig dcf,_,_ ->
            let closedPages = updateClosedPages m dcf.content
            { m with scene = { m.scene with dockConfig = dcf; closedPages = closedPages } }
        | AddPage de,_,_ -> 
            let closedPages = m.scene.closedPages |> List.filter(fun x -> x.id <> de.id)                
            let cont = addDockElement m.scene.dockConfig.content de
            let dockconfig = config {content(cont);appName "PRo3D"; useCachedConfig false }
            { m with scene = { m.scene with dockConfig = dockconfig; closedPages = closedPages } }
        | UpdateUserFeedback s,_,_ ->   { m with scene = { m.scene with userFeedback = s } }
        | ChangeDashboardMode mode, _, _ -> 
            { m with scene = { m.scene with dockConfig = mode.dockConfig }; dashboardMode = mode.name }
        //| StartImportMessaging sl,_,_ -> 
        //    sl |> ImportDiscoveredSurfaces |> ViewerAction |> mailbox.Post
        //    { m with scene = { m.scene with userFeedback = "Import OPCs..." } }
        | Logging (text,message),_,_ ->  
            message |> MailboxAction.ViewerAction |> mailbox.Post
            { m with scene = { m.scene with userFeedback = text } }
        | ThreadsDone id,_,_ ->  
            { m with scene = { m.scene with userFeedback = ""; feedbackThreads = ThreadPool.remove id m.scene.feedbackThreads;} }
        //| SnapshotThreadsDone id,_,_ ->  
        //    let _m = 
        //        { m with arnoldSnapshotThreads = ThreadPool.remove id m.arnoldSnapshotThreads }
        //    _m
        //    //  _m.shutdown ()
                
        //| MinervaActions a,_,_ ->
        //    let currentView = m.navigation.camera.view
        //    match a with 
        //    | Minerva.MinervaAction.SelectByIds ids -> // AND fly to its center
        //        let minerva' = MinervaApp.update currentView m.frustum m.minervaModel a                                                                               
        //        // Fly to center of selected prodcuts
        //        let center = Box3d(minerva'.selectedSgFeatures.positions).Center
        //        let newForward = (center - currentView.Location).Normalized             
        //        { m with minervaModel = minerva'; animations = createAnimation center newForward currentView.Up m.animations }   
        //    | Minerva.MinervaAction.FlyToProduct v -> 
        //        let newForward = (v - currentView.Location).Normalized             
        //        { m with animations = createAnimation v newForward currentView.Up m.animations }
        //    | _ ->                
        //        let minerva' = MinervaApp.update currentView m.frustum m.minervaModel a                                                                               
        //        //let linking' = PRo3D.Linking.LinkingApp.update currentView m.frustum injectLinking (PRo3D.Linking.LinkingAction(a))
        //        //MailboxAction.ViewerAction
        //        //ViewerAction.MinervaActions
        //        //a |> ViewerAction.MinervaActions |> MailboxAction.ViewerAction
        //        { m with minervaModel = minerva' }
        //| LinkingActions a,_,_ ->
        //    match a with
        //    | PRo3D.Linking.LinkingAction.MinervaAction d ->
        //        { m with minervaModel = MinervaApp.update m.navigation.camera.view m.frustum m.minervaModel d }
        //    | PRo3D.Linking.LinkingAction.OpenFrustum d ->
        //        let linking' = PRo3D.Linking.LinkingApp.update m.linkingModel a         

        //        let camera' = { m.navigation.camera with view = CameraView.ofTrafo d.f.camTrafo }
        //        { m with navigation = { m.navigation with camera = camera' }; overlayFrustum = Some(d.f.camFrustum); linkingModel = linking' }
        //    | PRo3D.Linking.LinkingAction.CloseFrustum ->
        //        let linking' = PRo3D.Linking.LinkingApp.update m.linkingModel a
        //        //let camera' = { m.navigation.camera with view = rememberCam }
        //        { m with overlayFrustum = None; linkingModel = linking' } //navigation = { m.navigation with camera = camera' }}
        //    | _ -> 
        //        { m with linkingModel = PRo3D.Linking.LinkingApp.update m.linkingModel a }
        | OnResize (a,id),_,_ ->              
            Log.line "[RenderControl Resized] %A" a
            updateFrustumAspect m a id
        | ResizeMainControl(a,id),_,_ -> 
            printfn "[main] resize %A" (a,id)
            updateFrustumAspect m a id
        | ResizeInstrumentControl(a,id),_,_ -> 
            printfn "[instrument] resize %A" (a,id)
            updateFrustumAspect m a id
        //| SetTextureFiltering b,_,_ ->
        //    {m with filterTexture = b}
       // | TestHaltonRayCasting _,_,_->
            //ViewerUtils.placeMultipleOBJs2 m [SnapshotShattercone.TestData] // TODO MarsDL put in own app
        //| UpdateShatterCones shatterCones,_,_ -> // TODO MarsDL put in own app
        //// TODO: LAURA
        //    match shatterCones.IsEmptyOrNull () with
        //    | false ->
        //        let m' = addOrClearSnapshotGroup m
        //        ViewerUtils.placeMultipleOBJs2 m' shatterCones
        //    | true ->
        //        Log.line "[Viewer] No shattercone updates found."
        //        m
        | StartDragging _,_,_ ->
            let m' =
                match m.multiSelectBox with
                | Some x -> { m with multiSelectBox = None }
                | None -> m
            m'
        | Dragging _,_,_ ->
            let m' =
                match m.multiSelectBox with
                | Some x -> { m with multiSelectBox = None }
                | None -> m
            m'
        | EndDragging (mousePos, mouseButton) ,_,_ -> 
          let m' =
                match m.multiSelectBox with
                | Some x -> { m with multiSelectBox = None }
                | None -> m
          let m' = 
            {m' with navigation = {m'.navigation with camera = {m'.navigation.camera with pan = false }}}
          m'
        | MouseIn _,_,_ ->
            {m with navigation = {m.navigation with camera = {m.navigation.camera with pan = true }}}
        | MouseOut _,_,_ ->
            {m with navigation = {m.navigation with camera = {m.navigation.camera with pan = false }}}

       // | CorrelationPanelMessage a,_,_ ->
            //let blurg =
            //    match a with 
            //    | CorrelationPanelsMessage.SemanticAppMessage _ ->
            //        m |> PRo3D.ViewerIO.colorBySemantic'                    
            //    | _ -> 
            //        m
            //let blurg =
            //    { blurg with correlationPlot = CorrelationPanelsApp.update m.correlationPlot m.scene.referenceSystem a }

            //let blarg = 
            //    match a with
            //    | CorrelationPanelsMessage.CorrPlotMessage 
            //        (CorrelationPlotAction.DiagramMessage
            //            (Svgplus.DA.DiagramAppAction.DiagramItemMessage
            //                (diagramItemId, Svgplus.DiagramItemAction.RectangleStackMessage 
            //                    (_ , Svgplus.RectangleStackAction.RectangleMessage 
            //                        (_, Svgplus.RectangleAction.Select rid)))))
            //            ->          
            //        Log.line "[Viewer] corrplotmessage %A" blurg.correlationPlot.correlationPlot.selectedFacies

            //        let plot = blurg.correlationPlot.correlationPlot

            //        let selectionSet =
            //            match plot.selectedFacies with
            //            | Some selected -> 
            //                let selectedLogId : LogId = 
            //                    diagramItemId 
            //                    |> LogId.fromDiagramItemId

            //                let log = 
            //                    plot.logsNuevo |> HashMap.find selectedLogId


            //                // find facies
            //                match Facies.tryFindFacies selected log.facies with
            //                | Some facies ->


            //                    // find measurements
            //                    Log.line "[Viewer] selected facies has %A measurements" facies.measurements

            //                    // also make for aggregation stuff ... secondary log
                                    

            //                    facies.measurements 
            //                    |> HashSet.map(fun x -> 
            //                        {
            //                            id = ContactId.value x
            //                            path = []
            //                            name = ""
            //                        }
            //                    )
            //                | None ->
            //                    HashSet.empty
            //            | None -> 
            //                HashSet.empty

            //        // m.drawing.annotations.selectedLeaves
            //        { 
            //            blurg with 
            //                drawing = { 
            //                    blurg.drawing with 
            //                        annotations = { 
            //                            blurg.drawing.annotations with 
            //                                selectedLeaves = selectionSet
            //                        }
            //                }
            //        }
            //    | _ -> 
            //        blurg

            //blarg
           // m
        | ViewerAction.PickSurface _,_,_ ->
            m 
        | ViewerAction.HeightValidation a,_,false ->
            { m with heighValidation = HeightValidatorApp.update m.heighValidation m.scene.referenceSystem.up.value m.scene.referenceSystem.north.value a }
        //| _ -> 
        //    Log.warn "[Viewer] don't know message %A. ignoring it." msg
        //    m 
        | ScaleBarsDrawingMessage msg,_,_->    
            let scDrawing = ScaleBarsDrawing.update m.scaleBarsDrawing msg
            { m with scaleBarsDrawing = scDrawing }
        | ScaleBarsMessage msg,_,_->  
            match msg with
            | ScaleBarsAction.FlyToSB id ->
                let _sb = m |> Optic.get _scaleBars |> HashMap.tryFind id
                match _sb with 
                | Some sb ->
                    let translation = (TransformationApp.translationFromReferenceSystemBasis sb.transformation.translation.value m.scene.referenceSystem) 
                    let viewLocation = sb.view.Location + translation
                    let viewForward = sb.position + translation

                    let view = CameraView.lookAt viewLocation viewForward m.scene.referenceSystem.up.value    

                    let animationMessage = 
                        CameraAnimations.animateForwardAndLocation view.Location view.Forward view.Up 2.0 "ForwardAndLocation2s"
                    let a' = AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))
                    { m with  animations = a'}
                | None -> m
            | _ ->
                //let _scaleBarsModel = (Model.scene_ >-> Scene.scaleBars_ )
                let scaleBars' = ScaleBarsApp.update m.scene.scaleBars msg m.scene.referenceSystem
                let m' = m |> Optic.set _scaleBarsModel scaleBars'  
                m'
        | GeologicSurfacesMessage msg,_,_-> 
            match msg with
            | GeologicSurfaceAction.FlyToGS id ->
                let _gs = m |> Optic.get _geologicSurfaces |> HashMap.tryFind id
                match _gs with 
                | Some gs ->
                    let animationMessage = 
                        CameraAnimations.animateForwardAndLocation gs.view.Location gs.view.Forward gs.view.Up 2.0 "ForwardAndLocation2s"
                    let a' = AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))
                    { m with  animations = a'}
                | None -> m

            | GeologicSurfaceAction.AddGS ->
                let geologicSurfaces' = 
                    GeologicSurfacesUtils.makeGeologicSurfaceFromAnnotations
                        m.drawing.annotations
                        m.scene.geologicSurfacesModel
                
                m |> Optic.set _geologicSurfacesModel geologicSurfaces'
            | _ ->
                let geologicSurfaces' = GeologicSurfacesApp.update m.navigation.camera.view m.scene.geologicSurfacesModel msg
                let m' = m |> Optic.set _geologicSurfacesModel geologicSurfaces'  
                m'
        | ScreenshotMessage msg, _ , _ ->
            let screenshotModel = 
                ScreenshotApp.update 
                    m.renderingUrl 
                    m.numberOfSamples 
                    m.screenshotDirectory 
                    m.scene.screenshotModel
                    msg

            let m = {m with scene = { m.scene with screenshotModel = screenshotModel }}

            match msg with
            | ScreenshotAction.CreateScreenshot -> 
                shortFeedback "Screenshot saved" m
            | _ -> m
        | TraverseMessage msg, _ , _ ->
            let animation =
                match msg with
                | FlyToSol (forward, up, location) ->
                    //let _tr = m |> Optic.get _traverses |> HashMap.tryFind id
                    //match _tr with 
                    //| Some tr ->
                    let animationMessage = 
                        CameraAnimations.animateForwardAndLocation location forward up 2.0 "ForwardAndLocation2s"
                    AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))   
                    //| None -> m.animations
                                 
                | _ ->
                    m.animations

            let m =
                match msg with 
                | PlaceRoverAtSol (name, trafo, location, refSystem) ->
                    let vpMessage = ViewPlanApp.Action.CreateNewViewplan (name, trafo, location, refSystem)
                    let model, viewPlanModel = ViewPlanApp.update m.scene.viewPlans vpMessage _navigation _footprint m.scene.scenePath refSystem m
                    { m with 
                        scene = { m.scene with viewPlans = viewPlanModel }
                        footPrint = model.footPrint
                    } 
                | _ -> m                                        
                
            { m with scene = { m.scene with traverses = TraverseApp.update m.scene.traverses msg }; animations = animation }                        
        | StopGeoJsonAutoExport, _, _ -> 
            let autoExport = { m.drawing.automaticGeoJsonExport with enabled = not m.drawing.automaticGeoJsonExport.enabled; lastGeoJsonPathXyz = None; }
            { m with drawing = { m.drawing with automaticGeoJsonExport = autoExport } }
        | SetSceneState state, _, _ ->
            Optic.set _sceneState state m
        | LoadPoseDefinitionFile path, _, _ -> 
            let path = path.Head
            //SimulatedViews.PoseData.writeDummyData path // for debugging
            let poseData : PoseData = SimulatedViews.PoseData.read path
            let sceneState = Optic.get _sceneState m 
            let bookmarks = PoseData.toSequencedBookmarks poseData sceneState m.viewerVersion
                            |> SequencedBookmarksApp.addBookmarks m.scene.sequencedBookmarks
            let bookmarks = {bookmarks with poseDataPath = Some path}
            let m = Optic.set _sequencedBookmarks bookmarks m
            m
        | SBookmarksToPoseDefinition, _, _ -> //RNO for creating dummy data for testing batch rendering with pose files
            let poseData = PoseData.fromSequencedBookmarks m.scene.sequencedBookmarks 
            poseData
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.Chiron.writeToFile poseData.path
            m
        | WriteBookmarkMetadata (path, bm) , _, _ ->
            match bm.metadata with
            | Some md ->
                Log.line "[Viewer] Writing metadata to %s" path
                if m.startupArgs.verbose then
                    let timer = Stopwatch.StartNew()
                    System.IO.File.WriteAllText(path, md)
                    timer.Stop()
                    printfn "Finished writing file after %A milliseconds" timer.ElapsedMilliseconds
                else
                    System.IO.File.WriteAllText(path, md)
                m
            | None ->
                Log.line "[Viewer] No metadata for bookmark %s" bm.name
                m
        | WriteCameraMetadata (path, camera),_,_ ->
            m
        | GisAppMessage msg, _ , _ ->
            let m, gisApp = 
                 Gis.GisApp.update m.scene.gisApp gisLenses m msg

            let m =
                match msg with
                | Gis.GisAppAction.ObservationInfoMessage msg ->
                    match Gis.GisApp.lookAtObserver gisApp with
                    | Some newCamera -> 
                        //let p = 
                        //    m.scene.surfacesModel.surfaces.flat 
                        //    |> HashMap.map (fun k v -> 
                        //        let s = Leaf.toSurface v
                        //        match Gis.GisApp.getSurfaceTrafo gisApp k with
                        //        | Some trafo -> 
                        //            //let pose = { position = pos; rotation = Rot3d.Identity; scale = V3d.III }
                        //            //let trafo = { TrafoController.initial with pose = pose; previewTrafo = Pose.toTrafo pose; mode = TrafoMode.Local }
                        //            Leaf.Surfaces { 
                        //                s with transformation = {
                        //                    s.transformation with
                        //                        translation = s.transformation.translation |> Optic.set V3dInput.value_ trafo.position
                        //                }
                        //            }
                        //        | _ -> v
                        //    )
                        //let m = { m with scene = { m.scene with surfacesModel = { m.scene.surfacesModel with surfaces = { m.scene.surfacesModel.surfaces with flat = p }}}}
                        { m with navigation = { m.navigation with camera = { m.navigation.camera with view = newCamera } }}
                    | _ -> m
                | _ -> m

            let animations = 
                match msg with
                //| Gis.GisAppAction.ObservationInfoMessage msg ->
                //    match Gis.GisApp.lookAtObserver gisApp with
                //    | Some newCamera -> 
                //        let addObserverAnimation (m : Model) =
                //            let animationMessage = 
                //                CameraAnimations.animateForwardAndLocation newCamera.Location newCamera.Forward V3d.OOI 2.0 "TransitionCameraToObserver"
                //            let blub =
                //                CameraAnimations.
                //            AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage); AnimationAction.PushAnimation blub)
                //        addObserverAnimation m
                //    | _ -> 
                //        m.animations

                | Gis.GisAppAction.SurfacesMessage msg ->
                    match msg with
                    | SurfaceAppAction.FlyToSurface id ->
                        addFlyToSurfaceAnimation m id
                    | _ ->
                        m.animations
                | _ -> 
                    m.animations
            (Optic.set _gisApp gisApp m)
            |> Optic.set ViewerLenses._animation animations
        | unknownAction, _, _ -> 
            Log.line "[Viewer] Message not handled: %s" (string unknownAction)
            m       
                   
   //let mutable lastMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() //DEBUG
    let updateInternal 
        (runtime   : IRuntime) 
        (signature : IFramebufferSignature) 
        (sendQueue : BlockingCollection<string>) 
        (mailbox   : MessagingMailbox) 
        (m         : Model) 
        (msg       : ViewerAnimationAction) =

        match msg with
        | ViewerMessage msg ->
            updateViewer runtime signature sendQueue mailbox m msg
        | AnewmationMessage msg ->
            //match msg with // Debugging Info
            //| Anewmation.AnimatorMessage.RealTimeTick ->
            //    let millis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            //    Log.warn "Elapsed: %i" (millis - lastMillis)
            //    lastMillis <- millis
            //    ()
            //| _ -> 
            //    ()
            Animation.Animator.update msg m   

        | ProvenanceMessage msg -> 
            ProvenanceApp.update msg m
            
    let updateWithProvenanceTracking 
        (runtime   : IRuntime) 
        (enableProvenance : bool)
        (signature : IFramebufferSignature) 
        (sendQueue : BlockingCollection<string>) 
        (mailbox   : MessagingMailbox) 
        (m         : Model) 
        (msg       : ViewerAnimationAction) =
        let newModel = updateInternal runtime signature sendQueue mailbox m msg 
        if enableProvenance then
            ProvenanceApp.track m newModel msg
        else
            newModel

    let mkBrushISg color size trafo : ISg<Message> =
      Sg.sphere 5 color size 
        |> Sg.shader {
            do! Shader.stableTrafo
            do! DefaultSurfaces.vertexColor
            do! DefaultSurfaces.simpleLighting
        }
        |> Sg.noEvents
        |> Sg.trafo(trafo)
    
    let renderControlAttributes (id : string) (m: AdaptiveModel) = 

        let renderControlAtts (model: AdaptiveNavigationModel) =
            amap {
                let! state = model.navigationMode
                match state with
                | NavigationMode.FreeFly -> 
                    yield! FreeFlyController.extractAttributes model.camera Navigation.Action.FreeFlyAction
                | NavigationMode.ArcBall ->                         
                    yield! ArcBallController.extractAttributes model.camera Navigation.Action.ArcBallAction
                | _ -> 
                    failwith "Invalid NavigationMode"
            } 
            |> AttributeMap.ofAMap |> AttributeMap.mapAttributes (AttributeValue.map NavigationMessage)   
        
        AttributeMap.unionMany [
            renderControlAtts m.navigation
                |> AttributeMap.mapAttributes (AttributeValue.map ViewerMessage)

            AttributeMap.ofList [
                attribute "style" "width:100%; height: 100%; float:left; background-color: #222222"
                attribute "data-samples" (sprintf "%i" dataSamples)
                attribute "useMapping" "true"
                //attribute "showFPS" "true"        
                //attribute "data-renderalways" "true"
                Aardvark.UI.Events.onKeyDown' (fun k -> 
                    let drawingAction = getDrawingActionForKey (m.interaction |> AVal.force) k 
                    [KeyDown k; DrawingMessage drawingAction]
                )
                onKeyUp   (KeyUp)        
                clazz "mainrendercontrol"
                onEvent "resizeControl"  [] (
                    fun p -> 
                        match p with
                        | w::h::[] ->
                            let w : float = Pickler.json.UnPickleOfString w
                            let h : float = Pickler.json.UnPickleOfString h 
                            printfn "%A" (w,h)
                            ResizeMainControl(V2i(w,h),id)
                        | _ -> Nop 
                )] |> AttributeMap.mapAttributes (AttributeValue.map ViewerMessage)
                //onResize  (fun s -> OnResize(s,id))
            AttributeMap.ofList [
                onEvent "onRendered" [] (fun _ -> AnewmationMessage Animation.AnimatorMessage.RealTimeTick)                    
            ] 
        ]            

    let instrumentControlAttributes (id : string) (m: AdaptiveModel) = 
        AttributeMap.unionMany [
            AttributeMap.ofList [
                attribute "style" "width:100%; height: 100%; float:left; background-color: #222222"
                attribute "data-samples" "4"
                attribute "useMapping" "true"
                onKeyDown (KeyDown)
                onKeyUp (KeyUp)
                clazz "instrumentrendercontrol"
                onEvent "resizeControl"  [] (
                    fun p -> 
                        match p with
                        | w::h::[] -> 
                            let w : float = Pickler.json.UnPickleOfString w
                            let h : float = Pickler.json.UnPickleOfString h
                            printfn "%A" (w,h)
                            ResizeMainControl(V2i(w,h),id)
                        | _ -> Nop 
                )
            ] |> AttributeMap.mapAttributes (AttributeValue.map ViewerMessage) 
        ]     
        
    let allowAnnotationPicking (m : AdaptiveModel) =       
        // drawing app needs pickable stuff. however whether annotations are pickable depends on 
        // outer application state. we consider annotations to pickable if they are visible
        // and we are in "pick annotation" mode.
        m.interaction |> AVal.map (function  
            | Interactions.PickAnnotation -> true
            | Interactions.DrawLog -> true
            | _ -> false
        )

    let allowLogPicking (m : AdaptiveModel) =       
        // drawing app needs pickable stuff. however whether logs are pickable depends on 
        // outer application state. we consider annotations to pickable if they are visible
        // and we are in "pick annotation" mode.
        AVal.map2 (fun ctrlPressed interaction -> 
            match ctrlPressed, interaction with
            | true, Interactions.PickLog -> true
            | _ -> false
        ) m.ctrlFlag m.interaction

    // overlays that occur in instrumentview + main renderview
    let getOverlayed (m: AdaptiveModel) (view :aval<CameraView>) (frustum : aval<Frustum>) =
        let refSystem =
            Sg.view
                m.scene.config
                mrefConfig
                m.scene.referenceSystem
                view
            |> Sg.map ReferenceSystemMessage  

        let exploreCenter =
            Navigation.Sg.view m.navigation
            |> Sg.onOff m.scene.config.showExplorationPointGui
          
        let homePosition =
            Sg.viewHomePosition m.scene.surfacesModel

        let annotationTexts =
            DrawingApp.viewTextLabels 
                m.scene.config
                mdrawingConfig
                view 
                m.drawing            
            
        let scaleBarTexts = 
            ScaleBarsApp.Sg.viewTextLabels 
                m.scene.scaleBars 
                view 
                m.scene.config
                mrefConfig
                m.scene.referenceSystem

        let traverse = 
            TraverseApp.Sg.viewText 
                m.scene.referenceSystem
                view
                m.scene.config.nearPlane.value 
                (frustum |> AVal.map Frustum.horizontalFieldOfViewInDegrees)
                m.scene.traverses
            |> Sg.map TraverseMessage

        let distancePointsText =
            ViewPlanApp.Sg.viewText 
                m.scene.referenceSystem
                m.scene.viewPlans 
            |> Sg.map ViewPlanMessage    


        [
            exploreCenter; 
            refSystem; 
            homePosition;
            annotationTexts |> Sg.noEvents
            scaleBarTexts
            traverse
            distancePointsText
        ] |> Sg.ofList
                                 
    // depthTested that occur in instrumentview + main renderview
    let getDepthTested (frustum: aval<Frustum>) (view :aval<CameraView>) (observer : aval<ObserverSystem option>) (id : string) (runtime : IRuntime) (m: AdaptiveModel) =
        let annotations, discs = 
            DrawingApp.view 
                m.scene.config 
                mdrawingConfig
                observer
                view //m.navigation.camera.view 
                frustum
                runtime
                (m.viewPortSizes |> AMap.tryFind id |> AVal.map (Option.defaultValue V2i.II))
                (allowAnnotationPicking m)                 
                m.drawing
         
        let annotationSg =
            let ds =
                discs
                |> Sg.map DrawingMessage
                |> Sg.fillMode (AVal.constant FillMode.Fill)
                |> Sg.cullMode (AVal.constant CullMode.None)

            let annos = 
                annotations
                |> Sg.map DrawingMessage
                |> Sg.fillMode (AVal.constant FillMode.Fill)
                |> Sg.cullMode (AVal.constant CullMode.None)
                
            Sg.ofList [ds;annos;]

        let scaleBars =
            ScaleBarsApp.Sg.view
                m.scene.scaleBars
                view //m.navigation.camera.view
                m.scene.config
                mrefConfig
                m.scene.referenceSystem
            |> Sg.map ScaleBarsMessage
       
        let traverses = 
            [ 
                TraverseApp.Sg.viewLines m.scene.referenceSystem m.scene.traverses
                TraverseApp.Sg.view     
                    view //m.navigation.camera.view
                    m.scene.referenceSystem
                    m.scene.traverses   
            ]
            |> Sg.ofList
            |> Sg.map TraverseMessage

        let distancePoints =
            ViewPlanApp.Sg.viewVPDistancePoints 
                m.scene.referenceSystem
                m.scene.viewPlans 
            |> Sg.map ViewPlanMessage    

        [
            scaleBars;
            annotationSg
            traverses
            distancePoints
        ] |> Sg.ofList

    let viewInstrumentView (runtime : IRuntime) (id : string) (m: AdaptiveModel) = 
        let frustum = m.scene.viewPlans.instrumentFrustum
        let observer = Gis.GisApp.getObserverSystemAdaptive m.scene.gisApp
        let icam = AVal.map2 Camera.create (m.scene.viewPlans.instrumentCam) m.scene.viewPlans.instrumentFrustum

        let ioverlayed = getOverlayed m m.scene.viewPlans.instrumentCam frustum
       
        let depthTested = getDepthTested frustum m.scene.viewPlans.instrumentCam observer id runtime m

        // instrument view control
        let icmds = ViewerUtils.renderCommands m.scene.surfacesModel.sgGrouped ioverlayed depthTested m.scene.viewPlans.instrumentCam false true runtime m // m.scene.surfacesModel.sgGrouped overlayed discs m
                        |> AList.map ViewerUtils.mapRenderCommand
        
        //onBoot "attachResize('__ID__')" (
        //    DomNode.RenderControl((renderControlAttributes id m), cam, cmds, None)
        //)
        onBoot "attachResize('__ID__')" (
            DomNode.RenderControl((instrumentControlAttributes id m), icam, icmds, None) //AttributeMap.Empty
        )

    let viewRenderView (runtime : IRuntime) (id : string) (m: AdaptiveModel) = 

        let frustum = AVal.map2 (fun o f -> o |> Option.defaultValue f) m.overlayFrustum m.frustum // use overlay frustum if Some()
        let cam     = AVal.map2 Camera.create m.navigation.camera.view frustum

        
        let gisEntities = Gis.GisApp.viewGisEntities m.scene.gisApp |> Sg.noEvents

        let observer = Gis.GisApp.getObserverSystemAdaptive m.scene.gisApp

        let overlayed =
                        
            let createLabelBillboards (model : amap<string, V3d>) (view:aval<CameraView>) (near:aval<float>) =        
                model
                |> AMap.map(fun txt pos ->
                   Sg.text view near 
                      ~~60.0
                      ~~pos
                      ~~(Trafo3d.Translation pos)
                      ~~20.0
                      ~~txt
                      ~~C4b.White
                ) 
                |> AMap.toASet  
                |> ASet.map(fun x -> snd x)            
                |> Sg.set

            //let alignment = 
            //    AlignmentApp.view m.alignment m.scene.navigation.camera.view
            //        |> Sg.map AlignmentActions
            //        |> Sg.fillMode (AVal.constant FillMode.Fill)
            //        |> Sg.cullMode (AVal.constant CullMode.None)

            let near = m.scene.config.nearPlane.value

            let overL = getOverlayed m m.navigation.camera.view frustum

            let leafLabels =
                m.scene.config.showLeafLabels 
                |> AVal.map (fun enabled -> 
                    if enabled then
                        Sg.viewLeafLabels ~~0.01 ~~60.0 m.navigation.camera.view m.scene.surfacesModel
                    else 
                        Sg.empty
                )
                |> Sg.dynamic
                                 
            let viewPlans =
                ViewPlanApp.Sg.view 
                    m.scene.config 
                    mrefConfig 
                    m.scene.viewPlans 
                    m.navigation.camera.view
                |> Sg.map ViewPlanMessage           

            //let solText = 
            //    MinervaApp.getSolBillboards m.minervaModel m.navigation.camera.view near |> Sg.map MinervaActions
                
            //let correlationLogs, _ =
            //    PRo3D.Correlations.CorrelationPanelsApp.viewWorkingLog 
            //        m.scene.config.dnsPlaneSize.value
            //        m.scene.cameraView 
            //        near 
            //        m.correlationPlot 
            //        m.drawing.dnsColorLegend

            //let finishedLogs, _ =
            //    PRo3D.Correlations.CorrelationPanelsApp.viewFinishedLogs 
            //        m.scene.config.dnsPlaneSize.value
            //        m.scene.cameraView 
            //        near 
            //        m.drawing.dnsColorLegend 
            //        m.correlationPlot 
            //        (allowLogPicking m)

            //let traverse = 
            //    [ 
            //        TraverseApp.Sg.viewLines m.scene.traverses
            //        TraverseApp.Sg.viewText 
            //            m.navigation.camera.view
            //            m.scene.config.nearPlane.value 
            //            m.scene.traverses
            //    ]
            //    |> Sg.ofList
            //    |> Sg.map TraverseMessage
           
            let heightValidation =
                HeightValidatorApp.view m.heighValidation |> Sg.map HeightValidation            
            
            let orientationCube = PRo3D.OrientationCube.Sg.view m.navigation.camera.view m.scene.config m.scene.referenceSystem

           
            [
                overL;
                viewPlans; 
                leafLabels;
             //   solText; 
                heightValidation;
                //traverse
                //gisEntities
            ] |> Sg.ofList // (correlationLogs |> Sg.map CorrelationPanelMessage); (finishedLogs |> Sg.map CorrelationPanelMessage)] |> Sg.ofList // (*;orientationCube*) //solText

        //let minervaSg =
        //    let minervaFeatures = 
        //        MinervaApp.viewFeaturesSg m.minervaModel |> Sg.map MinervaActions 

        //    let filterLocation =
        //        MinervaApp.viewFilterLocation m.minervaModel |> Sg.map MinervaActions

        //    Sg.ofList [minervaFeatures] //;filterLocation]

        //let all = m.minervaModel.data.features
        //let selected = 
        //    m.minervaModel.session.selection.highlightedFrustra
        //    |> AList.ofASet
        //    |> AList.toAVal 
        //    |> AVal.map (fun x ->
        //        x
        //        |> IndexList.take 500
        //    )
        //    |> AList.ofAVal
        //    |> ASet.ofAList
        
        //let linkingSg = 
        //    PRo3D.Linking.LinkingApp.view 
        //        m.minervaModel.hoveredProduct 
        //        selected 
        //        m.linkingModel
        //    |> Sg.map LinkingActions

        let depthTested = 
            getDepthTested frustum m.navigation.camera.view observer id runtime m //annotations + scaleBars

        let heightValidationDiscs =
            HeightValidatorApp.viewDiscs m.heighValidation |> Sg.map HeightValidation

        
        let sceneObjects =
            SceneObjectsApp.Sg.view m.scene.sceneObjectsModel m.scene.referenceSystem |> Sg.map SceneObjectsMessage

        let geologicSurfacesSg = 
            GeologicSurfacesApp.Sg.view m.scene.geologicSurfacesModel 
            |> Sg.map GeologicSurfacesMessage 


        //let traverses =
        //    TraverseApp.Sg.view     
        //        m.navigation.camera.view
        //        m.scene.referenceSystem
        //        m.scene.traverses   
        //    |> Sg.map TraverseMessage

        let depthTested = 
            [
             //   linkingSg; 
                depthTested; 
                //minervaSg; 
                heightValidationDiscs; 
                sceneObjects; 
                geologicSurfacesSg
                //traverses
                gisEntities
            ] |> Sg.ofList


        //render OPCs in priority groups
        let cmds  = 
            ViewerUtils.renderCommands 
                m.scene.surfacesModel.sgGrouped 
                overlayed depthTested 
                m.navigation.camera.view 
                true 
                false 
                runtime 
                m
            |> AList.map ViewerUtils.mapRenderCommand
        onBoot "attachResize('__ID__')" (
            DomNode.RenderControl((renderControlAttributes id m), cam, cmds, None)
        )
        
    let view (runtime : IRuntime) (m: AdaptiveModel) = //(localhost: string)

        let viewerDependencies = [
            { kind = Stylesheet;  name = "semui";           url = "https://cdn.jsdelivr.net/semantic-ui/2.2.6/semantic.min.css" }
            { kind = Stylesheet;  name = "semui-overrides"; url = "./resources/semui-overrides.css" }
            { kind = Script;      name = "semui";           url = "https://cdn.jsdelivr.net/semantic-ui/2.2.6/semantic.min.js" }
            { kind = Script;      name = "errorReporting";  url = "./resources/errorReporting.js"  }
            { kind = Script;      name = "resize";  url = "./resources/ResizeSensor.js"  }
            { kind = Script;      name = "resizeElem";  url = "./resources/ElementQueries.js"  }
            { kind = Script;      name = "utilities";  url = "./resources/utilities.js"  }
        ]
        
        let bodyAttributes : list<Attribute<ViewerAnimationAction>> = [
            style "background: #1B1C1E; height:100%; overflow-y:scroll; overflow-x:hidden;" //] //overflow-y : visible
            onMouseUp (fun button pos -> ViewerAnimationAction.ViewerMessage (EndDragging (pos, button)))
        ]

        page (
            fun request -> 
                Gui.Pages.pageRouting viewerDependencies bodyAttributes m viewInstrumentView viewRenderView runtime request
        )
                           
    let threadPool (m: Model) =
        let unionMany xs = List.fold ThreadPool.union ThreadPool.empty xs

        let drawing =
            DrawingApp.threads m.drawing |> ThreadPool.map DrawingMessage
       
        let animation = 
            AnimationApp.ThreadPool.threads m.animations |> ThreadPool.map AnimationMessage

        let nav =
            match m.navigation.navigationMode with
            | NavigationMode.FreeFly -> 
                FreeFlyController.threads m.navigation.camera
                |> ThreadPool.map Navigation.FreeFlyAction |> ThreadPool.map NavigationMessage
            | NavigationMode.ArcBall ->
                ArcBallController.threads m.navigation.camera
                |> ThreadPool.map Navigation.ArcBallAction |> ThreadPool.map NavigationMessage
            | _ -> failwith "invalid nav mode"
         
      //  let minerva = MinervaApp.threads m.minervaModel |> ThreadPool.map MinervaActions

        let sBookmarks = SequencedBookmarksApp.threads m.scene.sequencedBookmarks |> ThreadPool.map SequencedBookmarkMessage

        unionMany [drawing; animation; nav; m.scene.feedbackThreads; sBookmarks]
            |> ThreadPool.map ViewerMessage
            |> ThreadPool.union (
                Animation.Animator.threads m.animator 
                                    |> ThreadPool.map AnewmationMessage)
        
    let loadWaypoints m = 
        match Serialization.fileExists "./waypoints.wps" with
        | Some path -> 
            let wp = Serialization.loadAs<IndexList<WayPoint>> path
            { m with waypoints = wp }
        | None -> m

    type ViewerStartupLoad =
        | Empty
        | LoadLastScene
        | LoadScene of string

    let start 
        (runtime             : IRuntime) 
        (signature           : IFramebufferSignature)
        (startupLoad         : ViewerStartupLoad)
        (messagingMailbox    : MessagingMailbox)
        (sendQueue           : BlockingCollection<string>)
        (dumpFile            : string)
        (cacheFile           : string)
        (renderingUrl        : string)
        (dataSamples         : int)
        (enableProvenance    : bool)
        (screenshotDirectory : string)
        (viewerVersion       : string)
        =

        let viewerInitial =
            PRo3D.Viewer.Viewer.initial 
                messagingMailbox 
                StartupArgs.initArgs 
                renderingUrl
                dataSamples 
                screenshotDirectory 
                _animator 
                viewerVersion
                
        let m = 
            match startupLoad with
            | Empty -> 
                viewerInitial
                |> ProvenanceApp.emptyWithModel enableProvenance
                |> ViewerIO.loadRoverData
            | LoadLastScene ->
                viewerInitial
                |> ProvenanceApp.emptyWithModel enableProvenance
                |> SceneLoader.loadLastScene runtime signature                
                |> SceneLoader.loadLogBrush
                |> ViewerIO.loadRoverData                
                |> ViewerIO.loadAnnotations
                |> ViewerIO.loadCorrelations
                |> ViewerIO.loadLastFootPrint
                |> ViewerIO.loadSequencedBookmarks
                //|> ViewerIO.loadMinerva dumpFile cacheFile
                //|> ViewerIO.loadLinking
                |> SceneLoader.addScaleBarSegments
                |> SceneLoader.addGeologicSurfaces
            | LoadScene path ->
                viewerInitial
                |> ProvenanceApp.emptyWithModel enableProvenance
                |> SceneLoader.loadSceneFromFile runtime signature path
                |> SceneLoader.loadLogBrush
                |> ViewerIO.loadRoverData                
                |> ViewerIO.loadAnnotations
                |> ViewerIO.loadCorrelations
                |> ViewerIO.loadLastFootPrint
                |> ViewerIO.loadSequencedBookmarks
                //|> ViewerIO.loadMinerva dumpFile cacheFile
                //|> ViewerIO.loadLinking
                |> SceneLoader.addScaleBarSegments
                |> SceneLoader.addGeologicSurfaces
                
        let app = {
            unpersist = Unpersist.instance
            threads   = threadPool
            view      = view runtime //localhost
            update    = updateWithProvenanceTracking runtime enableProvenance signature sendQueue messagingMailbox
            initial   = m
        }
        app.startAndGetState()

