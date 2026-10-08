namespace PRo3D.Lite

open System
open System.Collections.Concurrent
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Primitives.Golden
open Aardvark.UI.Animation
open Aardvark.UI.Animation.Deprecated
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Composition

/// PRo3D Lite's update: every case either delegates to a PRo3D.Core sub-app or to the
/// PRo3D.Composition glue the full viewer uses as well. What is Lite's own is only the
/// wiring and the file handling.
module LiteApp =

    /// The tools Lite offers - the interactions PickRouting shares - with their tool-strip icon
    /// and tooltip, in strip order (a divider goes wherever the colour group changes).
    let tools =
        [
            Interactions.DrawAnnotation,        "pencil",                 "Draw annotation"
            Interactions.PickAnnotation,        "mouse pointer",          "Select annotation"
            Interactions.EditAnnotation,        "move",                   "Edit annotation - move the control points of the selected annotation"
            Interactions.PickSurface,           "mouse pointer",          "Select surface"
            Interactions.PlaceCoordinateSystem, "pro3d-coordinate-cross", "Place coordinate cross"
            Interactions.PickExploreCenter,     "bullseye",               "Pick the ArcBall orbit centre"
        ]

    let defaultLayout =
        layout {
            row {
                element {
                    id "render"
                    title "3D View"
                    weight 3
                }
                column {
                    stack {
                        // the tools get twice the height of the properties below them
                        weight 2
                        element {
                            id "annotations"
                            title "Annotations"
                        }
                        element {
                            id "surfaces"
                            title "Surfaces"
                        }
                        element {
                            id "scene"
                            title "Scene"
                        }
                    }
                    element {
                        id "properties"
                        title "Properties"
                    }
                }
            }
        }

    /// The view config with its frustum matching the configured near and far plane - the
    /// frustum is what the 3D view renders with.
    let private withConsistentFrustum (config : ViewConfigModel) =
        { config with frustumModel = { config.frustumModel with frustum = ConfigProperties.withClipPlanes config config.frustumModel.frustum } }

    let initial (userPrefs : UserPreferences) : LiteModel =
        let config = ViewConfigModel.initial |> withConsistentFrustum
        let navigation = HostConfigs.initialNavigation
        {
            surfaces     = SurfaceModel.initial
            drawing      = DrawingModel.initialdrawing
            navigation   = navigation
            refSystem    = ReferenceSystem.initial
            config       = config
            animations   =
                {
                    animations = IndexList.empty
                    animation  = Animate.On
                    cam        = navigation.camera.view
                }
            golden       = GoldenLayout.create LayoutConfig.Default defaultLayout

            interaction  = Interactions.DrawAnnotation
            ctrlFlag     = false
            shiftFlag    = false
            viewPortSize = V2i(1024, 768)

            scenePath    = None
            status       = "Import an OPC to begin (Surfaces > Import OPC)."

            userPrefs    = userPrefs
            sceneExtras  = None
            kdCache      = SurfacePicking.KdTreeCache()
        }

    /// Tools own the left mouse button while Ctrl is held; otherwise the camera has it.
    let toolArmed (m : LiteModel) = m.ctrlFlag

    let private mouseScheme (m : LiteModel) : Navigation.MouseScheme =
        { directToolMode = false; ctrlFlag = m.ctrlFlag }

    let private withStatus (text : string) (m : LiteModel) = { m with status = text }

    let private withStatusOption (text : Option<string>) (m : LiteModel) =
        match text with
        | Some t -> withStatus t m
        | None -> m

    /// Keeps position and viewing direction, re-ups the camera to the (new) sky of the frame.
    let private updateCameraUp (m : LiteModel) =
        let v = m.navigation.camera.view
        let view = ReferenceSystem.bodyAwareLookAt m.refSystem v.Location (v.Location + v.Forward)
        { m with navigation = { m.navigation with camera = { m.navigation.camera with view = view } } }

    let private setCameraView (view : CameraView) (m : LiteModel) =
        { m with
            navigation = { m.navigation with camera = { m.navigation.camera with view = view } }
            animations = { m.animations with cam = view } }

    let private flyTo (view : CameraView) (m : LiteModel) =
        let animation = CameraAnimations.animateForwardAndLocation view.Location view.Forward view.Up 2.0 "ForwardAndLocation2s"
        { m with animations = AnimationApp.update m.animations (AnimationAction.PushAnimation animation) }

    /// The world bounding box of one surface (its placed scene-graph box).
    let private surfaceBox (m : LiteModel) (id : Guid) : Option<Box3d> =
        match m.surfaces.surfaces.flat |> HashMap.tryFind id, m.surfaces.sgSurfaces |> HashMap.tryFind id with
        | Some leaf, Some sg ->
            let surface = Leaf.toSurface leaf
            let trafo = TransformationApp.fullTrafo' surface.transformation m.refSystem None None
            Some (sg.globalBB.Transformed trafo.Forward)
        | _ -> None

    /// Looking from the box corner at its centre, upright in the frame.
    let private viewOnto (m : LiteModel) (bb : Box3d) =
        ReferenceSystem.bodyAwareLookAt m.refSystem bb.Max bb.Center

    /// The camera that frames every visible surface, if there is any.
    let homeView (m : LiteModel) : Option<CameraView> =
        let boxes =
            m.surfaces.surfaces.flat
            |> HashMap.toList
            |> List.choose (fun (id, leaf) -> if leaf.visible then surfaceBox m id else None)
        match boxes with
        | [] -> None
        | boxes -> boxes |> List.fold (fun (all : Box3d) b -> all.Union b) Box3d.Invalid |> viewOnto m |> Some

    let private flyToSurface (id : Guid) (m : LiteModel) =
        let surface = m.surfaces.surfaces.flat |> HashMap.tryFind id |> Option.map Leaf.toSurface
        match surface |> Option.bind (fun s -> s.homePosition) with
        | Some hp -> flyTo hp m
        | None ->
            match surfaceBox m id with
            | Some bb -> flyTo (viewOnto m bb) m
            | None -> m

    /// The point under the screen centre, for ArcBall's implicit orbit centre.
    let private pickCenter (m : LiteModel) () =
        let v = m.navigation.camera.view
        let ray = FastRay3d(v.Location, v.Forward)
        SurfacePicking.intersect m.kdCache m.surfaces m.refSystem SurfacePicking.Observation.none SurfacePicking.onlyActive ray
        |> Option.map (fun hit -> ray.Ray.GetPointOnRay hit.hit.RayHit.T)

    let private updateDrawing (sendQueue : BlockingCollection<string>) (msg : DrawingAction) (m : LiteModel) =
        let drawing =
            DrawingApp.update m.refSystem HostConfigs.drawingConfig None sendQueue m.navigation.camera.view m.shiftFlag m.drawing msg
        { m with drawing = drawing }

    let private drawingActionForKey (interaction : Interactions) (k : Keys) =
        match k with
        | Keys.Enter -> Some DrawingAction.Finish
        | Keys.Back  -> Some DrawingAction.RemoveLastPoint
        | Keys.Escape ->
            match interaction with
            | Interactions.EditAnnotation -> Some DrawingAction.CancelVertexEdit
            | _ -> Some DrawingAction.ClearWorking
        | _ -> None

    let private pickSurface (sendQueue : BlockingCollection<string>) (hit : SceneHit) (m : LiteModel) =
        let filter =
            match m.interaction with
            | Interactions.PickSurface -> SurfacePicking.visibleAndActive
            | _ -> SurfacePicking.onlyActive
        let ray = hit.globalRay.Ray
        let camera = m.navigation.camera.view

        match SurfacePicking.pickSurface m.kdCache m.surfaces m.refSystem SurfacePicking.Observation.none
                filter m.drawing.projection (fun _ -> m.refSystem.planet) camera.Location ray with
        | None -> m |> withStatus "No surface under the cursor."
        | Some pick ->
            let ctx : PickRouting.PickContext =
                {
                    interaction  = m.interaction
                    standardMode = true
                    view         = camera
                    shiftFlag    = m.shiftFlag
                    mouseScheme  = mouseScheme m
                    sendQueue    = sendQueue
                    point        = pick.hit
                    surface      = pick.surface
                    hitF         = pick.hitF
                    observed     = pick.observed
                }
            let inputs : PickRouting.PickInputs =
                {
                    drawing    = m.drawing
                    surfaces   = m.surfaces
                    refSystem  = m.refSystem
                    navigation = m.navigation
                    config     = m.config
                    userPrefs  = m.userPrefs
                    scenePath  = m.scenePath
                }
            match PickRouting.routePick ctx inputs with
            | PickRouting.DrawingChanged (drawing, feedback) -> { m with drawing = drawing } |> withStatusOption feedback
            | PickRouting.SurfacesChanged surfaces           -> { m with surfaces = surfaces }
            | PickRouting.RefSystemChanged refSystem         -> { m with refSystem = refSystem } |> updateCameraUp
            | PickRouting.NavigationChanged (nav, feedback)  -> { m with navigation = nav } |> withStatusOption feedback
            | PickRouting.Unchanged
            | PickRouting.Unhandled -> m

    let private importOpcs runtime signature (paths : list<string>) (m : LiteModel) =
        let surfaces = SurfaceLoading.discoverOpcSurfaces m.config.importTriangleSize.value paths
        if IndexList.isEmpty surfaces then
            m |> withStatus "No OPC found in the chosen folder."
        else
            let wasEmpty = HashMap.isEmpty m.surfaces.surfaces.flat
            match SurfaceLoading.tryImportSurfaces runtime signature m.scenePath surfaces m.surfaces with
            | Ok imported ->
                let m = { m with surfaces = imported } |> withStatus (sprintf "Imported %d surface(s)." surfaces.Count)
                // the first data in an empty scene: put the camera in front of it
                match wasEmpty, homeView m with
                | true, Some view -> m |> setCameraView view
                | _ -> m
            | Result.Error e -> m |> withStatus (sprintf "Import failed: %s" e)

    let private sceneCore (m : LiteModel) : SceneCore =
        {
            cameraView      = m.navigation.camera.view
            navigationMode  = m.navigation.navigationMode
            exploreCenter   = m.navigation.exploreCenter
            surfaceModel    = m.surfaces
            config          = m.config
            referenceSystem = m.refSystem
        }

    let private saveScene (path : string) (m : LiteModel) =
        match LiteScene.save path m.sceneExtras (sceneCore m) with
        | Ok () ->
            match AnnotationFiles.save (AnnotationFiles.sidecarOf path) m.drawing with
            | Ok () -> { m with scenePath = Some path } |> withStatus (sprintf "Saved %s" (Path.GetFileName path))
            | Result.Error e -> { m with scenePath = Some path } |> withStatus (sprintf "Scene saved, annotations failed: %s" e)
        | Result.Error e -> m |> withStatus (sprintf "Save failed: %s" e)

    /// Opens a `.pro3d` scene (Lite's own or a full PRo3D one) and its annotation sidecar.
    let openScene runtime signature (path : string) (m : LiteModel) =
        match LiteScene.tryLoad path with
        | Result.Error e -> m |> withStatus (sprintf "Cannot open %s: %s" (Path.GetFileName path) e)
        | Ok (core, extras) ->
            try
                let surfaces =
                    core.surfaceModel
                    |> SurfaceLoading.expandRelativePaths path
                    |> SurfaceLoading.prepareSurfaceModel runtime signature (Some path)

                let drawing, annotationNote =
                    let sidecar = AnnotationFiles.sidecarOf path
                    if File.Exists sidecar then
                        match AnnotationFiles.tryLoad sidecar with
                        | Ok annotations -> AnnotationFiles.applyTo DrawingModel.initialdrawing annotations, ""
                        | Result.Error e -> DrawingModel.initialdrawing, sprintf " (annotations unreadable: %s)" e
                    else
                        DrawingModel.initialdrawing, ""

                let navigation =
                    { m.navigation with
                        navigationMode = core.navigationMode
                        exploreCenter  = core.exploreCenter
                        lockedAxis     = None }

                { m with
                    surfaces    = surfaces
                    drawing     = drawing
                    refSystem   = core.referenceSystem
                    config      = core.config |> withConsistentFrustum
                    navigation  = navigation
                    scenePath   = Some path
                    sceneExtras = Some extras
                    kdCache     = SurfacePicking.KdTreeCache() }
                |> setCameraView core.cameraView
                |> withStatus (sprintf "Opened %s%s" (Path.GetFileName path) annotationNote)
            with e ->
                m |> withStatus (sprintf "Cannot load the surfaces of %s: %s" (Path.GetFileName path) e.Message)

    let update
        (runtime   : IRuntime)
        (signature : IFramebufferSignature)
        (sendQueue : BlockingCollection<string>)
        (m         : LiteModel)
        (msg       : LiteAction) : LiteModel =

        match msg with
        | NavigationMsg msg when not (AnimationApp.shouldAnimate m.animations) ->
            let navigation, feedback =
                Navigation.update m.config m.refSystem HostConfigs.navConf m.userPrefs true (Some (pickCenter m)) m.navigation msg (mouseScheme m)
            { m with navigation = navigation; animations = { m.animations with cam = navigation.camera.view } }
            |> withStatusOption feedback
        | NavigationMsg _ ->
            // the camera belongs to the running fly-to animation
            m

        | AnimationMsg msg ->
            let animations = AnimationApp.update m.animations msg
            { m with
                animations = animations
                navigation = { m.navigation with camera = { m.navigation.camera with view = animations.cam } } }

        | SurfacesMsg msg ->
            let surfaces = SurfaceApp.update m.surfaces msg m.scenePath m.navigation.camera.view m.refSystem
            let m = { m with surfaces = surfaces }
            match msg with
            | SurfaceAppAction.FlyToSurface id -> flyToSurface id m
            | SurfaceAppAction.ChangeImportDirectories _ ->
                { m with surfaces = SurfaceLoading.prepareSurfaceModel runtime signature m.scenePath m.surfaces }
            | _ -> m

        | DrawingMsg (DrawingAction.FlyToAnnotation id) ->
            // the drawing app cannot move the camera: fly to where the annotation was drawn from
            match m.drawing.annotations.flat |> HashMap.tryFind id with
            | Some (Leaf.Annotations a) -> flyTo a.view m
            | _ -> m

        | DrawingMsg msg ->
            updateDrawing sendQueue msg m

        | AnnotationPropsMsg msg ->
            // the same edit + undo snapshot the full viewer does for the selected annotation
            match m.drawing.annotations.singleSelectLeaf with
            | Some selected ->
                let before = m.drawing.annotations
                let edit (leaf : Leaf) =
                    let a = AnnotationProperties.update m.refSystem (Leaf.toAnnotation leaf) msg
                    let a =
                        if a.geometry = Geometry.TT then
                            { a with results = Calculations.calcResultsLine a m.refSystem.up.value m.refSystem.north.value m.refSystem.planet |> Some }
                        else a
                    Leaf.Annotations a
                let after = Groups.updateLeaf selected edit before
                { m with drawing = { m.drawing with annotations = after } |> DrawingApp.pushUndo (SnapshotDelta(before, after)) }
            | None -> m

        | RefSystemMsg msg ->
            // a planet change re-bases the surfaces and recomputes every measurement -
            // the same sync the full viewer runs
            let refSystem, surfaces, drawing =
                ReferenceSystemSync.apply m.config msg m.refSystem m.surfaces m.drawing
            let skyMoved =
                ReferenceSystem.bodyAwareSky refSystem.planet refSystem.up.value
                    <> ReferenceSystem.bodyAwareSky m.refSystem.planet m.refSystem.up.value
            let m = { m with refSystem = refSystem; surfaces = surfaces; drawing = drawing }
            if skyMoved then updateCameraUp m else m

        | ConfigMsg msg ->
            let config = ConfigProperties.update m.config msg
            match msg with
            | ConfigProperties.Action.SetNearPlane _
            | ConfigProperties.Action.SetFarPlane _ -> { m with config = withConsistentFrustum config }
            | _ -> { m with config = config }

        | GoldenMsg msg ->
            { m with golden = GoldenLayout.update msg m.golden }

        | PickSurfaceHit (hit, _) ->
            pickSurface sendQueue hit m

        | SurfaceHover ->
            // a grabbed control point is only dropped by a click after the cursor has moved
            // (the click that grabs it is not a drop) - the full viewer arms it the same way
            match m.drawing.vertexGrab with
            | Some grab when not grab.movedSinceGrab -> updateDrawing sendQueue DrawingAction.ArmVertexGrab m
            | _ -> m

        | SetInteraction interaction ->
            let m = { m with interaction = interaction }
            match interaction with
            | Interactions.DrawAnnotation -> m |> withStatus "Draw: hold Ctrl and click the surface; Enter finishes, Backspace removes a point."
            | Interactions.PickAnnotation -> m |> withStatus "Select: hold Ctrl and click an annotation."
            | Interactions.EditAnnotation -> m |> withStatus "Edit: select an annotation, then Ctrl+drag its control points."
            | Interactions.PickSurface -> m |> withStatus "Select surface: hold Ctrl and click a surface."
            | Interactions.PickExploreCenter -> m |> withStatus "Orbit centre: hold Ctrl and click the surface (switches to orbit)."
            | Interactions.PlaceCoordinateSystem -> m |> withStatus "Coordinate cross: hold Ctrl and click the surface."
            | _ -> m

        | KeyDownMsg k ->
            match k with
            | Keys.LeftCtrl | Keys.RightCtrl -> { m with ctrlFlag = true }
            | Keys.LeftShift | Keys.RightShift -> { m with shiftFlag = true }
            | Keys.Z when m.ctrlFlag -> updateDrawing sendQueue DrawingAction.Undo m
            | Keys.Y when m.ctrlFlag -> updateDrawing sendQueue DrawingAction.Redo m
            | Keys.PageUp | Keys.PageDown ->
                match ConfigProperties.actionForKey m.config k with
                | Some action ->
                    let config = ConfigProperties.update m.config action
                    { m with config = config }
                    |> withStatus (sprintf "Navigation sensitivity: %.1f" config.navigationSensitivity.value)
                | None -> m
            | _ ->
                match drawingActionForKey m.interaction k with
                | Some action -> updateDrawing sendQueue action m
                | None -> m

        | KeyUpMsg k ->
            match k with
            | Keys.LeftCtrl | Keys.RightCtrl -> { m with ctrlFlag = false }
            | Keys.LeftShift | Keys.RightShift -> { m with shiftFlag = false }
            | _ -> m

        | Resize size when size.X > 0 && size.Y > 0 ->
            let aspect = float size.X / float size.Y
            let frustumModel = m.config.frustumModel
            let frustumModel = { frustumModel with frustum = FrustumUtils.withAspect aspect frustumModel.frustum; windowSize = size }
            { m with viewPortSize = size; config = { m.config with frustumModel = frustumModel } }
        | Resize _ -> m

        | ImportOpcs paths ->
            importOpcs runtime signature paths m

        | Home ->
            match homeView m with
            | Some view -> flyTo view m
            | None -> m |> withStatus "Nothing to frame - import an OPC first."

        | OpenScene paths ->
            match paths with
            | path :: _ -> openScene runtime signature path m
            | [] -> m

        | SaveScene ->
            match m.scenePath with
            | Some path -> saveScene path m
            | None -> m |> withStatus "Use \"Save as\" to choose where the scene goes."

        | SaveSceneAs path ->
            if String.IsNullOrWhiteSpace path then m else saveScene path m

        | LoadAnnotations paths ->
            match paths with
            | path :: _ ->
                match AnnotationFiles.tryLoad path with
                | Ok annotations ->
                    { m with drawing = AnnotationFiles.applyTo m.drawing annotations }
                    |> withStatus (sprintf "Loaded annotations from %s" (Path.GetFileName path))
                | Result.Error e -> m |> withStatus (sprintf "Cannot load annotations: %s" e)
            | [] -> m

        | SaveAnnotations path ->
            if String.IsNullOrWhiteSpace path then m
            else
                match AnnotationFiles.save path m.drawing with
                | Ok () -> m |> withStatus (sprintf "Saved annotations to %s" (Path.GetFileName path))
                | Result.Error e -> m |> withStatus (sprintf "Cannot save annotations: %s" e)

        | NoOp -> m

    let threads (m : LiteModel) =
        ThreadPool.union
            (NavigationView.threads m.navigation |> ThreadPool.map NavigationMsg)
            (AnimationApp.ThreadPool.threads m.animations |> ThreadPool.map AnimationMsg)
