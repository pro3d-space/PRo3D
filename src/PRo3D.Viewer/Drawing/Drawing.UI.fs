namespace PRo3D.DrawingApp

open System

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open Aardvark.Base.Incremental
open Aardvark.Base.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering.Text
open Aardvark.VRVis
open Aardvark.VRVis.Opc

open Aardvark.UI
open Aardvark.UI.Primitives    
open PRo3D
open PRo3D.Drawing
open PRo3D.Groups
open PRo3D.Base.Annotation

module UI =

    let viewAnnotationToolsHorizontal (model:MDrawingModel) =
        Html.Layout.horizontal [
            Html.Layout.boxH [ i [clazz "large Write icon"][] ]
            Html.Layout.boxH [ Html.SemUi.dropDown model.geometry SetGeometry ]
            Html.Layout.boxH [ Html.SemUi.dropDown model.projection SetProjection ]
            Html.Layout.boxH [ ColorPicker.view model.color |> UI.map ChangeColor; div[][] ]
            Html.Layout.boxH [ Numeric.view' [InputBox] model.thickness |> UI.map ChangeThickness ]
        //  Html.Layout.boxH [ Html.SemUi.dropDown model.semantic SetSemantic ]                
        ]
                    
    let mkColor (model : MGroupsModel) (a : MAnnotation) =
        let color =  
            model.selectedLeaves.Content
            |> Mod.bind (fun selected -> 
                if HRefSet.exists (fun x -> x.id = a.key) selected then 
                    Mod.constant C4b.VRVisGreen
                else a.color.c
            )
            //|> ASet.map(fun x -> x.id = a.key)
            //|> ASet.contains true
            //|> Mod.bind (function x -> if x then Mod.constant C4b.VRVisGreen else a.color.c)
        color              
      
    let mkCubeColor (model : MGroupsModel) (a : MAnnotation) =
        model.selectedLeaves.Content
        |> Mod.bind (fun selected -> 
            if HRefSet.exists (fun x -> x.id = a.key) selected then
                Mod.constant C4b.VRVisGreen
            else a.color.c
           )
        //|> ASet.map(fun x -> x.id = a.key)
        //|> ASet.contains true
        //|> Mod.bind (function x -> if x then Mod.constant C4b.VRVisGreen else a.color.c)
    
    let isSingleSelect (model : MGroupsModel) (a : MAnnotation) =
        model.singleSelectLeaf |> Mod.map( fun x -> 
            match x with 
            | Some selected -> selected = a.key
            | None -> false )
    
    let annotationMenu = //todo move to viewer io gui
        div [ clazz "ui dropdown item"] [
            text "Annotations"
            i [clazz "dropdown icon"][] 
            div [ clazz "menu"] [                    
                div [
                    clazz "ui inverted item"
                    Dialogs.onChooseFiles AddAnnotations
                    clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onchoose', top.aardvark.dialog.showOpenDialog({filters: [{ name: 'Annotations (*.ann)', extensions: ['ann']},],properties: ['openFile']}));" )
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
                    clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onsave', top.aardvark.dialog.showSaveDialog({filters: [{ name: 'Annotations (*.pro3d.ann)', extensions: ['pro3d.ann']}], options: [{title: 'Save Measurements as .csv'}]}));")
                ][
                    text "Export (*.pro3d.ann)"
                ]
                div [ 
                    clazz "ui inverted item"
                    Dialogs.onSaveFile ExportAsCsv
                    clientEvent "onclick" ("top.aardvark.processEvent('__ID__', 'onsave', top.aardvark.dialog.showSaveDialog({filters: [{ name: 'Annotations (*.csv)', extensions: ['csv']}], options: [{title: 'Save Measurements as .csv'}]}));")
                ][
                    text "Export (*.csv)"
                ]     
            ]
        ]                    
    
    let viewAnnotations (annotations : alist<MAnnotation>) : alist<DomNode<Action>> =      
        annotations 
        |> AList.map(fun a ->
            div [clazz "item"] [
                i [clazz "large Sticky Note middle aligned icon"][]
                div [clazz "content"] [
                    div [clazz "header"][Incremental.text (a.geometry |> Mod.map(string))]
                    div [clazz "description"][Incremental.text (a.points |> AList.count |> Mod.map(string))]
                ]
            ]
        )
                     
    let viewAnnotationsInGroup 
        (path         : list<Index>) 
        (model        : MGroupsModel)
        (singleSelect : MAnnotation*list<Index> -> 'outer)
        (multiSelect  : MAnnotation*list<Index> -> 'outer)
        (lift         : GroupsAppAction -> 'outer) 
        (annotations  : alist<MAnnotation>) 
        : alist<DomNode<'outer>> =
    
        annotations 
        |> AList.map(fun a ->
            
            let singleSelect = fun _ -> singleSelect(a,path)
            let multiSelect  = fun _ -> multiSelect(a,path)
            
            let ac = sprintf "color: %s" (Html.ofC4b C4b.White)
            
            let visibleIcon = 
                amap {
                    yield onMouseClick (fun _ -> lift <| GroupsAppAction.ToggleChildVisibility (a.key,path))
                    let! visible = a.visible
                    if visible then 
                        yield clazz "unhide icon" 
                    else 
                        yield clazz "hide icon"
                } |> AttributeMap.ofAMap

            let iconAttributes =
                amap {                  
                    yield clazz "cube middle aligned icon"
                    yield onClick (multiSelect)

                    let! c = mkCubeColor model a
                    yield style (sprintf "color: %s" (Html.ofC4b c))
                } |> AttributeMap.ofAMap
            
            let headerColor = 
                (isSingleSelect model a) 
                |> Mod.map(fun x -> 
                    if x then 
                        C4b.VRVisGreen
                    else
                        C4b.Gray
                    |> Html.ofC4b 
                    |> sprintf "color: %s"
                ) 

            let headerAttributes =
                amap {
                    yield onClick singleSelect
                    //let! selected = isSingleSelect model a
                    //yield if selected then style ("text-transform:uppercase") else style "text-transform:none"
                } |> AttributeMap.ofAMap

            let headerText = 
                adaptive {
                    let! geometry = a.geometry
                    let! semantic = a.semanticId
                    let! semanticType = a.semanticType

                    return 
                        match semanticType with
                        | SemanticType.Undefined -> 
                            geometry  |> sprintf "%A"
                        | _ -> 
                            let (SemanticId s) = semantic
                            s
            
                }
            
            div [clazz "item"] [
                Incremental.i iconAttributes AList.empty
                div [clazz "content"] [                  
                
                    //header
                    yield Incremental.div (AttributeMap.ofList [style ac]) (
                        alist {                          
                            //yield div[][
                            let! hc = headerColor
                            yield div[clazz "header"; style hc][
                                Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                            ]
                            yield Incremental.i visibleIcon AList.empty |> UI.wrapToolTipBottom "Toggle Visible"
                            yield i [clazz "binoculars icon"; onClick (fun _ -> FlyToAnnotation a.key)][] |> UI.wrapToolTipBottom "FlyTo"
                        } 
                    )

                    //description
                    let desc = Mod.map2 (fun x y -> sprintf "#Points: %A | taken on: %A" x y) (a.points |> AList.count) a.surfaceName
                    yield div [clazz "description";style ac ] [Incremental.text desc]
                ]
            ]                            
        )     

    let staticClickIcon (icon : string) (toolTipText : string) (onClickAction : 'a) : DomNode<'a> =
        if toolTipText.IsEmptyOrNull() then
            i [clazz icon; onClick (fun _ -> onClickAction)] []
        else
            i [clazz icon; onClick (fun _ -> onClickAction)] [] |> UI.wrapToolTipBottom toolTipText
                
    let rec viewTree path (group : MNode) (model : MGroupsModel) (lookup : amap<Guid, MAnnotation>) : DomNode<Action> =
                                                  
        let activeIcon =
            adaptive {
                let! s = model.activeGroup    
                let active = s.id
                let! group  =  group.key

                return if (active = group) then "circle icon" else "circle thin icon"
            }
                                      
        let setActiveAction = 
            GroupsAppAction.SetActiveGroup (group.key |> Mod.force, path, group.name |> Mod.force)
            |> GroupsMessage

        let setActiveAttributes = GroupsApp.clickIconAttributes activeIcon setActiveAction
                       
        let color = sprintf "color: %s" (Html.ofC4b C4b.White)
        let desc =
            div [style color] [       
                Incremental.text group.name
                Incremental.i setActiveAttributes AList.empty 
                |> UI.wrapToolTipBottom "Set active"
                  
                i [clazz "plus icon"; onMouseClick (fun _ -> GroupsMessage(GroupsAppAction.AddGroup path))] [] 
                |> UI.wrapToolTipBottom "Add Group"

                staticClickIcon "unhide icon" "Show All" (GroupsMessage(GroupsAppAction.SetVisibility(path,true)))
                staticClickIcon "hide icon"   "Hide All" (GroupsMessage(GroupsAppAction.SetVisibility(path,false)))

                staticClickIcon "bookmark icon"         "Select All"   (GroupsMessage(GroupsAppAction.SetSelection(path,true)))
                staticClickIcon "bookmark outline icon" "Deselect All" (GroupsMessage(GroupsAppAction.SetSelection(path,false)))
            ]
           
        let itemAttributes =
            amap {
                yield onMouseClick (fun _ -> Action.GroupsMessage(GroupsAppAction.ToggleExpand path))
                let! expanded = group.expanded
                if expanded then 
                    yield clazz "icon outline open folder"
                else 
                    yield clazz "icon outline folder"
                    yield style "overflow-y : visible"
            } |> AttributeMap.ofAMap
          
        let childrenAttribs =
            amap {
                yield clazz "list"
                let! isExpanded = group.expanded
                if (not isExpanded) then 
                    yield style "display:none"                             
            }         

        let singleSelect = 
            fun (a:MAnnotation,path:list<Index>) -> 
                Action.GroupsMessage(GroupsAppAction.SingleSelectLeaf (path, a.key, ""))

        let multiSelect = 
            fun (a:MAnnotation,path:list<Index>) -> 
                Action.GroupsMessage(GroupsAppAction.AddLeafToSelection (path, a.key, ""))

        let lift = fun (a:GroupsAppAction) -> (GroupsMessage a)

        let subNodes = 
            group.subNodes 
            |> AList.mapi (fun i v -> viewTree (i::path) v model lookup) 
                    
        let annos = 
            group.leaves 
            |> AList.filterM(fun x -> lookup |> AMap.keys |> ASet.contains x)
            |> AList.map(fun x -> lookup |> AMap.find x |> Mod.force) 
            |> viewAnnotationsInGroup path model singleSelect multiSelect lift

        let nodes = annos |> AList.append subNodes

        div [ clazz "item"] [
            Incremental.i itemAttributes AList.empty
            div [ clazz "content" ] [
                div [ clazz "description noselect"] [desc]
                Incremental.div (AttributeMap.ofAMap childrenAttribs) <|
                    alist {
                        let! expanded = group.expanded
                        if expanded then yield! nodes
                        else ()
                    } //(nodes)
            ]
        ]
          
    let toMAnnotation (leaf : MLeaf) =
        match leaf with 
        | MAnnotations a -> a
        | _ -> leaf |> sprintf "wrong type %A; expected MAnnotations'" |> failwith

    let viewAnnotationGroups (model:MDrawingModel) = 
        let a = 
          model.annotations.flat 
            |> AMap.map(fun _ v -> (v |> Mod.force) |> toMAnnotation)              
         
        require GuiEx.semui (
            let tree = viewTree [] model.annotations.rootGroup model.annotations a
            //Incremental.div (AttributeMap.ofList [clazz "ui list"]) ([])
            div [clazz "ui list"] [tree]
        )