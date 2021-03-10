namespace PRo3D.Core

open System

open Aardvark.Base
open Aardvark.Application
open Aardvark.Geometry
open Aardvark.UI
open Aardvark.UI.Trafos
open FSharp.Data.Adaptive
open Aardvark.SceneGraph
open Aardvark.VRVis.Opc

open PRo3D.Base
open PRo3D
//open PRo3D.Versioned

type GroupsAppAction =
    | ToggleExpand          of list<Index>
    | SetActiveGroup        of Guid*list<Index>*string
    | AddGroup              of list<Index>
    | AddLeaves             of list<Index>*IndexList<Leaf>    
    | RemoveGroup           of list<Index>
    | RemoveLeaf            of Guid*list<Index> 
    | ToggleChildVisibility of Guid*list<Index> 
    | ActiveChild           of Guid*list<Index>*string
    | UpdateLeafProperties  of Leaf
    | SetGroupName          of string
    | SetChildName          of string
    | ClearGroup            of list<Index>
    | AddLeafToSelection    of list<Index>*Guid*string 
    | SingleSelectLeaf      of list<Index>*Guid*string 
    | ToggleGroup           of list<Index>
    | SetVisibility         of path : list<Index> * isVisible : bool
    | SetSelection          of path : list<Index> * isSelected : bool
    | MoveLeaves      
    | AddAndSelectGroup     of list<Index> * Node
    | ClearSnapshotsGroup
    | ClearSelection
    | UpdateCam             of Guid
    | Nop

