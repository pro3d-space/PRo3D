namespace PRo3D.Core.Gis


open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
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

    let assignBody (entities : HashMap<SurfaceId, Entity>) 
                   (surfaceId : SurfaceId)
                   (body : option<BodySpiceName>) =
        let oldEntity = 
            entities
            |> HashMap.tryFind surfaceId
        let newEntity = 
            match oldEntity with
            | Some oldEntity ->
                match oldEntity with
                | Entity.EntitySurface gisSurface ->
                    Entity.EntitySurface {gisSurface with body = body}
                | _ -> 
                    Log.line "[GisApp] Expected EntitySurface but got %s" (string oldEntity)
                    oldEntity
            | None ->
                Entity.EntitySurface (GisSurface.fromBody surfaceId body)
        HashMap.add surfaceId newEntity entities

    let assignFrame (entities  : HashMap<SurfaceId, Entity>) 
                    (surfaceId : SurfaceId)
                    (frame     : option<FrameSpiceName>) =
        let oldEntity = 
            entities
            |> HashMap.tryFind surfaceId
        let newEntity = 
            match oldEntity with
            | Some oldEntity ->
                match oldEntity with
                | Entity.EntitySurface gisSurface ->
                    Entity.EntitySurface {gisSurface with referenceFrame = frame}
                | _ -> 
                    Log.line "[GisApp] Expected EntitySurface but got %s" (string oldEntity)
                    oldEntity
            | None ->
                Entity.EntitySurface (GisSurface.fromFrame surfaceId frame)
        HashMap.add surfaceId newEntity entities

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
        | GisAppAction.SpacecraftMessage (id, msg) ->
            let m = 
                match msg with
                | SpacecraftAction.Delete id ->
                    let spacecraft =
                        HashMap.remove id m.spacecraft
                    {m with spacecraft = spacecraft}
                | _ ->
                    let spacecraft = 
                        HashMap.update id (fun s -> 
                            match s with
                            | Some s ->
                                Spacecraft.update s msg
                            | None   -> Spacecraft.inital ()
                        ) m.spacecraft
                    {m with spacecraft = spacecraft}
            viewer, m
        | GisAppAction.NewSpacecraft ->
            let newSpacecraft = Spacecraft.inital ()
            let spacecraft = 
                HashMap.add newSpacecraft.id newSpacecraft m.spacecraft
            viewer, {m with spacecraft = spacecraft}
        | GisAppAction.Observe ->
            viewer, m
        | GisAppAction.AssignBody (surfaceId, body) ->
            let entities = assignBody m.entities surfaceId body
            viewer, {m with entities = entities}
        | GisAppAction.AssignReferenceFrame (surfaceId, frame) ->
            let entities = assignFrame m.entities surfaceId frame
            viewer, {m with entities = entities}

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
                let currentlyAssociatedBody = 
                    AMap.tryFind key m.entities
                    |> AVal.map (Option.bind (fun x ->
                        match x with
                        | Entity.EntitySurface gisSurface -> 
                            gisSurface.body
                        | _ -> None
                        ))
                let bodySelectionGui =
                    UI.dropDownWithEmptyText
                        (m.bodies 
                            |> AMap.keys
                            |> ASet.toAList)
                        currentlyAssociatedBody
                        (fun x -> AssignBody (key, x))  
                        (fun x -> x.Value)
                        "Select Body"
                let currentlyAssociatedRefFrame = 
                    AMap.tryFind key m.entities
                    |> AVal.map (Option.bind (fun x ->
                        match x with
                        | Entity.EntitySurface gisSurface -> 
                            gisSurface.referenceFrame
                        | _ -> None
                        ))
                let refFramesSelectionGui =
                    UI.dropDownWithEmptyText
                        (m.referenceFrames 
                            |> AMap.keys
                            |> ASet.toAList)
                        currentlyAssociatedRefFrame
                        (fun x -> AssignReferenceFrame (key, x))  
                        (fun x -> x.Value)
                        "Select Frame"
                let! c = SurfaceApp.mkColor model s
                let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
                let bgc = sprintf "color: %s" (Html.ofC4b c)
                let content = 
                    Incremental.div (AttributeMap.ofList [style infoc]) (
                        alist {
                            yield div [clazz "header"] [
                                div [] [
                                    Incremental.span AttributeMap.empty 
                                                    ([Incremental.text headerText] |> AList.ofList)
                                    i [
                                        clazz "home icon"; 
                                        onClick (fun _ -> FlyToSurface key);
                                        style "margin-left: 0.2rem;margin-right: 0.4rem"
                                        ] [] 
                                    |> UI.wrapToolTip DataPosition.Bottom "Fly to surface"   
                                    |> UI.map SurfacesMessage
                                    bodySelectionGui
                                    refFramesSelectionGui
                                ]
                            ] 
                        } 
                    )            
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

    let viewSpacecraft (m : AdaptiveGisApp) =
        let rows = 
            m.spacecraft
            |> AMap.toASet
            |> ASet.map snd
            |> AList.ofASet
            |> AList.map (fun s -> 
                Spacecraft.viewAsTr s m.referenceFrames
                |> UI.map (fun msg -> GisAppAction.SpacecraftMessage (s.id, msg)))

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
                           onClick (fun _ -> GisAppAction.NewSpacecraft)
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
                ObservationInfo.view m.defaultObservationInfo m.entities m.referenceFrames
                |> UI.map ObservationInfoMessage
            ]
            GuiEx.accordion "Surfaces" "Cubes" false 
                                  [ viewSurfacesGroupsGis surfaces.surfaces m]

            GuiEx.accordion "GIS Bookmarks" "Cubes" false [
                
            ]
            GuiEx.accordion "Spacecraft" "Cubes" false [
                viewSpacecraft m
            ]
                
        ]

    let inital : GisApp =
        let bodies =
            [
                (Body.earth.spiceName, Body.earth )
                (Body.mars.spiceName, Body.mars )
                (Body.moon.spiceName, Body.moon )
                (Body.didymos.spiceName, Body.didymos )
                (Body.dimorphos.spiceName, Body.dimorphos )
            ] |> HashMap.ofList
        let referenceFrames =
            [
                (ReferenceFrame.j2000.spiceName, ReferenceFrame.j2000)
                (ReferenceFrame.iauMars.spiceName, ReferenceFrame.iauMars)
                (ReferenceFrame.iauEarth.spiceName, ReferenceFrame.iauEarth)
            ] |> HashMap.ofList
        let spacecraft =
            let heraSpacecraft = Spacecraft.heraSpacecraft ()
            [
                heraSpacecraft.id, heraSpacecraft
            ] |> HashMap.ofList
        {
            version                 = GisApp.current
            bodies                  = bodies
            referenceFrames         = referenceFrames
            spacecraft              = spacecraft
            entities                = HashMap.empty
            defaultObservationInfo  = ObservationInfo.inital
        }