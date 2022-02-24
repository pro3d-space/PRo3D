namespace PRo3D.Lite

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering

open Adaptify.FSharp
open Adaptify
open PRo3D.Base


//type Elipsoid = 
//    | MarsIAU
//    | WSG84

//type LocalFrame = 
//    | JPL
//    | ENU

//type CoordinateFrames =     
//    | Global      of Elipsoid   // registered
//    | Local       of LocalFrame // camera center

//type Coordinates =
//    | LatLonAlt of Elipsoid * lat : float * lon : float * alt : float 
//    | Xyz of V3d

type ImportCoordinateFrame = 
    | Local
    | Global

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

[<ModelType>]
type State = 
    {
        surfaces : HashMap<string, Surface>
        planet   : PRo3D.Base.Planet
    }

