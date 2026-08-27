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
open PRo3D.ImageMapping

module Gui =            
    
    let pitchAndBearing (r:AdaptiveReferenceSystem) (view:aval<CameraView>) =
        adaptive {
          let! up    = r.up.value
          let! north = r.northO//r.north.value   
          let! v     = view
        
          return (Calculations.pitch up v.Forward, Calculations.bearing up north v.Forward)
        }

    let falseColorAttributes =
        [        
                "display"               => "block"; 
                "width"                 => "55px"; 
                "height"                => "75%"; 
                "preserveAspectRatio"   => "xMidYMid meet"; 
                "viewBox"               => "0 0 5% 100%" 
                "style"                 => "position:absolute; left: 0%; top: 25%"
                "pointer-events"        => "None"
        ] 
        |> AttributeMap.ofList
    
    let dnsColorLegend (m : AdaptiveModel) =
        let falseColorSvg = FalseColorLegendApp.Draw.createFalseColorLegendBasics "DnsLegend" m.drawing.dnsColorLegend

        Incremental.Svg.svg falseColorAttributes falseColorSvg

    /// the loaded annotations, for the attributes that need to enumerate them (surfaces)
    let annotationSet (m : AdaptiveModel) : aset<AdaptiveAnnotation> =
        m.drawing.annotations.flat
        |> AMap.toASet
        |> ASet.choose (fun (_, leaf) ->
            match leaf with
            | AdaptiveAnnotations a -> Some a
            | _ -> None)

    /// Same placement as falseColorAttributes, only wider: the hue wheel drawn for the
    /// cyclic attributes captions itself, and the caption does not fit into 55px.
    let colorByCategoryAttributes =
        [
                "display"               => "block";
                "width"                 => "170px";
                "height"                => "75%";
                "preserveAspectRatio"   => "xMidYMid meet";
                "viewBox"               => "0 0 5% 100%"
                "style"                 => "position:absolute; left: 0%; top: 25%"
                "pointer-events"        => "None"
        ]
        |> AttributeMap.ofList

    let colorByCategoryLegend (m : AdaptiveModel) =
        Incremental.Svg.svg colorByCategoryAttributes
            (ColorByCategory.Draw.legend m.drawing.colorByCategory)
                            
    let scalarsColorLegend (m : AdaptiveModel) =
        Incremental.Svg.svg falseColorAttributes (SurfaceApp.showColorLegend m.scene.surfacesModel)

    let depthColorLegend (m : AdaptiveModel) =
        let falseColorSvg = FalseColorLegendApp.Draw.createFalseColorLegendBasics "DepthLegend" m.footPrint.depthColorLegend                
        Incremental.Svg.svg falseColorAttributes falseColorSvg

    let projectedColorLegend (m : AdaptiveModel) =     
        let legend = 
            alist {
                let! selectedImage = PRo3D.GIS.ProjectedImagesListAppHelper.getSelectedImage m.scene.gisApp.projectedImageList

                match selectedImage with 
                | Some iM -> 
                    let falseColorSvg = FalseColorLegendApp.Draw.createFalseColorLegendBasics "ProjectedLegend" iM.falseColorModel
                    yield Incremental.Svg.svg AttributeMap.empty falseColorSvg
                | None -> yield div [] []                
        } 

        Incremental.Svg.svg falseColorAttributes legend

    
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
                    | Planet.Moon  -> "Moon"
                    | Planet.Deimos -> "Deimos"
                    | Planet.Phobos -> "Phobos"
                    | Planet.Dimorphos -> "Dimorphos"
                    | Planet.Didymos -> "Didymos"
                    | _ -> "[TextOverlays] missing text representation for selected planet."
                )  
            
            let pnb = pitchAndBearing m cv

            // Bearing/pitch suppressed on small bodies. The current math
            // (AnnotationHelpers.bearing/pitch + ReferenceSystem.northVector)
            // assumes world +Z is the body's north pole and computes pitch
            // against a global plane through origin -- both wrong for small
            // irregular bodies like Dimorphos (north pole is -Z in SHM, and
            // the camera sits a body-radius away from origin). Better to show
            // nothing than nonsense. See TODOS.md "small-body bearing/pitch
            // overlay" before re-enabling.
            let pitch =
                AVal.map2 (fun (p,_) planet ->
                    if CooTransformation.isSmallBody planet then "n/a"
                    else sprintf "%s deg" ((p : float).ToString("0.00"))) pnb m.planet
            let bearing =
                AVal.map2 (fun (_,b) planet ->
                    if CooTransformation.isSmallBody planet then "n/a"
                    else sprintf "%s deg" ((b : float).ToString("0.00"))) pnb m.planet
            
            let position = cv |> AVal.map(fun x -> x.Location.ToString("0.00"))
            
            let spericalc =
                AVal.map2 (fun (a : CameraView) b ->
                    CooTransformation.tryGetLatLonAlt b a.Location
                ) cv m.planet

            let altitude =
                AVal.map2 (fun (a : CameraView) b ->
                    CooTransformation.tryGetAltitude a.Location a.Up b) cv m.planet

            let formatCoo (project : CooTransformation.SphericalCoo -> string) =
                spericalc |> AVal.map (function
                    | Some sc -> project sc
                    | None    -> "conversion failed (set planet)")

            let lon = formatCoo (fun x -> sprintf "%s deg" ((360.0 - x.longitude).ToString()))
            let lat = formatCoo (fun x -> sprintf "%s deg" (x.latitude.ToString()))

            let alt2 =
                altitude |> AVal.map (function
                    | Some v -> sprintf "%s m" (v.ToString("0.00"))
                    | None   -> "conversion failed (set planet)")

            let conventionLabel =
                m.planet |> AVal.map (fun p ->
                    match CooTransformation.getConvention p with
                    | CooTransformation.Planetographic    -> "planetographic"
                    | CooTransformation.Spherical r       -> sprintf "spherical r=%.1fm" r
                    | CooTransformation.Ellipsoidal _     -> "ellipsoidal"
                    | CooTransformation.NonPlanetary      -> "n/a")
                                                   
            let style' = "color: white; font-family: Roboto Mono"
            
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
                    tr [] [
                        td [style style'] [text "Convention: "]
                        td [style style'] [Incremental.text conventionLabel]
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
                        td [style "color: white; font-family: Roboto Mono"] [Incremental.text instrument]
                    ]
                ]
            ]                              
        ]
    
    let textOverlaysUserFeedback (m : AdaptiveScene)  = 
        div [js "oncontextmenu" "event.preventDefault();"] [ 
            let style' = "color: white; font-family: Roboto Mono; font-size:16;"
            
            yield div [clazz "ui"; style "text-align: right; width: 250px; position: absolute; top: 15px; right: 15px; float:right" ] [ //float:left
                //arrowOverlay
                yield table [] [
                    tr [] [
                        td [style style'] [Incremental.text m.userFeedback]
                    ]
                ]
            ]                              
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

        // Every data export lives in the export window now; only the native
        // round-trip format stays here, since it has no settings. The automatic
        // GeoJSON stream is armed from the window too (its "Continuous GeoJSON"
        // file type) and switched off again in the config panel.
        let annotationMenu : DomNode<ViewerAction> =
            let drawingItem attributes children =
                div attributes children |> UI.map DrawingMessage

            div [ clazz "ui dropdown item"] [
                text "Annotations"
                i [clazz "dropdown icon"] []
                div [ clazz "menu"] [
                    drawingItem [
                        clazz "ui inverted item"
                        Dialogs.onChooseFiles AddAnnotations
                        clientEvent "onclick" jsOpenAnnotationFileDialog
                    ] [
                        text "Import Directory"
                    ]
                    drawingItem [
                        clazz "ui inverted item"; onMouseClick (fun _ -> Clear)
                    ] [
                        text "Clear"
                    ]
                    div [
                        clazz "ui inverted item"
                        onClick (fun _ -> AnnotationExportMessage AnnotationExportAction.Open)
                    ] [
                        text "Export..."
                    ]
                    drawingItem [
                        clazz "ui inverted item"
                        Dialogs.onSaveFile ExportAsAnnotations
                        clientEvent "onclick" jsExportAnnotationsFileDialog
                    ] [
                        text "Save as 'PRo3D' annotations (*.pro3d.ann)"
                    ]
                ]
            ]

        // Checkbox-style menu item bound to a single bool flag on
        // `m.userPreferences`. Click toggles the flag — the update handler
        // dispatches SetUserPreferences which both updates the model and
        // writes the JSON file under %APPDATA%/Pro3D.
        let prefToggle (label : string)
                       (getter : UserPreferences -> bool)
                       (setter : bool -> UserPreferences -> UserPreferences)
                       (m : AdaptiveModel) =
            let iconAttrs =
                amap {
                    let! prefs = m.userPreferences
                    yield clazz (if getter prefs then "check square outline icon" else "square outline icon")
                } |> AttributeMap.ofAMap

            div [ clazz "ui item"
                  onClick (fun _ ->
                      let prefs = AVal.force m.userPreferences
                      SetUserPreferences (setter (not (getter prefs)) prefs)) ] [
                Incremental.i iconAttrs AList.empty
                text (" " + label)
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
                                annotationMenu;   
                                subMenu "Change Mode"
                                        [
                                          menuItem "M2020" (ChangeDashboardMode DashboardModes.m2020)
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

                                        // SBMT structure files: real fixtures sometimes have no extension
                                        // (e.g. boulder catalogs) so the filter is wide. Frame is hardcoded
                                        // to DIMORPHOS_SHM in the handler -- see plans/archive/sbmtImport.md.
                                        let jsOpenSbmtFileDialogue = "top.aardvark.dialog.showOpenDialog({title:'Import SBMT annotations', filters: [{ name: 'SBMT structure files', extensions: ['txt', '*']}], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

                                        div [ clazz "ui item";
                                            Dialogs.onChooseFiles ImportSbmtAnnotations;
                                            clientEvent "onclick" jsOpenSbmtFileDialogue ] [
                                            text "Import SBMT Annotations"
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


                                        // SP: remove code if loading of projected images does work
                                        let jsImportImages = "top.aardvark.dialog.showOpenDialog({tile: 'Select directory to import images from', filters: [{ name: 'OPC (directories)'}], properties: ['openDirectory']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
                                        div [ clazz "ui item"; Dialogs.onChooseFiles (function [p] -> ViewerAction.GisAppMessage (GisAppAction.ProjectedImageListMessage (ProjectedImageListApp.loadDirMessage p)) | _ -> ViewerAction.Nop); clientEvent "onclick" jsImportImages ] [
                                            text "Load Image Projections"
                                        ]

                                        

                                        //menuItem "Create Pose File from SBookmarks" SBookmarksToPoseDefinition // for debugging
                                        div [clazz "ui item"; clientEvent "onclick" "aardvark.showReportDialog?.()"] [ // server-mode (i.e. deploy version) only
                                            text "Report Issue"
                                        ]
                                        div [clazz "ui item"; clientEvent "onclick" "aardvark.showLogViewer?.()"] [ // server-mode (i.e. deploy version) only
                                            text "View Log"
                                        ]
                                        a [style "visibility:hidden"; clazz "invisibleCrashButton"] []

                                        //let jsImportTrafosDialog = "top.aardvark.dialog.showOpenDialog({title:'Import Transformation files' , filters: [{ name: 'Trafos (*.json)', extensions: ['json']},], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

                                        //div [ clazz "ui item"; Dialogs.onChooseFiles ImportTrafo; clientEvent "onclick" jsImportTrafosDialog ] [
                                        //    text "Import Transformation (*.json)"
                                        //]

                                        //div [clazz "ui item"; onClick (fun _ ->  ViewerAction.Nop)] [
                                        //    text "Send Crash Report"
                                        //    a [attribute "href" "mailto:hs@pro3d.com?attach=C:\\Program Files (x86)\\ProcessExplorer\\procexp64.exe"] [text "go"]
                                        //]
                                    ]
                                ]

                                // Preferences: per-computer settings persisted to
                                // %APPDATA%/Pro3D/userPreferences.json. NOT part of
                                // the scene file or any bookmark.
                                div [ clazz "ui dropdown item"] [
                                    text "Preferences"
                                    i [clazz "dropdown icon"] []
                                    div [ clazz "menu"] [
                                        div [clazz "header"; style "padding: 6px 10px; color: black; font-weight: bold"] [
                                            text "MapView controls"
                                        ]
                                        prefToggle "Invert W / S (forward-back)"
                                            (fun p -> p.mapInvertForward)
                                            (fun b p -> { p with mapInvertForward = b })
                                            m
                                        prefToggle "Invert A / D (strafe)"
                                            (fun p -> p.mapInvertStrafe)
                                            (fun b p -> { p with mapInvertStrafe = b })
                                            m
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
                    let measurementTooltip = "Measurement to adapt the size of the axis gizmo"
                    let visibilityTooltip = "Toggle visibility of axis gizmo"
                    return Html.Layout.horizontal [
                        Html.Layout.boxH [ Html.SemUi.dropDown' m.scene.referenceSystem.scaleChart m.scene.referenceSystem.selectedScale ReferenceSystemAction.SetScale id ] |> UI.wrapToolTip DataPosition.Bottom measurementTooltip
                        Html.Layout.boxH [ GuiEx.iconToggle m.scene.referenceSystem.isVisible "unhide icon" "hide icon" ReferenceSystemAction.ToggleVisible  ] |> UI.wrapToolTip DataPosition.Bottom visibilityTooltip                     
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
            
        let style' = "color: white; font-family: Roboto Mono"

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
            | Interactions.PickSurfaceRefSys     -> sprintf "%s+click to place additional reference system for surface" ctrl
            //| Interactions.PickLinking           -> "CTRL+click to place point on surface"
            | _ -> ""

        /// As interactionText, but also reflects whether a control point is currently in hand.
        /// Click-to-grab has no drag affordance to feel out, so the hint line is most of what makes
        /// the gesture discoverable.
        let interactionTextWithState (i : Interactions) (grabbed : bool) =
            let ctrl = if RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) then "CMD" else "CTRL"
            match i with
            | Interactions.EditAnnotation when grabbed -> sprintf "%s+click to drop the point, ESC to cancel" ctrl
            | Interactions.EditAnnotation -> sprintf "%s+click a vertex of the selected annotation to move it" ctrl
            | _ -> interactionText i

        let interactionTooltip (i : Interactions) : string =
            match i with 
            | Interactions.PickExploreCenter     -> "Pick the camera pivot point if ArcBall navigation is activated."
            | Interactions.PlaceCoordinateSystem -> "Pick a point on the surface and choose a unit of measurement to adapt the size of the axis gizmo."
            | Interactions.DrawAnnotation        -> "Choose an annotation mode to draw an annotation on a surface."
            | Interactions.PlaceRover            -> "Select a rover model in the rover menu."
            | Interactions.PickAnnotation        -> "Select an annotation in the main view. The selected annotation will be highlighted green."
            | Interactions.EditAnnotation        -> "Move the vertices of the selected annotation. Its control points appear as handles; click one to pick it up, move the cursor over the surface and click again to put it down. Clicking an annotation selects it."
            | Interactions.PickSurface           -> "Select a surface in the main view. The selected surface will be highlighted green."
            | Interactions.SelectArea            -> ""
            | Interactions.PlaceScaleBar         -> ""
            | Interactions.PlaceSceneObject      -> ""
            | Interactions.PickPivotPoint        -> ""
            | _ -> ""

        let invertDrawingTooltip =
            "Invert drawing: swap the Ctrl modifier - pick and draw without Ctrl, hold Ctrl to navigate."

        let topMenuItems (model : AdaptiveModel) = [
            div [style "font-weight: bold;margin-left: 1px; margin-right:1px"]
                [Incremental.text (model.dashboardMode |> AVal.map (fun x -> sprintf "Mode: %s" x))]
            Navigation.UI.viewNavigationModes model.scene.referenceSystem.planet model.navigation |> UI.map NavigationMessage

            // Interaction selector + ctrl-click hint. The mode-specific tool
            // controls (annotation geometry, rover selector, etc.) live on the
            // secondary toolbar row below so the planet selector and scene
            // path stay visible on the main row at every window width.
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large wizard icon"] [] ]
                Html.Layout.boxH [ Drawing.UI.dropDown Interactions.hideSet model.interaction SetInteraction interactionTooltip ]
                Html.Layout.boxH [
                    div [style "font-style:italic"] [
                        Incremental.text (
                            (model.interaction, model.drawing.vertexGrab |> AVal.map Option.isSome)
                            ||> AVal.map2 interactionTextWithState)
                    ]]
            ]

            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large Globe icon"] [] ]
                Html.Layout.boxH [ Html.SemUi.dropDown model.scene.referenceSystem.planet ReferenceSystemAction.SetPlanet ] |> UI.map ReferenceSystemMessage
            ]

            // Inverts the Ctrl convention (picking = ctrlFlag <> inverseFlag). It is a global
            // interaction-mode switch like the two items above, so it lives on the main row
            // rather than in the Annotations dock page.
            Html.Layout.horizontal [
                Html.Layout.boxH [ GuiEx.iconToggle model.inverseFlag "toggle on icon" "toggle off icon" ViewerAction.InvertDrawing ]
                Html.Layout.boxH [ text "Invert Drawing" ]
            ] |> UI.wrapToolTip DataPosition.Bottom invertDrawingTooltip

            Html.Layout.horizontal [
                scenepath model
            ]
        ]

        // The secondary toolbar is always rendered (even when the active
        // interaction has no tool controls) so the dock below never jumps
        // when switching tools. `dynamicTopMenu` returns an empty div for
        // interactions without a secondary toolbar; the row height stays
        // stable thanks to the wrapping `.ui.menu`'s min-height.
        let secondaryToolbarRow (m : AdaptiveModel) =
            div [clazz "ui menu pro3d-secondary-toolbar"; style "padding:0; margin:0; border:0"] [
                div [clazz "item topmenu"] [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle (dynamicTopMenu m))
                ]
            ]

        let getTopMenu (m:AdaptiveModel) =
            div [clazz "pro3d-topbar"] [
                div [clazz "ui menu"; style "padding:0; margin:0; border:0"] [
                    yield (menu m)
                    for t in (topMenuItems m) do
                        yield div [clazz "item topmenu"] [t]
                ]
                secondaryToolbarRow m
            ]
        
    module Annotations =
      
        let viewAnnotationProperties (model : AdaptiveModel) =
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> AnnotationProperties.view Config.colorPaletteStore ann
                  | _ -> div [style "font-style:italic"] [ text "no annotation selected" ])
            
            model.drawing.annotations |> GroupsApp.viewSelected view AnnotationMessage

        // Bulk edit surface: targets the green multi-selection (selectedLeaves), which stays
        // distinct from the single-selection that drives the Properties panel. Fields bind to a
        // representative annotation but every edit is routed (write-only) to all selected.
        let viewBulkAnnotationProperties (model : AdaptiveModel) =
            let annotations = model.drawing.annotations
            adaptive {
                let! selected = annotations.selectedLeaves.Content
                let selectedList = selected |> HashSet.toList
                let count = selectedList |> List.length
                let! single = annotations.singleSelectLeaf

                // representative the fields show: the single-selected one when it is part of the
                // multi-selection, otherwise the first selected leaf (e.g. after a group Select All)
                let repId =
                    match single with
                    | Some id when selectedList |> List.exists (fun ts -> ts.id = id) -> Some id
                    | _ -> selectedList |> List.tryHead |> Option.map (fun ts -> ts.id)

                if count < 2 then
                    return div [style "font-style:italic; padding:5px"]
                               [ text "Select two or more annotations (multi-select via the cube icons or a group's Select All) to bulk edit." ]
                else
                    match repId with
                    | None -> return div [] []
                    | Some id ->
                        match! AMap.tryFind id annotations.flat with
                        | Some (AdaptiveAnnotations ann) ->
                            let header =
                                h5 [clazz "ui inverted horizontal divider header"; style "padding-top: 1rem"]
                                   [ text (sprintf "%d annotations selected — edits apply to all" count) ]
                            let clear =
                                div [style "padding: 5px 0px"] [
                                    button [
                                        clazz "ui tiny button"
                                        onClick (fun _ -> ViewerAction.DrawingMessage(DrawingAction.GroupsMessage(GroupsAppAction.ClearSelection)))
                                    ] [ text "Clear selection" ]
                                ]
                            let fields =
                                AnnotationProperties.viewBulk Config.colorPaletteStore ann
                                |> UI.map ViewerAction.AnnotationBulkMessage

                            // Dip-direction rose over the selection, restricted to the geometry
                            // types the user enabled. dipAzimuth is a stored field (read via the
                            // adaptive dnsResults option); see `angles` below for the shape of the
                            // adaptive graph this builds and what invalidates it.
                            let roseHeader =
                                h5 [clazz "ui inverted horizontal divider header"; style "padding-top: 1rem"]
                                   [ text "Dip direction rose" ]
                            // Activation button: the rose (and its type toggles) only exist while
                            // the feature is switched on, so nothing is binned when it is off.
                            let roseActivation =
                                Incremental.div (AttributeMap.ofList [style "padding: 5px 0px"]) (
                                    alist {
                                        let! enabled = model.roseEnabled
                                        yield button [
                                            clazz (if enabled then "ui tiny blue button" else "ui tiny button")
                                            onClick (fun _ -> ViewerAction.SetRoseEnabled (not enabled))
                                        ] [
                                            i [clazz (if enabled then "toggle on icon" else "toggle off icon")] []
                                            text (if enabled then "Rose diagram on" else "Rose diagram off")
                                        ]
                                    })
                            let toggles =
                                require GuiEx.semui (
                                    Html.table [
                                        Html.row "Polyline:" [ GuiEx.iconCheckBoxSet model.roseUsePolyline ViewerAction.SetRoseUsePolyline ]
                                        Html.row "DnS:"      [ GuiEx.iconCheckBoxSet model.roseUseDnS      ViewerAction.SetRoseUseDnS ]
                                    ])
                            // Dip azimuths of the selected annotations, collected as one
                            // incremental aggregate rather than one lookup per annotation:
                            //  * a single AMap.filter reader on `flat` replaces N AMap.tryFind
                            //    calls. AMap.tryFind is documented as re-evaluating on *every*
                            //    change of the map, so N of them turned finishing, deleting or
                            //    importing a single annotation into N invalidated lookups; the
                            //    filter sees the same event as one incremental add/remove.
                            //  * AMap.chooseA caches (geometry, azimuth) per annotation, so an
                            //    edit to one annotation re-reads that one and nothing else.
                            //  * the type toggles are applied at the *leaf*, filtering the already
                            //    collected map. Binding them above the per-annotation work would
                            //    make every checkbox click tear that whole subtree down and
                            //    rebuild it.
                            // The selection itself is still whole-value: it is bound by the
                            // enclosing adaptive block, which rebuilds the panel anyway.
                            // Annotations with no dip and strike surface here as NaN (the
                            // bindAdaptiveOption default), which RoseDiagram.includes rejects
                            // along with a genuinely NaN dipAzimuth.
                            let angles : aval<list<float>> =
                                let ids = selected |> HashSet.map (fun ts -> ts.id)
                                let perAnnotation =
                                    annotations.flat
                                    |> AMap.filter (fun annoId _ -> ids |> HashSet.contains annoId)
                                    |> AMap.chooseA (fun _ leaf ->
                                        match leaf with
                                        | AdaptiveAnnotations a ->
                                            AVal.map2
                                                (fun geo az -> Some(geo, az))
                                                a.geometry
                                                (AVal.bindAdaptiveOption a.dnsResults nan (fun d -> d.dipAzimuth))
                                        | _ -> AVal.constant None)
                                    |> AMap.toAVal
                                // Single fold - no intermediate list, one traversal per
                                // invalidation. RoseDiagram.includes is the one place that
                                // decides whether an annotation counts, shared with the tests.
                                let selectEnabled
                                    (perAnno : HashMap<System.Guid, PRo3D.Base.Annotation.Geometry * float>)
                                    usePoly useDns =
                                    perAnno
                                    |> HashMap.fold (fun acc _ (geo, az) ->
                                        if RoseDiagram.includes usePoly useDns geo az
                                        then az :: acc
                                        else acc) []
                                AVal.map3 selectEnabled perAnnotation model.roseUsePolyline model.roseUseDnS
                            let rose =
                                Incremental.div AttributeMap.empty (
                                    alist {
                                        let! enabled = model.roseEnabled
                                        if enabled then
                                            yield toggles
                                            let! angs = angles
                                            if List.isEmpty angs then
                                                yield div [style "font-style:italic; padding:5px"]
                                                          [ text "No dip directions in selection (enable a type, or select Polyline / DnS annotations)." ]
                                            else
                                                yield RoseDiagram.view angs
                                    })

                            return div [] [ header; fields; roseHeader; roseActivation; rose; clear ]
                        | _ ->
                            return div [style "font-style:italic; padding:5px"] [ text "no annotation selected" ]
            }

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

        /// Unlike the other Properties panels this one is global rather than per-selection,
        /// so it does not go through GroupsApp.viewSelected.
        let viewColorByCategory (model : AdaptiveModel) =
            ColorByCategory.view Config.colorPaletteStore (annotationSet model) model.drawing.colorByCategory
            |> UI.map (fun a -> ViewerAction.DrawingMessage(DrawingAction.ColorByCategoryMessage a))
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
                GuiEx.accordion "Curtain Settings" "Expand" false [
                    CrossSectionApp.viewCurtainSettings m.scene.crossSectionModel |> UI.map CrossSectionMessage
                ]
                GuiEx.accordion "Dip&Strike ColorLegend" "paint brush" false [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(viewDnSColorLegendUI m))
                ] 
                GuiEx.accordion "Actions" "Asterisk" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle (buttons))
                ]
            ]

    module AnnotationExport =

        let exportWindow (m : AdaptiveModel) =
            // the window lives in PRo3D.Core and is compiled before DrawingModel,
            // so the state of the background export is handed in from here
            let state : ContinuousExportState = {
                isRunning = m.drawing.automaticGeoJsonExport.enabled
                target    = m.drawing.automaticGeoJsonExport.lastGeoJsonPathXyz
            }
            AnnotationExportApp.viewModal state m.annotationExport
            |> UI.map AnnotationExportMessage

    module Config =

        /// Read-out of the OPC per-vertex attribute layers under the 3D cursor.
        ///
        /// The values come from the hit patch's `*.aara` attribute layers, barycentrically
        /// interpolated at the picked point - not from sampling the attribute textures.
        /// Texture sampling costs an image decode per layer, which is fine for profile
        /// extraction but not per mouse move, so surfaces without per-vertex layers show
        /// nothing here and say so.
        let underCursor (m : AdaptiveModel) : DomNode<ViewerAction> =
            // several opened namespaces carry records with `name` / `surfaceName` fields,
            // so the types below are spelled out to keep field resolution unambiguous
            let formatValues (values : float[]) =
                values |> Array.map (sprintf "%g") |> String.concat "; "

            let hint (message : string) =
                div [style "color: #cccccc; padding: 4px"] [text message]

            let content =
                alist {
                    let! enabled = m.scene.config.showPreviewIntersection
                    if not enabled then
                        yield hint "Preview cursor is off - enable \"Show Preview Cursor\" above."
                    else
                        let! cursor = m.cursorAttributes
                        match cursor with
                        | None ->
                            yield hint "Hold CTRL and move the mouse over a surface."
                        | Some (cursor : CursorAttributes) ->
                            let hit : AttributeHit = cursor.hit
                            // SPICE lat/lon/alt of the picked point, shown only when available.
                            // tryGetLatLonAlt is total: None for non-planetary frames
                            // (Planet.None/JPL/ENU) and when the native PGRREC call reports an
                            // error. Its out-params are nan-seeded, so a wrapper returning
                            // success without writing cannot yield silent zeros - the finiteness
                            // filter turns that into None as well. Either way the rows are just
                            // omitted; the rest of the read-out is unaffected.
                            let! planet = m.scene.referenceSystem.planet
                            let spherical =
                                CooTransformation.tryGetLatLonAlt planet hit.position
                                |> Option.filter (fun sc ->
                                    Double.IsFinite sc.latitude &&
                                    Double.IsFinite sc.longitude &&
                                    Double.IsFinite sc.altitude)
                            yield Html.table [
                                yield Html.row "Surface:"  [text cursor.surfaceName]
                                yield Html.row "Patch:"    [text hit.patchName]
                                yield Html.row "Position:" [text (hit.position.ToString("0.000"))]
                                match spherical with
                                | Some sc ->
                                    // raw convention, matching the Coordinate System panel
                                    yield Html.row "Latitude:"  [text (sprintf "%s deg" (sc.latitude.ToString("0.00000")))]
                                    yield Html.row "Longitude:" [text (sprintf "%s deg" (sc.longitude.ToString("0.00000")))]
                                    yield Html.row "Altitude:"  [text (sprintf "%s m"   (sc.altitude.ToString("0.00")))]
                                | None -> ()
                                for a : SampledAttribute in hit.attributes do
                                    yield Html.row (a.name + ":") [text (formatValues a.values)]
                            ]
                            if hit.attributes.IsEmpty then
                                // either the surface ships no *.aara layers at all, or the hit
                                // triangle touches the position grid's skirt, which carries none
                                yield hint "No per-vertex attribute layers cover this point."
                }

            Incremental.div AttributeMap.empty content

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
                GuiEx.accordion "Under Cursor" "Crosshairs" true [
                    underCursor m
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
                yield GuiEx.accordion "Rover Traverses" "Write" true [
                    RoverTraverseApp.UI.viewTraverses m.scene.referenceSystem m.scene.traverses
                ]
                yield
                    GuiEx.accordion
                        "RIMFAX Traverses"
                        "Write"
                        true
                        [RimfaxTraverseApp.UI.viewTraverses m.scene.referenceSystem m.scene.traverses]
                yield GuiEx.accordion "WayPoint Traverses" "Write" true [
                    WayPointsTraverseApp.UI.viewTraverses m.scene.referenceSystem m.scene.traverses
                ]
                //yield GuiEx.accordion "Strategic Annotations" "Write" true [
                    // not yet implemented
                    // StrategicAnnotationsTraverseApp.UI.viewTraverses m.scene.referenceSystem m.scene.traverses
                //]
                //yield GuiEx.accordion "Planned Targets" "Write" true [
                    // not yet implemented
                    // PlannedTargetsTraverseApp.UI.viewTraverses m.scene.referenceSystem m.scene.traverses
                //]
                yield GuiEx.accordion "Actions" "Asterisk" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(TraverseApp.UI.viewActions m.scene.traverses))
                ]
                yield GuiEx.accordion "Properties" "Content" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(TraverseApp.UI.viewProperties m.scene.traverses))
                ]
                yield GuiEx.accordion "Sols" "road" true [
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
              yield GuiEx.accordion "Depth Panoramas" "Write" true [
                  SequencedBookmarksApp.UI.viewPanoramaDepthGUI m.scene.sequencedBookmarks
              ]      
          ] |> UI.map SequencedBookmarkMessage
    
    //TODO refactor: two codes for resize attachments
    module Pages =
        let mutable renderViewportSizeId = System.Guid.NewGuid().ToString()
        let pageRouting viewerDependencies bodyAttributes (m : AdaptiveModel) viewInstrumentView viewRenderView (runtime : IRuntime) (request : IHttpRequest) =
            
            match request.QueryParam "page" with
            | Some "instrumentview" ->
                let id = System.Guid.NewGuid().ToString()

                let onResize (cb : V2i -> 'msg) =
                    onEvent "onresize" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

                let onFocus (cb : V2i -> 'msg) =
                    onEvent "onfocus" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

                let instrumentViewAttributes =
                    amap {
                        let! hor, vert = ViewPlanApp.getInstrumentResolution m.scene.viewPlans
                        let height = "height:" + (vert/uint32(2)).ToString() + ";" //uint32(2)
                        let width = "width:" + (hor/uint32(2)).ToString() + ";" //uint32(2)
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
                                yield colorByCategoryLegend m
                                yield (ComparisonApp.viewLegend m.scene.comparisonApp)
                                yield scalarsColorLegend m
                                yield projectedColorLegend m
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

                        GuiEx.accordion "Bulk Edit" "edit" false [
                            Incremental.div AttributeMap.empty (AList.ofAValSingle (Annotations.viewBulkAnnotationProperties m))
                        ]

                        GuiEx.accordion "Measurements" "Content" true [
                            Incremental.div AttributeMap.empty (AList.ofAValSingle results)                                        
                        ]
                        
                        GuiEx.accordion "Dip&Strike" "Calculator" false [
                            Incremental.div AttributeMap.empty (AList.ofAValSingle(Annotations.viewDipAndStrike m))]

                        GuiEx.accordion "Color by Category" "Theme" false [
                            Incremental.div AttributeMap.empty (AList.ofAValSingle(Annotations.viewColorByCategory m))]
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
                            // Overlay window; absent from the DOM while closed,
                            // so there is no JS modal state to keep in sync.
                            AnnotationExport.exportWindow m
                            |> UI.map ViewerMessage
                        ]
                    )
                )
            | _ -> body [] []
