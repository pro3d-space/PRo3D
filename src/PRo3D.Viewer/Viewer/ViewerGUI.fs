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
open Aardvark.Base.Rendering
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
open PRo3D.Groups
open PRo3D.ReferenceSystem
open PRo3D.Surfaces
open PRo3D.Bookmarkings
open PRo3D.Viewplanner
open PRo3D.Correlations

module Gui =            
    
    let pitchAndBearing (r:AdaptiveReferenceSystem) (view:aval<CameraView>) =
        adaptive {
          let! up    = r.up.value
          let! north = r.northO//r.north.value   
          let! v     = view
        
          return (DipAndStrike.pitch up v.Forward, DipAndStrike.bearing up north v.Forward)
        }
    
    let dnsColorLegend (m:MModel) =

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
                            
    let scalarsColorLegend (m:MModel) =
          
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
    
    let selectionRectangle (m:MModel) =
        
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

        let selectionRectangle = Incremental.Svg.rect attr AList.empty

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
                    CooTransformation.getLatLonAlt(a.Location) b ) cv m.planet
            
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
    
    let textOverlaysInstrumentView (m : MViewPlanModel)  = 
        let instrument =
            adaptive {
                let! vp = m.selectedViewPlan
                let! inst = 
                    match vp with
                    | Some v -> AVal.bindOption v.selectedInstrument "No instrument selected" (fun a -> a.id)
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
    
    let textOverlaysUserFeedback (m : MScene)  = 
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

    module TopMenu =
   
        let private importSurface =
            [
                text "Import"
                i [clazz "dropdown icon"][] 
                div [ clazz "menu"] [             
                    div [ clazz "ui inverted item";
                        Dialogs.onChooseFiles  ImportDiscoveredSurfacesThreads;
                        clientEvent "onclick" ("parent.aardvark.processEvent('__ID__', 'onchoose', parent.aardvark.dialog.showOpenDialog({properties: ['openDirectory', 'multiSelections']}));") ][
                        text "OPC"
                    ]
                    div [ clazz "ui inverted item"; 
                        Dialogs.onChooseFiles ImportObject;
                        clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({properties: ['openFile']}));") ][
                        text "OBJ"
                    ]
                ]
            ]
        
        let private scene (m:MModel) =
            let saveSceneDialog (m:MModel) = 
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
                                clientEvent "onclick" (
                                    "top.aardvark.processEvent('__ID__', 'onsave', top.aardvark.dialog.showSaveDialog({filters: [{ name: 'Scene', extensions: ['pro3d']},]}));"
                                )
                            ] [ text "Save" ]
                }
            [
                text "Scene" 
                i [clazz "dropdown icon"][]
                div [ clazz "menu"] [
                    //save scene
                    Incremental.div AttributeMap.empty (AList.ofAValSingle (saveSceneDialog m))

                    //save scene as
                    div [ 
                        clazz "ui inverted item"; Dialogs.onSaveFile SaveAs;
                        clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onsave', top.aardvark.dialog.showSaveDialog({filters: [{ name: 'Scene', extensions: ['pro3d']},]}));") ][
                        text "Save as..."
                    ]

                    //load scene
                    div [ 
                        clazz "ui inverted item"
                        Dialogs.onChooseFiles(fun x -> 
                            match (x |> List.tryHead) with 
                            | Some y -> LoadScene y 
                            | None -> NoAction "no scene selected")

                        clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({ filters: [{ name: 'Scene', extensions: ['pro3d','scn']},], properties: ['openFile']}));")][                                       
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
                                    //let sortedList = recentScenes |> List.sortBy( fun sh -> sh.writeDate.DayOfYear ) |> List.sortBy( fun sh -> sh.writeDate.Year )
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
        
        let surfaceUiThing (m : MModel)=
            let blarg =
                amap {
                    let! selected = 
                        m.scene.surfacesModel.surfaces.singleSelectLeaf
                    
                    match selected with
                    | Some s ->
                        yield clazz "ui item"
                        yield Dialogs.onChooseDirectory s SurfaceApp.Action.ChangeImportDirectory
                        yield clientEvent "onclick" (
                          "top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({properties: ['openDirectory','multiSelections']}));"
                        )
                    | None -> ()
                }
            
            let blurg = 
                alist {
                    let! selected = m.scene.surfacesModel.surfaces.singleSelectLeaf
                    match selected with
                    | Some _ -> yield text "fix broken path"
                    | None -> ()
                }
            
            Incremental.div(AttributeMap.ofAMap blarg) blurg |> UI.map SurfaceActions
        
        let fixAllBrokenPaths =
            let ui = 
                alist {
                    yield
                        div [ 
                            clazz "ui item";
                            Dialogs.onChooseFiles  SurfaceApp.Action.ChangeImportDirectories;
                            clientEvent "onclick" ("parent.aardvark.processEvent('__ID__', 'onchoose', parent.aardvark.dialog.showOpenDialog({properties: ['openDirectory', 'multiSelections']}));") ][
                            text "fix all broken paths"
                        ]
                }
        
            Incremental.div(AttributeMap.Empty) ui |> UI.map SurfaceActions                
        
        let menu (m : MModel) = 
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
                                DrawingApp.UI.annotationMenu |> UI.map DrawingMessage;                                                           
                            
                                //Extras Menu
                                div [ clazz "ui dropdown item"] [
                                    text "Extras"
                                    i [clazz "dropdown icon"][] 
                                    div [ clazz "menu"] [
                                        div [ clazz "ui item";
                                            Dialogs.onChooseFiles ImportAnnotationGroups;
                                            clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({filters: [{ name: 'xml', extensions: ['xml']},], properties: ['openFile']}));") ][
                                            text "Import Annotations Groups"
                                        ]
                                        div [ clazz "ui item";
                                            Dialogs.onChooseFiles ImportSurfaceTrafo;
                                            clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({filters: [{ name: 'xml', extensions: ['xml']},],properties: ['openFile']}));") ] [
                                            text "Import Surface Trafos"
                                        ]
                                        div [ clazz "ui item";
                                            Dialogs.onChooseFiles ImportRoverPlacement;
                                            clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({properties: ['openFile']}));") ] [
                                            text "Rover Placement"
                                        ]
                            
                                        //fixes all broken surface import paths
                                        fixAllBrokenPaths
                            
                                        PRo3D.Correlations.CorrelationPanelsApp.viewExportLogButton m.scene.scenePath 
                                        |> UI.map CorrelationPanelMessage

                                        //fixes particular broken surface import paths (doesn't work atm)
                                        //surfaceUiThing m
                                    ]
                                ]
                            ] 
                        ]
                    )
                ]
            ]
        
        let dynamicTop (m:MModel) =
            adaptive {
                let! interaction = m.interaction
                match interaction with
                | Interactions.DrawAnnotation -> 
                    return DrawingApp.UI.viewAnnotationToolsHorizontal m.drawing |> UI.map DrawingMessage
                | Interactions.PlaceRover ->
                    return ViewPlanApp.UI.viewSelectRover m.scene.viewPlans.roverModel |> UI.map RoverMessage
                | Interactions.PlaceCoordinateSystem -> 
                    return Html.Layout.horizontal [
                        Html.Layout.boxH [ Html.SemUi.dropDown' m.scene.referenceSystem.scaleChart m.scene.referenceSystem.selectedScale ReferenceSystemApp.Action.SetScale id ]
                        //Html.Layout.boxH [ i [clazz "unhide icon"][] ]
                        Html.Layout.boxH [ GuiEx.iconToggle m.scene.referenceSystem.isVisible "unhide icon" "hide icon" ReferenceSystemApp.Action.ToggleVisible  ]                        
                        ] |> UI.map ReferenceSystemMessage
                | _ -> 
                  return div[][]
            }
            
        let style' = "color: white; font-family:Consolas;"
        let scenepath (m:MModel) = 
            Incremental.div (AttributeMap.Empty) (
                alist {
                    let! scenePath = m.scene.scenePath 
                    let icon = 
                        match scenePath with
                        | Some p -> 
                            i [clazz "large folder icon" ; onClick (fun _ -> OpenSceneFileLocation p) ][] 
                            |> UI.wrapToolTip "open folder" TTAlignment.Bottom
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
        
        let topMenuItems (m:MModel) = [ 
            
            Navigation.UI.viewNavigationModes m.navigation  |> UI.map NavigationMessage 
              
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large wizard icon"][] ]
                Html.Layout.boxH [ Html.SemUi.dropDown m.interaction SetInteraction ]                                         
                Incremental.div  AttributeMap.empty (AList.ofAValSingle (dynamicTop m))
                Html.Layout.boxH [ 
                    div[style "font-style:italic; width:100%; text-align:right"] [
                        Incremental.text (m.interaction |> AVal.map interactionText)
                    ]]
            ]
              
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large Globe icon"][] ]
                Html.Layout.boxH [ Html.SemUi.dropDown m.scene.referenceSystem.planet ReferenceSystemApp.Action.SetPlanet ] |> UI.map ReferenceSystemMessage
            ] 
            Html.Layout.horizontal [
                scenepath m
            ]        
        ]        
        
        let getTopMenu (m:MModel) =
            div[clazz "ui menu"; style "padding:0; margin:0; border:0"] [
                yield (menu m)
                for t in (topMenuItems m) do
                    yield div [clazz "item topmenu"] [t]
            ]
        
    module Annotations =
      
        let viewAnnotationProperties (model :MModel) =
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> AnnotationProperties.view ann
                  | _ -> div[style "font-style:italic"][ text "no annotation selected" ])
            
            model.drawing.annotations |> GroupsApp.viewSelected view AnnotationMessage
                
        let viewAnnotationResults (model :MModel) =
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> AnnotationProperties.viewResults ann model.scene.referenceSystem.up.value
                  | _ -> div[style "font-style:italic"][ text "no annotation selected" ])
            
            model.drawing.annotations |> GroupsApp.viewSelected view AnnotationMessage
                       
        let viewDipAndStrike (model:MModel) = 
            let view = (fun leaf ->
                match leaf with
                  | AdaptiveAnnotations ann -> DipAndStrike.viewUI ann
                  | _ -> div[style "font-style:italic"][ text "no annotation selected" ])
        
            model.drawing.annotations |> GroupsApp.viewSelected view DnSProperties    
            
        let viewDnSColorLegendUI (model:MModel) = 
            model.drawing.dnsColorLegend 
            |> FalseColorLegendApp.viewDnSLegendProperties DnSColorLegendMessage 
            |> AVal.constant
          
        let annotationLeafButtonns' (model:MModel) = 
            let ts = model.drawing.annotations.activeChild
            let sel = model.drawing.annotations.singleSelectLeaf
            adaptive {  
                let! ts = ts
                let! sel = sel
                match sel with
                | Some _ -> return (GroupsApp.viewLeafButtons ts |> UI.map AnnotationGroupsMessageViewer)
                | None -> return div[style "font-style:italic"][ text "no annotation group selected" ]
            }      
            
        let annotationLeafButtonns (model:MModel) =           
            AVal.map2(fun ts sel -> 
                match sel with
                | Some _ -> (GroupsApp.viewLeafButtons ts |> UI.map AnnotationGroupsMessageViewer)
                | None -> div[style "font-style:italic"][ text "no annotation group selected" ]
            ) model.drawing.annotations.activeChild model.drawing.annotations.singleSelectLeaf
            
        let annotationGroupProperties (model:MModel) =                            
            GroupsApp.viewUI model.drawing.annotations 
            |> UI.map AnnotationGroupsMessageViewer 
            |> AVal.constant
        
        let annotationGroupButtons (model:MModel) = 
            model.drawing.annotations.activeGroup 
            |> AVal.map (fun x -> GroupsApp.viewGroupButtons x |> UI.map AnnotationGroupsMessageViewer)            
            
        let annotationUI (m : MModel) = 
            let prop = 
                m.drawing.annotations.lastSelectedItem
                |> AVal.bind (fun x -> 
                    match x with 
                    | SelectedItem.Group -> annotationGroupProperties m
                    | _ -> viewAnnotationProperties m
                )
            let results = 
                m.drawing.annotations.lastSelectedItem
                |> AVal.bind (fun x -> 
                    match x with 
                    | SelectedItem.Group -> annotationGroupProperties m
                    | _ -> viewAnnotationResults m 
                )
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
                    DrawingApp.UI.viewAnnotationToolsHorizontal m.drawing |> UI.map DrawingMessage // CHECK-merge viewAnnotationGroups
                ]
                GuiEx.accordion "Properties" "Content" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle prop)                                        
                ]
               
                GuiEx.accordion "Actions" "Asterisk" true [
                  Incremental.div AttributeMap.empty (AList.ofAValSingle (buttons))
                ]     
                
                GuiEx.accordion "Measurements" "Content" true [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle results)                                        
                ]
            
                GuiEx.accordion "Dip&Strike" "Calculator" false [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(viewDipAndStrike m))] 
               
                GuiEx.accordion "Dip&Strike ColorLegend" "paint brush" false [
                    Incremental.div AttributeMap.empty (AList.ofAValSingle(viewDnSColorLegendUI m))] 
                ]    

    module Config =
      let config (model:MModel) = 
            ConfigProperties.view model.scene.config 
              |> UI.map ConfigPropertiesMessage 
              |> AVal.constant
            
      let configUI (m:MModel) =
          div[][
              GuiEx.accordion "ViewerConfig" "Settings" true [
                      Incremental.div AttributeMap.empty (AList.ofAValSingle (config m))
              ]
              GuiEx.accordion "Coordinate System" "Map Signs" false [
                  ReferenceSystemApp.UI.view m.scene.referenceSystem m.navigation.camera |> UI.map ReferenceSystemMessage
              ]
              GuiEx.accordion "Camera" "Camera Retro" false [
                  CameraProperties.view m.navigation.camera
              ]
          ] 
          
    module ViewPlanner = 
      let viewPlanProperties (model:MModel) =
              //model.scene.viewPlans |> ViewPlan.UI.viewRoverProperties ViewPlanMessage 
              model.scene.viewPlans |> ViewPlanApp.UI.viewRoverProperties ViewPlanMessage model.footPrint.isVisible
    
      let viewPlannerUI (m:MModel) =             
          div [][
              GuiEx.accordion "ViewPlans" "Write" true [
                  ViewPlanApp.UI.viewViewPlans m.scene.viewPlans |> UI.map ViewPlanMessage
              ]
              GuiEx.accordion "Properties" "Content" true [
                  Incremental.div AttributeMap.empty (viewPlanProperties m |> AList.ofAValSingle)
              ]
          ]                

    module Bookmarks =
      let bookmarkGroupProperties (model:MModel) =                                       
            GroupsApp.viewUI model.scene.bookmarks 
              |> UI.map BookmarkUIMessage 
              |> AVal.constant
              
      let viewBookmarkProperties (model:MModel) = 
          let view = (fun leaf ->
              match leaf with
                | MBookmarks bm -> Bookmarks.UI.view bm
                | _ -> div[style "font-style:italic"][ text "no bookmark selected" ])
    
          model.scene.bookmarks |> GroupsApp.viewSelected view BookmarkUIMessage
    
      let bookmarksLeafButtonns (model:MModel) = 
          let ts = model.scene.bookmarks.activeChild
          let sel = model.scene.bookmarks.singleSelectLeaf
          adaptive {  
              let! ts = ts
              let! sel = sel
              match sel with
                  | Some _ -> return (GroupsApp.viewLeafButtons ts |> UI.map BookmarkUIMessage)
                  | None -> return div[style "font-style:italic"][ text "no bookmark selected" ]            
          } 
      
      let bookmarksGroupButtons (model:MModel) = 
          let ts = model.scene.bookmarks.activeGroup
          adaptive {  
              let! ts = ts
              return (GroupsApp.viewGroupButtons ts |> UI.map BookmarkUIMessage)
          } 
      
      let bookmarkUI (m:MModel) = 
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
    
