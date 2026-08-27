namespace PRo3D.Core

open Aardvark.Base

open FSharp.Data.Adaptive
open System.Collections.Generic
open Aardvark.GeoSpatial.Opc.PatchLod
open Aardvark.GeoSpatial.Opc
open PRo3D.Base.Annotation

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

        /// One layer of the projection stack (multi-image projection).
        type ProjectedStackLayer =
            {
                /// The projector's view*proj in the surface's reference frame at
                /// this image's own observation time. None when the projection
                /// did not resolve (no metadata / no SPICE coverage) -- the
                /// layer keeps its slot (a zero matrix in the uniform array, so
                /// texture-array slices and matrices stay index-aligned) and
                /// simply never covers a fragment.
                trafo : Option<Trafo3d>
                /// display min/max of this layer (the per-image false-color range)
                minMax : V2f
                /// image file feeding this layer's texture-array slice
                texturePath : string
                /// which band of a multi-band image is uploaded
                channel : int
            }

        type ProjectedImages =
            {
                imageProjection : aval<Option<Trafo3d>>
                /// The projection stack, bottom -> top. Bounded by
                /// ProjectedImages.maxCount; the stack shader consumes it as
                /// fixed-size uniform arrays (matrices + min/max) plus a count,
                /// and layer i samples slice i of the stack texture array.
                stackProjections : aval<array<ProjectedStackLayer>>
                /// InstrumentVisibilityMode.RelativeCount: tint fragments by
                /// how many stack layers cover them (projectedStackCoverage)
                stackCoverageEnabled : aval<bool>
                sunDirection : aval<Option<V3d>>
                sunLightEnabled : aval<bool>
                /// World -> sun-camera clip space for shadow mapping; None disables the
                /// shadow lookup (HasShadowMap = false). Rides along here -- not as an
                /// outer uniform -- because the per-patch uniform must compose the
                /// patch's Local2Global in double precision (see projectionUniformMap).
                lightViewProj : aval<Option<Trafo3d>>
            }

        type ProjectedImageApplicator(child : ISg, images : aval<Option<string>> -> aval<Option<ProjectedImages>>) =
            inherit Sg.AbstractApplicator(child)
            member x.Images = images

        [<Rule>]
        type ProjectedImageSem() =
            member x.ProjectedImages(app : ProjectedImageApplicator, scope : Ag.Scope) =
                app.Child?ProjectedImages <- app.Images

        type CrossSectionData =
            {
                polygon : Polygon2d
                basis   : CrossSection.ProjectionBasis2d
            }

        type CrossSectionApplicator(child : ISg, data : aval<Option<CrossSectionData>>) =
            inherit Sg.AbstractApplicator(child)
            member x.CrossSectionData = data

        [<Rule>]
        type CrossSectionSem() =
            member x.CrossSectionData(app : CrossSectionApplicator, scope : Ag.Scope) =
                app.Child?CrossSectionData <- app.CrossSectionData
            member x.CrossSectionData(s : Root<ISg>, scope : Ag.Scope) =
                s.Child?CrossSectionData <- AVal.constant None

        let applyCrossSection (data : aval<Option<CrossSectionData>>) (sg : ISg) =
            CrossSectionApplicator(sg, data) :> ISg

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

    type Ag.Scope with
        member x.CrossSectionData : aval<Option<Sg.CrossSectionData>> = x?CrossSectionData

    type Context =
        {
            footprintVP : aval<M44d>
            modelTrafo: aval<Trafo3d>
            projectedImages : aval<Option<Sg.ProjectedImages>>
            texturesScope : obj
            agScope : Ag.Scope
            crossSectionData : aval<Option<Sg.CrossSectionData>>
        }

    let captureContext (n : PatchNode) (s : Ag.Scope) =
        let footprintVP = s.FootprintVP
        let secondaryTexture = SecondaryTexture.getSecondary n s
        let modelTrafo = s.ModelTrafo
        let body = s.Body
        let projectedImages = s.ProjectedImages s.Body
        let crossSectionData = s.CrossSectionData

        {   footprintVP = footprintVP; texturesScope = secondaryTexture;
            modelTrafo = modelTrafo;
            projectedImages = projectedImages
            agScope = s
            crossSectionData = crossSectionData
        }  :> obj