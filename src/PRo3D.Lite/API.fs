namespace PRo3D.Lite

open System.IO
open MBrace.FsPickler
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


module Api =

    module Surface =

        let private serializer = FsPickler.CreateBinarySerializer()

        let approximateBoundingBox (surface : Surface) = 
            let bbs = 
                surface.opcs 
                |> Seq.map (fun (_,opc) -> 
                    match opc.opc.tree with
                    | QTree.Leaf p -> p.info.GlobalBoundingBox
                    | QTree.Node(p,_) -> p.info.GlobalBoundingBox
                )
            Box3d(bbs)

        let centerView' (planet : Planet) (boundingBox : Box3d) : CameraView = 
            let pos = boundingBox.Max 
            let up = CooTransformation.getUpVector pos planet
            CameraView.lookAt boundingBox.Max boundingBox.Center up

        let centerView (planet : Planet) (surface : Surface) : CameraView = 
            approximateBoundingBox surface |> centerView' planet

        let loadSurface (opcPaths : list<string>) =
            let opcs = 
                opcPaths 
                |> Seq.toList |> List.map (fun basePath -> 
                    basePath, { 
                        opc = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                    }
                )
                |> HashMap.ofSeq

            { opcs = opcs; trafo = Trafo3d.Identity }

        let loadSurfaceDirectory (directory : string) = 
            Directory.EnumerateDirectories(directory)
            |> Seq.toList
            |> loadSurface

    module State =

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



module DatascienceAPI =
    
    let getAnnotations (state : State) : list<Annotation> = 
        failwith ""