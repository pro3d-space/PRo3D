namespace PRo3D

open System
open Aardvark.Rendering
open Aardvark.UI
open PRo3D.Comparison
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Trafos
open Aardvark.UI.Primitives
open PRo3D.Core
open PRo3D.Base
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Base
open Chiron
open PRo3D.Base.Annotation
open Adaptify.FSharp.Core
open PRo3D.Comparison
open SurfaceMeasurements
open AreaSelection
open ComparisonUtils



module CustomGui =
    let dynamicDropdown<'msg when 'msg : equality> (items    : list<aval<string>>)
                                                   (selected : aval<string>) 
                                                   (change   : string -> 'msg) =
        let attributes (name : aval<string>) =
            AttributeMap.ofListCond [
                Incremental.always "value" name
                onlyWhen (AVal.map2 (=) name selected) (attribute "selected" "selected")
            ]
     
        let callback = onChange (fun str -> 
                                    str |> change)

        select [callback; style "color:black"] [
                for name in items do
                    let att = attributes name
                    yield Incremental.option att (AList.ofList [Incremental.text name])
        ] 

    let surfacesDropdown (surfaces : AdaptiveSurfaceModel) (change : string -> 'msg) (noSelection : string)=
        let surfaceToName (s : aval<AdaptiveSurface>) =
            s |> AVal.bind (fun s -> s.name)

        let surfaces = surfaces.surfaces.flat |> toAvalSurfaces
        let surfaceNames = surfaces |> AMap.map (fun g s -> s |> surfaceToName)                                                         
                                    |> AMap.toAVal
        let items = 
          surfaceNames |> AVal.map (fun n -> n.ToValueList ())
            |> AVal.map (fun x -> List.append [(noSelection |> AVal.constant)] x)

        let dropdown = 
            items |> AVal.map (fun items -> dynamicDropdown items (noSelection |> AVal.constant) change)

        Incremental.div (AttributeMap.ofList []) (AList.ofAValSingle dropdown)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ComparisonApp =

    let init : ComparisonApp = {   
        threads              = ThreadPool.empty
        state                = Idle
        showMeasurementsSg   = true
        originMode           = OriginMode.ModelOrigin
        surface1             = None
        surface2             = None
        surfaceMeasurements  = 
          {
              measurements1        = None
              measurements2        = None
              comparedMeasurements = None
          }
        annotationMeasurements = []     
        surfaceGeometryType = DistanceMode.SurfaceNormal
        initialAreaSize = Comparison.Init.areaSize
        pointSizeFactor = Comparison.Init.pointSizeFactor
        nrOfCreatedAreas = 0
        selectedArea = None
        isEditingArea = false
        areas = HashMap.empty
    }

    let threads m =
        m.threads

    let setSurfacesVisibleAndActive (surfaceId1   : option<string>) 
                                    (surfaceId2   : option<string>)
                                    (surfaceModel : SurfaceModel) =
        
        match surfaceId1, surfaceId2 with
        | Some id1, Some id2 ->
            let id1 = findSurfaceByName surfaceModel id1
            let id2 = findSurfaceByName surfaceModel id2
            match id1, id2 with
            | Some id1, Some id2 ->
                let s1 = surfaceModel.surfaces.flat |> HashMap.find id1
                                                    |> Leaf.toSurface
                let s2 = surfaceModel.surfaces.flat |> HashMap.find id2
                                                    |> Leaf.toSurface
                let s1 = {s1 with isVisible = true
                                  isActive  = true}
                let s2 = {s2 with isVisible = true
                                  isActive  = true}
                surfaceModel
                  |> SurfaceModel.updateSingleSurface s1
                  |> SurfaceModel.updateSingleSurface s2
            | _,_ -> surfaceModel
        | _,_ -> surfaceModel

    let updateAreaStatistics (m            : ComparisonApp) 
                             (surfaceModel : SurfaceModel) 
                             (refSystem    : ReferenceSystem) = 
        Log.line "[Comparison] Calculating area statistics..."
        let surfaceModel = setSurfacesVisibleAndActive m.surface1 m.surface2 surfaceModel
        let m = 
            match m.surface1, m.surface2 with
            | Some s1, Some s2 ->
                let areas =   
                  m.areas
                    |> HashMap.map (fun g x -> updateAreaStatistic surfaceModel refSystem
                                                                   m.pointSizeFactor.value
                                                                   m.surfaceGeometryType
                                                                   x s1 s2)
                    |> HashMap.filter (fun g x -> x.IsSome)
                    |> HashMap.map (fun g x -> x.Value)
                areas
            | _,_ -> HashMap.empty
        Log.line "[Comparison] Finished calculating area statistics."
        m

    let updateSurfaceMeasurements (m            : ComparisonApp) 
                                  (surfaceModel : SurfaceModel) 
                                  (refSystem    : ReferenceSystem) = 
        Log.line "[Comparison] Calculating coordinate system measurements..."
        let surfaceModel = setSurfacesVisibleAndActive m.surface1 m.surface2 surfaceModel
        let measurements1 = Option.bind (fun s1 -> updateSurfaceMeasurement 
                                                        surfaceModel refSystem m.originMode s1) 
                                        m.surface1 
        let measurements2 = Option.bind (fun s2 -> updateSurfaceMeasurement 
                                                        surfaceModel refSystem m.originMode s2)
                                        m.surface2
        let surfaceMeasurements = 
            {
                measurements1 = measurements1
                measurements2 = measurements2
                comparedMeasurements =
                    Option.map2 (fun a b -> SurfaceMeasurements.compare a b)
                                measurements1 measurements2
        
            }
        Log.line "[Comparison] Finished calculating coordinate system measurements..."
        surfaceMeasurements
        
    let updateAnnotationMeasurements (m            : ComparisonApp) 
                                     (surfaceModel : SurfaceModel) 
                                     (annotations  : HashMap<Guid, Annotation.Annotation>) 
                                     (bookmarks    : HashMap<Guid, Bookmark>)
                                     (refSystem    : ReferenceSystem) =
        Log.line "[Comparison] Calculating annotation measurements..."
        let annotationMeasurements =
            match m.surface1, m.surface2 with
            | Some s1, Some s2 ->
                AnnotationComparison.compareAnnotationMeasurements 
                                        s1 s2  annotations bookmarks
      
            | _,_ -> []
        Log.line "[Comparison] Finished calculating annotation measurements."
        annotationMeasurements
       

    let updateMeasurements (m            : ComparisonApp) 
                           (surfaceModel : SurfaceModel) 
                           (annotations  : HashMap<Guid, Annotation.Annotation>) 
                           (bookmarks    : HashMap<Guid, Bookmark>)
                           (refSystem    : ReferenceSystem) =
        Log.line "[Comparison] Calculating surface measurements..."
        let surfaceModel = setSurfacesVisibleAndActive m.surface1 m.surface2 surfaceModel
        let areas = updateAreaStatistics m surfaceModel refSystem

        let annotationMeasurements =
            updateAnnotationMeasurements m surfaceModel annotations bookmarks refSystem

        let surfaceMeasurements =
            updateSurfaceMeasurements m surfaceModel refSystem

        Log.line "[Comparison] Finished calculating surface measurements."
        {m with surfaceMeasurements    = surfaceMeasurements 
                annotationMeasurements = annotationMeasurements
                areas                  = areas
        }

    let updateArea (m            : ComparisonApp) 
                   (guid         : System.Guid)
                   (msg          : AreaSelectionAction) =
        let area = m.areas
                      |> HashMap.tryFind guid
        let m = 
            match area with
            | Some area -> 
                let area = AreaSelection.update area msg
                let areas =
                    HashMap.add guid area m.areas
                {m with areas = areas}
            | None -> m
        m //TODO rno

    let addThread (m : ComparisonApp) (actions : List<ComparisonAction>) = 
        let id = (System.Guid.NewGuid ()).ToString ()
        let proclst = 
          proclist {
              for a in actions do
                yield a
              yield (RemoveThread id)
          }
        {m with threads = ThreadPool.add id (proclst) m.threads}

    let removeThread (m : ComparisonApp) id =
        {m with threads = ThreadPool.remove id m.threads}

    let update (m            : ComparisonApp) 
               (surfaceModel : SurfaceModel) 
               (refSystem    : ReferenceSystem)
               (annotations  : HashMap<Guid, Annotation.Annotation>) 
               (bookmarks    : HashMap<Guid, Bookmark>)
               (msg          : ComparisonAction) =
        match msg with
        | SetState state -> 
            {m with state = state}, surfaceModel
        | RemoveThread id ->
            removeThread m id, surfaceModel
        | UpdateAllMeasurements -> 
            let m = {m with state = CalculatingStatistics}
            let m = addThread m [ASyncUpdateAnnotationMeasurements
                                 ASyncUpdateAreaMeasurements
                                 ASyncUpdateCoordinateSystemMeasurements
                                 ]
            m, surfaceModel
            //let m = updateMeasurements m surfaceModel annotations bookmarks refSystem
            //m , surfaceModel
        | UpdateCoordinateSystemMeasurements ->
            let surfaceMeasurements = updateSurfaceMeasurements m surfaceModel refSystem
            {m with surfaceMeasurements = surfaceMeasurements}, surfaceModel
        | UpdateAreaMeasurements ->
            let areas = updateAreaStatistics m surfaceModel refSystem
            {m with areas = areas} , surfaceModel
        | UpdateAnnotationMeasurements ->
            let annotationMeasurements = 
                updateAnnotationMeasurements m surfaceModel annotations bookmarks refSystem
            {m with annotationMeasurements = annotationMeasurements}, surfaceModel
        | ASyncUpdateCoordinateSystemMeasurements ->
            let m = addThread m [UpdateCoordinateSystemMeasurements]
            m, surfaceModel
        | ASyncUpdateAreaMeasurements ->
            let m = addThread m [UpdateAreaMeasurements]
            m, surfaceModel
        | ASyncUpdateAnnotationMeasurements ->
            let m = addThread m [UpdateAnnotationMeasurements]
            m, surfaceModel
        | SelectSurface1 str -> 
            let m = {m with surface1 = noSelectionToNone str}
            //let m =
            //    match m.surface1, m.surface2 with
            //    | Some s1, Some s2 ->
            //        updateMeasurements m surfaceModel annotations bookmarks refSystem
            //    | _,_ -> m
            ComparisonUtils.cache <- HashMap.empty
            m , surfaceModel
        | SelectSurface2 str -> 
            let m = {m with surface2 = noSelectionToNone str}
            //let m =
            //    match m.surface1, m.surface2 with
            //    | Some s1, Some s2 ->
            //        updateMeasurements m surfaceModel annotations bookmarks refSystem
            //    | _,_ -> m
            ComparisonUtils.cache <- HashMap.empty
            m , surfaceModel
        | ExportMeasurements filepath -> 
            m
              |> Json.serialize 
              |> Json.formatWith JsonFormattingOptions.Pretty 
              |> Serialization.writeToFile filepath
            Log.line "[Comparison] Measurements exported to %s" (System.IO.Path.GetFullPath filepath)
            m , surfaceModel
        | ComparisonAction.ToggleVisible ->
            let surfaceId1 = m.surface1 |> Option.bind (fun x -> findSurfaceByName surfaceModel x)
            let surfaceId2 = m.surface2 |> Option.bind (fun x -> findSurfaceByName surfaceModel x)
            let surfaceModel = toggleVisible surfaceId1 surfaceId2 surfaceModel
            m, surfaceModel
        | AddBookmarkReference bookmarkId ->
            m, surfaceModel
        | SetDistanceMode typ ->
            {m with surfaceGeometryType = typ}, surfaceModel
        | UpdateDefaultAreaSize msg ->
           let areaSize = Numeric.update m.initialAreaSize msg
           {m with initialAreaSize = areaSize}, surfaceModel
        | UpdatePointSizeFactor msg ->
           let factor = Numeric.update m.pointSizeFactor msg
           {m with pointSizeFactor = factor}, surfaceModel
        | AddSelectionArea location ->
            let areaName = sprintf "Area%i" (m.nrOfCreatedAreas + 1)
            let area = {AreaSelection.init (System.Guid.NewGuid ()) areaName
                          with location = location
                               radius     = m.initialAreaSize.value}
            let areas =
                HashMap.add area.id area m.areas
            
            {m with areas = areas
                    selectedArea = Some area.id
                    isEditingArea = true
                    nrOfCreatedAreas = m.nrOfCreatedAreas + 1}, surfaceModel
        | UpdateSelectedArea msg ->
            match m.selectedArea with
            | Some guid ->
                updateArea m guid msg, surfaceModel
            | None ->
                Log.line "[ComparisonApp] No area selected."
                m, surfaceModel
        | AreaSelectionMessage (guid, msg) ->
            updateArea m guid msg, surfaceModel
        | SelectArea guid -> 
            {m with selectedArea = guid}, surfaceModel
        | DeselectArea -> 
            {m with selectedArea  = None
                    isEditingArea = false}, surfaceModel
        | RemoveArea id ->
            let areas = m.areas.Remove id
            {m with areas = areas
                    selectedArea = None
            }, surfaceModel
        | StopEditingArea ->
            {m with isEditingArea = false}, surfaceModel

        | SetOriginMode originMode -> 
            let m = {m with originMode = originMode}
            let m = updateMeasurements m surfaceModel annotations bookmarks refSystem
            m, surfaceModel
        | Nop -> m, surfaceModel

    let isSelected (surfaceName : aval<string>) (m : AdaptiveComparisonApp) =
        let showSg = 
            AVal.map3 (fun (s1 : option<string>) s2 surfaceName -> 
                          match s1, s2 with
                          | Some s1, Some s2 ->
                            s1 = surfaceName || s2 = surfaceName
                          | Some s1, None -> s1 = surfaceName
                          | None, Some s2 -> s2 = surfaceName
                          | None, None -> false
                      ) m.surface1 m.surface2 surfaceName
        showSg

    let defaultCoordinateCross size trafo (origin : aval<V3d>) =
        let sg = 
            Sg.coordinateCross size
                |> Sg.trafo trafo
                |> Sg.noEvents
                |> Sg.effect [              
                    Shader.stableTrafo |> toEffect 
                    DefaultSurfaces.vertexColor |> toEffect
                ] 
                |> Sg.noEvents
                |> Sg.andAlso (
                    Sg.sphere 12 (C4b.Blue |> AVal.constant) 
                                 (size |> AVal.map (fun x -> x * 0.001)) 
                        |> Sg.trafo (origin |> AVal.map (fun x -> Trafo3d.Translation x))
                        |> Sg.noEvents
                        |> Sg.effect [              
                              Shader.stableTrafo |> toEffect 
                              DefaultSurfaces.vertexColor |> toEffect
                        ] 
                )
        sg

    //let areaStatisticsSg (m : AdaptiveComparisonApp) =   
    //    let sg = AVal.bind (fun s -> 
    //                        match s with
    //                        | Some s -> 
    //                            let a = AMap.tryFind s m.areas
    //                            a |> AVal.map (fun a -> 
    //                                              match a with
    //                                              | Some a -> AreaSelection.sgPoints a
    //                                              | None -> Sg.empty)
    //                        | None -> (Sg.empty |> AVal.constant)) m.selectedArea
    //    sg |> Sg.dynamic

    //let measurementsSg (surface     : aval<AdaptiveSurface>)
    //                   (size        : aval<float>)
    //                   (trafo       : aval<Trafo3d>) 
    //                   (referenceSystem : AdaptiveReferenceSystem)
    //                   (m           : AdaptiveComparisonApp) =    
    //    let surfaceName = surface |> AVal.bind (fun x -> x.name)
    //    let pivot = surface |> AVal.bind (fun x -> x.transformation.pivot)

    //   // let upDir = referenceSystem.up.value |> AVal.map (fun x -> x.Normalized)
    //  //  let northDir = referenceSystem.northO |> AVal.map (fun x -> x.Normalized)
    //  //  let east   =  AVal.map2 (fun (north : V3d) up -> north.Cross(up).Normalized) northDir upDir

    //    let showSg = isSelected surfaceName m

    //    let sg =
    //        showSg |> AVal.map (fun show -> 
    //                              match show with
    //                              | true -> defaultCoordinateCross size trafo pivot
    //                              | false -> Sg.empty
    //                           )
    //    sg |> Sg.dynamic

    let viewLegend (m : AdaptiveComparisonApp) =
        let legend = 
            AVal.map (fun s -> 
                        match s with
                        | Some s -> 
                            let a = AMap.tryFind s m.areas
                            let legend = 
                                a |> AVal.map (fun a -> 
                                                  match a with
                                                  | Some a -> 
                                                    AreaComparison.createColorLegend a
                                                  | None -> div[][])
                            Incremental.div ([] |> AttributeMap.ofList) (AList.ofAValSingle legend)
                        | None -> div[][]) m.selectedArea
        Incremental.div ([] |> AttributeMap.ofList) (AList.ofAValSingle legend)
        

    let view (m : AdaptiveComparisonApp) 
             (surfaces : AdaptiveSurfaceModel) =
        let measurementGui (name         : option<string>) 
                           (maesurements : option<SurfaceMeasurements>) =
            match name, maesurements with
            | Some name, Some maesurements -> 
                SurfaceMeasurements.view maesurements
            | _,_    -> 
                div [][]
                 

        let measurement1 = 
            (AVal.map2 (fun (s : option<string>) m -> 
                                measurementGui s m.measurements1) m.surface1 m.surfaceMeasurements)
                       |> AList.ofAValSingle


        let header surf = 
            (surf |> AVal.map (fun name -> 
                                      match name with
                                      | Some name -> sprintf "Measurements for %s"  name
                                      | None      -> "No surface selected"))

        let measurement2 = 
            (AVal.map2 (fun (s : option<string>) m -> 
                                measurementGui s m.measurements2
                       ) m.surface2 m.surfaceMeasurements)
                       |> AList.ofAValSingle

        let compared = 
            m.surfaceMeasurements
                |> (AVal.map (fun x -> 
                                  match x.comparedMeasurements with
                                  | Some m -> 
                                      SurfaceMeasurements.view m
                                  | None -> 
                                      div [] []
                             )
                    ) 

        let surfaceMeasurements =
            alist {
                yield div [] [Incremental.text (header m.surface1)]
                yield! measurement1
                yield div [] [Incremental.text (header m.surface2)]
                yield! measurement2
                yield div [] [text "Difference"]
                let! compared = compared
                yield compared
            }
        let surfaceMeasurements =
            Incremental.div (AttributeMap.ofList []) surfaceMeasurements
        //let header = sprintf "Measurements for %s"  name

        let surfaceMeasurements =
             let originModeDropDown =
                Html.table [
                  Html.row "Origin   " [Html.SemUi.dropDown m.originMode SetOriginMode]
                ]
             AVal.map2 (fun (s1 : option<string>) s2 -> 
                            match s1, s2 with
                            | Some s1, Some s2 -> 
                                GuiEx.accordionWithOnClick "Surface Measurements"  
                                                           "calculator" 
                                                           true 
                                                           [originModeDropDown;surfaceMeasurements] 
                                                           UpdateCoordinateSystemMeasurements
                            | _,_ ->
                                GuiEx.accordion "Surface Measurements"  
                                                "calculator" 
                                                true 
                                                [] 
                       ) m.surface1 m.surface2
            


        let updateButton =
          button [clazz "ui icon button"; onMouseClick (fun _ -> UpdateAllMeasurements )] 
                  [i [clazz "calculator icon"] []]  |> UI.wrapToolTip DataPosition.Bottom "Update"
        let exportButton = 
          button [clazz "ui icon button"
                  onMouseClick (fun _ -> ExportMeasurements "measurements.json")] 
                 [i [clazz "download icon"] [] ]
                    |> UI.wrapToolTip DataPosition.Bottom "Export"

        let annotationComparison =
            let tables = 
                adaptive {
                    let! s1 = m.surface1
                    let! s2 = m.surface2
                    let! measurements = m.annotationMeasurements
                    match s1, s2 with
                    | Some s1, Some s2 ->
                        let lst = 
                            alist {
                                for annoMeasurement in  measurements do
                                    yield (AnnotationComparison.view s1 s2 annoMeasurement)
                            }
                        let content = 
                            Incremental.div ([] |> AttributeMap.ofList) 
                                            lst
                        return content
                    | _,_ ->
                        return div [] []
                
                }
            let header = sprintf "Annotation Length Comparison"
            let content = Incremental.div  ([] |> AttributeMap.ofList) 
                                           (AList.ofAValSingle tables)
            let accordion =
                GuiEx.accordionWithOnClick header  "calculator" true [content] UpdateAnnotationMeasurements
            accordion

        let areas =
            let content = 
                let areas = m.areas |> AMap.toASet |> ASet.toAList
                alist {
                    let! selected = m.selectedArea
                    for key, area in areas do
                        let! label = area.label
                        let selected = AVal.map (fun s -> 
                                                    match s with
                                                    | Some s -> s = key
                                                    | None -> false) m.selectedArea
                        let selectIcon = Html.SemUi.iconToggle selected 
                                                                "large circle icon" 
                                                                "large circle outline icon"
                                                                (SelectArea (Some key))
                        let visibleIcon = Html.SemUi.iconToggle area.visible 
                                                               "large unhide icon" 
                                                               "large hide icon" 
                                                               (AreaSelectionAction.ToggleVisible)
                                              |> UI.map (fun x -> AreaSelectionMessage (area.id, x))
                        let resolutionIcon = Html.SemUi.iconToggle area.highResolution 
                                                               "large arrow up icon" 
                                                               "large arrow down icon" 
                                                               (AreaSelectionAction.ToggleResolution)
                                              |> UI.map (fun x -> AreaSelectionMessage (area.id, x))
                        //let infoIcon = i [clazz "large info icon "] [] 
                        //                  |> UI.wrapToolTip DataPosition.Top "Select | Visibility | Resolution | Info | Delete"
                        let deleteButton =
                                      i [clazz "large remove icon red";
                                         attribute "data-content" "Delete"; 
                                         onMouseClick (fun _ -> RemoveArea area.id) ] []  
                                                         

                        yield (Html.row label [selectIcon;visibleIcon;
                                               resolutionIcon;deleteButton])

                
                }
            Incremental.table ([clazz "ui celled striped inverted table unstackable"] |> AttributeMap.ofList) content
            //tbody
        let areaView =
            //let createAreaMenu (area : AdaptiveAreaSelection) =
                //Html.table ([      
                //  Html.row "Selected Area" []
                //  //Html.row "Visible"   
                //  //         [ 
                //  //    )
                //  Html.row "Remove"
                           
                //])
              
            let selectedAreaView =
                alist {
                    let! guid = m.selectedArea
                    if guid.IsSome then
                        let area = AMap.find guid.Value m.areas
                        //let menu = (area |> AVal.map createAreaMenu)
                        //let! menu = menu
                        //yield menu
                        let! domNode =  (area |> AVal.map AreaSelection.view)
                        yield domNode
                }


            let header = sprintf "Area Comparison"
            let areaSize = 
                Html.table [
                    Html.row "Default Area Radius " 
                             [Numeric.view' [InputBox] m.initialAreaSize 
                                |> UI.map UpdateDefaultAreaSize]
                    Html.row "Point Size Factor" 
                             [Numeric.view' [InputBox] m.pointSizeFactor
                                |> UI.map UpdatePointSizeFactor]
                    Html.row "Distance Calculation Mode" 
                             [Html.SemUi.dropDown m.surfaceGeometryType
                                                  SetDistanceMode]
                ]
            
            
                        
            let selectedAreaView = Incremental.div  ([] |> AttributeMap.ofList) 
                                           selectedAreaView
            let accordion =
                GuiEx.accordionWithOnClick header "calculator" true 
                                           [areaSize;areas;selectedAreaView] 
                                           UpdateAreaMeasurements
            accordion

        let statsGui =
            [
                br []
                div [] [areaView]
                br []
                Incremental.div ([] |> AttributeMap.ofList)  
                                (AList.ofAValSingle surfaceMeasurements)
                //GuiEx.accordion "Difference" "calculator" true [
                //   Incremental.div ([] |> AttributeMap.ofList)  (AList.ofAValSingle compared)
                //]
                br []
                annotationComparison
            ]

        //let statsGuiOrBusyIcon =
        //    let content = 
        //        alist {
        //            let! threads = m.threads
        //            if threads.store.Count > 0 then
        //                yield div [style "color: white"] [text (sprintf "threads: %i" threads.store.Count)]
        //            else
        //                yield div [] (statsGui ())
        //        }
            //let content = 
            //    alist {
            //        let! state = m.state
            //        yield div [style "color: white"] [text (sprintf "state %s" (m.state.ToString ()))]
            //        match state with
            //        | ComparisonAppState.CalculatingStatistics -> ()
            //        | ComparisonAppState.Idle -> 
            //            yield div [] (statsGui ())
            //    }

           // Incremental.div ([] |> AttributeMap.ofList) content



        div [][
            br []
            div [clazz "ui buttons inverted"] 
                [updateButton;exportButton]
            br []
            Html.table [
                Html.row "Surface1 " [CustomGui.surfacesDropdown surfaces SelectSurface1 noSelection]
                Html.row "Surface2 " [CustomGui.surfacesDropdown surfaces SelectSurface2 noSelection]
            ]
            div [] statsGui //|> UI.map ComparisonAction

        ]
