namespace CorrelationDrawing.Model

open Aardvark.Base
open FSharp.Data.Adaptive

open CorrelationDrawing.Types
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.SemanticTypes
open CorrelationDrawing.LogTypes
open CorrelationDrawing.LogNodeTypes
open CorrelationDrawing
open CorrelationDrawing.Nuevo

open Svgplus.CameraType

open Svgplus.DiagramItemType
open UIPlus
open Chiron
open Aardvark.UI
open Svgplus
open PRo3D.Base.Annotation
open System
open Svgplus.Correlations2

open Adaptify

type CorrelationPlotAction =
    | Clear
    | SvgCameraMessage       of Svgplus.SvgCamera.Action
    | KeyboardMessage        of Keyboard.Action
    //| SelectLog              of DiagramItemId
    | SelectLogNuevo         of LogId
    | LogPropertiesMessage   of GeologicalLogNuevoProperties.Action
    | FinishLog                           
    | DeleteLog              of DiagramItemId
    | LogMessage             of (DiagramItemId * GeologicalLogAction)  
    | ToggleEditCorrelations
    | SetSecondaryLevel      of NodeLevel
    | DiagramMessage         of Svgplus.DA.DiagramAppAction
    | MouseMove              of V2d
    | GrainSizeTypeMessage   of (Svgplus.RectangleType.RectangleId * ColourMap.Action)
    | CorrelationMessage     of CorrelationsAction

[<ModelType>]
type CorrelationPlotModel = {

    version             : int
    diagram             : Svgplus.DA.DiagramAppModel
    colorMap            : ColourMap
    
    svgCamera           : SvgCamera
    
    logs                : HashMap<DiagramItemId, GeologicalLog>

    logsNuevo           : HashMap<LogId, GeologicalLogNuevo>
    selectedLogNuevo    : option<LogId>
            
    selectedBorder      : option<Border>    
    
    editCorrelations    : bool

    param_selectedPoints      : HashMap<ContactId, V3d> //used for parameter exchange
    param_referencePlane      : DipAndStrikeResults  //used for parameter exchange .... TODO TO Refactor to message parameters
    param_referenceScale      : float  //used for parameter exchange .... TODO TO Refactor to message parameters

    upVector            : V3d
    northVector         : V3d

    selectedNode        : option<LogNodeId>
    selectedFacies      : option<FaciesId>
    //selectedLog         : option<DiagramItemId>
    secondaryLvl        : NodeLevel
    //creatingNew         : bool
    //viewType            : CorrelationPlotViewType
    
    //svgFlags            : SvgFlags
    //svgOptions          : SvgOptions
    
    //logAxisApp          : LogAxisApp
    xAxis               : CorrelationSemanticId
    currrentYMapping    : Option<float>
    
    defaultWidth        : float
    elevationZeroHeight : float
}
with 
    static member current = 0
    static member initial : CorrelationPlotModel  =
        let defaultWidth = 126.0 // approx. width of vfGravel
        let actionMapping 
            (log     : MGeologicalLog)
            (domNode : DomNode<GeologicalLogAction>) = 

            UI.map (fun a -> CorrelationPlotAction.LogMessage (log.diagramRef.itemId, a)) domNode                           

        {
            version              = CorrelationPlotModel.current
            diagram              = DiagramApp.init
            logs                 = HashMap.empty            
            
            editCorrelations     = false
            colorMap             = ColourMap.initial
            param_selectedPoints = HashMap.empty
            param_referencePlane = DipAndStrikeResults.initial
            param_referenceScale = Double.NaN

            logsNuevo            = HashMap.empty            
            selectedLogNuevo     = None
                                             
            selectedNode         = None
            selectedFacies       = None

            selectedBorder       = None
            secondaryLvl         = NodeLevel.init 1            
                                 
            upVector = V3d.NaN
            northVector = V3d.NaN

            svgCamera            = SvgCamera.init
                                 
            xAxis                = CorrelationSemanticId.invalid
            currrentYMapping     = None
                                 
            defaultWidth         = defaultWidth
            elevationZeroHeight  = 0.0
        }     
    static member private readV0 =
        json {
            let! geologicalLogsNuevo = Json.read "geologicalLogs"
            let! yScaleValue         = Json.read "yScaleValue"
            let! correlations        = Json.read "correlations"

            let geologicalLogsNuevo =
                geologicalLogsNuevo 
                |> List.map(fun (guid, log) -> guid |> LogId, log) |> HashMap.ofList            
            
            return { 
                CorrelationPlotModel.initial with
                    version     = GeologicalLogNuevo.current
                    logsNuevo   = geologicalLogsNuevo                                     
                    diagram     = { DiagramApp.init with yScaleValue = yScaleValue; correlations = correlations }
            }
        }
    static member FromJson(_:CorrelationPlotModel) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! CorrelationPlotModel.readV0
            | _ -> return! v |> sprintf "don't know version %d of CorrelationPlotModel" |> Json.error
        }
    static member ToJson (x : CorrelationPlotModel) =
        json {
            do! Json.write "version" x.version
            let logs = x.logsNuevo |> HashMap.toList |> List.map(fun (a,b) -> a |> LogId.value, b)
            do! Json.write "geologicalLogs" logs
            do! Json.write "yScaleValue"    x.diagram.yScaleValue
            do! Json.write "correlations"   x.diagram.correlations
        }