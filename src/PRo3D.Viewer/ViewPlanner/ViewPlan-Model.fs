namespace PRo3D.ViewPlan

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Primitives
open Aardvark.UI
open ViewPlanner
open FShade.Intrinsics
open Aardvark.Base.CameraView

open Chiron
open PRo3D.Rover


[<ModelType>]
type ViewPlan = {
    [<PrimaryKey>]
    id                  : Guid
    name                : string
    position            : V3d
    lookAt              : V3d
    viewerState         : CameraControllerState
    vectorsVisible      : bool
    rover               : Rover
    roverTrafo          : Trafo3d
    isVisible           : bool
    selectedInstrument  : option<Instrument>
    selectedAxis        : option<Axis>
    currentAngle        : NumericInput
}

[<ModelType>]
type ViewPlanModel = {
    viewPlans           : HashSet<ViewPlan>
    selectedViewPlan    : Option<ViewPlan>
    working             : list<V3d> // pos + lookAt
    roverModel          : RoverModel
    instrumentCam       : CameraControllerState
    instrumentFrustum   : Frustum
    //footPrint           : FootPrint 
    }

module ViewPlanModel =
    let currentAngle = 
        {
            value = 0.0
            min =  0.0
            max = 90.0
            step = 0.1
            format = "{0:0.0}"
        }

    let initRoverModel = {
        rovers = hmap.Empty
        platforms = hmap.Empty
        selectedRover = None
        //selectedInstrument = None
        //selectedAxis = None
        //currentAngle = currentAngle
        }
        
    let initial = {
        viewPlans         = hset.Empty
        selectedViewPlan  = None
        working           = list.Empty
        roverModel        = initRoverModel
        instrumentCam     = { CameraController.initial with view = CameraView.lookAt V3d.Zero V3d.One V3d.OOI }        
        instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0
        //footPrint = initFootPrint
    }

