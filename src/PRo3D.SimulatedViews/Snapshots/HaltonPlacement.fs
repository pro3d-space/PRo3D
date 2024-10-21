﻿namespace PRo3D.SimulatedViews

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.UI

open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Base.Gis



module HaltonPlacement =
    let create2DHaltonRandomSeries =
        new HaltonRandomSeries(2, new RandomSystem(System.DateTime.Now.Millisecond))

    let create1DHaltonRandomSeries =
        new HaltonRandomSeries(1, new RandomSystem(System.DateTime.Now.Millisecond))
    
    let genRandomNumbers count =
        let rnd = System.Random()
        List.init count (fun _ -> rnd.Next())
    
    let genRandomNumbersBetween count min max =
        let min, max =
            match max < min with
            | true ->
                Log.warn "[Viewer] min = %i, max = %i" min max
                Log.warn "[Viewer] swapping min and max"
                let tmp = max
                let max = min
                let min = tmp
                (min, max)
            | false ->
                (min, max)
        let rnd = System.Random()
        List.init count (fun _ -> rnd.Next(min, max))
        
    
    let computeSCRayRaster (number : int) (view : CameraView) (frustum : Frustum) (haltonRandom : HaltonRandomSeries) =
        [
        for i in [| 0 .. number-1|] do
            let x = frustum.left   + (haltonRandom.UniformDouble(0) * (frustum.right - frustum.left));
            let y = frustum.bottom + (haltonRandom.UniformDouble(1) * (frustum.top - frustum.bottom));
            
            let centralPointonNearPlane = view.Location + (view.Forward * frustum.near)
            let newPointOnNearPlane = centralPointonNearPlane + (view.Right * x) + (view.Up * y)
            let transformedForwardRay = new Ray3d(view.Location, (newPointOnNearPlane - view.Location).Normalized)
    
            yield transformedForwardRay            
        ]  
        

    let mutable lastHash = -1  
    
    let getSinglePointOnSurface 
        (interaction : Interactions) 
        (surfaces    : SurfaceModel) 
        (refSystem   : ReferenceSystem) 
        (observedSystem : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (cameraLocation : V3d ) 
        (ray : Ray3d) = 

        let mutable cache = HashMap.Empty
        let rayHash = ray.GetHashCode()

        if rayHash = lastHash then
            None
        else    
            let onlyActive (id : Guid) (l : Leaf) (s : SgSurface) = l.active
            let onlyVisible (id : Guid) (l : Leaf) (s : SgSurface) = l.visible

            let surfaceFilter = 
               match interaction with
               | Interactions.PickSurface -> onlyVisible
               | _ -> onlyActive
            Report.Verbosity <- -1 //TODO rno
            Log.startTimed "[RayCastSurface] try intersect kdtree"                                                             
            let hitF (camLocation : V3d) (p : V3d) = 
                let ray =
                    let dir = (p-camLocation).Normalized
                    FastRay3d(camLocation, dir)
                
                match SurfaceIntersection.doKdTreeIntersection surfaces refSystem observedSystem observerSystem ray surfaceFilter cache with
                | Some (t,surf), c ->                             
                    cache <- c; ray.Ray.GetPointOnRay t |> Some
                | None, c ->
                    cache <- c; None
                                  
            let result = 
                match SurfaceIntersection.doKdTreeIntersection surfaces refSystem observedSystem observerSystem (FastRay3d(ray)) surfaceFilter cache with
                | Some (t,surf), c ->                         
                    cache <- c
                    let hit = ray.GetPointOnRay(t)
                   
                    lastHash <- rayHash
                    match hitF cameraLocation hit with
                    | None -> None
                    | Some projectedPoint -> Some projectedPoint
                | None, _ -> 
                    Log.error "[RayCastSurface] no hit"
                    None
            Log.stop()
            Log.line "[HaltonPlacement] done intersecting"
            Report.Verbosity <- 3 //TODO rno
            result 

    let getPointsOnSurfaces 
        (interaction : Interactions) 
        (surfaces    : SurfaceModel)
        (refSystem   : ReferenceSystem) 
        (observedSystem : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (camLocation : V3d )
        (rays        : list<Ray3d>) =

        rays |> List.choose( fun ray -> getSinglePointOnSurface interaction surfaces refSystem  observedSystem observerSystem camLocation ray)
        
    let getHaltonRandomTrafos
        (interaction : Interactions) 
        (surfaces    : SurfaceModel) 
        (refSystem   : ReferenceSystem) 
        (observedSystem : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (parameters : ObjectPlacementParameters) 
        (frustum : Frustum) 
        (view : CameraView) =

        let haltonSeries = create2DHaltonRandomSeries
        let rays = computeSCRayRaster parameters.count view frustum haltonSeries
        let points = getPointsOnSurfaces interaction surfaces refSystem observedSystem observerSystem view.Location rays
        let hsScaling = 
            match parameters.scale with
            | Some s -> 
                let rs = genRandomNumbersBetween parameters.count s.X s.Y
                rs |> List.map(fun x -> (float)x/100.0) 
            | None -> 
                [ for i in 1 .. parameters.count -> 1.0 ]
        let xRotation =
            match parameters.xRotation with
            | Some rx -> genRandomNumbersBetween parameters.count rx.X rx.Y
            | None -> [ for i in 1 .. parameters.count -> 0 ]
        let yRotation = 
            match parameters.yRotation with
            | Some ry -> genRandomNumbersBetween parameters.count ry.X ry.Y
            | None -> [ for i in 1 .. parameters.count -> 0 ]
        let zRotation =
            match parameters.zRotation with
            | Some rz -> genRandomNumbersBetween parameters.count rz.X rz.Y //0 360
            | None -> [ for i in 1 .. parameters.count -> 0 ]
        let trafos =
            [
            for i in 0..points.Length-1 do
                yield Trafo3d.Scale(float hsScaling.[i]) * 
                Trafo3d.RotationZInDegrees(float zRotation.[i]) *
                Trafo3d.RotationYInDegrees(float yRotation.[i]) *
                Trafo3d.RotationXInDegrees(float xRotation.[i]) *
                Trafo3d.Translation(points.[i])
            ]
        points, trafos //points |> List.map( fun p -> Trafo3d.Scale(0.03) * Trafo3d.Translation(p) )

    let viewHaltonSeries (points : aval<list<V3d>>) =
        let points =
            aset{
                let! points = points
                let pnts = 
                    points 
                    |> List.map( fun p -> PRo3D.Base.Sg.dot ~~C4b.Cyan ~~5.0 ~~p)
                    |> Sg.ofList
                yield pnts
                } |> Sg.set
        points

    let getSgSurfacesWithBBIntersection (newSurfaces : IndexList<Surface>) 
                                        (sgSurfaces : list<Guid*SgSurface>) 
                                        (newSg : SgSurface) = 
         let mutable sgsurfs = sgSurfaces
         for i in [|0..newSurfaces.Count-1|] do
             let newSgSurf = {newSg with surface = newSurfaces.[i].guid}
             // put the first sgsurface in the list
             if sgsurfs.IsEmpty then 
                 sgsurfs <- sgsurfs @ [(newSgSurf.surface, newSgSurf)]

             // check for the new Sgsurface if bb intersects with others
             else
                 let obj1 = newSgSurf.globalBB.Transformed(newSurfaces.[i].preTransform) 
                 let addSurf = 
                     [
                     for x in 0..sgsurfs.Length-1 do
                         let surf2 = newSurfaces |> IndexList.toList |> List.find(fun s -> (fst sgsurfs.[x]) = s.guid)
                         let obj2 = (snd sgsurfs.[x]).globalBB.Transformed(surf2.preTransform)
                         yield (obj1).Intersects(obj2)
                         ]
                 if addSurf |> List.contains true then
                    ()
                 else
                     sgsurfs <- sgsurfs @ [(newSgSurf.surface, newSgSurf)]
         // keep only the remaining surfaces
         let sfs = sgsurfs |> List.map(fun sg -> newSurfaces |> IndexList.toList |> List.find(fun s -> (fst sg) = s.guid))
         sfs, sgsurfs
                       
