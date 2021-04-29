namespace PRo3D.Viewer

open Aardvark.Service

open System
open System.Diagnostics
open System.IO

open Aardvark.Base
open Aardvark.Base.Geometry
open Aardvark.Service
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Operators
open Aardvark.UI.Primitives
open Aardvark.Rendering.Text

open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.SgPrimitives.Sg
open Aardvark.VRVis

open MBrace.FsPickler
open System.IO

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

module Gui =            
    
    let pitchAndBearing (r:AdaptiveReferenceSystem) (view:aval<CameraView>) =
        adaptive {
          let! up    = r.up.value
          let! north = r.northO//r.north.value   
          let! v     = view
        
          return (DipAndStrike.pitch up v.Forward, DipAndStrike.bearing up north v.Forward)
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
            let planet = m.planet |> AVal.map(fun x -> sprintf "%s" (x.ToString()))  
            
            let pnb = pitchAndBearing m cv
            
            let pitch    = pnb |> AVal.map(fun (p,_) -> sprintf "%s deg" (p.ToString("0.00")))
            let bearing  = pnb |> AVal.map(fun (_,b) -> sprintf "%s deg" (b.ToString("0.00")))
            
            let position = cv |> AVal.map(fun x -> x.Location.ToString("0.00"))
            
            let spericalc = 
                AVal.map2 (fun (a : CameraView) b -> 
                    CooTransformation.getLatLonAlt b a.Location
                ) cv m.planet
            
            let alt2 = 
                AVal.map2 (fun (a : CameraView) b -> 
                    CooTransformation.getAltitude a.Location a.Up b ) cv m.planet
            
            let lon = spericalc |> AVal.map(fun x -> sprintf "%s deg" ((x.longitude).ToString("0.00")))
            let lat = spericalc |> AVal.map(fun x -> sprintf "%s deg" ((x.latitude).ToString("0.00")))            
            let alt2 = alt2 |> AVal.map(fun x -> sprintf "%s m" ((x).ToString("0.00")))            
                                                   
            let style' = "color: white; font-family:Consolas;"
            
            yield div [
                clazz "ui"; 
                style "position: absolute; top: 15px; left: 15px; float:left; pointer-events:None" 
                ] [                
                yield table [] [
                    tr[][
                        td[style style'][Incremental.text planet]
                    ]
                    tr[][
                        td[style style'][text "Bearing: "]
                        td[style style'][Incremental.text bearing]
                    ]
                    tr[][
                        td[style style'][text "Pitch: "]
                        td[style style'][Incremental.text pitch]
                    ]
                    tr[][
                        td[style style'][text "Position: "]
                        td[style style'][Incremental.text position]
                    ]
                    tr[][
                        td[style style'][text "Longitude: "]
                        td[style style'][Incremental.text lon]
                    ]
                    tr[][
                        td[style style'][text "Latitude: "]
                        td[style style'][Incremental.text lat]
                    ]
                    //tr[][
                    //    td[style style'][text "Altitude: "]
                    //    td[style style'][Incremental.text alt]
                    //]
                    tr[][
                        td[style style'][text "Altitude: "]
                        td[style style'][Incremental.text alt2]
                    ]                    
                ]
            ]                     
        ]
    
    let textOverlaysInstrumentView (m : AdaptiveViewPlanModel)  = 
        let instrument =
            adaptive {
                let! vp = m.selectedViewPlan
                let! inst = 
                    match Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption vp with
                    | Some v -> AVal.bindAdaptiveOption v.selectedInstrument "No instrument selected" (fun a -> a.id)
                    | None -> AVal.constant("")
        
                return inst
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
                    tr[][
                        td[style style'][Incremental.text m.userFeedback]
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

    module TopMenu =                       

        let jsImportOPCDialog =
            "top.aardvark.dialog.showOpenDialog({tile: 'Select directory to discover OPCs and import', filters: [{ name: 'OPC (directories)'}], properties: ['openDirectory', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
           
        let jsImportOBJDialog =
            "top.aardvark.dialog.showOpenDialog({tile: 'Select *.obj files to import', filters: [{ name: 'OBJ (*.obj)', extensions: ['obj']}], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

        let private importSurface =
            [
                text "Surfaces"
                i [clazz "dropdown icon"][] 
                div [ clazz "menu"] [
                    div [ clazz "ui inverted item";
                        Dialogs.onChooseFiles ImportDiscoveredSurfacesThreads;
                        clientEvent "onclick" (jsImportOPCDialog)
                    ][
                        text "Import OPCs"
                    ]
                    div [ clazz "ui inverted item"; 
                        Dialogs.onChooseFiles ImportObject;
                        clientEvent "onclick" (jsImportOBJDialog)
                    ][
                        text "Import (*.obj)"
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
                            div [ clazz "ui inverted item"; onMouseClick (fun _ -> SaveScene p)][text "Save"]
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
                i [clazz "dropdown icon"][]
                div [ clazz "menu"] [
                    //save scene
                    Incremental.div AttributeMap.empty (AList.ofAValSingle (saveSceneDialog m))

                    //save scene as
                    div [ 
                        clazz "ui inverted item"; Dialogs.onSaveFile SaveAs;
                        clientEvent "onclick" jsSaveSceneDialog
                    ][
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
                    ][      
                        text "Open"
                    ]

                    //new scene
                    div [ clazz "ui inverted item"; onMouseClick (fun _ -> NewScene)] [
                        text "New"
                    ]

                    //recent scenes
                    div[ clazz "ui inverted item" ] [
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
                        ][
                            text "Locate Surfaces"
                        ]
                }
        
            Incremental.div(AttributeMap.Empty) ui |> UI.map SurfaceActions      
            
        let jsOpenAnnotationFileDialog = 
            "top.aardvark.dialog.showOpenDialog({ title: 'Import Annotations', filters: [{ name: 'Annotations (*.ann)', extensions: ['ann']},], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

        let jsExportAnnotationsFileDialog = 
            "top.aardvark.dialog.showSaveDialog({ title: 'Save Annotations as', filters:  [{ name: 'Annotations (*.pro3d.ann)', extensions: ['pro3d.ann'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"

        let jsExportAnnotationsAsCSVDialog =
            "top.aardvark.dialog.showSaveDialog({ title: 'Export Annotations (*.csv)', filters:  [{ name: 'Annotations (*.csv)', extensions: ['csv'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"

        let jsExportAnnotationsAsGeoJSONDialog =
            "top.aardvark.dialog.showSaveDialog({ title: 'Export Annotations (*.json)', filters:  [{ name: 'Annotations (*.json)', extensions: ['json'] }] }).then(result => {top.aardvark.processEvent('__ID__', 'onsave', result.filePath);});"
              
        let annotationMenu = //todo move to viewer io gui
            div [ clazz "ui dropdown item"] [
                text "Annotations"
                i [clazz "dropdown icon"][] 
                div [ clazz "menu"] [                    
                    div [
                        clazz "ui inverted item"
                        Dialogs.onChooseFiles AddAnnotations
                        clientEvent "onclick" jsOpenAnnotationFileDialog
                    ][
                        text "Import"
                    ]
                    div [
                        clazz "ui inverted item"; onMouseClick (fun _ -> Clear)
                    ][
                        text "Clear"
                    ]                
                    div [ 
                        clazz "ui inverted item"
                        Dialogs.onSaveFile ExportAsAnnotations
                        clientEvent "onclick" jsExportAnnotationsFileDialog
                    ][
                        text "Export (*.pro3d.ann)"
                    ]
                    div [ 
                        clazz "ui inverted item"
                        Dialogs.onSaveFile ExportAsCsv
                        clientEvent "onclick" jsExportAnnotationsAsCSVDialog
                    ][
                        text "Export (*.csv)"
                    ]     
                    div [ 
                        clazz "ui inverted item"
                        Dialogs.onSaveFile ExportAsGeoJSON
                        clientEvent "onclick" jsExportAnnotationsAsGeoJSONDialog
                    ][
                        text "Export (*.json)"
                    ]     
                    div [ 
                        clazz "ui inverted item"
                        Dialogs.onSaveFile ExportAsGeoJSON_xyz
                        clientEvent "onclick" jsExportAnnotationsAsGeoJSONDialog
                    ][
                        text "Export xyz (*.json)"
                    ]
                ]
            ]       
        
        let menu (m : AdaptiveModel) =          
            let subMenu name menuItems = 
                div [ clazz "ui dropdown item"] [
                  text name
                  i [clazz "dropdown icon"][] 
                  div [ clazz "menu"] menuItems
                ]           
            let menuItem name action =
                div [ 
                    clazz "ui inverted item"
                    onClick (fun _ -> action)
                ][
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
                                Bookmarkings.Bookmarks.UI.menu |> UI.map ViewerAction.BookmarkMessage
                                subMenu "Change Mode"
                                        [
                                          menuItem "PRo3D Core" (ChangeDashboardMode DashboardModes.core)
                                          menuItem "Surface Comparison" (ChangeDashboardMode DashboardModes.comparison)
                                          menuItem "Render Only" (ChangeDashboardMode DashboardModes.renderOnly)
                                        ]
                 
                                //Extras Menu
                                div [ clazz "ui dropdown item"] [
                                    text "Extras"
                                    i [clazz "dropdown icon"][] 
                                    div [ clazz "menu"] [
                                        //fixes all broken surface import paths
                                        fixAllBrokenPaths

                                        let jsOpenOldAnnotationsFileDialogue = "top.aardvark.dialog.showOpenDialog({title:'Import legacy annotations from PRo3D 1.0' , filters: [{ name: 'Annotations (*.xml)', extensions: ['xml']},], properties: ['openFile']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

                                        div [ clazz "ui item";
                                            Dialogs.onChooseFiles ImportPRo3Dv1Annotations;
                                            clientEvent "onclick" jsOpenOldAnnotationsFileDialogue ][
                                            text "Import v1 Annotations (*.xml)"
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
                    return Drawing.UI.viewAnnotationToolsHorizontal m.drawing |> UI.map DrawingMessage
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
                | _ -> 
                  return div[][]
            }
            
        let style' = "color: white; font-family:Consolas;"

        let scenepath (m:AdaptiveModel) = 
            Incremental.div (AttributeMap.Empty) (
                alist {
                    let! scenePath = m.scene.scenePath
                    let icon = 
                        match scenePath with
                        | Some p -> 
                            i [clazz "large folder icon" ; onClick (fun _ -> OpenSceneFileLocation p) ][] 
                            |> UI.wrapToolTip DataPosition.Bottom "open folder"
                        | None -> div[][]  
                          
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
            match i with 
            | Interactions.PickExploreCenter     -> "CTRL+click to place arcball center"
            | Interactions.PlaceCoordinateSystem -> "CTRL+click to place coordinate cross"
            | Interactions.DrawAnnotation        -> "CTRL+click to pick point on surface"
            | Interactions.PickAnnotation        -> "CTRL+click on annotation to select"
            | Interactions.PickSurface           -> "CTRL+click on surface to select"
            | Interactions.PlaceRover            -> "CTRL+click to (1) place rover and (2) pick lookat"
            | Interactions.TrafoControls         -> "not implemented"
            | Interactions.PlaceSurface          -> "not implemented"
            //| Interactions.PickLinking           -> "CTRL+click to place point on surface"
            | _ -> ""
        
        let topMenuItems (m:AdaptiveModel) = [ 
            div [style "font-weight: bold;margin-left: 1px; margin-right:1px"] 
                [Incremental.text (m.dashboardMode |> AVal.map (fun x -> sprintf "Mode: %s" x))]
            Navigation.UI.viewNavigationModes m.navigation  |> UI.map NavigationMessage 
              
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large wizard icon"][] ]
                Html.Layout.boxH [ CustomGui.dropDown Interactions.hideSet m.interaction SetInteraction ]
                Incremental.div  AttributeMap.empty (AList.ofAValSingle (dynamicTopMenu m))
                Html.Layout.boxH [ 
                    div[style "font-style:italic; width:100%; text-align:right"] [
                        Incremental.text (m.interaction |> AVal.map interactionText)
                    ]]
            ]
              
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large Globe icon"][] ]
                Html.Layout.boxH [ Html.SemUi.dropDown m.scene.referenceSystem.planet ReferenceSystemAction.SetPlanet ] |> UI.map ReferenceSystemMessage
            ] 
            Html.Layout.horizontal [
                scenepath m
            ]        
        ]        
        
        let getTopMenu (m:AdaptiveModel) =
            div[clazz "ui menu"; style "padding:0; margin:0; border:0"] [
                yield (menu m)
                for t in (topMenuItems m) do
                    yield div [clazz "item topmenu"] [t]
            ]
        
    module Annotations =
      
        let viewAnnotationProperties (model : AdaptiveModel) =
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> AnnotationProperties.view ann
                  | _ -> div[style "font-style:italic"][ text "no annotation selected" ])
            
            model.drawing.annotations |> GroupsApp.viewSelected view AnnotationMessage
                
        let viewAnnotationResults (model : AdaptiveModel) =
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> AnnotationProperties.viewResults ann model.scene.referenceSystem.up.value
                  | _ -> div[style "font-style:italic"][ text "no annotation selected" ])
            
            model.drawing.annotations |> GroupsApp.viewSelected view AnnotationMessage
                       
        let viewDipAndStrike (model : AdaptiveModel) = 
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> DipAndStrike.viewUI ann
                  | _ -> div[style "font-style:italic"][ text "no annotation selected" ])
        
            model.drawing.annotations |> GroupsApp.viewSelected view DnSProperties    
            
        let viewDnSColorLegendUI (model : AdaptiveModel) = 
            model.drawing.dnsColorLegend 
            |> FalseColorLegendApp.viewDnSLegendProperties DnSColorLegendMessage 
            |> AVal.constant
          
        let annotationLeafButtonns' (model : AdaptiveModel) = 
            let ts = model.drawing.annotations.activeChild
            let sel = model.drawing.annotations.singleSelectLeaf
            adaptive {  
                let! ts = ts
                let! sel = sel
                match sel with
                | Some _ -> return (GroupsApp.viewLeafButtons ts |> UI.map AnnotationGroupsMessageViewer)
                | None -> return div[style "font-style:italic"][ text "no annotation group selected" ]
            }      
            
        let annotationLeafButtonns (model : AdaptiveModel) =           
            AVal.map2(fun ts sel -> 
                match sel with
                | Some _ -> (GroupsApp.viewLeafButtons ts |> UI.map AnnotationGroupsMessageViewer)
                | None -> div[style "font-style:italic"][ text "no annotation group selected" ]
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
            
            div [][
                GuiEx.accordion "Annotations" "Write" true [
                    GroupsApp.viewSelectionButtons |> UI.map AnnotationGroupsMessageViewer
                    Drawing.UI.viewAnnotationGroups m.drawing |> UI.map ViewerAction.DrawingMessage
                   // DrawingApp.UI.viewAnnotationToolsHorizontal m.drawing |> UI.map DrawingMessage // CHECK-merge viewAnnotationGroups
                ]
                
                GuiEx.accordion "Actions" "Asterisk" true [
                  Incremental.div AttributeMap.empty (AList.ofAValSingle (buttons))
                ]                    
               
                GuiEx.accordion "Dip&Strike ColorLegend" "paint brush" false [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(viewDnSColorLegendUI m))] 
                ]    

    module Config =
        let config (model : AdaptiveModel) = 
            ConfigProperties.view model.scene.config 
              |> UI.map ConfigPropertiesMessage 
              |> AVal.constant
              
        let configUI (m : AdaptiveModel) =
          div[][
              GuiEx.accordion "ViewerConfig" "Settings" true [
                      Incremental.div AttributeMap.empty (AList.ofAValSingle (config m))
              ]
              GuiEx.accordion "Coordinate System" "Map Signs" false [
                  ReferenceSystemApp.UI.view m.scene.referenceSystem |> UI.map ReferenceSystemMessage
              ]
              GuiEx.accordion "Camera" "Camera Retro" false [
                  CameraProperties.view m.scene.referenceSystem m.navigation.camera
              ]
          ] 
          
    module ViewPlanner = 
        let viewPlanProperties (model : AdaptiveModel) =
              //model.scene.viewPlans |> ViewPlan.UI.viewRoverProperties ViewPlanMessage 
              model.scene.viewPlans |> ViewPlanApp.UI.viewRoverProperties ViewPlanMessage model.footPrint.isVisible
        
        let viewPlannerUI (m : AdaptiveModel) =             
          div [][
              GuiEx.accordion "ViewPlans" "Write" true [
                  ViewPlanApp.UI.viewViewPlans m.scene.viewPlans |> UI.map ViewPlanMessage
              ]
              GuiEx.accordion "Properties" "Content" true [
                  Incremental.div AttributeMap.empty (viewPlanProperties m |> AList.ofAValSingle)
              ]
          ]                

    module Bookmarks =
        let bookmarkGroupProperties (model : AdaptiveModel) =                                       
            GroupsApp.viewUI model.scene.bookmarks 
              |> UI.map BookmarkUIMessage 
              |> AVal.constant
                
        let viewBookmarkProperties (model : AdaptiveModel) = 
          let view = (fun leaf ->
              match leaf with
                | AdaptiveBookmarks bm -> Bookmarks.UI.view bm
                | _ -> div[style "font-style:italic"][ text "no bookmark selected" ])
    
          model.scene.bookmarks |> GroupsApp.viewSelected view BookmarkUIMessage
        
        let bookmarksLeafButtonns (model : AdaptiveModel) = 
          let ts = model.scene.bookmarks.activeChild
          let sel = model.scene.bookmarks.singleSelectLeaf
          adaptive {  
              let! ts = ts
              let! sel = sel
              match sel with
                  | Some _ -> return (GroupsApp.viewLeafButtons ts |> UI.map BookmarkUIMessage)
                  | None -> return div[style "font-style:italic"][ text "no bookmark selected" ]            
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
          div [][
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
    

    module Pages =
        let pageRouting viewerDependencies bodyAttributes (m : AdaptiveModel) viewInstrumentView viewRenderView (runtime : IRuntime) request =
            
            match Map.tryFind "page" request.queryParams with
            | Some "instrumentview" ->

                
                let id = System.Guid.NewGuid().ToString()

                let instrumentViewAttributes =
                    amap {
                        let! hor, vert = ViewPlanApp.getInstrumentResolution m.scene.viewPlans
                        let height = "height:" + (vert/uint32(2)).ToString() + ";" ///uint32(2)
                        let width = "width:" + (hor/uint32(2)).ToString() + ";" ///uint32(2)
                        yield style ("background: #1B1C1E;" + height + width)
                        yield Events.onClick (fun _ -> SwitchViewerMode ViewerMode.Instrument)
                    } |> AttributeMap.ofAMap

                require (viewerDependencies) (
                    body [ style "background: #1B1C1E; width:100%; height:100%; overflow-y:auto; overflow-x:auto;"] [
                      Incremental.div instrumentViewAttributes (
                        alist {
                            yield viewInstrumentView runtime id m 
                            yield textOverlaysInstrumentView m.scene.viewPlans
                        } )
                    ]
                )
            | Some "render" -> 
                require (viewerDependencies) (

                    let id = System.Guid.NewGuid().ToString()

                    let onResize (cb : V2i -> 'msg) =
                        onEvent "onresize" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

                    let onFocus (cb : V2i -> 'msg) =
                        onEvent "onfocus" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

                    let renderViewAttributes = [ 
                        style "background: #1B1C1E; height:100%; width:100%"
                        Events.onClick (fun _ -> SwitchViewerMode ViewerMode.Standard)            
                        onResize (fun s -> OnResize(s, id))     
                        onFocus (fun s -> OnResize(s, id))     
                        onMouseDown (fun button pos -> StartDragging (pos, button))
                     //   onMouseMove (fun delta -> Dragging delta)
                        onMouseUp (fun button pos -> EndDragging (pos, button))
                    ]

                    body renderViewAttributes [ //[ style "background: #1B1C1E; height:100%; width:100%"] [
                        //div [style "background:#000;"] [
                        Incremental.div (AttributeMap.ofList[style "background:#000;"]) (
                            alist {
                                yield viewRenderView runtime id m
                                yield textOverlays m.scene.referenceSystem m.navigation.camera.view
                                yield textOverlaysUserFeedback m.scene
                                yield dnsColorLegend m
                                yield scalarsColorLegend m
                                yield selectionRectangle m
                                yield PRo3D.Linking.LinkingApp.sceneOverlay m.linkingModel |> UI.map LinkingActions
                            }
                        )
                    ]                
                )
            | Some "surfaces" -> 
                require (viewerDependencies) (
                    body bodyAttributes
                        [SurfaceApp.surfaceUI m.scene.surfacesModel |> UI.map SurfaceActions] 
                )
            | Some "annotations" -> 
                require (viewerDependencies) (body bodyAttributes [Annotations.annotationUI m])
            | Some "validation" -> 
                require (viewerDependencies) (body bodyAttributes [HeightValidatorApp.viewUI m.heighValidation |> UI.map HeightValidation])
            | Some "bookmarks" -> 
                require (viewerDependencies) (body bodyAttributes [Bookmarks.bookmarkUI m])
            | Some "comparison" -> 
                require (viewerDependencies) (body bodyAttributes [PRo3D.ComparisonApp.view m.comparisonApp m.scene.surfacesModel
                                                                    |> UI.map ComparisonMessage])
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

                require (viewerDependencies) (body bodyAttributes (blurg()))
            | Some "config" -> 
                require (viewerDependencies) (body bodyAttributes [Config.configUI m])
            | Some "viewplanner" -> 
                require (viewerDependencies) (body bodyAttributes [ViewPlanner.viewPlannerUI m])
            | Some "minerva" -> 
               //let pos = m.scene.navigation.camera.view |> AVal.map(fun x -> x.Location)
                let minervaItems = 
                    PRo3D.Minerva.MinervaApp.viewFeaturesGui m.minervaModel |> List.map (UI.map MinervaActions)

                let linkingItems =
                    [
                        Html.SemUi.accordion "Linked Products" "Image" false [
                            PRo3D.Linking.LinkingApp.viewSideBar m.linkingModel |> UI.map LinkingActions
                        ]
                    ]

                require (viewerDependencies @ Html.semui) (
                    body bodyAttributes (minervaItems @ linkingItems)
                )
            | Some "linking" ->
                require (viewerDependencies) (
                    body bodyAttributes [
                        PRo3D.Linking.LinkingApp.viewHorizontalBar m.minervaModel.session.selection.highlightedFrustra m.linkingModel |> UI.map LinkingActions
                    ]
                )
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
            | None -> 
                require (viewerDependencies) (
                    body [][                    
                        TopMenu.getTopMenu m
                        div[clazz "dockingMainDings"] [
                            m.scene.dockConfig
                            |> docking [                                           
                                style "width:100%; height:100%; background:#F00"
                                onLayoutChanged UpdateDockConfig ]
                        ]
                    ]
                )
            | _ -> body[][]
