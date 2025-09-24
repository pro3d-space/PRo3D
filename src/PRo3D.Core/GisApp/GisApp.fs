#nowarn "9"
namespace PRo3D.Core.Gis

open System
open System.IO
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
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

open PRo3D.SPICE

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

    let loadSpiceKernel (path : string) (m : GisApp) =
        match m.spiceKernel with
        | Some s when s = CooTransformation.SPICEKernel.ofPath path -> m 
        | _ -> 
            if File.Exists path then
                let directory = Path.GetDirectoryName path
                let name = Path.GetFileName path
                match CooTransformation.tryLoadKernel directory name with
                | None -> 
                    Log.line "[GisApp] Could not load spice kernel"
                    {m with spiceKernel = Some (CooTransformation.SPICEKernel.ofPath path)
                            spiceKernelLoadSuccess = false}
                | Some spiceKernel -> 
                    Log.line "[GisApp] Successfully loaded spice kernel %s" path
                    {m with spiceKernel = Some spiceKernel
                            spiceKernelLoadSuccess = true}
            else
                Log.line "[GisApp] Could not find path %s" path
                {m with spiceKernelLoadSuccess = false}

    let loadSpiceKernel' (m : GisApp) =
        match m.spiceKernel with
        | Some kernel ->
            loadSpiceKernel kernel.FullPath m
        | None ->
            m

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
        | GisAppAction.SetSpiceKernel path ->
            let m = loadSpiceKernel path m
            viewer, m
        | GisAppAction.ToggleCameraInObserver ->
            viewer, {m with cameraInObserver = not m.cameraInObserver}
        | GisAppAction.ToggleDrawMarkers -> 
            viewer, {m with showMarkers = not m.showMarkers }
        | GisAppAction.ImageProjection msg -> 
            let m = 
                match msg with 
                | ImageProjectionMessage.SelectImage idx -> 
                    match IndexList.tryGet idx m.projectedImages.images with
                    | None -> m
                    | Some img -> 
                        match img.projection with
                        | None -> m
                        | Some p ->
                            let info = ObservationInfo.update m.defaultObservationInfo (ObservationInfoAction.SetTime p.time)
                            let m = {m with defaultObservationInfo = info}
                            m
                | _ -> m
            viewer, {m with projectedImages = ProjectedImagesApp.update msg m.projectedImages }
            
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
                let infoc = sprintf "color: %s" (Html.color C4b.White)
                let bgc = sprintf "color: %s" (Html.color c)
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
            let color = sprintf "color: %s" (Html.color C4b.White)                
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
                    th [] [text "Spice Name"]
                    th [] [text "Reference Frame"]
                    th [] [text "Draw"]
                    th [] [text "Actions"]
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
                    //th [] [text "Label"]
                    th [] [text "Spice Name"]
                    th [] [text "Associated Entity"]
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
                   [text "Current Observation Settings"]
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

            GuiEx.accordion "Projected Images" "Images" false [
                ProjectedImagesApp.viewProjectedImages m.projectedImages |> UI.map GisAppAction.ImageProjection
            ]

            let kernelPathTextBox = 
                div [clazz "fullwidth textcontainer"] [
                    Html.SemUi.textBox 
                        (m.spiceKernel 
                            |> AVal.map (function Some p -> p.FullPath | _ -> "PRo3D Default Spice Kernel Path")
                        )
                        GisAppAction.SetSpiceKernel 
                ]

            let kernelStatusIcon =
                let attributes = 
                    amap {
                        let! ok = m.spiceKernelLoadSuccess
                        if ok then
                            yield clazz "ui green check icon"
                        else
                            yield clazz "ui red exclamation icon"
                    } |> AttributeMap.ofAMap

                Incremental.i attributes AList.empty

            GuiEx.accordion "Settings" "" false [
                require GuiEx.semui (
                    Html.table [ 
                        Html.row "Path to Spice Kernel" 
                                 [kernelPathTextBox;kernelStatusIcon]
                        Html.row "Show Markers" [GuiEx.iconCheckBox m.showMarkers ToggleDrawMarkers]
                        Html.row "Animation Camera in Observer"
                                 [GuiEx.iconCheckBox m.cameraInObserver ToggleCameraInObserver]
                    ]
                )
            ]
        ]



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
            [<TexCoord>] tc : V2d
            [<Color>] c : V4d
            [<Semantic("modelPos")>] modelPos : V3d
            [<Normal>] normal : V3d
        }

        type UniformScope with
            member x.UniformColor : V4d = uniform?UniformColor
            member x.HasTexture : bool = uniform?HasTexture

        let private diffuseSampler =
            sampler2d {
                texture uniform?DiffuseColorTexture
                filter Filter.MinMagMipLinear
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
            }

        let stableBodyTrafo (v : Vertex) =
            vertex {
                let ndc = uniform.ModelViewProjTrafo * v.pos
                return { v with pos = ndc; modelPos = v.pos.XYZ }
            }

        let color (v : Vertex) =
            vertex {
                return { v with c = uniform.UniformColor }
            }

        let generateUv (v : Vertex) =
            fragment {
                let p = v.modelPos
                let thetha = acos (p.Z) / Math.PI
                let phi = ((float (sign p.Y)) * acos (p.X / Vec.length p.XY)) / (Math.PI * 2.0)
                return { v with tc = V2d(phi, thetha); normal = v.modelPos } 
            }

        let texture (v : Vertex) =
            fragment {
                if uniform.HasTexture then 
                    return { v with c = diffuseSampler.Sample(v.tc) }
                else
                    return v
            }



    let getSurfaceTrafo (m : GisApp)  (s : SurfaceId) =
        match m.gisSurfaces |> HashMap.tryFind s with
        | None -> 
            None
        | Some gisSurface ->
            match gisSurface.entity, m.defaultObservationInfo.observer, m.defaultObservationInfo.referenceFrame with
            | Some entity, Some observer, Some observerFrame -> 
                match CooTransformation.transformBody entity gisSurface.referenceFrame observer observerFrame m.defaultObservationInfo.time.date with
                | None -> None
                | Some t -> 
                   Some t
                   //Some (t.position, Rot3d.Identity, V3d.III)
            | _ -> None
    
    let getFrameWithDefaulting (frame : aval<Option<FrameSpiceName>>) =
        frame |> AVal.map (Option.defaultValue (FrameSpiceName "J2000"))

    let entityToRenderable (observer : aval<EntitySpiceName>) (targetFrame : aval<Option<FrameSpiceName>>) (time : aval<DateTime>) (entity : AdaptiveEntity) =
        
        let trafo = 
            adaptive {
                let! radius = entity.radius
                let! entityRefRame = entity.defaultFrame 
                let! tartetRefFrame = getFrameWithDefaulting targetFrame
                let! observer = observer
                let! time = time

                // This is the transformation pipeline (in Trafo order, i.e. inverse matrix multiplication order)
                // BODY (Unit Sphere)
                // Scale to Radius
                // Transform from body reference frame to target reference frame
                // Set position, relative to observer

                let unitSphereToRadius = 
                    Trafo3d.Scale(radius) // unit sphere has radius 1, scaled by radius, we have real body radius.
                
                let bodyToObserver = 
                    CooTransformation.transformBody entity.spiceName entityRefRame observer tartetRefFrame time 
                    |> Option.map TransformedBody.trafo
                    |> Option.defaultValue Trafo3d.Identity

                return 
                    //unitSphereToRadius * bodyFrameToTargetFrame * bodyToObserver
                    unitSphereToRadius * bodyToObserver
            }


        { trafo = trafo; color = entity.color |> AVal.map C4b }


    let viewWithObserver (cam : aval<Camera>) (observerSpiceBody : aval<EntitySpiceName>) (targetReferenceFrame : aval<Option<FrameSpiceName>>) (time : aval<DateTime>) (showMarkers : aval<bool>) (bodies : aset<EntitySpiceName * AdaptiveEntity>) =

        let renderableBodies =
            bodies |> ASet.chooseA (fun (EntitySpiceName spiceBody, entity) ->
                entity.draw |> AVal.map (function 
                    | true -> (entityToRenderable observerSpiceBody targetReferenceFrame time entity, entity) |> Some
                    | _ -> None
                )
            )



        let markers = 
            showMarkers 
            |> AVal.map (function 
                | true -> 
                    let referenceFrame = targetReferenceFrame |> AVal.map (function None -> "IAU_MARS" | Some (FrameSpiceName m) -> m)
                    let observer = observerSpiceBody |> AVal.map (fun (EntitySpiceName n) -> n)
                    Markers.markers cam referenceFrame observer time
                | false -> Sg.empty
            )
            |> Sg.dynamic
                //let bodyVisualization = Rendering.bodiesVisualization 

        let nonInstancedRendering () =
            let body = 
                //Sg.unitSphere' 12 C4b.White
                IndexedGeometryPrimitives.solidSubdivisionSphere Sphere3d.Unit 5 C4b.White
                |> Sg.ofIndexedGeometry

            // time steps for sampling the past for trajectories. They end at present.
            let timeSteps =
                time
                |> AVal.map (fun endTime -> 
                    let steps = 1000
                    let trajectoryDuration = TimeSpan.FromDays 1.0
                    let startTime = endTime - trajectoryDuration
                    // could be [| startTime .. samplingDistance .. endTime |] if TimeSpan would have get_Zero static member...
                    Array.init steps (fun i -> endTime - ((endTime - startTime) / float steps) * float i) 
                )

            let bodyTrajectoryLines = 
                let targetReferenceFrame = getFrameWithDefaulting targetReferenceFrame
                bodies 
                |> ASet.mapA (fun (entityName, entity) ->

                    entity.showTrajectory |> AVal.map (fun showTrajectory -> 
                        if showTrajectory then
                            // pairwise trajectory points looking into the past....
                            let lineSegments = 
                                (timeSteps, targetReferenceFrame, observerSpiceBody) 
                                |||> AVal.map3 (fun steps targetReferenceFrame observer -> 
                                        steps 
                                        |> Array.choose (fun time -> 
                                            // get virutal body transformation using trajectory timestep.
                                            match CooTransformation.transformBody entityName (Some targetReferenceFrame) observer targetReferenceFrame time with
                                            | None -> 
                                                // happens when body is trafo is not valid for time step (e.g. spice kernel ends to early), skip those.
                                                None
                                            | Some bodyInPast -> 
                                                // just take position, ignore orientation, could be useful for vector representations though.
                                                Some bodyInPast.position
                                        )
                                        |> Array.pairwise
                                        |> Array.map Line3d
                                )
                        
                            Sg.lines (entity.color |> AVal.map C4b) lineSegments
                        else
                            Sg.empty
                    )
                )
                |> Sg.set

            let bodyTrajectory = 
                bodyTrajectoryLines 
                |> Sg.shader {
                    do! Shaders.stableBodyTrafo
                }

            let bodies = 
                renderableBodies 
                |> ASet.map (fun (renderableBody, entity) -> 
                    let texture =
                        entity.textureName 
                        |> AVal.map (fun textureName -> 
                            match textureName with
                            | None -> nullTexture, false
                            | Some n -> 
                                if File.Exists n then
                                    FileTexture(n, true), true
                                else
                                    Log.warn "texture for entity not found: %A, %s" entity.spiceName n 
                                    nullTexture, false
                        )

                    body
                    |> Sg.diffuseTexture (texture |> AVal.map fst)
                    |> Sg.uniform "HasTexture" (texture |> AVal.map snd)
                    |> Sg.uniform "UniformColor" entity.color
                    |> Sg.trafo renderableBody.trafo
                )
                |> Sg.set
                |> Sg.shader {
                    do! Shaders.stableBodyTrafo
                    do! Shaders.color
                    do! Shaders.generateUv
                    do! Shaders.texture
                }
                |> Sg.cullMode' CullMode.Back 

            Sg.ofList [bodies; bodyTrajectory]

        let bodies = 
            nonInstancedRendering () 

        Sg.ofList [bodies; markers]


    let getSpiceReferenceSystemFromSurfaces (s : SurfaceId) (m : HashMap<SurfaceId, GisSurface>)  = 
        match HashMap.tryFind s m with
        | None -> None
        | Some r -> 
            match r.referenceFrame, r.entity with
            | Some referenceFrame, Some entity -> 
                Some { referenceFrame = referenceFrame; body = entity;  }
            | _ -> None

    let getSpiceReferenceSystem (m : GisApp) (s : SurfaceId) = 
        getSpiceReferenceSystemFromSurfaces s m.gisSurfaces 

    let getSpiceReferenceSystemAdaptive (m : AdaptiveGisApp) (s : SurfaceId) =
        m.gisSurfaces.Content |> AVal.map (getSpiceReferenceSystemFromSurfaces s)

    let getSunDirection (m: AdaptiveGisApp) (s : SurfaceId) =
        let observer = m.defaultObservationInfo.observer 
        let observerWithDefault = observer |> AVal.map (Option.defaultValue (EntitySpiceName "mars"))
        let time = m.defaultObservationInfo.time.date
        let targetReferenceFrame = m.defaultObservationInfo.referenceFrame |> AVal.map (Option.defaultValue (FrameSpiceName "IAU_MARS"))
        getSpiceReferenceSystemAdaptive m s 
        |> AVal.bind (function
            | None -> AVal.constant None
            | Some r -> 
                (observerWithDefault, targetReferenceFrame, time) |||> AVal.map3 (fun observer targetReferenceFrame time -> 
                    let bodyPos = CooTransformation.transformBody r.body (Some r.referenceFrame) observer targetReferenceFrame time 
                    let sunPos = CooTransformation.transformBody (EntitySpiceName "SUN") (Some r.referenceFrame) observer targetReferenceFrame time 
                    match sunPos, bodyPos with
                    | Some sunPos, Some bodyPos -> sunPos.position - bodyPos.position |> Vec.normalize |> Some
                    | _ -> None
                )
        )
        
    let getObserver (v : ObservationInfo) =
        match v.observer, v.referenceFrame with
        | Some observer, Some referenceFrame -> Some { body = observer; referenceFrame = referenceFrame; time = v.time.date }
        | _ -> None

    let getObserverSystem (m : GisApp) =
        getObserver m.defaultObservationInfo 

    let getObserverSystemAdaptive (m: AdaptiveGisApp) =
        m.defaultObservationInfo.Current |> AVal.map getObserver


    let lookAtObserver' (observationInfo : ObservationInfo)  =
        match observationInfo.observer, observationInfo.referenceFrame, observationInfo.target with
        | Some observer, Some observerFrame, Some target ->
            Log.line "look at. target: %A, observer: %A, frame: %A" target observer observerFrame
            match CooTransformation.transformBody target (Some observerFrame) observer observerFrame observationInfo.time.date with
            | None -> 
                None
            | Some t -> 
                t.lookAtBody |> Some
        | _ -> None

    let lookAtObserver (m : GisApp) =
        lookAtObserver' m.defaultObservationInfo

    let viewGisEntities (cam : aval<Camera>) (m : AdaptiveGisApp) =
        let observer = m.defaultObservationInfo.observer 
        let observerWithDefault = observer |> AVal.map (Option.defaultValue (EntitySpiceName "mars"))
        let time = m.defaultObservationInfo.time.date
        let targetReferenceFrame = m.defaultObservationInfo.referenceFrame 
        viewWithObserver cam observerWithDefault targetReferenceFrame time m.showMarkers (m.entities |> AMap.toASet)
        |> Sg.onOff (observer |> AVal.map Option.isSome)


    let initial (spiceKernel : option<string>) : GisApp =
        let entities =
            [
                Entity.earth.spiceName,     Entity.earth 
                Entity.mars.spiceName,      Entity.mars 
                Entity.deimos.spiceName,    Entity.deimos 
                Entity.phobos.spiceName,    Entity.phobos 
                Entity.moon.spiceName,      Entity.moon 
                Entity.didymos.spiceName,   Entity.didymos 
                Entity.dimorphos.spiceName, Entity.dimorphos 
                Entity.heraSpacecraft.spiceName, Entity.heraSpacecraft 
            ] |> HashMap.ofList
        let referenceFrames =
            [
                (ReferenceFrame.j2000.spiceName, ReferenceFrame.j2000)
                (ReferenceFrame.eclipJ2000.spiceName, ReferenceFrame.eclipJ2000)
                (ReferenceFrame.iauMars.spiceName, ReferenceFrame.iauMars)
                (ReferenceFrame.iauEarth.spiceName, ReferenceFrame.iauEarth)
            ] |> HashMap.ofList

        let m =
            {
                version                 = GisApp.current
                gisSurfaces             = HashMap.empty
                referenceFrames         = referenceFrames
                entities                = entities
                newEntity               = None
                newFrame                = None
                defaultObservationInfo  = ObservationInfo.initial
                spiceKernel             = spiceKernel |> Option.map CooTransformation.SPICEKernel.ofPath  
                spiceKernelLoadSuccess  = true
                cameraInObserver        = false
                projectedImages         = ProjectedImages.initial //{ ProjectedImages.initial with images = Directory.EnumerateFiles(@"C:\pro3ddata\HERA\simulated") |> Seq.map (fun a -> { fullName = a }) |> IndexList.ofSeq }
                showMarkers             = false
            }

        match spiceKernel with
        | Some spiceKernel ->
            loadSpiceKernel spiceKernel m
        | None ->
            m
