namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.UI
open Adaptify

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


type ShatterconeAction =
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