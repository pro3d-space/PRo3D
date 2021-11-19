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
    platforms          : HashMap<string, JR.InstrumentPlatforms.SPlatform>
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

module FootPrint = 
        
    let getFootprintsPath (scenePath:string) =
        let path = Path.GetDirectoryName scenePath
        Path.combine [path;"FootPrints"]
       
    let createFootprintData (vp:ViewPlanModel) (scenePath:string) =

        let fpPath = getFootprintsPath scenePath

        match vp.selectedViewPlan with
            | Some v -> 
                let now = DateTime.Now
                let roverName = v.rover.id
                let instrumentName = 
                    match v.selectedInstrument with
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
                        match v.selectedInstrument with
                                | Some i -> let horRes = i.intrinsics.horizontalResolution/uint32(2)
                                            let vertRes = i.intrinsics.verticalResolution/uint32(2)
                                            int(horRes), int(vertRes)
                                | None -> 512, 512
                // save png file
                try Utilities.takeScreenshotFromAllViews "http://localhost:54322" width height pngName fpPath ".png" with e -> printfn "error: %A" e
               
                let fileInfo = {
                    fileType = "PNGImage"
                    path = fpPath
                    name = pngName
                }

                let calibration = {
                    instrumentPlatformXmlFileName       = v.rover.id + ".xml"
                    instrumentPlatformXmlFileVersion    = 1.0
                }

                let roverInfo = {
                    position = v.position
                    lookAtPosition = v.lookAt
                    placementTrafo = v.roverTrafo
                }

                let panAx = v.rover.axes.TryFind "Pan Axis" |> Option.map(fun x -> x.angle.value )
                let panVal = match panAx with | Some av -> av | None -> 1.0

                let tiltAx = v.rover.axes.TryFind "Tilt Axis" |> Option.map(fun x -> x.angle.value )
                let tiltVal = match tiltAx with | Some av -> av | None -> 1.0
                let angles = {
                    panAxis = panVal
                    tiltAxis = tiltVal
                }

                let focal =
                    match v.selectedInstrument with
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
        
        let id = 
            match model.selectedViewPlan with
            | Some vp -> Some vp.id
            | None -> None
        
        let res = V2i((int)instrument.intrinsics.horizontalResolution, (int)instrument.intrinsics.verticalResolution)
        //let image = PixImage<byte>(Col.Format.RGB,res).ToPixImage(Col.Format.RGB)
       
        let pi = PixImage<byte>(Col.Format.RGBA, res)
        pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
        let tex = PixTexture2d(PixImageMipMap [| (pi.ToPixImage(Col.Format.RGBA)) |], true) :> ITexture
        
        let projectionTrafo = model.instrumentFrustum |> Frustum.projTrafo
        
        let location = model.instrumentCam.Location - roverpos //transformenExt.position
        let testview = model.instrumentCam.WithLocation location

        let fp = 
            {
                vpId             = id
                isVisible        = true
                projectionMatrix = (model.instrumentFrustum |> Frustum.projTrafo).Forward
                instViewMatrix   = model.instrumentCam.ViewTrafo.Forward
                projTex          = tex
                globalToLocalPos = roverpos //transformenExt.position
            }
        fp //{ model with footPrint = fp }

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
        instViewMatrix      = M44d.Identity
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

  
