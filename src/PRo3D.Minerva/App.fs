namespace PRo3D.Minerva

open System
open System.Net.Sockets

open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.Rendering.Text 
open Aardvark.Geometry
open Aardvark.SceneGraph
open OpcViewer.Base

open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D.Base
open PRo3D.Minerva
open PRo3D.Minerva.Communication

open KdTreeHelper

open Drawing.MissingInBase

open PRo3D.Minerva.Communication.JsonNetworkCommand

open Chiron
open Aether
open Aether.Operators
        
module MinervaApp =      

    open System.IO

    let instrumentText (instr : Instrument) =
        match instr with
        | Instrument.MAHLI          -> "MAHLI"
        | Instrument.FrontHazcam    -> "FrontHazcam"
        | Instrument.Mastcam        -> "Mastcam"
        | Instrument.APXS           -> "APXS"
        | Instrument.FrontHazcamR   -> "FrontHazcamR"
        | Instrument.FrontHazcamL   -> "FrontHazcamL"
        | Instrument.MastcamR       -> "MastcamR"    
        | Instrument.MastcamL       -> "MastcamL"
        | Instrument.ChemLib        -> "ChemLib"    
        | Instrument.ChemRmi        -> "ChemRmi"
        | Instrument.NotImplemented -> "not impl. yet"
        | _ -> instr |> sprintf "unknown instrument identifier %A" |> failwith
    
    let writeLinesToFile path (contents : list<string>) =
        System.IO.File.WriteAllLines(path, contents)
    
    let sendMessage2Visplore (address) (port) (message : string) =
        let client = new TcpClient(address, port);
        let data = System.Text.Encoding.ASCII.GetBytes(message)
        let stream = client.GetStream()
        stream.Write(data, 0, data.Length)
        
        printfn "Sending message: %A" message
            
        stream.Close()
        client.Close()        
        
    let constructFilterByInstrument (filter:PRo3D.Minerva.QueryModel) =
        let mutable cqlStringInst = []
        if filter.checkMAHLI then cqlStringInst <- (cqlStringInst @ ["MAHLI"])
        if filter.checkAPXS then cqlStringInst <- (cqlStringInst @ ["APXS"])
        if filter.checkFrontHazcamR then cqlStringInst <- (cqlStringInst @ ["FHAZ_RIGHT_B"])
        if filter.checkFrontHazcamL then cqlStringInst <- (cqlStringInst @ ["FHAZ_LEFT_B"])
        if filter.checkMastcamR then cqlStringInst <- (cqlStringInst @ ["MAST_RIGHT"])
        if filter.checkMastcamL then cqlStringInst <- (cqlStringInst @ ["MAST_LEFT"])
        if filter.checkChemLib then cqlStringInst <- (cqlStringInst @ ["CHEMCAM_LIBS"])
        if filter.checkChemRmi then cqlStringInst <- (cqlStringInst @ ["CHEMCAM_RMI"])
        let groupString = cqlStringInst |> String.concat("','")
        sprintf @"(instrumentId IN ('" + groupString + "'))"
    
    let constructFilterById (filter:QueryModel) (ids:list<string>)  = 
        let groupString = ids |> String.concat("','")
        //let cql1 = [("cql", @"(identifier IN ('" + groupString + "'))")] |> HashMap.ofList
        let cqlStringSol = sprintf @"(planetDayNumber >= %f AND planetDayNumber <= %f)"  filter.minSol.value filter.maxSol.value
        let cqlStringInst = constructFilterByInstrument filter
        let cqlStringAll = sprintf @"(" + cqlStringSol + " AND " + cqlStringInst + ")"
        let cql = [("cql", cqlStringSol)] |> HashMap.ofList
        (@"https://minerva.eox.at/opensearch/collections/all/json/", cql)   
    
    let sendReplaceSelectionRequest (idList : list<string>) (comm : Communicator.Communicator) =         
        let name = "MinervaReplaceSelection"
        let para = {
            names           = [JsonNetworkCommand.Channel.IdList]
            numOfLists      = 1
            numOfEntries    = idList |> List.length
            image           = None
            imageResolution = None
            messageID       = "SelectionMessageID"
        }
    
        let data = { idList = idList; positions = List.empty; colors = List.empty}
    
        let jsonMessage = {name = name; parameters = para; data = data} |> JsonNetworkCommand.toJson
      
        comm.GetClient().SendMessage(jsonMessage)
    
    let sendReplaceProjectionRequest (idList : list<string>) (positions : list<V2d>) (imageResolution : V2i) (imagePath : string) (comm : Communicator.Communicator) =
                      
        let name = "MinervaReplaceProjection"
        let para = 
            { 
              names           = [JsonNetworkCommand.Channel.IdList; JsonNetworkCommand.Channel.Positions]
              numOfLists      = 2
              numOfEntries    = idList |> List.length
              image           = imagePath |> Some
              imageResolution = imageResolution |> Some
              messageID       = ""
            }
        
        let data = { idList = idList; positions = positions; colors = List.empty }
        printfn "names: %A, names: %A" name para.names 
        let jsonMessage = {name = name; parameters = para; data = data} |> JsonNetworkCommand.toJson
        
        comm.GetClient().SendMessage(jsonMessage)
              
    let shuffleR (r : Random) xs = xs |> List.sortBy (fun _ -> r.Next())
    
    let everyNth n elements =
        elements
            |> List.mapi (fun i e -> if i % n = n - 1 then Some(e) else None)
            |> List.choose id

    let adaptFiltersToFeatures (features : IndexList<Feature>) (model : QueryModel) : QueryModel =
        if features |> IndexList.isEmpty then
            Log.warn "[Minerva] Mapping filter failed. feature list is empty"
            model
        else        
            Log.startTimed "[Minerva] Mapping filter to features"
            let features = features |> IndexList.toList
            
            let minSol = features |> List.map(fun x -> x.sol) |> List.min
            let maxSol = features |> List.map(fun x -> x.sol) |> List.max

            let box = features |> List.map(fun x -> x.geometry.positions |> List.head) |> Box3d
            let filterPos = box.Center
            let distance = box.Size.Length


            let instruments = features |> List.groupBy(fun x -> x.instrument) |> HashMap.ofList

            let mahli = instruments.TryFind Instrument.MAHLI |> Option.isSome
            let apxs = instruments.TryFind Instrument.APXS |> Option.isSome
            let chemLib = instruments.TryFind Instrument.ChemLib |> Option.isSome
            let chemRmi = instruments.TryFind Instrument.ChemRmi |> Option.isSome
            let hazL = instruments.TryFind Instrument.FrontHazcamL |> Option.isSome
            let hazR = instruments.TryFind Instrument.FrontHazcamR |> Option.isSome
            let masL = instruments.TryFind Instrument.MastcamL |> Option.isSome
            let masR = instruments.TryFind Instrument.MastcamR |> Option.isSome
            Log.stop()
            {
                model with
                    minSol = { model.minSol with value = float minSol }
                    maxSol = { model.maxSol with value = float maxSol }
                    
                    filterLocation = filterPos
                    distance = { model.distance with value = distance }
                    
                    checkMAHLI = mahli
                    checkAPXS = apxs
                    checkChemLib = chemLib
                    checkChemRmi = chemRmi
                    checkFrontHazcamL = hazL
                    checkFrontHazcamR = hazR
                    checkMastcamL = masL
                    checkMastcamR = masR

                    checkMastcam = masL && masR
                    checkFrontHazcam = hazL && hazR

            }

    let _queryModel = MinervaModel.session_ >-> Session.queryFilter_

    let adaptFiltersToFeatures' model = 
        let queryModel = adaptFiltersToFeatures model.session.filteredFeatures model.session.queryFilter
        Optic.set _queryModel queryModel model

    let private updateSolLabels (features:IndexList<Feature>) (position : V3d) = 
        if features |> IndexList.isEmpty then
            HashMap.empty
        else
            let features = features |> IndexList.toList

            let minimum = features|> List.map(fun x -> x.sol) |> List.min
            let maximum = features|> List.map(fun x -> x.sol) |> List.max
            let numberOfLabels = 10
            let nth = max 1 (Range1i(minimum, maximum).Size / 10)
            
            if nth = 0 then
                HashMap.empty
            else
                features
                |> List.map(fun x -> x.sol |> string, x.geometry.positions.Head) 
                //|> List.sortBy(fun (_,p) -> V3d.DistanceSquared(position, p))
                |> HashMap.ofList //kill duplicates
                |> HashMap.toList
                //|> shuffleR (Random())
                //|> ... sortby bla
                |> everyNth nth
                |> List.take' numberOfLabels
                |> HashMap.ofList
    
    let private updateSgFeatures (features:IndexList<Feature>) =
      
        let array = features |> IndexList.toArray
        
        let names     = array |> Array.map(fun f -> f.id)            
        let positions = array |> Array.map(fun f -> f.geometry.positions.Head)            
        let colors    = array |> Array.map(fun f -> f.instrument |> MinervaModel.instrumentColor )
        
        let trafo =
            match positions |> Array.tryHead with
            | Some p -> Trafo3d.Translation p
            | _ -> Trafo3d.Identity
                         
        {
            names = names
            positions = positions
            colors = colors
            trafo = trafo
        }
    
    let private updateSelectedSgFeature (features:IndexList<Feature>) (selected:HashSet<string>) : SgFeatures =
        features
        |> IndexList.filter( fun x -> HashSet.contains x.id selected)
        |> updateSgFeatures
       
    let private setSelection (newSelection: HashSet<string>) (model: MinervaModel) =
        let selectedSgs = updateSelectedSgFeature model.session.filteredFeatures newSelection
        let session = { model.session with selection = { model.session.selection with selectedProducts = newSelection}}
        Log.line "[MinervaApp] currently %d %d features selected" (newSelection |> HashSet.count) (selectedSgs.names.Length)
        { model with session = session; selectedSgFeatures = selectedSgs}

    //let overwriteSelection (selectionIds: list<string>) (model:MinervaModel) =
    //    let newSelection  = selectionIds |> HashSet.ofList
    //    setSelection newSelection

    let updateSelectionToggle (names:list<string>) (model:MinervaModel) =
        let newSelection = 
            names
            |> List.fold(fun set name -> 
                match set |> HashSet.contains name with
                | true ->  set |> HashSet.remove name
                | false -> set |> HashSet.add name) model.session.selection.selectedProducts

        model |> setSelection newSelection 
    
    let updateFeaturesForRendering (model:MinervaModel) =
        Log.startTimed "[Minerva] building sgs"
        let solLabels  = updateSolLabels  model.session.filteredFeatures model.session.queryFilter.filterLocation //view frustum culling AND distance culling
        let sgFeatures = updateSgFeatures model.session.filteredFeatures
        Log.line "[Minerva] showing %d labels and %d products" solLabels.Count sgFeatures.positions.Length
        Log.stop()
        { model with solLabels = solLabels; sgFeatures = sgFeatures }
    
    let queryClosestPoint model hit =         
        let viewProj = hit.event.evtView * hit.event.evtProj
        let viewPort = V2d hit.event.evtViewport
        let size = 5.0 * 2.0 / viewPort
        let centerNDC = 
            let t = V2d hit.event.evtPixel / viewPort
            V2d(2.0 * t.X - 1.0, 1.0 - 2.0 * t.Y)
        
        let ellipse = Ellipse2d(centerNDC, V2d.IO * size, V2d.OI * size)

        match model.session.selection.kdTree with
        | null -> Seq.empty
        | _ -> 
            let closestPoints = 
                KdTreeQuery.FindPoints(
                    model.session.selection.kdTree, 
                    model.kdTreeBounds, 
                    model.session.selection.flatPos, 
                    viewProj, 
                    ellipse
                )
            
            closestPoints
    
    let rebuildKdTree (features : IndexList<Feature>) (m : SelectionModel): SelectionModel =
        if features |> IndexList.isEmpty then
            m
        else
            let flatList =
                features 
                |> IndexList.map(fun x -> x.geometry.positions |> List.head, x.id) 
                |> IndexList.toArray
            
            let input = flatList |> Array.map fst
            let flatId = flatList |> Array.map snd

            Log.startTimed "[Minerva] build point kdtree"
            let kdTree = PointKdTreeExtensions.CreateKdTree(input, Metric.Euclidean, 1e-5)
            Log.stop()

            { 
                m with
                    kdTree = kdTree
                    flatPos = input
                    flatID = flatId
            }

    let rebuildKdTree' (model : MinervaModel) : MinervaModel =
        if model.session.filteredFeatures.IsEmpty then
            model
        else
            let selectionModel = rebuildKdTree model.session.filteredFeatures model.session.selection  
            let kdTreeBounds = Box3d(selectionModel.flatPos)

            let session = 
                {
                    model.session with selection = selectionModel                                             
                }
            { 
              model with            
                kdTreeBounds = kdTreeBounds                 
                session = session
            }
        
    let updateProducts data (model : MinervaModel) =
        Log.line "[Minerva] found %d entries" data.features.Count   
        if data.features |> IndexList.isEmpty then
            model
        else
            let selectionModel = rebuildKdTree data.features model.session.selection  
            let kdTreeBounds = Box3d(selectionModel.flatPos)

            let session = 
                {
                    model.session with
                        selection = selectionModel                         
                        filteredFeatures = data.features
                }
            { 
              model with
                data = data                
                kdTreeBounds = kdTreeBounds                 
                session = session
            }

    let loadProducts dumpFile cacheFile model =
        Log.startTimed "[Minerva] Fetching full dataset from data file"
        let data = MinervaModel.loadDumpCSV dumpFile cacheFile
        Log.stop()     
        
        updateProducts data model

    let loadTifs (features: IndexList<Feature>) =
        let numOfFeatures = (features |> IndexList.count)
        Log.startTimed "[Minerva] Fetching TIFs %d selected products" numOfFeatures
        let credentials = "minerva:tai8Ies7" 

        let mutable client = new System.Net.WebClient()
        client.UseDefaultCredentials <- true       
        let credentials = System.Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credentials))
        client.Headers.[System.Net.HttpRequestHeader.Authorization] <- "Basic " + credentials  

        features
        |> IndexList.toList
        |> List.iteri(fun i feature -> 
            Report.Progress(float i / float numOfFeatures)
            Files.loadTifAndConvert client feature.id)

        Log.stop()
        

    // 1087 -> Some(Files.loadTifAndConvert credentials f.id) 
    let loadTifs1087 (model: MinervaModel) =
        let credentials = "minerva:tai8Ies7" 
        let features1087 = 
            model.data.features 
            |> IndexList.filter(fun (x :Feature) -> x.sol = 1087)

        let numOfFeatures = features1087.Count
        Log.startTimed "[Minerva] Fetching %d TIFs from data file" numOfFeatures

        let mutable client = new System.Net.WebClient()
        client.UseDefaultCredentials <- true       
        let credentials = System.Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credentials))
        client.Headers.[System.Net.HttpRequestHeader.Authorization] <- "Basic " + credentials  

        features1087
        |> IndexList.toList
        |> List.iteri(fun i feature -> 
            Report.Progress(float i / float numOfFeatures)
            Files.loadTifAndConvert client feature.id)
        
        Log.stop()
        model
            
    let _selection = MinervaModel.session_ >-> Session.selection_
    let _filtered = MinervaModel.session_ >-> Session.filteredFeatures_

    let mutable lastHit = String.Empty
    let update (view:CameraView) frustum (model : MinervaModel) (msg : MinervaAction) : MinervaModel =
        match msg with     
        | SendSelection -> 
            match model.comm with
            | Some c -> 
                Log.line "[Minerva:] Sending %d selected products" model.selectedSgFeatures.names.Length
                sendReplaceSelectionRequest (model.selectedSgFeatures.names |> Array.toList) c
            | None   -> ()

            model
        | SendScreenSpaceCoordinates -> 
            let viewProj = view.ViewTrafo * Frustum.projTrafo frustum

            Log.line "[Minerva] %A features available" model.session.filteredFeatures.Count

            let coords = 
                model.session.filteredFeatures
                |> IndexList.toList 
                |> List.map(fun feat  -> (feat.id, feat.geometry.positions.Head))
                |> List.map(fun (id,p) -> (id,viewProj.Forward.TransformPosProj p))
                |> List.filter(fun (_,p) -> Box3d(V3d(-1.0, -1.0, 0.0),V3d(1.0,1.0,1.0)).Contains p)
                |> List.map(fun (id,p) ->                    
                    let coord = (V2d(p.X, p.Y) + V2d.One) * 0.5
                    (id, coord))

            Log.line "[Minerva] %A in screenspace" coords.Length
            
            let width = 1920
            let height = ((float)width / (frustum |> Frustum.aspect)) |> int
            
            let docPath = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            let imagePathTmp = @"visplore\Minerva"
            let imagePath = Path.combine [docPath; imagePathTmp]
            let filename = "overview.jpg"
            
            Log.startTimed "[Minerva taking] Screenshot %A" (V2i(width, height))
            PRo3D.Base.ScreenshotUtilities.Utilities.takeScreenshotFromAllViews "http://localhost:54322" width height filename imagePath |> ignore
            Log.stop()
            
            match model.comm with
            | Some c -> 
                Log.line "[Minerva] Sending coords %d" coords.Length
                sendReplaceProjectionRequest 
                  (coords |> List.map fst) 
                  (coords |> List.map snd) 
                  (V2i(width, height)) (Path.combine [imagePath; filename]) 
                  c
            | None -> ()
            model
        | PerformQueries -> failwith "[Minerva] not implemented"
          //try
          //  //let data = model.queries |> MinervaGeoJSON.loadMultiple        
          //  Log.startTimed "[Minerva] Fetching full dataset from Server"
          //  let data = idTestList |> (getIdQuerySite model.queryFilter) |> fun (a,b) -> MinervaGeoJSON.loadPaged a b
          //  //let features = data.features |> IndexList.sortBy(fun x -> x.sol)    
          //  Log.stop()
    
          //  let queryM = QueryApp.updateFeaturesForRendering model.queryFilter data.features
          //  { model with data = data; queryFilter = queryM }
    
          //with e ->
          //  Log.error "%A" e.Message
          //  model
        | FilterFromSelection ->
            let filtered = 
                model.session.filteredFeatures 
                    |> IndexList.filter(fun x ->
                        model.session.selection.selectedProducts |> HashSet.contains x.id)

            model 
            |> Optic.set _filtered filtered
            |> adaptFiltersToFeatures'
            |> rebuildKdTree' 
            |> updateFeaturesForRendering
        | SelectByIds selectionIds -> // Set SelectByIds
            let newSelection  = selectionIds |> HashSet.ofList
            model |> setSelection newSelection
        | RectangleSelect box ->
            let viewProjTrafo = view.ViewTrafo * Frustum.projTrafo frustum
            
            Log.startTimed "[Minerva] checking %d against bounds" model.session.filteredFeatures.Count

            let featureArray = 
                model.session.filteredFeatures
                |> IndexList.map (fun x -> struct (x.id |> string, x.geometry.positions.Head))
                |> IndexList.toArray

            let newSelection = 
                let worstCase = System.Collections.Generic.List(featureArray.Length)
                for i in 0..featureArray.Length-1 do
                    let struct(id, pos) = featureArray.[i]
                    if box.Contains (viewProjTrafo.Forward.TransformPosProj pos) then
                        worstCase.Add id
                worstCase 
                |> HashSet.ofSeq

            Log.stop()
            Log.line "[Minerva] Selected %d" newSelection.Count

            if newSelection.IsEmptyOrNull() then
                //Log.line "Selection-Rect is empty"
                model
            else 
                //Log.line "Found %i Features within selection rect" newSelection.Count 
                model |> setSelection newSelection
        | ShowFrustraForSelected ->
            Log.line "[Minervar] computing frustra for %d products" model.session.selection.selectedProducts.Count
            let selectionModel = 
                { model.session.selection with highlightedFrustra = model.session.selection.selectedProducts }
            
            model |> Optic.set _selection selectionModel
        | FilterByIds idList -> 
                    
            Log.line "[Minerva] filtering data to set of %d" idList.Length
            
            let filterSet = idList |> HashSet.ofList
            let filtered = 
                model.data.features 
                    |> IndexList.filter(fun x -> x.id |> filterSet.Contains)

            let queries = 
                adaptFiltersToFeatures filtered model.session.queryFilter

            let session = { model.session with filteredFeatures = filtered; queryFilter = queries }

            { model with session = session } 
            |> adaptFiltersToFeatures' 
            |> rebuildKdTree' 
            |> updateFeaturesForRendering
        | ConnectVisplore -> 
            if model.vplMessages.store.IsEmpty then          
                Log.line "[Minerva] connecting to visplore"
                let comm = if model.comm.IsSome then model.comm.Value else new Communicator.Communicator()
                let createWorker () =
                   proclist {
                       let bc = new DataStructures.HarriSchirchWrongBlockingCollection<_>()
                       comm.Start(fun cmd ->
                           match cmd.name with
                           | "MinervaReplaceSelection" ->
                              Log.line "[Minerva] received vpl selection message"                                        
                              let action = cmd.data.idList |> SelectByIds 
                              bc.Enqueue action
                              cmd.parameters.messageID
                           | "MinervaReplaceFilter" ->
                              Log.line "[Minerva] received vpl filter message"
                              let action = cmd.data.idList |> FilterByIds
                              bc.Enqueue action
                              cmd.parameters.messageID
                           | "Shutdown" -> 
                              //bc.CompleteAdding()
                              "todo"
                           | _ -> sprintf "dont know %A" cmd               
                       )
                       while not bc.IsCompleted do
                           let! action = bc.TakeAsync()
                           Log.line "[Minerva] take async"
                           match action with
                           | Some a -> yield a
                           | None -> ()
                       yield FlyToProduct V3d.Zero
                   }          
                { model with vplMessages = ThreadPool.add "vpl server" (createWorker()) model.vplMessages; comm = Some comm}
            else
              model
        | FlyToProduct _ -> model //handled in higher level app
        
        // TODO....refactor openTif and loadTif
        | OpenTif id -> 
            Files.downloadAndOpenTif id model            
        | LoadTifs access ->
            loadTifs (
                model.session.filteredFeatures 
                    |> IndexList.filter(fun x -> model.session.selection.selectedProducts |> HashSet.contains x.id)
                )
            model
        | LoadProducts (dumpFile, cacheFile) -> 
            model |> loadProducts dumpFile cacheFile
        | QueryMessage msg -> 
            let filters = QueryApp.update model.session.queryFilter msg        
            let filtered = QueryApp.applyFilterQueries model.data.features filters
            let session = { model.session with queryFilter = filters; filteredFeatures = filtered }

            { model with session = session } |> rebuildKdTree' |> updateFeaturesForRendering
        | ApplyFilters ->
            let filtered = QueryApp.applyFilterQueries model.data.features model.session.queryFilter
            let session = 
                { model.session with 
                    filteredFeatures = filtered 
                    queryFilter = model.session.queryFilter |> adaptFiltersToFeatures model.data.features
                    } 
            { model with session = session } 
            |> rebuildKdTree' 
            |> updateFeaturesForRendering
        | ClearFilter ->            
            let session = 
                { model.session with 
                    filteredFeatures = model.data.features;
                    queryFilter = model.session.queryFilter |> adaptFiltersToFeatures model.data.features }

            { model with session = session } 
            |> rebuildKdTree' 
            |> updateFeaturesForRendering
        | SingleSelectProduct name ->
            let session = { model.session with selection = { model.session.selection with singleSelectProduct = Some name }}
            { model with session = session }
        | ClearSelection ->
            let session = { 
                model.session with 
                    selection = { 
                        model.session.selection with 
                            singleSelectProduct = None; selectedProducts = HashSet.empty; highlightedFrustra = HashSet.empty
                    }
            }
            { model with session = session; selectedSgFeatures = updateSgFeatures IndexList.empty }
        | AddProductToSelection name ->
            let m' = updateSelectionToggle [name] model
            let session = { m'.session with selection = { m'.session.selection with singleSelectProduct = Some name } }
            { m' with session = session }
        | PickProducts hit -> 
            let closestPoints = queryClosestPoint model hit
            match closestPoints with
            | emptySeq when Seq.isEmpty emptySeq -> model
            | seq -> 
                let hitString = 
                    hit.globalRay.Ray.Ray.Origin.ToString() + 
                    hit.globalRay.Ray.Ray.Direction.ToString()

                if hitString = lastHit then
                    model
                else
                    let index = seq |> Seq.map (fun (depth, pos, index) -> index) |> Seq.head
                    let closestID = model.session.selection.flatID.[index]
                    lastHit <- hitString
                    updateSelectionToggle [closestID] model                             
        | HoverProducts hit ->
            //Report.BeginTimed("hover-update") |> ignore
            
            let closestPoints = queryClosestPoint model hit
    
            let updateModel = 
                match closestPoints with
                | emptySeq when Seq.isEmpty emptySeq -> 
                    match model.hoveredProduct with
                    | None -> model
                    | Some _ -> { model with hoveredProduct = None}
                | seq -> 
                    let depth, pos, index = seq |> Seq.head
                    let id = model.session.selection.flatID.[index]
                    { model with hoveredProduct = Some { id = id; pos = pos} }
            //Report.EndTimed() |> ignore
    
            updateModel
        | HoverProduct o ->
            { model with hoveredProduct = o }
        | Save ->
            let minervaFolder = Path.combine [Config.besideExecuteable; "MinervaData"]

            if minervaFolder |> System.IO.Directory.Exists |> not then
                Directory.CreateDirectory(minervaFolder) |> ignore

            Log.line "[Minerva] saving minerva session"
            let path = Path.combine [minervaFolder; "minerva.session.json"]
            
            model.session 
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.SingleLine 
            |> Serialization.Chiron.writeToFile path

            model
        | Load -> 
            Log.line "[Minerva] loading minerva session"
            let path = @".\MinervaData\minerva.session.json"
            let session = 
                if System.IO.File.Exists path then            
                    Log.startTimed "[ViewerIO] Loading Minerva session"
                    let session = 
                        path 
                        |> Serialization.Chiron.readFromFile 
                        |> Json.parse 
                        |> Json.deserialize
                    Log.stop()
                    session
                else
                    PRo3D.Minerva.Session.initial

            let model = model |> updateProducts model.data 
            let filtered = QueryApp.applyFilterQueries model.data.features session.queryFilter
            let session = { session with filteredFeatures = filtered }  

            { model with session = session } |> rebuildKdTree' |> updateFeaturesForRendering
        | SetPointSize s ->
            let size = Numeric.update model.session.featureProperties.pointSize s

            let session = { model.session with featureProperties = { model.session.featureProperties with pointSize = size } }
            { model with session = session }
        | SetTextSize s ->
            let size = Numeric.update model.session.featureProperties.textSize s
            let session = { model.session with featureProperties = { model.session.featureProperties with textSize = size }}   
            { model with session = session }
    
    let selectionColor (model : AdaptiveMinervaModel) (feature : Feature) =
        failwith ""
        //model.session.selection.selectedProducts 
        //|> ASet.contains feature.id
        //|> AVal.map (fun x -> if x then C4b.VRVisGreen else C4b.White)

    let viewFeatures (instr : Instrument) model (features : list<Feature>) =
        
        features |> List.map(fun f -> 
            let headerAttributes = [ onClick(fun _ -> AddProductToSelection f.id) ]                                      
              
            let iconAttributes =
                [
                    clazz "ui map pin inverted middle aligned icon"                     
                    style (sprintf "color: %s" (Html.ofC4b (f.instrument |> MinervaModel.instrumentColor)))
                    onClick(fun _ -> AddProductToSelection f.id)
                ]
                                                                        
            div [clazz "ui inverted item"] [
                i iconAttributes [] //i iconAttributes []
                div [clazz "ui content"] [
                    Incremental.div (AttributeMap.ofList [style (sprintf "color: %s" (Html.ofC4b C4b.White))]) (
                        alist {
                            let! hc = selectionColor model f
                            let c = hc |> Html.ofC4b |> sprintf "color: %s"
                            yield div [clazz "header"; style c] [
                                div (headerAttributes) ((instr |> instrumentText |> text) |> List.singleton)
                            ]
                            yield div [clazz "ui description"] [
                                f.sol |> sprintf "Sol: %A" |> text
                                i [clazz "binoculars icon"; onClick (fun _ -> FlyToProduct f.geometry.positions.Head)] []
                                i [clazz "download icon"; onClick (fun _ -> OpenTif f.id)] []
                                i [clazz "folder icon"; onClick (fun _ -> OpenFolder f.id)] []
                            ]
                            //yield i [clazz "binoculars icon"; onClick (fun _ -> FlyToProduct f.geometry.positions.Head)][] //|> UI.wrapToolTip "FlyTo" 
                                     
                        } 
                    )
                ]            
            ])

    let viewFeaturesGui (model : AdaptiveMinervaModel) =
        let propertiesGui =
            require Html.semui ( 
                Html.table [ 
                    Html.row "point size:" [Numeric.view' [NumericInputType.Slider] model.session.featureProperties.pointSize |> UI.map (fun x -> SetPointSize x)] 
                    Html.row "text size:" [Numeric.view' [NumericInputType.InputBox] model.session.featureProperties.textSize |> UI.map (fun x -> SetTextSize x)] 
                ]        
            )     
            
        let viewFeatureProperties = 
            model.session.selection.singleSelectProduct
            |> AVal.map( fun selected ->
                match selected with
                | Some id ->
                    let feat = 
                        model.session.filteredFeatures
                        |> AList.force
                        |> IndexList.toList
                        |> List.find(fun x -> x.id = id)
        
                    require Html.semui (
                        Html.table [   
                            Html.row "Instrument:"    [Incremental.text (feat.instrument |> instrumentText |> AVal.constant)] 
                            Html.row "Sol:"           [Incremental.text (feat.sol.ToString() |> AVal.constant)]   
                            Html.row "FlyTo:"         [button [clazz "ui button tiny"; onClick (fun _ -> FlyToProduct feat.geometry.positions.Head )] []]
                            Html.row "Open Img:"      [button [clazz "ui button tiny"; onClick (fun _ -> OpenTif feat.id )] [text "img"]]
                            Html.row "Folder"         [i [clazz "folder icon"; onClick (fun _ -> OpenFolder feat.id)] []]
                            ]
                        )
                | None ->  div [style "font-style:italic"] [text "no product selected"]
            )

        let featuresGroupedByInstrument features =
            adaptive {
                let! features = features |> AList.toAVal
                let a = features |> IndexList.toList |> List.groupBy(fun x -> x.instrument)

                return a |> HashMap.ofList
            } |> AMap.ofAVal

        let selected = model.session.selection.selectedProducts
        let groupedFeatures = 
            model.session.filteredFeatures 
           // |> AList.filterM(fun x -> selected |> ASet.contains x.id)
            |> featuresGroupedByInstrument
                                      
        let listOfFeatures =
            alist {           
                let! groupedFeatures = groupedFeatures |> AMap.toAVal
                
                let! pos = model.session.queryFilter.filterLocation
                for (instr, group) in groupedFeatures do
                
                    let header = sprintf "%s (%d)" (instr |> instrumentText) group.Length
                    
                    Log.line "[Minerva] creating svgs for %A" instr

                    let g = 
                      group 
                        //|> List.sortBy(fun x -> V3d.DistanceSquared(pos, x.geometry.coordinates.Head)) 
                        |> List.take'(20)
                    
                    yield div [clazz "ui inverted item"] [
                        yield Html.SemUi.accordion header "circle" false [            
                          div [clazz "ui list"] (viewFeatures instr model g)
                        ]
                    ]
            }
        
        [
            Html.SemUi.accordion "Visplore" "chart bar" true [
                div [clazz "ui buttons"] [
                    button [clazz "ui button tiny"; onClick (fun _ -> ConnectVisplore)] [text "Connect"]
                    button [clazz "ui button tiny"; onClick (fun _ -> SendSelection)] [text "Send Selection"]
                    button [clazz "ui button tiny"; onClick (fun _ -> SendScreenSpaceCoordinates)] [text "Snapshot"]
                    //button [
              //  clazz "ui button"; 
              //  onEvent "onGetRenderId" [] (fun args -> Reset)
              //  clientEvent "onclick" "aardvark.processEvent(__ID__,'onGetRenderId', document.getElementsByClassName('mainrendercontrol')[0].id)"
              //] [text "SCREAM SHOT"]
                ]
            ]
            
            Html.SemUi.accordion "Query App" "filter" true [
                h4 [clazz "ui"] [text "Filter"]
                div [clazz "ui buttons"] [         
                    //button [clazz "ui button"; onClick (fun _ -> Save)][text "Save"]
              //button [clazz "ui button"; onClick (fun _ -> Load)][text "Load"]
                    button [clazz "ui button tiny"; onClick (fun _ -> ClearFilter)] [text "clear"]
                    button [clazz "ui button tiny"; onClick (fun _ -> ApplyFilters)] [text "apply"]
                ]
                h4 [clazz "ui"] [text "Selection"]
                div [clazz "ui buttons"] [         
                    button [clazz "ui button tiny"; onClick (fun _ -> ClearSelection)] [text "clear"]
                    button [clazz "ui button tiny"; onClick (fun _ -> FilterFromSelection)] [text "to filter"]
                    button [clazz "ui button tiny"; onClick (fun _ -> LoadTifs "")] [text "load images"]
                    button [clazz "ui button tiny"; onClick (fun _ -> ShowFrustraForSelected)] [text "show frustra"]  
                ]
                
                QueryApp.viewQueryFilters groupedFeatures model.session.queryFilter |> UI.map QueryMessage
                propertiesGui          
            ]

            //viewFeatureProperties

            //showing grouped lists of features
            Incremental.div 
                ([clazz "ui very compact stackable inverted relaxed divided list"] |> AttributeMap.ofList) 
                listOfFeatures
                        
            //Incremental.div 
            //    ([clazz "ui very compact stackable inverted relaxed divided list"] |> AttributeMap.ofList) 
            //    listOfFeatures //AList.empty  
                        
            //Html.SemUi.accordion "Mapping" "settings" false [ 
            //    Incremental.div AttributeMap.empty (viewFeatureProperties |> AList.ofModSingle)
            //]                                   
                       
         ]
    
    let viewWrapped pos (model : AdaptiveMinervaModel) =
        require Html.semui (
            body [style "width: 100%; height:100%; background: #252525; overflow-x: hidden; overflow-y: scroll"] [
                div [clazz "ui inverted segment"] (viewFeaturesGui model)
            ])
    
    // SG
    let viewPortLabels (model : AdaptiveMinervaModel) (view:aval<CameraView>) (frustum:aval<Frustum>) : ISg<MinervaAction> = 
        
        let viewProjTrafo = AVal.map2 (fun (v:CameraView) f -> v.ViewTrafo * Frustum.projTrafo f) view frustum
        let near = frustum |> AVal.map (fun x -> x.near)

        let featureArray = 
            model.session.filteredFeatures
            |> AList.map (fun x -> struct (x.sol |> string, x.geometry.positions.Head))
            |> AList.toAVal
            |> AVal.map (fun x -> x |> IndexList.toArray)

        let box = Box3d(V3d(-1.0, -1.0, 0.0),V3d(1.0,1.0,1.0))

        let visibleFeatures = 
            featureArray 
            |> AVal.map2 (fun (viewProj:Trafo3d) array -> 

                //Log.startTimed "filterStart"

                let worstCase = System.Collections.Generic.List(array.Length)
                
                for i in 0..array.Length-1 do
                    let struct(id, pos) = array.[i]
                    if box.Contains (viewProj.Forward.TransformPosProj pos) then
                        worstCase.Add struct (id, pos)

                //Log.stop()

                worstCase

            ) viewProjTrafo

        let maxCount = 5.0

        let topFeatures =
            visibleFeatures
            |> AVal.map (fun x -> 
                let count = float x.Count
                if count < maxCount then
                    x.ToArray()
                else 
                    let stepSize = count / maxCount
                    [|
                        for i in 0.0..stepSize..count-1.0 do
                            yield x.[i.Ceiling() |> int]
                    |])

        let sg =
            topFeatures 
            |> ASet.bind (fun x -> 
                x 
                |> Array.map (fun struct (text, pos) -> 
                        Sg.text view near ~~60.0 ~~pos ~~(Trafo3d.Translation pos) ~~0.05 ~~text) // (model.session.featureProperties.textSize.value)) 
                |> ASet.ofArray)
            |> Sg.set

        sg
    
    // fix-size billboards (expensive!)
    let getSolBillboards (model : AdaptiveMinervaModel) (view:aval<CameraView>) (near:aval<float>) : ISg<MinervaAction> =        
        model.solLabels
        |> AMap.map(fun txt pos ->
           Sg.text view near 
              ~~60.0
              ~~pos
              ~~(Trafo3d.Translation pos)
              (model.session.featureProperties.textSize.value)
              ~~txt
        ) 
        |> AMap.toASet  
        |> ASet.map(fun x -> snd x)
        |> Sg.set
            
    let viewFilterLocation (model : AdaptiveMinervaModel) =
        let height = 5.0
        let coneTrafo = 
            model.session.queryFilter.filterLocation 
            |> AVal.map(fun x -> 
                Trafo3d.RotateInto(V3d.ZAxis, -x.Normalized) * Trafo3d.Translation (x + (x.Normalized * height)))
            //lineLength |>
            //  AVal.map(fun s -> Trafo3d.RotateInto(V3d.ZAxis, dip) * Trafo3d.Translation(center' + dip.Normalized * s))

        Drawing.coneISg (C4b.VRVisGreen |> AVal.constant) (0.5 |> AVal.constant) (height |> AVal.constant) coneTrafo
            
    let viewFeaturesSg (model : AdaptiveMinervaModel) =
        let pointSize = model.session.featureProperties.pointSize.value
        
        Sg.ofList [
            Drawing.featureMousePick model.kdTreeBounds
            Drawing.drawFeaturePoints model.sgFeatures pointSize
            Drawing.drawSelectedFeaturePoints model.selectedSgFeatures pointSize
            Drawing.drawHoveredFeaturePoint model.hoveredProduct pointSize model.sgFeatures.trafo
        ]
    
    let threads (m : MinervaModel) = m.vplMessages
   
    let start()  =
      
      App.start {
          unpersist = Unpersist.instance
          threads   = fun m -> m.vplMessages
          view      = viewWrapped (AVal.constant V3d.Zero) //localhost
          update    = update (CameraView.lookAt V3d.Zero V3d.One V3d.OOI) (Frustum.perspective 90.0 0.001 100000.0 1.0)
          initial   = MinervaModel.initial
      }