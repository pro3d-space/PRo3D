namespace PRo3D.Core

open System

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Base.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering.Text

open Aardvark.UI
open Aardvark.UI.Primitives    

open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

open FShade

open Adaptify.FSharp.Core
open PRo3D.Base

module HeightComputations =

    let computePlane (p1:V3d) (p2:V3d) (up:V3d) (angle : float) =

        let dip = (p2 - p1).Normalized
        
        let strike = up.Cross dip
        
        let rotation     = Trafo3d.Rotation(strike, angle.RadiansFromDegrees())
        let tiltedUp     = rotation.Forward.TransformDir(up)
        new Plane3d(tiltedUp, p1)

    let updateValidator v (a : Numeric.Action) =
    
        let inclination = Numeric.update v.inclination a

        let tiltedPlane = computePlane v.upper v.lower v.upVector inclination.value

        Log.line "updating plane normal %A" tiltedPlane.Normal
    
        { v with inclination = inclination; tiltedPlane = tiltedPlane }

    let computeResult (validator : Validator) : ValidatorResult =    
        
        let up    = validator.upVector
        let dip   = validator.inclination.value
        let pos   = validator.location

        let lower = validator.lower
        let upper = validator.upper
       
        let cooHeightPos = PRo3D.Base.CooTransformation.getElevation' Planet.Mars lower
        let cooHeightUpper = PRo3D.Base.CooTransformation.getElevation' Planet.Mars upper

        let geographic = cooHeightUpper - cooHeightPos

        //vertical height over plane
        let horizontal = new Plane3d(up,lower)
        let horizontalHeight = horizontal.Height(upper)
        
        //true height over plane
        Log.line "plane %A" validator.tiltedPlane
        let tiltedHeight = validator.tiltedPlane.Height(upper)
        
        let res =
            {
                pointDistance = Vec.distance pos upper
                cooTrafoThickness_geographic = geographic
                cooTrafoThickness_true = geographic * cos (dip.RadiansFromDegrees())
                heightOverHorizontal = horizontalHeight
                heightOverPlaneThickness_true = tiltedHeight
            }        

        Log.line "[HeightValidator] %A" res

        res

    let createValidator (p1:V3d) (p2:V3d) (up:V3d) (north : V3d) (angle : float) =
        
        let tiltedPlane = computePlane p1 p2 up angle

        {
            location    = p1
            lower       = p2
            upper       = p1 // + (north * 2.0) + (up)
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