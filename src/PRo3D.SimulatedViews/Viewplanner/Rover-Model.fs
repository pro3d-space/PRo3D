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
open Aardvark.Rendering

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

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Intrinsics =    
    
    let current = 0   
    let read0 =
        json {
            let! horizontalFieldOfView            = Json.read "horizontalFieldOfView"
            let! verticalFieldOfView              = Json.read "verticalFieldOfView"
            let! horizontalResolution             = Json.read "horizontalResolution"
            let! verticalResolution               = Json.read "verticalResolution"
            let! horizontalPrinciplePoint         = Json.read "horizontalPrinciplePoint"
            let! verticalPrinciplePoint           = Json.read "verticalPrinciplePoint"
            let! horizontalFocalLengthPerPixel    = Json.read "horizontalFocalLengthPerPixel"
            let! verticalFocalLengthPerPixel      = Json.read "verticalFocalLengthPerPixel"
            let! horizontalDistortionMap          = Json.read "horizontalDistortionMap"
            let! verticalDistortionMap            = Json.read "verticalDistortionMap"
            let! vignettingMap                    = Json.read "vignettingMap"
            
            return 
                {
                   // version       = current
                    horizontalFieldOfView       = horizontalFieldOfView
                    verticalFieldOfView         = verticalFieldOfView
                    horizontalResolution        = horizontalResolution
                    verticalResolution          = verticalResolution
                    horizontalPrinciplePoint    = horizontalPrinciplePoint
                    verticalPrinciplePoint      = verticalPrinciplePoint
                    horizontalFocalLengthPerPixel   = horizontalFocalLengthPerPixel
                    verticalFocalLengthPerPixel     = verticalFocalLengthPerPixel
                    horizontalDistortionMap         = horizontalDistortionMap
                    verticalDistortionMap           = verticalDistortionMap
                    vignettingMap                   = vignettingMap
                }
        }

type Intrinsics with
    static member FromJson(_ : Intrinsics) =
        json {
            //let! v = Json.read "version"
            //match v with 
            //| 0 -> return! Intrinsics.read0
            //| _ -> 
            //    return! v 
            //    |> sprintf "don't know version %A  of Intrinsics"
            //    |> Json.error
            return! Intrinsics.read0
        }
    static member ToJson(x : Intrinsics) =
        json {
            //do! Json.write "version" x.version
            do! Json.write "horizontalFieldOfView" x.horizontalFieldOfView
            do! Json.write "verticalFieldOfView" x.verticalFieldOfView
            do! Json.write "horizontalResolution" x.horizontalResolution
            do! Json.write "verticalResolution" x.verticalResolution
            do! Json.write "horizontalPrinciplePoint" x.horizontalPrinciplePoint    
            do! Json.write "verticalPrinciplePoint" x.verticalPrinciplePoint
            do! Json.write "horizontalFocalLengthPerPixel" x.horizontalFocalLengthPerPixel
            do! Json.write "verticalFocalLengthPerPixel" x.verticalFocalLengthPerPixel
            do! Json.write "horizontalDistortionMap" x.horizontalDistortionMap
            do! Json.write "verticalDistortionMap" x.verticalDistortionMap    
            do! Json.write "vignettingMap" x.vignettingMap
        }

[<ModelType>]
type Extrinsics = {
    //version         : int

    position    : V3d
    camUp       : V3d
    camLookAt   : V3d
    box         : Box3d
}

module Extrinsics =
    let current = 0   
    let transformed (t:M44d) (ex:Extrinsics)=
        {
            //version  = ex.version
            position = t.TransformPos ex.position
            camUp    = t.TransformDir ex.camUp
            camLookAt= t.TransformDir ex.camLookAt
            box      = ex.box.Transformed t
        }

    let read0 =
        json {
            let! position   = Json.read "position"
            let! camUp      = Json.read "camUp"
            let! camLookAt  = Json.read "camLookAt" 
            let! box        = Json.read "box"
            
            return 
                {
                    //version     = current
                    position    = position |> V3d.Parse
                    camUp       = camUp |> V3d.Parse
                    camLookAt   = camLookAt |> V3d.Parse
                    box         = box |> Box3d.Parse
                }
        }

