namespace PRo3D

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.VRVis
open FSharp.Data.Adaptive
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.Base.Rendering

open System
open System.Diagnostics
open System.IO

open PRo3d
open PRo3D.ReferenceSystem
open PRo3D.Surfaces
open PRo3D.Groups
open PRo3D.Navigation2
open Aardvark.UI.Primitives
open CSharpUtils

module ViewPlanApp = 
    open IPWrappers
    open Aardvark.Base.MultimethodTest
    open Aardvark.Geometry
    
    type Action =
      | AddPoint         of V3d*ReferenceSystem*HashMap<string, ConcreteKdIntersectionTree>*SurfaceModel 
      | SelectViewPlan   of Guid
      | FlyToViewPlan    of Guid
      | IsVisible        of Guid
      | RemoveViewPlan   of Guid
      | SelectInstrument of option<Instrument>
      | SelectAxis       of option<Axis>
      | ChangeAngle      of string * PRo3DNumeric.Action
      | ChangeFocal      of string * Numeric.Action
      | SetVPName        of string
      | ToggleFootprint
      | SaveFootPrint
      | OpenFootprintFolder
            
    let loadRoverData (model : ViewPlanModel) (path : Option<string>) =      
      match path with
      | Some _ ->
        let directory = @".\InstrumentStuff"
        let errorCode = ViewPlanner.Init(directory, directory)
        printfn "%A" errorCode

        let names = RoverProvider.platformNames()    
        printfn "%A" names

        let roverData =
            names 
              |> Array.map RoverProvider.initRover 
              |> List.ofArray 

        let rovs = roverData|> List.map(fun (r,_) -> r.id, r) 
        let rovsHm = rovs |> HashMap.ofList
        let plats = roverData|> List.map(fun (r,p) -> r.id, p) |> HashMap.ofList

        printfn "found rover(s): %d" (rovsHm |> HashMap.count)
        
        let selected = rovs |> List.tryHead |> Option.map snd          
              
        let roverModel = { model.roverModel with rovers = rovsHm; platforms = plats; selectedRover = selected }
        { model with roverModel = roverModel }
      | _ -> model

    let updateViewPlans (vp:ViewPlan) (vps:HashSet<ViewPlan>) =
      vps |> HashSet.alter vp (fun x -> x)
    
    let updateViewPlanFromRover (roverModel:RoverModel) (vp:ViewPlanModel) =
      // update rover model
      let m = {vp with roverModel = roverModel}
      // update selected view plan and viewplans
      match vp.selectedViewPlan with
      | Some v -> 
        let viewPlans = updateViewPlans v vp.viewPlans
        let m = { m with viewPlans = viewPlans }

        match roverModel.selectedRover with
        | Some r -> { m with selectedViewPlan = Some { v with rover = r }}
        | None -> m
      | None -> m 

    let getInstrumentResolution (vp:MViewPlanModel) =
      adaptive {
        let! selected = vp.selectedViewPlan
        let fail = (uint32(1024), uint32(1024))

        match selected with
        | Some v -> 
          let! selectedI = v.selectedInstrument
          match selectedI with
          | Some i -> 
            let! intrinsics = i.intrinsics
            let horRes  = intrinsics.horizontalResolution
            let vertRes = intrinsics.verticalResolution
            return (horRes, vertRes)
          | None -> return fail
        | None -> return fail
      } 
        
    let trafoFromTranslatedBase (position:V3d) (tilt:V3d) (forward:V3d) (right:V3d) : Trafo3d =  
      let rotTrafo =  Trafo3d.FromOrthoNormalBasis( forward.Normalized, right.Normalized, tilt.Normalized )
      (rotTrafo * Trafo3d.Translation(position)) 

    let getPlaneNormalSign (v1:V3d) (v2:V3d) =
      let sign = V3d.Dot(v2, v1)
      (sign <= 0.0)

    let hitF (p : V3d) (dir:V3d) (refSystem : ReferenceSystem.ReferenceSystem) 
      (cache : HashMap<string, ConcreteKdIntersectionTree>) (surfaceModel:SurfaceModel) = 

      let mutable cache = cache
      let ray = FastRay3d(p, dir)  
               
      match SurfaceApp.doKdTreeIntersection surfaceModel refSystem ray cache with
        | Some (t,_), _ -> ray.Ray.GetPointOnRay(t) |> Some
        | None, _ -> None
       
    let initialPlacementTrafo (position:V3d) (lookAt:V3d) (up:V3d) : Trafo3d =
      let forward = (lookAt - position).Normalized
      let n = V3d.Cross(forward, up.Normalized).Normalized
      let tilt = V3d.Cross(n, forward).Normalized
      let right = V3d.Cross(tilt, forward).Normalized

      trafoFromTranslatedBase position tilt forward right
        
    let calculateSurfaceContactPoint (roverWheel:V3d) (refSystem : ReferenceSystem.ReferenceSystem) 
      (cache : HashMap<string, ConcreteKdIntersectionTree>) (trafo:Trafo3d) (surfaceModel:SurfaceModel) =

      let tilt = trafo.Forward.UpperLeftM33().C2;
      let wheelT = trafo.Forward.TransformPos(roverWheel)
      let origin = wheelT + 5000.0 * tilt //move up ray origin
      hitF origin (-tilt) refSystem cache surfaceModel      
       
    let nearestPoint (x:V3d) (plane:Plane3d) =
      let p = plane.Point;
      (x - plane.Normal.Dot(x - p) * plane.Normal);
    
    let calculateRoverPlacement (working:list<V3d>) (wheels:list<V3d>) (up:V3d) : Trafo3d = 
      let forward = (working.[1] - working.[0]).Normalized
      // projects intersection points along the lookAt vector onto a plane
      let plane1 = new Plane3d(forward, working.[1]);
      let planeIntersectionPoints = wheels |> List.map(fun x -> nearestPoint x plane1) |> List.toArray

      // line fitting with the projected points to get right vector
      let rightVecLine = PlaneFitting.Line planeIntersectionPoints //fitLineLeastSquares planeIntersectionPoints

      let newRightVec = rightVecLine.Direction.Normalized
      let newTiltVec = V3d.Cross(forward, newRightVec).Normalized

      match getPlaneNormalSign up newTiltVec with
      | true -> 
        let newTilt = newTiltVec.Negated
        let newRight = newRightVec.Negated
        trafoFromTranslatedBase working.[0] newTilt forward newRight
      | false -> 
        trafoFromTranslatedBase working.[0] newTiltVec forward newRightVec
        
    let createViewPlan (working:list<V3d>) (rover:Rover) (ref:ReferenceSystem) (camState:CameraControllerState) 
      (kdTree:HashMap<string, ConcreteKdIntersectionTree>) (surfaceModel:SurfaceModel) =

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
        id = Guid.NewGuid()
        name = rover.id
        position = position
        lookAt = lookAt
        viewerState = camState
        vectorsVisible = true
        rover = rover
        roverTrafo = placementTrafo
        isVisible = true
        selectedInstrument = None
        selectedAxis = None
        currentAngle = angle
      }

      newViewPlan

    let createViewPlanFromFile (data:HashMap<string,string>) (model:ViewPlanModel) (rover:Rover)
      (ref:ReferenceSystem) (camState:CameraControllerState) =

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
        let forward = rot.TransformDir(ref.north.value) //ref.north.value
        let forward' = pos + forward
        let trafo = initialPlacementTrafo pos forward' ref.up.value
        let angle = {
          value = 0.0
          min =  -90.0
          max = 90.0
          step = 1.0
          format = "{0:0.0}"
        }

        let newViewPlan = {
          id = Guid.NewGuid()
          name = rover.id
          position = pos
          lookAt = forward
          viewerState = camState
          vectorsVisible = true
          rover = rover
          roverTrafo = trafo
          isVisible = true
          selectedInstrument = None
          selectedAxis = None
          currentAngle = angle
        }

        { model with 
            viewPlans = HashSet.add newViewPlan model.viewPlans
            working = List.Empty
            selectedViewPlan = Some newViewPlan }

    let removeViewPlan (vps:HashSet<ViewPlan>) (id:Guid) = 
        vps |> HashSet.filter (fun x -> x.id <> id)   
    
    let transformExtrinsics (vp:ViewPlan) (ex:Extrinsics) =
      { 
        ex with 
          position  = vp.roverTrafo.Forward.TransformPos ex.position
          camLookAt = vp.roverTrafo.Forward.TransformDir ex.camLookAt
          camUp     = vp.roverTrafo.Forward.TransformDir ex.camUp
      }

    let updateInstrumentCam (vp:ViewPlan) (model:ViewPlanModel) (fp:FootPrint) : (FootPrint * ViewPlanModel)=
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
            instrumentCam     = { model.instrumentCam with view = view }
            instrumentFrustum = ifrustum 
        }

        Log.line "%A" model.instrumentCam.view.ViewTrafo

        let f = FootPrint.updateFootprint i vp.position m'
        f,m'
        
      | None -> 
        fp, { model with 
                instrumentCam     = { model.instrumentCam with view = CameraView.lookAt V3d.Zero V3d.One V3d.OOI }
                instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0 }
      
    let updateRovers (model:ViewPlanModel) (roverModel:RoverModel) (vp:ViewPlan) (fp:FootPrint) =
        let r = roverModel.rovers  |> HashMap.find vp.rover.id
        let i = 
            match vp.selectedInstrument with
            | Some i -> Some (r.instruments |> HashMap.find i.id)
            | None   -> None

        let vp = { vp with rover = r; selectedInstrument = i }  
        let m  = {
          model with 
            selectedViewPlan = Some vp
            viewPlans        = model.viewPlans |> HashSet.alter vp id
        }

        let fp, m = updateInstrumentCam vp m fp
        fp, { m with roverModel = roverModel }

    //let update (model : ViewPlanModel) (camState:CameraControllerState) (action : Action) =
    let update (model : ViewPlanModel) (action : Action) (navigation : Lens<'a,NavigationModel>)
      (footprint : Lens<'a,FootPrint>) (scenepath:Option<string>) (outerModel:'a) : ('a * ViewPlanModel) = 
      
      match action with
      | AddPoint (p,ref,kdTree,surfaceModel) ->
        match model.roverModel.selectedRover with
        | Some r -> 
          match model.working.IsEmpty with
          | true -> 
            outerModel, {model with working = [p]} // first point (position)
          | false -> // second point (lookAt)
            let w = List.append model.working [p]
            let navigation = (navigation.Get outerModel)
            let wp = createViewPlan w r ref navigation.camera kdTree surfaceModel
            outerModel, { model with viewPlans = HashSet.add wp model.viewPlans; working = List.Empty; selectedViewPlan = Some wp }
        | None -> outerModel, model

      | SelectViewPlan id ->
          let vp = model.viewPlans |> Seq.tryFind(fun x -> x.id = id)
          let fp = (footprint.Get outerModel)
          let vp', m , om =
            match vp, model.selectedViewPlan with
            | Some a, Some b -> 
              if a.id = b.id then 
                None, model, outerModel
              else 
                let fp', m' = updateInstrumentCam a model fp
                let newOuterModel = footprint.Set(outerModel, fp')
                Some a, m', newOuterModel
            | Some a, None -> 
              let fp', m' = updateInstrumentCam a model fp
              let newOuterModel = footprint.Set(outerModel, fp')
              Some a, m', newOuterModel
            | None, _ -> 
              None, model, outerModel
          
          om, { m with selectedViewPlan = vp' }

      | FlyToViewPlan id -> 
        let vp = model.viewPlans |> Seq.tryFind(fun x -> x.id = id)
        match vp with
        | Some v-> 
          let nav = (navigation.Get outerModel)
          let nav' = { nav with camera = v.viewerState }
          let newOuterModel = navigation.Set(outerModel, nav')
          (newOuterModel, model)
        | _ -> 
          (outerModel, model)

      | IsVisible id ->         
        let viewPlans =  model.viewPlans |> HashSet.map(fun x -> if x.id = id then { x with isVisible = not x.isVisible } else x)
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
          let fp            = (footprint.Get outerModel)
          let fp', m'       = updateInstrumentCam newVp model fp
          let newOuterModel = footprint.Set(outerModel, fp')

          let viewPlans = model.viewPlans |> HashSet.alter newVp id

          newOuterModel, { m' with selectedViewPlan = Some newVp; viewPlans = viewPlans }
        | None -> outerModel, model                                                     

      | SelectAxis a       -> 
        match model.selectedViewPlan with
        | Some vp -> 
          let newVp = { vp with selectedAxis = a }
          let viewPlans = model.viewPlans |> HashSet.alter newVp id

          outerModel, { model with selectedViewPlan = Some newVp; viewPlans = viewPlans }
        | None -> outerModel, model    

      | ChangeAngle (id,a) -> 
        match model.selectedViewPlan with
        | Some vp -> 
          match vp.rover.axes.TryFind id with
          | Some ax -> 
            let angle = PRo3DNumeric.update ax.angle a
            let ax' = { ax with angle = angle } 

            let rover = { vp.rover with axes = (vp.rover.axes |> HashMap.update id (fun _ -> ax')) }
            let vp' = { vp with rover = rover; currentAngle = angle }

            let angleUpdate = { 
              roverId = vp'.rover.id
              axisId = ax'.id ; 
              angle = ax'.angle.value 
            }

            let roverModel' = RoverApp.updateAnglePlatform angleUpdate model.roverModel
            let fp = (footprint.Get outerModel)
            let fp', m' = updateRovers model roverModel' vp' fp
            let newOuterModel = footprint.Set(outerModel, fp')
                                        
            newOuterModel, m'
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
            let fp = (footprint.Get outerModel)
            let fp', m' = updateRovers model roverModel' vp' fp
            let newOuterModel = footprint.Set(outerModel, fp')
            
            newOuterModel, m'
          | None -> outerModel, model                                       
        | None -> outerModel, model 

      | SetVPName t -> 
        match model.selectedViewPlan with
        | Some vp -> 
          let vp' = {vp with name = t}
          let viewPlans = model.viewPlans |> HashSet.alter vp' id
          outerModel, {model with selectedViewPlan = Some vp'; viewPlans = viewPlans }              
        | None -> outerModel, model

      | ToggleFootprint ->   
        let fp = (footprint.Get outerModel)
        let fp' = { fp with isVisible = not fp.isVisible }
        let newOuterModel = footprint.Set(outerModel, fp')
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
        let drawWorking (model:MViewPlanModel) =
            let point0 =
                AVal.map( fun w -> match w |> List.tryHead with
                                            | Some p -> 
                                                PRo3D.Sg.dot (AVal.constant p) (AVal.constant 3.0) (AVal.constant C4b.Green)
                                            | None -> Sg.empty
                        ) model.working |> AVal.toASet |> Sg.set
              
            let point1 =
                AVal.map( fun w -> match w |> List.tryLast with
                                            | Some p -> 
                                                PRo3D.Sg.dot (AVal.constant p) (AVal.constant 3.0) (AVal.constant C4b.Green)
                                            | None -> Sg.empty
                        ) model.working |> AVal.toASet |> Sg.set
            Sg.ofList [point0;point1]
        
        let drawInitPositions (viewPlan : MViewPlan) (cam:aval<CameraView>)=
            Sg.ofList [
                    ReferenceSystemApp.Sg.point viewPlan.position (AVal.constant C4b.Green) cam // position
                    ReferenceSystemApp.Sg.point viewPlan.lookAt (AVal.constant C4b.Yellow) cam // lookAt pos
                    ]

        let drawVectors (viewPlan : MViewPlan)(near:aval<float>) (length:aval<float>) 
                        (thickness:aval<float>) (cam:aval<CameraView>) =

            let lookAtVec =  AVal.map(fun (t:Trafo3d) -> t.Forward.UpperLeftM33().C0) viewPlan.roverTrafo
            let rightVec = AVal.map(fun (t:Trafo3d) -> t.Forward.UpperLeftM33().C1) viewPlan.roverTrafo
            let tiltVec = AVal.map(fun (t:Trafo3d) -> t.Forward.UpperLeftM33().C2) viewPlan.roverTrafo

            //let size = Sg.computeInvariantScale cam near viewPlan.position length (AVal.constant 60.0)

            let marker : ReferenceSystemApp.Sg.MarkerStyle = {
                position  = viewPlan.position
                direction = AVal.constant V3d.NaN
                color     = AVal.constant C4b.Black
                size      = length //size
                thickness = thickness
                hasArrow  = AVal.constant true
                text      = AVal.constant None
                fix       = AVal.constant false
            }

            let lookAt = { marker with direction = lookAtVec; color = (AVal.constant C4b.Yellow)}
            let right  = { marker with direction = rightVec;  color = (AVal.constant C4b.Red)}
            let tilt   = { marker with direction = tiltVec;   color = (AVal.constant C4b.Red)}

            Sg.ofList [
                    lookAt |> ReferenceSystemApp.Sg.directionMarker near cam
                    right  |> ReferenceSystemApp.Sg.directionMarker near cam
                    tilt   |> ReferenceSystemApp.Sg.directionMarker near cam                    
                ] |> Sg.onOff viewPlan.isVisible
        
        let drawAxis (axis:MAxis) (cam:aval<CameraView>) (thickness:aval<float>) (trafo:aval<Trafo3d>) =
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
            let color = AVal.constant C4b.Red
            Sg.ofList [
                    Sg.dot start (AVal.constant 3.0) color  
                    Sg.dot endp  (AVal.constant 3.0) color
                    PRo3D.Sg.lines axisLine (AVal.constant 0.0) color thickness trafo
                ] 
        
        let drawInstruments (instruments:alist<MInstrument>) (viewPlan:MViewPlan) (near:aval<float>)
                            (length:aval<float>) (thickness:aval<float>) (cam:aval<CameraView>) =
            alist {
                let! trafo = viewPlan.roverTrafo

                for i in instruments do

                    let camPosTrans = AVal.map(fun p -> trafo.Forward.TransformPos p) i.extrinsics.position
                    let camLookAtTrans = AVal.map(fun p -> trafo.Forward.TransformDir p) i.extrinsics.camLookAt
                    let camUpTrans = AVal.map(fun p -> trafo.Forward.TransformDir p) i.extrinsics.camUp

                    let! selInst = viewPlan.selectedInstrument

                    match selInst with
                        | Some s -> 
                            let! sid = s.id
                            let! id = i.id
                            //let size = Sg.computeInvariantScale cam near viewPlan.position length (AVal.constant 60.0)

                            let marker : ReferenceSystemApp.Sg.MarkerStyle = {
                                position  = camPosTrans
                                direction = AVal.constant V3d.NaN
                                color     = AVal.constant C4b.Black
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
                                        ReferenceSystemApp.Sg.point camPosTrans (AVal.constant C4b.Cyan) cam // position
                                        lookAt |> ReferenceSystemApp.Sg.directionMarker near cam 
                                        up     |> ReferenceSystemApp.Sg.directionMarker near cam
                                    ] 
                            | false -> 
                              let lookAt = { marker with direction = camLookAtTrans; color = (AVal.constant C4b.Blue)}                              
                              yield Sg.ofList [
                                        lookAt |> ReferenceSystemApp.Sg.directionMarker near cam 
                                        up     |> ReferenceSystemApp.Sg.directionMarker near cam
                                    ] 
                        | None -> yield Sg.ofList []
                }
                          
        let drawWheels (vp:MViewPlan) (cam:aval<CameraView>) =
          alist {
            let! wheels = vp.rover.wheelPositions
            let! trafo = vp.roverTrafo
            for w in wheels do
                let wheelPos = trafo.Forward.TransformPos w |> AVal.constant
                yield ReferenceSystemApp.Sg.point wheelPos (AVal.constant C4b.White) cam
            }
                
        let drawSelectionGeometry (vp:MViewPlan) (near:aval<float>) (length:aval<float>) 
          (thickness:aval<float>) (cam:aval<CameraView>) (roverM:MRoverModel)= 

            let wheels = 
              drawWheels vp cam
                |> ASet.ofAList 
                |> Sg.set

            let axes = vp.rover.axes |> RoverApp.mapTolist
                        
            let sgAxes = 
              axes 
                |> AList.map(fun a -> drawAxis a cam thickness vp.roverTrafo) 
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

        let view<'ma> (mbigConfig : 'ma) (minnerConfig : ReferenceSystemApp.MInnerConfig<'ma>)
          (model:MViewPlanModel) (cam:aval<CameraView>) : ISg<Action> =
                       
            let length    = minnerConfig.getArrowLength    mbigConfig
            let thickness = minnerConfig.getArrowThickness mbigConfig
            let near      = minnerConfig.getNearDistance   mbigConfig

            let viewPlans =
              aset {
                for vp in model.viewPlans do 
                 yield drawInitPositions vp cam
                 let! showVectors = vp.isVisible
                 if showVectors then
                   yield drawVectors vp near length thickness cam
                   let! selected = model.selectedViewPlan
                   let! id = vp.id
                   let! selDrawing = 
                     match selected with
                       | Some s -> 
                         let sg = (drawSelectionGeometry vp near length thickness cam model.roverModel) 
                         s.id |> AVal.map (fun g -> if g = id then sg else Sg.empty)
                       | None -> AVal.constant Sg.empty
                   yield selDrawing
                 else ()
              } |> Sg.set
                
            Sg.ofList [
                viewPlans
                drawWorking model
            ]
    
    module UI =

        let viewHeader (m:MViewPlan) (id:Guid) toggleMap = 
            [
                Incremental.text m.name; text " "

                i [clazz "home icon";                                                
                    onClick (fun _ -> FlyToViewPlan id)
                ][] |> UI.wrapToolTip "FlyTo" TTAlignment.Top                                                                               

                Incremental.i toggleMap AList.empty |> UI.wrapToolTip "Toggle Arrows" TTAlignment.Top                                                                    

                i [clazz "Remove icon red";                                             
                    onClick (fun _ -> RemoveViewPlan id)
                ][] |> UI.wrapToolTip "Remove" TTAlignment.Top                                        
            ]    

        let viewViewPlans (m:MViewPlanModel) = 
          let itemAttributes =
              amap {
                  yield clazz "ui divided list inverted segment"
                  yield style "overflow-y : visible"
              } |> AttributeMap.ofAMap

          Incremental.div itemAttributes (
            alist { 
              yield Incremental.i itemAttributes AList.empty
              let! selected = m.selectedViewPlan
              let viewPlans = m.viewPlans |> ASet.toAList
              for vp in viewPlans do
                let! vpid = vp.id
                let! color =
                    match selected with
                      | Some sel -> 
                        sel.id |> AVal.map (fun g -> if g = vpid then C4b.VRVisGreen else C4b.White)
                      | None -> AVal.constant C4b.White
                                                         
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
        
        let focalGui (i : MInstrument) =
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

        let instrumentProperties(i : MInstrument) =
            let sensor = i.intrinsics |> AVal.map(fun x -> sprintf "%d X %d" x.horizontalResolution x.verticalResolution)

            require GuiEx.semui (
                Html.table [                
                    Html.row "Sensor (px):"  [ Incremental.text sensor ]
                    Html.row "Focal (mm):"   [ focalGui i ]                
                ]
            )

        let viewInstrumentProperties (m : MViewPlan) = 
          m.selectedInstrument 
            |> AVal.map(fun x ->
                match x with 
                  | Some i -> instrumentProperties i
                  | None ->   div[][])

        let viewFootprintProperties (fpVisible:aval<bool>) (m : MViewPlan) = 
          m.selectedInstrument 
            |> AVal.map(fun x ->
              match x with 
              | Some _ -> 
                require GuiEx.semui (
                    Html.table [  
                        Html.row "show footprint:"  [GuiEx.iconCheckBox fpVisible ToggleFootprint]
                        Html.row "export footprint:"  [button [clazz "ui button tiny"; onClick (fun _ -> SaveFootPrint )][]]
                        Html.row "open footprint folder:"  [button [clazz "ui button tiny"; onClick (fun _ -> OpenFootprintFolder )][]]
                    ]
                )
              | None -> div[][])


        //let instrumentsDd (r:MRover) (m : MViewPlan) = 
        //    UI.dropDown'' (r.instruments |> RoverApp.mapTolist) m.selectedInstrument (fun x -> SelectInstrument (x |> Option.map(fun y -> y.Current |> AVal.force))) (fun x -> (x.id |> AVal.force) )   
        
        let instrumentsDd (r:MRover) (m : MViewPlan) = 
            UI.dropDown'' (r.instruments |> RoverApp.mapTolist) 
                           m.selectedInstrument (fun x -> SelectInstrument (x |> Option.map(fun y -> y.Current |> AVal.force))) 
                                                (fun x -> (x.id |> AVal.force) )   

        let viewAxesList (r : MRover) (m : MViewPlan) =
            alist {
                let! selectedI = m.selectedInstrument
                match selectedI with
                    | Some i ->
                        for axis in (r.axes |> RoverApp.mapTolist) do
                            yield div[][Incremental.text axis.id; text "(deg)"]
                            //yield Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox] axis.angle |> UI.map (fun x -> ChangeAngle (axis.id |> AVal.force,x))
                            let! id = axis.id
                            let! value = axis.angle.value
                            let! max = axis.angle.max
                            let! min = axis.angle.min
                            //yield Numeric.view' [InputBox] axis.angle |> UI.map (fun x -> ChangeAngle (id,x))
                            yield PRo3DNumeric.view' [NumericInputType.Slider; NumericInputType.InputBox] axis.angle |> UI.map (fun x -> ChangeAngle (id,x))
                            //if (value <= max) && (value >= min) then                    
                            //    yield Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox] axis.angle |> UI.map (fun x -> ChangeAngle (id,x))
                            //else
                            //    yield Incremental.text (axis.angle.value |> AVal.map string)
                            //yield br[]
                            //yield Incremental.text (axis.startPoint |> AVal.map (sprintf "%A"))
                            yield div[][text "["; Incremental.text (axis.angle.min |> AVal.map string); text ";"; Incremental.text (axis.angle.max |> AVal.map string); text "]"]
                            yield br[]
                    | None -> yield div[][]

            }

        let viewRoverProperties' (r : MRover) (m : MViewPlan) (fpVisible:aval<bool>) =
            require GuiEx.semui (
                Html.table [
                     Html.row "Change VPName:"[ Html.SemUi.textBox m.name SetVPName ]
                     Html.row "Name:"       [ Incremental.text r.id ]
                     Html.row "Instrument:" [ 
                        instrumentsDd r m 
                        Incremental.div AttributeMap.empty (AList.ofModSingle (viewInstrumentProperties m))                    
                     ]
                     Html.row "Axes:" [   
                        Incremental.div AttributeMap.empty (viewAxesList r m)
                     ]
                     Html.row "Footprint:" [   
                        Incremental.div AttributeMap.empty (AList.ofModSingle ( viewFootprintProperties fpVisible m ))
                     ]
                     ]
                
            )

        let viewSelectRover (m : MRoverModel) : DomNode<RoverApp.Action> =
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large Rocket icon"][] ]
                Html.Layout.boxH [ UI.dropDown'' (RoverApp.roversList m)  
                                                  m.selectedRover
                                                  (fun x -> RoverApp.Action.SelectRover (x |> Option.map(fun y -> y.Current |> AVal.force))) 
                                                  (fun x -> (x.id |> AVal.force) ) ]                
            ]

        let viewRoverProperties lifter (fpVisible:aval<bool>) (model : MViewPlanModel) = 
            model.selectedViewPlan 
                |> AVal.map(fun x ->
                    match x with 
                      | Some x -> viewRoverProperties' x.rover x fpVisible |> UI.map lifter
                      | None ->   div[][] |> UI.map lifter)        

       