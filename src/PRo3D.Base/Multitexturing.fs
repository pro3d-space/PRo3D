namespace PRo3D.Base

open Aardvark.Base
open Aardvark.Rendering

module ColorMaps =

    type private Marker = Marker

    let colorMaps = 
        let loadTexture (name : string) =
            lazy
                let stream = typeof<Marker>.Assembly.GetManifestResourceStream(sprintf "PRo3D.Base.resources.%s" name)
                PixTexture2d(PixImageMipMap(PixImage.Load(stream)), false)
        Map.ofList [ 
            "plasma",   loadTexture "plasma.png"
            "oranges",  loadTexture "oranges.png"
            "spectral", loadTexture "spectral.png"
        ]

    type TF = 
        | Ramp of min : float * max : float * name : string
        | Passthrough

    module TF =

        let trySetRamp (f : float -> float -> string -> TF) (tf : TF) =
            match tf with
            | TF.Ramp(min,max,name) -> f min max name
            | _ -> tf

        let trySetMin (min : float) (tf : TF) =
            trySetRamp (fun _ max name -> TF.Ramp(min,max,name)) tf

        let trySetMax (max : float) (tf : TF) =
            trySetRamp (fun min _ name -> TF.Ramp(min,max,name)) tf

        let trySetName (name : string) (tf : TF) =
            trySetRamp (fun min max _ -> TF.Ramp(min,max,name)) tf


type TextureCombiner =
    | Unknown = 0
    | Primary = 1
    | Secondary = 2
    | Multiply = 3

type TransferFunctionMode =
    | Unknown = 0
    | Ramp = 1
    | Passthrough = 2