namespace PRo3D

open Aardvark.UI
open PRo3D.Comparison
open FSharp.Data.Adaptive
open PRo3D.Base
open Aardvark.UI
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SurfaceMeasurements =
    let calculate (m : SurfaceMeasurements) (surface : Surface) (surfaceSg : SgSurface) =
        ()


    let view (m : AdaptiveSurfaceMeasurements) =
        let xSize = m.dimensions |> AVal.map (fun x -> sprintf "%f" x.X)
        let ySize = m.dimensions |> AVal.map (fun x -> sprintf "%f" x.Y)
        let zSize = m.dimensions |> AVal.map (fun x -> sprintf "%f" x.Z)
        let xDir  = m.axesDirections.xDir |> AVal.map (fun x -> string x) 
        let yDir  = m.axesDirections.yDir |> AVal.map (fun x -> string x) 
        let zDir  = m.axesDirections.zDir |> AVal.map (fun x -> string x) 
        require GuiEx.semui (
          Html.table [      
            Html.row "Size along x-axis" [Incremental.text xSize]
            Html.row "Size along y-axis" [Incremental.text ySize]
            Html.row "Size along z-axis" [Incremental.text zSize]
            Html.row "Direction x-axis " [Incremental.text xDir]
            Html.row "Direction y-axis " [Incremental.text yDir]
            Html.row "Direction z-axis " [Incremental.text zDir]
        ])