type Extrinsics with
    static member FromJson(_ : Extrinsics) =
        json {
            //let! v = Json.read "version"
            //match v with 
            //| 0 -> return! Extrinsics.read0
            //| _ -> 
            //    return! v 
            //    |> sprintf "don't know version %A  of Extrinsics"
            //    |> Json.error
            return! Extrinsics.read0
        }
    static member ToJson(x : Extrinsics) =
        json {
            //do! Json.write "version" x.version
            do! Json.write "position" (x.position.ToString())
            do! Json.write "camUp" (x.camUp.ToString())
            do! Json.write "camLookAt" (x.camLookAt.ToString())
            do! Json.write "box" (x.box.ToString())
        }

type InstrumentType = 
    | WACL     = 0   // Left Wide Angle Camera (PanCam)        
    | WACR     = 1   // Right Wide Angle Camera (PanCam)
    | HRC      = 2   // High Resolution Camera
    | WISDOM   = 3   // penetrating radar
    | CLUPI    = 4   // Close UP Imager microscope
    | ISEM     = 5   // Infrared Spectrometer
    | DRILL    = 6  // Drill
    | RIM      = 7  // Rover Inspection Mirror
    | Undefined = 8

//type ArnoldSnapshot = {
//    location      : V3d
//    forward       : V3d
//    up            : V3d
//    filename      : string
//}
//with 
//  static member current = 0
//  static member private readV0 = 
//      json {
//        let! location    = Json.read "location"
//        let! forward     = Json.read "forward"
//        let! up          = Json.read "up"
//        let! filename    = Json.read "filename"
  
//        return {
//          location    = location |> V3d.Parse
//          forward     = forward  |> V3d.Parse
//          up          = up       |> V3d.Parse
//          filename    = filename
//        }
//      }

//  static member FromJson(_ : ArnoldSnapshot) = 
//    json {
//        return! ArnoldSnapshot.readV0
//        //let! v = Json.read "version"
//        //match v with            
//        //  | 0 -> return! ArnoldSnapshot.readV0
//        //  | _ -> return! v |> sprintf "don't know version %A  of ArnoldSnapshot" |> Json.error
//    }
//  static member ToJson (x : ArnoldSnapshot) =
//    json {
//      do! Json.write      "location"  (x.location.ToString())
//      do! Json.write      "forward"   (x.forward.ToString())
//      do! Json.write      "up"        (x.up.ToString())
//      do! Json.write      "filename"  (x.filename.ToString())
//    }

//type ArnoldAnimation = {
//    fieldOfView   : double
//    resolution    : V2i
//    snapshots     : list<ArnoldSnapshot>
//}
//with 
//  static member current = 0
//  static member private readV0 = 
//      json {
//        let! fieldOfView    = Json.read "fieldOfView"
//        let! resolution     = Json.read "resolution"
//        let! snapshots      = Json.read "snapshots"

//        //let snapshots' = snapshots |> List.map 
  
//        return {
//          fieldOfView    = fieldOfView
//          resolution     = resolution |> V2i.Parse
//          snapshots      = snapshots  //|> Serialization.jsonSerializer.UnPickleOfString
//        }
//      }

//  static member FromJson(_ : ArnoldAnimation) = 
//    json {
//        let! v = Json.read "version"
//        match v with            
//          | 0 -> return! ArnoldAnimation.readV0
//          | _ -> return! v |> sprintf "don't know version %A  of ArnoldAnimation" |> Json.error
//    }
//  static member ToJson (x : ArnoldAnimation) =
//    json {
//        do! Json.write      "version"        0
//        do! Json.write      "fieldOfView"  (x.fieldOfView)
//        do! Json.write      "resolution"   (x.resolution.ToString())
//        do! Json.write      "snapshots"    (x.snapshots)
//    }

