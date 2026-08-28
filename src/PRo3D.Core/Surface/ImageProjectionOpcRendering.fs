namespace PRo3D.Core

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph.Semantics


        

/// Which way `ImageProjection.Shaders.generateNormal`'s face normal points for a given
/// dataset.
///
/// OPC datasets are inconsistently wound, so `cross edge1 edge2` points outward on one and
/// inward on another. Anything that tests a normal against a direction OTHER than the
/// render camera therefore needs to know: the projection shaders' "is this fragment facing
/// the projector" test is exactly that, and gets the answer backwards on an inward-wound
/// dataset -- the projection then survives only near the limb.
///
/// Lives here rather than next to the offscreen tools' scene graph because both the tools
/// (PRo3D.GIS.OpcSg) and the viewer (Surface.Sg) must bind the same value, and PRo3D.Core
/// is what they share.
module NormalWinding =

    open Aardvark.Rendering
    open Aardvark.Data.Opc
    open Aardvark.SceneGraph.Opc

    /// Sample up to ~100 faces of the coarse root patch and vote whether they point away
    /// from the body-fixed origin. Majority inward -> the shader must flip (1.0), else 0.0.
    /// Valid for star-shaped bodies, which is what this projection is for.
    let estimate (basePath : string) (rootPatch : Patch) : float =
        try
            let ig, _ = Patch.load (OpcPaths.OpcPaths basePath) ViewerModality.XYZ rootPatch.info
            let l2g = rootPatch.info.Local2Global.Forward
            match ig.IndexedAttributes.[DefaultSemantic.Positions], ig.IndexArray with
            | (:? array<V3f> as pos), (:? array<int> as idx) ->
                let triCount = idx.Length / 3
                let stride = max 1 (triCount / 100)   // ~100 samples spread across the patch
                let mutable outward = 0
                let mutable inward = 0
                let mutable t = 0
                while t < triCount do
                    let i = t * 3
                    let a = pos.[idx.[i]]
                    let b = pos.[idx.[i + 1]]
                    let c = pos.[idx.[i + 2]]
                    if not (a.IsNaN || b.IsNaN || c.IsNaN) then
                        let n = l2g.TransformDir (V3d (Vec.cross (b - a) (c - a)))
                        let centroid = l2g.TransformPos (V3d ((a + b + c) / 3.0f))
                        if Vec.dot n centroid > 0.0 then outward <- outward + 1
                        else inward <- inward + 1
                    t <- t + stride
                if outward + inward = 0 then 0.0
                else
                    let flip = if inward > outward then 1.0 else 0.0
                    Log.line "[opc]   winding: %d outward / %d inward -> NormalFlip %.0f"
                        outward inward flip
                    flip
            | _ -> 0.0
        with e ->
            Log.warn "[opc]   could not estimate winding (%s); NormalFlip 0" e.Message
            0.0

module ImageProjectionOpcExtensions =

    let projectionUniformMap : Map<string, obj -> Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch -> IAdaptiveValue> =
        Map.ofList [
            // NO modelTrafo here. These matrices are applied to the RAW PATCH-LOCAL
            // position (stableImageProjectionTrafo stashes localPos = v.pos), and
            // Local2Global already carries that to the surface's body-fixed frame, which is
            // the frame computeProjector builds the projector in. Composing the surface
            // model trafo as well applies the body's own orientation (pxform body-fixed ->
            // observer frame) a SECOND time.
            //
            // Measured on the AFC dataset, projecting an image back from its own camera:
            // with the scene's observation frame set to J2000 the reprojection correlates
            // 0.028 with the source image; with it set to DIMORPHOS_FIXED -- which makes
            // the model trafo identity and so cancels the double rotation -- 0.697. The
            // same image through the same shader offscreen, where the model trafo is
            // identity, reproduces the source at correlation 1.0000 / 0.0064 mean DN.
            //
            // The old note here said the model trafo "is required; it only worked without
            // while every body sat at identity". What it was compensating for is that
            // computeProjector falls back to "J2000" when a surface has no GIS reference
            // system -- and such a surface has no entity either, so getSurfaceTrafo returns
            // None and the model trafo is identity regardless. Dropping it is correct in
            // both cases, and it is what keeps the projection stuck to the TERRAIN when the
            // scene time changes: the body rotates, and the image rotates with it.
            // hover footprint (D5): the hovered image's projector, same
            // double-precision per-patch composition as the stack matrices
            "HoveredProjectionTrafo", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant M44d.Identity
                    | Some p ->
                        p.hoveredProjection |> AVal.map (fun vp ->
                            match vp with
                            | Some vp -> vp.Forward * patch.info.Local2Global.Forward
                            | None -> M44d.Identity
                        )
                ) :> IAdaptiveValue
            )
            "HoveredProjectionValid", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                context.projectedImages |> AVal.bind (function
                    | None -> AVal.constant false
                    | Some p -> p.hoveredProjection |> AVal.map Option.isSome
                ) :> IAdaptiveValue
            )
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
                        p.stackProjections
                        |> AVal.map (fun layers ->
                            layers |> Array.map (fun layer ->
                                match layer.trafo with
                                | Some vp -> vp.Forward * patch.info.Local2Global.Forward |> M44f
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
                        p.imageProjection |> AVal.map (fun vp ->
                            match vp with
                            | Some vp ->
                                vp.Forward * patch.info.Local2Global.Forward
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
