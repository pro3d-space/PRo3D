namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.Rendering
//open Aardvark.UI

open Adaptify
open Chiron
open PRo3D.Base.Json
open PRo3D.Core

type PoseId = string

type Pose =
    {
        [<NonAdaptive>]
        version  : int
        [<NonAdaptive>]
        key      : PoseId
        metadata : string
    }

module Pose =
    let current = 0   

    let read0  = 
        json {
            let! key           = Json.read "key"
            let! metadata      = Json.read "name"
            
            return
                {
                    version        = current
                    key            = key
                    metadata       = metadata
                }
        }

    let dummyData key =
        {
            version  = 0
            key      = key
            metadata = ""
        }

type Pose with 
    static member FromJson( _ : Pose) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Pose.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of Pose" 
                |> Json.error
        }

    static member ToJson(x : Pose) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "key"        x.key
            do! Json.write "metadata"   x.metadata
        }

type PoseTree =
    {
        key         : PoseId
        children    : list<PoseTree>
    } with
    static member FromJson( _ : PoseTree) =
        json {
            let! key       = Json.read "key"
            let! children = Json.read "children"

            return {
                key      = key
                children = children
            }
        }

    static member ToJson(x : PoseTree) =
        json {
            do! Json.write "key"       x.key
            do! Json.write "children"  x.children
        }

type PoseTreeStructure = 
    | Empty
    | PoseNode of PoseTree
    with 
    static member FromJson( _ : PoseTreeStructure) =
        json {
            let! structure       = Json.read "poseTree"
            match structure with
            | None -> 
                return PoseTreeStructure.Empty
            | Some node -> 
                return PoseTreeStructure.PoseNode node
        }

    static member ToJson(x : PoseTreeStructure) =
        json {
            do! Json.write "poseTree" <|
                    match x with
                    | Empty -> None
                    | PoseNode p -> Some p
        }


type PoseTreeData =
    {
        [<NonAdaptive>]
        version : int
        poses : list<Pose>
        structure : PoseTreeStructure
    }

module PoseTreeData =
    let current = 0   

    let read0  = 
        json {
            let! poses         = Json.read "poses"
            let! structure     = Json.read "structure"

            return
                {
                    version        = current
                    poses          = poses
                    structure      = structure
                }
        }

    let dummyData =
        let p0 = Pose.dummyData "Pose0"
        let p1 = Pose.dummyData "Pose1"
        let p2 = Pose.dummyData "Pose2"
        let p3 = Pose.dummyData "Pose3"

        {
            version   = 0
            poses     = [p0;p1;p2;p3]
            structure = PoseTreeStructure.PoseNode {key = p0.key;
                                                    children = 
                                                        [
                                                            {key = p1.key; children = []}
                                                            {key = p2.key; 
                                                             children = [
                                                                     {key = p3.key; children = []}          
                                                            ]}
                                                        ]
                                                    }
        }

type PoseTreeData with 
    static member FromJson( _ : PoseTreeData) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! PoseTreeData.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of Pose" 
                |> Json.error
        }

    static member ToJson(x : PoseTreeData) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "poses"      x.poses
            do! Json.write "structure"  x.structure
        }