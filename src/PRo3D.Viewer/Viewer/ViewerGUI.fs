namespace PRo3D.Viewer


open System
open System.IO
open System.Runtime.InteropServices


open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Operators
open Aardvark.UI.Primitives
open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation

open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Bookmarkings

open PRo3D.SimulatedViews

open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Core.Gis

module Gui =            
    
    let pitchAndBearing (r:AdaptiveReferenceSystem) (view:aval<CameraView>) =
        adaptive {
          let! up    = r.up.value
          let! north = r.northO//r.north.value   
          let! v     = view
        
          return (Calculations.pitch up v.Forward, Calculations.bearing up north v.Forward)
        }
    
    let dnsColorLegend (m : AdaptiveModel) =

        let falseColorSvg = FalseColorLegendApp.Draw.createFalseColorLegendBasics "DnsLegend" m.drawing.dnsColorLegend
                
        let attributes =
            [        
                "display"               => "block"; 
                "width"                 => "55px"; 
                "height"                => "75%"; 
                "preserveAspectRatio"   => "xMidYMid meet"; 
                "viewBox"               => "0 0 5% 100%" 
                "style"                 => "position:absolute; left: 0%; top: 25%"
                "pointer-events"        => "None"
            ] |> AttributeMap.ofList
        
        Incremental.Svg.svg attributes falseColorSvg
                            
    let scalarsColorLegend (m : AdaptiveModel) =
          
        let attributes =
            [            
                "display"               => "block"; 
                "width"                 => "55px"; 
                "height"                => "75%"; 
                "preserveAspectRatio"   => "xMidYMid meet"; 
                "viewBox"               => "0 0 5% 100%" 
                "style"                 => "position:absolute; right: 0px; top: 25%"
                "pointer-events"        => "None"
            ] |> AttributeMap.ofList
    
        Incremental.Svg.svg attributes (SurfaceApp.showColorLegend m.scene.surfacesModel)

    let depthColorLegend (m : AdaptiveModel) =

        let falseColorSvg = FalseColorLegendApp.Draw.createFalseColorLegendBasics "DepthLegend" m.footPrint.depthColorLegend
                
        let attributes =
            [        
                "display"               => "block"; 
                "width"                 => "55px"; 
                "height"                => "75%"; 
                "preserveAspectRatio"   => "xMidYMid meet"; 
                "viewBox"               => "0 0 5% 100%" 
                "style"                 => "position:absolute; left: 0%; top: 25%"
                "pointer-events"        => "None"
            ] |> AttributeMap.ofList
        
        Incremental.Svg.svg attributes falseColorSvg
    
    let selectionRectangle (m : AdaptiveModel) =
        
        let box = 
            m.multiSelectBox 
            |> AVal.map(fun x -> 
                x 
                |> Option.map(fun x -> x.renderBox) 
                |> Option.defaultValue Box2i.Invalid
            )

        let attr = 
            amap{
                yield style "fill:white;stroke:green;stroke-width:2;fill-opacity:0.1;stroke-opacity:0.9"
                let! b = box
                yield attribute "x" (sprintf "%ipx" b.Min.X)
                yield attribute "y" (sprintf "%ipx" b.Min.Y)
                yield attribute "width" (sprintf "%ipx" b.SizeX)
                yield attribute "height" (sprintf "%ipx" b.SizeY)
            } |> AttributeMap.ofAMap

        let selectionRectangle = Incremental.Svg.rect attr //Incremental.Svg.rect attr AList.empty

        let canvasAttributes = 
            [
                "style" => "position:absolute; left: 0; top: 0"
                "width" => "100%"
                "height" => "100%"
                attribute "pointer-events" "None"   
            ]

        Svg.svg canvasAttributes [ selectionRectangle ]

    let textOverlays (m : AdaptiveReferenceSystem) (cv : aval<CameraView>) = 
        div [js "oncontextmenu" "event.preventDefault();"] [ 
            let planet = 
                m.planet 
                |> AVal.map(fun x -> 
                    match x with
                    | Planet.Mars  -> "Mars (IAU ellipsoid)"
                    | Planet.Earth -> "Earth (ellipsoid)"
                    | Planet.JPL   -> "JPL Rover Frame"
                    | Planet.None  -> "None xyz"          
                    | Planet.ENU   -> "ENU"
                    | _ -> "[TextOverlays] missing enum"
                )  
            
            let pnb = pitchAndBearing m cv
            
            let pitch    = pnb |> AVal.map(fun (p,_) -> sprintf "%s deg" (p.ToString("0.00")))
            let bearing  = pnb |> AVal.map(fun (_,b) -> sprintf "%s deg" (b.ToString("0.00")))
            
            let position = cv |> AVal.map(fun x -> x.Location.ToString("0.00"))
            
            let spericalc = 
                AVal.map2 (fun (a : CameraView) b -> 
                    CooTransformation.getLatLonAlt b a.Location
                ) cv m.planet
            
            let altitude = 
                AVal.map2 (fun (a : CameraView) b -> 
                    CooTransformation.getAltitude a.Location a.Up b ) cv m.planet
            
            let lon = 
                spericalc 
                |> AVal.map(fun x -> 
                    if x.longitude.IsNaN() then
                        sprintf "not available"
                    else
                        sprintf "%s deg" ((360.0 - x.longitude).ToString())
                )
            let lat = 
                spericalc 
                |> AVal.map(fun x -> 
                    if x.latitude.IsNaN() then
                        sprintf "not available"
                    else
                        sprintf "%s deg" ((x.latitude).ToString())
                ) 
                
            let alt2 = altitude |> AVal.map(fun x -> sprintf "%s m" ((x).ToString("0.00")))            
                                                   
            let style' = "color: white; font-family:Consolas;"
            
            yield div [
                clazz "ui"; 
                style "position: absolute; top: 15px; left: 15px; float:left; pointer-events:None" 
                ] [                
                yield table [] [
                    tr [] [
                        td [style style'] [Incremental.text planet]
                    ]
                    tr [] [
                        td [style style'] [text "Bearing: "]
                        td [style style'] [Incremental.text bearing]
                    ]
                    tr [] [
                        td [style style'] [text "Pitch: "]
                        td [style style'] [Incremental.text pitch]
                    ]
                    tr [] [
                        td [style style'] [text "Position: "]
                        td [style style'] [Incremental.text position]
                    ]
                    tr [] [
                        td [style style'] [text "Latitude: "]
                        td [style style'] [Incremental.text lat]
                    ]
                    tr [] [
                        td [style style'] [text "Longitude: "]
                        td [style style'] [Incremental.text lon]
                    ]
                    //tr[][
                    //    td[style style'][text "Altitude: "]
                    //    td[style style'][Incremental.text alt]
                    //]
                    tr [] [
                        td [style style'] [text "Altitude: "]
                        td [style style'] [Incremental.text alt2]
                    ]                    
                ]
            ]                     
        ]
    
    let textOverlaysInstrumentView (m : AdaptiveViewPlanModel)  = 
        let instrument =
            adaptive {
                let! id = m.selectedViewPlan
                match id with
                | Some v -> 
                    let! vp = m.viewPlans |> AMap.tryFind v
                    match vp with
                    | Some selVp -> 
                        return! (AVal.bindAdaptiveOption selVp.selectedInstrument "No instrument selected" (fun a -> a.id)) 
                    | None -> return ""
                | None -> return "" 
            } 
        div [js "oncontextmenu" "event.preventDefault();"] [                         
            yield div [clazz "ui"; style "position: absolute; top: 15px; left: 15px; float:left" ] [
                //arrowOverlay
                yield table [] [
                    tr [] [
                        td [style "color: white; font-family:Consolas"] [Incremental.text instrument]
                    ]
                ]
            ]                              
        ]
    
    let textOverlaysUserFeedback (m : AdaptiveScene)  = 
        div [js "oncontextmenu" "event.preventDefault();"] [ 
            let style' = "color: white; font-family:Consolas; font-size:16;"
            
            yield div [clazz "ui"; style "text-align: right; width: 250px; position: absolute; top: 15px; right: 15px; float:right" ] [ //float:left
                //arrowOverlay
                yield table [] [
                    tr [] [
                        td [style style'] [Incremental.text m.userFeedback]
                    ]
                ]
            ]                              
        ]

    module CustomGui =
        let dropDown<'a, 'msg when 'a : enum<int> and 'a : equality> (exclude : HashSet<'a>) (selected : aval<'a>) (change : 'a -> 'msg) =
            let names  = Enum.GetNames(typeof<'a>)
            let values = Enum.GetValues(typeof<'a>) |> unbox<'a[]> 
            let nv     = Array.zip names values

            let attributes (name : string) (value : 'a) =
                AttributeMap.ofListCond [
                    always (attribute "value" name)
                    onlyWhen (AVal.map ((=) value) selected) (attribute "selected" "selected")
                ]
       
            select [onChange (fun str -> Enum.Parse(typeof<'a>, str) |> unbox<'a> |> change); style "color:black"] [
                for (name, value) in nv do
                    if exclude |> HashSet.contains value |> not then
                        let att = attributes name value
                        yield Incremental.option att (AList.ofList [text name])
            ]

        let dropDownWithTooltip<'a, 'msg when 'a : enum<int> and 'a : equality> (exclude : HashSet<'a>) (selected : aval<'a>) (change : 'a -> 'msg) (getTooltip : 'a -> string) =
            let names  = Enum.GetNames(typeof<'a>)
            let values = Enum.GetValues(typeof<'a>) |> unbox<'a[]> 
            let nv     = Array.zip names values

            let attributes (name : string) (value : 'a) =
                AttributeMap.ofListCond [
                    always (attribute "value" name)
                    onlyWhen (AVal.map ((=) value) selected) (attribute "selected" "selected")
                ]
       
            select [onChange (fun str -> Enum.Parse(typeof<'a>, str) |> unbox<'a> |> change); style "color:black"] [
                for (name, value) in nv do
                    if exclude |> HashSet.contains value |> not then
                        let att = attributes name value
                        let tooltip = getTooltip value
                        yield Incremental.option att (AList.ofList [text name]) |> UI.wrapToolTip DataPosition.Bottom tooltip
            ]

    module TopMenu =                       

        let jsImportOPCDialog =
            "top.aardvark.dialog.showOpenDialog({tile: 'Select directory to discover OPCs and import', filters: [{ name: 'OPC (directories)'}], properties: ['openDirectory', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
           
        let jsImportOBJDialog =
            "top.aardvark.dialog.showOpenDialog({tile: 'Select *.obj files to import', filters: [{ name: 'OBJ (*.obj)', extensions: ['obj']}], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
        
        let jsImportglTfDialog =
            "top.aardvark.dialog.showOpenDialog({tile: 'Select *.gltf files to import', filters: [{ name: 'glTF (*.gltf)', extensions: ['gltf']}], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
        
        let jsImportSceneObjectDialog =
            "top.aardvark.dialog.showOpenDialog({tile: 'Select *.obj or *.dae files to import', filters: [{ name: 'OBJ (*.obj)', extensions: ['obj']}, { name: 'DAE (*.dae)', extensions: ['dae']}], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

        let jsImportPLYDialog =
            "top.aardvark.dialog.showOpenDialog({tile: 'Select *.ply files to import', filters: [{ name: 'PLY (*.ply)', extensions: ['ply']}], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"                    

        let private importSurface =
            [
                text "Surfaces"
                i [clazz "dropdown icon"] [] 
                div [ clazz "menu"] [
                    div [ clazz "ui inverted item";
                        Dialogs.onChooseFiles ImportDiscoveredSurfacesThreads;
                        clientEvent "onclick" (jsImportOPCDialog)
                    ] [
                        text "Import OPCs"
                    ]
                    //div [ clazz "ui inverted item"; 
                    //    Dialogs.onChooseFiles (curry ViewerAction.ImportObject MeshLoaderType.Assimp);
                    //    clientEvent "onclick" (jsImportOBJDialog)
                    //] [
                    //    text "Import (*.obj) using assimp"
                    //]
                    div [ clazz "ui inverted item"; 
                        Dialogs.onChooseFiles (curry ViewerAction.ImportObject MeshLoaderType.Wavefront);
                        clientEvent "onclick" (jsImportOBJDialog)
                    ] [
                        text "Import (*.obj)"
                    ]
                    //div [ clazz "ui inverted item"; 
                    //    Dialogs.onChooseFiles (curry ViewerAction.ImportObject MeshLoaderType.GlTf);
                    //    clientEvent "onclick" (jsImportOBJDialog)
                    //] [
                    //    text "Import (*.gltf) "
                    //]
                    div [ clazz "ui inverted item"; 
                        Dialogs.onChooseFiles (curry ViewerAction.ImportObject MeshLoaderType.Ply);
                        clientEvent "onclick" (jsImportPLYDialog)
                    ] [
                        text "Import (*.ply)"
                    ]                    
                ]
            ]

        let private importSCeneObject =
            [
                text "Scene Objects"
                i [clazz "dropdown icon"] [] 
                div [ clazz "menu"] [
                    div [ clazz "ui inverted item"; 
                        Dialogs.onChooseFiles ImportSceneObject;
                        clientEvent "onclick" (jsImportSceneObjectDialog)
                    ] [
                        text "Import (*.obj or *.dae)"
                    ]
                ]
            ]
        

        let private scene (m:AdaptiveModel) =
            let jsSaveSceneDialog = 
                "top.aardvark.dialog.showSaveDialog({ title:'Save Scene as', filters:  [{ name: 'Scene (*.pro3d)', extensions: ['pro3d'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"

            let saveSceneDialog (m:AdaptiveModel) = 
                adaptive {
                    let! path = m.scene.scenePath
                    return 
                        match path with
                        | Some p ->
                            div [ clazz "ui inverted item"; onMouseClick (fun _ -> SaveScene p)] [text "Save"]
                        | None ->
                            div [
                                clazz "ui inverted item"
                                Dialogs.onSaveFile SaveScene
                                clientEvent "onclick" jsSaveSceneDialog
                            ] [ text "Save" ]
                }

            let jsOpenSceneDialog = "top.aardvark.dialog.showOpenDialog({ title:'Open scene', filters: [{ name: 'Scene (*.pro3d, *.scn)', extensions: ['pro3d','scn']},], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

            [
                text "Scene" 
                i [clazz "dropdown icon"] []
                div [ clazz "menu"] [
                    //save scene
                    Incremental.div AttributeMap.empty (AList.ofAValSingle (saveSceneDialog m))

                    //save scene as
                    div [ 
                        clazz "ui inverted item"; Dialogs.onSaveFile SaveAs;
                        clientEvent "onclick" jsSaveSceneDialog
                    ] [
                        text "Save as"
                    ]

                    //load scene
                    div [ 
                        clazz "ui inverted item"
                        Dialogs.onChooseFiles(fun x -> 
                            match (x |> List.tryHead) with 
                            | Some y -> LoadScene y 
                            | None -> NoAction "no scene selected"
                        )

                        clientEvent "onclick" jsOpenSceneDialog
                    ] [      
                        text "Open"
                    ]

                    //new scene
                    div [ clazz "ui inverted item"; onMouseClick (fun _ -> NewScene)] [
                        text "New"
                    ]

                    //recent scenes
                    div [ clazz "ui inverted item" ] [
                        onBoot """$('#__ID__').popup({inline:true,hoverable:true, position   : 'right center'});""" (
                            text "Recent"
                        )
                
                        div [clazz "ui flowing popup bottom left transition hidden"] [
                            Incremental.div (AttributeMap.ofList [clazz "ui link list"]) (
                                alist {
                                    let! recentScenes = m.recent.recentScenes                                    
                                    let last10Scenes =
                                        if recentScenes.Length > 10 then
                                            recentScenes |> List.take 10
                                        else
                                            recentScenes
        
                                    for s in last10Scenes do
                                        yield a [clazz "item inverted"; onClick (fun _ -> LoadScene s.path)] [
                                             span [style "color:black"] [Incremental.text (AVal.constant s.name)]
                                        ]                                    
                                } 
                            )
                        ] 
                    ] 
                ]
            ]        
        
        let fixAllBrokenPaths =
            let jsLocateSurfacesDialog = 
                "top.aardvark.dialog.showOpenDialog({title:'Select directory to locate OPCs', filters: [{ name: 'OPC (directories)'}], properties: ['openDirectory']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

            let ui = 
                alist {
                    yield
                        div [ 
                            clazz "ui item";
                            Dialogs.onChooseFiles  SurfaceAppAction.ChangeImportDirectories;
                            clientEvent "onclick" jsLocateSurfacesDialog 
                        ] [
                            text "Locate Surfaces"
                        ]
                }
        
            Incremental.div(AttributeMap.Empty) ui |> UI.map SurfaceActions   
            
        let fixAllBrokenOBJPaths =
            let jsLocateOBJDialog = 
                "top.aardvark.dialog.showOpenDialog({title:'Select directory to locate OBJs', filters: [{ name: 'OBJs (*.obj)', extensions: ['obj']}], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

            let ui = 
                alist {
                    yield
                        div [ 
                            clazz "ui item";
                            Dialogs.onChooseFiles  SurfaceAppAction.ChangeOBJImportDirectories;
                            clientEvent "onclick" jsLocateOBJDialog 
                        ] [
                            text "Locate OBJ Surfaces"
                        ]
                }
        
            Incremental.div(AttributeMap.Empty) ui |> UI.map SurfaceActions      

        let fixAllBrokenSOPaths =
            let jsLocateSODialog = 
                "top.aardvark.dialog.showOpenDialog({title:'Select directory to locate Scene Objects', filters: [{ name: 'OBJ (*.obj)', extensions: ['obj']}, { name: 'DAE (*.dae)', extensions: ['dae']}], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

            let ui = 
                alist {
                    yield
                        div [ 
                            clazz "ui item";
                            Dialogs.onChooseFiles  SceneObjectAction.ChangeSOImportDirectories;
                            clientEvent "onclick" jsLocateSODialog 
                        ] [
                            text "Locate Scene Objects"
                        ]
                }
        
            Incremental.div(AttributeMap.Empty) ui |> UI.map SceneObjectsMessage      
            
        let jsOpenAnnotationFileDialog = 
            "top.aardvark.dialog.showOpenDialog({ title: 'Import Annotations', filters: [{ name: 'Annotations (*.ann)', extensions: ['ann']},], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

        let jsExportAnnotationsFileDialog = 
            "top.aardvark.dialog.showSaveDialog({ title: 'Save Annotations as', filters:  [{ name: 'Annotations (*.pro3d.ann)', extensions: ['pro3d.ann'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"

        let jsExportAnnotationsAsCSVDialog =
            "top.aardvark.dialog.showSaveDialog({ title: 'Export Annotations (*.csv)', filters:  [{ name: 'Annotations (*.csv)', extensions: ['csv'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"

        let jsExportProfileAsCSVDialog =
            "top.aardvark.dialog.showSaveDialog({ title: 'Export Profile (*.csv)', filters:  [{ name: 'Annotations (*.csv)', extensions: ['csv'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"

        let jsExportAnnotationsAsGeoJSONDialog =
            "top.aardvark.dialog.showSaveDialog({ title: 'Export Annotations (*.json)', filters:  [{ name: 'Annotations (*.json)', extensions: ['json'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"              

        let annotationMenu = //todo move to viewer io gui
            div [ clazz "ui dropdown item"] [
                text "Annotations"
                i [clazz "dropdown icon"] [] 
                div [ clazz "menu"] [                    
                    div [
                        clazz "ui inverted item"
                        Dialogs.onChooseFiles AddAnnotations
                        clientEvent "onclick" jsOpenAnnotationFileDialog
                    ] [
                        text "Import"
                    ]
                    div [
                        clazz "ui inverted item"; onMouseClick (fun _ -> Clear)
                    ] [
                        text "Clear"
                    ]      
                    div [ clazz "ui dropdown item"] [
                        text "Export"
                        i [clazz "dropdown icon"] [] 
                        div [ clazz "menu"] [
                    
                            div [ 
                                clazz "ui inverted item"
                                Dialogs.onSaveFile ExportAsAnnotations
                                clientEvent "onclick" jsExportAnnotationsFileDialog
                            ] [
                                text "all as 'PRo3D' annotations (*.pro3d.ann)"
                            ]
                            div [ 
                                clazz "ui inverted item"
                                Dialogs.onSaveFile ExportAsCsv
                                clientEvent "onclick" jsExportAnnotationsAsCSVDialog
                            ] [
                                text "visible as table (*.csv)"
                            ]     
                            div [ 
                                clazz "ui inverted item"
                                Dialogs.onSaveFile ExportAsProfileCsv
                                clientEvent "onclick" jsExportProfileAsCSVDialog
                            ]  [
                                text "selected as profile (*.csv)"
                            ]     
                            div [ 
                                clazz "ui inverted item"
                                Dialogs.onSaveFile ExportAsGeoJSON
                                clientEvent "onclick" jsExportAnnotationsAsGeoJSONDialog
                            ]  [
                                text "visible as GeoJSON (*.json)"
                            ]     
                            div [ 
                                clazz "ui inverted item"
                                Dialogs.onSaveFile ExportAsGeoJSON_xyz
                                clientEvent "onclick" jsExportAnnotationsAsGeoJSONDialog
                            ] [
                                text "visible as GeoJSON xyz (*.json)"
                            ]
                            div [ 
                                clazz "ui inverted item"
                                Dialogs.onSaveFile ContinuouslyGeoJson
                                clientEvent "onclick" jsExportAnnotationsAsGeoJSONDialog
                            ] [
                                text "continuously export as GeoJSON xyz (*.json)"
                            ]
                            div [ 
                                clazz "ui inverted item"
                                Dialogs.onSaveFile ExportAsAttitude
                                clientEvent "onclick" jsExportAnnotationsAsGeoJSONDialog
                            ] [
                                text "dns as 'Attitude' planes (*.json)"
                            ]
                        ]
                    ]
                ]
            ]       
        
        let menu (m : AdaptiveModel) =          
            let subMenu name menuItems = 
                div [ clazz "ui dropdown item"] [
                  text name
                  i [clazz "dropdown icon"] [] 
                  div [ clazz "menu"] menuItems
                ]           
            let menuItem name action =
                div [ 
                    clazz "ui inverted item"
                    onClick (fun _ -> action)
                ] [
                    text name
                ]
                    

            div [clazz "menu-bar"] [
                // menu
                div [ clazz "ui top menu"; style "z-index: 10000; padding:0px; margin:0px"] [
                    onBoot "$('#__ID__').dropdown('on', 'hover');" (
                        div [ clazz "ui dropdown item"; style "padding:0px 5px"] [
                            i [clazz "large sidebar icon"; style "margin:0px 2px"] []
                            
                            div [ clazz "ui menu"] [
            
                                //import surfaces
                                div [ clazz "ui dropdown item"; style "width: 150px"] importSurface
                            
                                //scene menu
                                div [ clazz "ui dropdown item"] (scene m)
                            
                                //annotations menu
                                annotationMenu |> UI.map DrawingMessage;   
                                subMenu "Change Mode"
                                        [
                                          menuItem "PRo3D Core" (ChangeDashboardMode DashboardModes.core)
                                          menuItem "Surface Comparison" (ChangeDashboardMode DashboardModes.comparison)
                                          menuItem "Render Only" (ChangeDashboardMode DashboardModes.renderOnly)
                                          menuItem "Provenance" (ChangeDashboardMode DashboardModes.provenance)
                                          menuItem "GIS" (ChangeDashboardMode DashboardModes.gis)
                                        ]   
                                
                                //scene objects
                                div [ clazz "ui dropdown item"; style "width: 150px"] importSCeneObject
                                                            
                                //Extras Menu
                                div [ clazz "ui dropdown item"] [
                                    text "Extras"
                                    i [clazz "dropdown icon"] [] 
                                    div [ clazz "menu"] [
                                        //fixes all broken surface import paths
                                        fixAllBrokenPaths
                                        //fixes all broken obj import paths
                                        fixAllBrokenOBJPaths
                                        //fixes all broken scene obj paths
                                        fixAllBrokenSOPaths

                                        let jsOpenOldAnnotationsFileDialogue = "top.aardvark.dialog.showOpenDialog({title:'Import legacy annotations from PRo3D 1.0' , filters: [{ name: 'Annotations (*.xml)', extensions: ['xml']},], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

                                        div [ clazz "ui item";
                                            Dialogs.onChooseFiles ImportPRo3Dv1Annotations;
                                            clientEvent "onclick" jsOpenOldAnnotationsFileDialogue ] [
                                            text "Import v1 Annotations (*.xml)"
                                        ]
                                        
                                        let jsImportTraverseDialog = "top.aardvark.dialog.showOpenDialog({title:'Import Traverse files' , filters: [{ name: 'Traverses (*.json)', extensions: ['json']},], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

                                        div [ clazz "ui item"; Dialogs.onChooseFiles ImportTraverse; clientEvent "onclick" jsImportTraverseDialog ] [
                                            text "Import Traverses (*.json)"
                                        ]
                                        //div [ clazz "ui item";
                                        //    Dialogs.onChooseFiles ImportSurfaceTrafo;
                                        //    clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({filters: [{ name: 'xml', extensions: ['xml']},],properties: ['openFile']}));") ] [
                                        //    text "Import Surface Trafos"
                                        //]
                                        //div [ clazz "ui item";
                                        //    Dialogs.onChooseFiles ImportRoverPlacement;
                                        //    clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({properties: ['openFile']}));") ] [
                                        //    text "Rover Placement"
                                        //]
                                        
                                        div [ clazz "ui item"; 
                                            clientEvent "onclick" (sprintf "aardvark.electron.shell.openPath('%s')" (Config.configPath.Replace("\\","\\\\")))] [
                                            text "Open Configuration Folder"
                                        ]

                                        div [ clazz "ui item"; 
                                            clientEvent "onclick" "aardvark.electron.shell.openExternal('https://github.com/pro3d-space/PRo3D/blob/develop/CREDITS.MD')"] [
                                            text "3rd Party Licences"
                                        ]


                                        let jsOpenPose = "top.aardvark.dialog.showOpenDialog({title:'Import Pose File' , filters: [{ name: 'Pose Data (*.json)', extensions: ['json']},], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
                                        div [ clazz "ui item"; Dialogs.onChooseFiles ViewerAction.LoadPoseDefinitionFile; clientEvent "onclick" jsOpenPose ] [
                                            text "Load Pose Definition File"
                                        ]


                                        let jsLoadSpice = "top.aardvark.dialog.showOpenDialog({title:'Load SPICE kernel' , filters: [{ name: 'SPICE Kernel (*.spk, *.pck, *.ik, *.ck, *.tm)', extensions: ['spk','pck','ik','ck','tm']},], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
                                        div [ clazz "ui item"; Dialogs.onChooseFiles (function [p] -> ViewerAction.GisAppMessage (GisAppAction.SetSpiceKernel p) | _ -> ViewerAction.Nop); clientEvent "onclick" jsLoadSpice ] [
                                            text "Load SPICE kernel"
                                        ]

                                        

                                        //menuItem "Create Pose File from SBookmarks" SBookmarksToPoseDefinition // for debugging
                                        div [clazz "ui item"; clientEvent "onclick" "sendCrashDump()"] [
                                            text "Send log to maintainers"
                                        ]
                                        a [style "visibility:hidden"; clazz "invisibleCrashButton"] []

                                        //div [clazz "ui item"; onClick (fun _ ->  ViewerAction.Nop)] [
                                        //    text "Send Crash Report"
                                        //    a [attribute "href" "mailto:hs@pro3d.com?attach=C:\\Program Files (x86)\\ProcessExplorer\\procexp64.exe"] [text "go"]
                                        //]
                                    ]
                                ]
                            ] 
                        ]
                    )
                ]
            ]
        
        let dynamicTopMenu (m:AdaptiveModel) =
            adaptive {
                let! interaction = m.interaction
                match interaction with
                | Interactions.DrawAnnotation -> 
                    return Drawing.UI.viewAnnotationToolsHorizontal Config.colorPaletteStore m.drawing |> UI.map DrawingMessage
                | Interactions.PlaceRover ->
                    return ViewPlanApp.UI.viewSelectRover m.scene.viewPlans.roverModel |> UI.map RoverMessage
                | Interactions.PlaceCoordinateSystem -> 
                    return Html.Layout.horizontal [
                        Html.Layout.boxH [ Html.SemUi.dropDown' m.scene.referenceSystem.scaleChart m.scene.referenceSystem.selectedScale ReferenceSystemAction.SetScale id ]
                        //Html.Layout.boxH [ i [clazz "unhide icon"][] ]
                        Html.Layout.boxH [ GuiEx.iconToggle m.scene.referenceSystem.isVisible "unhide icon" "hide icon" ReferenceSystemAction.ToggleVisible  ]                        
                        ] |> UI.map ReferenceSystemMessage
                | Interactions.PickAnnotation ->
                     return Html.Layout.horizontal [
                        Html.Layout.boxH [text "eps.:"]
                        Html.Layout.boxH [
                            Numeric.view' [InputBox] m.scene.config.pickingTolerance |> UI.map (fun x -> (ConfigProperties.Action.SetPickingTolerance x) |> ConfigPropertiesMessage)] 
                     ]
                | Interactions.PlaceScaleBar ->
                    return ScaleBarsDrawing.UI.viewScaleBarToolsHorizontal m.scaleBarsDrawing |> UI.map ScaleBarsDrawingMessage
                | Interactions.PickPivotPoint ->
                    return Html.Layout.horizontal [
                        Html.Layout.boxH [text "for:"]
                        Html.Layout.boxH [ Html.Layout.boxH [ Html.SemUi.dropDown m.pivotType SetPivotType ] ]
                     ]
                | _ -> 
                  return div [] []
            }
            
        let style' = "color: white; font-family:Consolas;"

        let scenepath (m:AdaptiveModel) = 
            Incremental.div (AttributeMap.Empty) (
                alist {
                    let! scenePath = m.scene.scenePath
                    let icon = 
                        match scenePath with
                        | Some p -> 
                            i [clazz "large folder icon" ; clientEvent "onclick" (Electron.showItemInFolder p)] [] 
                            |> UI.wrapToolTip DataPosition.Bottom "open folder"
                        | None -> div [] []  
                          
                    let scenePath = AVal.bindOption m.scene.scenePath "" (fun sp -> AVal.constant sp)
                    yield  div [] [                     
                        Html.Layout.boxH [ icon ]
                        Html.Layout.boxH [ 
                            Incremental.text (
                                scenePath 
                                |> AVal.map(fun x -> 
                                    if x.IsEmpty() then "*new scene" else Path.GetFileName x)
                            )                                     
                        ]
                    ]
                }        
        )
            
        let interactionText (i : Interactions) =
            let ctrl = if RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) then "CMD" else "CTRL"
            match i with 
            | Interactions.PickExploreCenter     -> sprintf "%s+click to place arcball center" ctrl
            | Interactions.PlaceCoordinateSystem -> sprintf "%s+click to place coordinate cross" ctrl
            | Interactions.DrawAnnotation        -> sprintf "%s+click to pick point on surface" ctrl
            | Interactions.PickAnnotation        -> sprintf "%s+click on annotation to select" ctrl
            | Interactions.PickSurface           -> sprintf "%s+click on surface to select" ctrl
            | Interactions.PlaceRover            -> sprintf "%s+click to (1) place rover and (2) pick lookat" ctrl
            | Interactions.TrafoControls         -> "not implemented"
            | Interactions.PlaceSurface          -> "not implemented"
            | Interactions.PlaceScaleBar         -> sprintf "%s+click to place scale bar" ctrl
            | Interactions.PlaceSceneObject      -> sprintf "%s+click to place scene object" ctrl
            | Interactions.PickPivotPoint        -> sprintf "%s+click to place pivot point" ctrl
            //| Interactions.PickLinking           -> "CTRL+click to place point on surface"
            | _ -> ""

        let interactionTooltip (i : Interactions) : string =
            match i with 
            | Interactions.PickExploreCenter     -> "Pick the camera pivot point if ArcBall navigation is activated. Press CTRL + LMB to pick the pivot point on a surface."
            | Interactions.PlaceCoordinateSystem -> "Press CTRL+LMB to pick a point on the surface and choose a unit of measurement to adapt the size of the axis gizmo."
            | Interactions.DrawAnnotation        -> "Choose an annotation mode to draw an annotation on a surface. Press CTRL+LMB to pick a point on a surface."
            | Interactions.PlaceRover            -> "Select a rover model in the rover menu."
            | Interactions.PickAnnotation        -> "Press CTRL+LMB and pick an annotation in the main view. The selected annotation will be highlighted green."
            | Interactions.PickSurface           -> "Press CTRL+LMB and pick the surface in the main view. The surface’s name turns its color to green in the listing."
            | Interactions.SelectArea            -> ""
            | Interactions.PlaceScaleBar         -> ""
            | Interactions.PlaceSceneObject      -> ""
            | Interactions.PickPivotPoint        -> ""
            | _ -> ""
        
        let topMenuItems (model : AdaptiveModel) = [ 


            div [style "font-weight: bold;margin-left: 1px; margin-right:1px"] 
                [Incremental.text (model.dashboardMode |> AVal.map (fun x -> sprintf "Mode: %s" x))]
            Navigation.UI.viewNavigationModes model.navigation  |> UI.map NavigationMessage 
              
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large wizard icon"] [] ]
                Html.Layout.boxH [ CustomGui.dropDownWithTooltip Interactions.hideSet model.interaction SetInteraction interactionTooltip ]
                Incremental.div  AttributeMap.empty (AList.ofAValSingle (dynamicTopMenu model))
                Html.Layout.boxH [ 
                    div [style "font-style:italic; width:100%; text-align:right"] [
                        Incremental.text (model.interaction |> AVal.map interactionText)
                    ]]
            ]
              
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large Globe icon"] [] ]
                Html.Layout.boxH [ Html.SemUi.dropDown model.scene.referenceSystem.planet ReferenceSystemAction.SetPlanet ] |> UI.map ReferenceSystemMessage
            ] 
            Html.Layout.horizontal [
                scenepath model
            ]        
        ]        
        
        let getTopMenu (m:AdaptiveModel) =
            div [clazz "ui menu"; style "padding:0; margin:0; border:0"] [
                yield (menu m)
                for t in (topMenuItems m) do
                    yield div [clazz "item topmenu"] [t]
            ]
        
    module Annotations =
      
        let viewAnnotationProperties (model : AdaptiveModel) =
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> AnnotationProperties.view Config.colorPaletteStore ann
                  | _ -> div [style "font-style:italic"] [ text "no annotation selected" ])
            
            model.drawing.annotations |> GroupsApp.viewSelected view AnnotationMessage
                
        let viewAnnotationResults (model : AdaptiveModel) =
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> AnnotationProperties.viewResults ann model.scene.referenceSystem.up.value
                  | _ -> div [style "font-style:italic"] [ text "no annotation selected" ])
            
            model.drawing.annotations |> GroupsApp.viewSelected view AnnotationMessage
                       
        let viewDipAndStrike (model : AdaptiveModel) = 
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> DipAndStrike.viewUI ann
                  | _ -> div [style "font-style:italic"] [ text "no annotation selected" ])
        
            model.drawing.annotations |> GroupsApp.viewSelected view DnSProperties    
            
        let viewDnSColorLegendUI (model : AdaptiveModel) = 
            model.drawing.dnsColorLegend 
            |> FalseColorLegendApp.viewDnSLegendProperties Config.colorPaletteStore DnSColorLegendMessage 
            |> AVal.constant
          
        let annotationLeafButtonns' (model : AdaptiveModel) = 
            let ts = model.drawing.annotations.activeChild
            let sel = model.drawing.annotations.singleSelectLeaf
            adaptive {  
                let! ts = ts
                let! sel = sel
                match sel with
                | Some _ -> return (GroupsApp.viewLeafButtons ts |> UI.map AnnotationGroupsMessageViewer)
                | None -> return div [style "font-style:italic"] [ text "no annotation group selected" ]
            }      
            
        let annotationLeafButtonns (model : AdaptiveModel) =           
            AVal.map2(fun ts sel -> 
                match sel with
                | Some _ -> (GroupsApp.viewLeafButtons ts |> UI.map AnnotationGroupsMessageViewer)
                | None -> div [style "font-style:italic"] [ text "no annotation group selected" ]
            ) model.drawing.annotations.activeChild model.drawing.annotations.singleSelectLeaf
            
        let annotationGroupProperties (model : AdaptiveModel) =                            
            GroupsApp.viewUI model.drawing.annotations 
            |> UI.map AnnotationGroupsMessageViewer 
            |> AVal.constant
        
        let annotationGroupButtons (model : AdaptiveModel) = 
            model.drawing.annotations.activeGroup 
            |> AVal.map (fun x -> GroupsApp.viewGroupButtons x |> UI.map AnnotationGroupsMessageViewer)            
            
        let annotationUI (m : AdaptiveModel) = 
            
            let buttons = 
                m.drawing.annotations.lastSelectedItem
                |> AVal.bind (fun x -> 
                    match x with 
                    | SelectedItem.Group -> annotationGroupButtons m
                    | _ -> annotationLeafButtonns m 
                )
            
            div [] [
                GuiEx.accordion "Annotations" "Write" true [
                    GroupsApp.viewSelectionButtons |> UI.map AnnotationGroupsMessageViewer
                    Drawing.UI.viewAnnotationGroups m.drawing |> UI.map ViewerAction.DrawingMessage
                   // DrawingApp.UI.viewAnnotationToolsHorizontal m.drawing |> UI.map DrawingMessage // CHECK-merge viewAnnotationGroups
                ]
                GuiEx.accordion "Dip&Strike ColorLegend" "paint brush" false [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(viewDnSColorLegendUI m))
                ] 
                GuiEx.accordion "Actions" "Asterisk" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle (buttons))
                ]
            ]    

    module Config =
        let config (model : AdaptiveModel) = 
            ConfigProperties.view model.scene.config 
            |> UI.map ConfigPropertiesMessage
            |> AVal.constant
              
        let configUI (m : AdaptiveModel) =
            div [] [
                GuiEx.accordion "ViewerConfig" "Settings" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle (config m))
                ]
                GuiEx.accordion "Coordinate System" "Map Signs" false [
                    ReferenceSystemApp.UI.view m.scene.referenceSystem |> UI.map ReferenceSystemMessage
                ]
                GuiEx.accordion "Camera" "Camera Retro" false [
                    CameraProperties.view m.scene.referenceSystem m.navigation.camera
                ]
                GuiEx.accordion "Frustum" "Settings" false [
                    FrustumProperties.view m.scene.config.frustumModel |> UI.map FrustumMessage
                ]
                GuiEx.accordion "Screenshots" "Settings" false [
                    ScreenshotApp.view m.screenshotDirectory m.scene.screenshotModel |> UI.map ScreenshotMessage
                ]
                GuiEx.accordion "Data Management" "Settings" false [
                    Html.table [  
                        Html.row "Automatically GeoJson export: "  [
                            let attributes = 
                                amap {
                                    yield onClick (fun _ -> StopGeoJsonAutoExport) 
                                    let! enabled = m.drawing.automaticGeoJsonExport.enabled
                                    if enabled then 
                                        yield clazz "ui small inverted button"; 
                                    else 
                                        yield clazz "ui small disabled inverted button"; 
                                } |> AttributeMap.ofAMap
                            Generic.button attributes [text "Stop AutoExport"]
                        ]
                        Html.row "Automatically GeoJson export path: "  [
                            Incremental.text (m.drawing.automaticGeoJsonExport.lastGeoJsonPathXyz  |> AVal.map (function None -> "not set" | Some path -> path))
                        ]
                    ]
                ]
            ] 
          
    module ViewPlanner =
        let viewPlanProperties (model : AdaptiveModel) =
              //model.scene.viewPlans |> ViewPlan.UI.viewRoverProperties ViewPlanMessage 
              model.scene.viewPlans |> ViewPlanApp.UI.viewRoverProperties ViewPlanMessage model.footPrint.isVisible model.footPrint.isDepthVisible
        
        let viewPlannerUI (m : AdaptiveModel) =             
            div [] [
                GuiEx.accordion "ViewPlans" "Write" true [
                    ViewPlanApp.UI.viewViewPlans m.scene.viewPlans |> UI.map ViewPlanMessage
                ]
                GuiEx.accordion "Properties" "Content" true [
                    Incremental.div AttributeMap.empty (viewPlanProperties m |> AList.ofAValSingle)
                ]
            ]

    module SceneObjects =
        let sceneObjectsUI (m : AdaptiveModel) =             
            div [] [
                GuiEx.accordion "SceneObjects" "Write" true [
                    SceneObjectsApp.UI.viewSceneObjects m.scene.sceneObjectsModel 
                ]
                GuiEx.accordion "Transformation" "expand arrows alternate " false [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(SceneObjectsApp.UI.viewTranslationTools m.scene.sceneObjectsModel))
                ]
               
            ] 
            |> UI.map SceneObjectsMessage      
          
    module Traverse =
        let traverseUI (m : AdaptiveModel) =
            div [] [
                yield GuiEx.accordion "Actions" "Asterisk" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(TraverseApp.UI.viewActions m.scene.traverses))
                ]
                yield GuiEx.accordion "Properties" "Content" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(TraverseApp.UI.viewProperties m.scene.traverses))
                ]

                yield GuiEx.accordion "Traverses" "Write" true [
                    TraverseApp.UI.viewTraverses m.scene.referenceSystem m.scene.traverses
                ]
                yield GuiEx.accordion "Sols" "road" true [
                    //TraverseApp.UI.viewSols m.scene.referenceSystem m.scene.traverse
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(TraverseApp.UI.viewSols m.scene.referenceSystem m.scene.traverses))
                ]
            ] 
            |> UI.map TraverseMessage

    module ScaleBars = 
        
        let scaleBarsUI (m : AdaptiveModel) =             
            div [] [
                GuiEx.accordion "ScaleBars" "Write" true [
                    ScaleBarsApp.UI.viewScaleBars m.scene.scaleBars
                ]
                // Todo: properties
                GuiEx.accordion "Properties" "Content" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(ScaleBarsApp.UI.viewProperties m.scene.scaleBars))
                ]
                GuiEx.accordion "Transformation" "expand arrows alternate " false [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(ScaleBarsApp.UI.viewTranslationTools m.scene.scaleBars))
                ]
            ] 
            |> UI.map ScaleBarsMessage

    module GeologicSurfaces = 
        
        let geologicSurfacesUI (m : AdaptiveModel) =           
            let annos = m.drawing.annotations

            div [] [
                br []
                GeologicSurfacesApp.UI.addMesh

                GuiEx.accordion "GeologicSurfaces" "Write" true [
                    GeologicSurfacesApp.UI.viewGeologicSurfaces m.scene.geologicSurfacesModel
                ]
                GuiEx.accordion "Properties" "Content" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(GeologicSurfacesApp.UI.viewProperties m.scene.geologicSurfacesModel)) 
                ]
            ] 
            |> UI.map GeologicSurfacesMessage

    module Bookmarks =
        let bookmarkGroupProperties (model : AdaptiveModel) =                                       
            GroupsApp.viewUI model.scene.bookmarks 
            |> UI.map BookmarkUIMessage 
            |> AVal.constant
                
        let viewBookmarkProperties (model : AdaptiveModel) = 
            let view = (fun leaf ->
                match leaf with
                | AdaptiveBookmarks bm -> Bookmarks.UI.view bm
                | _ -> div [style "font-style:italic"] [ text "no bookmark selected" ]
            )
    
            model.scene.bookmarks |> GroupsApp.viewSelected view BookmarkUIMessage
        
        let bookmarksLeafButtonns (model : AdaptiveModel) = 
            let ts = model.scene.bookmarks.activeChild
            let sel = model.scene.bookmarks.singleSelectLeaf
            adaptive {  
                let! ts = ts
                let! sel = sel
                match sel with
                | Some _ -> return (GroupsApp.viewLeafButtons ts |> UI.map BookmarkUIMessage)
                | None -> return div [style "font-style:italic"] [text "no bookmark selected"]
            } 
        
        let bookmarksGroupButtons (model : AdaptiveModel) = 
            let ts = model.scene.bookmarks.activeGroup
            adaptive {  
                let! ts = ts
                return (GroupsApp.viewGroupButtons ts |> UI.map BookmarkUIMessage)
            } 
        
        let bookmarkUI (m : AdaptiveModel) = 
            let item2 = 
                m.scene.bookmarks.lastSelectedItem 
                |> AVal.bind (fun x -> 
                    match x with 
                    | SelectedItem.Group -> bookmarkGroupProperties m
                    | _ -> viewBookmarkProperties m 
                )
            let buttons =
                m.scene.bookmarks.lastSelectedItem 
                |> AVal.bind (fun x -> 
                    match x with 
                    | SelectedItem.Group -> bookmarksGroupButtons m
                    | _ -> bookmarksLeafButtonns m 
                )
            div [] [
                br []
                Bookmarks.UI.viewGUI |> UI.map BookmarkMessage
      
                GuiEx.accordion "Bookmarks" "Write" true [
                    //Groups.viewSelectionButtons |> UI.map BookmarkUIMessage
                    Bookmarks.viewBookmarksGroups m.scene.bookmarks |> UI.map BookmarkMessage
                ]                
                GuiEx.accordion "Properties" "Content" true [
                    Incremental.div AttributeMap.empty ( item2 |> AList.ofAValSingle)
                ] 
                GuiEx.accordion "Actions" "Asterisk" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle (buttons))
                ]
            ]

    module SequencedBookmarks =

        let sequencedBookmarksUI (m : AdaptiveModel) =           
          div [] [
              yield br []
              yield (SequencedBookmarksApp.UI.viewBookmarkControls m.scene.sequencedBookmarks)
              yield GuiEx.accordion "SequencedBookmarks" "Write" true [
                  SequencedBookmarksApp.UI.viewSequencedBookmarks m.scene.sequencedBookmarks
              ]        
              yield GuiEx.accordion "Properties" "Content" true [
                  Incremental.div AttributeMap.empty (AList.ofAValSingle(SequencedBookmarksApp.UI.viewProperties m.scene.sequencedBookmarks)) 
              ]
              yield GuiEx.accordion "Animation" "Write" true [
                SequencedBookmarksApp.UI.viewAnimationGUI m.scene.sequencedBookmarks
              ]   
              yield GuiEx.accordion "Snapshots" "Write" true [
                  SequencedBookmarksApp.UI.viewSnapshotGUI m.scene.sequencedBookmarks
              ]                 
          ] |> UI.map SequencedBookmarkMessage
    
    //TODO refactor: two codes for resize attachments
    module Pages =
        let mutable renderViewportSizeId = System.Guid.NewGuid().ToString()
        let pageRouting viewerDependencies bodyAttributes (m : AdaptiveModel) viewInstrumentView viewRenderView (runtime : IRuntime) request =
            
            match Map.tryFind "page" request.queryParams with
            | Some "instrumentview" ->
                let id = System.Guid.NewGuid().ToString()

                let onResize (cb : V2i -> 'msg) =
                    onEvent "onresize" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

                let onFocus (cb : V2i -> 'msg) =
                    onEvent "onfocus" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

                let instrumentViewAttributes =
                    amap {
                        let! hor, vert = ViewPlanApp.getInstrumentResolution m.scene.viewPlans
                        let height = "height:" + (vert/uint32(2)).ToString() + ";" ///uint32(2)
                        let width = "width:" + (hor/uint32(2)).ToString() + ";" ///uint32(2)
                        yield onResize (fun s -> OnResize(s, id))
                        yield onFocus (fun s -> OnResize(s, id))
                        yield style ("background: #1B1C1E;" + height + width)
                        yield Events.onClick (fun _ -> SwitchViewerMode ViewerMode.Instrument)
                    } |> AttributeMap.ofAMap
                      |> AttributeMap.mapAttributes (AttributeValue.map ViewerMessage)

                require (viewerDependencies) (
                    body [ style "background: #1B1C1E; width:100%; height:100%; overflow-y:auto; overflow-x:auto;"] [
                      Incremental.div instrumentViewAttributes (
                        alist {
                            yield viewInstrumentView runtime id m 
                            yield textOverlaysInstrumentView m.scene.viewPlans
                            yield depthColorLegend m
                        } )
                    ]
                )
            | Some "render" -> 
                require (viewerDependencies) (

                    renderViewportSizeId <- System.Guid.NewGuid().ToString()

                    let onResize (cb : V2i -> 'msg) =
                        onEvent "onresize" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

                    let onFocus (cb : V2i -> 'msg) =
                        onEvent "onfocus" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

                    let renderViewAttributes : list<Attribute<ViewerAnimationAction>> = 
                        [ 
                        style "background: #1B1C1E; height:100%; width:100%"
                        Events.onClick (fun _ -> SwitchViewerMode ViewerMode.Standard)
                        onResize (fun s -> OnResize(s, renderViewportSizeId))
                        onFocus (fun s -> OnResize(s, renderViewportSizeId))
                        onMouseDown (fun button pos -> StartDragging (pos, button))
                     //   onMouseMove (fun delta -> Dragging delta)
                        onMouseUp (fun button pos -> EndDragging (pos, button))
                        //onMouseEnter (fun pos ->  (MouseIn pos))
                        onMouseOut (fun pos ->  (MouseOut pos))
                        ] |> List.map (ViewerUtils.mapAttribute ViewerMessage)

                    body renderViewAttributes [ //[ style "background: #1B1C1E; height:100%; width:100%"] [
                        //div [style "background:#000;"] [
                        Incremental.div (AttributeMap.ofList [style "background:#000;"]) (
                            alist {
                                yield viewRenderView runtime renderViewportSizeId m
                                yield textOverlays m.scene.referenceSystem m.navigation.camera.view
                                yield textOverlaysUserFeedback m.scene
                                yield dnsColorLegend m
                                yield (ComparisonApp.viewLegend m.scene.comparisonApp)
                                yield scalarsColorLegend m
                                yield selectionRectangle m
                                //yield PRo3D.Linking.LinkingApp.sceneOverlay m.linkingModel |> UI.map LinkingActions
                                //                                                           |> UI.map ViewerMessage
                            }
                        )
                    ]                
                )
            | Some "surfaces" -> 
                require (viewerDependencies) (
                    body bodyAttributes
                        [SurfaceApp.surfaceUI m.scene.scenePath Config.colorPaletteStore m.scene.surfacesModel |> UI.map SurfaceActions |> UI.map ViewerMessage] 
                )
            | Some "annotations" -> 
                require (viewerDependencies) (body bodyAttributes [Annotations.annotationUI m
                                                                        |> UI.map ViewerMessage])
            | Some "validation" -> 
                require (viewerDependencies) (body bodyAttributes [HeightValidatorApp.viewUI m.heighValidation 
                                                                            |> UI.map HeightValidation
                                                                            |> UI.map ViewerMessage])
            | Some "bookmarks" -> 
                require (viewerDependencies) (body bodyAttributes [Bookmarks.bookmarkUI m |> UI.map ViewerMessage])
            | Some "comparison" -> 
                require (viewerDependencies) (body bodyAttributes [PRo3D.ComparisonApp.view m.scene.comparisonApp m.scene.surfacesModel
                                                                    |> UI.map ComparisonMessage
                                                                    |> UI.map ViewerMessage])
            | Some "sceneobjects" -> 
                require (viewerDependencies) (body bodyAttributes [SceneObjects.sceneObjectsUI m |> UI.map ViewerMessage])
            | Some "scalebars" -> 
                require (viewerDependencies) (body bodyAttributes [ScaleBars.scaleBarsUI m |> UI.map ViewerMessage])
            | Some "traverse" -> 
                require (viewerDependencies) (body bodyAttributes [Traverse.traverseUI m |> UI.map ViewerMessage])
            | Some "geologicSurf" -> 
                require (viewerDependencies) (body bodyAttributes [GeologicSurfaces.geologicSurfacesUI m |> UI.map ViewerMessage])
            | Some "sequencedBookmarks" -> 
                require (viewerDependencies) (body bodyAttributes [SequencedBookmarks.sequencedBookmarksUI m |> UI.map ViewerMessage])
            | Some "properties" ->
                let prop = 
                    m.drawing.annotations.lastSelectedItem
                    |> AVal.bind (fun x -> 
                        match x with 
                        | SelectedItem.Group -> Annotations.annotationGroupProperties m
                        | _ -> Annotations.viewAnnotationProperties m
                    )

                let results = 
                    m.drawing.annotations.lastSelectedItem
                    |> AVal.bind (fun x -> 
                        match x with 
                        | SelectedItem.Group -> Annotations.annotationGroupProperties m
                        | _ -> Annotations.viewAnnotationResults m 
                    )

                let blurg ()=
                    [
                        GuiEx.accordion "Properties" "Content" true [
                                           Incremental.div AttributeMap.empty (AList.ofAValSingle prop)
                        ]
                                       
                        GuiEx.accordion "Measurements" "Content" true [
                            Incremental.div AttributeMap.empty (AList.ofAValSingle results)                                        
                        ]
                        
                        GuiEx.accordion "Dip&Strike" "Calculator" false [
                            Incremental.div AttributeMap.empty (AList.ofAValSingle(Annotations.viewDipAndStrike m))]
                    ]

                require (viewerDependencies) (body bodyAttributes (blurg() |> List.map (UI.map ViewerMessage)))
            | Some "config" -> 
                require (viewerDependencies) (body bodyAttributes [Config.configUI m |> UI.map ViewerMessage])
            | Some "viewplanner" -> 
                require (viewerDependencies) (body bodyAttributes [ViewPlanner.viewPlannerUI m |> UI.map ViewerMessage])
            //| Some "minerva" -> 
            //   //let pos = m.scene.navigation.camera.view |> AVal.map(fun x -> x.Location)
            //    let minervaItems = 
            //        PRo3D.Minerva.MinervaApp.viewFeaturesGui m.minervaModel |> List.map (UI.map MinervaActions)

            //    let linkingItems =
            //        [
            //            Html.SemUi.accordion "Linked Products" "Image" false [
            //                PRo3D.Linking.LinkingApp.viewSideBar m.linkingModel |> UI.map LinkingActions
            //            ]
            //        ]

            //    require (viewerDependencies @ Html.semui) (
            //        body bodyAttributes (minervaItems  @ linkingItems
            //                                |> List.map ( UI.map ViewerMessage))
            //    )
            //| Some "linking" ->
            //    require (viewerDependencies) (
            //        body bodyAttributes [
            //            PRo3D.Linking.LinkingApp.viewHorizontalBar m.minervaModel.session.selection.highlightedFrustra m.linkingModel 
            //                    |> UI.map LinkingActions
            //                    |> UI.map ViewerMessage
            //        ]
            //    )
            //| Some "corr_logs" ->
            //    CorrelationPanelsApp.viewLogs m.correlationPlot
            //    |> UI.map CorrelationPanelMessage
            //| Some "corr_svg" -> 
            //    CorrelationPanelsApp.viewSvg m.correlationPlot
            //    |> UI.map CorrelationPanelMessage
            //| Some "corr_semantics" -> 
            //    CorrelationPanelsApp.viewSemantics m.correlationPlot
            //    |> UI.map CorrelationPanelMessage
            //| Some "corr_mappings" -> 
            //    require (myCss) (
            //        body bodyAttributes [
            //            CorrelationPanelsApp.viewMappings m.correlationPlot |> UI.map CorrelationPanelMessage
            //        ] )
            | Some "provenance" ->
                require (viewerDependencies) (body bodyAttributes [ProvenanceApp.view m |> UI.map ProvenanceMessage])
            | Some "gis" ->
                require (viewerDependencies) (
                    body bodyAttributes 
                         [GisApp.view m.scene.gisApp 
                                      m.scene.surfacesModel 
                                      m.scene.sequencedBookmarks
                            |> UI.map GisAppMessage
                            |> UI.map ViewerMessage]
                )
            | None -> 
                require (viewerDependencies) (
                    onBoot (sprintf "document.title = '%s'" Config.title) (
                        body [] [                    
                            TopMenu.getTopMenu m
                            |> UI.map ViewerMessage
                            div [clazz "dockingMainDings"] [
                                m.scene.dockConfig
                                |> docking [                                           
                                    style "width:100%; height:100%; background:#F00"
                                    onLayoutChanged UpdateDockConfig
                                    |> ViewerUtils.mapAttribute ViewerMessage
                                ]
                            ]
                        ]
                    )
                )
            | _ -> body [] []
