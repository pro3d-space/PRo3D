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
        version : int
        enabled  : bool
        targetLayer : Option<TextureLayer>
        distance : NumericInput
        width    : NumericInput
        border   : NumericInput
    }

module ContourLineModel =

    let current = 0

    let initial = 
        {
            version = current
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

    let read0 = 
        json {
            let! enabled = Json.read "enabled"
            let! targetLayer = Json.readOrDefault "targetLayer" None
            let! distanceValue = Json.readFloat "distance"
            let! widthValue = Json.readFloat "width"
            let! borderValue = Json.readFloat "border"
            
            return {
                version = current
                enabled = enabled
                targetLayer = targetLayer
                distance = { initial.distance with value = distanceValue }
                width = { initial.width with value = widthValue }
                border = { initial.border with value = borderValue }
            }
        }

type ContourLineModel with 
    static member FromJson(_ : ContourLineModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ContourLineModel.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of TextureLayer" 
                |> Json.error 
        }
    static member ToJson (x : ContourLineModel) =
        json {
            do! Json.write "version" x.version
            do! Json.write "enabled" x.enabled
            do! Json.writeOption "targetLayer" x.targetLayer
            do! Json.writeFloat "distance" x.distance.value
            do! Json.writeFloat "width" x.width.value
            do! Json.writeFloat "border" x.border.value
        }

/// Additive latitude/longitude graticule drawn on OPC planetary surfaces.
/// Overlay only - composed into the surface effect stack next to the contour
/// lines, never replacing a shader. See docs/LatLon-Shader.md.
[<ModelType>]
type LatLonShaderModel =
    {
        version     : int
        enabled     : bool
        /// Phase 2 renders the degree-indicator labels; Phase 1 only stores the flag.
        showLabels  : bool
        /// Label size in screen pixels (consumed in Phase 2).
        textSize    : NumericInput
        /// Degrees between parallels; an integer divisor of 360 (equidistant).
        latInterval : int
        /// Degrees between meridians; an integer divisor of 360 (equidistant).
        lonInterval : int
        lineColor   : ColorInput
        /// Grid line width in screen pixels.
        lineWidth   : NumericInput
    }

module LatLonShaderModel =

    let current = 0

    /// The 24 positive integer divisors of 360. Dropdown source; the update guard
    /// and the JSON clamp both reference this.
    let divisorsOf360 : list<int> =
        [ 1; 2; 3; 4; 5; 6; 8; 9; 10; 12; 15; 18; 20; 24
          30; 36; 40; 45; 60; 72; 90; 120; 180; 360 ]

    let defaultInterval = 10

    /// Force an interval onto the divisor grid, falling back to the default.
    let sanitizeInterval (i : int) =
        if i > 0 && 360 % i = 0 then i else defaultInterval

    let initial =
        {
            version     = current
            enabled     = false
            showLabels  = false
            textSize    = { value = 14.0; min = 4.0; max = 96.0; step = 1.0; format = "{0:0}" }
            latInterval = defaultInterval
            lonInterval = defaultInterval
            lineColor   = { c = C4b(255uy, 210uy, 60uy, 255uy) }   // amber - visible on Mars terrain
            lineWidth   = { value = 1.5; min = 0.5; max = 10.0; step = 0.5; format = "{0:0.0}" }
        }

    let read0 =
        json {
            let! enabled     = Json.readOrDefault "enabled" false
            let! showLabels  = Json.readOrDefault "showLabels" false
            let! textSize    = Json.tryRead "textSize"
            let! latInterval = Json.readOrDefault "latInterval" defaultInterval
            let! lonInterval = Json.readOrDefault "lonInterval" defaultInterval
            let! lineWidth   = Json.tryRead "lineWidth"
            let! lineColorJ  = Json.tryRead "lineColor"
            let! lineColor =
                match lineColorJ with
                | Some (_ : Chiron.Json) -> Json.readWith Ext.fromJson<ColorInput,Ext> "lineColor"
                | None -> json { return initial.lineColor }
            return {
                version     = current
                enabled     = enabled
                showLabels  = showLabels
                textSize    = match textSize  with Some v -> { initial.textSize  with value = v } | None -> initial.textSize
                latInterval = sanitizeInterval latInterval
                lonInterval = sanitizeInterval lonInterval
                lineColor   = lineColor
                lineWidth   = match lineWidth with Some v -> { initial.lineWidth with value = v } | None -> initial.lineWidth
            }
        }

type LatLonShaderModel with
    static member FromJson(_ : LatLonShaderModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! LatLonShaderModel.read0
            | _ ->
                return! v
                |> sprintf "don't know version %A of LatLonShaderModel"
                |> Json.error
        }
    static member ToJson (x : LatLonShaderModel) =
        json {
            do! Json.write "version"     x.version
            do! Json.write "enabled"     x.enabled
            do! Json.write "showLabels"  x.showLabels
            do! Json.writeFloat "textSize" x.textSize.value
            do! Json.write "latInterval" x.latInterval
            do! Json.write "lonInterval" x.lonInterval
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>) "lineColor" x.lineColor
            do! Json.writeFloat "lineWidth" x.lineWidth.value
        }
