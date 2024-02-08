namespace PRo3D.Core.Gis


open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
open PRo3D.Core.Surface
open PRo3D.Base.GisModels
open Aether

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module GisApp = 
    let update (m : GisApp) 
               (lenses : GisAppLenses<'viewer>)
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
        | GisAppAction.Observe ->
            viewer, m
        | GisAppAction.AssignBody body ->
            viewer, m
        | GisAppAction.AssignReferenceFrame frame ->
            viewer, m

    let private viewSurfaces (m : AdaptiveGisApp) (surfaces : AdaptiveGroupsModel)  =
        let surfaceNames = 
            surfaces.flat 
            |> PRo3D.SurfaceUtils.toAvalSurfaces
            |> AMap.map (fun key value -> PRo3D.SurfaceUtils.toName value)
        let surfacesList =
            surfaceNames
            |> AMap.toASet
            |> ASet.toAList


        require GuiEx.semui (
            Incremental.div 
              (AttributeMap.ofList [clazz "ui celled list"]) 
              AList.empty      
        ) 

    let viewSurfacesInGroups 
        (path         : list<Index>) 
        (model        : AdaptiveGroupsModel) 
        (lift         : GroupsAppAction -> SurfaceAppAction) 
        (surfaceIds   : alist<System.Guid>) : alist<DomNode<SurfaceAppAction>> =
        alist {
            let surfaces = 
                surfaceIds 
                |> AList.chooseA (fun x -> model.flat |> AMap.tryFind x)
            
            let surfaces = 
                surfaces
                |> AList.choose(function | AdaptiveSurfaces s -> Some s | _-> None )
            
            for s in surfaces do 
                let! c = SurfaceApp.mkColor model s
                let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
            
                let! key = s.guid                                                                             
              
                let headerText = 
                    AVal.map (fun a -> sprintf "%s" a) s.name

                let bgc = sprintf "color: %s" (Html.ofC4b c)
                yield div [clazz "item"; style infoc] [
                    i [clazz "cube middle aligned icon"; style bgc] [] 
                    div [clazz "content"; style infoc] [                     
                        yield Incremental.div (AttributeMap.ofList [style infoc])(
                            alist {
                                yield div [clazz "header";] [
                                   Incremental.span AttributeMap.empty 
                                                    ([Incremental.text headerText] |> AList.ofList)
                                   i [clazz "home icon"; 
                                      onClick (fun _ -> FlyToSurface key)
                                      style "margin-left: 0.2rem"] [] 
                                        |> UI.wrapToolTip DataPosition.Bottom "Fly to surface"   
                                ]                                  
                            } 
                        )                                     
                    ]
                ]
        }

    let rec viewTree path (group : AdaptiveNode) 
                     (model : AdaptiveGroupsModel) : alist<DomNode<SurfaceAppAction>> =

        alist {
            let! s = model.activeGroup
            let color = sprintf "color: %s" (Html.ofC4b C4b.White)                
            let children = AList.collecti (fun i v -> viewTree (i::path) v model) group.subNodes    
            let activeAttributes = GroupsApp.setActiveGroupAttributeMap path model group GroupsMessage
                                   
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
                div [ clazz "content" ] [                         
                    div [ clazz "description noselect"] [desc]
                    Incremental.div (AttributeMap.ofAMap childrenAttribs) (                          
                        alist { 
                            let! isExpanded = group.expanded
                            if isExpanded then yield! children
                            
                            if isExpanded then 
                                yield! viewSurfacesInGroups 
                                        path model lift group.leaves
                        }
                    )  
                ]
            ]
                
        }

    let viewSurfacesGroupsGis (surfaces:AdaptiveGroupsModel) = 
        require GuiEx.semui (
            Incremental.div 
              (AttributeMap.ofList [clazz "ui inverted celled list"]) 
              (viewTree [] surfaces.rootGroup surfaces)            
        )    
        |> UI.map GisAppAction.SurfacesMessage

    let view (m : AdaptiveGisApp) (surfaces : AdaptiveSurfaceModel) =  
        div [] [ 
            yield GuiEx.accordion "Surfaces" "Cubes" false 
                                  [ viewSurfacesGroupsGis surfaces.surfaces]
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
            [
                (Spacecraft.heraSpacecraft.spiceName, Spacecraft.heraSpacecraft)
            ] |> HashMap.ofList

        {
            bodies = bodies
            referenceFrames = referenceFrames
            spacecraft = spacecraft
            entities = HashMap.empty
        }