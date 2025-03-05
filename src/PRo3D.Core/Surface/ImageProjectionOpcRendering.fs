namespace PRo3D.Core

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph.Semantics

module ImageProjectionOpcExtensions = 

    let projectionUniformMap (imageProjection : aval<Option<Trafo3d>>) 
                             (localImageProjectionTrafos : aval<array<Trafo3d>>)
                             (sunLightDirection : aval<Option<V3d>>) 
                             (sunLightingEnabled : aval<bool>) =
        Map.ofList [
            "ProjectedImagesLocalTrafos", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                (localImageProjectionTrafos, context.modelTrafo)
                ||> AVal.map2 (fun arr modelTrafo -> 
                    arr
                    |> Array.map (fun (vp : Trafo3d) -> 
                        // first to body space, then through projection
                        vp.Forward * modelTrafo.Forward * patch.info.Local2Global.Forward  |> M44f
                    )
                ) :> IAdaptiveValue
            )
            "ProjectedImageModelViewProjValid", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                imageProjection |> AVal.map Option.isSome :> IAdaptiveValue
            )
            "ProjectedImageModelViewProj", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                let context = scope |> unbox<OpcRenderingExtensions.Context>
                (imageProjection, context.modelTrafo) ||> AVal.map2 (fun vp m -> 
                    match vp with
                    | Some vp -> 
                        vp.Forward * m.Forward * patch.info.Local2Global.Forward
                    | None -> 
                        M44d.Identity
                ) :> IAdaptiveValue
            )
            "ApproximateBodyNormalLocalSpace", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                patch.info.Local2Global.Backward.TransformDir(patch.info.GlobalBoundingBox.Center.Normalized).Normalized |> AVal.constant :> IAdaptiveValue
            )
            "SunDirectionWorld", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                sunLightDirection |> AVal.map (Option.defaultValue V3d.Zero) :> IAdaptiveValue
            )
            "SunLightEnabled", (fun _ _ -> 
                (sunLightingEnabled, sunLightDirection) 
                ||> AVal.map2 (fun enabled dir -> Option.isSome dir && enabled) 
                :> IAdaptiveValue
            )
        ]