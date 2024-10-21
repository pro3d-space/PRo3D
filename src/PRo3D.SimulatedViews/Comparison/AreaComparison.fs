﻿namespace PRo3D.Comparison

open System
open Aardvark.UI.Operators
open Adaptify.FSharp.Core
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open MBrace.FsPickler
open Aardvark.Geometry
open OpcViewer.Base
open FSharp.Data.Adaptive
open OpcViewer.Base.Picking
open Aardvark.SceneGraph
open Aardvark.Data.Opc

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Comparison.ComparisonUtils
open Aardvark.Geometry
open OpcViewer.Base.KdTrees

//open System.Collections.Generic

module AreaComparison =

    let getPositions (level0Tree : Level0KdTree) areaBox (surfaceTrafo : Trafo3d) =
        let rec toPositionsList (objectSet : IIntersectableObjectSet) = 
            match objectSet with
            | :? Aardvark.Geometry.TriangleSet -> 
                let triangleSet = (objectSet :?> TriangleSet) 
                let lst : List<V3d> = List.ofSeq triangleSet.Position3dList
                lst |> Some
            | :? Aardvark.Geometry.KdTreeSet ->
                let kdTreeSet = (objectSet :?> KdTreeSet)
                let lst : List<ConcreteKdIntersectionTree> = 
                    kdTreeSet.ConcreteKdTreeList 
                        |> List.ofSeq
                let lst =
                  lst |> List.map (fun x -> x.KdIntersectionTree.BoundingBox3d, x)
                let lst = 
                      lst |> List.filter (fun (box, tree) -> 
                                            box.Transformed(surfaceTrafo).Intersects areaBox    
                                          )
                          |> List.map (fun (box, tree) -> tree)
                          
                      
                let lst = lst 
                            |> List.map (fun t -> t.Trafo, toPositionsList t.KdIntersectionTree.ObjectSet)
                            |> List.filter (fun (t, x) -> x.IsSome)
                            |> List.map (fun (t, x) -> x.Value)
                let lst = List.concat (Seq.ofList lst)
                lst |> Some
            | _ ->
                Log.warn "[Comparison] Unknown Object Set Type"
                None

        let positions = 
            match level0Tree with
                | Aardvark.VRVis.Opc.KdTrees.InCoreKdTree kd -> 
                    (kd.kdTree.KdIntersectionTree.ObjectSet |> toPositionsList)
                | Aardvark.VRVis.Opc.KdTrees.LazyKdTree kd ->       
                  match kd.kdTree with
                  | Some tree -> 
                      (tree.KdIntersectionTree.ObjectSet |> toPositionsList)
                  | None -> 
                      let triangles = (DebugKdTreesX.loadTriangles kd)
                      let lst : List<V3d> = List.ofSeq (triangles.Position3dList)
                      Some lst
        positions

    let getSurfaceVerticesIn  (kdTree : HashMap<Box3d,KdTrees.Level0KdTree>)
                              (surfaceTrafo : Trafo3d)
                              (area : AreaSelection) =
        

        let areaBox = Box3d.FromCenterAndSize (area.location, V3d(area.radius))
        //let areaBoxModelCS = areaBox.Transformed(surfaceTrafo.Backward)
        //get bbs that are hit
        let treesIntersectingArea =
             kdTree
             |> HashMap.toList
             |> List.filter(fun ((x : Box3d), tree) -> 
                              x.Transformed(surfaceTrafo).Intersects areaBox    
                           )

        let positions = 
            treesIntersectingArea
              |> List.map (fun (box, tree) -> getPositions tree areaBox surfaceTrafo)
              |> List.filter (fun t -> t.IsSome)
              |> List.map (fun t -> t.Value)

        let dist = area.radius

        let findVerticesInSphere (positions : list<V3d>) =
            let positions = positions |> List.filter (fun p -> area.location.Distance p < dist)
            positions

        //let findVerticesInBox (triangles : TriangleSet) =
        //    let lst : List<V3d> = List.ofSeq triangles.Position3dList
        //    let lst = lst |> List.filter (fun p -> areaBoxModelCS.Contains p)
        //    lst
              
        let vertices = positions
                          |> List.map findVerticesInSphere

        let vertices = 
            match vertices with
            | [] -> []
            | _ ->
                vertices
                   |> List.reduce List.append 
                    
        vertices

    let autoRotate (area : AreaSelection) =
        let plane = PlaneFitting.planeFit(area.verticesSurf1 |> IndexList.toList)       
        //let box = Box3d.FromCenterAndSize (area.location, area.dimensions)
        //let normal = plane.Normal
        //let foo = plane.
        //{area with rotation = rotation}
        //box.Transformed rotation
        area

    let calculateVertices (surface : Surface) (sgSurface : SgSurface)
                          (referenceSystem : ReferenceSystem) (area : AreaSelection) =
        let picking = sgSurface.picking
        let kdTrees =
            match picking with
            | NoPicking -> None
            | PickMesh mesh -> None
            | KdTree   tree -> 
                if tree.IsEmpty then None 
                else Some tree
        let trafo = SurfaceTransformations.fullTrafo' surface None None referenceSystem
        let vertices = kdTrees |> Option.map (fun trees -> getSurfaceVerticesIn trees trafo area)
        let vertices =
            match vertices with
            | Some v -> v |> IndexList.ofList
            | None -> IndexList.empty
        vertices
        
    module Instancing = 
      //let cylinder = IndexedGeometryPrimitives.Cylinder.solidCylinder V3d.OOO V3d.OOI 1.0 1.0 1.0 16 C4b.White
      let sphere = Sphere3d.FromCenterAndRadius (V3d(0.0), 1.0)
      let indexedSphere = IndexedGeometryPrimitives.Sphere.solidSubdivisionSphere sphere 6 C4b.White
      let normals = indexedSphere.IndexedAttributes.Item(Aardvark.Rendering.DefaultSemantic.Normals) |> AVal.constant

      let distToColor (dist : float) (maxDist : float) =
        let r = clamp 0.0 1.0 (dist / maxDist)
        C4b(r,1.0 - r,0.1 , 1.0)

      let statisticsToGeometry (stats : AdaptiveVertexStatistics) (radius : float) =
        let scaleTrafo = Trafo3d.Scale radius

        let colors = 
            adaptive {
                let! distances = stats.distances 
                let! maxDistance = stats.maxDistance
                return (distances |> List.map (fun x -> distToColor x maxDistance))
            }

        let lines =
              AVal.map2 (fun p1 p2 -> (Array.zip p1 p2) 
                                        |> Array.map (fun (a,b) -> Line3d(a,b))) 
                        stats.diffPoints1 stats.diffPoints2

        let colors = 
           colors |> AVal.map (fun colors -> colors@colors |> Array.ofSeq :> System.Array)

        let multTrafos (translation : V3d) (trafo : Trafo3d) =
            trafo * (Trafo3d.Translation translation)
       
        let trafos1 = 
          AVal.map2 (fun p (t : Trafo3d) -> p |> Array.map (fun (p : V3d) -> multTrafos p t))
                    stats.diffPoints1 stats.trafo1

        let trafos2 = 
          AVal.map2 (fun p (t : Trafo3d) -> p |> Array.map (fun (p : V3d) -> multTrafos p t))
                    stats.diffPoints2 stats.trafo2

        let trafos = 
          AVal.map2 (fun t1 t2 -> Array.append t1 t2
                                      :> System.Array) trafos1 trafos2
            //AVal.map2 (fun p1 p2 -> 
            //              ((p1 |> Array.map (fun x -> (Trafo3d.Translation x) *  ))
            //                (p2 |> Array.map (fun x -> scaleTrafo * Trafo3d.Translation x)))
            //                  :> System.Array) stats.diffPoints1 stats.diffPoints2
        let instancedAttributes =
            Map.ofList [
                string Aardvark.Rendering.DefaultSemantic.Colors, (typeof<C4b>, colors ) 
                "ModelTrafo", (typeof<Trafo3d>, trafos) 
            ]
        instancedAttributes, lines



    let createColorLegend (area : AdaptiveAreaSelection) =
        let attributes =
            [        
                "display"               => "block"; 
                "width"                 => "55px"; 
                "height"                => "75%"; 
                "preserveAspectRatio"   => "xMidYMid meet"; 
                "viewBox"               => "0 0 5% 100%" 
                "style"                 => "position:absolute; left: 0%; top: 25%"
                "pointer-events"        => "None"
            ] |> AttributeMap.ofList

        let create (colorLegend : AdaptiveFalseColorsModel) = 
            Incremental.Svg.svg attributes 
                                (PRo3D.FalseColorLegendApp.Draw.createFalseColorLegendBasics 
                                  "ScalarLegend" colorLegend)
        
        let legend = 
            AVal.map (fun stats -> 
                              match stats with
                              | AdaptiveSome (stats : AdaptiveVertexStatistics) -> 
                                  create stats.colorLegend
                              | _ -> 
                                  div [] []
            ) area.statistics
        Incremental.div ([] |> AttributeMap.ofList) (AList.ofAValSingle legend)
        
    let sgDifference (area : AdaptiveAreaSelection) =
        AVal.map3 (fun stats visible radius -> 
                          match stats, visible with
                          | AdaptiveSome stats, true ->
                              let attributes, lines = Instancing.statisticsToGeometry stats (radius * 0.05)
                              Instancing.indexedSphere
                                  |> Sg.ofIndexedGeometry
                                  |> Sg.instanced' attributes
                                  |> Sg.andAlso (Sg.lines (C4b.Grey |> AVal.constant) lines)
                          | _,_ -> Sg.empty
        ) area.statistics area.visible area.radius

    let sgAllDifferences (areas : amap<System.Guid, AdaptiveAreaSelection>) =
        areas |> AMap.toASet
              |> ASet.map (fun (g,x) -> (sgDifference x) |> Sg.dynamic)
              |> Sg.set
              |> Sg.effect [     
                  Aardvark.UI.Trafos.Shader.stableTrafo |> toEffect 
                  DefaultSurfaces.vertexColor |> toEffect
              ] 
              |> Sg.noEvents 
        
    let calculateStatistics (surface1 : Surface) (sgSurface1 : SgSurface) 
                            (surface2 : Surface) (sgSurface2 : SgSurface) 
                            (surfaceModel : SurfaceModel) (geometryType : DistanceMode)
                            (pointSizeFactor : float)
                            (referenceSystem : ReferenceSystem) (area : AreaSelection) =
        let area = 
            {area with verticesSurf1 = calculateVertices surface1 sgSurface1 referenceSystem area}
        let area = 
            {area with verticesSurf2 = calculateVertices surface2 sgSurface2 referenceSystem area}
        
        match area.verticesSurf1.IsEmptyOrNull (), area.verticesSurf2.IsEmptyOrNull () with
        | false, false ->
            let vertices1 = area.verticesSurf1 |> IndexList.toList
            let vertices2 = area.verticesSurf2 |> IndexList.toList

            let biggerList, smallerList = 
                if vertices1.Length > vertices2.Length then 
                  vertices1, vertices2
                else 
                  vertices2, vertices1



            let surfaceFilter1 (id : Guid) (l : Leaf) (s : SgSurface) = 
                id = surface1.guid

            let surfaceFilter2 (id : Guid) (l : Leaf) (s : SgSurface) = 
                id = surface2.guid

            let mutable points = []

            let sendRay ray surfFilter =
                let hitInfo, c = SurfaceIntersection.doKdTreeIntersection surfaceModel 
                                                                           referenceSystem 
                                                                           (constF None)
                                                                           None
                                                                           (FastRay3d(ray)) 
                                                                           surfFilter 
                                                                           cache
                cache <- c
                hitInfo

            let calcDistanceRound (localPoint : V3d) =
                let raysFrom : V3d = sgSurface1.globalBB.Center
                let direction : V3d = (localPoint - raysFrom) //((localPoint - fromPoint).Normalized)
                let direction = direction.Normalized
                let ray = new Ray3d (raysFrom, direction)
                let hitInfo1 = sendRay ray surfaceFilter1
                let hitInfo2 = sendRay ray surfaceFilter2
                
                match hitInfo1, hitInfo2 with
                | Some (t1,surf1), Some (t2,surf2) ->       
                    let hit1 = ray.GetPointOnRay(t1)                         
                    let hit2 = ray.GetPointOnRay(t2)     
                    let dist = hit1.Distance hit2 
                    points <- points@[hit1, hit2]
                    Some (dist)
                |  _, _ ->
                    Log.line "[RayCastSurface] no hit in direction %s" (direction.ToString ())
                    None

            let calcDistanceFlat (localPoint : V3d) =
                let fittedPlane = PlaneFitting.planeFit smallerList
                let direction = fittedPlane.Normal
                let fromPoint = localPoint + direction
                let rayPosDir = new Ray3d (fromPoint, direction)
                let hitInfo1Pos = sendRay rayPosDir surfaceFilter1
                let hitInfo2Pos = sendRay rayPosDir surfaceFilter2
                let rayNegDir = new Ray3d (fromPoint, -direction)
                let hitInfo1Neg = sendRay rayNegDir surfaceFilter1
                let hitInfo2Neg = sendRay rayNegDir surfaceFilter2

                let chooseHit hitInfoPos hitInfoNeg =
               // let hit1, ray1 =
                    match hitInfoPos, hitInfoNeg with
                    | Some (v1, surf1), Some (v2, surf2) -> 
                        if localPoint.Distance(rayPosDir.GetPointOnRay(v1))
                            < localPoint.Distance(rayNegDir.GetPointOnRay(v2))
                        then hitInfoPos, rayPosDir
                        else hitInfoNeg, rayNegDir
                    | Some h1, None -> hitInfoPos, rayPosDir
                    | None, Some h2 -> hitInfoNeg, rayNegDir
                    | None, None -> None, rayPosDir

                let hit1, ray1 = chooseHit hitInfo1Pos hitInfo1Neg

                let hit2, ray2 = chooseHit hitInfo2Pos hitInfo2Neg
                    //if hitInfo2Pos.IsSome then hitInfo2Pos, rayPosDir else
                    //  if hitInfo2Neg.IsSome then hitInfo2Neg, rayNegDir else None, rayPosDir

    
                match hit1, hit2 with
                | Some (t1,surf1), Some (t2,surf2) ->       
                    let hit1 = ray1.GetPointOnRay(t1)                         
                    let hit2 = ray2.GetPointOnRay(t2)     
                    let dist = hit1.Distance hit2 
                    points <- points@[hit1, hit2]
                    Some (dist)
                |  _, _ ->
                    Log.line "[RayCastSurface] no hit in direction %s" (direction.ToString ())
                    None

            let calcDistance localPoint = 
                match geometryType with
                | DistanceMode.Spherical -> 
                    calcDistanceRound localPoint
                | _ ->
                    calcDistanceFlat localPoint

            let indexedDistances =
                match area.highResolution with
                | false ->
                    smallerList 
                      |> List.map calcDistance
                      |> List.zip [0..(smallerList.Length - 1)]
                      |> List.filter (fun (i, x) -> x.IsSome)
                      |> List.map (fun (i,x) -> (i, x.Value))
                | true -> 
                    biggerList 
                      |> List.map calcDistance
                      |> List.zip [0..(biggerList.Length - 1)]
                      |> List.filter (fun (i, x) -> x.IsSome)
                      |> List.map (fun (i,x) -> (i, x.Value))

            //let distances =
            //    indexedDistances |> List.map snd

            if indexedDistances.IsEmptyOrNull () then 
                Log.warn "[Comparison] Could not calculate any distances."
                area
            else

                let maxDistance = indexedDistances |> List.maxBy (fun (i,x) -> x) |> snd
                let minDistance = indexedDistances |> List.minBy (fun (i,x) -> x) |> snd
                let avgDistance = indexedDistances |> List.averageBy (fun (i,x) -> x)

                //To deal with holes in meshes we remove outliers
                // it would be better to let the user choose whether to apply this, because it could remove relevant data
                let thresh = 8.0 * avgDistance
                let noOutliersDistances =
                    indexedDistances |> List.filter (fun (i, d) -> d < thresh)
                let noOutliersPoints =
                    points |> List.zip (indexedDistances |> List.map snd)
                           |> List.filter (fun (d, _) -> d < thresh)
                           |> List.map snd

                let maxDistance = noOutliersDistances |> List.maxBy (fun (i,x) -> x) |> snd
                let minDistance = noOutliersDistances |> List.minBy (fun (i,x) -> x) |> snd
                let avgDistance = noOutliersDistances |> List.averageBy (fun (i,x) -> x)

                let floatRange = (maxDistance - minDistance)
                let legendRange = Range1d.FromMinAndSize (minDistance, floatRange)
                let fromColor = C4b(0.0, 1.0, 0.1)
                let toColor   = C4b(1.0, 0.0, 0.1)
                let legend = {FalseColorsModel.initDefinedScalarsLegend 
                                legendRange with useFalseColors = true}
                let legend = {legend with interval = {legend.interval with value = (floatRange * 0.1)}
                                          lowerColor      = {legend.lowerColor with c = fromColor}
                                          upperColor      = {legend.upperColor with c = toColor}}
                let diffPoints1 = 
                    if vertices1.Length > vertices2.Length then 
                        noOutliersPoints |> List.map fst else noOutliersPoints |> List.map snd 
                let diffPoints2 = 
                    if vertices1.Length > vertices2.Length then 
                        noOutliersPoints |> List.map snd else noOutliersPoints |> List.map fst 
                                        
                let trafo1 = SurfaceTransformations.fullTrafo' surface1 None None referenceSystem
                let trafo2 = SurfaceTransformations.fullTrafo' surface2 None None referenceSystem
                let scaleTrafo = Trafo3d.Scale (area.radius * pointSizeFactor)
                let trafo1 = scaleTrafo //* (surface1.preTransform * trafo1)
                let trafo2 = scaleTrafo //* (surface2.preTransform * trafo2)


                let statistics =
                    match noOutliersDistances with
                    | [] -> None
                    | _ -> 
                        {
                            avgDistance = avgDistance
                            maxDistance = maxDistance
                            minDistance = minDistance
                            distances   = noOutliersDistances |> List.map snd
                            diffPoints1 = diffPoints1 |> Array.ofList
                            diffPoints2 = diffPoints2 |> Array.ofList
                            trafo1      = trafo1
                            trafo2      = trafo2
                            colorLegend = legend
                        } |> Some

                {area with statistics           = statistics}
        | _, _ -> 
            Log.warn "One surface has no vertices is the selected area."
            area
