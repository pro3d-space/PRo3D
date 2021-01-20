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

    let createValidator (p1:V3d) (p2:V3d) (up:V3d) (north : V3d) (angle : float) =

        let east = up.Cross north
        
        let rotation     = Trafo3d.Rotation(east, angle.RadiansFromDegrees())
        let tiltedUp     = rotation.Forward.TransformDir(up)
        let tiltedPlane  = new Plane3d(tiltedUp, p1)
        
        {
            location    = p1
            lower       = p1
            upper       = p2 // + (north * 2.0) + (up)
            upVector    = up
            northVector = north
            height      = 1.0
            inclination = 
                {
                    min     = 0.0
                    max     = 90.0
                    value   = angle
                    step    = 1.0
                    format  = "{0:0.0}"
                }
            tiltedPlane = tiltedPlane
        }

    let updateValidator v (a : Numeric.Action) =
        
        let east = v.upVector.Cross v.northVector
        
        let rotation     = Trafo3d.Rotation(east, v.inclination.value.RadiansFromDegrees())
        let tiltedUp     = rotation.Forward.TransformDir(v.upVector)
        let tiltedPlane  = new Plane3d(tiltedUp, v.lower)

        Log.line "updating plane normal %A" tiltedPlane.Normal

        { v with inclination = Numeric.update v.inclination a; tiltedPlane = tiltedPlane }

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
        
    let computeResult (validator : Validator) : ValidatorResult =

        //let angle = 90.0 - validator.inclination.value

       // let trafo = Trafo3d.Rotation(validator.northVector, angle.RadiansFromDegrees())
        
        let up    = validator.upVector
        let dip   = validator.inclination.value
        let pos   = validator.location

        let lower = validator.lower
        let upper = validator.upper
       
        let cooHeightPos   = PRo3D.Base.CooTransformation.getElevation' Planet.Mars lower
        let cooHeightUpper = PRo3D.Base.CooTransformation.getElevation' Planet.Mars upper

        let geographic = cooHeightUpper - cooHeightPos

        let horizontal = new Plane3d(up,lower)
        let horizontalHeight = horizontal.Height(upper)
        
        let tiltedHeight = validator.tiltedPlane.Height(upper)
        
        let res =
            {
                pointDistance                 = Vec.distance pos upper
                cooTrafoThickness_geographic  = geographic
                cooTrafoThickness_true        = geographic * cos (dip.RadiansFromDegrees())
                heightOverHorizontal          = horizontalHeight
                heightOverPlaneThickness_true = tiltedHeight
            }        

        Log.line "[HeightValidator] %A" res

        res