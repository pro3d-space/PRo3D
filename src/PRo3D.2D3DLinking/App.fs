namespace LinkingTestApp

//open System
//open System.IO

//open Aardvark.Base
//open FSharp.Data.Adaptive
//open Aardvark.Rendering

//open Aardvark.Rendering.Text

//open Aardvark.SceneGraph
//open Aardvark.SceneGraph.Opc
//open FShade

//open Aardvark.Application
//open Aardvark.UI
//open Aardvark.UI.Primitives
//open Aardvark.UI.Trafos

//open OpcViewer.Base
//open OpcViewer.Base.Picking
//open PRo3D.Base
//open PRo3D.Minerva
//open Aardvark.VRVis.Opc
//open PRo3D.Linking

//open Aether
//open Aether.Operators

//module App = 
    
//    let updateFreeFlyConfig (incr : float) (cam : CameraControllerState) = 
//        let s' = cam.freeFlyConfig.moveSensitivity + incr
//        Log.line "[App] sensitivity: %A" s'
//        let config = 
//            { 
//            cam.freeFlyConfig with
//                panMouseSensitivity       = exp(s') * 0.0025
//                dollyMouseSensitivity     = exp(s') * 0.0025
//                zoomMouseWheelSensitivity = exp(s') * 0.1
//                moveSensitivity           = s'          
//            }    

//        { cam with freeFlyConfig = config }
    
//    //---saving and restoring camera state
//    let toCameraStateLean (view : CameraView) : CameraStateLean = 
//        {
//        location = view.Location
//        forward  = view.Forward
//        sky      = view.Sky
//        }

//    let fromCameraStateLean (c : CameraStateLean) : CameraView = 
//        CameraView.lookAt c.location (c.location + c.forward) c.sky    
//    //---


//    //---saving and restoring plane state
//    let toPlaneCoords (coords : IndexList<V3d>): PlaneCoordinates =
//        {
//        points = coords
//        }

//    let fromPlaneCoords (c : PlaneCoordinates) : IndexList<V3d> =
//        c.points
//    //---

//    //---UPDATE
//    let update (model : Model) (msg : LinkingTestApp.Action) =   
//        match msg with
//        | Camera m when model.pickingActive = false -> 
//            { model with cameraState = FreeFlyController.update model.cameraState m; }

//        | Action.KeyDown m ->
//         match m with
//          | Keys.LeftCtrl -> 
//            { model with pickingActive = true }
//          | _ -> model
//        | Action.KeyUp m ->
//            match m with
//            | Keys.LeftCtrl -> 
//                { model with pickingActive = false }
//            | Keys.Delete ->            
//                { model with pickingModel = PickingApp.update model.pickingModel (PickingAction.ClearPoints) }
//            | Keys.Back ->
//                { model with pickingModel = PickingApp.update model.pickingModel (PickingAction.RemoveLastPoint) }
//            | Keys.PageUp ->             
//                { model with cameraState = model.cameraState |>  updateFreeFlyConfig +0.5 }
//            | Keys.PageDown ->             
//                { model with cameraState = model.cameraState |>  updateFreeFlyConfig -0.5 }
//            | _ -> model


//        | PickingAction msg ->
//            // TODO...refactor this!
//            let pickingModel, linkingModel, minervaModel =
//              match msg with
//              | HitSurface (a,b) -> //,_) -> 
//                let updatePickM = PickingApp.update model.pickingModel (HitSurface (a,b))
//                let lastPick = updatePickM.intersectionPoints |> IndexList.tryFirst

//                let updatedLinkingM, updatedMinervaM = 
//                    match lastPick with
//                    | Some p -> 
//                        let filtered = model.minervaModel.session.filteredFeatures |> IndexList.map (fun f -> f.id) |> IndexList.toList |> HashSet.ofList
//                        let linkingAction, minervaAction = PRo3D.Linking.LinkingApp.checkPoint p filtered model.linkingModel
//                        let minerva' = MinervaApp.update model.cameraState.view model.mainFrustum model.minervaModel minervaAction
//                        let linking' = LinkingApp.update model.linkingModel linkingAction
//                        (linking', minerva')
//                    | None -> (model.linkingModel, model.minervaModel)

//                updatePickM, updatedLinkingM, updatedMinervaM
//              | _ -> (PickingApp.update model.pickingModel msg), model.linkingModel, model.minervaModel
//            { model with pickingModel = pickingModel; linkingModel = linkingModel; minervaModel = minervaModel }

//        | Action.MinervaAction msg ->
//            { model with minervaModel = MinervaApp.update model.cameraState.view model.mainFrustum model.minervaModel msg }

//        | LinkingAction msg ->
//            match msg with
//            | OpenFrustum d ->
//                let updatedLinking = LinkingApp.update model.linkingModel msg
//                let newCamState = { model.cameraState with view = CameraView.ofTrafo d.f.camTrafo }
//                { model with cameraState = newCamState; overlayFrustum = Some(d.f.camFrustum); linkingModel = updatedLinking }
//            | CloseFrustum ->
//                let updatedLinking = LinkingApp.update model.linkingModel CloseFrustum
//                { model with overlayFrustum = None; linkingModel = updatedLinking }
//            | _ -> 
//                { model with linkingModel = LinkingApp.update model.linkingModel msg }

