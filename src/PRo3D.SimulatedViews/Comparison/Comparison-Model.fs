namespace PRo3D.Comparison

open Adaptify
open FSharp.Data.Adaptive

open Aardvark.Base
open PRo3D.Base.Annotation
open Aardvark.UI
open PRo3D.Core
open PRo3D.Base
open Chiron

type OriginMode =
    | ModelOrigin       = 0
    | BoundingBoxCentre = 1

type SurfaceMeasurements =  {
    /// the dimensions of this surface along x/y/z axes
    dimensions      : V3d
    /// the angles of x/y/z axes of the model in worldspace
    rollPitchYaw    : V3d
} with
    static member init = {
        dimensions     = V3d.OOO
        rollPitchYaw   = V3d.OOO
    }
    static member ToJson (x:SurfaceMeasurements) =
        json {
            do! Json.write "dimensions"   (x.dimensions |> string)
            do! Json.write "rollPitchYaw" (x.rollPitchYaw |> string)
        }


type AnnotationMeasurement = {
    annotationKey : System.Guid
    text          : string
    length        : float
} with
    static member ToJson (x:AnnotationMeasurement) =
        json {
            if x.text.Length > 0 then
              do! Json.write "text" x.text
            do! Json.write "length" (sprintf "%f" x.length)
        }

/// Used to compare the way length of two annotations on two different surfaces.
/// The annotations need to be drawn using Bookmark projection.
/// To compare two annotations they need to be drawn with the same bookmark selected.
type AnnotationComparison = {
    bookmarkName      : string
    bookmarkId        : System.Guid
    measurement1      : option<AnnotationMeasurement>
    measurement2      : option<AnnotationMeasurement>
    difference        : option<float>
} with
    static member ToJson (x:AnnotationComparison) =
        json {
            do! Json.write "bookmark" x.bookmarkName
            do! Json.write "bookmarkId" (x.bookmarkId.ToString ())
            //do! Json.write "bookmarkId" (x.bookmarkId.ToString ())
            if x.measurement1.IsSome then 
                do! Json.write "measurements1"  x.measurement1.Value
            if x.measurement2.IsSome then 
                do! Json.write "measurements2"  x.measurement2.Value
            if x.difference.IsSome then 
                do! Json.write "difference"   x.difference.Value
        }
    static member FromJson (_ : AnnotationComparison) =
        json {
            let! bookmarkName = Json.read "bookmarkName"
            let! bookmarkId = Json.read "bookmarkId"
                                 
            return {
                bookmarkName = bookmarkName 
                bookmarkId   = bookmarkId |> System.Guid.Parse
                measurement1 = None
                measurement2 = None
                difference   = None
            }
           
        }
      
type SurfaceComparison = {
    measurements1         : option<SurfaceMeasurements>
    measurements2         : option<SurfaceMeasurements>
    comparedMeasurements  : option<SurfaceMeasurements>
} with
    static member init =
        {
            measurements1         = None
            measurements2         = None
            comparedMeasurements  = None            
        }
    static member ToJson (x:SurfaceComparison) =
        json {
            if x.measurements1.IsSome then 
                do! Json.write "measurements1"  x.measurements1.Value
            if x.measurements2.IsSome then 
                do! Json.write "measurements2"  x.measurements2.Value
            if x.comparedMeasurements.IsSome then 
                do! Json.write "difference"   x.comparedMeasurements.Value
        }

[<ModelType>]
type VertexStatistics = {
    avgDistance : float
    maxDistance : float
    minDistance : float
    diffPoints1 : V3d[]
    diffPoints2 : V3d[]
    trafo1      : Trafo3d
    trafo2      : Trafo3d
    distances   : list<float>
    colorLegend : PRo3D.Base.FalseColorsModel
} with
  static member ToJson (x:VertexStatistics) =
    json {
        do! Json.write "minDistance" x.minDistance
        do! Json.write "avgDistance" x.avgDistance
        do! Json.write "maxDistance" x.maxDistance
    }




type AreaSelectionAction =
  | SetRadius of float
  | SetLocation of V3d
  | ToggleVisible
  | ToggleResolution
  | UpdateStatistics
  | MakeBigger
  | MakeSmaller
  | Nop

[<ModelType>]
type AreaSelection = {
    [<NonAdaptive>]
    id         : System.Guid
    label      : string
    radius     : float
    location   : V3d
    highResolution : bool
    visible    : bool
    surfaceTrafo   : Trafo3d
    verticesSurf1 : IndexList<V3d>
    verticesSurf2 : IndexList<V3d>
    statistics : option<VertexStatistics>
} with
    static member ToJson (x:AreaSelection) =
        json {
            do! Json.write "id" x.id
            do! Json.write "label"  x.label
            do! Json.write "radius" x.radius
            do! Json.write "location" (x.location |> string)
            do! Json.write "highResolution" x.highResolution
            do! Json.write "visible" x.visible
            if x.statistics.IsSome then 
                do! Json.write "statistics"  x.statistics
        }
    static member FromJson ( _ : AreaSelection) =
        json {
            let! id = Json.read "id"
            let! label = Json.read "label" 
            let! radius = Json.read "radius" 
            let! location = Json.read "location"
            let! highResolution = Json.read "highResolution"
            let! visible = Json.read "visible"

            return 
                {
                    id = id |> System.Guid.Parse
                    label = label
                    radius = radius
                    location = location |> V3d.Parse
                    highResolution = highResolution
                    visible = visible
                    surfaceTrafo = Trafo3d.Identity
                    verticesSurf1 = IndexList.empty
                    verticesSurf2 = IndexList.empty
                    statistics = None
                }
        }


