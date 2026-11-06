namespace PRo3D.Base

open System
open Aardvark.Base
open Aardvark.Rendering

open Chiron

module ColorMaps =

    type Marker = Marker

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

        let read = 
            json {
                let! case  = Json.read "case"
                match case with
                | "Ramp" -> 
                    let! min  = Json.read "min"
                    let! max  = Json.read "max"
                    let! name = Json.read "name"
                    return Ramp(min, max, name)
                | "Passthrough" ->
                    return Passthrough
                | _ ->
                    return Passthrough // is this ok as a default?
            }

    type TF with 
        static member FromJson(_ : TF) =
            json {
                return! TF.read
            }
        static member ToJson (x : TF) =
            match x with
            | Ramp(min, max, name) ->
                json {
                    do! Json.write "case" "Ramp"
                    do! Json.write "min" (if Double.IsFinite min && abs min < 7.9e28 then min else 0.0) // readers use plain read: never null (#834)
                    do! Json.write "max" (if Double.IsFinite max && abs max < 7.9e28 then max else 1.0)
                    do! Json.write "name" name
                }
            | Passthrough ->
                json {
                    do! Json.write "case" "Passthrough"
                }

type TextureCombiner =
    | Unknown = 0
    | Primary = 1
    | Secondary = 2
    | Multiply = 3
    | Blend = 4

type TransferFunctionMode =
    | Unknown = 0
    | Ramp = 1
    | Passthrough = 2