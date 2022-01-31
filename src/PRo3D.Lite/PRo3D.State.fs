namespace PRo3D.Lite

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering

open Adaptify.FSharp
open Adaptify


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
    }

[<ModelType>]
type State = 
    {
        surfaces : HashMap<string, Surface>
    }

module DataAPI = 

    /// ray is in global coord. MarsIAU
    let intersect (ray : Ray3d) (opcs : Surface) : Option<V3d> = 
        failwith ""


    let centerView (opcs : Surface) : CameraView = 
        failwith ""

module PRo3DApi =

    module Surface =

        let flyToSurface (surfaceName : string) (state : State) : State =
            state

        let addSurface (surface : Surface) (state : State) : State =
            failwith ""

        let filterTriangles (maxSize : float) (surfaceName : string) (state : State) : State = 
            failwith ""


    module Rendering =

        let captureScreenshot (state : State) : PixImage = 
            failwith ""

    module Interaction = 

        type IViewer =
            abstract member FlyTo : string -> unit
            abstract member GetAnnotations : unit -> list<Annotation>

        let runViewer (state : State) : IViewer =
            failwith ""

    module Crazy = 

        let derive (f : 'a -> 'insight) : 'insight = 
            failwith ""

        let retarget (oldTuple : 'a * 'insight) (newInput : 'a) : 'insight = 
            failwith "" 
            

(*

let analysis =
    let weather = getWeather ($tirol, $sept) ($tirol: latlon)
    let daysWithStrongWind = weather |> Seq.filter $(fun day -> day.$maxGust < 30)
    let windDirectionBins = makeHisto daysWithStrongWind
    let prominentDirection = $getMax windDirectionBins
    // juhu, föhn

varyingParameters analysis

// UI - zeit und ort als input über karte
// UI slider condition
// statistik


*)


module DatascienceAPI =
    
    let getAnnotations (state : State) : list<Annotation> = 
        failwith ""