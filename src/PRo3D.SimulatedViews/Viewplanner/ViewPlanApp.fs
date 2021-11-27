namespace PRo3D.SimulatedViews

open System
open System.Diagnostics
open System.IO

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.Geometry
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.VRVis

open Aardvark.UI
open Aardvark.UI.Primitives

open CSharpUtils
open IPWrappers

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

open Adaptify.FSharp.Core

open Aether
open Aether.Operators

module ViewPlanApp = 
        
    type Action =
    | CreateNewViewplan of (string * Trafo3d * V3d * ReferenceSystem) //name and trafo and position and refSystem
    | AddPoint          of V3d*ReferenceSystem*HashMap<string, ConcreteKdIntersectionTree>*SurfaceModel     
    | SelectViewPlan    of Guid
    | FlyToViewPlan     of Guid
    | IsVisible         of Guid
    | RemoveViewPlan    of Guid
    | SelectInstrument  of option<Instrument>
    | SelectAxis        of option<Axis>
    | ChangeAngle       of string * PRo3D.Base.Utilities.PRo3DNumeric.Action
    | ChangeFocal       of string * Numeric.Action
    | SetVPName         of string
    | ToggleFootprint
    | SaveFootPrint
    | OpenFootprintFolder
            
    let loadRoverData 
        (model : ViewPlanModel) 
        (path : Option<string>) =      
        match path with
        | Some _ ->            
            let names = RoverProvider.platformNames()    
            printfn "%A" names
            
            let roverData =
                names 
                |> Array.map RoverProvider.initRover 
                |> List.ofArray 
            
            let rovers = roverData|> List.map(fun (r,_) -> r.id, r) 
            let roverMap = rovers |> HashMap.ofList
            let platforms = roverData|> List.map(fun (r,p) -> r.id, p) |> HashMap.ofList
            
            printfn "found rover(s): %d" (roverMap |> HashMap.count)
            
            let selected = 
                rovers 
                |> List.tryHead 
                |> Option.map snd          
                  
            let roverModel = { model.roverModel with rovers = roverMap; platforms = platforms; selectedRover = selected }
            { model with roverModel = roverModel }
        | _ -> model

    let updateViewPlans 
        (vp:ViewPlan) 
        (vps:HashMap<Guid,ViewPlan>) =
        vps |> HashMap.alter vp.id (function | Some _ -> Some vp | None -> None )
    
    let updateViewPlanFroAdaptiveRover 
        (roverModel:RoverModel) 
        (vp:ViewPlanModel) =
        // update rover model
        let m = {vp with roverModel = roverModel}
        // update selected view plan and viewplans
        match vp.selectedViewPlan with
        | Some v -> 
            // TODO v5: refactor            
            let viewPlans = updateViewPlans v (vp.viewPlans)
            let m = { m with viewPlans = viewPlans |> HashMap.map (fun _ _ -> v) }
            
            match roverModel.selectedRover with
            | Some r -> { m with selectedViewPlan = Some { v with rover = r }}
            | None -> m
        | None -> m     

    let getPlaneNormalSign 
        (v1:V3d) 
        (v2:V3d) =
        let sign = Vec.Dot(v2, v1)
        (sign <= 0.0)

    let decomposeRoverTrafo
        (roverTrafo : Trafo3d) =

        let forward = roverTrafo.Forward.UpperLeftM33().C0
        let right   = roverTrafo.Forward.UpperLeftM33().C1
        let up      = roverTrafo.Forward.UpperLeftM33().C2

        forward, right, up


    let hitF 
        (p : V3d) 
        (dir:V3d) 
        (refSystem : ReferenceSystem) 
        (cache : HashMap<string, ConcreteKdIntersectionTree>) 
        (surfaceModel:SurfaceModel) = 
        
        let mutable cache = cache
        let ray = FastRay3d(p, dir)  
                 
        match SurfaceIntersection.doKdTreeIntersection 
            surfaceModel refSystem ray (fun id l surf -> l.active) cache with
        | Some (t,_), _ -> ray.Ray.GetPointOnRay(t) |> Some
        | None, _ -> None

    let getInstrumentResolution (vp:AdaptiveViewPlanModel) =
        adaptive {
            let! selected = vp.selectedViewPlan
            let fail = (uint32(1024), uint32(1024))
            
            match selected with
            | AdaptiveSome v -> 
                let! selectedI = v.selectedInstrument
                match selectedI with
                | AdaptiveSome i -> 
                    let! intrinsics = i.intrinsics
                    let horRes  = intrinsics.horizontalResolution
                    let vertRes = intrinsics.verticalResolution
                    return (horRes, vertRes)
                | AdaptiveNone -> return fail
            | AdaptiveNone -> return fail
        } 
        
    let trafoFromTranslatedBase
        (position   : V3d) 
        (tilt       : V3d) 
        (forward    : V3d) 
        (right      : V3d) 
        : Trafo3d =

        let rotTrafo =  Trafo3d.FromOrthoNormalBasis(forward.Normalized, right.Normalized, tilt.Normalized)
        (rotTrafo * Trafo3d.Translation(position))
           
    let initialPlacementTrafo' 
        (position:V3d) 
        (forward : V3d) 
        (up:V3d) : Trafo3d =
        
        let forward = forward.Normalized
        let up = up.Normalized

        let n = Vec.Cross(forward, up.Normalized).Normalized
        let tilt = Vec.Cross(n, forward).Normalized
        let right = Vec.Cross(tilt, forward).Normalized        

        trafoFromTranslatedBase position tilt forward right

    let initialPlacementTrafo 
        (position:V3d) 
        (lookAt:V3d) 
        (up:V3d) : Trafo3d =
        let forward = (lookAt - position).Normalized

        initialPlacementTrafo' position forward up                        
        
    let calculateSurfaceContactPoint 
        (roverWheel:V3d) 
        (refSystem : ReferenceSystem) 
        (cache : HashMap<string, ConcreteKdIntersectionTree>) 
        (trafo:Trafo3d) 
        (surfaceModel:SurfaceModel) =
        
        let tilt = trafo.Forward.UpperLeftM33().C2;
        let wheelT = trafo.Forward.TransformPos(roverWheel)
        let origin = wheelT + 5000.0 * tilt //move up ray origin
        hitF origin (-tilt) refSystem cache surfaceModel      
       
    let nearestPoint (x:V3d) (plane:Plane3d) =
        let p = plane.Point;
        (x - plane.Normal.Dot(x - p) * plane.Normal);
    
    let calculateRoverPlacement 
        (working:list<V3d>) 
        (wheels:list<V3d>) 
        (up:V3d) : Trafo3d = 
        let forward = (working.[1] - working.[0]).Normalized        

        // projects intersection points along the lookAt vector onto a plane
        let plane1 = new Plane3d(forward, working.[1]);
        let planeIntersectionPoints = wheels |> List.map(fun x -> nearestPoint x plane1) |> List.toArray
        
        // line fitting with the projected points to get right vector
        let rightVecLine = PlaneFitting.Line planeIntersectionPoints //fitLineLeastSquares planeIntersectionPoints
        
        let newRightVec = rightVecLine.Direction.Normalized
        let newTiltVec = Vec.Cross(forward, newRightVec).Normalized
        
        match getPlaneNormalSign up newTiltVec with
        | true -> 
            let newTilt = -newTiltVec
            let newRight = -newRightVec
            trafoFromTranslatedBase working.[0] newTilt forward newRight
        | false -> 
            trafoFromTranslatedBase working.[0] newTiltVec forward newRightVec

    let placementTrafoFromSolTrafo 
        position 
        (refSystem : ReferenceSystem) 
        (rotTrafo : Trafo3d) = 
        let north = refSystem.northO
        let up = refSystem.up.value
        let east = Vec.cross up north

        let forward = rotTrafo.Forward.TransformDir north
        let tilt = rotTrafo.Forward.TransformDir up
        let right = rotTrafo.Forward.TransformDir east

        trafoFromTranslatedBase position tilt forward right

    let createViewPlanFromTrafo     
        (name              : string)
        (rover             : Rover) 
        (ref               : ReferenceSystem)
        (cameraView        : CameraView) 
        (rotationTrafo     : Trafo3d)
        (placementLocation : V3d) =
                                                        
        let angle =  {
            value = 0.0
            min =  -90.0
            max = 90.0
            step = 1.0
            format = "{0:0.0}"
        }

        let trafo = placementTrafoFromSolTrafo placementLocation ref rotationTrafo // (rotationTrafo * Trafo3d.Translation(placementLocation))
        
        let newViewPlan = {
            version            = ViewPlan.current
            id                 = Guid.NewGuid()
            name               = name
            position           = placementLocation
            lookAt             = ref.northO//(ref.northO |> rotationTrafo.Forward.TransposedTransformDir)
            viewerState        = cameraView
            vectorsVisible     = true
            rover              = rover
            roverTrafo         = trafo
            isVisible          = true
            selectedInstrument = None
            selectedAxis       = None
            currentAngle       = angle            
        }
        
        newViewPlan    

    let createViewPlanFromPlacement
        (working      : list<V3d>) 
        (rover        : Rover) 
        (ref          : ReferenceSystem) 
        (cameraView   : CameraView)
        (kdTree       : HashMap<string, ConcreteKdIntersectionTree>) 
        (surfaceModel : SurfaceModel) =
        
        let position = working.[0]
        let lookAt = working.[1]
        
        let initTrafo = initialPlacementTrafo position lookAt ref.up.value
        
        //ray casting for surface intersection with rover wheels
        let surfaceIntersectionPoints = 
            rover.wheelPositions 
            |> List.choose(fun x -> calculateSurfaceContactPoint x ref kdTree initTrafo surfaceModel)
        
        let placementTrafo = //initTrafo
            if (surfaceIntersectionPoints.Length > 2) then
                calculateRoverPlacement working surfaceIntersectionPoints ref.up.value
            else
                initTrafo
        
        let angle =  {
            value = 0.0
            min =  -90.0
            max = 90.0
            step = 1.0
            format = "{0:0.0}"
        }
        
        let newViewPlan = {
            version        = ViewPlan.current
            id             = Guid.NewGuid()
            name           = rover.id
            position       = position
            lookAt         = lookAt
            viewerState    = cameraView
            vectorsVisible = true
            rover          = rover
            roverTrafo     = placementTrafo
            isVisible      = true
            selectedInstrument = None
            selectedAxis   = None
            currentAngle   = angle            
        }
        
        newViewPlan

    let createViewPlanFromFile 
        (data       : HashMap<string,string>) 
        (model      : ViewPlanModel) 
        (rover      : Rover)
        (ref        : ReferenceSystem) 
        (cameraView : CameraView) =

        let pos = 
          V3d((data |> HashMap.find "X").ToFloat(), 
              (data |> HashMap.find "Y").ToFloat(), 
              (data |> HashMap.find "Z").ToFloat())
        
        let posQ = 
          V4d((data |> HashMap.find "qX").ToFloat(), 
              (data |> HashMap.find "qY").ToFloat(), 
              (data |> HashMap.find "qZ").ToFloat(), 
              (data |> HashMap.find "qW").ToFloat())

        let rot = Rot3d(posQ.W,posQ.X, posQ.Y,posQ.Z)
        let forward = rot.Transform(ref.north.value) //ref.north.value
        let forward' = pos + forward
        let trafo = initialPlacementTrafo pos forward' ref.up.value
        let angle = {
            value  = 0.0
            min    = -90.0
            max    = 90.0
            step   = 1.0
            format = "{0:0.0}"
        }

        let newViewPlan = {
            version            = ViewPlan.current
            id                 = Guid.NewGuid()
            name               = rover.id
            position           = pos
            lookAt             = forward
            viewerState        = cameraView
            vectorsVisible     = true
            rover              = rover
            roverTrafo         = trafo
            isVisible          = true
            selectedInstrument = None
            selectedAxis       = None
            currentAngle       = angle         
        }

        { model with 
            viewPlans        = HashMap.add newViewPlan.id newViewPlan model.viewPlans
            working          = List.Empty
            selectedViewPlan = Some newViewPlan 
        }

    let removeViewPlan 
        (vps:HashMap<_,ViewPlan>) 
        (id:Guid) = 
        HashMap.remove id vps //vps |> HashSet.filter (fun x -> x.id <> id)   
    
    let transformExtrinsics 
        (vp:ViewPlan) 
        (ex:Extrinsics) =
        { 
            ex with 
                position  = vp.roverTrafo.Forward.TransformPos ex.position
                camLookAt = vp.roverTrafo.Forward.TransformDir ex.camLookAt
                camUp     = vp.roverTrafo.Forward.TransformDir ex.camUp
        }

    let updateInstrumentCam 
        (vp    : ViewPlan) 
        (model : ViewPlanModel) 
        (fp    : FootPrint)
        : (FootPrint * ViewPlanModel) =

        match vp.selectedInstrument with
        | Some i -> 
            let extr     = transformExtrinsics vp i.extrinsics
            let frust    = model.instrumentFrustum
            let hfov     = i.intrinsics.horizontalFieldOfView
            let resh     = float(i.intrinsics.horizontalResolution)
            let resv     = float(i.intrinsics.verticalResolution)
            
            let view     = CameraView.look extr.position extr.camLookAt.Normalized extr.camUp.Normalized
            let ifrustum = Frustum.perspective (hfov.DegreesFromGons()) frust.near frust.far (resh/resv)
            
            let m' = {
              model with 
                instrumentCam     = view    // CHECK-merge { model.instrumentCam with view = view }
                instrumentFrustum = ifrustum 
            }
            
            //Log.line "%A" model.instrumentCam.ViewTrafo
            
            let f = FootPrint.updateFootprint i vp.position m'
            f,m'
            
        | None -> 
            fp, { model with 
                    instrumentCam     = CameraView.lookAt V3d.Zero V3d.One V3d.OOI
                    instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0 }
      
    let updateRovers 
        (model      : ViewPlanModel) 
        (roverModel : RoverModel) 
        (vp         : ViewPlan) 
        (fp         : FootPrint)
        : (FootPrint * ViewPlanModel) =

        let r = roverModel.rovers  |> HashMap.find vp.rover.id
        let i = 
            match vp.selectedInstrument with
            | Some i -> Some (r.instruments |> HashMap.find i.id)
            | None   -> None

        let vp = { vp with rover = r; selectedInstrument = i }  
        let m  = {
            model with 
                selectedViewPlan = Some vp
                viewPlans        = model.viewPlans |> HashMap.alter vp.id id
        }

        let fp, m = updateInstrumentCam vp m fp
        fp, { m with roverModel = roverModel }

    //let update (model : ViewPlanModel) (camState:CameraControllerState) (action : Action) =
    let update 
        (model       : ViewPlanModel) 
        (action      : Action) 
        (_navigation : Lens<'a,NavigationModel>)
        (_footprint  : Lens<'a,FootPrint>) 
        (scenepath   : Option<string>)
        (outerModel  :'a)
        : ('a * ViewPlanModel) = 
        
        Log.line "ViewplanApp %A" (action.GetType())

        match action with
        | CreateNewViewplan (name, trafo, position, refSystem) ->
            match model.roverModel.selectedRover with
            | Some rover -> 
                let navigation = Optic.get _navigation outerModel

                let vp = createViewPlanFromTrafo name rover refSystem navigation.camera.view trafo position

                outerModel, { model with viewPlans = HashMap.add vp.id vp model.viewPlans; working = List.Empty; selectedViewPlan = Some vp }
            | None ->
                outerModel, model

        | AddPoint (p,ref,kdTree,surfaceModel) ->
            match model.roverModel.selectedRover with
            | Some r -> 
                match model.working.IsEmpty with
                | true -> 
                    outerModel, {model with working = [p]} // first point (position)
                | false -> // second point (lookAt)
                    let w = List.append model.working [p]
                    let navigation = Optic.get _navigation outerModel
                    let vp = createViewPlanFromPlacement w r ref navigation.camera.view kdTree surfaceModel

                    outerModel, { model with viewPlans = HashMap.add vp.id vp model.viewPlans; working = List.Empty; selectedViewPlan = Some vp }
            | None -> 
                outerModel, model

        | SelectViewPlan id ->
            let viewPlanToSelect = model.viewPlans |> HashMap.tryFind id
            let fp = Optic.get _footprint outerModel
            let vp, m , om =
                match viewPlanToSelect, model.selectedViewPlan with
                | Some current, Some old -> 
                    if current.id = old.id then 
                        None, model, outerModel
                    else 
                        let fp', m' = updateInstrumentCam current model fp
                        let newOuterModel = Optic.set _footprint fp' outerModel
                        Some current, m', newOuterModel
                | Some a, None -> 
                    let fp', m' = updateInstrumentCam a model fp
                    let newOuterModel = Optic.set _footprint fp' outerModel
                    Some a, m', newOuterModel
                | None, _ -> 
                    None, model, outerModel
                
            om, { m with selectedViewPlan = vp }

        | FlyToViewPlan id -> 
            let vp = model.viewPlans |> HashMap.tryFind id
            match vp with
            | Some v-> 
                let forward, _, up = decomposeRoverTrafo v.roverTrafo

                let nav = Optic.get _navigation outerModel
                let cameraView = 
                    nav.camera.view 
                    |> CameraView.withForward forward
                    |> CameraView.withLocation v.position
                    |> CameraView.withUp up

                let nav' = { nav with camera = { nav.camera with view = cameraView }}
                let newOuterModel = Optic.set _navigation nav' outerModel
                (newOuterModel, model)
            | _ -> 
                (outerModel, model)

        | IsVisible id ->         
            let viewPlans =  
                model.viewPlans 
                |> HashMap.alter id (function None -> None | Some o -> Some { o with isVisible = not o.isVisible }) //.map(fun x -> if x.id = id then { x with isVisible = not x.isVisible } else x)
            outerModel, { model with viewPlans = viewPlans }

        | RemoveViewPlan id ->
            let vp' = 
                match model.selectedViewPlan with
                | Some v -> if v.id = id then None else Some v
                | None -> None

            let vps = removeViewPlan model.viewPlans id
            outerModel, { model with viewPlans = vps; selectedViewPlan = vp' }

        | SelectInstrument i -> 
            match model.selectedViewPlan with
            | Some vp -> 
                let newVp         = { vp with selectedInstrument = i }
                let fp            = Optic.get _footprint outerModel
                let fp', m'       = updateInstrumentCam newVp model fp
                let newOuterModel = Optic.set _footprint fp' outerModel

                let viewPlans = model.viewPlans |> HashMap.add newVp.id newVp 

                newOuterModel, { m' with selectedViewPlan = Some newVp; viewPlans = viewPlans }
            | None -> outerModel, model                                                     

        | SelectAxis a       -> 
            match model.selectedViewPlan with
            | Some vp -> 
                let newVp = { vp with selectedAxis = a }
                let viewPlans = model.viewPlans |> HashMap.add newVp.id newVp 

                outerModel, { model with selectedViewPlan = Some newVp; viewPlans = viewPlans }
            | None -> outerModel, model    

        | ChangeAngle (id,a) -> 
            match model.selectedViewPlan with
            | Some vp -> 
                match vp.rover.axes.TryFind id with
                | Some ax -> 
                    let angle = Utilities.PRo3DNumeric.update ax.angle a
                    //printfn "numeric angle: %A" angle
                    let ax' = { ax with angle = angle }

                    let rover = { vp.rover with axes = (vp.rover.axes |> HashMap.update id (fun _ -> ax')) }
                    let vp' = { vp with rover = rover; currentAngle = angle; }

                    let angleUpdate = { 
                        roverId       = vp'.rover.id
                        axisId        = ax'.id
                        angle         = Axis.Mapping.from180 ax'.angle.min ax'.angle.max ax'.angle.value
                        shiftedAngle  = ax'.degreesMapped
                        invertedAngle = ax'.degreesNegated
                    }

                    let roverModel = RoverApp.updateAnglePlatform angleUpdate model.roverModel

                    let fp = Optic.get _footprint outerModel
                    let fp, model = updateRovers model roverModel vp' fp
                    let newOuterModel = Optic.set _footprint fp outerModel
                                                
                    newOuterModel, model
                | None -> outerModel, model
            | None -> outerModel, model

        | ChangeFocal (id, f) ->
            match model.selectedViewPlan with
            | Some vp -> 
                match vp.selectedInstrument with
                | Some inst ->   
                    let focal = Numeric.update inst.focal f
                    let inst' =  { inst with focal = focal }

                    let instruments' = 
                      vp.rover.instruments 
                        |> HashMap.update id (fun x -> 
                           match x with
                           | Some _ -> inst'
                           | None   -> failwith "instrument not found")

                    let rover = { vp.rover with instruments = instruments'}
                    let vp' = { vp with rover = rover; selectedInstrument = Some inst' }

                    let focusUpdate = {
                        roverId      = vp'.rover.id
                        instrumentId = inst'.id
                        focal        = inst'.focal.value
                    }

                    let roverModel' = RoverApp.updateFocusPlatform focusUpdate model.roverModel
                    let fp = Optic.get _footprint outerModel
                    let fp', m' = updateRovers model roverModel' vp' fp
                    let newOuterModel = Optic.set _footprint fp' outerModel
                    
                    newOuterModel, m'
                | None -> outerModel, model                                       
            | None -> outerModel, model 

        | SetVPName t -> 
            match model.selectedViewPlan with
            | Some vp -> 
                let vp' = {vp with name = t}
                let viewPlans = model.viewPlans |> HashMap.add vp'.id vp'
                outerModel, {model with selectedViewPlan = Some vp'; viewPlans = viewPlans }              
            | None -> outerModel, model

        | ToggleFootprint ->   
            let fp = Optic.get _footprint outerModel
            let fp' = { fp with isVisible = not fp.isVisible }
            let newOuterModel = Optic.set _footprint fp' outerModel
            newOuterModel, model

        | SaveFootPrint -> 
            match scenepath with
            | Some sp -> outerModel, (FootPrint.createFootprintData model sp)
            | None -> outerModel, model

        | OpenFootprintFolder ->
            match scenepath with
            | Some sp -> 
                let fpPath = FootPrint.getFootprintsPath sp
                if (Directory.Exists fpPath) then Process.Start("explorer.exe", fpPath) |> ignore
                outerModel, model
            | None -> outerModel, model        

    module Sg =     
        let drawWorking (model:AdaptiveViewPlanModel) =
            let point0 =
                model.working
                |> AVal.map(fun w -> 
                    match w |> List.tryHead with
                    | Some p -> 
                        Sg.dot (AVal.constant C4b.Green) (AVal.constant 3.0)  (AVal.constant p) 
                    | None -> Sg.empty
                )  
                |> ASet.ofAValSingle 
                |> Sg.set
              
            let point1 =
                model.working 
                |> AVal.map(fun w -> 
                    match w |> List.tryLast with
                    | Some p -> 
                        Sg.dot (AVal.constant C4b.Green) (AVal.constant 3.0) (AVal.constant p) 
                    | None -> Sg.empty
                ) 
                |> ASet.ofAValSingle 
                |> Sg.set

            Sg.ofList [point0; point1]                

        let drawPlatformCoordinateCross 
            (viewPlan  : AdaptiveViewPlan)
            (near      : aval<float>) 
            (length    : aval<float>) 
            (thickness : aval<float>) 
            (cam       : aval<CameraView>) =

            let lookAtVec = AVal.map(fun (t:Trafo3d) -> t.Forward.UpperLeftM33().C0) viewPlan.roverTrafo
            let rightVec  = AVal.map(fun (t:Trafo3d) -> t.Forward.UpperLeftM33().C1) viewPlan.roverTrafo
            let tiltVec   = AVal.map(fun (t:Trafo3d) -> t.Forward.UpperLeftM33().C2) viewPlan.roverTrafo

            //let size = Sg.computeInvariantScale cam near viewPlan.position length (AVal.constant 60.0)

            let marker : Sg.MarkerStyle = {
                position  = viewPlan.position
                direction = ~~V3d.NaN
                color     = ~~C4b.Black
                size      = length //size
                thickness = thickness
                hasArrow  = ~~true
                text      = ~~None
                fix       = ~~false
            }

            let lookAt = { marker with direction = lookAtVec; color = (AVal.constant C4b.Yellow)}
            let right  = { marker with direction = rightVec;  color = (AVal.constant C4b.Red)}
            let tilt   = { marker with direction = tiltVec;   color = (AVal.constant C4b.Red)}

            Sg.ofList [
                lookAt |> Sg.directionMarker near cam
                right  |> Sg.directionMarker near cam
                tilt   |> Sg.directionMarker near cam                    
            ] 
            |> Sg.onOff viewPlan.isVisible
        
        let viewCoordinateCross 
            (refSystem : AdaptiveReferenceSystem) 
            (trafo : aval<Trafo3d>) =
            
            let up = refSystem.up.value
            let north = refSystem.northO
            let east = AVal.map2(Vec.cross) up north

            [
                Sg.drawSingleLine ~~V3d.Zero up    ~~C4b.Blue  ~~2.0 trafo
                Sg.drawSingleLine ~~V3d.Zero north ~~C4b.Red   ~~2.0 trafo
                Sg.drawSingleLine ~~V3d.Zero east  ~~C4b.Green ~~2.0 trafo
            ] 
            |> Sg.ofList

        let drawPlatformAxis 
            (axis       : AdaptiveAxis) 
            (cam        : aval<CameraView>) 
            (thickness  : aval<float>) 
            (trafo      : aval<Trafo3d>) =

            let start = AVal.map2( fun (t:Trafo3d) p -> t.Forward.TransformPos p) trafo axis.startPoint
            let endp = AVal.map2( fun (t:Trafo3d) p -> t.Forward.TransformPos p) trafo axis.endPoint
            let axisLine = 
                alist {
                    let! s = axis.startPoint
                    let! e = axis.endPoint
                    let! trafo = trafo
                    yield trafo.Forward.TransformPos e
                    yield trafo.Forward.TransformPos s
                } 
            let color = AVal.constant C4b.White
            Sg.ofList [
                Sg.dot color ~~3.0 start
                Sg.dot color ~~3.0 endp
                //Sg.lines color axisLine ~~0.0  thickness trafo //todo fix
            ]
        
        let drawInstruments 
            (instruments    : alist<AdaptiveInstrument>) 
            (viewPlan       : AdaptiveViewPlan) 
            (near           : aval<float>)
            (length         : aval<float>)
            (thickness      : aval<float>)
            (cam            : aval<CameraView>) =

            alist {                
                
                let! trafo     = viewPlan.roverTrafo
                let! selInst   = viewPlan.selectedInstrument

                for i in instruments do

                    let camPosTrans     = AVal.map(fun p -> trafo.Forward.TransformPos p) i.extrinsics.position
                    let camLookAtTrans  = AVal.map(fun p -> trafo.Forward.TransformDir p) i.extrinsics.camLookAt
                    let camUpTrans      = AVal.map(fun p -> trafo.Forward.TransformDir p) i.extrinsics.camUp

                    match selInst with
                    | AdaptiveSome s -> 
                        let! sid = s.id
                        let! id = i.id
                        //let size = Sg.computeInvariantScale cam near viewPlan.position length (AVal.constant 60.0)

                        let marker : Sg.MarkerStyle = {
                            position  = camPosTrans
                            direction = AVal.constant V3d.NaN
                            color     = ~~C4b.White
                            size      = length //size
                            thickness = thickness
                            hasArrow  = AVal.constant true
                            text      = AVal.constant None
                            fix       = AVal.constant false
                        }

                        let up = { marker with direction = camUpTrans; color = (AVal.constant C4b.Red)}

                        match sid = id with
                        | true ->
                            let lookAt = { marker with direction = camLookAtTrans; color = (AVal.constant C4b.Cyan)}
                            yield Sg.ofList [
                                Sg.point camPosTrans (AVal.constant C4b.Cyan) cam // position
                                lookAt |> Sg.directionMarker near cam 
                                up     |> Sg.directionMarker near cam
                            ]
                        | false -> 
                            let lookAt = { marker with direction = camLookAtTrans; color = (AVal.constant C4b.Blue)}                              
                            yield Sg.ofList [
                                lookAt |> Sg.directionMarker near cam 
                                up     |> Sg.directionMarker near cam
                            ]
                    | AdaptiveNone -> 
                        yield Sg.ofList []
                }
                          
        let drawWheels (vp:AdaptiveViewPlan) (cam:aval<CameraView>) =
            alist {
                let! wheels = vp.rover.wheelPositions
                let! trafo = vp.roverTrafo
                for w in wheels do
                    let wheelPos = trafo.Forward.TransformPos w |> AVal.constant
                    yield Sg.point wheelPos (AVal.constant C4b.White) cam
                }
                
        let drawRover 
            (vp        : AdaptiveViewPlan) 
            (near      : aval<float>) 
            (length    : aval<float>) 
            (thickness : aval<float>) 
            (cam       : aval<CameraView>) 
            (roverM    : AdaptiveRoverModel) =

            let wheels = 
                drawWheels vp cam
                |> ASet.ofAList 
                |> Sg.set

            let axes = vp.rover.axes |> RoverApp.mapTolist
                        
            let sgAxes = 
                axes 
                |> AList.map(fun a -> drawPlatformAxis a cam thickness vp.roverTrafo)
                |> ASet.ofAList 
                |> Sg.set

            let instruments = vp.rover.instruments |> RoverApp.mapTolist

            let sgInstruments = 
                drawInstruments instruments vp near length thickness cam
                |> ASet.ofAList 
                |> Sg.set

            Sg.ofList [
                wheels
                sgAxes
                sgInstruments
            ]         

        let view<'ma> 
            (mbigConfig   : 'ma) 
            (minnerConfig : MInnerConfig<'ma>)
            (model        : AdaptiveViewPlanModel)
            (cam          : aval<CameraView>)
            : ISg<Action> =
                       
            let length    = minnerConfig.getArrowLength    mbigConfig
            let thickness = minnerConfig.getArrowThickness mbigConfig
            let near      = minnerConfig.getNearDistance   mbigConfig

            let viewPlans =
                aset {
                    for _,vp in model.viewPlans do 
                        
                        let! showVectors = vp.isVisible
                        if showVectors then
                            yield drawPlatformCoordinateCross vp near length thickness cam
                            let! selected = model.selectedViewPlan
                            let id = vp.id
                            
                            let gg = 
                                match selected with
                                | AdaptiveSome s -> 
                                    let sg = drawRover vp near length thickness cam model.roverModel
                                    let condition = ~~(s.id = id)
                                    sg |> Sg.onOff (condition)
                                | AdaptiveNone -> 
                                    Sg.empty
                            yield gg    
                } |> Sg.set
                
            Sg.ofList [
                viewPlans
                drawWorking model
            ]
    
    module UI =

        let viewHeader (m:AdaptiveViewPlan) (id:Guid) toggleMap = 
            [
                Incremental.text m.name; text " "

                i [clazz "home icon";                                                
                    onClick (fun _ -> FlyToViewPlan id)
                ][] |> UI.wrapToolTip DataPosition.Bottom "FlyTo"                                                                                

                Incremental.i toggleMap AList.empty 
                |> UI.wrapToolTip DataPosition.Bottom "Toggle Arrows"                                                                     

                i [clazz "Remove icon red";                                             
                    onClick (fun _ -> RemoveViewPlan id)
                ][] |> UI.wrapToolTip DataPosition.Bottom "Remove"                                         
            ]    

        let viewViewPlans (m:AdaptiveViewPlanModel) = 
            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div itemAttributes (
                alist { 
                    yield Incremental.i itemAttributes AList.empty
                    let! selected = m.selectedViewPlan
                    let viewPlans = m.viewPlans |> AMap.toASetValues |> ASet.sortBy (fun a -> a.id)
                    for vp in viewPlans do
                        let vpid = vp.id
                        let! color =
                            match selected with
                            | AdaptiveSome sel -> 
                                AVal.constant (if sel.id = vpid then C4b.VRVisGreen else C4b.White)
                            | AdaptiveNone -> 
                                AVal.constant C4b.White
                                                                 
                        let bgc = color |> Html.ofC4b |> sprintf "color: %s"

                        let toggleIcon = 
                            AVal.map( fun toggle -> if toggle then "unhide icon" else "hide icon") vp.isVisible

                        let toggleMap = 
                            amap {
                                let! toggleIcon = toggleIcon
                                yield clazz toggleIcon
                                yield onClick (fun _ -> IsVisible vpid)
                            } |> AttributeMap.ofAMap  


                        yield div [clazz "item"] [
                            Incremental.i itemAttributes AList.empty
                            i [
                                clazz "large cube middle aligned icon"; 
                                style bgc
                                onClick (fun _ -> SelectViewPlan vpid)
                            ][]
                            div [clazz "content"] [
                                Incremental.i itemAttributes AList.empty
                                div [clazz "header"] (
                                    viewHeader vp vpid toggleMap
                                )      
                            ]
                        ]
                }
            )
        
        let focalGui (i : AdaptiveInstrument) =
            let nodes =
              alist {
                let! id = i.id
                let! focals = i.calibratedFocalLengths
                if focals.Length > 1 then                    
                    yield Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox] i.focal |> UI.map (fun x -> ChangeFocal (id,x))
                else
                    yield Incremental.text (i.focal.value |> AVal.map string)
              }
            Incremental.div AttributeMap.Empty nodes

        let instrumentProperties(i : AdaptiveInstrument) =
            let sensor = i.intrinsics |> AVal.map(fun x -> sprintf "%d X %d" x.horizontalResolution x.verticalResolution)

            require GuiEx.semui (
                Html.table [                
                    Html.row "Sensor (px):"  [ Incremental.text sensor ]
                    Html.row "Focal (mm):"   [ focalGui i ]                
                ]
            )

        let viewInstrumentProperties (m : AdaptiveViewPlan) = 
            m.selectedInstrument
            |> AVal.map(fun x ->
                match x with 
                | AdaptiveSome i -> instrumentProperties i
                | AdaptiveNone ->   div[][]
            )

        let viewFootprintProperties (fpVisible:aval<bool>) (m : AdaptiveViewPlan) = 
          m.selectedInstrument 
            |> AVal.map(fun x ->
              match x with 
              | AdaptiveSome _ -> 
                require GuiEx.semui (
                    Html.table [  
                        Html.row "show footprint:"  [GuiEx.iconCheckBox fpVisible ToggleFootprint]
                        Html.row "export footprint:"  [button [clazz "ui button tiny"; onClick (fun _ -> SaveFootPrint )][]]
                        Html.row "open footprint folder:"  [button [clazz "ui button tiny"; onClick (fun _ -> OpenFootprintFolder )][]]
                    ]
                )
              | AdaptiveNone -> div[][])


        //let instrumentsDd (r:AdaptiveRover) (m : AdaptiveViewPlan) = 
        //    UI.dropDown'' (r.instruments |> RoverApp.mapTolist) m.selectedInstrument (fun x -> SelectInstrument (x |> Option.map(fun y -> y.Current |> AVal.force))) (fun x -> (x.id |> AVal.force) )   
        
        let instrumentsDd (r:AdaptiveRover) (m : AdaptiveViewPlan) = 
            UI.dropDown'' 
                (r.instruments |> RoverApp.mapTolist) 
                (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption m.selectedInstrument) 
                (fun x -> SelectInstrument (x |> Option.map(fun y -> y.Current |> AVal.force))) 
                (fun x -> (x.id |> AVal.force))

        let viewAxesList (r : AdaptiveRover) (m : AdaptiveViewPlan) =
            alist {
                let! selectedI = m.selectedInstrument
                match selectedI with
                    | AdaptiveSome i ->
                        for axis in (r.axes |> RoverApp.mapTolist) do
                            yield div[][Incremental.text axis.id; text "(deg)"]
                            //yield Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox] axis.angle |> UI.map (fun x -> ChangeAngle (axis.id |> AVal.force,x))
                            //let! value = axis.angle.value
                            //let! max = axis.angle.max
                            //let! min = axis.angle.min
                            //yield Numeric.view' [InputBox] axis.angle |> UI.map (fun x -> ChangeAngle (id,x))

                            yield Utilities.PRo3DNumeric.viewContinuously [NumericInputType.Slider; NumericInputType.InputBox] axis.angle |> UI.map (fun x -> ChangeAngle (axis.id.GetValue(),x))
                            //if (value <= max) && (value >= min) then                    
                            //    yield Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox] axis.angle |> UI.map (fun x -> ChangeAngle (id,x))
                            //else
                            //    yield Incremental.text (axis.angle.value |> AVal.map string)
                            //yield br[]
                            //yield Incremental.text (axis.startPoint |> AVal.map (sprintf "%A"))
                            yield div[][text "["; Incremental.text (axis.angle.min |> AVal.map string); text ";"; Incremental.text (axis.angle.max |> AVal.map string); text "]"]
                            yield br[]
                    | AdaptiveNone -> yield div[][]

            }

        let viewRoverProperties' (r : AdaptiveRover) (m : AdaptiveViewPlan) (fpVisible:aval<bool>) =
            require GuiEx.semui (
                Html.table [
                     Html.row "Change VPName:"[ Html.SemUi.textBox m.name SetVPName ]
                     Html.row "Name:"       [ Incremental.text r.id ]
                     Html.row "Instrument:" [ 
                        instrumentsDd r m 
                        Incremental.div AttributeMap.empty (AList.ofAValSingle (viewInstrumentProperties m))                    
                     ]
                     Html.row "Axes:" [   
                        Incremental.div AttributeMap.empty (viewAxesList r m)
                     ]
                     Html.row "Footprint:" [   
                        Incremental.div AttributeMap.empty (AList.ofAValSingle ( viewFootprintProperties fpVisible m ))
                     ]
                     ]
                
            )

        let viewSelectRover (m : AdaptiveRoverModel) : DomNode<RoverApp.Action> =
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large Rocket icon"][] ]
                Html.Layout.boxH [ UI.dropDown'' (RoverApp.roversList m)  
                                                  (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption m.selectedRover)
                                                  (fun x -> RoverApp.Action.SelectRover (x |> Option.map(fun y -> y.Current |> AVal.force))) 
                                                  (fun x -> (x.id |> AVal.force) ) ]                
            ]


        let viewRoverProperties lifter (fpVisible:aval<bool>) (model : AdaptiveViewPlanModel) = 
            model.selectedViewPlan 
            |> AVal.map(fun x ->
                match x with 
                | AdaptiveSome x -> viewRoverProperties' x.rover x fpVisible |> UI.map lifter
                | AdaptiveNone ->   div[][] |> UI.map lifter)

       
