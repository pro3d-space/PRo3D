namespace PRo3D.Core.Gis


open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open PRo3D.Core.Surface
open PRo3D.Base.Gis
open Aether

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module GisApp = 
    type GisLenses<'viewer> = 
        {
            surfacesModel   : Lens<'viewer, SurfaceModel>
            bookmarks       : Lens<'viewer, SequencedBookmarks.SequencedBookmarks>
            scenePath       : Lens<'viewer, option<string>> 
            navigation      : Lens<'viewer, NavigationModel>
            referenceSystem : Lens<'viewer, ReferenceSystem>
        }

    let assignBody (gisSurfaces : HashMap<SurfaceId, GisSurface>) 
                   (surfaceId   : SurfaceId)
                   (entity      : option<EntitySpiceName>) =
        let oldSurface = 
            gisSurfaces
            |> HashMap.tryFind surfaceId
        let newSurface = 
            match oldSurface with
            | Some oldSurface ->
                {oldSurface with entity = entity}
            | None ->
               GisSurface.fromBody surfaceId entity
        HashMap.add surfaceId newSurface gisSurfaces

    let assignFrame (gisSurfaces : HashMap<SurfaceId, GisSurface>)
                    (surfaceId : SurfaceId)
                    (frame     : option<FrameSpiceName>) =
        let oldSurface = 
            gisSurfaces
            |> HashMap.tryFind surfaceId
        let newSurface = 
            match oldSurface with
            | Some oldSurface ->
                {oldSurface with referenceFrame = frame}
            | None ->
               GisSurface.fromFrame surfaceId frame
        HashMap.add surfaceId newSurface gisSurfaces

    let update (m : GisApp) 
               (lenses : GisLenses<'viewer>)
               (viewer : 'viewer)
               (msg : GisAppAction) =
        match msg with
        | GisAppAction.SurfacesMessage msg ->
            let surfaces = 
                SurfaceApp.update 
                    (Optic.get lenses.surfacesModel viewer)
                    msg
                    (Optic.get lenses.scenePath viewer)
                    (Optic.get lenses.navigation viewer).camera.view
                    (Optic.get lenses.referenceSystem viewer)
            let viewer = 
                Optic.set lenses.surfacesModel surfaces viewer
            viewer, m
        | GisAppAction.ObservationInfoMessage msg ->
            let info = ObservationInfo.update m.defaultObservationInfo msg
            let m = {m with defaultObservationInfo = info}
            viewer, m
        | GisAppAction.EntityMessage (id, msg) ->
            let m = 
                match msg with
                | EntityAction.Delete id ->
                    let entities =
                        HashMap.remove id m.entities
                    {m with entities = entities}
                | EntityAction.Cancel ->
                    {m with newEntity = None}
                | EntityAction.Save ->
                    match m.newEntity with
                    | Some newEntity ->
                        let newEntity = Entity.update newEntity EntityAction.Save 
                        let entities = 
                            HashMap.add newEntity.spiceName newEntity m.entities
                        {m with entities  = entities
                                newEntity = None}
                    | None -> m
                | _ ->
                    match m.newEntity with
                    | Some newEntity when newEntity.spiceName = id ->
                        let newEntity = Entity.update newEntity msg
                        {m with newEntity = Some newEntity}
                    | _ ->
                        let entities = 
                            HashMap.update id (fun s -> 
                                match s with
                                | Some s ->
                                    Entity.update s msg
                                | None   -> Entity.inital ()
                            ) m.entities

                        {m with entities = entities}
            viewer, m
        | GisAppAction.NewEntity ->
            let newEntity = Entity.inital ()
            viewer, {m with newEntity = Some newEntity}
        | GisAppAction.Observe ->
            viewer, m
        | GisAppAction.AssignBody (surfaceId, spiceName) ->
            let gisSurfaces = assignBody m.gisSurfaces surfaceId spiceName
            viewer, {m with gisSurfaces = gisSurfaces}
        | GisAppAction.AssignReferenceFrame (surfaceId, frame) ->
            let gisSurfaces = assignFrame m.gisSurfaces surfaceId frame
            viewer, {m with gisSurfaces = gisSurfaces}

    let private currentlyAssociatedEntity
        (surface : SurfaceId) 
        (gisSurfaces : amap<SurfaceId, GisSurface>) 
        (bodies      : amap<EntitySpiceName, AdaptiveEntity>) =
        adaptive {
            let! surf =  AMap.tryFind surface gisSurfaces
            let spiceName = Option.bind (fun (s : GisSurface) -> s.entity) surf
            return spiceName
        }

    let private currentlyAssociatedFrame 
        (surface : SurfaceId) 
        (gisSurfaces : amap<SurfaceId, GisSurface>) 
        (frames      : amap<FrameSpiceName, ReferenceFrame>) =
        adaptive {
            let! surf =  AMap.tryFind surface gisSurfaces
            let spiceName = Option.bind (fun (s : GisSurface) -> s.referenceFrame) surf
            return spiceName
        }

    let private viewSurfacesInGroups 
        (path         : list<Index>) 
        (model        : AdaptiveGroupsModel) 
        (lift         : GroupsAppAction -> SurfaceAppAction) 
        (surfaceIds   : alist<System.Guid>) 
        (m            : AdaptiveGisApp) =
        alist {
            let surfaces = 
                surfaceIds 
                |> AList.chooseA (fun x -> model.flat |> AMap.tryFind x)
            
            let surfaces = 
                surfaces
                |> AList.choose(function | AdaptiveSurfaces s -> Some s | _-> None )
            
            for s in surfaces do 
                let headerText = 
                    AVal.map (fun a -> sprintf "%s" a) s.name
                let! key = s.guid

                let entity = 
                    currentlyAssociatedEntity key m.gisSurfaces m.entities
                    
                let entitySelectionGui =
                    UI.dropDownWithEmptyText
                        (m.entities 
                            |> AMap.toASet
                            |> ASet.toAList
                            |> AList.map fst)
                        entity 
                        (fun x -> AssignBody (key, x))  
                        (fun x -> x.Value)
                        "Select Entity"
                let frame = 
                    currentlyAssociatedFrame key m.gisSurfaces m.referenceFrames
                let refFramesSelectionGui =
                    UI.dropDownWithEmptyText
                        (m.referenceFrames 
                            |> AMap.toASet
                            |> ASet.toAList
                            |> AList.map fst)
                        frame
                        (fun x -> AssignReferenceFrame (key, x))  
                        (fun x -> x.Value)
                        "Select Frame"
                let! c = SurfaceApp.mkColor model s
                let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
                let bgc = sprintf "color: %s" (Html.ofC4b c)
                let content = 
                    div [style infoc] [
                        div [clazz "header"] [
                            span [] [Incremental.text headerText]
                            i [
                                clazz "home icon"; 
                                onClick (fun _ -> FlyToSurface key);
                                style "margin-left: 0.2rem;margin-right: 0.4rem"
                                ] [] 
                            |> UI.wrapToolTip DataPosition.Bottom "Fly to surface"   
                            |> UI.map SurfacesMessage
                            entitySelectionGui
                            refFramesSelectionGui
                        ]
                    ]
                        
                               
                let items = 
                    div [clazz "item"; style infoc] [
                        div [clazz "content"; style infoc] [                     
                            content           
                        ]
                    ]
                yield items
        }

    let rec viewTree path (group : AdaptiveNode) 
                     (surfaces : AdaptiveGroupsModel) 
                     (m : AdaptiveGisApp) =

        alist {
            let! s = surfaces.activeGroup
            let color = sprintf "color: %s" (Html.ofC4b C4b.White)                
            let children = AList.collecti (fun i v -> viewTree (i::path) v surfaces m) group.subNodes    
            let activeAttributes = GroupsApp.setActiveGroupAttributeMap path surfaces group GroupsMessage
                                   
            let desc =
                div [style color] [       
                    Incremental.text group.name
                    Incremental.i activeAttributes AList.empty 
                    |> UI.wrapToolTip DataPosition.Bottom "Set active"
                ]
                 
            let itemAttributes =
                amap {
                    yield onMouseClick (fun _ -> SurfaceAppAction.GroupsMessage(GroupsAppAction.ToggleExpand path))
                    let! selected = group.expanded
                    if selected 
                    then yield clazz "icon outline open folder"
                    else yield clazz "icon outline folder"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap
            
            let childrenAttribs =
                amap {
                    yield clazz "list"
                    let! isExpanded = group.expanded
                    if isExpanded then yield style "visible"
                    else yield style "hidden"
                }         

            let lift = fun (a:GroupsAppAction) -> (GroupsMessage a)

            yield div [ clazz "item"] [
                Incremental.i itemAttributes AList.empty
                |> UI.map GisAppAction.SurfacesMessage
                div [ clazz "content" ] [                         
                    div [ clazz "description noselect"] [desc]
                    |> UI.map GisAppAction.SurfacesMessage
                    Incremental.div (AttributeMap.ofAMap childrenAttribs) (                          
                        alist { 
                            let! isExpanded = group.expanded
                            if isExpanded then yield! children
                            
                            if isExpanded then 
                                yield! (viewSurfacesInGroups 
                                            path surfaces lift group.leaves m)
                        }
                    )  
                ]
            ]
                
        }

    let viewSurfacesGroupsGis (surfaces:AdaptiveGroupsModel)
                              (m : AdaptiveGisApp) = 
        require GuiEx.semui (
            Incremental.div 
              (AttributeMap.ofList [clazz "ui inverted celled list"]) 
              (viewTree [] surfaces.rootGroup surfaces m)            
        )    

    let viewEntity (m : AdaptiveGisApp) =
        let mapper msg s =
            GisAppAction.EntityMessage (s, msg)
        let newEntityRow =
            alist {
                let! newEntity = m.newEntity
                match newEntity with
                | AdaptiveSome newEntity ->
                    yield (Entity.viewAsTr newEntity m.referenceFrames mapper)
                | AdaptiveNone ->
                    ()
            }
        let entityRows = 
            m.entities
            |> AMap.toASet
            |> ASet.map snd
            |> AList.ofASet
            |> AList.map (fun s -> 
                Entity.viewAsTr s m.referenceFrames mapper)
        
        let rows = 
            AList.append entityRows newEntityRow

        let headers =
            [
                tr [] [
                    th [] [text "Label"]
                    th [] [text "Spice Name"]
                    th [] [text "Reference Frame"]
                    th [] []
                ]
            ] |> AList.ofList

        let actions =
            [
                tr [] [
                    td [attribute "colspan" "4"; style "text-align: right"] [
                        i [clazz "green plus icon"
                           onClick (fun _ -> GisAppAction.NewEntity)
                        ] []
                    ]
                ]
            ] |> AList.ofList

        Incremental.table 
            ([clazz "ui unstackable inverted table"] |> AttributeMap.ofList)
            (AList.append (AList.append headers rows) actions)

    let view (m : AdaptiveGisApp) (surfaces : AdaptiveSurfaceModel) =  
        div [] [ 
            div [clazz "ui inverted segment"] [ 
                h5 [clazz "ui inverted horizontal divider header"
                    style "padding-top: 1rem"] 
                   [text "Default Observation Settings"]
                ObservationInfo.view m.defaultObservationInfo 
                                     m.entities m.referenceFrames
                |> UI.map ObservationInfoMessage
            ]
            GuiEx.accordion "Surfaces" "Cubes" false 
                                  [ viewSurfacesGroupsGis surfaces.surfaces m]

            GuiEx.accordion "GIS Bookmarks" "Cubes" false [
                
            ]
            GuiEx.accordion "Entity" "Cubes" false [
                viewEntity m
            ]
        ]

    let inital : GisApp =
        let entities =
            [
                (Entity.earth.spiceName,     Entity.earth )
                (Entity.mars.spiceName,      Entity.mars )
                (Entity.moon.spiceName,      Entity.moon )
                (Entity.didymos.spiceName,   Entity.didymos )
                (Entity.dimorphos.spiceName, Entity.dimorphos )
                (Entity.heraSpacecraft.spiceName, Entity.heraSpacecraft )
            ] |> HashMap.ofList
        let referenceFrames =
            [
                (ReferenceFrame.j2000.spiceName, ReferenceFrame.j2000)
                (ReferenceFrame.iauMars.spiceName, ReferenceFrame.iauMars)
                (ReferenceFrame.iauEarth.spiceName, ReferenceFrame.iauEarth)
            ] |> HashMap.ofList
        {
            version                 = GisApp.current
            gisSurfaces             = HashMap.empty
            referenceFrames         = referenceFrames
            entities                = entities
            newEntity               = None
            defaultObservationInfo  = ObservationInfo.inital
        }
