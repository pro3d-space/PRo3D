namespace PRo3D.SimulatedViews

open System
open Aardvark.Base
open Aardvark.UI
open System.IO
open FShade
open FSharp.Data.Adaptive
open PRo3D.Core
open PRo3D.Core.Surface

//open Aardvark.GeoSpatial.Opc.Shader

module PlacementUtils =
    let addSnapshotGroup (m    : SurfaceModel) =
        let groupsModel = GroupsApp.addGroupToRoot m.surfaces "snapshots"
        {m with surfaces = groupsModel}

    let clearSnapshotNode (m : SurfaceModel) (n : Node) =
        let groups = GroupsApp.clearGroupAtRoot m.surfaces "snapshots"
        let sgs = 
          m.sgSurfaces 
              |> HashMap.filter(fun k _ -> groups.flat |> HashMap.containsKey k)
        {m with surfaces = groups; sgSurfaces = sgs } |> SurfaceModel.triggerSgGrouping  

    let clearSnapshotGroup (m : SurfaceModel) =
        let snapShotNode = 
            m.surfaces.rootGroup.subNodes 
                |> IndexList.toList
                |> List.tryFind(fun x -> x.name = "snapshots")
        let m = 
          match snapShotNode with
          | Some n -> 
              clearSnapshotNode m n
          | None -> m
        m

    let getSnapshotGroup (surfacesModel : SurfaceModel) =
        let snapshotGroup = 
            surfacesModel.surfaces.rootGroup.subNodes 
              |> IndexList.toList
              |> List.tryFind(fun x -> x.name = "snapshots")
        let m, snapshotGroup =
            match snapshotGroup with
            | Some s -> surfacesModel,s
            | None   -> 
                Log.line "[SCUtils] No snapshot group found. Creating group."
                let m = addSnapshotGroup surfacesModel
                let sng = 
                    surfacesModel.surfaces.rootGroup.subNodes 
                      |> IndexList.toList
                      |> List.find(fun x -> x.name = "snapshots")
                m,sng
        m, snapshotGroup

    let getSurfacesInSnapshotGroup (surfacesModel : SurfaceModel) = 
        let surfs = surfacesModel.surfaces.flat 
                            |> HashMap.toList
                            |> List.map(fun (_,v) -> v |> Leaf.toSurface)
        //get all objs from the "snapshots" group
        let m, snapshotGroup = getSnapshotGroup surfacesModel
        let sObjsIds =
            snapshotGroup
              |> GroupsApp.collectLeaves                
              |> IndexList.toList 
        let sObjsSurfs =
            sObjsIds
              |> List.map(fun id -> (surfs |> List.find (fun x -> x.guid = id)))
        let sObjsSgs = 
            surfacesModel.sgSurfaces
                |> HashMap.filter(fun k _ -> sObjsIds |> List.contains k)
                |> HashMap.toList
        sObjsSgs, sObjsSurfs

    let writeDistancesToFile (surfs:list<Surface>) 
                             (filename:string) (name:string) (nav : NavigationModel) =
        let distances =
            surfs
            |> List.map( fun s -> Vec.Distance(s.preTransform.Forward.C3.XYZ, nav.camera.view.Location))
            
        let distXml = sprintf "%s_%s_distances.xml" filename name
        let text = (distances |> List.map(fun d -> sprintf "%f\n" d)) |> List.fold (+)""
        //Log.error "%s" text
        File.WriteAllText(distXml, text)

   
    let updateColorCorrection (objectPlacement : ObjectPlacementParameters) (surf : Surface) =
        let colorAdaption = surf.colorCorrection
        let sColor = 
            match objectPlacement.color with
            | Some col -> {colorAdaption with color = {c = col}; useColor = true}
            | None -> colorAdaption
        let conColor = 
            match objectPlacement.contrast with
            | Some contr -> let contrast = {sColor.contrast with value = contr}
                            {sColor with contrast = contrast; useContrast = true}
            | None -> sColor
        let brightnColor = 
            match objectPlacement.brightness with
            | Some brightn -> let brightness = {conColor.brightness with value = brightn}
                              {conColor with brightness = brightness; useBrightn = true}
            | None -> conColor
        let gammaColor = 
            match objectPlacement.gamma with
            | Some gamma -> let gamma = {brightnColor.gamma with value = gamma}
                            {brightnColor with gamma = gamma; useGamma = true}
            | None -> brightnColor
        {surf with colorCorrection = gammaColor}

    let generateSnapshotSCParas (surfacesModel    : SurfaceModel) 
                                (objectPlacements : HashMap<string, ObjectPlacementApp>) =
        let surfacesModel = clearSnapshotGroup surfacesModel
        let surfacesWithSCPlacement =
            surfacesModel.surfaces.flat 
                |> HashMap.map (fun key s -> Leaf.toSurface s)
                |> HashMap.map (fun key s -> s, HashMap.tryFind s.name objectPlacements)
        let paras = 
            surfacesWithSCPlacement
                |> HashMap.filter (fun guid (s,p) ->  p.IsSome)
                |> HashMap.map (fun guid (s,p) -> s, p.Value)
                |> HashMap.map (fun guid (s,p) ->  ObjectPlacementApp.toObjectPlacementParameters  
                                                    p s.name s.colorCorrection)
                |> HashMap.values |> Seq.toList
        paras

    let placeSc (surf : Surface) (filename : string) frustum  
                (refSystem   : ReferenceSystem)
                (view : CameraView) (navModel : NavigationModel) 
                (surfacesModel : SurfaceModel)
                (placementParameters : ObjectPlacementParameters) =
        let hasName surf = 
            String.contains placementParameters.name surf.importPath
              || String.contains surf.importPath placementParameters.name

        let place originalSgs = 
            let (sObjsSgs, sObjsSurfs) = getSurfacesInSnapshotGroup surfacesModel
            let transformableSurfs = sObjsSurfs |> List.filter hasName
            // get halton random points on surface (points for debugging)
            let surf = surf |> updateColorCorrection placementParameters
            let pnts, trafos = 
                HaltonPlacement.getHaltonRandomTrafos Interactions.PickSurface surfacesModel
                                                      refSystem placementParameters frustum view
            let transformSurfaces toTransform trafos =
                //let oldTrafos = toTransform |> List.map (fun s -> s.preTransform)
                let zipped = List.zip trafos toTransform    
                let update (t,s) =
                    let s = s |> updateColorCorrection placementParameters
                    {s with preTransform = t; isVisible = true;}

                let updatedSurfaces =
                    zipped |> List.map update
                updatedSurfaces
            let (newSurfaces, toDelete) = 
                match transformableSurfs.Length with
                | nr when nr = 0 ->      
                    let newSurfaces =
                        [
                          for t in [|0..trafos.Length-1|] do
                              yield {surf with guid = Guid.NewGuid(); preTransform = trafos.[t]; isVisible = true} 
                        ] 
                    newSurfaces, List.empty    
                | nr when nr = trafos.Length  ->
                    (transformSurfaces transformableSurfs trafos, List.empty)
                | nr when nr > trafos.Length ->
                    let (transformThese, deleteThese) = List.splitAt trafos.Length transformableSurfs
                    (transformSurfaces transformThese trafos, deleteThese)
                | nr when nr < trafos.Length ->
                    let transformTrafos, newTrafos = List.splitAt transformableSurfs.Length trafos
                    let transformedSurfs = transformSurfaces transformableSurfs transformTrafos
                    let newSurfs =
                        seq {
                            for trafo in newTrafos do
                                yield {surf with guid = Guid.NewGuid (); preTransform = trafo; isVisible = true}
                        } |> List.ofSeq
                    transformedSurfs@newSurfs, List.empty
                | _ -> List.empty, List.empty
            let keepGuids = newSurfaces |> List.map (fun s -> s.guid)
            let deleteGuids = toDelete |> List.map (fun s -> s.guid)
            let allSurfaces =
                sObjsSurfs 
                    |> List.filter (fun s -> not (List.contains s.guid keepGuids)) // delete surfaces that need to be updated
                    |> List.filter (fun s -> not (List.contains s.guid deleteGuids))
                    |> List.append newSurfaces // add new or updated Surfaces
            let allObjSgSurfaces = 
                sObjsSgs
                    |> List.filter (fun (g, s) -> not (List.contains g keepGuids)) // delete sgsurfaces that need to be updated
                    |> List.filter (fun (g, s) -> not (List.contains g deleteGuids))
            let surfs, sgSurfs = 
                HaltonPlacement.getSgSurfacesWithBBIntersection ((allSurfaces)|> IndexList.ofList) 
                                                                allObjSgSurfaces 
                                                                originalSgs
            surfs, sgSurfs, deleteGuids

        match hasName surf with
        | true -> 
            let sgGrouped = surfacesModel.sgGrouped
            let sgSurface =
                    sgGrouped 
                        |> IndexList.map(fun x -> x.TryFind surf.guid)
                        |> IndexList.tryFirst
                        |> Option.flatten
    
            let surfacesModel = 
                match sgSurface with
                | Some originalSgs -> 
                    let snapshotSurfs, objSgSurfs, deleteGuids = place originalSgs
                    // points for debugging
                    //let m = { m with drawing = {m.drawing with haltonPoints = []}}

                    //distance.xml
                    writeDistancesToFile snapshotSurfs filename (Path.GetFileNameWithoutExtension placementParameters.name) navModel |> ignore 
                    let newLeaves = snapshotSurfs |> IndexList.ofList |> IndexList.map Leaf.Surfaces
                    let surfacesModel = 
                        let groups = 
                            surfacesModel.surfaces 
                                |> GroupsApp.removeLeavesFromGroup "snapshots" deleteGuids
                        let groups = 
                            groups
                                |> GroupsApp.removeLeavesFromGroup "snapshots" (newLeaves.AsList |> List.map (fun x -> x.id))
                        let groups =
                            groups
                                |> GroupsApp.addLeavesToSnapshots newLeaves  
                        {surfacesModel with surfaces = groups}
                    let snapshotObjGuids = snapshotSurfs |> List.map (fun s -> s.guid)
                    let filteredOldSgSurfs =
                        surfacesModel.sgSurfaces
                            |> HashMap.filter (fun g s -> not (List.contains g deleteGuids))
                            |> HashMap.filter (fun g s -> not (List.contains g snapshotObjGuids))
                    //handle sg surfaces
                    let allSgSurfs = 
                        objSgSurfs
                            |> HashMap.ofList
                            |> HashMap.union filteredOldSgSurfs
                    let surfacesModel = {surfacesModel with sgSurfaces = allSgSurfs}
                                                   
                    surfacesModel 
                        |> SurfaceModel.triggerSgGrouping 
                | None -> surfacesModel
            surfacesModel
        | false -> surfacesModel

    /// applys function f for each item in lst, using the resulting Model for the next step
    let rec applyToModel (lst : List<'a>) (m : 'm) (f : 'm -> 'a -> 'm) =
        match lst with
        | [] -> m
        | head::tail -> 
            let m = f m head
            applyToModel tail m f
    
    let placeObjs (frustum      : Frustum) 
                  (filename     : string) 
                  (refSystem    : ReferenceSystem)
                  (navModel     : NavigationModel) 
                  (shattercones : list<ObjectPlacementParameters>) 
                  (m            : SurfaceModel)
                  (surface      : Surface) =
        let placeSc = placeSc surface filename frustum refSystem navModel.camera.view navModel
        let m = applyToModel shattercones m placeSc
        m

    let placeMultipleOBJs (m            : SurfaceModel) 
                          (placementParameters : list<ObjectPlacementParameters>) 
                          (frustum      : Frustum) 
                          (filename     : string) 
                          (refSystem    : ReferenceSystem)
                          (navModel     : NavigationModel) =
        let snapSgs, snapSurfs = getSurfacesInSnapshotGroup m
        let snapshotObjGuids = snapSgs |> List.map fst
        let surfaces = m.surfaces.flat 
                            |> HashMap.toList
                            |> List.map(fun (_,v) -> v |> Leaf.toSurface)
                            |> List.filter (fun s -> s.surfaceType = SurfaceType.SurfaceOBJ)
                            |> List.filter (fun s -> not (List.contains s.guid snapshotObjGuids))
        let placeObjs = placeObjs frustum filename  refSystem navModel placementParameters
        let m = applyToModel surfaces m placeObjs
        m

    let interpolateView (fromView : CameraView) (toView : CameraView) (steps : int) =
        let locFrom = fromView.Location
        let upFrom  = fromView.Up
        let forwardFrom = fromView.Forward
        let locTo = toView.Location
        let upTo  = toView.Up
        let forwardTo = toView.Forward
        let deltaLoc = locTo - locFrom
        let deltaUp = upTo - upFrom
        let deltaFw = forwardTo - forwardFrom
        let stepLoc = (deltaLoc / float steps)
        let stepUp  = (deltaUp / float steps)
        let stepFw  = (deltaFw / float steps)
        let interp = 
            seq {
                for i in [0 .. steps] do
                  let loc = locFrom + (float i) * stepLoc
                  let up  = upFrom  + (float i) * stepUp
                  let fw  = forwardFrom + (float i) * stepFw
                  yield (toView |> CameraView.withLocation loc
                                |> CameraView.withUp up
                                |> CameraView.withForward fw
                        )  
            }
        interp