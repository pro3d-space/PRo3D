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
                if outward + inward = 0 then
                    Log.warn "[opc]   winding: no usable face in the root patch of %s; NormalFlip 0" basePath
                    0.0
                else
                    let flip = if inward > outward then 1.0 else 0.0
                    Log.line "[opc]   winding: %d outward / %d inward -> NormalFlip %.0f"
                        outward inward flip
                    flip
            | p, i ->
                Log.warn "[opc]   winding: unexpected geometry layout in %s (positions %s, indices %s); NormalFlip 0"
                    basePath (if isNull p then "none" else p.GetType().Name)
                    (if isNull i then "none" else i.GetType().Name)
                0.0
        with e ->
            Log.warn "[opc]   could not estimate winding (%s); NormalFlip 0" e.Message
            0.0

module ImageProjectionOpcExtensions =

    /// The viewer's per-patch "NormalFlip": 0 while nothing is projected or hovered, the
    /// hierarchy's winding vote otherwise. `flip` is forced on first use, so a scene that
    /// never projects never loads a patch to vote on. (The offscreen tools bind the vote
    /// eagerly instead: their shading reads the flipped normal on every render.)
    let normalFlipUniform (flip : Lazy<float>) : obj -> Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch -> IAdaptiveValue =
        fun scope _ ->
            let context = scope |> unbox<OpcRenderingExtensions.Context>
            context.projectedImages |> AVal.bind (function
                | None -> AVal.constant 0.0f
                | Some p ->
                    (p.stackProjections, p.hoveredProjection) ||> AVal.map2 (fun layers hovered ->
                        if layers.Length = 0 && Option.isNone hovered then 0.0f
                        else float32 flip.Value)
            ) :> IAdaptiveValue

    let projectionUniformMap : Map<string, obj -> Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch -> IAdaptiveValue> =
        Map.ofList [
            // The projector matrices below are vp * Local2Global and deliberately NOT
            // vp * modelTrafo * Local2Global: they apply to the raw patch-local position,
            // Local2Global already lands in the surface's body-fixed frame, and that is
            // the frame computeProjector builds vp in. The model trafo would apply the
            // body's orientation a second time. Leaving it out is also what keeps the
            // projection on the terrain when the scene time changes.
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
