module MapViewCameraController

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.UI

open Aardvark.UI.Primitives
open PRo3D.Core
open PRo3D.Base.Gis
open PRo3D.Base

module MapViewController =
    open FSharp.Data.Adaptive.Operators

    type Message = 
        | Down      of button : MouseButtons * pos : V2i
        | Up        of button : MouseButtons
        | Move      of V2i
        | StepTime
        | KeyDown   of key : Keys
        | KeyUp     of key : Keys
        | Wheel     of V2d
        | Blur
        | Pick      of V3d
        | Nop

    let initial =
        {
            view              = CameraView.lookAt (6.0 * V3d.III) V3d.Zero V3d.OOI
            dragStart         = V2i.Zero
            movePos           = V2i.Zero
            look              = false
            zoom              = false
            pan               = false
            dolly             = false
            forward           = false; backward = false; left = false; right = false; isWheel = false; upward = false; downward = false
            upSpeed           = 0.0
            downSpeed         = 0.0
            moveVec           = V3d.Zero
            rotateVec         = V3d.Zero
            lastTime          = None
            orbitCenter       = None
            stash             = None
            sensitivity       = 1.0
            zoomFactor        = 0.01
            panFactor         = 0.01
            rotationFactor    = 0.01
                              
            moveSpeed         = 0.0
            scrollSensitivity = 1.0
            scrolling         = false
            targetPhiTheta    = V2d.Zero
            animating         = false
            targetPan         = V2d.Zero 
            targetDolly       = 0.0
            panSpeed          = 0.0
            targetZoom        = 0.0

            targetJump = None

            freeFlyConfig = FreeFlyConfig.initial
        }

    let sw = Diagnostics.Stopwatch()
    do sw.Start()

    let withTime (model : CameraControllerState) =
        { model with lastTime = Some sw.Elapsed.TotalSeconds }
        

    let exp x =
        let v = Math.Pow(Math.E, x)        
        v

    let setCameraViewCenter (north : V3d) (view : CameraView) =
        let p = V3d.OOO
        CameraView.lookAt view.Location p north

    /// The local east/north/up frame MapView orients the camera by.
    ///
    /// `polarAxis` is the body axis `north` is derived from, i.e. the axis on
    /// which the frame degenerates (north is undefined over a pole). It is
    /// `None` for frames whose up is a fixed axis convention rather than a
    /// function of position - those have no singularity at all.
    type MapFrame =
        {
            up        : V3d
            east      : V3d
            north     : V3d
            polarAxis : Option<V3d>
        }

    let mapFrame (planet : Planet) (p : V3d) : MapFrame =
        match planet with
        // Non-planetary frames fix up to a constant axis, so the generic
        // "east = pole x up" construction degenerates for *every* position.
        // Name the axes directly instead - deriving them from the degenerate
        // fallback happens to be right for JPL but puts ENU's north on its
        // east axis, rotating the whole map view by 90 degrees.
        | Planet.JPL ->                                                     // NED: X north, Y east, Z down
            { up = -V3d.OOI; east = V3d.OIO; north = V3d.IOO; polarAxis = None }
        | Planet.ENU
        | Planet.None ->                                                    // ENU: X east, Y north, Z up
            { up = V3d.OOI; east = V3d.IOO; north = V3d.OIO; polarAxis = None }
        | _ ->
            let up = CooTransformation.getUpVector p planet |> Vec.Normalized
            // Body-fixed +Z is the rotation axis of every supported body, so
            // east = Z x up and north = up x east.
            let pole = V3d.OOI
            let east = pole.Cross(up)
            if east.LengthSquared > 1e-12 then
                let east = east.Normalized
                { up = up; east = east; north = up.Cross(east).Normalized; polarAxis = Some pole }
            else
                // Directly over a pole: north is genuinely undefined. Pick an
                // arbitrary but stable frame; `blocksPole` below keeps the
                // camera from reaching this configuration in the first place.
                let east = V3d.IOO.Cross(up).Normalized
                { up = up; east = east; north = up.Cross(east).Normalized; polarAxis = Some pole }

    /// True when a camera motion would drive the view direction into the
    /// body's polar axis, where the north-up frame is undefined (gimbal lock).
    /// Motion *away* from the pole always passes, so the camera can never get
    /// stuck at the singularity.
    let private blocksPole (frame : MapFrame) (oldForward : V3d) (newForward : V3d) =
        match frame.polarAxis with
        | None -> false                                                     // constant-up frame: no singularity
        | Some axis ->
            let n = Vec.dot newForward axis
            let o = Vec.dot oldForward axis
            (n > 0.999 && n > o) || (n < -0.999 && n < o)

    /// Camera height above the body's reference surface, floored to a small
    /// positive value.
    ///
    /// Pan speed, zoom step and the zoom-in limit all scale with this, so it
    /// must never reach zero or go negative. Terrain below the reference
    /// surface makes that routine - common in Martian basins, and permanent on
    /// a body like Dimorphos whose mean radius (77 m) exceeds its polar radius
    /// (57.5 m) - and a non-positive height freezes the controls outright.
    let private heightAboveSurface (model : CameraControllerState) =
        let radius = model.rotationFactor
        let distanceToCentre = Vec.Length model.view.Location
        max (distanceToCentre - radius) (max (radius * 1e-5) 1.0)

    let updateCameraForMapView (planet : Planet) (model : CameraControllerState) =
        let frame = mapFrame planet model.view.Location
        let view =
            model.view
            |> CameraView.withUp frame.north
            |> setCameraViewCenter frame.north

        { model with view = view}

    let switchToMapViewController (planet : Planet) (model : CameraControllerState)  =
        { model with orbitCenter = Some(V3d.OOO) }
        |> updateCameraForMapView planet


    let update (planet : Planet) (model : CameraControllerState) (message : Message) =
        match message with
            | Nop -> model
            | Blur ->
                { initial with view = model.view; lastTime = None; orbitCenter = model.orbitCenter; rotationFactor = model.rotationFactor }
            | Pick p -> 
                let cam = model.view
                //let newForward = p - cam.Location |> Vec.normalize
                //let tempCam = cam.WithForward newForward

                let p = V3d.OOO
                                
                { model with orbitCenter = Some p; view = CameraView.lookAt cam.Location p cam.Up}
            | StepTime ->
              let now = sw.Elapsed.TotalSeconds
              let cam = model.view

              let cam, center = 
                  match model.lastTime with
                  | Some last ->
                      let dt = now - last

                      //let dir = 
                      //    cam.Forward * float model.moveVec.Z +
                      //    cam.Right * float model.moveVec.X +
                      //    cam.Sky * float model.moveVec.Y


                      //if model.moveVec.AllTiny then
                      //    printfn "useless time %A" now

                      //let step = dir * (exp model.sensitivity) * dt                      
                      //let loc' = cam.Location + step
                      //let direction = (Vec.Dot(model.orbitCenter.Value - loc', cam.Forward)).Sign()

                      //if (model.left || model.right) then 
                      //    cam.WithLocation(loc'), (model.orbitCenter.Value + step) |> Some
                      //else if (model.forward || model.backward || model.isWheel) && direction > 0 then      
                      //    cam.WithLocation(loc'), model.orbitCenter
                      //else
                      //    cam, model.orbitCenter
                      
                      if model.isWheel then
                          let distancetoSurface = heightAboveSurface model
                          let sensitivity = ((model.sensitivity + 2.0) * 5.0) / 100.0
                          let step = distancetoSurface * (model.moveVec.Z * cam.Forward * sensitivity * dt)

                          if step.Length > distancetoSurface && (model.moveVec.Z > 0.0)then
                              cam, model.orbitCenter
                          else
                              let loc' = cam.Location + step
                              cam.WithLocation(loc'), model.orbitCenter



                      else if model.orbitCenter.IsSome then
                          let distanceBetweenCameraSurface = heightAboveSurface model

                          let angle = model.panFactor
                          let windowSize = model.targetPhiTheta
                          let aspect = windowSize.X / windowSize.Y

                          let halfVisibleSurfaceSizeX = tan (angle / 2.0) * distanceBetweenCameraSurface
                          let halfVisibleSurfaceSizeY = halfVisibleSurfaceSizeX / aspect

                          // Angle subtended at the body centre by the visible
                          // ground patch: 2 * atan(halfSize / radius).
                          let visibleAngleX = (atan (halfVisibleSurfaceSizeX / model.rotationFactor) * 2.0)
                          let visibleAngleY = (atan (halfVisibleSurfaceSizeY / model.rotationFactor) * 2.0)

                          let sensitivity = ((model.sensitivity + 5.0) * 4.0) / 100.0 //(exp model.sensitivity) * (distanceBetweenCameraSurface / distanceToCenter)

                          let movingDistance = (V2d(visibleAngleX, visibleAngleY)) * sensitivity

                          let rot = 
                              if model.right then
                                  M44d.Rotation(cam.Up, movingDistance.X * dt)
                              else if model.left then
                                  M44d.Rotation(cam.Up, -movingDistance.X * dt)
                              else if model.forward then 
                                  M44d.Rotation(cam.Left, movingDistance.Y * dt)
                              else if model.backward then
                                  M44d.Rotation(cam.Left, -movingDistance.Y * dt)
                              else
                                  M44d.Identity

                          let trafo = 
                              M44d.Translation (model.orbitCenter.Value) * 
                              rot *
                              M44d.Translation (-model.orbitCenter.Value)
                          
                          let newLocation = trafo.TransformPos (cam.Location)
                          
                          let newUp = trafo.TransformDir (cam.Up)
                          let newRight = trafo.TransformDir (cam.Right)
                          
                          //let tempcam = cam.WithLocation newLocation
                          
                          // make cam with up vector
                          
                          //tempcam.WithForward newForward
                          let newForward = model.orbitCenter.Value - newLocation |> Vec.normalize
                          if blocksPole (mapFrame planet cam.Location) cam.Forward newForward then
                              cam, model.orbitCenter
                          else
                              CameraView(cam.Sky, newLocation, newForward, newUp, newRight), model.orbitCenter
                      else
                          cam, model.orbitCenter
                  | None -> 
                      cam, model.orbitCenter

              let model = if model.isWheel then { model with moveVec = V3d.Zero; isWheel = false} else model                

              { model with lastTime = Some now; view = cam; orbitCenter = center }

            | KeyDown Keys.W ->                
                if not model.forward then
                    withTime { model with forward = true; moveVec = model.moveVec + V3d.OOI  }
                else
                    model

            | KeyUp Keys.W ->
                if model.forward then
                    withTime { model with forward = false; moveVec = model.moveVec - V3d.OOI  }
                else
                    model

            | KeyDown Keys.S ->
                if not model.backward then
                    withTime { model with backward = true; moveVec = model.moveVec - V3d.OOI  }
                else
                    model

            | KeyUp Keys.S ->
                if model.backward then
                    withTime { model with backward = false; moveVec = model.moveVec + V3d.OOI  }
                else
                    model

            | KeyDown Keys.A ->
                if not model.left then
                    withTime { model with left = true; moveVec = model.moveVec - V3d.IOO  }
                else
                    model

            | KeyUp Keys.A ->
                if model.left then
                    withTime { model with left = false; moveVec = model.moveVec + V3d.IOO }
                else
                    model

            | KeyDown Keys.D ->
                if not model.right then
                    withTime { model with right = true; moveVec = model.moveVec + V3d.IOO  }
                else
                    model

            | KeyUp Keys.D ->
                if model.right then
                    withTime { model with right = false; moveVec = model.moveVec - V3d.IOO}
                else
                    model

            | Wheel delta ->
                withTime { model with isWheel = true; moveVec = model.moveVec + V3d.OOI * float (int delta.Y) * 10.0 }

            | KeyDown _ | KeyUp _ ->
                model


            | Down(button,pos) ->
                let model = { model with dragStart = pos }
                match button with
                    | MouseButtons.Left -> { model with look = true }
                    | MouseButtons.Middle -> { model with pan = true }
                    | MouseButtons.Right -> { model with zoom = true }
                    | _ -> model

            | Up button ->
                match button with
                    | MouseButtons.Left -> { model with look = false; zoom = false }
                    | MouseButtons.Middle -> { model with pan = false; zoom = false }
                    | MouseButtons.Right -> { model with zoom = false }
                    | _ -> model

            | Move pos ->
                
                let cam = model.view

                let angle = model.panFactor
                let windowSize = model.targetPhiTheta
                let aspect = windowSize.X / windowSize.Y
                
                let delta = pos - model.dragStart

                let distanceBetweenCameraSurface = heightAboveSurface model

                let relDistance = V2d(delta) / windowSize

                let halfVisibleSurfaceSizeX = tan (angle / 2.0) * distanceBetweenCameraSurface
                let halfVisibleSurfaceSizeY = halfVisibleSurfaceSizeX / aspect

                // Angle subtended at the body centre by the visible ground
                // patch: 2 * atan(halfSize / radius).
                let visibleAngleX = (atan (halfVisibleSurfaceSizeX / model.rotationFactor) * 2.0)
                let visibleAngleY = (atan (halfVisibleSurfaceSizeY / model.rotationFactor) * 2.0)

                let movingDistance = (V2d(visibleAngleX, visibleAngleY)) * relDistance

                //orientation
                // Middle-drag pans with the same gesture as left-drag: in a
                // body-centred map view the camera always looks at the centre,
                // so "pan" and "orbit" are the same motion over the ground.
                let cam =
                    if (model.look || model.pan) && model.orbitCenter.IsSome then
                        let trafo = 
                            M44d.Translation (model.orbitCenter.Value) *
                            M44d.Rotation (cam.Right, -movingDistance.Y) * 
                            M44d.Rotation (cam.Up, -movingDistance.X) *
                            M44d.Translation (-model.orbitCenter.Value)
                     
                        let newLocation = trafo.TransformPos (cam.Location)

                        let newUp = trafo.TransformDir (cam.Up)
                        let newRight = trafo.TransformDir (cam.Right)

                        //let tempcam = cam.WithLocation newLocation
                        
                        // make cam with up vector

                        //tempcam.WithForward newForward
                        let newForward = model.orbitCenter.Value - newLocation |> Vec.normalize
                        if blocksPole (mapFrame planet cam.Location) cam.Forward newForward then
                            cam
                        else
                            CameraView(cam.Sky, newLocation, newForward, newUp, newRight)

                        //CameraView(cam.Sky, newLocation, newForward, newUp, newRight)
                    else
                        cam


                // zoom and pan
                let cam =
                    if model.zoom && model.orbitCenter.IsSome then
                        let step = -model.zoomFactor * (exp model.sensitivity) * (cam.Forward * float delta.Y)

                        let loc' = cam.Location + step
                        let direction = (Vec.Dot(model.orbitCenter.Value - loc', cam.Forward)).Sign()

                        if direction > 0 then
                          cam.WithLocation(loc')
                        else
                          cam
                    else
                        cam

                // The orbit centre is the body centre and stays there - moving
                // it (the old translate-pan) only fought `updateCameraForMapView`,
                // which re-aims at the centre on the very next frame.
                { model with view = cam; dragStart = pos; orbitCenter = model.orbitCenter }

    let attributes (state : AdaptiveCameraControllerState) (f : Message -> 'msg) =
        AttributeMap.ofListCond [
            always (onBlur (fun _ -> f Blur))
            always (onCapturedPointerDownModifiers None (fun t _ b p ->
                f <| match t with Mouse -> Down(b, p) | _ -> Nop
            ))
            always (onCapturedPointerUp None (fun t b p -> match t with Mouse -> f (Up(b)) | _ -> f Nop))
            always (onKeyDown (KeyDown >> f))
            always (onKeyUp (KeyUp >> f))
            always (onWheelPrevent true (fun x -> f (Wheel x)))
            onlyWhen (state.look %|| state.pan %|| state.zoom) (onCapturedPointerMove None (fun t p -> match t with Mouse -> f (Move p) | _ -> f Nop ))
        ]

    let extractAttributes (state : AdaptiveCameraControllerState) (f : Message -> 'msg)  =
        attributes state f |> AttributeMap.toAMap

    let controlledControl (state : AdaptiveCameraControllerState) (f : Message -> 'msg) (frustum : aval<Frustum>) (att : AttributeMap<'msg>) (sg : ISg<'msg>) =
        let cam = AVal.map2 Camera.create state.view frustum
        let controllerAtts = attributes state f
        DomNode.RenderControl(AttributeMap.union att controllerAtts, cam, sg)

    let view (state : AdaptiveCameraControllerState) =
        let frustum = Frustum.perspective 60.0 0.1 100.0 1.0
        div [attribute "style" "display: flex; flex-direction: row; width: 100%; height: 100%; border: 0; padding: 0; margin: 0"] [
  
            controlledControl state id 
                (AVal.constant frustum)
                (AttributeMap.empty)                
                (
                    Sg.box' C4b.Green (Box3d(-V3d.III, V3d.III))
                        |> Sg.noEvents
                        |> Sg.shader {
                            do! DefaultSurfaces.trafo
                            do! DefaultSurfaces.vertexColor
                            do! DefaultSurfaces.simpleLighting
                        }
                )
        ]

    let threads (state : CameraControllerState) =
        let pool = ThreadPool.empty
       
        let rec time() =
            proclist {
                do! Proc.Sleep 10
                yield StepTime
                yield! time()
            }

        if state.moveVec.AllTiny |> not then
            ThreadPool.add "timer" (time()) pool

        else
            pool


    let start () =
        App.start {
            unpersist = Unpersist.instance
            view = view
            threads = threads
            update = update Planet.None
            initial = initial
        }

