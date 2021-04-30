namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.UI
open Adaptify
open Chiron
open PRo3D.Base

type SnapshotSettingsAction =
    | SetNumSnapshots of Numeric.Action
    | SetFieldOfView  of Numeric.Action
    | SetRenderMask   of option<bool>
    | ToggleUseObjectPlacements

[<ModelType>]
type SnapshotSettings = {
    version              : int
    numSnapshots         : NumericInput
    fieldOfView          : NumericInput
    renderMask           : option<bool>
    useObjectPlacements  : bool
} with
    static member read0 = 
        json {
            let! numSnapshots = Json.readWith Ext.fromJson<NumericInput,Ext> "numSnapshots"
            let! fieldOfView  = Json.readWith Ext.fromJson<NumericInput,Ext> "fieldOfView"
            let! renderMask   = Json.tryRead "renderMask"
            let! useObjectPlacements = Json.tryRead "useObjectPlacements"
            let useObjectPlacements =
                match useObjectPlacements with
                | Some true -> true
                | _ -> false
            return {  
                version       = 0
                numSnapshots  = numSnapshots
                fieldOfView   = fieldOfView
                renderMask    = renderMask
                useObjectPlacements = useObjectPlacements
              }
            }

    static member FromJson(_ : SnapshotSettings) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! SnapshotSettings.read0
            | _ -> return! v |> sprintf "don't know version %A  of ViewConfigModel" |> Json.error
        }
    static member ToJson (x : SnapshotSettings) =
        json {                    
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "numSnapshots" x.numSnapshots
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "fieldOfView" x.fieldOfView
            if x.renderMask.IsSome then
              do! Json.write "renderMask" x.renderMask
            if x.useObjectPlacements then
              do! Json.write "useObjectPlacements" x.useObjectPlacements
            do! Json.write "version" x.version
        }


