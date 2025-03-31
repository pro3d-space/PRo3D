namespace PRo3D.SimulatedViews

open System
open System.Diagnostics
open System.IO

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Rendering.Text
open Aardvark.Application
open Aardvark.Geometry
open Aardvark.SceneGraph
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.VRVis

open Aardvark.UI
open Aardvark.UI.Primitives

open CSharpUtils
open IPWrappers

open PRo3D
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
    | ToggleDepth
    | DepthColorLegendMessage   of FalseColorLegendApp.Action
    | SaveDepthData
    | OpenDepthDataFolder
    | SelectImage        of Guid
    | RemoveImage        of Guid
    | ToggleProjectedImage
    | LoadImage          of list<string>
    | ImportExInTrinsics of list<string>
    | LoadImageTest
    | SelectDistancePoint of Guid
    | RemoveDistancePoint of Guid
    | LookAtPoint         of Guid
    | ToggleText          
    | AddDistancePoint    of V3d
    | SetTextSize         of Numeric.Action
    | SetDPointSize       of Numeric.Action
    | SetPointColor       of ColorPicker.Action

            
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
        | Some id -> 
            // TODO v5: refactor 
            let selectedVp = vp.viewPlans |> HashMap.find id
            let viewPlans = updateViewPlans selectedVp (vp.viewPlans)
            { m with viewPlans = viewPlans |> HashMap.map (fun _ _ -> selectedVp) }
        | None -> m     

    let updateVPOutput 
         (model : ViewPlanModel) 
         (vp : ViewPlan) 
         outerModel =
         let viewPlans = model.viewPlans |> HashMap.add vp.id vp
         outerModel, {model with viewPlans = viewPlans }     

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
            surfaceModel refSystem (constF None) None ray (fun id l surf -> l.active) cache with
        | Some (t,_), _ -> ray.Ray.GetPointOnRay(t) |> Some
        | None, _ -> None

    let getInstrumentResolution (vp:AdaptiveViewPlanModel) =
        adaptive {
            let! selected = vp.selectedViewPlan
            let fail = (uint32(1024), uint32(1024))
          
            match selected with
            | Some id -> 
                let! selectedVp = vp.viewPlans |> AMap.tryFind id
                match selectedVp with
                | Some v ->
                    let! selectedI = v.selectedInstrument
                    match selectedI with
                    | AdaptiveSome i -> 
                        let! intrinsics = i.intrinsics
                        let horRes  = intrinsics.horizontalResolution
                        let vertRes = intrinsics.verticalResolution
                        return (horRes, vertRes)
                    | AdaptiveNone -> return fail
                | None -> return fail
            | None -> return fail
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
        //(working:list<V3d>)
        (position : V3d)
        (lookAt   : V3d)
        (wheels:list<V3d>) 
        (up:V3d) : Trafo3d = 
        let forward = (lookAt - position).Normalized        

        // projects intersection points along the lookAt vector onto a plane
        let plane1 = new Plane3d(forward, lookAt);
        let planeIntersectionPoints = wheels |> List.map(fun x -> nearestPoint x plane1) |> List.toArray
        
        // line fitting with the projected points to get right vector
        let rightVecLine = PlaneFitting.Line planeIntersectionPoints //fitLineLeastSquares planeIntersectionPoints
        
        let newRightVec = rightVecLine.Direction.Normalized
        let newTiltVec = Vec.Cross(forward, newRightVec).Normalized
        
        match getPlaneNormalSign up newTiltVec with
        | true -> 
            let newTilt = -newTiltVec
            let newRight = -newRightVec
            trafoFromTranslatedBase position newTilt forward newRight
        | false -> 
            trafoFromTranslatedBase position newTiltVec forward newRightVec

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
            footPrint          = FootPrint.initFootPrint
            distancePoints     = HashMap.Empty
            selectedDistPoint  = None
            showDistanceText   = true
            textSize           = ViewPlan.initTextSize 4.0
            dPointSize         = ViewPlan.initPointSize 8.0
            dPointColor        = {c = C4b.Orange}
        }
        
        newViewPlan    

    let calcNewRoverTrafo
        (position : V3d)
        (lookAt   : V3d)
        (wheelPositions  : list<V3d>)
        (ref      : ReferenceSystem) =
        
            let initTrafo = initialPlacementTrafo position lookAt ref.up.value
            if (wheelPositions.Length > 2) then
                calculateRoverPlacement position lookAt wheelPositions ref.up.value
            else
                initTrafo

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
        
        let placementTrafo = 
            if (surfaceIntersectionPoints.Length > 2) then
                calculateRoverPlacement position lookAt surfaceIntersectionPoints ref.up.value
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
            footPrint      = FootPrint.initFootPrint
            distancePoints     = HashMap.Empty
            selectedDistPoint  = None
            showDistanceText   = true
            textSize           = ViewPlan.initTextSize 4.0
            dPointSize         = ViewPlan.initPointSize 8.0
            dPointColor        = {c = C4b.Orange}
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
            footPrint          = FootPrint.initFootPrint
            distancePoints     = HashMap.Empty
            selectedDistPoint  = None
            showDistanceText   = true
            textSize           = ViewPlan.initTextSize 4.0
            dPointSize         = ViewPlan.initPointSize 8.0
            dPointColor        = {c = C4b.Orange}
        }

        { model with 
            viewPlans        = HashMap.add newViewPlan.id newViewPlan model.viewPlans
            working          = List.Empty
            selectedViewPlan = Some newViewPlan.id 
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
            
            let i' = { i with extrinsics = extr}
            let vp' = { vp with selectedInstrument = Some i';} 
            
            let m' = {
              model with
                instrumentCam     = view    // CHECK-merge { model.instrumentCam with view = view }
                instrumentFrustum = ifrustum 
            }
            
            //Log.line "%A" model.instrumentCam.ViewTrafo
            
            let f = FootPrintUtils.updateFootprint i' vp m'
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

        let vp' = { vp with rover = r; selectedInstrument = i }  
        let m  = { model with
                    selectedViewPlan = Some vp'.id
                    viewPlans        = model.viewPlans |> HashMap.alter vp'.id  (Option.map(fun _ -> vp')) //id 
                    roverModel       = roverModel}

        let fp, m = updateInstrumentCam vp' m fp 
        fp, m //{ m with roverModel = roverModel }

    let selectViewplan outerModel _footprint id model =
        let viewPlanToSelect = model.viewPlans |> HashMap.tryFind id
        match viewPlanToSelect, model.selectedViewPlan with
        | Some a, Some b ->
            if a.id = b then 
                outerModel, { model with selectedViewPlan = None }
            else
                let footPrint = Optic.get _footprint outerModel
                let footPrint, model = updateInstrumentCam a model footPrint 
                let newOuterModel = Optic.set _footprint footPrint outerModel
                newOuterModel, { model with selectedViewPlan = Some a.id }
        | Some a, None -> 
            let footPrint = Optic.get _footprint outerModel
            let footPrint, model = updateInstrumentCam a model footPrint 
            let newOuterModel = Optic.set _footprint footPrint outerModel
            newOuterModel, { model with selectedViewPlan = Some a.id }
        | None, _ -> outerModel, model

    let updateFootPrint 
        (model : ViewPlanModel)
        (selectedVp : ViewPlan) 
        (fp' : FootPrint)
        (_footprint  : Lens<'a,FootPrint>)
        (outerModel  :'a) =
        let vp' = {selectedVp with footPrint = fp'}
        let viewPlans = model.viewPlans |> HashMap.add vp'.id vp'
        let newOuterModel = Optic.set _footprint fp' outerModel
        newOuterModel, {model with viewPlans = viewPlans }         

    //let update (model : ViewPlanModel) (camState:CameraControllerState) (action : Action) =
    let update 
        (model       : ViewPlanModel) 
        (action      : Action) 
        (_navigation : Lens<'a,NavigationModel>)
        (_footprint  : Lens<'a,FootPrint>) 
        (scenepath   : Option<string>)
        (refSys      : ReferenceSystem)   
        (outerModel  :'a)
        : ('a * ViewPlanModel) = 
        
        Log.line "ViewplanApp %A" (action.GetType())

        match action with
        | CreateNewViewplan (name, trafo, position, refSystem) ->
            match model.roverModel.selectedRover with
            | Some rover -> 
                let navigation = Optic.get _navigation outerModel

                let vp = createViewPlanFromTrafo name rover refSystem navigation.camera.view trafo position

                { model with viewPlans = HashMap.add vp.id vp model.viewPlans; working = List.Empty } 
                |> selectViewplan outerModel _footprint vp.id
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

                    { model with viewPlans = HashMap.add vp.id vp model.viewPlans; working = List.Empty } 
                    |> selectViewplan outerModel _footprint vp.id
            | None -> 
                outerModel, model

        | SelectViewPlan id ->
            //let viewPlanToSelect = model.viewPlans |> HashMap.tryFind id
            //match viewPlanToSelect with 
            //| Some viewplan ->                
            //    match model.selectedViewPlan with
            //    | Some selected when selected.id = viewplan.id ->                    
            //        outerModel, { model with selectedViewPlan = None }
            //    | _ ->                   
            //        let footPrint = Optic.get _footprint outerModel
            //        let footPrint, model = updateInstrumentCam viewplan model footPrint
            //        let newOuterModel = Optic.set _footprint footPrint outerModel
            //        newOuterModel, { model with selectedViewPlan = Some viewplan }
            //| None ->
            //    Log.line "[ViewplanApp] viewplan with selected id does not exist"
            //    outerModel, model

            selectViewplan outerModel _footprint id model
                
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
                | Some v -> if v = id then None else Some v
                | None -> None

            let vps = removeViewPlan model.viewPlans id
            outerModel, { model with viewPlans = vps; selectedViewPlan = vp' }

        | SelectInstrument i -> 
            match model.selectedViewPlan with
            | Some id-> 
                let selectedVp = model.viewPlans |> HashMap.find id
                let newVp         = { selectedVp with selectedInstrument = i }
                let fp            = Optic.get _footprint outerModel
                let fp', m'       = updateInstrumentCam newVp model fp 
                let newOuterModel = Optic.set _footprint fp' outerModel
                updateVPOutput m' newVp newOuterModel
            | None -> outerModel, model                                                                   

        | SelectAxis a       -> 
            match model.selectedViewPlan with
            | Some id -> 
                let selectedVp = model.viewPlans |> HashMap.find id 
                let newVp = { selectedVp with selectedAxis = a }
                updateVPOutput model newVp outerModel
            | None -> outerModel, model    

        | ChangeAngle (id,a) -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid 
                match selectedVp.rover.axes.TryFind id with
                | Some ax -> 
                    let angle = Utilities.PRo3DNumeric.update ax.angle a
                    //printfn "numeric angle: %A" angle
                    let ax' = { ax with angle = angle }

                    let rover = { selectedVp.rover with axes = (selectedVp.rover.axes |> HashMap.update id (fun _ -> ax')) }
                    let vp' = { selectedVp with rover = rover; currentAngle = angle; }

                    let angleUpdate = { 
                        roverId       = vp'.rover.id
                        axisId        = ax'.id
                        angle         = Axis.Mapping.from180 ax'.angle.min ax'.angle.max ax'.angle.value
                        shiftedAngle  = ax'.degreesMapped
                        invertedAngle = ax'.degreesNegated
                    }

                    let roverModel = RoverApp.updateAnglePlatform angleUpdate model.roverModel

                    let fp = Optic.get _footprint outerModel
                    let fp, model' = updateRovers model roverModel vp' fp
                    let newOuterModel = Optic.set _footprint fp outerModel

                    //let viewPlans = model.viewPlans |> HashMap.add vp'.id vp'
                                                
                    newOuterModel, model' //{ model with viewPlans = viewPlans }
                | None -> outerModel, model
            | None -> outerModel, model

        | ChangeFocal (id, f) ->
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid 
                match selectedVp.selectedInstrument with
                | Some inst ->   
                    let focal = Numeric.update inst.focal f
                    let inst' =  { inst with focal = focal }

                    let instruments' = 
                      selectedVp.rover.instruments 
                        |> HashMap.update id (fun x -> 
                           match x with
                           | Some _ -> inst'
                           | None   -> failwith "instrument not found")

                    let rover = { selectedVp.rover with instruments = instruments'}
                    let vp' = { selectedVp with rover = rover; selectedInstrument = Some inst' }

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
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid 
                let vp' = {selectedVp with name = t}
                updateVPOutput model vp' outerModel
            | None -> outerModel, model

        | ToggleFootprint ->  
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid 
                let fp = Optic.get _footprint outerModel
                let fp' = { fp with isVisible = not fp.isVisible }
                updateFootPrint model selectedVp fp' _footprint outerModel     
            | None -> outerModel, model


        | SaveFootPrint -> 
            match scenepath with
            | Some sp -> outerModel, (FootPrintUtils.createFootprintData model sp)
            | None -> outerModel, model

        | OpenFootprintFolder ->
            match scenepath with
            | Some sp -> 
                let fpPath = FootPrintUtils.getDataPath sp "FootPrints"
                if (not (Directory.Exists fpPath)) then 
                    Directory.CreateDirectory fpPath |> ignore
                Process.Start("explorer.exe", fpPath) |> ignore
                outerModel, model
            | None -> outerModel, model   

        | ToggleDepth ->   
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid 
                let fp = Optic.get _footprint outerModel
                let fp' = { fp with isDepthVisible = not fp.isDepthVisible }
                updateFootPrint model selectedVp fp' _footprint outerModel   
            | None -> outerModel, model

        | DepthColorLegendMessage msg -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid 
                let fp = Optic.get _footprint outerModel
                let colorUpdate = FalseColorLegendApp.update fp.depthColorLegend msg
                let fp' = { fp with depthColorLegend = colorUpdate }
                updateFootPrint model selectedVp fp' _footprint outerModel  
            | None -> outerModel, model

        | SaveDepthData -> 
            match scenepath with
            | Some sp -> outerModel, (FootPrintUtils.createFootprintData model sp)
            | None -> outerModel, model

        | OpenDepthDataFolder ->
            match scenepath with
            | Some sp -> 
                let fpPath = FootPrintUtils.getDataPath sp "DepthData"
                if (Directory.Exists fpPath) then Process.Start("explorer.exe", fpPath) |> ignore
                outerModel, model
            | None -> outerModel, model  
        
        | SelectImage id -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let fp  = Optic.get _footprint outerModel
                let fp' = { fp with selectedImage = Some id }
                updateFootPrint model selectedVp fp' _footprint outerModel
            | None -> outerModel, model                 
        | RemoveImage id -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let fp  = Optic.get _footprint outerModel
                let fp' = { fp with images = (HashMap.remove id fp.images) }
                updateFootPrint model selectedVp fp' _footprint outerModel
            | None -> outerModel, model                 
        | ToggleProjectedImage -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid 
                let fp = Optic.get _footprint outerModel
                //TEST
                //let pImage = ProjectedImage.initProjectedImage "C:\Users\laura\VRVis\PRo3D\Data\mars-2020-pico-turquino-south-sol-1326-SketchFab\m1_PT_south_1x.jpg"
                //let fp' = { fp with images = HashMap.add pImage.id pImage fp.images; useProjectedImage = not fp.useProjectedImage }
                let fp' = { fp with useProjectedImage = not fp.useProjectedImage }
                updateFootPrint model selectedVp fp' _footprint outerModel      
            | None -> outerModel, model
        | LoadImage pathList -> 
            match pathList with
            | filepath :: tail ->
                match System.IO.File.Exists filepath with
                | true ->
                    match model.selectedViewPlan with
                    | Some vpid -> 
                        let selectedVp = model.viewPlans |> HashMap.find vpid 
                        let fp = Optic.get _footprint outerModel
                        let pImage = ProjectedImage.initProjectedImage filepath
                        //@Laura: add ex- and intrinsics here
                        let fp' = { fp with images = HashMap.add pImage.id pImage fp.images }
                        updateFootPrint model selectedVp fp' _footprint outerModel
                    | None -> outerModel, model
                | false ->
                    outerModel, model
            | [] -> outerModel, model
        | ImportExInTrinsics pathList ->
            match pathList with
            | filepath :: tail ->
                match System.IO.File.Exists filepath with
                | true ->
                    match model.selectedViewPlan with
                    | Some vpid -> 
                        let selectedVp = model.viewPlans |> HashMap.find vpid 
                        let fp = Optic.get _footprint outerModel
                        match selectedVp.footPrint.selectedImage with
                        | Some img ->
                            let selectedImage = fp.images |> HashMap.find img
                        
                                //@Harri: add ex- and intrinsics here

                            outerModel, model
                        | None -> outerModel, model
                    | None -> outerModel, model
                | false ->
                    outerModel, model
            | [] -> outerModel, model

        | LoadImageTest -> outerModel, model
            //match pathList with
            //| filepath :: tail ->
            //    match System.IO.File.Exists filepath with
            //    | true ->
            //        match model.selectedViewPlan with
            //        | Some vpid -> 
            //            let selectedVp = model.viewPlans |> HashMap.find vpid 
            //            let fp = Optic.get _footprint outerModel
            //            let pImage = ProjectedImage.initProjectedImage filepath
            //            //@Laura: add ex- and intrinsics here
            //            let fp' = { fp with images = HashMap.add pImage.id pImage fp.images }
            //            updateFootPrint model selectedVp fp' _footprint outerModel
            //        | None -> outerModel, model
            //    | false ->
            //        outerModel, model
            //| [] -> outerModel, model
        | SelectDistancePoint id -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let vp' = {selectedVp with selectedDistPoint = Some id}
                updateVPOutput model vp' outerModel
            | None -> outerModel, model     
        | RemoveDistancePoint id -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let vp' = {selectedVp with distancePoints = (HashMap.remove id selectedVp.distancePoints)}
                updateVPOutput model vp' outerModel
            | None -> outerModel, model     
        | LookAtPoint         id -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let selPoint = selectedVp.distancePoints |> HashMap.find id
                
                let footPrint = Optic.get _footprint outerModel
                let forward, right, up = decomposeRoverTrafo selectedVp.roverTrafo
                let forward = (selPoint.position - selectedVp.position).Normalized

                let rTrafo = initialPlacementTrafo' selectedVp.position forward up
                let vp' = {selectedVp with roverTrafo = rTrafo}
                let fp', m'       = updateInstrumentCam vp' model footPrint
                let newOuterModel = Optic.set _footprint fp' outerModel

                updateVPOutput m' vp' newOuterModel
            | None -> outerModel, model   
        | ToggleText           -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let vp' = {selectedVp with showDistanceText = not selectedVp.showDistanceText}
                updateVPOutput model vp' outerModel
            | None -> outerModel, model     
        | AddDistancePoint    p -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let distance = Vec.Distance(selectedVp.position, p)
                let forward = (p - selectedVp.position).Normalized
                let newDP = DistancePoint.initDistancePoint p distance (Some vpid)
                let vp' = {selectedVp with distancePoints = (selectedVp.distancePoints |> HashMap.add newDP.id newDP)}
                updateVPOutput model vp' outerModel
            | None -> outerModel, model   
        | SetTextSize s -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let ts = Numeric.update selectedVp.textSize s
                let vp' = {selectedVp with textSize = ts}
                updateVPOutput model vp' outerModel
            | None -> outerModel, model   
        | SetDPointSize s -> 
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let ds = Numeric.update selectedVp.dPointSize s
                let vp' = {selectedVp with dPointSize = ds}
                updateVPOutput model vp' outerModel
            | None -> outerModel, model   
         | SetPointColor a ->
            match model.selectedViewPlan with
            | Some vpid -> 
                let selectedVp = model.viewPlans |> HashMap.find vpid
                let vp' = {selectedVp with dPointColor = ColorPicker.update selectedVp.dPointColor a }
                updateVPOutput model vp' outerModel
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
        let directionMarker = Sg.directionMarker ~~60.0

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
                textsize  = AVal.constant 0.05
                textcolor = AVal.constant C4b.White
                fix       = ~~false
            }

            let lookAt = { marker with direction = lookAtVec; color = (AVal.constant C4b.Yellow)}
            let right  = { marker with direction = rightVec;  color = (AVal.constant C4b.Red)}
            let tilt   = { marker with direction = tiltVec;   color = (AVal.constant C4b.Red)}

            Sg.ofList [
                lookAt |> directionMarker near cam
                right  |> directionMarker near cam
                tilt   |> directionMarker near cam                    
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
            let directionMarker = Sg.directionMarker ~~60.0

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
                            textsize  = AVal.constant 0.05
                            textcolor = AVal.constant C4b.White
                            fix       = AVal.constant false
                        }

                        let up = { marker with direction = camUpTrans; color = (AVal.constant C4b.Red)}

                        match sid = id with
                        | true ->
                            let lookAt = { marker with direction = camLookAtTrans; color = (AVal.constant C4b.Cyan)}
                            yield Sg.ofList [
                                Sg.point camPosTrans (AVal.constant C4b.Cyan) cam // position
                                lookAt |> directionMarker near cam 
                                up     |> directionMarker near cam
                            ]
                        | false -> 
                            let lookAt = { marker with direction = camLookAtTrans; color = (AVal.constant C4b.Blue)}                              
                            yield Sg.ofList [
                                lookAt |> directionMarker near cam 
                                up     |> directionMarker near cam
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
                    let! selected' = model.selectedViewPlan
                    for _,vp in model.viewPlans do 
                        
                        let! showVectors = vp.isVisible
                        if showVectors then
                            yield drawPlatformCoordinateCross vp near length thickness cam

                            let selected =
                                match selected' with
                                | Some sel -> sel = vp.id 
                                | None -> false
                            
                            let gg = 
                                if selected then
                                    let sg = drawRover vp near length thickness cam model.roverModel
                                    //let condition = ~~(sid = vp.id)
                                    sg //|> Sg.onOff (condition)
                                else
                                    Sg.empty
                            yield gg    
                } |> Sg.set
                
            Sg.ofList [
                viewPlans
                drawWorking model
            ]
        
        let getDistancePointOffsetTransform (refSystem : AdaptiveReferenceSystem) (model : AdaptiveViewPlan) =
            (refSystem.Current, model.Current, AVal.constant(0.5)) |||> AVal.map3 (fun refSystem current offset ->
                match current.distancePoints |> HashMap.toList |> List.tryHead with
                | None -> Trafo3d.Identity
                | Some distP -> 
                    let id, point = distP
                    let north, up, east = PRo3D.Core.Surface.TransformationApp.getNorthAndUpFromPivot point.position refSystem
                    Trafo3d.Translation(offset * up)
            )
        
        let getTextPosition (point : aval<V3d>) (dist : aval<float>) (size : aval<float>) =
            adaptive {
                let! pos = point
                let! dist = dist
                let loc = pos + pos.Normalized * 1.5
                let! size = size
                let trafo = (Trafo3d.Scale((float)size)) * (Trafo3d.Translation loc)
                let text = $"{dist}"
                //let stableTrafo = viewTrafo |> AVal.map (fun view -> trafo * view) // stable, and a bit slow
                return AVal.constant trafo, AVal.constant text
            } |> AVal.force

        let drawTextsFast (refSystem : AdaptiveReferenceSystem) (vp : AdaptiveViewPlan) : ISg<Action> = 
            let contents = 
                //let viewTrafo = view |> AVal.map CameraView.viewTrafo
                vp.distancePoints 
                |> AMap.map (fun _ point -> getTextPosition point.position point.distance vp.textSize.value )
                |> AMap.toASet 
                |> ASet.map snd 
                        
            let sg = 
                let config = { Text.TextConfig.Default with renderStyle = RenderStyle.Billboard; color = C4b.White }
                Sg.textsWithConfig config contents
                |> Sg.noEvents
                |> Sg.onOff vp.showDistanceText
            sg 
            |> Sg.trafo (getDistancePointOffsetTransform refSystem vp)

        let viewText (refSystem : AdaptiveReferenceSystem) (model : AdaptiveViewPlanModel) = //(vp : AdaptiveViewPlan) =
            let vps = model.viewPlans
            vps 
            |> AMap.map(fun id vp -> drawTextsFast refSystem vp )
            |> AMap.toASet 
            |> ASet.map snd 
            |> Sg.set
           
        
        let calcTrafo (view : aval<CameraView>) (shift : aval<Trafo3d>) (point : aval<V3d>) =
             adaptive {
                    let! view = view
                    let! shift = shift
                    let! point = point
                    let viewTrafo = view.ViewTrafo
                    return (Trafo3d.Translation(point) * shift * viewTrafo) 
                    }|> AVal.force

        //let viewDistanceDots (refSystem: AdaptiveReferenceSystem) (view : aval<CameraView>) (vp : AdaptiveViewPlan) =
        //    let shift = getDistancePointOffsetTransform refSystem vp
        //    let pointstest = 
        //        vp.distancePoints
        //        |> AMap.map (fun _ point -> point.position)
        //        |> AMap.toASet 
        //        |> ASet.map snd 
        //        |> ASet.map (fun point -> (calcTrafo view shift point) ) 
        //        |> ASet.toAList
        //        |> AList.toAVal |> AVal.map IndexList.toArray 
        //        //|> AVal.force
        //    let solCenterTrafo = 
        //        vp.distancePoints 
        //            |> AMap.map (fun _ point -> (calcTrafo view shift point.position) )
        //            |> AMap.toASet 
        //            |> ASet.map snd 
        //            |> ASet.toAList
        //            |> AList.toAVal |> AVal.map IndexList.toArray
            
                
        //    //let solNumbers =
        //    //    traverse.sols 
        //    //    |> AVal.map (fun sols -> 
        //    //        sols |> List.toArray |> Array.map (fun s -> s.solNumber) :> Array
        //    //    )

        //    let attributes = 
        //        Map.ofList [
        //            ("ModelTrafo", (typeof<Trafo3d>, (pointstest)))
        //            //("SolNumber", (typeof<int>, solNumbers))
        //        ]
        //    Sg.sphere 4 (AVal.constant(C4b.Orange)) ~~0.3
        //    |> Sg.shader {
        //        do! DefaultSurfaces.trafo // stable via modelTrafo = model view track trick
        //    }
        //    |> Sg.viewTrafo' Trafo3d.Identity // modelTrafo = model view track trick
        //    |> Sg.uniform "SelectionColor" ~~C4b.VRVisGreen
        //    |> Sg.uniform "SelectedSol" (vp.selectedDistPoint |> AVal.map (Option.defaultValue (Guid.Empty)))
        //    |> Sg.instanced' attributes
        //    |> Sg.noEvents
        //    //|> Sg.onOff traverse.showDots

        let calcPoint (selected : option<Guid>) (position : aval<V3d>) (id : Guid) (size : aval<float>) (col : aval<C4b>): ISg<Action> =
            adaptive {
                let! pos = position
                let! size = size
                let color =
                    match selected with
                    | Some sel -> 
                        if id = sel then  AVal.constant(C4b.VRVisGreen) else col
                    | None -> col
                return PRo3D.Core.Drawing.Sg.sphere' color ~~size ~~pos
                } |> Sg.dynamic

        let viewVPDistancePoints
            (refSystem : AdaptiveReferenceSystem) (model : AdaptiveViewPlanModel) = //(vp : AdaptiveViewPlan) = 

            let points =
                aset {
                    
                    for _,vp in model.viewPlans do 
                        let! selected = vp.selectedDistPoint
                        let et = 
                            vp.distancePoints
                            |> AMap.map (fun id (point:AdaptiveDistancePoint) -> calcPoint selected point.position id vp.dPointSize.value vp.dPointColor.c)
                            |> AMap.toASet 
                            |> ASet.map snd 
                            |> Sg.set
                            |> Sg.trafo (getDistancePointOffsetTransform refSystem vp)
                        yield et
                    }|> Sg.set

            points
        
        
    module UI =

    
        let viewHeader (m:AdaptiveViewPlan) (id:Guid) toggleMap = 
            [
                Incremental.text m.name; text " "

                i [clazz "home icon";                                                
                    onClick (fun _ -> FlyToViewPlan id)
                ] [] |> UI.wrapToolTip DataPosition.Bottom "FlyTo"                                                                                

                Incremental.i toggleMap AList.empty 
                |> UI.wrapToolTip DataPosition.Bottom "Toggle Arrows"                                                                     

                i [clazz "Remove icon red";                                             
                    onClick (fun _ -> RemoveViewPlan id)
                ] [] |> UI.wrapToolTip DataPosition.Bottom "Remove"                                         
            ]    

        let viewViewPlans (m:AdaptiveViewPlanModel) = 
            let listAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div listAttributes (
                alist { 
                    let! selected = m.selectedViewPlan
                    let viewPlans = 
                        m.viewPlans 
                        |> AMap.toASetValues 
                        |> ASet.sortBy (fun a -> a.id)

                    for vp in viewPlans do
                        let vpid = vp.id

                        let toggleIcon = 
                            AVal.map( fun toggle -> if toggle then "unhide icon" else "hide icon") vp.isVisible

                        let toggleMap = 
                            amap {
                                let! toggleIcon = toggleIcon
                                yield clazz toggleIcon
                                yield onClick (fun _ -> IsVisible vpid)
                            } |> AttributeMap.ofAMap  
                        
                        let itemAttributes = 
                            amap {                                
                                yield clazz "large cube middle aligned icon";

                                let color =
                                    match selected with
                                      | Some sel -> 
                                        AVal.constant (if sel = vpid then C4b.VRVisGreen else C4b.Gray) 
                                      | None -> AVal.constant C4b.Gray
                                                                         
                                let! c = color
                                let bgc = sprintf "color: %s" (Html.color c)

                                yield style bgc
                                yield onClick (fun _ -> SelectViewPlan vpid)
                            } |> AttributeMap.ofAMap

                        yield div [clazz "item"] [
                            Incremental.i itemAttributes AList.empty
                            div [clazz "content"] [
                                //Incremental.i listAttributes AList.empty
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
                | AdaptiveNone ->   div [] []
            )

        let viewFootprintProperties (fpVisible:aval<bool>) (m : AdaptiveViewPlan) = 
          m.selectedInstrument 
            |> AVal.map(fun x ->
              match x with 
              | AdaptiveSome _ -> 
                require GuiEx.semui (
                    Html.table [  
                        Html.row "show footprint:"  [GuiEx.iconCheckBox m.footPrint.isVisible ToggleFootprint]
                        Html.row "export footprint:"  [button [clazz "ui button tiny"; onClick (fun _ -> SaveFootPrint )] []]
                        Html.row "open footprint folder:"  [button [clazz "ui button tiny"; onClick (fun _ -> OpenFootprintFolder )] []]
                    ]
                )
              | AdaptiveNone -> div [] [])

        let viewImageHeader (m:AdaptiveProjectedImage) (id:Guid) = 
            [
                Incremental.text m.image; text " "

                i [clazz "Remove icon red";                                             
                    onClick (fun _ -> RemoveImage id)
                ] [] |> UI.wrapToolTip DataPosition.Bottom "Remove"                                         
            ]    


        let viewProjedtedImages (m:AdaptiveViewPlan) = 
            let listAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div listAttributes (
                alist { 
                    let! selected = m.footPrint.selectedImage
                    let images = 
                        m.footPrint.images 
                        |> AMap.toASetValues 
                        |> ASet.sortBy (fun a -> a.id)

                    for img in images do
                        let imgId = img.id
                        
                        let itemAttributes = 
                            amap {                                
                                yield clazz "large cube middle aligned icon";

                                let color =
                                    match selected with
                                      | Some sel -> 
                                        AVal.constant (if sel = imgId then C4b.VRVisGreen else C4b.Gray) 
                                      | None -> AVal.constant C4b.Gray
                                                                         
                                let! c = color
                                let bgc = sprintf "color: %s" (Html.color c)

                                yield style bgc
                                yield onClick (fun _ -> SelectImage imgId)
                            } |> AttributeMap.ofAMap

                        yield div [clazz "item"] [
                            Incremental.i itemAttributes AList.empty
                            div [clazz "content"] [
                                //Incremental.i listAttributes AList.empty
                                div [clazz "header"] (
                                    viewImageHeader img imgId
                                )      
                            ]
                        ]
                }
            )
            

        let viewProjectedImagesProperties (m : AdaptiveViewPlan) = 
            
            let readImagesGui =
                let attributes = 
                    alist {
                        yield Dialogs.onChooseFiles LoadImage;
                        yield clientEvent "onclick" (Dialogs.jsImportImagesDialog)
                        yield (style "word-break: break-all")
                    } |> AttributeMap.ofAList 

                let content =
                        alist {
                            yield i [clazz "ui button tiny"] []
                        }
                Incremental.div attributes content


            let jsImportImageInfoDialog =
                                "top.aardvark.dialog.showOpenDialog({title:'Import Imageinfo files' , filters: [{ name: '(*.json)', extensions: ['json']},], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

            let readImageInfoGui =
                //let attributes = 
                //    alist {
                //        yield Dialogs.onChooseFiles ImportExInTrinsics;
                //        yield clientEvent "onclick" (jsImportImageInfoDialog)
                //        yield (style "word-break: break-all")
                //    } |> AttributeMap.ofAList 

                //let content =
                //        alist {
                //            yield i [clazz "ui button tiny"] []
                //        }
                //Incremental.div attributes content

                let ui = 
                    alist {
                        yield
                            div [ 
                                clazz "ui item";
                                Dialogs.onChooseFiles  ImportExInTrinsics;
                                clientEvent "onclick" Dialogs.jsImportImagesDialog
                            ] [
                                i [clazz "ui button tiny"] [] //text "load image"
                            ]
                    }
                
                Incremental.div(AttributeMap.Empty) ui // |> UI.map this.Action

            require GuiEx.semui (
                Html.table [
                     Html.row "use image:"  [ GuiEx.iconCheckBox m.footPrint.useProjectedImage ToggleProjectedImage ]
                     Html.row "load image" [ readImagesGui ]
                     Html.row "load image info"  [readImageInfoGui]
                     Html.row "Images:" [ viewProjedtedImages m ]
                     ] 
                
            )

        // distance points gui
        let viewDistancePointHeader (m:AdaptiveDistancePoint) (id:Guid) = 
            [
                Incremental.text (m.distance |> AVal.map string); text " "

                i [clazz "home icon";                                                
                    onClick (fun _ -> LookAtPoint id)
                ] [] |> UI.wrapToolTip DataPosition.Bottom "LookAt"   

                i [clazz "Remove icon red";                                             
                    onClick (fun _ -> RemoveDistancePoint id)
                ] [] |> UI.wrapToolTip DataPosition.Bottom "Remove"                                         
            ]    


        let viewDistancePoints (m:AdaptiveViewPlan) = 
            let listAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div listAttributes (
                alist { 
                    let! selected = m.selectedDistPoint
                    let points = 
                        m.distancePoints 
                        |> AMap.toASetValues 
                        |> ASet.sortBy (fun a -> a.id)

                    for p in points do
                        let pId = p.id

                        let itemAttributes = 
                            amap {                                
                                yield clazz "large cube middle aligned icon";

                                let color =
                                    match selected with
                                      | Some sel -> 
                                        AVal.constant (if sel = pId then C4b.VRVisGreen else C4b.Gray) 
                                      | None -> AVal.constant C4b.Gray
                                                                         
                                let! c = color
                                let bgc = sprintf "color: %s" (Html.color c)

                                yield style bgc
                                yield onClick (fun _ -> SelectDistancePoint pId)
                            } |> AttributeMap.ofAMap

                        yield div [clazz "item"] [
                            Incremental.i itemAttributes AList.empty
                            div [clazz "content"] [
                                //Incremental.i listAttributes AList.empty
                                div [clazz "header"] (
                                    viewDistancePointHeader p pId //toggleMap
                                )      
                            ]
                        ]
                }
            )
            

        let viewDistancePointsProperties (m : AdaptiveViewPlan) = 

            require GuiEx.semui (
                Html.table [
                     Html.row "show Text:" [GuiEx.iconCheckBox m.showDistanceText ToggleText ]
                     Html.row "textsize:" [Numeric.view' [InputBox] m.textSize |> UI.map SetTextSize]
                     Html.row "pointsize:" [Numeric.view' [InputBox] m.dPointSize |> UI.map SetDPointSize]
                     Html.row "color:"  [ColorPicker.view m.dPointColor |> UI.map SetPointColor ]
                     Html.row "Points:" [ viewDistancePoints m ]
                     ] 
                
            )

            ///////////////////////

        let viewDepthColorLegendUI (m : AdaptiveViewPlan) = 
            m.footPrint.depthColorLegend
            |> FalseColorLegendApp.viewDepthLegendProperties DepthColorLegendMessage 
            |> AVal.constant

        let viewDepthImageProperties (m : AdaptiveViewPlan) = 
          m.selectedInstrument 
            |> AVal.map(fun x ->
              match x with 
              | AdaptiveSome _ -> 
                require GuiEx.semui (
                    Html.table [  
                        Html.row "show depth:"  [GuiEx.iconCheckBox m.footPrint.isDepthVisible ToggleDepth]
                        ] 
                    //]
                    

                )
              | AdaptiveNone -> div [] [])


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
                            yield div [] [Incremental.text axis.id; text "(deg)"]
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
                            yield div [] [text "["; Incremental.text (axis.angle.min |> AVal.map string); text ";"; Incremental.text (axis.angle.max |> AVal.map string); text "]"]
                            yield br []
                    | AdaptiveNone -> yield div [] []

            }

        let viewRoverProperties' (model : AdaptiveViewPlanModel) (r : AdaptiveRover) (m : AdaptiveViewPlan) (fpVisible:aval<bool>) (diVisible:aval<bool>) =
            require GuiEx.semui (
                Html.table [
                     Html.row "Change VPName:" [Html.SemUi.textBox m.name SetVPName]
                     Html.row "Name:"       [ Incremental.text r.id ]
                     Html.row "Instrument:" [ 
                        instrumentsDd r m 
                        Incremental.div AttributeMap.empty (AList.ofAValSingle (viewInstrumentProperties m))                    
                     ]
                     Html.row "Axes:" [   
                        Incremental.div AttributeMap.empty (viewAxesList r m)
                     ]
                     Html.row "DistancePoints:" [   
                        Incremental.div AttributeMap.empty (AList.ofAValSingle ( (viewDistancePointsProperties m)|> AVal.constant ))
                     ]
                     Html.row "Footprint:" [   
                        Incremental.div AttributeMap.empty (AList.ofAValSingle ( viewFootprintProperties fpVisible m ))
                     ]
                     Html.row "ProjectedImages:" [   
                        Incremental.div AttributeMap.empty (AList.ofAValSingle ( (viewProjectedImagesProperties m)|> AVal.constant ))
                     ]
                     Html.row "Depthimage:" [   
                        Incremental.div AttributeMap.empty (AList.ofAValSingle ( viewDepthImageProperties m ))
                     ]
                     Html.row "Colors:" [   
                        Incremental.div AttributeMap.empty (AList.ofAValSingle (viewDepthColorLegendUI m))
                     ]
                     
                     ]
                
            )

        let viewSelectRover (m : AdaptiveRoverModel) : DomNode<RoverApp.Action> =
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large Rocket icon"] [] ]
                Html.Layout.boxH [ 
                    UI.dropDown'' 
                        (RoverApp.roversList m)  
                        (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption m.selectedRover)
                        (fun x -> RoverApp.Action.SelectRover (x |> Option.map(fun y -> y.Current |> AVal.force))) 
                        (fun x -> (x.id |> AVal.force))
                    ]                
            ]


        let viewRoverProperties lifter (fpVisible:aval<bool>) (diVisible:aval<bool>) (model : AdaptiveViewPlanModel) = 
            adaptive {
                let! guid = model.selectedViewPlan
                let empty = div [] [] |> UI.map lifter 

                match guid with
                | Some id -> 
                  let! vp = model.viewPlans |> AMap.tryFind id
                  match vp with
                  | Some x -> return (viewRoverProperties' model x.rover x fpVisible diVisible |> UI.map lifter)
                  | None -> return empty
                | None -> return empty
            }  



       