type ComparisonAppState =
    | Idle
    | CalculatingStatistics

type DistanceMode =
    | Spherical = 0
    | SurfaceNormal = 1

type ComparisonAction =
  | SetState of ComparisonAppState
  | SelectSurface1 of string
  | SelectSurface2 of string
  | UpdateDefaultAreaSize of Numeric.Action
  | UpdateAllMeasurements
  | UpdateCoordinateSystemMeasurements
  | ASyncUpdateCoordinateSystemMeasurements
  | UpdateAreaMeasurements
  | ASyncUpdateAreaMeasurements
  | UpdateAnnotationMeasurements
  | ASyncUpdateAnnotationMeasurements
  | ExportMeasurements of string
  | ToggleVisible
  | AddBookmarkReference of System.Guid
  | SetOriginMode of OriginMode
  | SetDistanceMode of DistanceMode
  | AddSelectionArea of V3d
  | UpdateSelectedArea of AreaSelectionAction
  | UpdatePointSizeFactor of Numeric.Action
  | AreaSelectionMessage of System.Guid * AreaSelectionAction
  | SelectArea of option<(System.Guid)>
  | DeselectArea
  | RemoveArea of System.Guid
  | StopEditingArea
  | RemoveThread of string
  | Nop



/// Used to compare different attributes of two surfaces.
[<ModelType>]
type ComparisonApp = {
    state                        : ComparisonAppState
    threads                      : ThreadPool<ComparisonAction>
    showMeasurementsSg           : bool
    nrOfCreatedAreas             : int
    originMode                   : OriginMode
    surface1                     : option<string>
    surface2                     : option<string>
    surfaceMeasurements          : SurfaceComparison
    annotationMeasurements       : list<AnnotationComparison>
    surfaceGeometryType          : DistanceMode
    initialAreaSize              : NumericInput
    pointSizeFactor              : NumericInput
    selectedArea                 : option<(System.Guid)>
    isEditingArea                : bool
    areas                        : HashMap<System.Guid, AreaSelection>
} with
    static member ToJson (x:ComparisonApp) =
        json {
            if x.surface1.IsSome then 
                do! Json.write "surface1"       x.surface1.Value
            if x.surface2.IsSome then 
                do! Json.write "surface2"       x.surface2.Value 
            do! Json.write "nrOfCreatedAreas"   x.nrOfCreatedAreas
            do! Json.write "originMode"         (x.originMode.ToString ())
            do! Json.write "distanceMode"         (x.surfaceGeometryType.ToString ())
            do! Json.write "surfaceMeasurements" x.surfaceMeasurements
            do! Json.write "annotationMeasurements" x.annotationMeasurements
            let areas = (x.areas |> HashMap.values) |> List.ofSeq
            do! Json.write "areas" areas
        }
    static member FromJson ( _ : ComparisonApp) =
        json {
    
            let! nrOfCreatedAreas = Json.read "nrOfCreatedAreas"
            let! originMode = Json.read "originMode"
            let! surface1               = Json.tryRead "surface1"              //option<string>
            let! surface2               = Json.tryRead "surface2"              //option<string>
            let! annotationMeasurements = Json.read "annotationMeasurements"//list<AnnotationComparison>
            let! distanceMode           = Json.read "distanceMode"   //DistanceMode
           
           
            //let! selectedArea           = Json.read "selectedArea"          //option<(System.Guid)>
            //let! isEditingArea          = Json.read "isEditingArea"         //bool
            let! (areas : list<(AreaSelection)>) = 
                Json.read "areas"                 //HashMap<System.Guid, AreaSelection>            

            //let! points = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            //let! areas = Json.read "areas"
            let areas =
                areas  
                  |> List.map (fun a -> (a.id, a))
                  |> HashMap.ofList
            PRo3D.Comparison.ComparisonUtils.cache <- HashMap.Empty
            return {
                    state                  = ComparisonAppState.Idle
                    threads                = ThreadPool.empty
                    showMeasurementsSg     = false
                    nrOfCreatedAreas       = nrOfCreatedAreas
                    originMode             = originMode |> OriginMode.Parse
                    surface1               = surface1
                    surface2               = surface2
                    surfaceMeasurements    = SurfaceComparison.init
                    annotationMeasurements = annotationMeasurements
                    surfaceGeometryType    = distanceMode |> DistanceMode.Parse
                    initialAreaSize        = PRo3D.Comparison.Init.areaSize
                    pointSizeFactor        = Init.pointSizeFactor
                    selectedArea           = None
                    isEditingArea          = false
                    areas                  = areas
            }
        }
