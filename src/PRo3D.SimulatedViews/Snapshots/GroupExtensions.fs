namespace PRo3D.Core

open System

open Aardvark.Base
open PRo3D.Core.GroupsApp
open Aardvark.Application
open Aardvark.Geometry
open Aardvark.UI
open Aardvark.UI.Trafos
open FSharp.Data.Adaptive
open Aardvark.SceneGraph
open Aardvark.VRVis.Opc

open PRo3D.Base
open PRo3D

module GroupsApp =
    let clearGroupAtRoot (model : GroupsModel) (groupName : string) =
        let node = 
            model.rootGroup.subNodes 
            |> IndexList.toList
            |> List.tryFind(fun x -> x.name = groupName)
        match node with
        | Some node ->
            let leaves =
                node
                |> collectLeaves   
            let flat=
                leaves
                |> IndexList.toList 
                |> List.fold (fun rest k -> HashMap.remove k rest) model.flat
 
            let t = [{node with leaves = IndexList.Empty}] |> IndexList.ofList
            let root = {model.rootGroup with subNodes = t}
            let m = { model with rootGroup = root; flat = flat }
            m
        | None -> model

    let addGroupToRoot (m : GroupsModel) (name : string) =
        let existingGroup = 
            m.rootGroup.subNodes 
                |> IndexList.toList
                |> List.tryFind(fun x -> x.name = name)
        match existingGroup with
        | Some n -> m
        | None -> 
            let newGroup = {createEmptyGroup () with name = name}
            let msg = GroupsAppAction.AddAndSelectGroup ([],newGroup)
            let surfModel = update m msg
            surfModel

    let removeLeavesFromGroup (group : string) (leaves : list<Guid>) (m : GroupsModel) = 
            match leaves.IsEmpty with
            | false -> 
                let node = 
                    m.rootGroup.subNodes 
                    |> IndexList.toList
                    |> List.tryFind(fun x -> x.name = group)

                let keep oldLeaf =
                  leaves 
                    |> List.exists (fun deleteLeaf -> deleteLeaf = oldLeaf)
                    |> not
                match node with
                | Some node ->
                    let leaves = node.leaves 
                                  |> IndexList.filter keep
                    let node = {node with leaves = leaves}
                    let flat = m.flat |> HashMap.filter (fun g x -> keep g)
                    let updatedNode = {node with leaves = leaves}
                    let subNodes = m.rootGroup.subNodes
                                    |> IndexList.filter (fun n -> not (n.key = updatedNode.key))
                                    |> IndexList.add updatedNode
                        
                    let root = {m.rootGroup with subNodes = subNodes}
                    { m with rootGroup = root; flat = flat }   
                | None -> 
                    Log.line "[Groups] Group %s not found. Cannot delete." group
                    m
            | true -> m