module GroupsApp =
                                        
    let getNode (path : list<Index>) (root : Node) : Node =
        let rec goDeeper (p : list<Index>) (node : Node) =
            match p with 
            | [] -> 
                Log.warn "found %A" node.name
                node
            | x :: rest ->
                Log.warn "visiting %A" x
                match IndexList.tryGet x node.subNodes with
                | Some n -> goDeeper rest n
                | None   -> Log.error "Index list does not match tree"; node

        goDeeper (List.rev path) root

    let collectLeaves (target : Node) =
        let rec loop (target : Node) =
            let leafIds = target.leaves |> IndexList.toList
            
            let subs = 
                target.subNodes 
                |> IndexList.toList 
                |> List.collect(fun x -> loop x)

            List.concat [leafIds; subs]

        loop target |> IndexList.ofList

    let updateLeaf' id f model =
        let update = 
            (fun (d:option<Leaf>) ->
                match d with 
                | Some k -> Some (f k)
                | None   -> None )
          
        HashMap.alter id update model.flat    
        
    let updateLeaf id f model =
        let flat' = updateLeaf' id f model
        { model with flat = flat' }

    let replaceLeaf (l:Leaf) (m:GroupsModel) : GroupsModel =
        let f = (fun _ -> l)
        updateLeaf l.id f m        
   
    let createUpdate f (m:GroupsModel) id =
        match (m.flat |> HashMap.tryFind id) with
        | Some leaf -> HashMap.single leaf.id (f leaf)
        | None      -> HashMap.empty

    let updateLeaves leaves f model =
        let flat =
            leaves
            |> IndexList.toList
            |> List.map  (createUpdate f model)
            |> List.fold (fun a b -> HashMap.union a b) model.flat

        { model with flat = flat }

    
    let updateNodeAt (p : list<Index>) (f : Node -> Node) (t : Node) = 
        let rec go (p : list<Index>) (t : Node) =
            match p with
            | [] -> f t
            | x::rest ->
                match IndexList.tryGet x t.subNodes with
                | Some c -> { t with subNodes = IndexList.set x (go rest c) t.subNodes } //potentially slow                              
                | None   -> t
        go (List.rev p) t 
    
    let updateGroupsLookup (m:GroupsModel) =
        let rec go (n:Node) =
            let looks = n.leaves |> IndexList.map( fun x -> (x, n.name))
            let subs = 
                n.subNodes |> IndexList.collect(fun x -> go x)
            IndexList.concat [looks; subs]

        (go m.rootGroup)
        |> IndexList.toList
        |> HashMap.ofList                 
    
    let updateActiveGroup (f : Node -> Node) (m : GroupsModel) =
        let root = updateNodeAt m.activeGroup.path f m.rootGroup        

        { m with rootGroup = root }
    
    let updateStructureAt (p : list<Index>) (f : Node -> Guid -> Node) (t : Node) = 
        let rec go (p : list<Index>) (t : Node)  =
            match p with
            | x::rest -> 
                match rest with
                | [] -> 
                    match IndexList.tryGet x t.subNodes with
                    | Some c -> f t c.key
                    | None -> t
                | _ -> 
                    match IndexList.tryGet x t.subNodes with
                    | Some c -> { t with subNodes = IndexList.set x (go rest c) t.subNodes }
                    | None   -> t
            | _ -> t
        go (List.rev p) t 
        
    let removeGroup (ag:Node) (id:Guid) =
        ag.subNodes |> IndexList.filter (fun x -> x.key <> id)           

    let rec repairGroupNodesGuid (tree : Node) : Node =

        let subNodes = 
            tree.subNodes
            |> IndexList.map repairGroupNodesGuid

        { tree with key = Guid.NewGuid(); subNodes = subNodes }

    // add one child to children of active group ... maybe move to active group
    let addLeafToActiveGroup (leaf:Leaf) (select:bool) model=

        let treeSelection =  
            {
                id = leaf.id
                path = model.activeGroup.path
                name = ""
            }

        let model = 
            { model with 
                flat = model.flat |> HashMap.add leaf.id leaf; 
                singleSelectLeaf = if select then Some leaf.id else None
                //selectedLeaves = model.selectedLeaves |> HashSet.add treeSelection
            }

        let func = (fun (x : Node) -> { x with leaves = x.leaves |> IndexList.prepend leaf.id })
        model |> updateActiveGroup func

    // add a list of children to children of active group
    let addLeaves path (cs : IndexList<Leaf>) model = 
        let f = (fun (x:Node) -> 
            { x with leaves = x.leaves |> IndexList.append (cs |> IndexList.map (fun x -> x.id)) })

        //add ids to group
        let root' = updateNodeAt path f model.rootGroup

        //add child nodes to flat
        let cs = 
            cs |> IndexList.toList |> List.map(fun x -> (x.id,x))|> HashMap.ofList
        
        { model with rootGroup = root'; flat = model.flat |> HashMap.union cs }    
        
    // add a list of children to the "snapshots" group
    let addLeavesToSnapshots (cs : IndexList<Leaf>) model = 

        //add ids to group
        let node = 
            model.rootGroup.subNodes 
            |> IndexList.toList
            |> List.find(fun x -> x.name = "snapshots")

         //add child nodes to flat
        let cs' = cs |> IndexList.toList |> List.map(fun x -> (x.id,x))|> HashMap.ofList

        let t = [{node with leaves = node.leaves |> IndexList.append (cs |> IndexList.map (fun x -> x.id)) }] |> IndexList.ofList
        let root' = {model.rootGroup with subNodes = t}
        
        { model with rootGroup = root'; flat = model.flat |> HashMap.union cs' }    
               
    let removeFromSelection id path model =
        { model with selectedLeaves = model.selectedLeaves |> HashSet.remove( { id = id; path = path; name = "" } ) }
    
    let removeLeaf model (id:Guid) path removeFromFlat =        
        let func = (fun (x:Node) -> 
            Log.warn "[GroupsApp] removing id %A from group %A" id x.name
            { x with leaves = x.leaves |> IndexList.filter (fun id' -> id' <> id) }
        ) // TODO v5: check
        let root' = updateNodeAt path func model.rootGroup

        let m = model |> removeFromSelection id path

        if removeFromFlat then
            let flat' = m.flat |> HashMap.remove id
            { m with rootGroup = root'; flat = flat'; singleSelectLeaf = None }
        else
            { m with rootGroup = root'; singleSelectLeaf = None } 

    let rec removeSelected (selection:list<TreeSelection>) (removeFromFlat:bool) (m:GroupsModel)  =
        match selection with
        | x::rest ->                
            let m' = removeLeaf m x.id x.path removeFromFlat
            removeSelected rest removeFromFlat m'
        |_ -> m
                    
    let moveChildren m =
       
        let toMove = 
            m.selectedLeaves
            |> HashSet.choose (fun x -> m.flat |> HashMap.tryFind x.id)
            |> HashSet.toSeq
            |> IndexList.ofSeq
                
        // * delete leaves in source group
        // * add leaves to destination group
        let m = 
            m
            |> removeSelected (m.selectedLeaves |> HashSet.toList) false
            |> updateActiveGroup (fun (x:Node) -> 
                let ids = (toMove |> IndexList.map(fun x -> x.id))
                { x with leaves = x.leaves |> IndexList.append ids }
            )
         
        // update selection paths
        let sel = (m.selectedLeaves |> HashSet.map( fun x -> {x with path = m.activeGroup.path }))
        { m with selectedLeaves = sel}        

    let checkSelection model =
        model.selectedLeaves 
        |> HashSet.map(fun x -> if model.flat.ContainsKey x.id then Some x else None)
        |> HashSet.choose(fun x -> x)   

    let checkLastSelected model = 
        match model.singleSelectLeaf with
        | Some x -> 
            if (model.selectedLeaves |> HashSet.filter ( fun y -> y.id = x)).IsEmpty then 
                None
            else
                Some x
        | _ -> 
            None 

    let addSingleSelectedLeaf (model:GroupsModel) (p : list<Index>) (id : Guid) (s: string) = 
        let treeSelection = { id = id; path = p; name = s }
        let single = 
            match model.singleSelectLeaf with
            | Some s -> if s = id then None else Some id
            | None -> Some id

        { model with 
            singleSelectLeaf = single; 
            activeChild      = treeSelection; 
            lastSelectedItem = SelectedItem.Child
            }
            
    let createEmptyGroup () = 
        {    
            version   = Node.current
            name      = "newGroup" 
            key       = Guid.NewGuid()
            leaves    = IndexList.Empty
            subNodes  = IndexList.Empty
            visible   = true
            expanded  = true
        }    

    let insertGroup path (group : Node) (model : GroupsModel) =         

        let func = 
            (fun (x:Node) -> 
                { x with subNodes = IndexList.add group x.subNodes })

        { model with rootGroup = updateNodeAt path func model.rootGroup }

    let union (left : GroupsModel) (right : GroupsModel) : GroupsModel =
        if left.rootGroup.key = right.rootGroup.key then
            Log.line "[Groups] left and right group are identical"
            left
        else
            let merged = 
                left 
                |> insertGroup List.empty right.rootGroup
            
            { merged with
                flat = left.flat |> HashMap.union right.flat
                groupsLookup = left.groupsLookup |> HashMap.union right.groupsLookup
            }    

    let update (model : GroupsModel) (action : GroupsAppAction) =
        match action with
        | SetActiveGroup (g, p, s) -> 
            let selection = { id = g; path = p; name = s}

            { model with 
                activeGroup      = selection
                lastSelectedItem = SelectedItem.Group
                } 
        | ActiveChild (g, p, s) -> 
            { model with
                activeChild = { id = g; path = p; name = s }
                lastSelectedItem = SelectedItem.Child
                } 
        | ToggleExpand p -> 
            let func = (fun (x:Node) -> { x with expanded = not x.expanded })
            { model with rootGroup = updateNodeAt p func model.rootGroup }
        | AddGroup p -> 
            insertGroup p (createEmptyGroup()) model
        | RemoveGroup p -> 
            
            //delete from flat
            let flat' = 
                model.rootGroup 
                  |> getNode p
                  |> collectLeaves                
                  |> IndexList.toList 
                  |> List.fold (fun rest k -> HashMap.remove k rest) model.flat

            //delet from hierarchy                                
            let func = 
                fun (x:Node) (id:Guid) -> { x with subNodes = removeGroup x id }

            let root' = updateStructureAt p func model.rootGroup
            let m' = { model with rootGroup = root'; flat = flat' }

            let selection = checkSelection m'
            let last = checkLastSelected m'

            // update selection
            let newSelection = {
                id   = m'.rootGroup.key
                path = list.Empty
                name = m'.rootGroup.name}
           
            { m' with selectedLeaves = selection ; singleSelectLeaf = last; activeGroup = newSelection; }

        | AddLeaves (p,cs) ->
            failwith "addchildren"
            //AddChildren p cs model
            model
        | RemoveLeaf (k,p) ->
            removeLeaf model k p true
        | ToggleChildVisibility (c,p) -> 
            model |> Groups.updateLeaf c (fun x -> Leaf.toggleVisibility x)
        | UpdateLeafProperties a ->
            Groups.replaceLeaf a model
        | SetGroupName t -> 
            let func = 
                fun (x:Node) -> { x with name = t }
          
            { model with 
                rootGroup   = updateNodeAt model.activeGroup.path func model.rootGroup
                activeGroup = { model.activeGroup with name = t }
            }
        | SetChildName t -> 
            match model.singleSelectLeaf with
            | Some id -> model |> Groups.updateLeaf id (fun x -> Leaf.setName x t)
            | None -> model
        | ClearGroup p -> 
            //failwith "clear group"

            //delete from flat
            let flat' = 
                model.rootGroup 
                  |> getNode p
                  |> collectLeaves                
                  |> IndexList.toList 
                  |> List.fold (fun rest k -> HashMap.remove k rest) model.flat

            //delet from hierarchy                                
            let func = 
                fun (x:Node) -> { x with leaves = IndexList.Empty }

            let root' = updateNodeAt p func model.rootGroup
            let m' = { model with rootGroup = root'; flat = flat' }

            let selection = checkSelection m'
            let last = checkLastSelected m'

            { m' with selectedLeaves = selection ; singleSelectLeaf = last }
        | SingleSelectLeaf (p,id,s) ->
            addSingleSelectedLeaf model p id s
        | AddLeafToSelection (p,id,s) ->
            let incomingSelection = {   // CHECK-merge (treeSelection)
                id = id
                path = p
                name = s
            }
            
            if HashSet.contains incomingSelection model.selectedLeaves
            then 
                let a = HashSet.remove incomingSelection model.selectedLeaves
                                            
                let singleSelect = 
                    match model.singleSelectLeaf with
                    | Some single when single = incomingSelection.id -> None
                    | _ -> model.singleSelectLeaf

                { 
                    model with 
                        singleSelectLeaf = singleSelect
                        //activeChild      = incomingSelection
                      //  lastSelectedItem = SelectedItem.Child 
                        selectedLeaves = a
                }                    
            else
                let b = HashSet.add incomingSelection model.selectedLeaves //HashSet.empty

                { 
                    model with 
                        singleSelectLeaf = incomingSelection.id |> Some
                        activeChild      = incomingSelection
                        lastSelectedItem = SelectedItem.Child 
                        selectedLeaves = b
                }                                                                          
        | ToggleGroup p ->
            
            let leaves = 
                getNode p model.rootGroup |> collectLeaves

            let func = fun (x:Node) -> { x with visible = not x.visible }
            let m' = {model with rootGroup = updateNodeAt p func model.rootGroup }

            let f = (fun (k:Leaf) -> (k.setVisible (not k.visible)))
            updateLeaves leaves f m'
        | SetVisibility (p, isVisible) ->    
            let leaves = 
                  getNode p model.rootGroup |> collectLeaves

            let func = fun (x:Node) -> { x with visible = isVisible }
            let m' = {model with rootGroup = updateNodeAt p func model.rootGroup }

            let f = (fun (k:Leaf) -> (k.setVisible isVisible))
            updateLeaves leaves f m'              
        | SetSelection (p, isSelected) ->    
            let node = 
                getNode p model.rootGroup 
                //|> collectLeaves
            let leaves =
                node.leaves
                |> IndexList.toList
                |> List.map(fun x -> 
                    {
                        id = x
                        path = p
                        name = ""
                    }
                ) |> HashSet.ofList

            if isSelected then
                { model with selectedLeaves = HashSet.union model.selectedLeaves leaves }
            else
                { model with selectedLeaves = HashSet.difference model.selectedLeaves leaves }        
        | MoveLeaves  -> 
            moveChildren model
        | ClearSelection ->
            { model with selectedLeaves = HashSet.Empty; } //singleSelectLeaf = None
        | UpdateCam id -> 
            Log.warn "[Groups] flyto %A not handled in higher level app" id
            model
        | AddAndSelectGroup (p,node) ->
            let t = insertGroup p node model   
            let selection = { id = node.key; path = p; name = node.name}

            { t with 
                activeGroup      = selection
                lastSelectedItem = SelectedItem.Group } 
        | ClearSnapshotsGroup -> 

            let node = 
                model.rootGroup.subNodes 
                |> IndexList.toList
                |> List.find(fun x -> x.name = "snapshots")

            let leaves =
                node
                |> collectLeaves   
                
            let flat'=
                leaves
                |> IndexList.toList 
                |> List.fold (fun rest k -> HashMap.remove k rest) model.flat
            
            
            let t = [{node with leaves = IndexList.Empty}] |> IndexList.ofList
            //let newGroup = [{newGroup with name = "snapshots"}]|> IndexList.ofList
            let root' = {model.rootGroup with subNodes = t}
            
            let m' = { model with rootGroup = root'; flat = flat' }
            let selection = checkSelection m'
            let last = checkLastSelected m'

            { m' with selectedLeaves = selection ; singleSelectLeaf = last }
        | _ -> 
            model
    
    let mkColor (activeId : Guid) (groupId : Guid) = 
        if activeId = groupId then AVal.constant C4b.VRVisGreen else AVal.constant C4b.White
    
    let clickIconAttributes icon action =
        amap {
            let! icon = icon
            yield clazz icon
            yield onClick (fun _ -> action)
        } |> AttributeMap.ofAMap

    let viewSelectionButtons =
        // Html.table [
        div[][
            div [clazz "ui buttons inverted"] [
                
                button [clazz "ui icon button"; attribute "data-content" "Move Selection"; onMouseClick (fun _ -> MoveLeaves)] [
                    i [clazz "Move icon"] [] ] |> UI.wrapToolTip DataPosition.Top "Move Selection"                
            ]
            div [clazz "ui buttons inverted"] [                
                button [clazz "ui icon button"; attribute "data-content" "Clear Selection"; onMouseClick (fun _ -> ClearSelection)] [
                            i [clazz "Remove icon"] [] ] |> UI.wrapToolTip DataPosition.Top "Clear Selection"                
            ] 
        ]

    let viewUI (model : AdaptiveGroupsModel) =   
        let ts = model.activeGroup
        require GuiEx.semui (
            Html.table [                            
                Html.row "Change Groupname:"[Html.SemUi.textBox (ts |> AVal.map (fun x -> x.name)) SetGroupName ]
                //div [clazz "ui buttons inverted"] [
                //    onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                //        button [clazz "ui icon button"; attribute "data-content" "Remove Group"; onMouseClick (fun _ -> RemoveGroup (ts |> AVal.map (fun x -> x.path) |> AVal.force))] [ //
                //                i [clazz "remove icon red"] [] ] 
                //    )
                //] 
            ]
        )

    let viewSelected (view : AdaptiveLeafCase -> DomNode<'a>) (lifter : 'a -> 'b) (model : AdaptiveGroupsModel) : aval<DomNode<'b>> = 
        adaptive {
            let! selected = model.singleSelectLeaf
            match selected with
                | Some guid -> 
                    let! exists = model.flat |> AMap.keys |> ASet.contains guid
                    if exists then
                      let item = 
                        model.flat |> AMap.find guid |> AVal.force // TODO v5: to - wrong adaptive, where was bind on id - why?
                      return view item |> UI.map lifter
                    else return div[][] |> UI.map lifter
                | None ->
                    return div[][] |> UI.map lifter
        }

    let deleteLeaf (ts:TreeSelection) =
        Html.table [                            
                div [clazz "ui buttons inverted"] [
                    onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                        button [clazz "ui icon button"; onMouseClick (fun _ -> RemoveLeaf (ts.id,ts.path))] [
                                i [clazz "remove icon red"] [] ] |> UI.wrapToolTip DataPosition.Right "Remove"
                    )
                ] 
            ]

    let deleteClearGroup (ts:TreeSelection) =
        Html.table [                            
                div [clazz "ui buttons inverted"] [
                    //onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                        button [clazz "ui icon button"; attribute "data-content" "Remove Group"; onMouseClick (fun _ -> RemoveGroup ts.path)] [ //
                                i [clazz "remove icon red"] [] ] |> UI.wrapToolTip DataPosition.Right "Remove Group"
                    //)
                ] 
                div [clazz "ui buttons inverted"] [
                    //onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                        button [clazz "ui icon button"; attribute "data-content" "Clear Group"; onMouseClick (fun _ -> ClearGroup ts.path)] [ //
                                i [clazz "remove circle icon"] [] ] |> UI.wrapToolTip DataPosition.Right "Clear Group"
                    //)
                ] 
            ]

    let viewGroupButtons (ts:TreeSelection)  =
        require GuiEx.semui (
                Html.table [
                     Html.row "Remove/Clear:"[deleteClearGroup ts]
                     Html.row "Selection:"[ viewSelectionButtons ]
                ]
            )

    let viewLeafButtons (ts:TreeSelection)  =
        require GuiEx.semui (
                Html.table [
                     Html.row "Remove:"[deleteLeaf ts]
                     Html.row "Selection:"[ viewSelectionButtons ]                     
                ]
            )

    let showNothing : DomNode<Action> = 
        require GuiEx.semui (div [][])

