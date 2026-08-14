namespace PRo3D.Core.Drawing

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.Data.Opc

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

type SamplingUnit =
| km = 0
| m  = 1
| cm = 2
| mm = 3

// Delta for the annotations collection — stores only what changed per undoable operation.
// LeafAdded/LeafRemoved carry just the leaf and the group path, which is sufficient to reverse the operation.
// SnapshotDelta stores a full GroupsModel before/after for rare bulk operations (RemoveGroup, Clear).
type AnnotationsDelta =
    | LeafAdded     of leaf: Leaf * groupPath: list<Index>
    | LeafRemoved   of leaf: Leaf * groupPath: list<Index>
    | SnapshotDelta of before: GroupsModel * after: GroupsModel

type DrawingAction =
| SetSemantic         of Semantic
| ChangeThickness     of Numeric.Action
| SetFillNewAnnotations of bool
| ChangeDefaultFillAlpha of Numeric.Action
| ChangeSamplingAmount of Numeric.Action
| SetSamplingUnit     of SamplingUnit
| SetGeometry         of Geometry
| SetProjection       of Projection
| SetExportPath       of string
| Move                of V3d
| Exit            
| StartDrawing            
| StopDrawing   
| StartPicking            
| StopPicking  
| StartPickingMulti     
| StopPickingMulti  
| AddPointAdv         of V3d * (V3d -> Option<V3d>) * Option<PRo3D.Base.Gis.SpiceReferenceSystem> * string * option<Guid>
| RemoveLastPoint  
| ClearWorking
| ClearSelection
| Clear
| LegacySaveVersioned
| LegacyLoadVersioned
| SetSegment             of int * Segment
| Finish                   
| Undo                   
| Redo                   
| FlyToAnnotation        of Guid
//| RemoveAnnotation           of list<Index>*Guid       
| Send                     
| Nop                    
| UpVectorChanged        of V3d
| NorthVectorChanged     of V3d
| GroupsMessage          of GroupsAppAction
| RecalculateMeasurements
/// Union all selected annotations into one (or one per component). The payload lands vertices
/// invented by the union on the terrain; the UI sends None and the viewer re-dispatches with
/// the surface raycast - the same enrichment pattern as AddPointAdv's sample function.
| UnionSelectedAnnotations of Option<V3d -> Option<V3d>>
| DnsColorLegendMessage  of FalseColorLegendApp.Action  
| ExportAsAnnotations    of string
| AddAnnotations         of list<string>
| PickAnnotation         of SceneHit * Guid
| PickDirectly           of Guid
| ExportAsCsv            of string
| ExportAsProfileCsv     of string
| ExportMultiAttributeProfile of string
| ExportAsGeoJSON        of string
| ExportAsGeoJSON_xyz    of string
| ExportAsGeoJSONQGIS_latlon    of string
| ExportAsGeoJSONQGIS_xyz       of string 
| ExportAsGeoJSONQGIS_both      of string // also exports geojson with lat lon, but additionally adds xyz information to metadata
| ContinuouslyGeoJson    of string
| ExportAsAttitude       of string

[<ModelType>]
type AutomaticGeoJsonExport = 
    {
        enabled : bool
        lastGeoJsonPath    : Option<string>
        lastGeoJsonPathXyz : Option<string>
    }



[<ModelType>]
type DrawingModel = {

    draw          : bool
    pick          : bool
    multi         : bool
    hoverPosition : option<Trafo3d>    

    working    : Option<Annotation>

    //TODO refactor ... put this into separate model type and save it with the scene or in user/app data
    projection : Projection
    geometry   : Geometry
    semantic   : Semantic
    thickness  : NumericInput

    /// defaults applied to the next annotation drawn. Fill colour is deliberately absent - it
    /// comes from the active group's default colour via Annotation.make, the same single source
    /// the outline colour uses (see the note in Drawing.UI.fs).
    fillNewAnnotations : bool
    defaultFillAlpha   : NumericInput

    samplingAmount   : NumericInput
    samplingUnit     : SamplingUnit
    samplingDistance : float

    annotations: GroupsModel 
    exportPath : Option<string>

    pendingIntersections : ThreadPool<DrawingAction>    

    [<TreatAsValue>]
    undoStack : list<AnnotationsDelta>

    [<TreatAsValue>]
    redoStack : list<AnnotationsDelta>

    dnsColorLegend : FalseColorsModel

    // test laura
    haltonPoints   : list<V3d>

    automaticGeoJsonExport : AutomaticGeoJsonExport
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]    
module DrawingModel =

    let tryGet (ans:IndexList<Annotation>) key = 
        ans |> Seq.tryFind(fun x -> x.key = key)

    let tryGet' (ans:HashSet<Annotation>) key = 
        ans |> Seq.tryFind(fun x -> x.key = key)

    let update (ans:IndexList<Annotation>) (ann : Annotation) =
        ans.AsList 
        |> List.updateIf (fun x -> x.key = ann.key) (fun x -> ann) 
        |> IndexList.ofList

    let calculateSamplingDistance (amount : NumericInput) (unit : SamplingUnit) =
        match unit with
        | SamplingUnit.km -> amount.value * 1000.0
        | SamplingUnit.m  -> amount.value
        | SamplingUnit.cm -> amount.value * 0.01
        | SamplingUnit.mm -> amount.value * 0.001
        | _ -> 1.0

    let initialdrawing : DrawingModel = {
        hoverPosition = None
        draw          = false  
        pick          = false
        multi         = false
        thickness     = Annotation.Initial.thickness

        fillNewAnnotations = false
        defaultFillAlpha   = Annotation.initialFillAlpha

        working     = None
        projection  = Projection.Linear
        geometry    = Geometry.Line
        semantic    = Semantic.Horizon3

        samplingAmount   = Annotation.Initial.samplingAmount
        samplingDistance = calculateSamplingDistance Annotation.Initial.samplingAmount SamplingUnit.m
        samplingUnit     = SamplingUnit.m

        annotations = GroupsModel.initial

        exportPath  = Some @"."
        
        pendingIntersections = ThreadPool.empty

        undoStack = []
        redoStack = []
        
        dnsColorLegend = FalseColorsModel.initDnSLegend

        // test laura
        haltonPoints = []

        automaticGeoJsonExport = { enabled = false; lastGeoJsonPath = None; lastGeoJsonPathXyz = None }
    }