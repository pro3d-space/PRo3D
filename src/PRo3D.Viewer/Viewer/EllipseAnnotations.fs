namespace PRo3D

open System
open FSharp.Data.Adaptive

open Aardvark.Base

open PRo3D.Viewer

open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI

module EllipseAnnotations =
    
    

    type EllipseMessage = 
        | SetPreviewPoint of SurfaceIntersection
        | PickPoint of SurfaceIntersection


    module EllipseComputation =

        let sampleEllipsePoints (center : V2d) (majorAxis : V2d) (minorAxis : V2d) (numSamples: int) =
            let (uX, uY) = majorAxis.X, majorAxis.Y
            let (vX, vY) = minorAxis.X, minorAxis.Y
            let stepSize = 2.0 * Math.PI / float numSamples

            [| for i in 0 .. numSamples - 1 do
                let t = stepSize * float i
                let x = center.X + uX * cos t + vX * sin t
                let y = center.Y + uY * cos t + vY * sin t
                yield V2d (x, y) |]

        let computePlane (sky : V3d) (project : V3d -> V3d) (p0 : V3d) (p1 : V3d) = 
            let p0Proj = project p0
            let p1Proj = project p1

            let bb = Box2d.FromPoints(p0Proj.XY, p1Proj.XY)
            let start = bb.Min
            let support = bb.Min + bb.Size.OY
            let supportAboveSurface = V3d(support.X, support.Y, p0.Z) + sky * 10.0
            let pSupport = project supportAboveSurface
            let p01 = p1Proj - p0Proj |> Vec.Normalized
            let p02 = pSupport - p0Proj |> Vec.Normalized
            let normal = Vec.cross p01 p02 |> Vec.Normalized
            let reconstructedPlane = Plane3d(p0Proj, normal)
            let ellipse = Ellipse2d(bb.Center, bb.SizeX * V2d.IO * 0.5, bb.SizeY * V2d.OI * 0.5)
            let pointsOnProjectedEllipse = sampleEllipsePoints ellipse.Center ellipse.Axis0 ellipse.Axis1 100
            let points = ()
            ()
       

    module App = 

        let computeVisualization (sky : V3d) (projectToSurface : V3d -> V3d) (model : EllipseModel) = 
            match model.currentWorldPos with
            | Some p1 ->   
                let p1 = 
                    match model.secondWorldPick with
                    | Some p -> p
                    | _ -> p1
                let p = Plane3d(sky , model.firstWorldPick.hitPoint)
                let w2Plane = p.GetWorldToPlane()
                let plane2World = p.GetPlaneToWorld()
                let p0Plane = w2Plane.TransformPos(model.firstWorldPick.hitPoint)
                let p1Plane = w2Plane.TransformPos(p1.hitPoint)
                let projected = Box2d.FromPoints(p0Plane.XY, p1Plane.XY)
                let footprint = 
                    [| projected.Min; projected.Min + projected.Size.XO; projected.Max; projected.Min + projected.Size.OY; projected.Min |]
                    |> Array.map (fun p -> plane2World.TransformPos(V3d(p.X, p.Y, 0.0)) |> projectToSurface) 

                let ellipse = Ellipse2d(projected.Center, projected.SizeX * V2d.IO * 0.5, projected.SizeY * V2d.OI * 0.5)
                let ellipsePoints = EllipseComputation.sampleEllipsePoints ellipse.Center ellipse.Axis0 ellipse.Axis1 40
                let ellipseVertices = 
                    ellipsePoints |> Array.map (fun p -> plane2World.TransformPos(V3d(p.X, p.Y, 0.0)))
                let projectedPoints = 
                    match model.secondWorldPick with
                    | None -> None
                    | Some p -> 
                        ellipseVertices |> Array.map projectToSurface |> Some
                { model with 
                    boundaryVertices = Some footprint; 
                    projectionPlane = Some p; 
                    projectedEllipse = 
                        Some { 
                            ellipse = ellipse; 
                            approximatePoints = ellipseVertices
                            surfaceProjectedPoints = projectedPoints 
                        }
                }
            | _ -> 
                { model with boundaryVertices = None }

        let update (sky : V3d) (projectToSurface : V3d -> V3d) (message : EllipseMessage) (model : Option<EllipseModel>) =
            match model, message with
            | None, PickPoint point -> 
                Some (EllipseModel.initial point)
            | Some model, SetPreviewPoint point -> 
                if model.secondWorldPick.IsSome then 
                    Some model
                else
                    { model with currentWorldPos = Some point } 
                    |> computeVisualization sky projectToSurface |> Some
            | Some model, PickPoint point -> 
                match model.secondWorldPick with
                | Some _ -> 
                    None
                | None -> 
                    { model with secondWorldPick = Some point } 
                    |> computeVisualization sky projectToSurface |> Some
            | None, SetPreviewPoint _ -> 
                None

        let getAxisAlignedBounds (model : AdaptiveEllipseModel)= 
            let boundingLines = 
                model.boundaryVertices |> AVal.map (fun arr -> 
                    match arr with
                    | Some arr when arr.Length > 0 -> 
                        let lines = 
                            arr 
                            |> Seq.map (fun p -> p - arr[0])
                            |> Seq.pairwise
                            |> Seq.map Line3d
                            |> Seq.toArray
                        lines, Trafo3d.Translation arr[0]
                    | _ -> 
                        [||], Trafo3d.Identity
                )
            (boundingLines |> AVal.map fst)
            |> Sg.lines (AVal.constant C4b.AliceBlue)
            |> Sg.trafo (boundingLines |> AVal.map snd)
            |> Sg.uniform' "LineWidth" 5.0
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
                do! DefaultSurfaces.thickLine
            }

        let getEllipseVisualization (model : AdaptiveEllipseModel) = 

            let boundingLines = 
                model.projectedEllipse |> AVal.map (fun p -> 
                    match p with
                    | Some p ->
                        let arr = 
                            match p.surfaceProjectedPoints with
                            | Some p -> p
                            | None -> p.approximatePoints
                        if arr.Length > 0 then
                            let lines = 
                                arr 
                                |> Seq.map (fun p -> p - arr[0])
                                |> Seq.pairwise
                                |> Seq.map Line3d
                                |> Seq.toArray
                            lines, Trafo3d.Translation arr[0]
                        else    
                            [||], Trafo3d.Identity
                    | _ -> 
                        [||], Trafo3d.Identity
                )

            let lines = 
                (boundingLines |> AVal.map fst)
                |> Sg.lines (model.secondWorldPick |> AVal.map (function | None -> C4b.DarkViolet | _ -> C4b.BlueViolet))
                |> Sg.trafo (boundingLines |> AVal.map snd)
                |> Sg.uniform' "LineWidth" 2.0
                |> Sg.shader {
                    do! DefaultSurfaces.stableTrafo
                    do! DefaultSurfaces.thickLine
                }

            lines

        let viewDepthTested (model : AdaptiveEllipseModel) =
            let sphereTrafos = 
                model.projectedEllipse 
                |> AVal.map (fun p -> 
                    match p with
                    | Some p -> 
                        match p.surfaceProjectedPoints with
                        | Some p -> p |> Array.map Trafo3d.Translation
                        | None -> [||]
                    | _ -> 
                        [||]
                )

            let sphereSgs = 
                sphereTrafos 
                |> ASet.ofAVal
                |> ASet.map (fun t -> 
                    Sg.sphere' 4 C4b.White 0.2
                    |> Sg.trafo' t
                    |> Sg.shader {
                        do! DefaultSurfaces.stableTrafo
                        do! DefaultSurfaces.stableHeadlight
                    }
                )

            sphereSgs
            |> Sg.set


        let viewOverlayed (model : AdaptiveEllipseModel) =

            let boundary = 
                getAxisAlignedBounds model
                |> Sg.noEvents

            let ellipse = 
                getEllipseVisualization model
                |> Sg.noEvents

            Sg.ofList [boundary; ellipse]