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
                
    let updateCameraForMapView (planet : Planet) (model : CameraControllerState) =
        let point = model.view.Location
        let up = CooTransformation.getUpVector point planet |> Vec.Normalized
        let east = V3d.OOI.Cross(up).Normalized
        let north = up.Cross(east).Normalized
        let view = 
            model.view
            |> CameraView.withUp north
            |> setCameraViewCenter north

        { model with view = view}        

    let switchToMapViewController (planet : Planet) (model : CameraControllerState)  = 
        { model with orbitCenter = Some(V3d.OOO) }
        |> updateCameraForMapView planet

   
    let update (model : CameraControllerState) (message : Message) =
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
                          let distancetoSurface = (Vec.distance model.view.Location V3d.OOO) - model.rotationFactor
                          let sensitivity = ((model.sensitivity + 2.0) * 5.0) / 100.0
                          let step = (Math.Max(distancetoSurface, 10.0)) * (model.moveVec.Z * cam.Forward * sensitivity * dt)
                          
                          if step.Length > distancetoSurface && (model.moveVec.Z > 0.0)then
                              cam, model.orbitCenter
                          else
                              let loc' = cam.Location + step
                              cam.WithLocation(loc'), model.orbitCenter


                          
                      else if model.orbitCenter.IsSome then
                          let distanceToCenter = Vec.distance model.view.Location V3d.OOO
                          let distanceBetweenCameraSurface = Math.Abs(distanceToCenter - model.rotationFactor)
                          
                          let angle = model.panFactor
                          let windowSize = model.targetPhiTheta
                          let aspect = windowSize.X / windowSize.Y
                          
                          let halfVisibleSurfaceSizeX = tan (angle / 2.0) * distanceBetweenCameraSurface
                          let halfVisibleSurfaceSizeY = halfVisibleSurfaceSizeX / aspect
                          
                          let visibleAngleX = (tanh (halfVisibleSurfaceSizeX / model.rotationFactor) * 2.0)
                          let visibleAngleY = (tanh (halfVisibleSurfaceSizeY / model.rotationFactor) * 2.0)
                          
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
                          if (newForward.Z > 0.999 && newForward.Z > cam.Forward.Z) || (newForward.Z < -0.999 && newForward.Z < cam.Forward.Z) then 
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

                let distanceToCenter = Vec.distance model.view.Location V3d.OOO
                let distanceBetweenCameraSurface = Math.Abs(distanceToCenter - model.rotationFactor)
                //let sensitivity = 0.01 * (exp model.sensitivity) * (distanceBetweenCameraSurface / distanceToCenter)

                //let distanceToCenter = Vec.distance model.view.Location V3d.OOO
                //let sensitivity = -0.01 * model.sensitivity * (model.rotationFactor / distanceToCenter)

                let relDistance = V2d(delta) / windowSize

                let halfVisibleSurfaceSizeX = tan (angle / 2.0) * distanceBetweenCameraSurface
                let halfVisibleSurfaceSizeY = halfVisibleSurfaceSizeX / aspect

                let visibleAngleX = (tanh (halfVisibleSurfaceSizeX / model.rotationFactor) * 2.0)
                let visibleAngleY = (tanh (halfVisibleSurfaceSizeY / model.rotationFactor) * 2.0)
                
                let movingDistance = (V2d(visibleAngleX, visibleAngleY)) * relDistance

                //orientation
                let cam =
                    if model.look && model.orbitCenter.IsSome then
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
                        if (newForward.Z > 0.999 && newForward.Z > cam.Forward.Z) || (newForward.Z < -0.999 && newForward.Z < cam.Forward.Z) then 
                            cam
                        else  
                            CameraView(cam.Sky, newLocation, newForward, newUp, newRight)
                        
                        //CameraView(cam.Sky, newLocation, newForward, newUp, newRight)
                    else
                        cam


                // zoom and pan
                let cam =
                    if model.zoom then
                        let step = -model.zoomFactor * (exp model.sensitivity) * (cam.Forward * float delta.Y)

                        let loc' = cam.Location + step
                        let direction = (Vec.Dot(model.orbitCenter.Value - loc', cam.Forward)).Sign()

                        if direction > 0 then
                          cam.WithLocation(loc')
                        else 
                          cam
                    else
                        cam

                let cam, center =
                    if model.pan && model.orbitCenter.IsSome then
                        let step = model.panFactor * (exp model.sensitivity) * (cam.Down * float delta.Y + cam.Right * float delta.X)
                        let center = model.orbitCenter.Value + step
                        cam.WithLocation(cam.Location + step), Some center
                    else
                        cam, model.orbitCenter            

                { model with view = cam; dragStart = pos; orbitCenter = center }

    [<Obsolete>]
    let onMouseDown (cb : MouseButtons -> V2i -> 'msg) = 
        onEvent 
            "onmousedown" 
            ["event.clientX"; "event.clientY"; "event.which"; "event.getModifierState('Control')" ]
            (fun args ->
                match args with
                    | x :: y :: b :: isControl :: _ ->
                        let x = int (float x)
                        let y = int (float y)
                        let b = Aardvark.UI.Helpers.button b
                        let modKey = if b = MouseButtons.Left && isControl = "true" then MouseButtons.Right else b
                        cb modKey (V2i(x,y))
                    | _ ->
                        failwith "Mousedown Event Failed in MapViewCameraController"
            )

    (*let onMouseUp (cb : MouseButtons -> V2i -> 'msg) = 
        onEvent 
            "onmouseup" 
            ["event.clientX"; "event.clientY"; "event.which"; "event.getModifierState('Control')" ]
            (fun args ->
                match args with
                    | x :: y :: b :: isControl :: _ ->
                        let x = int (float x)
                        let y = int (float y)
                        let b = Aardvark.UI.Helpers.button b
                        let modKey = if b = MouseButtons.Left && isControl = "true" then MouseButtons.Right else b
                        cb modKey (V2i(x,y))
                    | _ ->
                        failwith "asdasd"
            )*)

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

    let controlledControlWithClientValues (state : AdaptiveCameraControllerState) (f : Message -> 'msg) (frustum : aval<Frustum>) (att : AttributeMap<'msg>) (sg : Aardvark.Service.ClientValues -> ISg<'msg>) =
        let attributes = AttributeMap.union att (attributes state f)
        let cam = AVal.map2 Camera.create state.view frustum 
        Incremental.renderControlWithClientValues cam attributes sg

    let controlledControl (state : AdaptiveCameraControllerState) (f : Message -> 'msg) (frustum : aval<Frustum>) (att : AttributeMap<'msg>) (sg : ISg<'msg>) =
        controlledControlWithClientValues state f frustum att (constF sg)

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
            update = update
            initial = initial
        }

