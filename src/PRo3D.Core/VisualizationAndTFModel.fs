namespace PRo3D.Core

open Adaptify
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open Chiron

[<ModelType>]
type ScalarLayer = {
    version      : int
    label        : string
    actualRange  : Range1d
    definedRange : Range1d
    index        : int
    colorLegend  : FalseColorsModel
}
module ScalarLayer =
    let current = 0  

    let read0 =
        json {
            let! label        = Json.read "label"
            let! actualRange  = Json.read "actualRange" 
            let! definedRange = Json.read "definedRange"
            let! index        = Json.read "index"       
            let! colorLegend  = Json.read "colorLegend"
            
            return
                {
                    version      = current
                    label        = label
                    actualRange  = actualRange  |> Range1d.Parse
                    definedRange = definedRange |> Range1d.Parse
                    index        = index
                    colorLegend  = colorLegend 
                }
        }

type ScalarLayer with 
    static member FromJson(_ : ScalarLayer) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ScalarLayer.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of ScalarLayer"
                |> Json.error
        }
    static member ToJson (x : ScalarLayer) =
        json {
            do! Json.write "version"        x.version
            do! Json.write "label"          x.label
            do! Json.write "actualRange"    (x.actualRange.ToString())
            do! Json.write "definedRange"   (x.definedRange.ToString())
            do! Json.write "index"          x.index
            do! Json.write "colorLegend"    x.colorLegend
        }

type TextureLayer = {
    version : int
    label   : string
    index   : int
}

module TextureLayer =
    let current = 0
    let read0 = 
        json {
            let! label  = Json.read "label"
            let! index  = Json.read "index"

            return {
                version = current
                label   = label
                index   = index
            }
        }

type TextureLayer with 
    static member FromJson(_ : TextureLayer) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! TextureLayer.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of TextureLayer" 
                |> Json.error 
        }
    static member ToJson (x : TextureLayer) =
        json {
            do! Json.write "version" x.version
            do! Json.write "label"   x.label
            do! Json.write "index"   x.index
        }

type AttributeLayer = 
    | ScalarLayer  of ScalarLayer
    | TextureLayer of TextureLayer

[<ModelType>]
type ContourLineModel =
    {
        enabled  : bool
        targetLayer : Option<TextureLayer>
        distance : NumericInput
        width    : NumericInput
        border   : NumericInput
    }

module ContourLineModel =

    let initial = 
        {
            enabled = false
            distance = {
                value = 0
                min =  0.0
                max = 100.0
                step = 0.0001
                format = "{0:0.0000}"
            }
            width = {
                value = 0.01
                min =  0.0
                max = 10.0
                step = 0.0001
                format = "{0:0.0000}"
            }
            border = {
                value = 0.01
                min =  0.0
                max = 10.0
                step = 0.0001
                format = "{0:0.0000}"
            }
            targetLayer = None
        }
