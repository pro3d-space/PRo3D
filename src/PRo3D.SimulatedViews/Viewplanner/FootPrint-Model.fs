namespace PRo3D.FootPrint

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI.Primitives
open Aardvark.UI
open FShade.Intrinsics
open Aardvark.Rendering

open Chiron
open PRo3D
open Adaptify

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
}



