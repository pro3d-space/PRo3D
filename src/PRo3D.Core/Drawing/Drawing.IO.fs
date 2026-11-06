namespace PRo3D.Core.Drawing

open Aardvark.Base
open PRo3D
open PRo3D.Base
open PRo3D.Core
open Chiron

module IO =

    /// The persisted part of the drawing model (the `.pro3d.ann` document).
    let toAnnotations (model : DrawingModel) : Annotations =
        {
            version         = Annotations.current
            annotations     = model.annotations
            dnsColorLegend  = model.dnsColorLegend
            colorByCategory = model.colorByCategory
            geologicalMaps  = model.geologicalMaps
        }

    let getSerialized (model : DrawingModel) : string =
        toAnnotations model
        |> Json.serialize
        |> Json.formatWith JsonFormattingOptions.Pretty

    let saveVersioned (model : DrawingModel) (path : string) =
        Log.startTimed "[Drawing] Writing annotation grouping %s" path
        getSerialized model
        |> Serialization.writeToFileAtomic path

        Log.stop()
            
        model

