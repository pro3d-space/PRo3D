namespace PRo3D.SimulatedViews

open System
open Aardvark.Base
open Adaptify
open Aardvark.UI
open PRo3D.Core.Surface
open PRo3D.Core
open FSharp.Data.Adaptive

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SnapshotSettings =
    let view (model : AdaptiveSnapshotSettings) =
       require Html.semui (
          Html.table 
            [      
                Html.row "Snapshots per pair of bookmarks:" [ Numeric.view' [InputBox] model.numSnapshots    |> UI.map SetNumSnapshots ]
                Html.row "field ofview                   :" [ Numeric.view' [InputBox] model.fieldOfView     |> UI.map SetFieldOfView  ]
            ]
       )

