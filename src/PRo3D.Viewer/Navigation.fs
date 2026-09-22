namespace PRo3D

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives

open System

open FSharp.Data.Adaptive
open Aardvark.Rendering
open PRo3D.Base
open PRo3D.Core
open PRo3D.Navigation2
open MapViewCameraController
open MapViewCameraController.MapViewController

module Navigation =
    
   // open Navigation.Sg


    type Action =
        | ArcBallAction            of ArcBallController.Message        
        //| FreeFlyAction            of CameraController.Message
        | FreeFlyAction            of FreeFlyController.Message
        | MapViewControllerAction  of MapViewController.Message
        | SetNavigationMode        of NavigationMode

    type smallConfig<'a,'b> =
        {
            navigationSensitivity : Lens<'a, float>
            up                    : Lens<'b, V3d>
            north                 : Lens<'b, V3d>
            northO                : Lens<'b, V3d>
            frustum               : Lens<'a, Frustum>
            windowSize            : Lens<'a, V2i>
            planet                : Lens<'b, Planet>
        }

    let pickOrbitCenter (pickFunction : Option<unit->Option<V3d>>) (model : NavigationModel) = 
        let orbitCenter = 
            match pickFunction with
            | None -> None
            | Some f -> 
                Log.startTimed "pick new orbit"
                let point = f()
                Log.stop()
                point

        match orbitCenter with
        | None -> 
            Log.warn "could not get new orbit center, please center view to surface"

            let oldNavMode = 
                if model.navigationMode = NavigationMode.ArcBall then NavigationMode.FreeFly
                else model.navigationMode
                    

            { model with navigationMode = oldNavMode }, Some "could not pick new orbit center with center ray.\n Please center view to surface before changing to ArcBall or select explore center manually"
        | Some p ->
            Log.line "new orbit implicitly set to center ray"
            { model with exploreCenter = p; navigationMode = NavigationMode.ArcBall }, Some("New orbit set with center ray")

    /// Distance from the frame origin to the ground beneath the camera.
    /// MapView stores it in `CameraControllerState.rotationFactor` and scales
    /// every pan/zoom step by the camera's height above it.
    ///
    /// It has to be derived from the body rather than from scene state: MapView
    /// is also entered by restoring a bookmark or loading a scene, neither of
    /// which runs `SetNavigationMode`, so a value computed only on the mode
    /// switch is absent (0.01, the FreeFly default) on exactly those paths -
    /// which makes pan and zoom wildly oversensitive.
    ///
    /// Non-planetary frames (JPL/ENU) have no body, so the only scale available
    /// is where the data sits relative to the origin.
    let mapViewRadius (planet : Planet) (model : NavigationModel) =
        let location = model.camera.view.Location
        match CooTransformation.tryGetBodyRadius planet location with
        | Some r -> r
        | None ->
            let fromExplore = Vec.Length model.exploreCenter
            if fromExplore > 0.0 then fromExplore
            else max (Vec.Length location * 0.5) 1.0

    /// Which mouse buttons drive the camera. All three controllers agree on the
    /// default binding - left looks/orbits, middle pans, right dollies - so one
    /// rule remaps all of them.
    ///
    /// In `directToolMode` the left button belongs to the active tool, so its
    /// look gesture moves onto the right button and the dolly that displaces is
    /// left to the wheel. Ctrl temporarily hands the left button back to the
    /// camera; the right button keeps looking either way, so a drag started
    /// with it survives a Ctrl press.
    type MouseScheme =
        {
            directToolMode : bool
            ctrlFlag       : bool
        }

    module MouseScheme =
        /// The default binding, unchanged from what the controllers ship with.
        let full = { directToolMode = false; ctrlFlag = false }

        /// The button whose camera gesture a press should run, or `None` when the
        /// press does not belong to the camera at all.
        let mapDown (scheme : MouseScheme) (button : MouseButtons) =
            if not scheme.directToolMode then Some button
            else
                match button with
                | MouseButtons.Left  -> if scheme.ctrlFlag then Some MouseButtons.Left else None
                | MouseButtons.Right -> Some MouseButtons.Left
                | b                  -> Some b

        /// A release is *never* dropped, only remapped: dropping it would strand
        /// `look = true` when Ctrl is released in the middle of a left drag. A
        /// release with no matching press only clears state that is already clear.
        let mapUp (scheme : MouseScheme) (button : MouseButtons) =
            if scheme.directToolMode && button = MouseButtons.Right then MouseButtons.Left
            else button

    let update<'a,'b> (bigConfigA : 'a) (bigConfigB : 'b) (smallConfig : smallConfig<'a,'b>) (userPrefs : UserPreferences) (switchToArcball : bool) (pickFunction : Option<unit->Option<V3d>>) (model : NavigationModel) (act : Action) (scheme : MouseScheme) =
        match act with
        | ArcBallAction a ->
            let model, feedback =
                match a with
                | ArcBallController.Message.Pick a when switchToArcball->
                    { model with navigationMode =  NavigationMode.ArcBall; exploreCenter = a }, None
                | _ ->
                    model, None

            let (msg : Option<ArcBallController.Message>) =
                match a with
                | ArcBallController.Message.Down (button, pos) ->
                    MouseScheme.mapDown scheme button
                    |> Option.map (fun mb -> ArcBallController.Message.Down (mb, pos))
                | ArcBallController.Message.Up button ->
                    ArcBallController.Message.Up (MouseScheme.mapUp scheme button) |> Some
                | _ -> Some a

            // A press the scheme drops is not a camera gesture at all - leave the
            // camera exactly as it was.
            match msg with
            | None -> model, feedback
            | Some msg ->
                let beforeView = model.camera.view
                let cam = ArcBallController.update model.camera msg
                let cam = { cam with sensitivity = smallConfig.navigationSensitivity.Get(bigConfigA); orbitCenter = Some model.exploreCenter }

                // Axis lock (ArcBall only): collapse this step's orientation change to its
                // twist about the locked world axis, orbiting `exploreCenter`; dolly stays
                // free. `Pick` is a deliberate re-centre, so it is left unconstrained.
                let cam =
                    match model.lockedAxis with
                    | Some navAxis when model.navigationMode = NavigationMode.ArcBall
                                     && (match a with ArcBallController.Message.Pick _ -> false | _ -> true) ->
                        let axisDir =
                            NavigationConstraint.axisWorldDirection
                                (smallConfig.planet.Get bigConfigB)
                                (smallConfig.up.Get bigConfigB)
                                (smallConfig.northO.Get bigConfigB)
                                navAxis
                        { cam with view = NavigationConstraint.constrainRotationToAxis beforeView cam.view axisDir model.exploreCenter }
                    | _ -> cam

                match cam.orbitCenter with
                | Some oc -> { model with camera = cam; exploreCenter = oc}, feedback
                | None -> { model with camera = cam }, feedback


        | FreeFlyAction a ->
            let (msg : Option<FreeFlyController.Message>) =
                match a with
                | FreeFlyController.Message.Down (button, pos) ->
                    MouseScheme.mapDown scheme button
                    |> Option.map (fun mb -> FreeFlyController.Message.Down (mb, pos))
                | FreeFlyController.Message.Up button ->
                    FreeFlyController.Message.Up (MouseScheme.mapUp scheme button) |> Some
                | _ -> Some a

            match msg with
            | None -> model, None
            | Some msg ->
                let cam' = FreeFlyController.update model.camera msg
                let sensitivity = smallConfig.navigationSensitivity.Get(bigConfigA)

                let config = {
                  cam'.freeFlyConfig with
                    panMouseSensitivity       = exp(sensitivity) * 0.0025
                    dollyMouseSensitivity     = exp(sensitivity) * 0.0025
                    zoomMouseWheelSensitivity = exp(sensitivity) * 0.1
                    moveSensitivity           = sensitivity
                    lookAtMouseSensitivity    = 0.004
                    lookAtDamping             = 50.0
                    }

                {
                  model with camera = { cam' with freeFlyConfig = config }
                }, None
        | MapViewControllerAction a ->
            let frustum = smallConfig.frustum.Get(bigConfigA)
            let planet  = smallConfig.planet.Get(bigConfigB)

            // Horizontal field of view: `right` is the half-width at the near
            // plane, so the half-angle is atan(right / near).
            let angle = Math.Atan(frustum.right / frustum.near) * 2.0

            let windowSize = smallConfig.windowSize.Get(bigConfigA)

            //let view =
            //    model.camera.view
            //    |> CameraView.withUp (smallConfig.north.Get(bigConfigB))
            //    |> setCameraViewCenter (smallConfig.north.Get(bigConfigB))

            // Apply the mouse scheme and the user's MapView WASD invert preferences
            // before dispatch, so the controller stays unaware of both.
            let (msg : Option<MapViewController.Message>) =
                match a with
                | MapViewController.Message.KeyDown k ->
                    MapViewController.Message.KeyDown (UserPreferences.remapMapViewKey userPrefs k) |> Some
                | MapViewController.Message.KeyUp k ->
                    MapViewController.Message.KeyUp (UserPreferences.remapMapViewKey userPrefs k) |> Some
                | MapViewController.Message.Down (button, pos) ->
                    MouseScheme.mapDown scheme button
                    |> Option.map (fun mb -> MapViewController.Message.Down (mb, pos))
                | MapViewController.Message.Up button ->
                    MapViewController.Message.Up (MouseScheme.mapUp scheme button) |> Some
                | _ -> Some a

            match msg with
            | None -> model, None
            | Some a ->

                let cam = {
                    model.camera with
                        view = model.camera.view
                        sensitivity    = smallConfig.navigationSensitivity.Get(bigConfigA)
                        // MapView always orbits the frame origin. Note this is not
                        // `exploreCenter` - that belongs to ArcBall and MapView must
                        // not clobber it.
                        orbitCenter    = Some V3d.OOO
                        targetPhiTheta = V2d(windowSize.X, windowSize.Y)
                        panFactor      = angle
                        // Refreshed per message, not just on the mode switch, so
                        // bookmark restore and scene load get a valid body scale.
                        rotationFactor = mapViewRadius planet model
                    }

                let cam = MapViewController.update planet cam a

                let cam =
                    if model.updatePerFrame then
                        match a with
                        | KeyUp _
                        | Up _
                        | Move _
                        | StepTime ->
                            cam |> MapViewController.updateCameraForMapView planet
                        | _ -> cam
                    else
                        match a with
                        | KeyUp _
                        | Up _ ->
                            cam |> MapViewController.updateCameraForMapView planet
                        | _ -> cam

                // Axis lock (MapView supports the vertical edge only): constant-latitude
                // orbit about the body spin axis (falling back to the map-frame up for the
                // constant-up frames). A spin-axis twist preserves look-at-origin + north-up,
                // so it is safe to apply after `updateCameraForMapView`.
                let cam =
                    match model.lockedAxis with
                    | Some NavigationAxis.UpDown ->
                        let frame = mapFrame planet model.camera.view.Location
                        let axisDir = frame.polarAxis |> Option.defaultValue frame.up
                        { cam with view = NavigationConstraint.constrainRotationToAxis model.camera.view cam.view axisDir V3d.OOO }
                    | _ -> cam

                { model with camera = cam }, None

        | SetNavigationMode mode ->
            // A camera-mode switch always clears the gizmo axis lock.
            let model = { model with lockedAxis = None }
            match mode with
            | NavigationMode.ArcBall ->
                let model, message = pickOrbitCenter pickFunction model
                { model with updatePerFrame = false }, message
            | NavigationMode.FreeFly ->
                let center =
                    match model.camera.orbitCenter with
                    | Some x ->  x
                    | None   -> V3d.OOO

                let sky =
                    ReferenceSystem.bodyAwareSky
                        (smallConfig.planet.Get(bigConfigB))
                        (smallConfig.up.Get(bigConfigB))
                let view' =
                    CameraView.lookAt model.camera.view.Location center sky

                { model with camera = { model.camera with view = view'}; navigationMode = mode; updatePerFrame = false}, None
            | NavigationMode.MapView ->
                let planet     = smallConfig.planet.Get(bigConfigB)

                let cam = model.camera |> switchToMapViewController planet

                // Body radius for pan/zoom scaling. Avoids a synchronous
                // kdtree pick which stalls the UI on large scenes.
                let cam = { cam with rotationFactor = mapViewRadius planet model }

                // `exploreCenter` is deliberately left alone: it is ArcBall's
                // orbit centre, MapView orbits V3d.OOO regardless, and zeroing
                // it left the next switch into MapView with no scale at all -
                // which is what froze the map view on first entry.
                { model with camera = cam; navigationMode = NavigationMode.MapView; updatePerFrame = true }, None
            | _ ->  { model with navigationMode = mode; updatePerFrame = false }, None
               
    module UI =        

        type smallConfig<'ma> = 
            {
                getNearPlane : 'ma -> aval<float>
                getFarPlane  : 'ma -> aval<float>
            }

        //let frustum near far =
        //    adaptive {
        //        let! near = near
        //        let! far = far
        //        return (Frustum.perspective 90.0 near far 1.0)
        //        }

        let renderControlAttributes (model : AdaptiveNavigationModel) near far =
            amap {
                let! state = model.navigationMode 
                match state with
                | NavigationMode.FreeFly -> yield! FreeFlyController.extractAttributes model.camera FreeFlyAction
                | NavigationMode.ArcBall -> yield! ArcBallController.extractAttributes model.camera ArcBallAction
                | NavigationMode.MapView -> yield! MapViewController.extractAttributes model.camera MapViewControllerAction
                | _ -> failwith "Invalid NavigationMode"
            } |> AttributeMap.ofAMap

        let geometryTooltip (nMode : NavigationMode) : string =
            match nMode with 
            | NavigationMode.FreeFly -> "Enter FreeFlyMode"
            | NavigationMode.ArcBall -> "Enter ArcBallMode - Camera is focused on a point and moves arround it"
            | NavigationMode.MapView -> "Enter MapViewMode - Camera is focused on current planetery object center, up is north and move speed depends on camera distance to surface"
            | _  -> ""         

        let viewNavigationModes  (planet : aval<Planet>) (model : AdaptiveNavigationModel) =
            Html.Layout.horizontal [
                Html.Layout.boxH [ div [style "font-weight:bold"] [text "Navigation:"] ]                
                Html.Layout.boxH [ Incremental.div (AttributeMap.empty) (
                    alist {
                        let navMode = model.navigationMode
                        let! p = planet
                        // MapView needs a reference body (it orients to the planet
                        // centre with up = north). With Planet.None we still show it,
                        // but greyed out with a note, instead of hiding it.
                        let disabled =
                            if p = Planet.None then
                                [ NavigationMode.MapView ] |> HashSet.ofList
                            else
                                HashSet.empty

                        Drawing.UI.dropDownDisabled HashSet.empty disabled "needs a planet" navMode SetNavigationMode geometryTooltip
                    })]
            ]

    module Sg =
        let view (model:AdaptiveNavigationModel)=
            let point = PRo3D.Base.Sg.dot (AVal.constant C4b.Magenta) (AVal.constant 3.0) model.exploreCenter 
            Sg.ofList [point] 
           
