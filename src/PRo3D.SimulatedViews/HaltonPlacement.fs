namespace PRo3D.SimulatedViews

open System
open System.IO

open Aardvark.Base
open Aardvark.Base.Geometry
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Base.Rendering
open Aardvark.Base.Rendering.Effects
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open Aardvark.UI.Animation
open Aardvark.Rendering.Text

open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.SgPrimitives.Sg
open Aardvark.GeoSpatial.Opc
open OpcViewer.Base

open Adaptify.FSharp.Core

module HaltonPlacement =
    let create2DHaltonRandomSeries =
        new HaltonRandomSeries(2, new RandomSystem(System.DateTime.Now.Second))

    let create1DHaltonRandomSeries =
        new HaltonRandomSeries(1, new RandomSystem(System.DateTime.Now.Second))
    
    let genRandomNumbers count =
        let rnd = System.Random()
        List.init count (fun _ -> rnd.Next())
    
    let genRandomNumbersBetween count min max =
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
        
    //let getHaltonRandomTrafos (count : int) (m : Model) =
    let getHaltonRandomTrafos (shattercone : SnapshotShattercone) (frustum : Frustum) (view : CameraView) =
        let haltonSeries = create2DHaltonRandomSeries
        let rays = computeSCRayRaster shattercone.count view frustum haltonSeries
        let points = getPointsOnSurfaces m rays m.scene.cameraView.Location 

        let hsScaling = 
            match shattercone.scale with
            | Some s -> let rs = genRandomNumbersBetween shattercone.count s.X s.Y
                        rs |> List.map(fun x -> (float)x/100.0) 
            | None -> [ for i in 1 .. shattercone.count -> 1.0 ]

        let xRotation =
            match shattercone.xRotation with
            | Some rx -> genRandomNumbersBetween shattercone.count rx.X rx.Y
            | None -> [ for i in 1 .. shattercone.count -> 0 ]
        
        //let yRotation = genRandomNumbersBetween shattercone.count 45 135
        let yRotation = 
            match shattercone.yRotation with
            | Some ry -> genRandomNumbersBetween shattercone.count ry.X ry.Y
            | None -> [ for i in 1 .. shattercone.count -> 0 ]

        let zRotation =
            match shattercone.zRotation with
            | Some rz -> genRandomNumbersBetween shattercone.count rz.X rz.Y //0 360
            | None -> [ for i in 1 .. shattercone.count -> 0 ]
        //let zRotation = genRandomNumbersBetween shattercone.count 0 360

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
