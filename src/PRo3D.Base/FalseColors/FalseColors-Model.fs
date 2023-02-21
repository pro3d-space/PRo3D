namespace PRo3D.Base

open System
open Aardvark.Base
open Aardvark.UI

open Chiron
open Adaptify

#nowarn "0686"

[<ModelType>]
type FalseColorsModel = {
    version         : int
    useFalseColors  : bool
    lowerBound      : NumericInput
    upperBound      : NumericInput
    interval        : NumericInput
    invertMapping   : bool
    lowerColor      : ColorInput //C4b
    upperColor      : ColorInput //C4b    
}

type FalseColorsShaderParams = {
    hsvStart   : V3d //(h, s, v)
    hsvEnd     : V3d //(h, s, v)
    interval   : float
    inverted   : bool
    lowerBound : float
    upperBound : float
    stepS      : float
    numOfRG    : float
}

module FalseColorsModel =
    
    let current = 0
    let read0 =
        json {
            let! useFalseColors = Json.read "useFalseColors"
            let! lowerBound     = Json.readWith Ext.fromJson<NumericInput,Ext> "lowerBound"
            let! upperBound     = Json.readWith Ext.fromJson<NumericInput,Ext> "upperBound"
            let! interval       = Json.readWith Ext.fromJson<NumericInput,Ext> "interval"
            let! invertMapping  = Json.read "invertMapping"
            let! lowerColor     = Json.readWith Ext.fromJson<ColorInput,Ext> "lowerColor"
            let! upperColor     = Json.readWith Ext.fromJson<ColorInput,Ext> "upperColor"

            return 
                {
                    version        = current
                    useFalseColors = useFalseColors
                    lowerBound     = lowerBound
                    upperBound     = upperBound
                    interval       = interval
                    invertMapping  = invertMapping
                    lowerColor     = lowerColor
                    upperColor     = upperColor
                }
        }
        //TODO TO rename inits
    let dnSInterv  = {
        value   = 5.0
        min     = 0.0
        max     = 90.0
        step    = 0.1
        format = "{0:0.00}"
    } 
    let initMinAngle = {
        value   = 0.0
        min     = 0.0
        max     = 90.0
        step    = 1.0
        format  = "{0:0.0}"
    }
    let initMaxAngle = {
        value   = 45.0
        min     = 1.0
        max     = 90.0
        step    = 1.0
        format  = "{0:0.0}"
    }

    let initDnSLegend = 
        {
            version         = current
            useFalseColors  = false
            lowerBound      = initMinAngle
            upperBound      = initMaxAngle
            interval        = dnSInterv
            invertMapping   = false
            lowerColor      = { c = C4b.Blue }
            upperColor      = { c = C4b.Red }
        }
   
    let scalarsInterv  = {
        value   = 5.0
        min     = 0.0
        max     = 90.0
        step    = 0.0001
        format  = "{0:0.0000}"
    } 

    let initlb (range: Range1d) = {
        value   = range.Min
        min     = range.Min
        max     = range.Max
        step    = 0.0001
        format  = "{0:0.0000}"
    }

    let initub (range: Range1d) = {
        value   = range.Max
        min     = range.Min
        max     = range.Max
        step    = 0.0001
        format  = "{0:0.0000}"
    }

    let initDefinedScalarsLegend (range: Range1d) = {
        version         = current
        useFalseColors  = false
        lowerBound      = initlb range
        upperBound      = initub range 
        interval        = scalarsInterv 
        invertMapping   = false
        lowerColor      = { c = C4b.Blue }
        upperColor      = { c = C4b.Red }
    }
    
    let initShaderParams = 
        {
            hsvStart = V3d(0.0, 0.0, 0.0) 
            hsvEnd   = V3d(0.0, 0.0, 0.0) 
            interval = 1.0
            inverted = false
            lowerBound = 0.0
            upperBound = 1.0
            stepS     = 1.0
            numOfRG  = 1.0
        }

    let depthInterv  = {
        value   = 5.0
        min     = 0.0
        max     = 90.0
        step    = 0.1
        format = "{0:0.00}"
    } 
    let initMinDepth = {
        value   = 0.0
        min     = 0.0
        max     = 100.0
        step    = 1.0
        format  = "{0:0.0}"
    }
    let initMaxDepth = {
        value   = 50.0
        min     = 1.0
        max     = 10000.0
        step    = 1.0
        format  = "{0:0.0}"
    }

    let initDepthLegend = 
        {
            version         = current
            useFalseColors  = false
            lowerBound      = initMinDepth
            upperBound      = initMaxDepth
            interval        = depthInterv
            invertMapping   = false
            lowerColor      = { c = C4b.Blue }
            upperColor      = { c = C4b.Red }
        }

type FalseColorsModel with
    static member FromJson(_ : FalseColorsModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! FalseColorsModel.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of FalseColorsModel"
                |> Json.error
        }
    static member ToJson (x : FalseColorsModel) =
        json {
            do! Json.write "version"         x.version
            do! Json.write "useFalseColors"  x.useFalseColors
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "lowerBound" x.lowerBound
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "upperBound" x.upperBound
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "interval"   x.interval
            do! Json.write "invertMapping"   x.invertMapping 
            do! Json.writeWith Ext.toJson<ColorInput,Ext> "lowerColor"   x.lowerColor
            do! Json.writeWith Ext.toJson<ColorInput,Ext> "upperColor"   x.upperColor
    
        }    

