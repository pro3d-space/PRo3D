module BrightnessContrastTest

open Expecto

open PRo3D.ImageMapping

// Brightness / contrast of projected images (docs/MultiImageProjection.md): the
// adjustment the preview and the stack shader share, and its uniform in the surface GLSL.

let tests () =
    testList "brightness/contrast" [

        test "brightness 0 and contrast 1 leave the value unchanged" {
            for t in [0.0f; 0.25f; 0.5f; 0.75f; 1.0f] do
                Expect.floatClose Accuracy.high (float (BrightnessContrastShader.apply t 0.0f 1.0f)) (float t) "identity"
        }

        test "brightness shifts, contrast scales around mid grey, clamped to 0..1" {
            Expect.floatClose Accuracy.high (float (BrightnessContrastShader.apply 0.3f 0.2f 1.0f)) 0.5 "brighter"
            Expect.floatClose Accuracy.high (float (BrightnessContrastShader.apply 0.5f 0.0f 3.0f)) 0.5 "mid grey stays"
            Expect.floatClose Accuracy.high (float (BrightnessContrastShader.apply 0.6f 0.0f 2.0f)) 0.7 "more contrast spreads"
            Expect.floatClose Accuracy.high (float (BrightnessContrastShader.apply 0.9f 0.0f 0.0f)) 0.5 "contrast 0: flat grey"
            Expect.equal (BrightnessContrastShader.apply 0.9f 0.5f 1.0f) 1.0f "clamped at white"
            Expect.equal (BrightnessContrastShader.apply 0.1f -0.5f 1.0f) 0.0f "clamped at black"
        }

        test "the default leaves an image unchanged" {
            let bc = BrightnessContrast.init
            Expect.equal (bc.brightness.value, bc.contrast.value) (0.0, 1.0) "brightness 0, contrast 1"
        }

        test "the surface effect applies the per-layer values and needs no GL extension (macOS: core 4.1)" {
            let code = OutcropTraceShaderTest.surfaceEffectGlsl.Value
            Expect.stringContains code "ProjectedStackBrightnessContrast[i]" "per-layer brightness/contrast"
            let extensions =
                code.Split('\n')
                |> Array.filter (fun l -> l.TrimStart().StartsWith "#extension")
            Expect.isEmpty extensions "an extension pragma fails the whole program on macOS"
        }
    ]
