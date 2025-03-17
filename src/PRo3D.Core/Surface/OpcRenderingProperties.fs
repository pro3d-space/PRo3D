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

        type BodyApplicator(child : ISg, body : aval<Option<string>>) =
            inherit Sg.AbstractApplicator(child)
            member x.Body = body
        

        [<Rule>]
        type BodySem() =
            member x.Body(app : BodyApplicator, scope : Ag.Scope) =
                app.Child?Body <- app.Body
            member x.Body(s : Root<ISg>, scope : Ag.Scope) =
                let empty : aval<Option<string>> = AVal.constant None
                s.Child?Body <- AVal.constant empty

        type ProjectedImages = 
            {
                imageProjection : aval<Option<Trafo3d>>
                localImageProjectionTrafos : aval<array<Trafo3d>>
                sunDirection : aval<Option<V3d>>
                sunLightEnabled : aval<bool>
            }

        type ProjectedImageApplicator(child : ISg, images : aval<Option<string>> -> aval<Option<ProjectedImages>>) =
            inherit Sg.AbstractApplicator(child)
            member x.Images = images

        [<Rule>]
        type ProjectedImageSem() =
            member x.ProjectedImages(app : ProjectedImageApplicator, scope : Ag.Scope) =
                app.Child?ProjectedImages <- app.Images

        let applyBody (s : aval<Option<string>>) (sg : ISg) = 
            BodyApplicator(sg, s) :> ISg

        let applyProjectedImages' (s : aval<Option<string>> -> aval<Option<ProjectedImages>>) (sg : ISg) = 
            ProjectedImageApplicator(sg, s) :> ISg

        let applyProjectedImages (s : aval<Option<string>> -> aval<Option<ProjectedImages>>) (sg : ISg<_>) = 
            ProjectedImageApplicator(sg, s) 
            |> Sg.noEvents

module OpcRenderingExtensions =
    open Aardvark.Base.Ag
    open Aardvark.SceneGraph.Semantics
    open SgExtensions.Sg

    type Ag.Scope with
        member x.FootprintVP : aval<M44d> = x?FootprintVP
        member x.ProjectedImages : aval<Option<string>> -> aval<Option<ProjectedImages>> = x?ProjectedImages
        member x.Body : aval<Option<string>> = x?Body

    type Context = 
        { 
            footprintVP : aval<M44d> 
            modelTrafo: aval<Trafo3d>
            projectedImages : aval<Option<Sg.ProjectedImages>>
            texturesScope : obj
            agScope : Ag.Scope
        }

    let captureContext (n : PatchNode) (s : Ag.Scope) =
        let footprintVP = s.FootprintVP
        let secondaryTexture = SecondaryTexture.getSecondary n s
        let modelTrafo = s.ModelTrafo
        let body = s.Body
        let projectedImages = s.ProjectedImages s.Body

        {   footprintVP = footprintVP; texturesScope = secondaryTexture; 
            modelTrafo = modelTrafo;
            projectedImages = projectedImages
            agScope = s 
        }  :> obj