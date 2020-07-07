namespace PRo3D.Minerva

open System
open System.Net.Sockets

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Base.Incremental.Operators
open Aardvark.Base.Rendering
open Aardvark.Rendering.Text 
open Aardvark.Geometry
open Aardvark.SceneGraph
open Aardvark.GeoSpatial.Opc

open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D.Base
open PRo3D.Minerva
open PRo3D.Minerva.Communication

open KdTreeHelper

open Drawing.MissingInBase

open PRo3D.Minerva.Communication.JsonNetworkCommand

open Chiron

        
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
        //let cql1 = [("cql", @"(identifier IN ('" + groupString + "'))")] |> HMap.ofList
        let cqlStringSol = sprintf @"(planetDayNumber >= %f AND planetDayNumber <= %f)"  filter.minSol.value filter.maxSol.value
        let cqlStringInst = constructFilterByInstrument filter
        let cqlStringAll = sprintf @"(" + cqlStringSol + " AND " + cqlStringInst + ")"
        let cql = [("cql", cqlStringSol)] |> HMap.ofList
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

    let adaptFiltersToFeatures (features : plist<Feature>) (model : QueryModel) : QueryModel =
        if features |> PList.isEmpty then
            Log.warn "[Minerva] Mapping filter failed. feature list is empty"
            model
        else        
            Log.startTimed "[Minerva] Mapping filter to features"
            let features = features |> PList.toList
            
            let minSol = features |> List.map(fun x -> x.sol) |> List.min
            let maxSol = features |> List.map(fun x -> x.sol) |> List.max

            let box = features |> List.map(fun x -> x.geometry.positions |> List.head) |> Box3d
            let filterPos = box.Center
            let distance = box.Size.Length


            let instruments = features |> List.groupBy(fun x -> x.instrument) |> HMap.ofList

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

    let _queryModel = MinervaModel.Lens.session |. Session.Lens.queryFilter

    let adaptFiltersToFeatures' model = 
        let queryModel = adaptFiltersToFeatures model.session.filteredFeatures model.session.queryFilter

        model |> Lenses.set _queryModel queryModel

    let private updateSolLabels (features:plist<Feature>) (position : V3d) = 
        if features |> PList.isEmpty then
            HMap.empty
        else
            let features = features |> PList.toList

            let minimum = features|> List.map(fun x -> x.sol) |> List.min
            let maximum = features|> List.map(fun x -> x.sol) |> List.max
            let numberOfLabels = 10
            let nth = max 1 (Range1i(minimum, maximum).Size / 10)
            
            if nth = 0 then
                HMap.empty
            else
                features
                |> List.map(fun x -> x.sol |> string, x.geometry.positions.Head) 
                //|> List.sortBy(fun (_,p) -> V3d.DistanceSquared(position, p))
                |> HMap.ofList //kill duplicates
                |> HMap.toList
                //|> shuffleR (Random())
                //|> ... sortby bla
                |> everyNth nth
                |> List.take' numberOfLabels
                |> HMap.ofList
    
    let private updateSgFeatures (features:plist<Feature>) =
      
        let array = features |> PList.toArray
        
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
    
    let private updateSelectedSgFeature (features:plist<Feature>) (selected:hset<string>) : SgFeatures =
        features
        |> PList.filter( fun x -> HSet.contains x.id selected)
        |> updateSgFeatures
       
    let private setSelection (newSelection: hset<string>) (model: MinervaModel) =
        let selectedSgs = updateSelectedSgFeature model.session.filteredFeatures newSelection
        let session = { model.session with selection = { model.session.selection with selectedProducts = newSelection}}
        Log.line "[MinervaApp] currently %d %d features selected" (newSelection |> HSet.count) (selectedSgs.names.Length)
        { model with session = session; selectedSgFeatures = selectedSgs}

    //let overwriteSelection (selectionIds: list<string>) (model:MinervaModel) =
    //    let newSelection  = selectionIds |> HSet.ofList
    //    setSelection newSelection

    let updateSelectionToggle (names:list<string>) (model:MinervaModel) =
        let newSelection = 
            names
            |> List.fold(fun set name -> 
                match set |> HSet.contains name with
                | true ->  set |> HSet.remove name
                | false -> set |> HSet.add name) model.session.selection.selectedProducts

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
    
    let rebuildKdTree (features : plist<Feature>) (m : SelectionModel): SelectionModel =
        if features |> PList.isEmpty then
            m
        else
            let flatList =
                features 
                |> PList.map(fun x -> x.geometry.positions |> List.head, x.id) 
                |> PList.toArray
            
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
        if model.session.filteredFeatures.IsEmpty() then
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
        if data.features |> PList.isEmpty then
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

    let loadTifs (features: plist<Feature>) =
        let numOfFeatures = (features |> PList.count)
        Log.startTimed "[Minerva] Fetching TIFs %d selected products" numOfFeatures
        let credentials = "minerva:tai8Ies7" 

        let mutable client = new System.Net.WebClient()
        client.UseDefaultCredentials <- true       
        let credentials = System.Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credentials))
        client.Headers.[System.Net.HttpRequestHeader.Authorization] <- "Basic " + credentials  

        features
        |> PList.toList
        |> List.iteri(fun i feature -> 
            Report.Progress(float i / float numOfFeatures)
            Files.loadTifAndConvert client feature.id)

        Log.stop()
        

    // 1087 -> Some(Files.loadTifAndConvert credentials f.id) 
    let loadTifs1087 (model: MinervaModel) =
        let credentials = "minerva:tai8Ies7" 
        let features1087 = 
            model.data.features 
            |> PList.filter(fun (x :Feature) -> x.sol = 1087)

        let numOfFeatures = features1087.Count
        Log.startTimed "[Minerva] Fetching %d TIFs from data file" numOfFeatures

        let mutable client = new System.Net.WebClient()
        client.UseDefaultCredentials <- true       
        let credentials = System.Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credentials))
        client.Headers.[System.Net.HttpRequestHeader.Authorization] <- "Basic " + credentials  

        features1087
        |> PList.toList
        |> List.iteri(fun i feature -> 
            Report.Progress(float i / float numOfFeatures)
            Files.loadTifAndConvert client feature.id)
        
        Log.stop()
        model
            
    let _selection = MinervaModel.Lens.session |. Session.Lens.selection
    let _filtered = MinervaModel.Lens.session |. Session.Lens.filteredFeatures

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
                |> PList.toList 
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
            let imagePath = Path.combine[docPath; imagePathTmp]
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
                  (V2i(width, height)) (Path.combine[imagePath; filename]) 
                  c
            | None -> ()
            model
        | PerformQueries -> failwith "[Minerva] not implemented"
          //try
          //  //let data = model.queries |> MinervaGeoJSON.loadMultiple        
          //  Log.startTimed "[Minerva] Fetching full dataset from Server"
          //  let data = idTestList |> (getIdQuerySite model.queryFilter) |> fun (a,b) -> MinervaGeoJSON.loadPaged a b
          //  //let features = data.features |> PList.sortBy(fun x -> x.sol)    
          //  Log.stop()
    
          //  let queryM = QueryApp.updateFeaturesForRendering model.queryFilter data.features
          //  { model with data = data; queryFilter = queryM }
    
          //with e ->
          //  Log.error "%A" e.Message
          //  model
        | FilterFromSelection ->
            let filtered = 
                model.session.filteredFeatures 
                    |> PList.filter(fun x ->
                        model.session.selection.selectedProducts |> HSet.contains x.id)

            model 
            |> Lenses.set _filtered filtered
            |> adaptFiltersToFeatures'
            |> rebuildKdTree' 
            |> updateFeaturesForRendering
        | SelectByIds selectionIds -> // Set SelectByIds
            let newSelection  = selectionIds |> HSet.ofList
            model |> setSelection newSelection
        | RectangleSelect box ->
            let viewProjTrafo = view.ViewTrafo * Frustum.projTrafo frustum
            
            Log.startTimed "[Minerva] checking %d against bounds" model.session.filteredFeatures.Count

            let featureArray = 
                model.session.filteredFeatures
                |> PList.map (fun x -> struct (x.id |> string, x.geometry.positions.Head))
                |> PList.toArray

            let newSelection = 
                let worstCase = System.Collections.Generic.List(featureArray.Length)
                for i in 0..featureArray.Length-1 do
                    let struct(id, pos) = featureArray.[i]
                    if box.Contains (viewProjTrafo.Forward.TransformPosProj pos) then
                        worstCase.Add id
                worstCase 
                |> HSet.ofSeq

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
            
            model |> Lenses.set _selection selectionModel
        | FilterByIds idList -> 
                    
            Log.line "[Minerva] filtering data to set of %d" idList.Length
            
            let filterSet = idList |> HSet.ofList
            let filtered = 
                model.data.features 
                    |> PList.filter(fun x -> x.id |> filterSet.Contains)

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
                    |> PList.filter(fun x -> model.session.selection.selectedProducts |> HSet.contains x.id)
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
                            singleSelectProduct = None; selectedProducts = HSet.empty; highlightedFrustra = HSet.empty
                    }
            }
            { model with session = session; selectedSgFeatures = updateSgFeatures PList.empty }
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
            let minervaFolder = @".\MinervaData"

            if minervaFolder |> System.IO.Directory.Exists |> not then
                Directory.CreateDirectory(minervaFolder) |> ignore

            Log.line "[Minerva] saving minerva session"
            let path = Path.combine[minervaFolder; "minerva.session.json"]
            
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
    
    let selectionColor (model : MMinervaModel) (feature : Feature) =
        model.session.selection.selectedProducts 
        |> ASet.contains feature.id
        |> Mod.map (fun x -> if x then C4b.VRVisGreen else C4b.White)

    let viewFeatures (instr : Instrument) model (features : list<Feature>) =
        
        features |> List.map(fun f -> 
            let headerAttributes = [ onClick(fun _ -> AddProductToSelection f.id) ]                                      
              
            let iconAttributes =
                [
                    clazz "ui map pin inverted middle aligned icon"                     
                    style (sprintf "color: %s" (Html.ofC4b (f.instrument |> MinervaModel.instrumentColor)))
                    onClick(fun _ -> AddProductToSelection f.id)
                ]
                                                                        
            div [clazz "ui inverted item"][
                i iconAttributes [] //i iconAttributes []
                div [clazz "ui content"] [
                    Incremental.div (AttributeMap.ofList [style (sprintf "color: %s" (Html.ofC4b C4b.White))]) (
                        alist {
                            let! hc = selectionColor model f
                            let c = hc |> Html.ofC4b |> sprintf "color: %s"
                            yield div[clazz "header"; style c][
                                div (headerAttributes) ((instr |> instrumentText |> text) |> List.singleton)
                            ]
                            yield div [clazz "ui description"] [
                                f.sol |> sprintf "Sol: %A" |> text
                                i [clazz "binoculars icon"; onClick (fun _ -> FlyToProduct f.geometry.positions.Head)][]
                                i [clazz "download icon"; onClick (fun _ -> OpenTif f.id)][]
                                i [clazz "folder icon"; onClick (fun _ -> OpenFolder f.id)][]
                            ]
                            //yield i [clazz "binoculars icon"; onClick (fun _ -> FlyToProduct f.geometry.positions.Head)][] //|> UI.wrapToolTip "FlyTo" 
                                     
                        } 
                    )
                ]            
            ])

    let viewFeaturesGui (model : MMinervaModel) =
        let propertiesGui =
            require Html.semui ( 
                Html.table [ 
                    Html.row "point size:" [Numeric.view' [NumericInputType.Slider] model.session.featureProperties.pointSize |> UI.map (fun x -> SetPointSize x)] 
                    Html.row "text size:" [Numeric.view' [NumericInputType.InputBox] model.session.featureProperties.textSize |> UI.map (fun x -> SetTextSize x)] 
                ]        
            )     
            
        let viewFeatureProperties = 
            model.session.selection.singleSelectProduct
            |> Mod.map( fun selected ->
                match selected with
                | Some id ->
                    let feat = 
                        model.session.filteredFeatures
                        |> AList.toList
                        |> List.find(fun x -> x.id = id)
        
                    require Html.semui (
                        Html.table [   
                            Html.row "Instrument:"    [Incremental.text (feat.instrument |> instrumentText |> Mod.constant)] 
                            Html.row "Sol:"           [Incremental.text (feat.sol.ToString() |> Mod.constant)]   
                            Html.row "FlyTo:"         [button [clazz "ui button tiny"; onClick (fun _ -> FlyToProduct feat.geometry.positions.Head )][]]
                            Html.row "Open Img:"      [button [clazz "ui button tiny"; onClick (fun _ -> OpenTif feat.id )][text "img"]]
                            Html.row "Folder"         [i [clazz "folder icon"; onClick (fun _ -> OpenFolder feat.id)][]]
                            ]
                        )
                | None ->  div[style "font-style:italic"][ text "no product selected" ]
            )

        let featuresGroupedByInstrument features =
            adaptive {
                let! features = features |> AList.toMod
                let a = features |> PList.toList |> List.groupBy(fun x -> x.instrument)

                return a |> HMap.ofList
            } |> AMap.ofMod

        let selected = model.session.selection.selectedProducts
        let groupedFeatures = 
            model.session.filteredFeatures 
           // |> AList.filterM(fun x -> selected |> ASet.contains x.id)
            |> featuresGroupedByInstrument
                                      
        let listOfFeatures =
            alist {           
                let! groupedFeatures = groupedFeatures |> AMap.toMod
                
                let! pos = model.session.queryFilter.filterLocation
                for (instr, group) in groupedFeatures do
                
                    let header = sprintf "%s (%d)" (instr |> instrumentText) group.Length
                    
                    Log.line "[Minerva] creating svgs for %A" instr

                    let g = 
                      group 
                        //|> List.sortBy(fun x -> V3d.DistanceSquared(pos, x.geometry.coordinates.Head)) 
                        |> List.take'(20)
                    
                    yield div [clazz "ui inverted item"][
                        yield Html.SemUi.accordion header "circle" false [            
                          div [clazz "ui list"] (viewFeatures instr model g)
                        ]
                    ]
            }
        
        [
            Html.SemUi.accordion "Visplore" "chart bar" true [
                div [clazz "ui buttons"] [
                    button [clazz "ui button tiny"; onClick (fun _ -> ConnectVisplore)][text "Connect"]
                    button [clazz "ui button tiny"; onClick (fun _ -> SendSelection)][text "Send Selection"]
                    button [clazz "ui button tiny"; onClick (fun _ -> SendScreenSpaceCoordinates)][text "Snapshot"]
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
                    button [clazz "ui button tiny"; onClick (fun _ -> ClearFilter)][text "clear"]
                    button [clazz "ui button tiny"; onClick (fun _ -> ApplyFilters)][text "apply"]
                ]
                h4 [clazz "ui"] [text "Selection"]
                div [clazz "ui buttons"] [         
                    button [clazz "ui button tiny"; onClick (fun _ -> ClearSelection)][text "clear"]
                    button [clazz "ui button tiny"; onClick (fun _ -> FilterFromSelection)][text "to filter"]
                    button [clazz "ui button tiny"; onClick (fun _ -> LoadTifs "")][text "load images"]
                    button [clazz "ui button tiny"; onClick (fun _ -> ShowFrustraForSelected)][text "show frustra"]  
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
    
    let viewWrapped pos (model : MMinervaModel) =
        require Html.semui (
            body [style "width: 100%; height:100%; background: #252525; overflow-x: hidden; overflow-y: scroll"] [
                div [clazz "ui inverted segment"] (viewFeaturesGui model)
            ])
    
    // SG
    let viewPortLabels (model : MMinervaModel) (view:IMod<CameraView>) (frustum:IMod<Frustum>) : ISg<MinervaAction> = 
        
        let viewProjTrafo = Mod.map2 (fun (v:CameraView) f -> v.ViewTrafo * Frustum.projTrafo f) view frustum
        let near = frustum |> Mod.map (fun x -> x.near)

        let featureArray = 
            model.session.filteredFeatures
            |> AList.map (fun x -> struct (x.sol |> string, x.geometry.positions.Head))
            |> AList.toMod
            |> Mod.map (fun x -> x |> PList.toArray)

        let box = Box3d(V3d(-1.0, -1.0, 0.0),V3d(1.0,1.0,1.0))

        let visibleFeatures = 
            featureArray 
            |> Mod.map2 (fun (viewProj:Trafo3d) array -> 

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
            |> Mod.map (fun x -> 
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
    let getSolBillboards (model : MMinervaModel) (view:IMod<CameraView>) (near:IMod<float>) : ISg<MinervaAction> =        
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
            
    let viewFilterLocation (model : MMinervaModel) =
        let height = 5.0
        let coneTrafo = 
            model.session.queryFilter.filterLocation 
            |> Mod.map(fun x -> 
                Trafo3d.RotateInto(V3d.ZAxis, -x.Normalized) * Trafo3d.Translation (x + (x.Normalized * height)))
            //lineLength |>
            //  Mod.map(fun s -> Trafo3d.RotateInto(V3d.ZAxis, dip) * Trafo3d.Translation(center' + dip.Normalized * s))

        Drawing.coneISg (C4b.VRVisGreen |> Mod.constant) (0.5 |> Mod.constant) (height |> Mod.constant) coneTrafo
            
    let viewFeaturesSg (model : MMinervaModel) =
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
          view      = viewWrapped (Mod.constant V3d.Zero) //localhost
          update    = update (CameraView.lookAt V3d.Zero V3d.One V3d.OOI) (Frustum.perspective 90.0 0.001 100000.0 1.0)
          initial   = MinervaModel.initial
      }