//        | UpdateDockConfig cfg ->
//            { model with dockConfig = cfg }
//        | _ -> model
    
//    //---

//    //---VIEW
//    let view (m : AdaptiveModel) =
                               
//        let opcs = 
//            m.opcInfos
//                |> AMap.toASet
//                |> ASet.map(fun info -> Sg.createSingleOpcSg (AVal.constant None) m.pickingActive m.cameraState.view info)
//                |> Sg.set
//                |> Sg.effect [ 
//                    toEffect Shader.stableTrafo
//                    Shader.OPCFilter.EffectOPCFilter
//                ]

//        let near = m.mainFrustum |> AVal.map(fun x -> x.near)
//        let far = m.mainFrustum |> AVal.map(fun x -> x.far)

//        let scene = 
//            [
//                opcs |> Sg.map PickingAction
//                LinkingApp.view m.minervaModel.hoveredProduct m.minervaModel.session.selection.selectedProducts m.linkingModel |> Sg.map LinkingAction
//                MinervaApp.viewFeaturesSg m.minervaModel |> Sg.map Action.MinervaAction
//            ]
//            |> Sg.ofList

//        let textOverlays (cv : aval<CameraView>) = 
//            div [js "oncontextmenu" "event.preventDefault();"] [ 
//                let style' = "color: white; font-family:Consolas;"

//                yield div [clazz "ui"; style "position: absolute; top: 15px; left: 15px; float:left; z-index: 20" ] [          
//                    yield table [] [
//                        tr[][
//                            td[style style'][Incremental.text(cv |> AVal.map(fun x -> x.Location.ToString("0.00")))]
//                        ]
//                    ]
//                ]
//            ]

//        let renderControl =
//            FreeFlyController.controlledControl m.cameraState Camera 
//                (AVal.map2(fun o m -> o |> Option.defaultValue m) m.overlayFrustum m.mainFrustum)
//                (AttributeMap.ofList [ 
//                    style "width: 100%; height:100%"; 
//                    attribute "showFPS" "true";       // optional, default is false
//                    attribute "useMapping" "true"
//                    attribute "data-renderalways" "false"
//                    attribute "data-samples" "4"
//                    onKeyDown (Action.KeyDown)
//                    onKeyUp (Action.KeyUp)
//                ]) 
//                (scene)
 
//        page (fun request -> 
//        match Map.tryFind "page" request.queryParams with
//        | Some "render" ->
//            require Html.semui ( // we use semantic ui for our gui. the require function loads semui stuff such as stylesheets and scripts
//                div [clazz "ui"; style "background: #1B1C1E"] [
//                    renderControl
//                    textOverlays (m.cameraState.view)
//                    LinkingApp.sceneOverlay m.linkingModel |> UI.map LinkingAction
//                ]
//            )
//        | Some "controls" -> 
//            require Html.semui (
//                body [style "width: 100%; height:100%; background: transparent; min-width: 0; min-height: 0";] [
//                    div[style "color:white; padding: 5px 15px 5px 5px"][
//                    h3[][text "2D/3D Linking"]
//                    p[][text "Hold Ctrl-Left to Pick Point"]

//                    LinkingApp.viewSideBar m.linkingModel |> UI.map LinkingAction

//                    ]
//                ]
//            )
//        | Some "products" -> LinkingApp.viewHorizontalBar m.minervaModel.session.selection.selectedProducts m.linkingModel |> UI.map LinkingAction
//        | Some other -> 
//            let msg = sprintf "Unknown page: %A" other
//            body [] [
//                div [style "color: white; font-size: large; background-color: red; width: 100%; height: 100%"] [text msg]
//            ]  
//        | None -> 
//            m.dockConfig
//            |> docking [
//                style "width:100%; height:100%; background:#F00"
//                onLayoutChanged UpdateDockConfig ]
//        )


//    //---


//    let app dir (rotate : bool) dumpFile cacheFile access =
//        Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)

//        let phDirs = Directory.GetDirectories(dir) |> Array.head |> Array.singleton


//        let patchHierarchies =
//            [ 
//            for h in phDirs do
//                yield PatchHierarchy.load 
//                    Serialization.binarySerializer.Pickle 
//                    Serialization.binarySerializer.UnPickle 
//                    (h |> OpcPaths)
//            ]    

//        let box = 
//            patchHierarchies 
//            |> List.map(fun x -> x.tree |> QTree.getRoot) 
//            |> List.map(fun x -> x.info.GlobalBoundingBox)
//            |> List.fold (fun a b -> Box3d(a, b)) Box3d.Invalid
      
//        let opcInfos = 
//            [
//                for h in patchHierarchies do
            
//                    let rootTree = h.tree |> QTree.getRoot

//                    let horror : HashMap<Box3d, Aardvark.VRVis.Opc.KdTrees.Level0KdTree> = (KdTrees.loadKdTrees' h Trafo3d.Identity true ViewerModality.XYZ Serialization.binarySerializer)

