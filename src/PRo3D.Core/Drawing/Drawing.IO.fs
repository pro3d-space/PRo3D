namespace PRo3D.Core.Drawing

open Aardvark.Base
open PRo3D
open PRo3D.Base
open PRo3D.Core
open Chiron

module IO =

    let getSerialized (model : DrawingModel) : string =
        {
            version        = Annotations.current
            annotations    = model.annotations
            dnsColorLegend = model.dnsColorLegend
        } 
        |> Json.serialize 
        |> Json.formatWith JsonFormattingOptions.Pretty 
    
    let saveVersioned (model : DrawingModel) (path : string) =
        if path.IsNullOrEmpty() then
            model
        else
            Log.startTimed "[Drawing] Writing annotation grouping %s" path
            getSerialized model
            |> Serialization.writeToFile path

            Log.stop()
            
            model

