namespace PRo3D.Core

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph.Semantics


        

module ImageProjectionOpcExtensions = 

    let projectionUniformMap : Map<string, obj -> Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch -> IAdaptiveValue> =
        Map.ofList [
            "ProjectedImagesLocalTrafos", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                let context = scope |> unbox<OpcRenderingExtensions.Context> 
                context.projectedImages |> AVal.bind (function 
                    | None -> AVal.constant Array.empty<M44f>
                    | Some p ->
                        (p.localImageProjectionTrafos, context.modelTrafo)
                        ||> AVal.map2 (fun arr modelTrafo ->  
                            arr |> Array.map (fun (vp : Trafo3d) ->
                                // first to body space, then through projection.
                                // modelTrafo included for the same reason as in
                                // ProjectedImageModelViewProj below.
                                vp.Forward * modelTrafo.Forward * patch.info.Local2Global.Forward |> M44f
                            )
                        )
                ) :> IAdaptiveValue
            )
            "ProjectedImagesLocalTrafosCount", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant 0 
                    | Some p -> 
                        (p.localImageProjectionTrafos |> AVal.map Array.length)
                ) :> IAdaptiveValue
            )
            "ProjectedImageModelViewProjValid", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant false
                    | Some p -> 
                        p.imageProjection |> AVal.map Option.isSome 
                ) :> IAdaptiveValue
            )
            "ProjectedImageModelViewProj", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function 
                    | None -> AVal.constant M44d.Identity
                    | Some p -> 
                        (p.imageProjection, context.modelTrafo) ||> AVal.map2 (fun vp m ->
                            match vp with
                            | Some vp ->
                                // m.Forward is required: patch positions are patch-local,
                                // so they need Local2Global to reach body space and THEN
                                // the surface's model trafo to reach the frame the
                                // projector lives in. Omitting it only looks correct while
                                // every surface sits at identity -- a body placed via
                                // Sg.trafo (e.g. Dimorphos positioned relative to Didymos
                                // by SPICE) renders in the right place but gets its image
                                // projected in the wrong frame.
                                vp.Forward * m.Forward * patch.info.Local2Global.Forward
                            | None -> 
                                M44d.Identity
                        ) 
                ) :> IAdaptiveValue
            )
            // Patch-local -> the OPC's own body-fixed frame, in which the body is centred
            // on the origin. That makes "outward" well defined per triangle: sign of
            // dot(faceNormal, centroid). Needed because triangle winding is a property of
            // the dataset, not of the scene -- the Didymos and Dimorphos OPCs are wound
            // oppositely -- so face-normal orientation cannot be a global constant.
            // Deliberately NOT ApproximateBodyNormalLocalSpace: that is one direction per
            // patch, and at coarse LOD a single patch covers an entire body, so it
            // degenerates to a constant that splits the body in half.
            "Local2Global", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                patch.info.Local2Global.Forward |> AVal.constant :> IAdaptiveValue
            )
            "ApproximateBodyNormalLocalSpace", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                patch.info.Local2Global.Backward.TransformDir(patch.info.GlobalBoundingBox.Center.Normalized).Normalized |> AVal.constant :> IAdaptiveValue
            )
            "SunDirectionWorld", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function 
                    | None -> V3d.OOO |> AVal.constant 
                    | Some d -> 
                        d.sunDirection |> AVal.map (Option.defaultValue V3d.Zero)
                ) :> IAdaptiveValue
            )
            "SunLightEnabled", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function 
                | None -> false |> AVal.constant
                | Some p -> 
                    (p.sunLightEnabled, p.sunDirection) 
                    ||> AVal.map2 (fun enabled dir -> Option.isSome dir && enabled) 
                ) :> IAdaptiveValue
            )
        ]


    //let projectionUniformMap (imageProjection : aval<Option<Trafo3d>>) 
    //                         (localImageProjectionTrafos : aval<array<Trafo3d>>)
    //                         (sunLightDirection : aval<Option<V3d>>) 
    //                         (sunLightingEnabled : aval<bool>) =
    //    Map.ofList [
    //        "ProjectedImagesLocalTrafos", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
    //            let context = scope |> unbox<OpcRenderingExtensions.Context>
    //            (localImageProjectionTrafos, context.modelTrafo)
    //            ||> AVal.map2 (fun arr modelTrafo -> 
    //                arr
    //                |> Array.map (fun (vp : Trafo3d) -> 
    //                    // first to body space, then through projection
    //                    vp.Forward * modelTrafo.Forward * patch.info.Local2Global.Forward  |> M44f
    //                )
    //            ) :> IAdaptiveValue
    //        )
    //        "ProjectedImageModelViewProjValid", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
    //            imageProjection |> AVal.map Option.isSome :> IAdaptiveValue
    //        )
    //        "ProjectedImageModelViewProj", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
    //            let context = scope |> unbox<OpcRenderingExtensions.Context>
    //            (imageProjection, context.modelTrafo) ||> AVal.map2 (fun vp m -> 
    //                match vp with
    //                | Some vp -> 
    //                    vp.Forward * m.Forward * patch.info.Local2Global.Forward
    //                | None -> 
    //                    M44d.Identity
    //            ) :> IAdaptiveValue
    //        )
    //        "ApproximateBodyNormalLocalSpace", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
    //            patch.info.Local2Global.Backward.TransformDir(patch.info.GlobalBoundingBox.Center.Normalized).Normalized |> AVal.constant :> IAdaptiveValue
    //        )
    //        "SunDirectionWorld", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
    //            sunLightDirection |> AVal.map (Option.defaultValue V3d.Zero) :> IAdaptiveValue
    //        )
    //        "SunLightEnabled", (fun _ _ -> 
    //            (sunLightingEnabled, sunLightDirection) 
    //            ||> AVal.map2 (fun enabled dir -> Option.isSome dir && enabled) 
    //            :> IAdaptiveValue
    //        )
    //    ]