//                    yield {
//                        patchHierarchy = h
//                        kdTree         = Aardvark.VRVis.Opc.KdTrees.expandKdTreePaths h.opcPaths.Opc_DirAbsPath horror
//                        localBB        = rootTree.info.LocalBoundingBox 
//                        globalBB       = rootTree.info.GlobalBoundingBox
//                        neighborMap    = HashMap.empty
//                    }
//            ]
//            |> List.map (fun info -> info.globalBB, info)
//            |> HashMap.ofList      
                      
//        let up = if rotate then (box.Center.Normalized) else V3d.OOI

//        let restoreCamState : CameraControllerState =
//            if File.Exists ".\camerastate" then          
//                Log.line "[App] restoring camstate"
//                let csLight : CameraStateLean = Serialization.loadAs ".\camerastate"
//                { FreeFlyController.initial with view = csLight |> fromCameraStateLean }
//            else 
//                { FreeFlyController.initial with view = CameraView.lookAt (box.Max) box.Center up; }                    

//        let camState = restoreCamState

//        let restorePlane =
//            if File.Exists ".\planestate" then
//                Log.line "[App] restoring planestate"
//                let p : PlaneCoordinates = Serialization.loadAs ".\planestate"
//                p |> fromPlaneCoords
//            else
//                IndexList.empty

//        let planeState = restorePlane

//        let setPlaneForPicking =
//            match planeState.IsEmpty with
//            | true -> None
//            | false -> Some planeState


//        let roverinitialCamera = CameraView.lookAt box.Max box.Center box.Center.Normalized


//        let ffConfig = { camState.freeFlyConfig with lookAtMouseSensitivity = 0.004; lookAtDamping = 50.0; moveSensitivity = 0.0}
//        let camState = camState |> Optic.set (CameraControllerState.freeFlyConfig_) ffConfig

//        let initialDockConfig = 
//            config {
//                content (
//                    horizontal 8.0 [
//                        vertical 2.0 [
//                            element { id "render"; title "Render View"; weight 3.0 }
//                            element { id "products"; title "Product View"; weight 1.0 }
//                        ]
//                        element { id "controls"; title "Controls"; weight 1.0 }                         
//                    ]
//                )
//                appName "2D3D Linking"
//                useCachedConfig true
//            }

//        let initFrustum = Frustum.perspective 60.0 0.01 1000.0 1.0

//        let loadMinerva dumpFile cacheFile view frustum (m: MinervaModel) =
            
//            let m' = 
//                MinervaApp.update
//                    view
//                    frustum
//                    m
//                    MinervaAction.Load
                    
//            let data = MinervaModel.loadDumpCSV dumpFile cacheFile 

//            let whiteListFile = Path.ChangeExtension(dumpFile, "white")
//            let whiteListIds =
//                if whiteListFile |> File.Exists then
//                    File.readAllLines whiteListFile |> HashSet.ofArray
//                else 
//                    data.features |> IndexList.map(fun x -> x.id) |> IndexList.toList |> HashSet.ofList
                
//            let validFeatures = data.features |> IndexList.filter (fun x -> whiteListIds |> HashSet.contains x.id)
//            let data = { data with features = validFeatures }

//            let minerva = 
//                { m' with data = data }
//                    |> MinervaApp.updateProducts data
//                    |> MinervaApp.loadTifs1087 // load tifs for linking TODO hardcoded sol value (subset)

//            //refactor ... make chain
//            let filtered = 
//                QueryApp.applyFilterQueries 
//                    minerva.data.features 
//                    minerva.session.queryFilter

//            { minerva with 
//                session = { 
//                    minerva.session with
//                        filteredFeatures = filtered 
//                } 
//            }  
//            |> MinervaApp.updateFeaturesForRendering    
//          //  |> MinervaApp.loadTifs
        
//        let minervaModel       = MinervaModel.initial |> loadMinerva dumpFile cacheFile camState.view initFrustum
//        let linkingModel       = LinkingModel.initial |> LinkingApp.initFeatures minervaModel.data.features
      
//        let initialModel : Model = 
//            { 
//                cameraState        = camState
//                mainFrustum        = initFrustum
//                overlayFrustum     = None
//                fillMode           = FillMode.Fill                    
//                patchHierarchies   = patchHierarchies          
            
//                threads            = FreeFlyController.threads camState |> ThreadPool.map Camera
//                boxes              = List.empty 
            
//                pickingActive      = false
//                opcInfos           = opcInfos
//                pickingModel       = { PickingModel.initial with pickingInfos = opcInfos }
//                pickedPoint        = None
//                planePoints        = setPlaneForPicking
//                dockConfig         = initialDockConfig   
//                linkingModel       = linkingModel
//                minervaModel       = minervaModel
//            }

//        {
//            initial = initialModel             
//            update = update
//            view   = view          
//            threads = fun m -> m.threads
//            unpersist = Unpersist.instance<Model, AdaptiveModel>
//        }


