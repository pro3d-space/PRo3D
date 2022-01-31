namespace PRo3D.Lite

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering

open Adaptify.FSharp
open Adaptify

type Opc = {
    opc   : PatchHierarchy
}

module PatchHierarchy = 
    let estimateBoundingBox (p : PatchHierarchy) = 
        match p.tree with
        | QTree.Node(p,c) -> p.info.GlobalBoundingBox
        | QTree.Leaf(p) -> p.info.GlobalBoundingBox

[<ModelType>]
type Surface =
    {
        opcs  : HashMap<string, Opc>
        trafo : Trafo3d
    }

type Annotation = 
    {
        points : list<V3d>
        color  : C4b
    }

type State = 
    {
        surfaces : HashMap<string, Surface>
    }

