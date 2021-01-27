namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.UI
open Adaptify
open Chiron
open PRo3D.Base

module internal PlacementInit =
    let count = {
        value   = float 1
        min     = float 0
        max     = float 20
        step    = float 1
        format  = "{0:0}"
    }
    let scale = {
        value   = float 100
        min     = float 1
        max     = float 10000
        step    = float 1
        format  = "{0:0}"    
    }
    let rotation = {
        value   = float 0
        min     = float 0
        max     = float 360
        step    = float 1
        format  = "{0:0}"    
    }
    let distance = {
        value   = float 1000
        min     = float 0
        max     = float 100000
        step    = float 1
        format  = "{0:0}"    
    }
    let sub = {
        value   = float 50
        min     = float 0
        max     = float 100
        step    = float 1
        format  = "{0:0}"    
    }

open PlacementInit

[<ModelType>]
type ShatterconePlacement = {
    name          : string
    count         : NumericInput
    scaleFrom     : NumericInput 
    scaleTo       : NumericInput
    xRotationFrom : NumericInput
    xRotationTo   : NumericInput
    yRotationFrom : NumericInput 
    yRotationTo   : NumericInput 
    zRotationFrom : NumericInput
    zRotationTo   : NumericInput
    maxDistance   : NumericInput
    subsurface    : NumericInput
    maskColor     : ColorInput

} with 
    static member init = 
        {
            name            = ""
            count           = count
            scaleFrom       = scale
            scaleTo         = scale
            xRotationFrom   = {rotation with value = 90.0}
            xRotationTo     = {rotation with value = 90.0}
            yRotationFrom   = rotation 
            yRotationTo     = rotation 
            zRotationFrom   = rotation
            zRotationTo     = rotation
            maxDistance     = distance
            subsurface      = sub
            maskColor       = {c = C4b.Green}
        }     

    static member TestData : ShatterconePlacement=
        ShatterconePlacement.init

    static member current = 0
    static member private readV0 : Json<ShatterconePlacement>= 
        json {
            let! name           = Json.read "name"
            let! count          = Json.readWith Ext.fromJson<NumericInput,Ext> "count"
            let! scaleFrom      = Json.readWith Ext.fromJson<NumericInput,Ext> "scaleFrom"
            let! scaleTo        = Json.readWith Ext.fromJson<NumericInput,Ext> "scaleTo"
            let! xRotationFrom  = Json.readWith Ext.fromJson<NumericInput,Ext> "xRotationFrom"
            let! yRotationFrom  = Json.readWith Ext.fromJson<NumericInput,Ext> "yRotationFrom"
            let! zRotationFrom  = Json.readWith Ext.fromJson<NumericInput,Ext> "zRotationFrom"
            let! xRotationTo  = Json.readWith Ext.fromJson<NumericInput,Ext> "xRotationTo"
            let! yRotationTo  = Json.readWith Ext.fromJson<NumericInput,Ext> "yRotationTo"
            let! zRotationTo  = Json.readWith Ext.fromJson<NumericInput,Ext> "zRotationTo"
            let! maxDistance    = Json.readWith Ext.fromJson<NumericInput,Ext> "maxDistance"
            let! subsurface     = Json.readWith Ext.fromJson<NumericInput,Ext> "subsurface"
            let! maskColor      = Json.readWith Ext.fromJson<ColorInput,Ext> "maskColor"
      
            let res = {
                name          = name
                count         = count
                scaleFrom     = scaleFrom 
                scaleTo       = scaleTo
                xRotationFrom = xRotationFrom
                yRotationFrom = yRotationFrom
                zRotationFrom = zRotationFrom
                xRotationTo   = xRotationTo
                yRotationTo   = yRotationTo
                zRotationTo   = zRotationTo
                maxDistance   = maxDistance
                subsurface    = subsurface 
                maskColor     = maskColor
            }
            return res
        }
    static member FromJson(_ : ShatterconePlacement) = 
        json {
            return! ShatterconePlacement.readV0
        }
    static member ToJson (x : ShatterconePlacement) =
        json {
            do! Json.write "name" x.name
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "count"         x.count
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "scaleFrom"     x.scaleFrom
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "scaleTo"       x.scaleTo
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "xRotationFrom" x.xRotationFrom
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "yRotationFrom" x.yRotationFrom
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "zRotationFrom" x.zRotationFrom
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "xRotationTo"   x.xRotationTo
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "yRotationTo"   x.yRotationTo
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "zRotationTo"   x.zRotationTo
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "maxDistance"   x.maxDistance
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "subsurface"    x.subsurface
            do! Json.writeWith Ext.toJson<ColorInput,Ext> "maskColor"       x.maskColor
        }


type ObjectPlacementAction =
    | SetName         of string
    | SetCount        of Numeric.Action
    | ScaleFrom       of Numeric.Action 
    | ScaleTo         of Numeric.Action
    | XRotationFrom   of Numeric.Action
    | XRotationTo     of Numeric.Action
    | YRotationFrom   of Numeric.Action 
    | YRotationTo     of Numeric.Action 
    | ZRotationFrom   of Numeric.Action
    | ZRotationTo     of Numeric.Action
    | SetMaxDistance  of Numeric.Action
    | SetSubsurface   of Numeric.Action
    | SetMaskColor    of ColorPicker.Action