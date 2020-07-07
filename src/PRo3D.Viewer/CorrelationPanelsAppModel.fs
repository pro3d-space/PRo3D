namespace PRo3D.Correlations

open System
open Aardvark.Base

open Aardvark.Base.Incremental
open CorrelationDrawing
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.Model
open PRo3D.Groups
open PRo3D.Annotation
open PRo3D.Base.Annotation
open Chiron


type LogPoint = {
    annoId   : Guid //PRo3D Annotation Id
    position : V3d
}

[<DomainType>]
type LogDrawingBrush = {
    pointsTable    : hmap<Guid, LogPoint>
    localPoints    : plist<LogPoint>
    modelTrafo     : Trafo3d
    referencePlane : option<DipAndStrikeResults>
    planeScale     : float
}

module LogDrawingBrush =
    let clearLogPoints brush =
        {
            pointsTable    = HMap.empty
            localPoints    = PList.empty
            modelTrafo     = Trafo3d.Identity
            referencePlane = brush.referencePlane
            planeScale     = brush.planeScale
        }

type LoggingMode =
| PickReferencePlane
| PickLoggingPoints
| EditLog

[<DomainType>]
type CorrelationPanelModel = {  
    version                : int
    logginMode             : LoggingMode
    logBrush               : option<LogDrawingBrush>
    contacts               : ContactsTable   
    correlationPlot        : CorrelationPlotModel
    semanticApp            : SemanticTypes.SemanticsModel
    contactOfInterest      : hset<ContactId>    
}
with 
    static member current = 0
    static member private readV0 : Json<CorrelationPanelModel> =
        json {
            let! correlationPlot = Json.read "correlationPlot"
            let! semanticApp     = Json.read "semanticApp"            

            return {
                version                = CorrelationPanelModel.current
                logBrush               = None
                contacts               = HMap.empty                
                correlationPlot        = correlationPlot
                semanticApp            = semanticApp
                logginMode             = PickReferencePlane
                contactOfInterest      = HSet.empty
            }
        }
    static member FromJson(_:CorrelationPanelModel) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! CorrelationPanelModel.readV0
            | _ -> return! v |> sprintf "don't know version %d of CorrelationPanelModel" |> Json.error
        }
    static member ToJson (x : CorrelationPanelModel) =
        json {
            do! Json.write "version"         x.version
            do! Json.write "correlationPlot" x.correlationPlot
            do! Json.write "semanticApp"     x.semanticApp
        }

module CorrelationPanelModel =
    let initial = {
        version                = CorrelationPanelModel.current
        logBrush               = None
        contacts               = HMap.empty        
        correlationPlot        = CorrelationPlotModel.initial
        semanticApp            = SemanticApp.getInitialWithSamples    
        logginMode             = PickReferencePlane
        contactOfInterest      = HSet.empty
    }


