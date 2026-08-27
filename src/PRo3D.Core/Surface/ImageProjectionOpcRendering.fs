namespace PRo3D.Core

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph.Semantics


        

module ImageProjectionOpcExtensions = 

    let projectionUniformMap : Map<string, obj -> Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch -> IAdaptiveValue> =
        Map.ofList [
            "ProjectedStackCoverageEnabled", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant false
                    | Some p -> p.stackCoverageEnabled
                ) :> IAdaptiveValue
            )
            // The projection stack (multi-image projection), bottom -> top.
            // Same double-precision composition as ProjectedImageModelViewProj
            // below; the stack shader binds these as fixed-size uniform arrays
            // (Arr<N<32>, _>, see ProjectedImages.maxCount) -- a plain array
            // source binds to a UBO array field, short arrays are zero-filled
            // (UniformWriters.ArrayWriter), and StackCount bounds the loop.
            "ProjectedStackTrafos", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant Array.empty<M44f>
                    | Some p ->
                        (p.stackProjections, context.modelTrafo)
                        ||> AVal.map2 (fun layers modelTrafo ->
                            layers |> Array.map (fun layer ->
                                match layer.trafo with
                                | Some vp -> vp.Forward * modelTrafo.Forward * patch.info.Local2Global.Forward |> M44f
                                // unresolved layer: the zero matrix maps every
                                // vertex to (0,0,0,0), whose NaN NDC fails the
                                // coverage test -- the slot stays, paints nothing
                                | None -> M44f.Zero
                            )
                        )
                ) :> IAdaptiveValue
            )
            "ProjectedStackMinMax", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant Array.empty<V2f>
                    | Some p -> p.stackProjections |> AVal.map (Array.map (fun l -> l.minMax))
                ) :> IAdaptiveValue
            )
            "ProjectedStackCount", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant 0
                    | Some p ->
                        // clamped to the Arr<N<32>> size (= ProjectedImages.maxCount,
                        // not referencable here -- this file compiles before the
                        // model): UniformWriters truncates an over-long matrix
                        // array, and the shader loop must not index past what was
                        // written (effectiveStack already caps the viewer's
                        // stack; the testbeds can hand over more)
                        p.stackProjections |> AVal.map (fun l -> min l.Length 32)
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
                                // m.Forward (the surface model trafo) is required; it only
                                // worked without while every body sat at identity.
                                vp.Forward * m.Forward * patch.info.Local2Global.Forward
                            | None -> 
                                M44d.Identity
                        ) 
                ) :> IAdaptiveValue
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
            // patch-local -> sun-camera clip, for the shadow-map lookup
            // (transformShadowVertices). Composed on the CPU in double, like
            // ProjectedImageModelViewProj above -- the whole point of routing the light
            // matrix through ProjectedImages instead of an outer float32 uniform.
            "StableModelViewProjTexture", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant M44d.Identity
                    | Some p ->
                        (p.lightViewProj, context.modelTrafo) ||> AVal.map2 (fun vp m ->
                            match vp with
                            | Some vp -> vp.Forward * m.Forward * patch.info.Local2Global.Forward
                            | None -> M44d.Identity
                        )
                ) :> IAdaptiveValue
            )
            "HasShadowMap", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant false
                    | Some p -> p.lightViewProj |> AVal.map Option.isSome
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
