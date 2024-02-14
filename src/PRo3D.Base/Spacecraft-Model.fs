namespace PRo3D.Base.Gis

open Adaptify
open Chiron

[<ModelType>]
type Spacecraft =
    {
        [<NonAdaptive>]
        id             : SpacecraftId
        label          : string
        spiceName      : SpacecraftSpiceName
        referenceFrame : option<FrameSpiceName>
    }
 with
    static member FromJson(_ : Spacecraft) = 
        json {
            let! id             = Json.read     "id"
            let! label          = Json.read     "label"
            let! spiceName      = Json.read     "spiceName"
            let! referenceFrame = Json.tryRead  "referenceFrame"
         
            return {
                id             = id            
                label          = label         
                spiceName      = spiceName     
                referenceFrame = referenceFrame                
            }
        }
    static member ToJson (x : Spacecraft) =
        json {              
            do! Json.write     "id"             x.id            
            do! Json.write     "label"          x.label         
            do! Json.write     "spiceName"      x.spiceName     
            do! Json.write     "referenceFrame" x.referenceFrame
        }