namespace PRo3D.Core.Drawing

open System

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives    

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

module UI =

    let viewAnnotationToolsHorizontal (paletteFile : string) (model:AdaptiveDrawingModel) =
        Html.Layout.horizontal [
            Html.Layout.boxH [ i [clazz "large Write icon"] [] ]
            Html.Layout.boxH [ Html.SemUi.dropDown model.geometry SetGeometry ]
            Html.Layout.boxH [ Html.SemUi.dropDown model.projection SetProjection ]
            Html.Layout.boxH [ ColorPicker.viewAdvanced ColorPicker.defaultPalette paletteFile "pro3d" false model.color |> UI.map ChangeColor; div [] [] ]
            Html.Layout.boxH [ Numeric.view' [InputBox] model.thickness |> UI.map ChangeThickness ]
            Html.Layout.boxH [ i [clazz "large crosshairs icon"] [] ]
            Html.Layout.boxH [ Numeric.view' [InputBox] model.samplingAmount |> UI.map ChangeSamplingAmount ]
            Html.Layout.boxH [ Html.SemUi.dropDown model.samplingUnit SetSamplingUnit ]
        //  Html.Layout.boxH [ Html.SemUi.dropDown model.semantic SetSemantic ]
        ]
                    
    let mkColor (model : AdaptiveGroupsModel) (a : AdaptiveAnnotation) =        
        model.selectedLeaves.Content
            |> AVal.bind (fun selected -> 
                if HashSet.exists (fun x -> x.id = a.key) selected then 
                    AVal.constant C4b.VRVisGreen
                else 
                    a.color.c
            )                              
    
    let isSingleSelect (model : AdaptiveGroupsModel) (a : AdaptiveAnnotation) =
        model.singleSelectLeaf |> AVal.map( fun x -> 
            match x with 
            | Some selected -> selected = a.key
            | None -> false )
    
               
    
    let viewAnnotations (annotations : alist<AdaptiveAnnotation>) : alist<DomNode<Action>> =      
        annotations 
        |> AList.map(fun a ->
            div [clazz "item"] [
                i [clazz "large Sticky Note middle aligned icon"] []
                div [clazz "content"] [
                    div [clazz "header"] [Incremental.text (a.geometry |> AVal.map(string))]
                    div [clazz "description"] [Incremental.text (a.points |> AList.count |> AVal.map(string))]
                ]
            ]
        )
                     
    let viewAnnotationsInGroup 
        (path         : list<Index>) 
        (model        : AdaptiveGroupsModel)
        (singleSelect : AdaptiveAnnotation*list<Index> -> DrawingAction)
        (multiSelect  : AdaptiveAnnotation*list<Index> -> DrawingAction)
        (lift         : GroupsAppAction -> DrawingAction) 
        (annotations  : alist<AdaptiveAnnotation>) 
        : alist<DomNode<DrawingAction>> =
    
        annotations 
        |> AList.map(fun a ->
            
            let singleSelect = fun _ -> singleSelect(a,path)
            let multiSelect  = fun _ -> multiSelect(a,path)
            
            let ac = sprintf "color: %s" (Html.color C4b.White)
            
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

                    let! guh = model.selectedLeaves.Content
                    let! c = mkColor model a
                    let s = style (sprintf "color: %s" (Html.color c))
                    yield s
                } |> AttributeMap.ofAMap
            
            let headerColor = 
                (isSingleSelect model a) 
                |> AVal.map(fun x -> 
                    if x then 
                        C4b.VRVisGreen
                    else
                        C4b.Gray
                    |> Html.color 
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
                            yield div [clazz "header"; style hc] [
                                Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                            ]
                            yield Incremental.i visibleIcon AList.empty 
                                |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"
                            yield 
                                i [clazz "home icon"; onClick (fun _ -> FlyToAnnotation a.key)] [] 
                                    |> UI.wrapToolTip DataPosition.Bottom "FlyTo" 
                        } 
                    )

                    //description
                    let desc = AVal.map2 (fun x y -> sprintf "#Points: %A | taken on: %A" x y) (a.points |> AList.count) a.surfaceName
                    yield div [clazz "description";style ac ] [Incremental.text desc]
                ]
            ]                            
        )     

    let staticClickIcon (icon : string) (toolTipText : string) (onClickAction : 'a) : DomNode<'a> =
        if toolTipText.IsEmptyOrNull() then
            i [clazz icon; onClick (fun _ -> onClickAction)] []
        else
            i [clazz icon; onClick (fun _ -> onClickAction)] [] |> UI.wrapToolTip DataPosition.Bottom toolTipText
                
    let rec viewTree path (group : AdaptiveNode) (model : AdaptiveGroupsModel) (lookup : amap<Guid, AdaptiveAnnotation>) : DomNode<DrawingAction> =
                                                  
        let setActiveAttributes = GroupsApp.setActiveGroupAttributeMap path model group GroupsMessage
                       
        let color = sprintf "color: %s" (Html.color C4b.White)
        let desc =
            div [style color] [       
                Incremental.text group.name
                Incremental.i setActiveAttributes AList.empty 
                |> UI.wrapToolTip DataPosition.Bottom "Set active"
                  
                i [clazz "plus icon"; onMouseClick (fun _ -> GroupsMessage(GroupsAppAction.AddGroup path))] [] 
                |> UI.wrapToolTip DataPosition.Bottom "Add Group"

                staticClickIcon "unhide icon" "Show All" (GroupsMessage(GroupsAppAction.SetVisibility(path,true)))
                staticClickIcon "hide icon"   "Hide All" (GroupsMessage(GroupsAppAction.SetVisibility(path,false)))

                staticClickIcon "bookmark icon"         "Select All"   (GroupsMessage(GroupsAppAction.SetSelection(path,true)))
                staticClickIcon "bookmark outline icon" "Deselect All" (GroupsMessage(GroupsAppAction.SetSelection(path,false)))
            ]
           
        let itemAttributes =
            amap {
                yield onMouseClick (fun _ -> DrawingAction.GroupsMessage(GroupsAppAction.ToggleExpand path))
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
            fun (a:AdaptiveAnnotation,path:list<Index>) -> 
                DrawingAction.GroupsMessage(GroupsAppAction.SingleSelectLeaf (path, a.key, ""))

        let multiSelect = 
            fun (a:AdaptiveAnnotation,path:list<Index>) -> 
                DrawingAction.GroupsMessage(GroupsAppAction.AddLeafToSelection (path, a.key, ""))

        let lift = fun (a:GroupsAppAction) -> (GroupsMessage a)

        let subNodes = 
            group.subNodes 
            |> AList.mapi (fun i v -> viewTree (i::path) v model lookup) 
                    
        let annos = 
            group.leaves 
            |> AList.filterA (fun x -> lookup |> AMap.keys |> ASet.contains x)
            |> AList.map(fun x -> lookup |> AMap.find x |> AVal.force) 
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
          
    let toAdaptiveAnnotation (leaf : AdaptiveLeafCase) =
        match leaf with 
        | AdaptiveAnnotations a -> a
        | _ -> leaf |> sprintf "wrong type %A; expected AdaptiveAnnotations'" |> failwith

    let viewAnnotationGroups (model:AdaptiveDrawingModel) = 
        let a = 
          model.annotations.flat 
            |> AMap.map(fun _ v -> v |> toAdaptiveAnnotation)              
         
        require GuiEx.semui (
            let tree = viewTree [] model.annotations.rootGroup model.annotations a
            //Incremental.div (AttributeMap.ofList [clazz "ui list"]) ([])
            div [clazz "ui list"] [tree]
        )