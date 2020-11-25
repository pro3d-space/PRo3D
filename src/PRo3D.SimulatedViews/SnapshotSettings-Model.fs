namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.UI
open Adaptify

[<ModelType>]
type SnapshotSettings = {
    numSnapshots  : NumericInput
    fieldOfView   : NumericInput
} with 
    static member init = 
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
            numSnapshots    = snapshots
            fieldOfView     = foV
        }

type SnapshotSettingsAction =
    | SetNumSnapshots of Numeric.Action
    | SetFieldOfView  of Numeric.Action