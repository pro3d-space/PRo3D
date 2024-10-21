namespace PRo3D

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering

open PRo3D.Base
open PRo3D.Core
open PRo3D.Base.Annotation
open Chiron

#nowarn "0044"
#nowarn "0686"



type InteractionMode = PickOrbitCenter = 0 | Draw = 1 | PlaceObject = 2
type Points = list<V3d>

type Projection = 
    | Linear    = 0 
    | Viewpoint = 1 
    | Sky       = 2

type Geometry = 
    | Point     = 0 
    | Line      = 1 
    | Polyline  = 2 
    | Polygon   = 3 
    | DnS       = 4

type Semantic = 
    | Horizon0  = 0 
    | Horizon1  = 1 
    | Horizon2  = 2 
    | Horizon3  = 3 
    | Horizon4  = 4 
    | Crossbed  = 5 
    | GrainSize = 6
    | None      = 7

type ViewerMode =
    | Standard
    | Instrument

type GuiMode =
    | NoGui
    | RenderViewOnly
    | CoreGui
    | CompleteGui


type StartupArgs = {
    showExplorationPoint  : bool
    startEmpty            : bool
    useAsyncLoading       : bool
    serverMode            : bool
    port                  : Option<string>
    enableRemoteApi       : bool
    disableCors           : bool
    magnificationFilter   : bool
    remoteApp             : bool
    enableProvenanceTracking : bool

    useMapping            : string
    data_samples          : Option<string>
    backgroundColor       : string

    isBatchRendering      : bool

    defaultSpiceKernelPath : Option<string>

    verbose               : bool    

} with 
    static member initArgs =
      {
          showExplorationPoint  = true
          startEmpty            = false
          useAsyncLoading       = false
          magnificationFilter   = false
          remoteApp             = false
          serverMode            = false
          data_samples          = None
          backgroundColor       = "#222222"
          useMapping            = "true"
          verbose               = false      
          disableCors           = false
          port                  = None
          enableRemoteApi       = false
          enableProvenanceTracking = false
          isBatchRendering         = false
          defaultSpiceKernelPath   = None
      }


[<ModelType>]
type Statistics = {
  average      : float
  min          : float
  max          : float
  stdev        : float
  sumOfSquares : float
}

module Statistics = 
  let init = {
    average      = Double.NaN
    min          = Double.NaN
    max          = Double.NaN
    stdev        = Double.NaN
    sumOfSquares = Double.NaN
  }

[<ModelType>]
type OrientationCubeModel = {
    camera  : CameraControllerState
}

type Style = {
    color : C4b
    thickness : NumericInput
}
    
module JsonTypes =
    type _V3d = {
        X : double
        Y : double
        Z : double
    }

    type _Points = list<_V3d>

    type _Segment = list<_V3d>

    type _Annotation = {       
        semantic : string
        geometry : _Points 
        segments : list<_Segment>
        color : string
        thickness : double        
        projection : string
        elevation : double
        distance : double
        azimuth : double
        angle   : double
        surfid  : string
    }

    let ofV3d (v:V3d) : _V3d = { X = v.X; Y = v.Y; Z = v.Z }

    let ofPolygon (p:Points) : _Points = p  |> List.map ofV3d

    let ofSegment (s:Segment) : _Segment = 
        s.points  
        |> IndexList.map ofV3d 
        |> IndexList.toList

    let ofSegment1 (s:IndexList<V3d>) : _Segment = 
        s  
        |> IndexList.map ofV3d
        |> IndexList.toList

    let sumDistance (polyline : Points) : double =
        polyline  
        |> List.pairwise 
        |> List.fold (fun s (a,b) -> s + (b - a).LengthSquared) 0.0 
        |> Math.Sqrt
    
[<ModelType>]
type PathProxy = {
    absolutePath : option<string>
    relativePath : option<string>
}

type SurfaceShift = {
    id : string
    shift: float
}