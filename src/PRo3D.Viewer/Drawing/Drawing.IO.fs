namespace PRo3D.Drawing

open Aardvark.Base
open PRo3D
open PRo3D.Base
open PRo3D.Core
open Chiron

module IO =
    
    let saveVersioned (model : DrawingModel) (path : string) =
        if path.IsNullOrEmpty() then
            model
        else
            Log.startTimed "[Drawing] Writing annotation grouping %s" path
            {
                version        = Annotations.current
                annotations    = model.annotations
                dnsColorLegend = model.dnsColorLegend
            } 
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile path

            Log.stop()
            
            model

