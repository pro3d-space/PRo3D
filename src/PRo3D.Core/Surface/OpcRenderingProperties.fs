namespace PRo3D.Core

open Aardvark.Base

open FSharp.Data.Adaptive
open System.Collections.Generic
open Aardvark.GeoSpatial.Opc.PatchLod
open Aardvark.GeoSpatial.Opc

[<AutoOpen>]
module SgExtensions =

    module Sg = 

        open Aardvark.Base.Ag
        open Aardvark.SceneGraph
        open Aardvark.SceneGraph.Semantics
        open Aardvark.UI

        type BodyApplicator(child : ISg, body : Option<string>) =
            inherit Sg.AbstractApplicator(child)
            member x.Body = body
        

        [<Rule>]
        type BodySem() =
            member x.Body(app : BodyApplicator, scope : Ag.Scope) =
                app.Child?Body <- app.Body
            member x.Body(s : Root<ISg>, scope : Ag.Scope) =
                s.Child?Body <- None

        type ProjectedImages = 
            {
                imageProjection : aval<Option<Trafo3d>>
                localImageProjectionTrafos : aval<array<Trafo3d>>
                sunDirection : aval<Option<V3d>>
                sunLightEnabled : aval<bool>
            }

        type ProjectedImageApplicator(child : ISg, images : Option<string -> Option<ProjectedImages>>) =
            inherit Sg.AbstractApplicator(child)
            member x.Images = images

        [<Rule>]
        type ProjectedImageSem() =
            member x.ProjectedImages(app : ProjectedImageApplicator, scope : Ag.Scope) =
                app.Child?ProjectedImages <- app.Images

        let applyBody (s : string) (sg : ISg) = 
            BodyApplicator(sg, Some s) :> ISg

        let applyProjectedImages' (s : Option<string -> Option<ProjectedImages>>) (sg : ISg) = 
            ProjectedImageApplicator(sg, s) :> ISg

        let applyProjectedImages (s : Option<string -> Option<ProjectedImages>>) (sg : ISg<_>) = 
            ProjectedImageApplicator(sg, s) 
            |> Sg.noEvents

module OpcRenderingExtensions =
    open Aardvark.Base.Ag
    open Aardvark.SceneGraph.Semantics

    type Ag.Scope with
        member x.FootprintVP : aval<M44d> = x?FootprintVP
        member x.ProjectedImages : Option<string -> Option<Sg.ProjectedImages>> = x?ProjectedImages
        member x.Body : Option<string> = x?Body

    type Context = 
        { 
            footprintVP : aval<M44d> 
            modelTrafo: aval<Trafo3d>
            projectedImages : Option<Sg.ProjectedImages>
            texturesScope : obj
            agScope : Ag.Scope
        }

    let captureContext (n : PatchNode) (s : Ag.Scope) =
        let footprintVP = s.FootprintVP
        let secondaryTexture = SecondaryTexture.getSecondary n s
        let modelTrafo = s.ModelTrafo
        let body = s.Body
        let projectedImages = 
            match s.ProjectedImages, s.Body with
            | Some p, Some b -> 
                p b
            | _ -> 
                None
        {   footprintVP = footprintVP; texturesScope = secondaryTexture; 
            modelTrafo = modelTrafo;
            projectedImages = projectedImages
            agScope = s 
        }  :> obj