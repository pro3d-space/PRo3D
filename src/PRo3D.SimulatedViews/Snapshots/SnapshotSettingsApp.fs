namespace PRo3D.SimulatedViews

open System
open Aardvark.Base
open Adaptify
open Aardvark.UI
open PRo3D.Core.Surface
open PRo3D.Core
open FSharp.Data.Adaptive
open Chiron
open PRo3D.Base

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SnapshotSettings =
    let view (model : AdaptiveSnapshotSettings) =
       require Html.semui (
          Html.table 
            [   Html.row "Snapshot Settings" []
                Html.row "Snapshots per Pair of bookmarks :" [ Numeric.view' [InputBox] model.numSnapshots    |> UI.map SetNumSnapshots ]
                Html.row "Field of View                   :" [ Numeric.view' [InputBox] model.fieldOfView     |> UI.map SetFieldOfView  ]
            ]
       )

    let currentVersion = 0
    let init = 
        let snapshots = {
            value   = float 10
            min     = float 1
            max     = float 10000
            step    = float 1
            format  = "{0:0}"    
        }

        let foV = {
            value   = float 60.0
            min     = float 0.0
            max     = float 100.0
            step    = float 0.01
            format  = "{0:0.00}"
        }
        {
            version         = currentVersion
            numSnapshots    = snapshots
            fieldOfView     = foV
            renderMask      = Some true
        }

