namespace PRo3D.SimulatedViews

open System
open System.IO

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI
open IPWrappers
open FShade.Intrinsics
open Aardvark.Base.CameraView

open PRo3D.Base
open Chiron

open Adaptify


type Intrinsics = {
    horizontalFieldOfView           : double
    verticalFieldOfView             : double
    horizontalResolution            : uint32
    verticalResolution              : uint32
    horizontalPrinciplePoint        : double
    verticalPrinciplePoint          : double
    horizontalFocalLengthPerPixel   : double
    verticalFocalLengthPerPixel     : double
    horizontalDistortionMap         : string
    verticalDistortionMap           : string
    vignettingMap                   : string    
}

[<ModelType>]
type Extrinsics = {
    position    : V3d
    camUp       : V3d
    camLookAt   : V3d
    box         : Box3d
}

module Extrinsics =
    let transformed (t:M44d) (ex:Extrinsics)=
        {
            position = t.TransformPos ex.position
            camUp    = t.TransformDir ex.camUp
            camLookAt= t.TransformDir ex.camLookAt
            box      = ex.box.Transformed t
        }

type InstrumentType = 
| WACL       // Left Wide Angle Camera (PanCam)        
| WACR       // Right Wide Angle Camera (PanCam)
| HRC        // High Resolution Camera
| WISDOM     // penetrating radar
| CLUPI      // Close UP Imager microscope
| ISEM       // Infrared Spectrometer
| DRILL      // Drill
| RIM        // Rover Inspection Mirror
| Undefined

type CameraViewLean = { 
    location : V3d
    forward  : V3d
    sky      : V3d
}

module CameraViewLean =
    let fromCameraView (view : CameraView) : CameraViewLean = 
        {
            location = view.Location
            forward  = view.Forward
            sky      = view.Sky
        }

    let toCameraView (c : CameraViewLean) : CameraView = 
        CameraView.lookAt c.location (c.location + c.forward) c.sky  

[<ModelType>]
type Instrument = {
    id                      : string
    iType                   : InstrumentType
    calibratedFocalLengths  : list<double>
    //currentFocalLength      : double
    focal                   : NumericInput
    intrinsics              : Intrinsics
    extrinsics              : Extrinsics   
    index                   : int
}

type AxisAngleUpdate = {
    roverId : string
    axisId  : string
    angle   : double
}

type InstrumentFocusUpdate = {
    roverId       : string
    instrumentId  : string
    focal         : double
}

[<ModelType>]
type Axis = {
    id           : string
    description  : string
    startPoint   : V3d
    endPoint     : V3d
    //minAngle     : double
    //maxAngle     : double
    //currentAngle : double

    index        : int
    angle        : NumericInput
}

[<ModelType>]
type Rover = {
    id               : string
    platform2Ground  : M44d
    wheelPositions   : list<V3d>
    instruments      : HashMap<string, Instrument>
    axes             : HashMap<string, Axis>
    box              : Box3d   
}

[<ModelType>]
type RoverModel = {
    rovers             : HashMap<string, Rover>
    platforms          : HashMap<string, IPWrappers.ViewPlanner.SPlatform>
    selectedRover      : option<Rover>
    //selectedInstrument : option<Instrument>
    //selectedAxis       : option<Axis>
    //currentAngle       : NumericInput
}

type XMLScheme = {
    xmlType    : string
    version     : float
}
with 
  static member current = 0
  static member ToJson (x : XMLScheme) =
    json {
      do! Json.write      "xmlType"    x.xmlType
      do! Json.write      "version"    x.version
    }

type FileInfo = {
    fileType    : string
    path        : string
    name        : string
}
with 
  static member current = 0
  static member ToJson (x : FileInfo) =
    json {
      do! Json.write      "fileType"    x.fileType
      do! Json.write      "path"        x.path
      do! Json.write      "name"        x.name
    }

type Calibration = {
    instrumentPlatformXmlFileName       : string
    instrumentPlatformXmlFileVersion    : float
}
with 
  static member current = 0
  static member ToJson (x : Calibration) =
    json {
      do! Json.write      "instrumentPlatformXmlFileName"      x.instrumentPlatformXmlFileName
      do! Json.write      "instrumentPlatformXmlFileVersion"   x.instrumentPlatformXmlFileVersion
    }

