namespace PRo3D.Comparison

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives

open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Comparison.ComparisonUtils

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SurfaceMeasurements =

    let getAxesAngles surface refSystem =
        let trafo = SurfaceTransformations.fullTrafo' surface refSystem

        let mutable rot = V3d.OOO
        let mutable tra = V3d.OOO
        let mutable sca = V3d.OOO
    
        trafo.Decompose (&sca, &rot, &tra)
        //let (x : float) = System.Math.Round (rot.X, 15)
        //let (y : float) = System.Math.Round (rot.Y, 15)
        //let (z : float) = System.Math.Round (rot.Z, 15)
        //V3d (x, y, z)
        rot



    let getDimensions (surface : Surface)
                      (surfaceModel : SurfaceModel) 
                      (refSystem    : ReferenceSystem)
                      (originMode   : OriginMode) =
        let surfaceFilter (id : Guid) (l : Leaf) (s : SgSurface) = 
            id = surface.guid
        let trafo = SurfaceTransformations.fullTrafo' surface refSystem
        let mutable rot = V3d.OOO
        let mutable tra = V3d.OOO
        let mutable sca = V3d.OOO

        trafo.Decompose (&sca, &rot, &tra)
        let origin =
            match originMode with
            | OriginMode.ModelOrigin -> tra
            | OriginMode.BoundingBoxCentre -> 
                let sgSurface = HashMap.find surface.guid surfaceModel.sgSurfaces 
                let boundingBox = sgSurface.globalBB.Transformed trafo
                boundingBox.Center
            | _ -> - tra
    
        let rotation = rot |> Trafo3d.RotationEuler 
                           |> Rot3d.FromTrafo3d
        Log.warn "[DEBUG] calc trafo translation = %s" (tra.ToString ())
        Log.warn "[DEBUG] ref system origin = %s" (refSystem.origin.ToString ())
        let localZDir = rotation.Transform refSystem.up.value.Normalized
        let localYDir = rotation.Transform refSystem.north.value.Normalized
        let localXDir = (localZDir.Cross localYDir).Normalized

        let zDirHit = calculateRayHit origin localZDir surfaceModel refSystem surfaceFilter
        let minusZDirHit = calculateRayHit origin -localZDir surfaceModel refSystem surfaceFilter
        let yDirHit = calculateRayHit origin localYDir surfaceModel refSystem surfaceFilter
        let minusYDirHit = calculateRayHit origin -localYDir surfaceModel refSystem surfaceFilter
        let xDirHit = calculateRayHit origin localXDir surfaceModel refSystem surfaceFilter
        let minusXDirHit = calculateRayHit origin -localXDir surfaceModel refSystem surfaceFilter

        let zSize = Option.map2 (fun (a : V3d) b ->  Vec.Distance (a, b)) zDirHit minusZDirHit
        let ySize = Option.map2 (fun (a : V3d) b ->  Vec.Distance (a, b)) yDirHit minusYDirHit
        let xSize = Option.map2 (fun (a : V3d) b ->  Vec.Distance (a, b)) xDirHit minusXDirHit

        match xSize, ySize, zSize with
        | Some x, Some y, Some z ->
            V3d (x, y, z)
        | _,_,_ -> 
            Log.error "[Comparison] Could not calculate surface size along axes."
            V3d.OOO

    let updateSurfaceMeasurement  (surfaceModel : SurfaceModel) 
                                  (refSystem    : ReferenceSystem) 
                                  (originMode   : OriginMode)
                                  (surfaceName  : string) =
        let surfaceId = findSurfaceByName surfaceModel surfaceName
        match surfaceId with
        | Some surfaceId ->
            let surface = surfaceModel.surfaces.flat |> HashMap.find surfaceId |> Leaf.toSurface
            let sgSurface = surfaceModel.sgSurfaces |> HashMap.find surfaceId
      
            let axesAngles = getAxesAngles surface refSystem
            let dimensions = 
                getDimensions surface surfaceModel refSystem originMode
            {SurfaceMeasurements.init with rollPitchYaw = axesAngles
                                           dimensions   = dimensions
            } |> Some
        | None -> None

    let compare (m1 : SurfaceMeasurements) 
                (m2 : SurfaceMeasurements) =
        {
            dimensions   = V3d.Abs (m1.dimensions - m2.dimensions)
            rollPitchYaw = V3d.Abs (m1.rollPitchYaw - m2.rollPitchYaw)
        }

    let view (m : SurfaceMeasurements) =
        let xSize = sprintf "%f" m.dimensions.X
        let ySize = sprintf "%f" m.dimensions.Y
        let zSize = sprintf "%f" m.dimensions.Z
        let roll  = sprintf "%f" m.rollPitchYaw.X
        let pitch = sprintf "%f" m.rollPitchYaw.Y
        let yaw   = sprintf "%f" m.rollPitchYaw.Z
        require GuiEx.semui (
          Html.table [      
            Html.row "Size along x-axis" [text xSize]
            Html.row "Size along y-axis" [text ySize]
            Html.row "Size along z-axis" [text zSize]
            Html.row "X-axis angle (radians)" [text roll]
            Html.row "Y-axis angle (radians)" [text pitch]
            Html.row "Z-axis angle (radians)" [text yaw]
        ])        


    //let view (m : AdaptiveSurfaceMeasurements) =
    //    let xSize = m.dimensions |> AVal.map (fun x -> sprintf "%f" x.X)
    //    let ySize = m.dimensions |> AVal.map (fun x -> sprintf "%f" x.Y)
    //    let zSize = m.dimensions |> AVal.map (fun x -> sprintf "%f" x.Z)
    //    let roll  = m.rollPitchYaw |> AVal.map (fun x -> sprintf "%f" x.X)
    //    let pitch  = m.rollPitchYaw |> AVal.map (fun x -> sprintf "%f" x.Y)
    //    let yaw  = m.rollPitchYaw |> AVal.map (fun x -> sprintf "%f" x.Z)
    //    require GuiEx.semui (
    //      Html.table [      
    //        Html.row "Size along x-axis" [Incremental.text xSize]
    //        Html.row "Size along y-axis" [Incremental.text ySize]
    //        Html.row "Size along z-axis" [Incremental.text zSize]
    //        Html.row "X-axis angle (radians)" [Incremental.text roll]
    //        Html.row "Y-axis angle (radians)" [Incremental.text pitch]
    //        Html.row "Z-axis angle (radians)" [Incremental.text yaw]
    //    ])
