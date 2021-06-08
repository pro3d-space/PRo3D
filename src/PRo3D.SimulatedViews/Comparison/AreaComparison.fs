namespace PRo3D.Comparison

open System
open Aardvark.Base
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
        
        
    let calculateStatistics (surface1 : Surface) (sgSurface1 : SgSurface) 
                            (surface2 : Surface) (sgSurface2 : SgSurface) 
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

            let findClosest (point : V3d) = 
                let distances = 
                    smallerList |> List.map (fun v -> v.Distance point)
                let index = distances.MinIndex ()
                index, distances.[index]

            let closest =
                biggerList
                  |> List.map (fun v -> findClosest v)

            let closestDistances =
                closest |> List.map snd
            
            let statistics : VertexStatistics =
                {
                    avgDistance = closestDistances |> List.average
                    maxDistance = closestDistances |> List.max
                    minDistance = closestDistances |> List.min
                }

            {area with statistics = Some statistics}
        | _, _ -> 
            Log.warn "One surface has no vertices is the selected area."
            area
