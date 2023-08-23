namespace PRo3D.Base

open System
open System.IO
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph.Opc
open Aardvark.GeoSpatial.Opc

open Aardvark.SceneGraph
open FSharp.Data.Adaptive 
open PRo3D.Base.Annotation

open System.Collections.Generic

open Aardvark.Geometry

module QTree =

    open Aardvark.SceneGraph.Opc

    let rec foldCulled (consider : Box3d -> bool) (f : Patch -> 's -> 's) (seed : 's) (tree : QTree<Patch>) =
        match tree with
        | Node(p, children) -> 
            if consider p.info.GlobalBoundingBox then
                Seq.fold (foldCulled consider f) seed children
            else
                seed
        | Leaf(p) -> 
            if consider p.info.GlobalBoundingBox then
                f p seed
            else
                seed

        
type QueryAttribute = { channels : int; array : System.Array }
type QueryResult = 
    {
        attributes        : Map<string, QueryAttribute>
        globalPositions   : IReadOnlyList<V3d>
        localPositions    : IReadOnlyList<V3f>
        patchFileInfoPath : string
        indices           : IReadOnlyList<int>
    }

module AnnotationQuery =


    let pick (hierarchies : list<PatchHierarchy * FileName>) (requestedAttributes : list<string>) 
             (hit : List<QueryResult> -> unit) (a : Annotation) =

        let corners = a.points |> IndexList.toSeq
        let segmentPoints = a.segments |> Seq.collect (fun s -> s.points)
        let points = Seq.concat [corners; segmentPoints]

        let lineRegression = 
            let regression = new LinearRegression3d(points)
            regression.TryGetRegressionInfo()
        let plane = 
            match lineRegression with
            | None -> 
                Log.warn "line regression failed"
                CSharpUtils.PlaneFitting.Fit(points |> Seq.toArray)
            | Some p -> p.Plane

        let projectedPolygon = 
            points 
            |> Seq.map (fun p -> plane.ProjectToPlaneSpace p)
            |> Polygon2d

        let intersectsQuery (globalBoundingBox : Box3d) =
            let p2w = plane.GetPlaneToWorld()
            let pointsInWorld = projectedPolygon.Points |> Seq.map (fun p -> p2w.TransformPos(V3d(p,0.0)))
            pointsInWorld |> Seq.exists (fun p -> globalBoundingBox.Contains p)
                

        let globalCoordWithinQuery (p : V3d) =
            let projected = plane.ProjectToPlaneSpace(p) 
            let h = plane.Height(p)
            projectedPolygon.Contains(projected) && h >= -0.2 && h < 0.2

        let handlePatch (paths : OpcPaths) (requestedAttributes : list<string>) (p : Patch) (o : List<QueryResult>) =
            let ig, _ = Patch.load paths ViewerModality.XYZ p.info
            let pfi = paths.Patches_DirAbsPath +/ p.info.Name
            let attributes = 
                let available = Set.ofList p.info.Attributes
                let attributes = 
                    requestedAttributes |> List.choose (fun l -> 
                        if Set.contains l available then
                            let path = paths.Patches_DirAbsPath +/ p.info.Name +/ l
                            if File.Exists path then
                                Some (l, path)
                            else
                                None
                        else 
                            Log.warn $"[Queries] requested attribute {l} but patch {p.info.Name} does not provide it." 
                            Log.line "[Queries] available attributes: %s" (available |> Set.toSeq |> String.concat ",")
                            None
                    )

                attributes
                |> List.map (fun (attributeName, filePath) -> 
                    let arr = filePath |> fromFile<float> // change this to allow different attribute types
                    attributeName, arr
                )
                |> Map.ofList

            //let positions = paths.Patches_DirAbsPath +/ p.info.Name +/ p.info.Positions |> fromFile<V3f>
            let positions = 
                match ig.IndexedAttributes[DefaultSemantic.Positions] with
                | (:? array<V3f> as v) when not (isNull v) -> v
                | _ -> failwith "[Queries] Patch has no V3f[] positions"


            let idxArray = 
                if ig.IsIndexed then
                    match ig.IndexArray with
                    | :? array<int> as idx -> idx
                    | _ -> failwith "[Queries] Patch index geometry has no int[] index"
                else
                    failwith "[Queries] Patch index geometry is not indexed."


            let attributesInputOutput =
                attributes 
                |> Map.map (fun name inputArray -> 
                    inputArray, List<float>()
                )
            let globalOutputPositions = List<V3d>()
            let localOutputPositions = List<V3f>()
            let indices = List<int>()
                

            for startIndex in 0 .. 3 ..  idxArray.Length - 3 do
                let tri = [| idxArray[startIndex]; idxArray[startIndex + 1]; idxArray[startIndex + 2] |] 
                let localVertices = tri |> Array.map (fun idx -> positions[idx])
                if localVertices |> Array.exists (fun v -> v.IsNaN) then
                    ()
                else
                    let globalVertices = 
                        localVertices |> Array.map (fun local -> 
                            let v = V3d local
                            p.info.Local2Global.TransformPos v
                        )
                    let validInside (v : V3d) = globalCoordWithinQuery v
                    let triWithinQuery = globalVertices |> Array.forall validInside
                    if triWithinQuery then
                        indices.Add(localOutputPositions.Count)
                        indices.Add(localOutputPositions.Count + 1)
                        indices.Add(localOutputPositions.Count + 2)
                        attributesInputOutput |> Map.iter (fun name (inputArray, output) -> 
                            tri |> Array.iter (fun idx -> 
                                output.Add(inputArray[idx])
                            )
                        )
                        globalOutputPositions.AddRange(globalVertices)
                        localOutputPositions.AddRange(localVertices)

            o.Add {
                attributes = attributesInputOutput |> Map.map (fun p (i,o) -> { channels = 1; array = o.ToArray() :> System.Array})
                globalPositions = globalOutputPositions :> IReadOnlyList<V3d>
                patchFileInfoPath = pfi
                localPositions = localOutputPositions :> IReadOnlyList<V3f>
                indices = indices :> IReadOnlyList<int>
            }
            o


        let hits = 
            let points = List<QueryResult>()
            hierarchies |> List.fold (fun points (h, basePath) -> 
                let paths = OpcPaths basePath
                let result = QTree.foldCulled intersectsQuery (handlePatch paths requestedAttributes) points h.tree
                points
            ) points
             
        hit hits
