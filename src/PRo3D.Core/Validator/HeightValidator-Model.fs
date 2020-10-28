namespace PRo3D.Core

open Aardvark.Base
open Adaptify
open Aardvark.UI
open PRo3D.Base
open FSharp.Data.Adaptive

type HeightValidatorAction = 
| PlaceValidator    of V3d
| ChangeInclination of Numeric.Action
| RemoveValidator

[<ModelType>]
type Validator = {
    location       : V3d
    lower          : V3d
    upper          : V3d
    upVector       : V3d
    northVector    : V3d
    height         : double
    inclination    : NumericInput
    tiltedPlane    : Plane3d
}

[<ModelType>]
type ValidatorResult = {    
    pointDistance                     : double
    cooTrafoThickness_geographic      : double
    cooTrafoThickness_true            : double
    heightOverPlaneThickness_true     : double
    heightOverHorizontal : double        
}

[<ModelType>]
type HeightValidatorModel = {
    validatorBrush : IndexList<V3d>
    validator      : Validator
    result         : ValidatorResult
}

module HeightValidatorModel =
    let initValidator() =
        {
            location    = V3d.NaN
            lower       = V3d.NaN
            upper       = V3d.NaN
            upVector    = V3d.NaN
            northVector = V3d.NaN
            height      = 1.0
            inclination = 
                {
                    min     = 0.0
                    max     = 90.0
                    value   = 0.0
                    step    = 1.0
                    format  = "{0:0.0}"
                }
            tiltedPlane = Plane3d.Invalid
        }
        
    let initResult() =        
        {
            pointDistance = nan
            cooTrafoThickness_geographic = nan
            cooTrafoThickness_true = nan
            heightOverHorizontal = nan
            heightOverPlaneThickness_true = nan
        }    

    let init() =
        {
            validatorBrush = IndexList.empty
            validator      = initValidator()
            result         = initResult()
        }
        
    