[<ModelType>]
type Instrument = {
    //version                 : int
    id                      : string
    iType                   : InstrumentType
    calibratedFocalLengths  : list<double>
    //currentFocalLength      : double
    focal                   : NumericInput
    intrinsics              : Intrinsics
    extrinsics              : Extrinsics   
    index                   : int
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Instrument =    
    
    let current = 0   
    let read0 =
        json {
            let! id            = Json.read "id"
            let! iType         = Json.read "iType"
            let! calibratedFocalLengths = Json.read "calibratedFocalLengths"
            let! focal         = Json.readWith Ext.fromJson<NumericInput,Ext> "focal"
            let! intrinsics    = Json.read "intrinsics"
            let! extrinsics    = Json.read "extrinsics"
            let! index         = Json.read "index"
            
            return 
                {
                    //version       = current
                    id            = id
                    iType         = iType |> enum<InstrumentType>
                    calibratedFocalLengths      = calibratedFocalLengths
                    focal         = focal
                    intrinsics    = intrinsics
                    extrinsics    = extrinsics
                    index          = index
                }
        }

type Instrument with
    static member FromJson(_ : Instrument) =
        json {
            //let! v = Json.read "version"
            //match v with 
            //| 0 -> return! Instrument.read0
            //| _ -> 
            //    return! v 
            //    |> sprintf "don't know version %A  of Instrument"
            //    |> Json.error
            return! Instrument.read0
        }
    static member ToJson(x : Instrument) =
        json {
            //do! Json.write "version" x.version
            do! Json.write "id" x.id
            do! Json.write "iType" (x.iType |> int)
            do! Json.write "calibratedFocalLengths" x.calibratedFocalLengths
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "focal" x.focal
            do! Json.write "intrinsics" x.intrinsics
            do! Json.write "extrinsics" x.extrinsics    
            do! Json.write "index" x.index
        }

type AxisAngleUpdate = {
    roverId : string
    axisId  : string
    shiftedAngle : bool  // whether angle was shifted from [0,360] to [0,180]
    invertedAngle : bool // whether angle should be artifically negated
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

    index        : int
    angle        : NumericInput

    degreesMapped  : bool
    degreesNegated : bool
}

module Axis =
    let current = 0
    let read0 =
        json {
            let! id             = Json.read "id"
            let! description    = Json.read "description"
            let! startPoint     = Json.read "startPoint"
            let! endPoint       = Json.read "endPoint"
            let! index          = Json.read "index"
            let! angle          = Json.readWith Ext.fromJson<NumericInput,Ext> "angle"
            let! degreesMapped  = Json.read "degreesMapped"
            let! degreesNegated = Json.read "degreesNegated"

            return 
                {
                    //version         = current
                    id              = id
                    description     = description
                    startPoint      = startPoint |> V3d.Parse
                    endPoint        = endPoint |> V3d.Parse
                    index           = index
                    angle           = angle
                    degreesMapped   = degreesMapped
                    degreesNegated  = degreesNegated
                }

            }

    module Mapping =
        let to180 (min : float) (max : float) (v : float) = 
            if Fun.ApproximateEquals(min, 0.0) && Fun.ApproximateEquals(max, 360.0) then 
                if v > 180.0 then -(360.0-v)
                else v
            else 
                v

        let from180 (min : float) (max : float) (v : float) =
            if Fun.ApproximateEquals(min, 0.0) && Fun.ApproximateEquals(max, 360.0) then 
                if v < 0.0 then 360.0 + v
                else v
            else 
                v

    open Mapping

    let to180 (v : Axis) =
        if v.degreesMapped then
            let angle = to180 v.angle.min v.angle.max v.angle.value
            { v with angle = { v.angle with value = (if v.degreesNegated then -angle else angle); min = -180.0; max = 180.0 } }
        else 
            v

type Axis with
    static member FromJson(_ : Axis) =
        json {
            //let! v = Json.read "version"
            //match v with 
            //| 0 -> return! Axis.read0
            //| _ -> 
            //    return! v 
            //    |> sprintf "don't know version %A  of Axis"
            //    |> Json.error
            return! Axis.read0
        }
    static member ToJson(x : Axis) =
        json {
            //do! Json.write "version" x.version
            do! Json.write "id" x.id
            do! Json.write "description" x.description
            do! Json.write "startPoint" (x.startPoint.ToString())
            do! Json.write "endPoint" (x.endPoint.ToString())
            do! Json.write "index" x.index
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "angle" x.angle
            do! Json.write "degreesMapped" x.degreesMapped  
            do! Json.write "degreesNegated" x.degreesNegated
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

module Rover = 
    let current = 0   
    let read0 =
        json {
            let! id                 = Json.read "id"
            let! platform2Ground    = Json.read "platform2Ground"
            let! wheelPositions     = Json.readWith Ext.fromJson<list<V3d>,Ext> "wheelPositions"
            let! inst               = Json.read "instruments"
            let instruments = inst |> List.map(fun (a : Instrument) -> (a.id, a)) |> HashMap.ofList
            let! axes               = Json.read "axes"
            let axes = axes |> List.map(fun (a : Axis) -> (a.id, a)) |> HashMap.ofList
            let! box                = Json.read "box"

            return 
                {
                    //version             = current
                    id                  = id
                    platform2Ground     = platform2Ground |> M44d.Parse
                    wheelPositions      = wheelPositions
                    instruments         = instruments
                    axes                = axes
                    box                 = box |> Box3d.Parse
                }

            }

type Rover with
    static member FromJson(_ : Rover) =
        json {
            //let! v = Json.read "version"
            //match v with 
            //| 0 -> return! Rover.read0
            //| _ -> 
            //    return! v 
            //    |> sprintf "don't know version %A  of Rover"
            //    |> Json.error
            return! Rover.read0
        }
    static member ToJson(x : Rover) =
        json {
            //do! Json.write "version" x.version
            do! Json.write "id" x.id
            do! Json.write "platform2Ground" (x.platform2Ground.ToString())
            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "wheelPositions" x.wheelPositions
            do! Json.write "instruments" (x.instruments |> HashMap.toList |> List.map snd)
            do! Json.write "axes" (x.axes |> HashMap.toList |> List.map snd)
            do! Json.write "box" (x.box.ToString()) 
        }

[<ModelType>]
type RoverModel = {
    rovers             : HashMap<string, Rover>
    platforms          : HashMap<string, JR.InstrumentPlatforms.SPlatform>
    selectedRover      : option<Rover>
    //selectedInstrument : option<Instrument>
    //selectedAxis       : option<Axis>
    //currentAngle       : NumericInput
}

module RoverModel =
    let initial = {
        rovers = HashMap.Empty
        platforms = HashMap.Empty
        selectedRover = None
        //selectedInstrument = None
        //selectedAxis = None
        //currentAngle = currentAngle
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
    depthTexture        : option<IBackendTexture>
    isDepthVisible      : bool
    depthColorLegend    : FalseColorsModel
}


[<ModelType>]
type ViewPlan = {
    version             : int
    [<NonAdaptive>]
    id                  : Guid
    name                : string
    position            : V3d
    lookAt              : V3d
    viewerState         : CameraView
    vectorsVisible      : bool
    rover               : Rover
    roverTrafo          : Trafo3d
    isVisible           : bool
    selectedInstrument  : option<Instrument>
    selectedAxis        : option<Axis>
    currentAngle        : NumericInput    
}

module ViewPlan =
    let current = 0

    let initialAngle = {
        value = 0.0
        min =  0.0
        max = 90.0
        step = 0.1
        format = "{0:0.0}"
    }

    let read0 =
        json {
            let! id             = Json.read "id"
            let! name           = Json.read "name"
            let! position       = Json.read "position"
            let! lookAt         = Json.read "lookAt"

            let! (viewerState : list<string>) = Json.read "viewerState"
            let viewerState = viewerState |> List.map V3d.Parse
            let viewerState = CameraView(viewerState.[0],viewerState.[1],viewerState.[2],viewerState.[3], viewerState.[4])

            let! vectorsVisible = Json.read "vectorsVisible"
            let! rover          = Json.read "rover"
            let! roverTrafo     = Json.read "roverTrafo"
            let! isVisible      = Json.read "isVisible"

            let! selectedInstrument  = Json.read "selectedInstrument"
            let! selectedAxis        = Json.read "selectedAxis"
            let! currentAngle   = Json.readWith Ext.fromJson<NumericInput,Ext> "currentAngle"

            return 
                {
                    version         = current
                    id            = id |> Guid
                    name            = name

                    position    = position |> V3d.Parse
                    lookAt      = lookAt |> V3d.Parse
                    viewerState = viewerState
                    rover       = rover
                    roverTrafo  = roverTrafo |> Trafo3d.Parse

                    isVisible   = isVisible

                    vectorsVisible = vectorsVisible

                    selectedInstrument = selectedInstrument
                    selectedAxis       = selectedAxis

                    currentAngle = currentAngle
                }
        }

type ViewPlan with
    static member FromJson(_ : ViewPlan) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! ViewPlan.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of ViewPlan"
                |> Json.error
        }
    static member ToJson(x : ViewPlan) =
        json {
            do! Json.write "version" x.version
            do! Json.write "id" x.id
            do! Json.write "name" x.name
            do! Json.write "position" (x.position.ToString())
            do! Json.write "lookAt" (x.lookAt.ToString())
            let camView = x.viewerState
            let camView = 
                [camView.Sky; camView.Location; camView.Forward; camView.Up ; camView.Right] 
                |> List.map(fun x -> x.ToString())
            do! Json.write "viewerState" camView

            do! Json.write "rover" x.rover
            do! Json.write  "roverTrafo"   (x.roverTrafo.ToString())
            do! Json.write "isVisible" x.isVisible 
            do! Json.write "vectorsVisible" x.vectorsVisible 

            do! Json.write "selectedInstrument" x.selectedInstrument
            do! Json.write "selectedAxis" x.selectedAxis  
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "currentAngle" x.currentAngle
        }

    //let initial = {
    //    version = current
    //    id                  = Guid.Empty
    //    name                = ""
    //    position            = V3d.NaN
    //    lookAt              = V3d.NaN
    //    viewerState         = CameraView.ofTrafo Trafo3d.Identity
    //    vectorsVisible      = false
    //    rover               = ...
    //    roverTrafo          = Trafo3d.Identity
    //    isVisible           = false
    //    selectedInstrument  = None
    //    selectedAxis        = None
    //    currentAngle        = initialAngle
    
    //}
    

[<ModelType>]
type ViewPlanModel = {
    version             : int
    viewPlans           : HashMap<Guid,ViewPlan>
    selectedViewPlan    : Option<Guid>
    working             : list<V3d> // pos + lookAt
    roverModel          : RoverModel
    instrumentCam       : CameraView
    instrumentFrustum   : Frustum
    footPrint           : FootPrint
    
}

module ViewPlanModel = 
    let current = 1 
           
    let initPixTex = 
        let res = V2i((int)1024, (int)1024)
        let pi = PixImage<byte>(Col.Format.RGBA, res)
        pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
        PixTexture2d(PixImageMipMap [| (pi.ToPixImage(Col.Format.RGBA)) |], true) :> ITexture

    let initFootPrint = {
        vpId                = None
        isVisible           = false
        projectionMatrix    = M44d.Identity
        instViewMatrix      = M44d.Identity
        projTex             = initPixTex
        globalToLocalPos    = V3d.OOO
        depthTexture        = None
        isDepthVisible      = false
        depthColorLegend    = FalseColorsModel.initDepthLegend
    }

    let initial = {
        version           = current
        viewPlans         = HashMap.Empty
        selectedViewPlan  = None
        working           = list.Empty
        roverModel        = RoverModel.initial
        instrumentCam     = CameraView.lookAt V3d.Zero V3d.One V3d.OOI
        instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0
        footPrint         = initFootPrint        
    }

    let readV0 = 
        json {                           

            //let! viewPlans       = Json.read "viewPlans"            

            return {
                version           = current
                viewPlans         = HashMap.empty//viewPlans |> HashMap.ofList 
                selectedViewPlan  = None
                working           = list.Empty
                roverModel        = RoverModel.initial
                instrumentCam     = CameraView.lookAt V3d.Zero V3d.One V3d.OOI
                instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0
                footPrint         = initFootPrint                
            }
        }    

    let readV1 = 
        json {                           

            let! viewPlans = Json.read "viewPlans"
            let viewPlans = viewPlans |> List.map(fun (a : ViewPlan) -> (a.id, a)) |> HashMap.ofList

            return {
                version           = current
                viewPlans         = viewPlans 
                selectedViewPlan  = None
                working           = list.Empty
                roverModel        = RoverModel.initial
                instrumentCam     = CameraView.lookAt V3d.Zero V3d.One V3d.OOI
                instrumentFrustum = Frustum.perspective 60.0 0.1 10000.0 1.0
                footPrint         = initFootPrint                
            }
        }    

type ViewPlanModel with
    static member FromJson(_:ViewPlanModel) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ViewPlanModel.readV0
            | 1 -> return! ViewPlanModel.readV1
            | _ -> return! v |> sprintf "don't know version %d of ViewPlanModel" |> Json.error
        }
    static member ToJson (x : ViewPlanModel) =
        json {
            do! Json.write "version"             x.version
            do! Json.write "viewPlans"       (x.viewPlans |> HashMap.toList |> List.map snd)
        }

