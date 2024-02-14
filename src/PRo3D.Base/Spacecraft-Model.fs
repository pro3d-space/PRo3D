namespace PRo3D.Base.Gis

open Adaptify


[<ModelType>]
type Spacecraft =
    {
        [<NonAdaptive>]
        id             : SpacecraftId
        label          : string
        spiceName      : SpacecraftSpiceName
        referenceFrame : option<FrameSpiceName>
    }