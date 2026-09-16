/// The OPC surface effect comes in four variants (#719): with or without the geometry
/// stage (triangleSizeFilter + generateNormal) and with or without the cross-section
/// discard. ViewerUtils.surfaceEffectPool switches between them per surface without
/// rebuilding render objects, and caches their shared input layout on disk.
///
/// Generating GLSL for this stack takes tens of seconds per variant (that is #719's
/// startup concern), so only the two extreme variants are compiled, once each; together
/// they cover both values of both switches.
module SurfaceEffectVariantTest

open Expecto

open Aardvark.Base
open FShade

open PRo3D

let private lean = lazy (OutcropTraceShaderTest.compile "lean variant" (ViewerUtils.surfaceEffectVariant false false))
// ViewerUtils.surfaceEffect is surfaceEffectVariant true true
let private full = OutcropTraceShaderTest.surfaceEffectGlsl

/// The variants without the geometry stage, where noFaceNormal stands in for generateNormal.
let private leanVariants =
    [
        for crossSectionClip in [ false; true ] do
            yield sprintf "crossSectionClip=%b" crossSectionClip, ViewerUtils.surfaceEffectVariant false crossSectionClip
    ]

let tests () =
    testSequenced <| testList "surface effect variants (#719)" [

        test "the lean variant has neither a geometry stage nor a discard" {
            Expect.isFalse (lean.Value.Contains "#ifdef Geometry") "no geometry stage without geometryStage"
            Expect.isFalse (lean.Value.Contains "discard") "no discard without crossSectionClip"
        }

        test "the full variant has both" {
            Expect.isTrue (full.Value.Contains "#ifdef Geometry") "a geometry stage with geometryStage"
            // crossSectionClip is the only stage in the stack that discards
            Expect.isTrue (full.Value.Contains "discard") "a discard with crossSectionClip"
        }

        test "the variants without a geometry stage need no vertex input the full effect does not" {
            // All variants share one input layout, so an input only a lean variant reads
            // would become an attribute the OPC patches must provide. The classic case:
            // dropping generateNormal without noFaceNormal makes LocalNormal an attribute.
            // (Effect.Inputs is FShade's pre-link view: it lists LocalNormal for the full
            // effect too, which the linker drops because generateNormal writes it.)
            let fullInputs = ViewerUtils.surfaceEffect.Inputs |> Map.keys |> Set.ofSeq
            for name, effect in leanVariants do
                let inputs = effect.Inputs |> Map.keys |> Set.ofSeq
                Expect.isEmpty (Set.difference inputs fullInputs)
                    (sprintf "%s reads no vertex input beyond the full effect's" name)
                Expect.isFalse (inputs.Contains "LocalNormal")
                    (sprintf "%s: noFaceNormal provides LocalNormal, so it is no vertex input" name)
        }

        test "the cached input layout survives the round trip" {
            // a small effect with inputs, uniforms and a sampler, so every field is covered
            let effect =
                Effect.compose [
                    Effect.ofFunction ViewerUtils.Shader.stableTrafo
                    Effect.ofFunction PRo3D.Base.OPCFilter.improvedDiffuseTexture
                ]
            let module_ =
                effect |> Effect.toModule { EffectConfig.empty with outputs = Map.ofList [ "Colors", (typeof<V4f>, 0) ] }
            let layout = EffectInputLayout.ofModules [ module_ ]
            Expect.isGreaterThan layout.Uniforms.Count 0 "the test layout has uniforms"

            let back = layout |> ViewerUtils.SharedEffectPool.serialize |> ViewerUtils.SharedEffectPool.deserialize
            Expect.equal back layout "the stored layout equals the original"
            Expect.equal (back.ComputeHash()) (layout.ComputeHash())
                "and hashes the same, so GL finds the cached programs"
        }

        test "the variant index enumerates the pool in order" {
            Expect.equal (ViewerUtils.surfaceEffectIndex false false) 0 "lean"
            Expect.equal (ViewerUtils.surfaceEffectIndex true  false) 1 "geometry stage"
            Expect.equal (ViewerUtils.surfaceEffectIndex false true)  2 "cross-section clip"
            Expect.equal (ViewerUtils.surfaceEffectIndex true  true)  3 "everything"
        }
    ]
