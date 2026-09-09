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
        /// 1° parallels (line width 0.5 px).
        lat1        : bool
        /// 5° parallels (line width 1.0 px).
        lat5        : bool
        /// 15° parallels (line width 2.0 px).
        lat15       : bool
        /// 1° meridians (line width 0.5 px).
        lon1        : bool
        /// 5° meridians (line width 1.0 px).
        lon5        : bool
        /// 15° meridians (line width 2.0 px).
        lon15       : bool
        /// Colour of the 1/5/15 graticule lines. The equator is always drawn
        /// yellow and the prime meridian red (width 2.5 px), regardless of this.
        lineColor   : ColorInput
    }

module LatLonShaderModel =

    let current = 1

    let initial =
        {
            version     = current
            enabled     = false
            lat1        = false
            lat5        = true
            lat15       = true
            lon1        = false
            lon5        = true
            lon15       = true
            lineColor   = { c = C4b(0uy, 0uy, 0uy, 255uy) }   // amber - visible on Mars terrain
        }

    let private readLineColor =
        json {
            let! lineColorJ = Json.tryRead "lineColor"
            match lineColorJ with
            | Some (_ : Chiron.Json) -> return! Json.readWith Ext.fromJson<ColorInput,Ext> "lineColor"
            | None -> return initial.lineColor
        }

    /// v0 stored a single lat/lon interval + a line width. The multi-scale grid
    /// replaced them, so the granularity toggles fall back to the v1 defaults;
    /// enabled and the line colour are preserved.
    let read0 =
        json {
            let! enabled   = Json.readOrDefault "enabled" false
            let! lineColor = readLineColor
            return { initial with enabled = enabled; lineColor = lineColor }
        }

    let read1 =
        json {
            let! enabled   = Json.readOrDefault "enabled" false
            let! lat1      = Json.readOrDefault "lat1"  initial.lat1
            let! lat5      = Json.readOrDefault "lat5"  initial.lat5
            let! lat15     = Json.readOrDefault "lat15" initial.lat15
            let! lon1      = Json.readOrDefault "lon1"  initial.lon1
            let! lon5      = Json.readOrDefault "lon5"  initial.lon5
            let! lon15     = Json.readOrDefault "lon15" initial.lon15
            let! lineColor = readLineColor
            return {
                version   = current
                enabled   = enabled
                lat1 = lat1; lat5 = lat5; lat15 = lat15
                lon1 = lon1; lon5 = lon5; lon15 = lon15
                lineColor = lineColor
            }
        }

type LatLonShaderModel with
    static member FromJson(_ : LatLonShaderModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! LatLonShaderModel.read0
            | 1 -> return! LatLonShaderModel.read1
            | _ ->
                return! v
                |> sprintf "don't know version %A of LatLonShaderModel"
                |> Json.error
        }
    static member ToJson (x : LatLonShaderModel) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "enabled"    x.enabled
            do! Json.write "lat1"  x.lat1
            do! Json.write "lat5"  x.lat5
            do! Json.write "lat15" x.lat15
            do! Json.write "lon1"  x.lon1
            do! Json.write "lon5"  x.lon5
            do! Json.write "lon15" x.lon15
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>) "lineColor" x.lineColor
        }
