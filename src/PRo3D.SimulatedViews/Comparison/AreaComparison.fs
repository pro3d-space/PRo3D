namespace PRo3D.Comparison

open System
open Adaptify.FSharp.Core
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.VRVis.Opc.KdTrees
open MBrace.FsPickler
open Aardvark.Geometry
open OpcViewer.Base
open FSharp.Data.Adaptive
open OpcViewer.Base.Picking
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis.Opc

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3DCompability
open PRo3D.Comparison.ComparisonUtils

//open System.Collections.Generic

module AreaComparison =

    let getTriangleSet (level0Tree : Level0KdTree) =
        let toTriangleSet (objectSet : IIntersectableObjectSet) = 
            match objectSet with
            | :? Aardvark.Geometry.TriangleSet -> 
                (objectSet :?> TriangleSet) |> Some
            | _ -> None

        let triangleSet = 
            match level0Tree with
                | InCoreKdTree kd -> 
                    (kd.kdTree.KdIntersectionTree.ObjectSet |> toTriangleSet)
                | LazyKdTree kd ->       
                  match kd.kdTree with
                  | Some tree -> 
                      (tree.KdIntersectionTree.ObjectSet |> toTriangleSet)
                  | None -> 
                      let triangles = (DebugKdTreesX.loadTriangles kd)
                      Some triangles
        triangleSet

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

        let triangles = 
            treesIntersectingArea 
              |> List.map (fun (box, tree) -> getTriangleSet tree)
              |> List.filter (fun t -> t.IsSome)
              |> List.map (fun t -> t.Value)

        let dist = area.radius

        let findVerticesInSphere (triangles : TriangleSet) =
            let lst : List<V3d> = List.ofSeq triangles.Position3dList
            let lst = lst |> List.filter (fun p -> area.location.Distance p < dist)
            lst

        //let findVerticesInBox (triangles : TriangleSet) =
        //    let lst : List<V3d> = List.ofSeq triangles.Position3dList
        //    let lst = lst |> List.filter (fun p -> areaBoxModelCS.Contains p)
        //    lst
              
        let vertices = triangles
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
            | PickMesh mesh -> None
            | KdTree   tree -> 
                if tree.IsEmpty then None 
                else Some tree
        let trafo = SurfaceTransformations.fullTrafo' surface referenceSystem
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
        C4b(r,1.0 - r,0.0 , 1.0)

      let statisticsToGeometry (stats : AdaptiveVertexStatistics) =
        let colors = 
            adaptive {
                let! distances = stats.distances 
                let! maxDistance = stats.maxDistance
                return (distances |> List.map (fun x -> distToColor x maxDistance))
            }

        let lines =
            stats.diffPoints 
              |> AVal.map (fun lst -> lst |> List.map (fun (a,b) -> Line3d(a,b))
                                          |> Array.ofList)

        let colors = 
           colors |> AVal.map (fun colors -> colors@colors |> Array.ofSeq :> System.Array)
        
        let trafos = stats.diffPoints 
                        |> AVal.map (fun points -> 
                                        (points |> List.map fst |> List.map Trafo3d.Translation)
                                        @(points |> List.map snd |> List.map Trafo3d.Translation)
                                          |> Array.ofSeq :> System.Array)
        let instancedAttributes =
            Map.ofList [
                string Aardvark.Rendering.DefaultSemantic.Colors, (typeof<C4b>, colors ) 
                "ModelTrafo", (typeof<Trafo3d>, trafos) 
            ]
        instancedAttributes, lines

    let sgDifference (area : AdaptiveAreaSelection) =
        AVal.map2 (fun stats visible -> 
                          match stats, visible with
                          | AdaptiveSome stats, true ->
                              let attributes, lines = Instancing.statisticsToGeometry stats
                              Instancing.indexedSphere
                                  |> Sg.ofIndexedGeometry
                                  |> Sg.instanced' attributes
                                  |> Sg.andAlso (Sg.lines (C4b.Grey |> AVal.constant) lines)
                          | _,_ -> Sg.empty
        ) area.statistics area.visible

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
                            (surfaceModel : SurfaceModel)
                            (referenceSystem : ReferenceSystem) (area : AreaSelection) =
        let area = 
            {area with verticesSurf1 = calculateVertices surface1 sgSurface1 referenceSystem area}
        let area = 
            {area with verticesSurf2 = calculateVertices surface2 sgSurface2 referenceSystem area}
        
        match area.verticesSurf1.IsEmptyOrNull (), area.verticesSurf2.IsEmptyOrNull () with
        | false, false ->
            let vertices1 = area.verticesSurf1 |> IndexList.toList
            let vertices2 = area.verticesSurf2 |> IndexList.toList
            let s1RayFrom = sgSurface1.globalBB.Center
            let s2RayFrom = sgSurface2.globalBB.Center

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

            let calcDistance (point : V3d) =
                let direction = (point - s1RayFrom).Normalized
                let ray = new Ray3d (s1RayFrom, direction)
                let hitInfo1, c = SurfaceIntersection.doKdTreeIntersection surfaceModel 
                                                                           referenceSystem 
                                                                           (FastRay3d(ray)) 
                                                                           surfaceFilter1 
                                                                           cache
                cache <- c
                

                let hitInfo2, c = SurfaceIntersection.doKdTreeIntersection surfaceModel 
                                                                           referenceSystem 
                                                                           (FastRay3d(ray)) 
                                                                           surfaceFilter2 
                                                                           cache

                cache <- c

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

            let distances =
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

            let distances =
                distances |> List.map snd

            let maxDistance = distances |> List.max

            let statistics =
                match distances with
                | [] -> None
                | _ -> 
                    {
                        avgDistance = distances |> List.average
                        maxDistance = maxDistance
                        minDistance = distances |> List.min
                        distances   = distances
                        diffPoints  = points
                    } |> Some

            {area with statistics           = statistics}
        | _, _ -> 
            Log.warn "One surface has no vertices is the selected area."
            area
