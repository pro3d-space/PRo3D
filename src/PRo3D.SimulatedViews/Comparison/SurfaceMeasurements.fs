namespace PRo3D

open Aardvark.Base
open Aardvark.UI
open PRo3D.Comparison
open FSharp.Data.Adaptive
open PRo3D.Base
open Aardvark.UI
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Base
open Aardvark.Rendering
open Adaptify.FSharp.Core

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SurfaceMeasurements =

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
