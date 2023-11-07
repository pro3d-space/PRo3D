namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.UI
open Adaptify
open Chiron
open PRo3D.Base

#nowarn "0686"

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

/// GUI interface to object placement parameters
[<ModelType>]
type ObjectPlacementApp = {
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
    /// maximim distance of placed objects to camera
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

    static member TestData : ObjectPlacementApp=
        ObjectPlacementApp.init

    static member current = 0
    static member private readV0 : Json<ObjectPlacementApp>= 
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
    static member FromJson(_ : ObjectPlacementApp) = 
        json {
            return! ObjectPlacementApp.readV0
        }
    static member ToJson (x : ObjectPlacementApp) =
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

/// parameters for ramdom object placements in snapshots
type ObjectPlacementParameters = {
  name         : string
  count        : int
  color        : option<C4b>
  contrast     : option<float>
  brightness   : option<float>
  gamma        : option<float>
  scale        : option<V2i> // [min,max]
  xRotation    : option<V2i> // [min,max] in degree?
  yRotation    : option<V2i> 
  zRotation    : option<V2i>
  maxDistance  : option<float>
  subsurface   : option<int>
  maskColor    : option<C4b>
} 
with 
  static member TestData =
    {
        name         = "02-Presqu-ile-LF.obj"//"Steinheim_Germany_Model_1.obj"
        count        = 4
        color        = None //Some (C4b(242, 198, 153, 255))
        contrast     = None
        brightness   = None
        gamma        = None
        scale        = Some (V2i(30, 100))
        xRotation    = None
        yRotation    = None //Some (V2i(0, 180))
        zRotation    = Some (V2i(0, 360))
        maxDistance  = None // Some 5.0
        subsurface   = Some 50 //None
        maskColor    = Some C4b.Green
    }
  static member current = 0
  static member private readV0 = 
      json {
        let! name        = Json.read "name"
        let! count       = Json.read "count"
        let! color       = Json.parseOption (Json.tryRead "color") C4b.Parse
        let! contrast    = Json.tryRead "contrast"
        let! brightness  = Json.tryRead "brightness"
        let! gamma       = Json.tryRead "gamma"
        let! scale       = Json.parseOption (Json.tryRead "scale") V2i.Parse 
        let! xRotation   = Json.parseOption (Json.tryRead "xRotation") V2i.Parse 
        let! yRotation   = Json.parseOption (Json.tryRead "yRotation") V2i.Parse 
        let! zRotation   = Json.parseOption (Json.tryRead "zRotation") V2i.Parse 
        let! maxDistance = Json.tryRead "maxDistance"
        let! subsurface  = Json.tryRead "subsurface"
        let! maskColor   = Json.parseOption (Json.tryRead "maskColor") C4b.Parse
        
        let res = {
            name        = name
            count       = count
            color       = color
            contrast    = contrast
            brightness  = brightness
            gamma       = gamma
            scale       = scale 
            xRotation   = xRotation 
            yRotation   = yRotation 
            zRotation   = zRotation   
            maxDistance  = maxDistance
            subsurface   = subsurface 
            maskColor    = maskColor            
        }
        return res
      }
  static member FromJson(_ : ObjectPlacementParameters) = 
    json {
        return! ObjectPlacementParameters.readV0
    }
  static member ToJson (x : ObjectPlacementParameters) =
    json {
      do! Json.write              "name"          (x.name.ToString())
      do! Json.write              "count"         x.count
      if x.color.IsSome then
        do! Json.writeOption      "color"       x.color
      if x.contrast.IsSome then
        do! Json.writeOptionFloat "contrast"    x.contrast
      if x.brightness.IsSome then
        do! Json.writeOptionFloat "brightness"  x.brightness
      if x.gamma.IsSome then
        do! Json.writeOptionFloat "gamma"       x.gamma
      do! Json.writeOption        "scale"         x.scale
      do! Json.writeOption        "xRotation"     x.xRotation
      do! Json.writeOption        "yRotation"     x.yRotation
      do! Json.writeOption        "zRotation"     x.zRotation
      do! Json.writeOptionFloat   "maxDistance"   x.maxDistance
      do! Json.writeOptionInt     "subsurface"    x.subsurface
      if x.maskColor.IsSome then
        do! Json.writeOption      "maskColor"   x.maskColor
    }