module FootPrint = 
        
    let getDataPath (scenePath:string) (dirName:string) =
        let path = Path.GetDirectoryName scenePath
        let fpPath = Path.combine [path;dirName]
        if not (Directory.Exists fpPath) then Directory.CreateDirectory(fpPath) |> ignore
        fpPath

    let initNewDepthImage 
        (runtimeInstance: IRuntime) 
        (vp:ViewPlanModel) 
        (scenePath:string) =

        let fpPath = getDataPath scenePath "DepthData"

        match vp.selectedViewPlan with
        | Some id -> 
            let selectedVp = vp.viewPlans |> HashMap.find id
            let now = DateTime.Now
            let roverName = selectedVp.rover.id
            let width, height =
                match selectedVp.selectedInstrument with
                | Some i -> 
                    let horRes = i.intrinsics.horizontalResolution/uint32(2)
                    let vertRes = i.intrinsics.verticalResolution/uint32(2)
                    int(horRes), int(vertRes)
                | None -> 
                    512, 512
            //let fovH = frustum |> Frustum.horizontalFieldOfViewInDegrees
            //let asp = frustum |> Frustum.aspect
            //let fovV = Math.Round((fovH / asp), 0)

            let resolution = V3i (width, height, 1)
            let depth = runtimeInstance.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.Depth24Stencil8, 1, 8);

            //let signature = 
            //    runtimeInstance.CreateFramebufferSignature [
            //    DefaultSemantic.Depth, { format = RenderbufferFormat.Depth24Stencil8; samples = 1 }
            //    ]

            //let fbo = 
            //    runtimeInstance.CreateFramebuffer(
            //        signature, 
            //        Map.ofList [
            //            DefaultSemantic.Depth, depth.GetOutputView()
            //        ]
            //    )
        
            //let description = fbo |> OutputDescription.ofFramebuffer
            //let projTrafo  = Frustum.projTrafo(frustum)

            //let render2TextureSg =
            //    renderSg
            //    |> Sg.viewTrafo vT
            //    |> Sg.projTrafo (Mod.constant projTrafo)
            //    |> Sg.effect [
            //        toEffect DefaultSurfaces.trafo 
            //        toEffect DefaultSurfaces.diffuseTexture
            //    ]

            //let vR = float res.Y
            //let pixelSizeNear = pixelSizeCm frustum.near vR fovV
            //let pixelSizeFar = pixelSizeCm frustum.far vR fovV

            //let mat = Matrix<float32>(int64 size.X, int64 size.Y)

            //let task : IRenderTask =  runtimeInstance.CompileRender(signature, render2TextureSg)
            //let taskclear : IRenderTask = runtimeInstance.CompileClear(signature,Mod.constant C4f.Black,Mod.constant 1.0)
            //let realTask = RenderTask.ofList [taskclear; task]
            { vp with footPrint = {vp.footPrint with depthTexture = Some depth}}
        | None -> vp

        
       
    let createFootprintData (vp:ViewPlanModel) (scenePath:string) =

        let fpPath = getDataPath scenePath "FootPrints"
        if (not (Directory.Exists fpPath)) then 
            Directory.CreateDirectory fpPath |> ignore

        match vp.selectedViewPlan with
        | Some id -> 
            let selectedVp = vp.viewPlans |> HashMap.find id
            let now = DateTime.Now
            let roverName = selectedVp.rover.id
            let instrumentName = 
                match selectedVp.selectedInstrument with
                | Some i -> i.id
                | None -> ""
                       
            let pngName = 
                System.String.Format(
                    "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}_{6}_{7}",
                    now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, roverName, instrumentName
                )

            let svxName = 
                System.String.Format(
                    "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}_{6}_{7}.svx",
                    now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, roverName, instrumentName
                )

            let width, height =
                match selectedVp.selectedInstrument with
                | Some i -> 
                    let horRes = i.intrinsics.horizontalResolution/uint32(2)
                    let vertRes = i.intrinsics.verticalResolution/uint32(2)
                    int(horRes), int(vertRes)
                | None -> 
                    512, 512
            // save png file
            try Utilities.takeScreenshotFromAllViews "http://localhost:54322" width height pngName fpPath ".png" 4 with e -> printfn "error: %A" e //"http://localhost:54322"
           
            let fileInfo = {
                fileType = "PNGImage"
                path = fpPath
                name = pngName
            }

            let calibration = {
                instrumentPlatformXmlFileName       = selectedVp.rover.id + ".xml"
                instrumentPlatformXmlFileVersion    = 1.0
            }

            let roverInfo = {
                position = selectedVp.position
                lookAtPosition = selectedVp.lookAt
                placementTrafo = selectedVp.roverTrafo
            }

            let panAx = selectedVp.rover.axes.TryFind "Pan Axis" |> Option.map(fun x -> x.angle.value )
            let panVal = match panAx with | Some av -> av | None -> 1.0

            let tiltAx = selectedVp.rover.axes.TryFind "Tilt Axis" |> Option.map(fun x -> x.angle.value )
            let tiltVal = match tiltAx with | Some av -> av | None -> 1.0
            let angles = {
                panAxis = panVal
                tiltAxis = tiltVal
            }

            let focal =
                match selectedVp.selectedInstrument with
                | Some i -> i.focal.value
                | None -> 1.0

            let referenceFrameInfo = {
                name = "Ground"
                parentFrameName = ""
            }

            let instrumentinfo = {
                camIdentifier       = instrumentName
                angles              = angles
                focalLength         = focal
                referenceFrameInfo  = referenceFrameInfo
            }

            let acquisition = {
                roverInfo       = roverInfo
                instrumentInfo  = instrumentinfo
            }

            let simulatedViewData =
                {
                    fileInfo    = fileInfo
                    calibration = calibration
                    acquisition = acquisition
                }
            //Serialization.save (Path.Combine(fpPath, svxName)) simulatedViewData |> ignore
            let json = 
                simulatedViewData 
                |> Json.serialize 
                |> Json.formatWith JsonFormattingOptions.Pretty 
                |> Serialization.writeToFile (Path.Combine(fpPath, svxName))
            //Serialization.writeToFile (Path.Combine(fpPath, svxName)) json 
            vp
        | None -> vp
    
    let updateFootprint (instrument:Instrument) (roverpos:V3d) (model:ViewPlanModel) =
        
        let res = V2i((int)instrument.intrinsics.horizontalResolution, (int)instrument.intrinsics.verticalResolution)
        //let image = PixImage<byte>(Col.Format.RGB,res).ToPixImage(Col.Format.RGB)
       
        // reactivate if texture based footprint is needed in the future
        //let pi = PixImage<byte>(Col.Format.RGBA, res)
        //pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> 
        //    let c = V2i c
        //    if c.X < 5 || c.X > res.X - 5 || c.Y < 5 || c.Y > res.Y - 5 then C4b.Red else C4b(0,0,0,0)
        //) |> ignore
        //let tex = PixTexture2d(PixImageMipMap [| (pi.ToPixImage(Col.Format.RGBA)) |], true) :> ITexture

        let fp = 
            { 
                vpId             = model.selectedViewPlan
                isVisible        = model.footPrint.isVisible //true
                projectionMatrix = (model.instrumentFrustum |> Frustum.projTrafo).Forward
                instViewMatrix   = model.instrumentCam.ViewTrafo.Forward
                projTex          = DefaultTextures.blackTex.GetValue()
                globalToLocalPos = roverpos //transformenExt.position
                depthTexture     = None
                isDepthVisible   = model.footPrint.isDepthVisible
                depthColorLegend = model.footPrint.depthColorLegend //FalseColorsModel.initDepthLegend
            }
        fp //{ model with footPrint = fp }
    

  
