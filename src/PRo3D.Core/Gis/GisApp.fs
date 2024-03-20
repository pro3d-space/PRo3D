#nowarn "9"
namespace PRo3D.Core.Gis

open System
open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open PRo3D.Core.Surface
open PRo3D.Base.Gis
open Aether
open PRo3D.Core.SequencedBookmarks

open Aardvark.Rendering
open Aardvark.SceneGraph

// SPICE
open PRo3D.Extensions
open PRo3D.Extensions.FSharp

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
        | GisAppAction.BookmarkObservationInfoMessage (id, msg) ->
            let bookmarks = Optic.get lenses.bookmarks viewer
            let f (bm : SequencedBookmarkModel) =
                Optic.map SequencedBookmarkModel.observationInfo_ (fun a -> 
                    match a with
                    | Some a -> 
                        ObservationInfo.update a msg
                        |> Some
                    | None ->
                        None
                    ) bm
            let bookmarks = SequencedBookmarksApp.updateSelected f bookmarks 
            let viewer =
                Optic.set lenses.bookmarks bookmarks viewer
            viewer, m
        | GisAppAction.EntityMessage (id, msg) ->
            let m = 
                match msg with
                | EntityAction.Delete id ->
                    let entities =
                        HashMap.remove id m.entities
                    {m with entities = entities}
                | EntityAction.Cancel id ->
                    {m with newEntity = None}
                | EntityAction.Save id ->
                    match m.newEntity with
                    | Some newEntity when newEntity.spiceName = id ->
                        let newEntity = Entity.update newEntity (EntityAction.Save id)
                        let entities = 
                            HashMap.add newEntity.spiceName newEntity m.entities
                        {m with entities  = entities
                                newEntity = None}
                    | _ -> 
                        let entities = 
                            HashMap.update id (fun s -> 
                                match s with
                                | Some s ->
                                    Entity.update s msg
                                | None   -> Entity.initial ()
                            ) m.entities
                        {m with entities = entities}
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
                                | None   -> Entity.initial ()
                            ) m.entities

                        {m with entities = entities}
            viewer, m
        | GisAppAction.FrameMessage (id, msg) ->
            let m = 
                match msg with
                | ReferenceFrameAction.Delete id ->
                    let referenceFrames =
                        HashMap.remove id m.referenceFrames
                    {m with referenceFrames = referenceFrames}
                | ReferenceFrameAction.Cancel ->
                    {m with newFrame = None}
                | ReferenceFrameAction.Save ->
                    match m.newFrame with
                    | Some newFrame ->
                        let newFrame = ReferenceFrame.update newFrame ReferenceFrameAction.Save 
                        let referenceFrames = 
                            HashMap.add newFrame.spiceName newFrame m.referenceFrames
                        {m with referenceFrames  = referenceFrames
                                newFrame = None}
                    | None -> m
                | _ ->
                    match m.newFrame with
                    | Some newFrame when newFrame.spiceName = id ->
                        let newFrame = ReferenceFrame.update newFrame msg
                        {m with newFrame = Some newFrame}
                    | _ ->
                        let referenceFrames = 
                            HashMap.update id (fun s -> 
                                match s with
                                | Some s ->
                                    ReferenceFrame.update s msg
                                | None   -> ReferenceFrame.initial ()
                            ) m.referenceFrames

                        {m with referenceFrames = referenceFrames}
            viewer, m
        | GisAppAction.NewEntity ->
            let newEntity = Entity.initial ()
            viewer, {m with newEntity = Some newEntity}
        | GisAppAction.NewFrame ->
            let newFrame = ReferenceFrame.initial ()
            viewer, {m with newFrame = Some newFrame}
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
        (frames      : amap<FrameSpiceName, AdaptiveReferenceFrame>) =
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

    let viewEntities (m : AdaptiveGisApp) =
        let mapper msg s =
            GisAppAction.EntityMessage (s, msg)
        let newEntityRow =
            alist {
                let! newEntity = m.newEntity
                match newEntity with
                | AdaptiveSome newEntity ->
                    yield (Entity.newViewAsTr newEntity m.referenceFrames mapper)
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

    let viewFrames (m : AdaptiveGisApp) =
        let mapper msg s =
            GisAppAction.FrameMessage (s, msg)
        let newFrameRow =
            alist {
                let! newFrame = m.newFrame
                match newFrame with
                | AdaptiveSome newFrame ->
                    yield (ReferenceFrame.viewAsTr newFrame m.entities mapper)
                | AdaptiveNone ->
                    ()
            }
        let frameRows = 
            m.referenceFrames
            |> AMap.toASet
            |> ASet.map snd
            |> AList.ofASet
            |> AList.map (fun s -> 
                ReferenceFrame.viewAsTr s m.entities mapper)
        
        let rows = 
            AList.append frameRows newFrameRow

        let headers =
            [
                tr [] [
                    th [] [text "Label"]
                    th [] [text "Spice Name"]
                    th [] [text "Entity"]
                    th [] []
                ]
            ] |> AList.ofList

        let actions =
            [
                tr [] [
                    td [attribute "colspan" "4"; style "text-align: right"] [
                        i [clazz "green plus icon"
                           onClick (fun _ -> GisAppAction.NewFrame)
                        ] []
                    ]
                ]
            ] |> AList.ofList

        Incremental.table 
            ([clazz "ui unstackable inverted table"] |> AttributeMap.ofList)
            (AList.append (AList.append headers rows) actions)

    let viewSelectedBookmark
            (bookmark : SequencedBookmarks.AdaptiveSequencedBookmarkModel) 
            (m : AdaptiveGisApp) =  
        let info =
            alist {
                let! info = bookmark.observationInfo
                match info with
                | AdaptiveSome info ->
                    yield (ObservationInfo.view info m.entities m.referenceFrames
                            |> UI.map (fun msg -> 
                        GisAppAction.BookmarkObservationInfoMessage (bookmark.bookmark.key, msg)))
                | AdaptiveNone ->
                    ()
            }
        Incremental.div AttributeMap.empty info

    let view (m : AdaptiveGisApp)
             (surfaces : AdaptiveSurfaceModel)
             (bookmarks : SequencedBookmarks.AdaptiveSequencedBookmarks) =  
        let bookmarkGisInfo =
            alist {
                let! (id : option<System.Guid>) = bookmarks.selectedBookmark 
                match id with
                | Some id ->
                    let! bm = AMap.tryFind id bookmarks.bookmarks
                    match bm with
                    | Some (AdaptiveSequencedBookmark.AdaptiveLoadedBookmark bookmark) ->
                        yield h5 [clazz "ui inverted horizontal divider header"
                                  style "padding-top: 1rem"] 
                                 [Incremental.text (bookmark.name 
                                    |> AVal.map (sprintf "Observation Settings: %s"))]
                        yield viewSelectedBookmark bookmark m
                    | Some (AdaptiveSequencedBookmark.AdaptiveNotYetLoaded bm) ->
                        Log.warn "[SequencedBookmarksApp] Bookmark not loaded %s" (string id)
                        ()
                    | None ->
                        Log.error "[SequencedBookmarksApp] Could not find bookmark %s" (string id)
                        ()
                    ()
                | None ->
                    ()
            }
        div [] [ 
            div [clazz "ui inverted segment"] [ 
                h5 [clazz "ui inverted horizontal divider header"
                    style "padding-top: 1rem"] 
                   [text "Default Observation Settings"]
                ObservationInfo.view m.defaultObservationInfo 
                                     m.entities m.referenceFrames
                |> UI.map ObservationInfoMessage
                Incremental.div AttributeMap.empty bookmarkGisInfo
            ]
            GuiEx.accordion "Surfaces" "Cubes" false 
                                  [ viewSurfacesGroupsGis surfaces.surfaces m]
            GuiEx.accordion "Entities" "Cubes" false [
                viewEntities m
            ]
            GuiEx.accordion "Reference Frames" "Cubes" false [
                viewFrames m
            ]
        ]


    module CooTransformation =

        let getPositionTransformationMatrix (pcFrom : string) (pcTo : string) (time : DateTime) : Option<M33d> = 
            let m33d : array<double> = Array.zeroCreate 9
            let pdRotMat = fixed &m33d[0] 
            let result = CooTransformation.GetPositionTransformationMatrix(pcFrom, pcTo, CooTransformation.Time.toUtcFormat time, pdRotMat)
            if result <> 0 then 
                None
            else
                Some (M33d(m33d))


    [<Struct>]
    type RenderableBody = 
        {
            trafo : aval<Trafo3d>
            color : aval<C4b>
        }

    module Shaders = 
        open FShade

        type Vertex = {
            [<Position>] pos : V4d
            [<Color>] c : V4d
        }

        let stableBodyTrafo (v : Vertex) =
            vertex {
                let ndc = uniform.ModelViewProjTrafo * v.pos
                return { v with pos = ndc }
            }



    let entityToRenderable (targetFrame : aval<Option<FrameSpiceName>>) (time : aval<DateTime>) (relState : aval<Option<CooTransformation.RelState>>) (entity : AdaptiveEntity) =
        
        let entityPosition =
            relState |> AVal.map (function 
                | None -> Trafo3d.Identity
                | Some relState -> 
                    Trafo3d.Translation(relState.pos)
            )
        
        let trafo = 
            adaptive {
                let! radius = entity.radius
                let! entityRefRame = entity.defaultFrame
                let! tartetRefFrame = targetFrame
                let! time = time

                // This is the transformation pipeline (in Trafo order, i.e. inverse matrix multiplication order)
                // BODY (Unit Sphere)
                // Scale to Radius
                // Transform from body reference frame to target reference frame
                // Set position, relative to observer

                let unitSphereToRadius = 
                    Trafo3d.Scale(radius) // unit sphere has radius 1, scaled by radius, we have real body radius.
                
                let bodyFrameToTargetFrame =
                    match entityRefRame, tartetRefFrame with
                    | Some (FrameSpiceName bodySpiceFrame), Some (FrameSpiceName targetSpiceFrame) -> 
                        match CooTransformation.getPositionTransformationMatrix bodySpiceFrame targetSpiceFrame time with
                        | Some toTarget -> 
                            let m44d = M44d toTarget
                            Trafo3d(m44d, m44d.Inverse)
                        | None -> 
                            Log.warn "getPositionTransformationMatrix failed: %A" (bodySpiceFrame, targetSpiceFrame, time)
                            Trafo3d.Identity
                    | _ -> 
                        Trafo3d.Identity

                let! bodyToObserver = entityPosition

                return 
                    //unitSphereToRadius * bodyFrameToTargetFrame * bodyToObserver
                    unitSphereToRadius * bodyToObserver
            }


        { trafo = trafo; color = entity.color |> AVal.map C4b }


    let getDefaultReferenceFrame (frame : aval<Option<FrameSpiceName>>) =
        frame |> AVal.map (Option.defaultValue (FrameSpiceName "J2000"))

    let viewWithObserver (observerSpiceBody : aval<EntitySpiceName>) (targetReferenceFrame : aval<Option<FrameSpiceName>>) (time : aval<DateTime>) (bodies : aset<EntitySpiceName * AdaptiveEntity>) =

        let targetReferenceFrameWithDefault = getDefaultReferenceFrame targetReferenceFrame 

        let renderableBodies =
            bodies |> ASet.map (fun (EntitySpiceName spiceBody, entity) ->
                let relState = 
                    adaptive {
                        let! (FrameSpiceName frame) = targetReferenceFrameWithDefault
                        let! (EntitySpiceName observer) = observerSpiceBody
                        let! time = time
                        match CooTransformation.getRelState spiceBody "sun" observer time frame with
                        | Some r -> 
                            return Some r
                        | None -> 
                            Log.warn "getRelState failed: %A" (spiceBody, "sun", observer, time, frame)
                            return None
                    }
                entityToRenderable targetReferenceFrame time relState entity
            )

        let extractArray (f : AdaptiveToken -> RenderableBody -> 'a) = 
            renderableBodies.Content
            |> AVal.bind (fun s -> 
                AVal.custom (fun t -> 
                    s |> HashSet.toArray |> Array.map (f t)
                )
            )

        let trafos = extractArray (fun t b -> b.trafo.GetValue(t))
        let colors = extractArray (fun t b -> b.color.GetValue(t))

        let bodySg = 
            IndexedGeometryPrimitives.solidSubdivisionSphere (Sphere3d.FromCenterAndRadius(V3d.Zero, 1.0)) 3 C4b.White
            |> Sg.ofIndexedGeometry

        let instancedUniforms = 
            Map.ofList [
                Sym.toString DefaultSemantic.Colors, (typeof<C4b>, colors |> AVal.map (fun c -> c :> Array))
                "ModelTrafo", (typeof<Trafo3d>, (trafos |> AVal.map (fun t -> t :> Array)))
            ]

        bodySg
        |> Sg.instanced' instancedUniforms 
        |> Sg.shader {
            do! Shaders.stableBodyTrafo
        }
        |> Sg.cullMode' CullMode.Back
        |> Sg.depthTest' DepthTest.None 


    let getSurfaceAdaptiveToViewerTrafo (m : AdaptiveGisApp) (s : SurfaceId) =
        let observer = m.defaultObservationInfo.observer 
        let observerWithDefault = observer |> AVal.map (Option.defaultValue (EntitySpiceName "mars"))
        let time = m.defaultObservationInfo.time.date
        let targetReferenceFrameWithDefault = getDefaultReferenceFrame m.defaultObservationInfo.referenceFrame 

        adaptive {
            match! m.gisSurfaces |> AMap.tryFind s with
            | None -> 
                return Trafo3d.Identity
            | Some gisSurface -> 
                let! time = m.defaultObservationInfo.time.date
                let! observer = observer
                let! (FrameSpiceName targetRefFrame) = targetReferenceFrameWithDefault

                let surfaceToGlobalReferenceFrame =
                    match gisSurface.referenceFrame with
                    | None -> Trafo3d.Identity
                    | Some (FrameSpiceName surfaceReferenceFrame) -> 
                        match CooTransformation.getPositionTransformationMatrix surfaceReferenceFrame targetRefFrame time with
                        | Some toTarget -> 
                            let m44d = M44d toTarget
                            Trafo3d(m44d, m44d.Inverse)
                        | None -> 
                            Log.warn "getPositionTransformationMatrix failed: %A" (surfaceReferenceFrame,targetRefFrame,time)
                            Trafo3d.Identity

                let position =
                    match gisSurface.entity, observer with
                    | Some (EntitySpiceName body), Some (EntitySpiceName observer) ->
                        match CooTransformation.getRelState body "sun" observer time targetRefFrame with
                        | Some r -> Trafo3d.Translation(r.pos)
                        | None -> 
                            Log.warn "getRelState failed: %A" (body, "sun", observer, time, targetRefFrame)
                            Trafo3d.Identity
                    | _ -> Trafo3d.Identity

                return surfaceToGlobalReferenceFrame * position
        }

    let lookAtObserver (m : GisApp) =
        match m.defaultObservationInfo.observer, m.defaultObservationInfo.referenceFrame, m.defaultObservationInfo.target with
        | Some (EntitySpiceName observer), Some (FrameSpiceName refFrame), Some (EntitySpiceName target) ->
            Log.line "look at. target: %A, observer: %A, %A" target observer refFrame
            match CooTransformation.getRelState target "SUN" observer  m.defaultObservationInfo.time.date refFrame with
            | None -> 
                Log.warn "getRelState failed: %A" (target, "sun", observer, time, m.defaultObservationInfo.time.date, refFrame)
                None
            | Some rel -> 
                let rot = rel.rot
                let t = Trafo3d.FromBasis(rot.C0, rot.C1, -rot.C2, V3d.Zero)
                CameraView.ofTrafo t.Inverse |> Some
                //CameraView.lookAt rel.pos V3d.Zero V3d.OOI |> Some
        | _ -> None

    let view3D (m : AdaptiveGisApp) =
        let observer = m.defaultObservationInfo.observer 
        let observerWithDefault = observer |> AVal.map (Option.defaultValue (EntitySpiceName "mars"))
        let time = m.defaultObservationInfo.time.date
        let targetReferenceFrame = m.defaultObservationInfo.referenceFrame 
        viewWithObserver observerWithDefault targetReferenceFrame time (m.entities |> AMap.toASet)
        |> Sg.onOff (observer |> AVal.map Option.isSome)



    let initial : GisApp =
        let entities =
            [
                Entity.earth.spiceName,     Entity.earth 
                Entity.mars.spiceName,      Entity.mars 
                Entity.deimos.spiceName,    Entity.deimos 
                Entity.phobos.spiceName,    Entity.phobos 
                Entity.moon.spiceName,      Entity.moon 
                //Entity.didymos.spiceName,   Entity.didymos 
                //Entity.dimorphos.spiceName, Entity.dimorphos 
                Entity.heraSpacecraft.spiceName, Entity.heraSpacecraft 
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
            newFrame                = None
            defaultObservationInfo  = ObservationInfo.initial
        }