type RoverInfo = {
    position        : V3d
    lookAtPosition  : V3d
    placementTrafo  : Trafo3d
}
with 
  static member current = 0
  static member ToJson (x : RoverInfo) =
    json {
      do! Json.write      "position"         (x.position.ToString())
      do! Json.write      "lookAtPosition"   (x.lookAtPosition.ToString())
      do! Json.write      "placementTrafo"   (x.placementTrafo.ToString())
    }

type Angles = {
    panAxis     : double
    tiltAxis    : double
}
with 
  static member current = 0
  static member ToJson (x : Angles) =
    json {
      do! Json.write      "panAxis"    x.panAxis
      do! Json.write      "tiltAxis"   x.tiltAxis
    }

type ReferenceFrameInfo = {
    name            : string
    parentFrameName : string
}
with 
  static member current = 0
  static member ToJson (x : ReferenceFrameInfo) =
    json {
      do! Json.write      "name"              x.name
      do! Json.write      "parentFrameName"   x.parentFrameName
    }

type InstrumentInfo = {
    camIdentifier       : string
    angles              : Angles
    focalLength         : double
    referenceFrameInfo  : ReferenceFrameInfo
}
with 
  static member current = 0
  static member ToJson (x : InstrumentInfo) =
    json {
      do! Json.write      "camIdentifier"       x.camIdentifier
      do! Json.write      "angles"              x.angles
      do! Json.write      "focalLength"         x.focalLength
      do! Json.write      "referenceFrameInfo"  x.referenceFrameInfo
    }

type SurfaceInfoData = {
    opcId   : Guid
    layers  : List<string>
}
with 
  static member current = 0
  static member ToJson (x : SurfaceInfoData) =
    json {
      do! Json.write      "opcId"    x.opcId
      do! Json.write      "layers"   x.layers
    }


type Acquisition = {
    roverInfo           : RoverInfo
    instrumentInfo      : InstrumentInfo
    //surfaceInfoData     : SurfaceInfoData
    //renderingInfo       : RenderingInfo
}
with 
  static member current = 0
  static member ToJson (x : Acquisition) =
    json {
      do! Json.write      "roverInfo"       x.roverInfo
      do! Json.write      "instrumentInfo"  x.instrumentInfo
    }

[<ModelType>]
type SimulatedViewData = {
    //xmlScheme   : XMLScheme
    fileInfo    : FileInfo
    calibration : Calibration
    acquisition : Acquisition
}
with 
  static member current = 0
  static member ToJson (x : SimulatedViewData) =
    json {
      //do! Json.write      "xmlScheme"    x.xmlScheme
      do! Json.write      "fileInfo"     x.fileInfo
      do! Json.write      "calibration"  x.calibration
      do! Json.write      "acquisition"  x.acquisition
    }

[<ModelType>]
type FootPrint = {
    vpId                : option<Guid>
    isVisible           : bool
    projectionMatrix    : M44d
    instViewMatrix      : M44d
    projTex             : ITexture
    globalToLocalPos    : V3d
}

[<ModelType>]
type ViewPlan = {
    [<NonAdaptive>]
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
    viewPlans           : HashMap<Guid,ViewPlan>
    selectedViewPlan    : Option<ViewPlan>
    working             : list<V3d> // pos + lookAt
    roverModel          : RoverModel
    instrumentCam       : CameraView
    instrumentFrustum   : Frustum
    footPrint           : FootPrint 
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
        rovers = HashMap.Empty
        platforms = HashMap.Empty
        selectedRover = None
        //selectedInstrument = None
        //selectedAxis = None
        //currentAngle = currentAngle
        }
    
    let initPixTex = 
        let res = V2i((int)1024, (int)1024)
        let pi = PixImage<byte>(Col.Format.RGBA, res)
        pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
        PixTexture2d(PixImageMipMap [| (pi.ToPixImage(Col.Format.RGBA)) |], true) :> ITexture

    let initFootPrint = {
        vpId                = None
        isVisible           = false
        projectionMatrix    = M44d.Identity
        instViewMatrix    = M44d.Identity
        projTex             = initPixTex
        globalToLocalPos    = V3d.OOO
    }

    let initial = {
        viewPlans         = HashMap.Empty
        selectedViewPlan  = None
        working           = list.Empty
        roverModel        = initRoverModel
        instrumentCam     = CameraView.lookAt V3d.Zero V3d.One V3d.OOI
        instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0
        footPrint         = initFootPrint
    }

  
