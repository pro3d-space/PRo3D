namespace PRo3D

open System
open System.IO

open Aardvark
open Aardvark.Base
open Aardvark.Rendering

open PRo3D
open PRo3D.Base
//open PRo3D.Minerva
//open PRo3D.Linking
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Viewer
open PRo3D.SimulatedViews

open FSharp.Data.Adaptive

open Chiron

//open CorrelationDrawing
//open CorrelationDrawing.Model
#nowarn "0044"


//TODO make nice api for serialization
module ViewerIO =          

    //rover data
    let loadRoverData (m:Model) =
        try
            let vps = ViewPlanApp.loadRoverData m.scene.viewPlans m.scene.scenePath
            { m with scene = { m.scene with viewPlans = vps } }
        with e ->
            Log.warn "[Rover Data] could not load rover data: %A" e.Message
            m
    
    
    //let getAnnotationPath_depr (scenePath:string) = 
    //  scenePath |> changeExtension ".ann" 

    //let getAnnotationPath (scenePath:string) = 
    //  scenePath |> changeExtension ".ann.json" 

    //annotations
    type ScenePaths =
        {
            scene               : string                           
            annotations         : string
            correlations        : string

            //deprecated
            [<Obsolete>]
            annotationGroups    : string
            [<Obsolete>]
            annotationsFlat     : string
            [<Obsolete>]
            annotationDepr      : string
        }
    module ScenePaths =
        let create(scenepath : string) =
            {
                scene               = scenepath          
                annotations         = scenepath |> Serialization.changeExtension ".pro3d.ann"
                correlations        = scenepath |> Serialization.changeExtension ".pro3d.corr"
                
                annotationGroups    = scenepath |> Serialization.changeExtension ".ann" 
                annotationsFlat     = scenepath |> Serialization.changeExtension ".ann.json" 
                annotationDepr      = scenepath |> Serialization.changeExtension ".ann_old" 
            }   

    let saveVersioned' (model : DrawingModel) (paths : ScenePaths) =        
        PRo3D.Core.Drawing.IO.saveVersioned model paths.annotations                
        
        
    let tryLoadAnnotations (scenePath : string) : option<Annotations> =
        try        
            (DrawingUtilities.IO.loadAnnotations scenePath) |> Some
        with e ->        
            Log.error "[ViewerIO] couldn't load %A" e
            None
    
    //let colorBySemantic 
    //    (semantics : HashMap<SemanticTypes.CorrelationSemanticId, SemanticTypes.CorrelationSemantic>) 
    //    flat =

    //    flat
    //    |> Leaf.toAnnotations
    //    |> HashMap.map(fun k a ->
    //        let (Annotation.SemanticId semId) = a.semanticId 
    //        let semantic = 
    //            semantics
    //            |> HashMap.tryFind (semId |> SemanticTypes.CorrelationSemanticId)

    //        match semantic with
    //        | Some s -> { a with color = s.color }
    //        | None -> a
    //    )
    //    |> HashMap.map(fun _ v -> Leaf.Annotations v)

    //let colorBySemantic' (model : Model) =
    //    let flat = 
    //        colorBySemantic 
    //            model.correlationPlot.semanticApp.semantics
    //            model.drawing.annotations.flat

    //    { 
    //        model with 
    //            drawing = {
    //                model.drawing with 
    //                    annotations = {
    //                        model.drawing.annotations with flat = flat
    //                    }
    //            }                    
    //    }
            

    let loadAnnotations (m : Model) = 
        m.scene.scenePath 
        |> Option.bind(fun p ->        
            let scenePaths = p |> ScenePaths.create
            scenePaths.annotations |> tryLoadAnnotations
        )
        |> Option.map(fun g ->                 

            //color annotations according to semantics
            //let flat = 
            //    g.annotations.flat |> colorBySemantic m.correlationPlot.semanticApp.semantics
                

            //update contacts table for correlation panel (TODO TO remove duplication)
            //let corr = 
            //    CorrelationPanelsApp.update
            //        m.correlationPlot 
            //        m.scene.referenceSystem                    
            //        (UpdateAnnotations flat)
            
            {   
                m with                    
                    //correlationPlot = corr
                    drawing = {
                        m.drawing with 
                            annotations    = g.annotations
                            dnsColorLegend = g.dnsColorLegend 
                    }
            }            
            
        ) 
        |> Option.defaultValue m

    let loadCorrelations (m : Model) : Model =
        m.scene.scenePath
        |> Option.bind(fun path ->
            (ScenePaths.create path).correlations
            |> Serialization.fileExists
        )    
        |> Option.map(fun correlationsPath ->

            //let correlationPlotModel = 
            //    correlationsPath
            //    |> Serialization.Chiron.readFromFile 
            //    |> Json.parse 
            //    |> Json.deserialize

            //TODO TO refactor weird point of failure, having contacts and annotations
            //let correlationPlotModel = 
            //    UpdateAnnotations m.drawing.annotations.flat 
            //    |> CorrelationPanelsApp.update correlationPlotModel m.scene.referenceSystem
               
            //let correlationPlotModel = 
            //    { 
            //        correlationPlotModel with 
            //            correlationPlot = {
            //                correlationPlotModel.correlationPlot with
            //                    upVector    = m.scene.referenceSystem.up.value
            //                    northVector = m.scene.referenceSystem.north.value
            //            }
            //    }

            //let m = 
            //    { m with correlationPlot = correlationPlotModel }                    
            
            //let correlationPlotModel =
            //    CorrelationDrawing.CorrelationPlotApp.reconstructDiagramsFromLogs 
            //      m.correlationPlot.contacts
            //      m.correlationPlot.semanticApp
            //      m.correlationPlot.correlationPlot.colorMap
            //      m.correlationPlot.correlationPlot

            //let diagram =
            //    correlationPlotModel.diagram
            //    |> Svgplus.DiagramApp.update (Svgplus.DA.DiagramAppAction.SetYScaling(correlationPlotModel.diagram.yScaleValue))

            //let correlationPlotModel =
            //    { correlationPlotModel with diagram = diagram }

            //{ m with correlationPlot = { m.correlationPlot with correlationPlot = correlationPlotModel } }
            m
        )
        |> Option.defaultValue m

    //let loadMinerva dumpFile cacheFile (m:Model) =
              
    //    let data = MinervaModel.loadDumpCSV dumpFile cacheFile 

    //    let whiteListFile = Path.ChangeExtension(dumpFile, "white")
    //    let whiteListIds =
    //        if whiteListFile |> File.Exists then
    //            File.readAllLines whiteListFile |> HashSet.ofArray
    //        else 
    //            data.features |> IndexList.map(fun x -> x.id) |> IndexList.toList |> HashSet.ofList
            
    //    let validFeatures = data.features |> IndexList.filter (fun x -> whiteListIds |> HashSet.contains x.id)
    //    let data = { data with features = validFeatures }

    //    let minerva = 
    //        MinervaApp.update m.navigation.camera.view m.frustum m.minervaModel MinervaAction.Load
    //        |> fun m -> { m with data = data }
    //        |> MinervaApp.updateProducts data
    //        |> MinervaApp.loadTifs1087    

    //    //refactor ... make chain
    //    let filtered = QueryApp.applyFilterQueries minerva.data.features minerva.session.queryFilter

    //    let newModel = 
    //        { 
    //            minerva with 
    //                session = { 
    //                    minerva.session with
    //                        filteredFeatures = filtered 
    //                } 
    //        } |> MinervaApp.updateFeaturesForRendering        

    //    { m with minervaModel = newModel }

    //// minerva has to be preloaded at this point
    //let loadLinking (m: Model) = 
    //    { m with linkingModel = m.linkingModel |> LinkingApp.initFeatures m.minervaModel.data.features }
      
    let loadLastFootPrint (m:Model) = 
        let fp = 
            m.scene.viewPlans.selectedViewPlan 
            |> Option.bind(fun vp -> 
                vp.selectedInstrument |> Option.map(fun instr -> (instr,vp)))
            |> Option.map(fun (instr, vp) -> 
                FootPrint.updateFootprint instr vp.position m.scene.viewPlans)
            |> Option.defaultValue ViewPlanModel.initFootPrint
       
        { m with footPrint = fp }
           
    let saveEverything (path:string) (m:Model) =         

        if path.IsEmptyOrNull() then m
        else
           //saving scene
            let scenePaths = path |> ScenePaths.create             
            let cameraState = m.navigation.camera.view            
            let scene = { m.scene with scenePath      = Some scenePaths.scene; 
                                       cameraView     = cameraState;
                                       exploreCenter  = m.navigation.exploreCenter
                                       navigationMode = m.navigation.navigationMode}
            scene
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty
            |> Serialization.Chiron.writeToFile scenePaths.scene
            
            //m.correlationPlot
            //|> Json.serialize 
            //|> Json.formatWith JsonFormattingOptions.Pretty
            //|> Serialization.Chiron.writeToFile scenePaths.correlations

            //saving annotations
            let drawing =                 
                scenePaths |> saveVersioned' m.drawing
            
            ////saving minerva session
            //let minerva = 
            //    try
            //    MinervaApp.update 
            //        m.navigation.camera.view
            //        m.frustum
            //        m.minervaModel
            //        MinervaAction.Save
            //    with e -> 
            //        Log.warn "[Minerva] update failed, could not save, using old model: %A" e
            //        m.minervaModel

            //saving correlations session                        
            
            { m with 
                scene        = scene 
                //correlationPlot = { m.correlationPlot with semanticApp = m.correlationPlot.semanticApp }
                drawing      = drawing }
                //minervaModel = minerva } 
            |> Model.stashAndSaveRecent path

