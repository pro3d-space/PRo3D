namespace PRo3D.Lite

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering

open Adaptify.FSharp
open Adaptify

open PRo3D.Base
open PRo3D.Lite

module DataAPI = 

    /// ray is in global coord. MarsIAU
    let intersect (ray : Ray3d) (opcs : Surface) : Option<V3d> = 
        failwith ""



module PRo3DApi =

    module Surface =

        let centerView (surface : Surface) : CameraView = 
            let bbs = 
                surface.opcs 
                |> Seq.map (fun (_,opc) -> 
                    match opc.opc.tree with
                    | QTree.Leaf p -> p.info.GlobalBoundingBox
                    | QTree.Node(p,_) -> p.info.GlobalBoundingBox
                )
            let bb = Box3d(bbs)
            let pos = bb.Max 
            // TODO
            let up = CooTransformation.getUpVector pos Planet.Mars
            CameraView.lookAt bb.Max bb.Center up

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