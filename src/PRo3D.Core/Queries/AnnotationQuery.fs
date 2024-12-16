namespace PRo3D.Base

open System
open System.IO
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Data.Opc
open Aardvark.GeoSpatial.Opc

open Aardvark.SceneGraph
open FSharp.Data.Adaptive 
open PRo3D.Base.Annotation

open System.Collections.Generic

open Aardvark.Geometry
open Aardvark.Data.Opc.Aara

module QTree =

    open Aardvark.Data.Opc

    let rec foldCulled 
        (consider : Box3d -> bool) 
        (f        : Patch -> 's -> 's) 
        (seed     : 's) 
        (tree     : QTree<Patch>) =
        match tree with
        | QTree.Node(p, children) -> 
            if consider p.info.GlobalBoundingBox then
                Seq.fold (foldCulled consider f) seed children
            else
                seed
        | QTree.Leaf(p) -> 
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

type QueryFunctions =
    {
        boxIntersectsQuery          : Box3d -> bool
        globalWorldPointWithinQuery : V3d -> bool
    }

module AnnotationQuery =

    let queryFunctionsFromPointsOnPlane (heightRange : Range1d) (points : seq<V3d>) : QueryFunctions =

        let pointsOnPlaneBBEnlarged = lazy (Box3d points).EnlargedByRelativeEps(0.1)

        let linearRegression = 
            let regression = new LinearRegression3d(points)
            regression.TryGetRegressionInfo()

        let plane = 
            match linearRegression with
            | Some p -> Plane3d(p.Normal.Normalized, p.Center)
            | None -> 
                Log.warn "[Queries] line regression failed, using fallback"
                CSharpUtils.PlaneFitting.Fit(points |> Seq.toArray)

        let projectedPolygon = 
            points 
            |> Seq.map (fun p -> plane.ProjectToPlaneSpace p)
            |> Polygon2d

        let projectedPolygon = projectedPolygon.ComputeConvexHullIndexPolygon().ToPolygon2d()
                     
        let intersectsQuery (globalBoundingBox : Box3d) =      
            //let p2w = plane.GetPlaneToWorld()
            //let pointsInWorld = projectedPolygon.Points |> Seq.map (fun p -> p2w.TransformPos(V3d(p,0.0)))
            //pointsInWorld |> Seq.exists (fun p -> globalBoundingBox.Contains p)
            globalBoundingBox.Intersects(pointsOnPlaneBBEnlarged.Value)
                
        let globalCoordWithinQuery (p : V3d) =
            let projected = plane.ProjectToPlaneSpace(p) 
            let h = plane.Height(p)
            projectedPolygon.Contains(projected) && heightRange.Contains(h)

        {
            boxIntersectsQuery = intersectsQuery
            globalWorldPointWithinQuery = globalCoordWithinQuery
        }

    let queryFunctionsFromAnnotation 
        (heightRange : Range1d)
        (annotation : Annotation)
        : QueryFunctions =
        
        let corners = annotation.points |> IndexList.toSeq
        let segmentPoints = annotation.segments |> Seq.collect (fun s -> s.points)
        let points = Seq.concat [corners; segmentPoints]
        queryFunctionsFromPointsOnPlane heightRange points

//    let loadAttibuteLayer =

    let handlePatch 
        (q                   : QueryFunctions) 
        (paths               : OpcPaths) 
        (attributeLayerNames : list<string>) 
        (p                   : Patch) 
        (o                   : List<QueryResult>) 
        : List<QueryResult> =

        let ig, _ = Patch.load paths ViewerModality.XYZ p.info
        let pfi = paths.Patches_DirAbsPath +/ p.info.Name
        let attributes = 
            let available = Set.ofList p.info.Attributes
            let attributes = 
                attributeLayerNames 
                |> List.choose (fun layerName -> 
                    if Set.contains layerName available then
                        let path = paths.Patches_DirAbsPath +/ p.info.Name +/ layerName
                        if File.Exists path then
                            Some (layerName, path)
                        else
                            None
                    else 
                        Log.warn $"[Queries] requested attribute {layerName} but patch {p.info.Name} does not provide it." 
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
                let validInside (v : V3d) = q.globalWorldPointWithinQuery v
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
            attributes          = attributesInputOutput |> Map.map (fun p (i,o) -> { channels = 1; array = o.ToArray() :> System.Array})
            globalPositions     = globalOutputPositions :> IReadOnlyList<V3d>
            patchFileInfoPath   = pfi
            localPositions      = localOutputPositions :> IReadOnlyList<V3f>
            indices             = indices :> IReadOnlyList<int>
        }
        o

    let clip 
        (hierarchies         : list<PatchHierarchy * FileName>) 
        (requestedAttributes : list<string>) 
        (q                   : QueryFunctions) 
        (handlePatch         : OpcPaths -> list<string> -> Patch -> List<QueryResult> -> List<QueryResult>) 
        (hit                 : List<QueryResult> -> unit) =

        let queryResults =      
            hierarchies 
            |> List.fold (fun points (h, basePath) -> 
                let paths = OpcPaths basePath
                
                QTree.foldCulled 
                    q.boxIntersectsQuery 
                    (handlePatch paths requestedAttributes) 
                    points 
                    h.tree //|> ignore                
            ) (List<QueryResult>())


        Log.line("[AnnotationQuery:] found following patches") 
        queryResults |> List.ofSeq |> List.iter(fun x -> Log.line $"{x.patchFileInfoPath |> Path.GetFileName}")
            

        hit queryResults
        queryResults

    type OutputReferenceFrame = 
        | Local     //per patch local positions
        | Centered  //per output centered positions
        | Global    //per output global positions

    type OutputGeometryType = 
        | Mesh
        | PointCloud

    /// Clips the specified geometry to the area defined by the provided annotation polygon.
    /// - `hierarchies`: A list of patch hierarchies and associated file names to be processed within the region of interest.
    /// - `attributeNames`: A list of attribute names relevant to the clipping operation, specifying which attributes to consider.
    /// - `heightRange`: The height range that bounds the vertical extent of the region of interest.
    /// - `hit`: A callback function that processes the list of query results after clipping.
    /// - `annotation`: The clipping polygon that defines the region of interest. ... assumes polygonal annotation
    /// Returns a list of query results representing the geometry clipped to the specified region.
    let clipToRegion    
        (hierarchies    : list<PatchHierarchy * FileName>) 
        (attributeNames : list<string>) 
        (heightRange    : Range1d) 
        (hit            : List<QueryResult> -> unit) 
        (annotation     : Annotation)         
        : List<QueryResult> =

        let q = queryFunctionsFromAnnotation heightRange annotation
        clip hierarchies attributeNames q (handlePatch q) hit

    let queryResultsToObj 
        (outputReferenceFrame : OutputReferenceFrame)
        (outputGeometryType   : OutputGeometryType)
        (queryResults         : List<QueryResult>) =
       
        match outputGeometryType with
        | Mesh ->
            let objGeometries = 
                queryResults 
                |> Seq.map (fun h -> 
                    let colors =
                        match h.attributes |> Map.toSeq |> Seq.tryHead with
                        | None -> None
                        | Some (name, att) -> 
                            match att.array with
                            | :? array<float> as v when v.Length > 0 -> 
                                let min, max = v.Min(), v.Max()
                                if max - min > 0 then
                                    let colors = 
                                        v 
                                        |> Array.map (fun v -> 
                                            if v.IsNaN() then 
                                                C3b.Black 
                                            else 
                                                (v - min) / (max - min) |> TransferFunction.transferPlasma
                                        )
                                    Some colors
                                else 
                                    None
                            | _ -> 
                                None

                    let vertices = 
                        match outputReferenceFrame with
                        | Local -> h.localPositions |> Seq.map V3d  |> Seq.toArray
                        | Global -> h.globalPositions |> Seq.map V3d  |> Seq.toArray
                        | Centered -> 
                            failwith "Centered not implemented yet"

                    let geometry = 
                        {
                            colors = colors
                            indices = h.indices |> Seq.toArray
                            vertices = vertices
                        }
                    geometry
                )

            objGeometries 
            |> WavefrontGeometry.mergeWithDefaultColor C3b.White  
            |> Seq.singleton
            |> RudimentaryObjExport.writeToString 

        | PointCloud ->
            let shiftByCenterOfMass (points: V3d[]) : V3d[] =
                // Calculate the center of mass
                let centerOfMass = 
                    points 
                    |> Array.reduce (+) 
                    |> fun sum -> sum / float points.Length

                // Shift each point by subtracting the center of mass
                points |> Array.map (fun point -> point - centerOfMass)

            let allVertices = 
                queryResults 
                |> Seq.collect (fun h -> h.globalPositions) 
                |> Seq.toArray

            let geometry = 
                {
                    colors = None
                    indices = [| |] // h.indices |> Seq.toArray
                    vertices = allVertices |> shiftByCenterOfMass
                }

            geometry |> Seq.singleton |> RudimentaryObjExport.writeToString        