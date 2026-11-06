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
            bookmarksFolder     : string

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
                bookmarksFolder     = Path.combine 
                                        [
                                            (Path.GetDirectoryName scenepath)
                                            (Path.GetFileNameWithoutExtension scenepath)
                                        ]
                annotationGroups    = scenepath |> Serialization.changeExtension ".ann" 
                annotationsFlat     = scenepath |> Serialization.changeExtension ".ann.json" 
                annotationDepr      = scenepath |> Serialization.changeExtension ".ann_old" 
            }   

    let saveVersioned' (model : DrawingModel) (paths : ScenePaths) =      
        PRo3D.Core.Drawing.IO.saveVersioned model paths.annotations                
        
        
    let tryLoadAnnotations (scenePath : string) : option<Annotations> =
        try        
            (DrawingUtilities.IO.loadAnnotationsFromFile scenePath) |> Some
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
            
    let replaceAnnotations (m : Model) (annotations : Annotations) =
         {   
            m with                    
                drawing = {
                    m.drawing with
                        annotations     = annotations.annotations
                        dnsColorLegend  = annotations.dnsColorLegend
                        colorByCategory = annotations.colorByCategory
                        // opened past the experimental notice: stays open for the session
                        geologicalMaps  = { annotations.geologicalMaps with enabled = m.drawing.geologicalMaps.enabled }
                }
         }  

    let loadAnnotations (m : Model) = 
        m.scene.scenePath 
        |> Option.bind(fun p ->        
            let scenePaths = p |> ScenePaths.create
            scenePaths.annotations |> tryLoadAnnotations
        )
        |> Option.map(fun (g : Annotations) ->                 

            //color annotations according to semantics
            //let flat = 
            //    g.annotations.flat |> colorBySemantic m.correlationPlot.semanticApp.semantics
                

            //update contacts table for correlation panel (TODO TO remove duplication)
            //let corr = 
            //    CorrelationPanelsApp.update
            //        m.correlationPlot 
            //        m.scene.referenceSystem                    
            //        (UpdateAnnotations flat)
            
            replaceAnnotations m g
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
        let fp, viewPlans' = 
            m.scene.viewPlans.selectedViewPlan 
            |> Option.bind(fun vp -> 
                let selectedVp = m.scene.viewPlans.viewPlans |> HashMap.find vp
                selectedVp.selectedInstrument |> Option.map(fun instr -> (instr,vp)))
            |> Option.map(fun (instr, vp) -> 
                let selectedVp = m.scene.viewPlans.viewPlans |> HashMap.find vp
                let footPrint = FootPrintUtils.updateFootprint instr selectedVp m.scene.viewPlans
                ViewPlanApp.updateInstrumentCam selectedVp m.scene.viewPlans footPrint)
            |> Option.defaultValue (FootPrint.initFootPrint, m.scene.viewPlans)

        { m with scene = {m.scene with viewPlans = viewPlans'};  footPrint = fp }

    let loadSequencedBookmarks (m : Model) =
        let bookmarks = BookmarkUtils.loadAll m.scene.sequencedBookmarks
        {m with scene = {m.scene with sequencedBookmarks = bookmarks}}

    type SerializedModel =
        {
            sceneAsJson : string
            drawingAsJson : string
            version : string
        } 

    module SerializedModel =

        open System.Text.Json
        
        let toJson (s : SerializedModel) =
            JsonSerializer.Serialize(s)

        let fromJson (s : string) =
            JsonSerializer.Deserialize(s)

    let getSerializedModel  (m : Model) : SerializedModel  =
        let sceneJson =        
            let cameraState = m.navigation.camera.view            
            let scene = 
                { m.scene with 
                    cameraView     = cameraState;
                    exploreCenter  = m.navigation.exploreCenter
                    navigationMode = m.navigation.navigationMode
                }
            scene
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty

        let drawingJson =
            PRo3D.Core.Drawing.IO.getSerialized m.drawing

        { sceneAsJson = sceneJson; drawingAsJson = drawingJson; version = "1" }

    /// Scene files must end in `.pro3d`: the Linux save dialog returns the name as typed, and
    /// without the extension the bookmark folder `<dir>/<name>` IS the scene path, so the
    /// folder is created first and the scene write then fails with "access denied" (#834).
    let normalizeScenePath (path : string) =
        if path.EndsWith(".pro3d", StringComparison.OrdinalIgnoreCase) then path
        else path + ".pro3d"

    /// What a save did. `problems` lists everything that was NOT written (each also logged);
    /// empty means everything was saved.
    type SaveOutcome =
        {
            path     : string
            problems : list<string>
        }

    let private describeLeaf (l : Leaf) =
        match l with
        | Leaf.Annotations a ->
            if String.IsNullOrWhiteSpace a.text then sprintf "annotation %A (%A)" a.geometry a.key
            else sprintf "annotation '%s' (%A)" a.text a.key
        | Leaf.Surfaces s  -> sprintf "surface '%s'" s.name
        | Leaf.Bookmarks b -> sprintf "bookmark '%s'" b.name

    let inline private trySerialize (x : ^a) =
        try x |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty |> Result.Ok
        with e -> Result.Error e.Message

    /// The leaves of `groups` that cannot be serialized on their own, with the reason.
    let private unserializableLeaves (groups : GroupsModel) =
        let mutable bad = []
        for (id, leaf) in groups.flat do
            match trySerialize leaf with
            | Result.Ok _ -> ()
            | Result.Error e -> bad <- (id, describeLeaf leaf, e) :: bad
        bad

    /// Last-resort isolation when serializing a document failed: drop exactly the leaves of
    /// the given groups that cannot be serialized, report each, and keep everything else.
    /// Only the written copy loses them - the model in memory stays complete.
    let private withoutUnserializable (groups : GroupsModel) : GroupsModel * list<string> =
        match unserializableLeaves groups with
        | [] -> groups, []
        | bad ->
            let problems =
                bad |> List.map (fun (_, name, e) ->
                    Log.error "[Save] %s could not be saved and was left out of the file: %s" name e
                    sprintf "%s was not saved: %s" name e)
            let ids = bad |> List.fold (fun s (id, _, _) -> HashSet.add id s) HashSet.empty
            GroupsApp.removeLeavesById ids groups, problems

    /// Saves scene, annotations and sequenced bookmarks. Saving must never cost work (#834):
    /// - everything is serialized in memory first; nothing is written if that fails
    /// - a single object that cannot be serialized (e.g. a non-finite number that slipped
    ///   past the NaN-safe writers) is left out and reported, the rest is saved
    /// - every file is written to a temp file and then moved over the old one, so a failing
    ///   write never truncates an existing scene
    /// Error means nothing changed on disk or in the model, with the reason.
    let saveEverything (path : string) (m : Model) : Result<Model * SaveOutcome, string> =
        if String.IsNullOrWhiteSpace path then Result.Error "no file name given"
        else
            try
                let path = normalizeScenePath path
                let scenePaths = path |> ScenePaths.create
                let mutable problems = []

                // sequenced bookmarks (in memory)
                let bookmarks, bookmarkFiles, bookmarkProblems =
                    BookmarkUtils.serializeSequencedBookmarks scenePaths.bookmarksFolder m.scene.sequencedBookmarks
                problems <- bookmarkProblems @ problems

                // scene (in memory)
                let scene = { m.scene with scenePath          = Some scenePaths.scene
                                           cameraView         = m.navigation.camera.view
                                           exploreCenter      = m.navigation.exploreCenter
                                           navigationMode     = m.navigation.navigationMode
                                           sequencedBookmarks = bookmarks }
                let toWrite = { scene with sequencedBookmarks = BookmarkUtils.unloadBookmarks bookmarks }
                let sceneJson =
                    match trySerialize toWrite with
                    | Result.Ok json -> json
                    | Result.Error e ->
                        Log.error "[Save] scene could not be serialized (%s), isolating the objects at fault" e
                        let surfaces, p1 = withoutUnserializable toWrite.surfacesModel.surfaces
                        let views,    p2 = withoutUnserializable toWrite.bookmarks
                        problems <- List.rev (p1 @ p2) @ problems
                        let toWrite' =
                            { toWrite with
                                surfacesModel = { toWrite.surfacesModel with surfaces = surfaces }
                                bookmarks     = views }
                        match trySerialize toWrite' with
                        | Result.Ok json -> json
                        | Result.Error e -> failwithf "the scene could not be serialized: %s" e

                // annotations (in memory)
                let drawingJson =
                    match trySerialize (Drawing.IO.toAnnotations m.drawing) with
                    | Result.Ok json -> Some json
                    | Result.Error e ->
                        Log.error "[Save] annotations could not be serialized (%s), isolating the annotations at fault" e
                        let annotations, p = withoutUnserializable m.drawing.annotations
                        problems <- List.rev p @ problems
                        match trySerialize (Drawing.IO.toAnnotations { m.drawing with annotations = annotations }) with
                        | Result.Ok json -> Some json
                        | Result.Error e ->
                            Log.error "[Save] annotation file not written: %s" e
                            problems <- sprintf "annotations were NOT saved (the previous annotation file is kept): %s" e :: problems
                            None

                // only now touch the disk
                BookmarkUtils.writeSequencedBookmarks scenePaths.bookmarksFolder bookmarks bookmarkFiles
                Serialization.Chiron.writeToFileAtomic scenePaths.scene sceneJson
                drawingJson |> Option.iter (Serialization.writeToFileAtomic scenePaths.annotations)

                let m = { m with scene = scene } |> Model.stashAndSaveRecent path
                Result.Ok (m, { path = path; problems = List.rev problems })
            with e ->
                Log.error "[Save] saving %s failed: %A" path e
                Result.Error e.Message

    /// `saveEverything`, then the window layout beside the scene (docs/WindowLayouts.md).
    /// The sidecar is written only once the scene is saved and cannot fail the save: a
    /// failure is reported like any other problem.
    let saveEverythingWithLayout (path : string) (m : Model) : Result<Model * SaveOutcome, string> =
        match saveEverything path m with
        | Result.Ok (m, outcome) ->
            match SceneLayoutSidecar.tryWrite outcome.path m.layout.current with
            | Result.Ok () -> Result.Ok (m, outcome)
            | Result.Error e ->
                Log.warn "[Layouts] scene saved, but not the layout beside it (%s): %s" (SceneLayoutSidecar.path outcome.path) e
                let problem = sprintf "window layout not stored beside the scene: %s" e
                Result.Ok (m, { outcome with problems = outcome.problems @ [problem] })
        | Result.Error e -> Result.Error e

