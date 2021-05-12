namespace PRo3D

open System
open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.UI
open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Navigation2
open PRo3D.Bookmarkings
open PRo3D.Core.Surface
open PRo3D.Viewer

open PRo3D.SimulatedViews
module ViewerSnapshotUtils =



    let placeAllObjs (m : Model) (snapshotPlacements) filename =
        let surfacesModel = SnapshotUtils.addSnapshotGroup m.scene.surfacesModel
        let m = Model.withScene {m.scene with surfacesModel = surfacesModel} m
        let snapSgs, snapSurfs = 
            SnapshotUtils.getSurfacesInSnapshotGroup surfacesModel
        let snapshotObjGuids = snapSgs |> List.map fst
        let surfaces = m.scene.surfacesModel.surfaces.flat 
                            |> HashMap.toList
                            |> List.map(fun (_,v) -> v |> Leaf.toSurface)
                            |> List.filter (fun s -> s.surfaceType = SurfaceType.SurfaceOBJ)
                            |> List.filter (fun s -> not (List.contains s.guid snapshotObjGuids))
        let placeObjs (m : Model) surf = 
            let surfaceModel = 
                SnapshotUtils.placeMultipleOBJs m.scene.surfacesModel
                                                   snapshotPlacements 
                                                   m.frustum
                                                   filename
                                                   m.scene.referenceSystem
                                                   m.navigation
            Model.withScene {m.scene with surfacesModel = surfaceModel} m
        let m = SnapshotUtils.applyToModel surfaces m placeObjs
        m

    let updateObjPlacementsFromGui (m : Model) =
        let snapshotSCParameters = 
          SnapshotUtils.generatePlacementParameters m.scene.surfacesModel
                                                    m.scene.objectPlacements
        placeAllObjs m snapshotSCParameters ""