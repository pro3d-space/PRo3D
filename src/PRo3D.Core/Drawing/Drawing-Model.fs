namespace PRo3D.Core.Drawing

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

type DrawingAction =
| SetSemantic         of Semantic
| ChangeColor         of ColorPicker.Action
| ChangeThickness     of Numeric.Action
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
| AddPointAdv         of V3d * (V3d -> Option<V3d>) * string * option<Guid>
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
| DnsColorLegendMessage  of FalseColorLegendApp.Action  
| ExportAsAnnotations    of string
| AddAnnotations         of list<string>
| PickAnnotation         of SceneHit * Guid
| PickDirectly           of Guid
| ExportAsCsv            of string
| ExportAsGeoJSON        of string
| ExportAsGeoJSON_xyz    of string
| ContinuouslyGeoJson    of string
| ExportAsAttitude       of string
| SendAnnotationID       of Guid

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
    projection : Projection
    geometry   : Geometry
    semantic   : Semantic
    color      : ColorInput
    thickness  : NumericInput

    annotations: GroupsModel 
    exportPath : Option<string>

    pendingIntersections : ThreadPool<DrawingAction>    

    [<TreatAsValue>]
    past : Option<DrawingModel> 

    [<TreatAsValue>]
    future : Option<DrawingModel>

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

    let initialdrawing : DrawingModel = {
        hoverPosition = None
        draw          = false  
        pick          = false
        multi         = false
        color         = { c = C4b.DarkBlue } 
        thickness     = Annotation.Initial.thickness

        working     = None
        projection  = Projection.Linear
        geometry    = Geometry.Line
        semantic    = Semantic.Horizon3

        annotations = GroupsModel.initial

        exportPath  = Some @"."
        
        pendingIntersections = ThreadPool.empty

        past    = None
        future  = None
        
        dnsColorLegend = FalseColorsModel.initDnSLegend

        // test laura
        haltonPoints = []

        automaticGeoJsonExport = { enabled = false; lastGeoJsonPath = None; lastGeoJsonPathXyz = None }
    }