module ShadowsHighlightsTest

open Expecto
open Aardvark.Base

open PRo3D.ImageMapping

// Shadows/highlights for projected images (docs/ShadowsHighlights.md): the CPU mask
// pass and the adjustment the shaders apply.

/// A horizontal ramp, black at the left to white at the right.
let private ramp (w : int) (h : int) =
    let pi = PixImage<byte>(Col.Format.Gray, V2i(w, h))
    let mutable m = pi.GetChannel Col.Channel.Gray
    for y in 0 .. h - 1 do
        for x in 0 .. w - 1 do
            m.[x, y] <- byte (255 * x / (w - 1))
    pi :> PixImage

let private key radius : ShadowsHighlights.MaskKey =
    {
        path = "ramp"; channel = 0
        minMax = V2d(0.0, 1.0); dataType = 2
        shadowTone = 0.5; shadowRadius = radius
        highlightTone = 0.5; highlightRadius = radius
    }

let private masksOf (k : ShadowsHighlights.MaskKey) (pi : PixImage) =
    match ShadowsHighlights.computeMasks k pi with
    | Some (:? PixImage<byte> as m) -> m.GetChannel Col.Channel.Red, m.GetChannel Col.Channel.Green
    | other -> failwithf "expected RGBA byte masks, got %A" other

let tests () =
    testList "shadows/highlights" [

        test "the shadow mask selects the dark tones, the highlight mask the bright ones" {
            let shadow, highlight = masksOf (key 0.0) (ramp 101 3)
            Expect.equal shadow.[0, 1] 255uy "black is fully shadow"
            Expect.equal highlight.[0, 1] 0uy "black is no highlight"
            Expect.equal shadow.[100, 1] 0uy "white is no shadow"
            Expect.equal highlight.[100, 1] 255uy "white is fully highlight"
            // tone 0.5: the masks end / start at mid grey
            Expect.equal shadow.[60, 1] 0uy "above tone: no shadow"
            Expect.equal highlight.[40, 1] 0uy "below 1 - tone: no highlight"
        }

        test "a radius softens the mask edge, radius 0 leaves it sharp" {
            let sharp, _ = masksOf (key 0.0) (ramp 101 3)
            let soft, _ = masksOf (key 40.0) (ramp 101 3)
            // just above the tone the sharp mask is 0, the blurred one picks up shadow
            Expect.equal sharp.[55, 1] 0uy "sharp"
            Expect.isGreaterThan soft.[55, 1] 0uy "softened"
        }

        test "unsupported pixel types give no mask instead of failing" {
            let pi = PixImage<int32>(Col.Format.Gray, V2i(4, 4)) :> PixImage
            Expect.isNone (ShadowsHighlights.computeMasks (key 0.0) pi) "int32"
        }

        test "amount 0 leaves the value untouched" {
            for c in [0.0f; 0.25f; 0.5f; 0.75f; 1.0f] do
                Expect.floatClose Accuracy.high (float (ShadowsHighlights.apply c 1.0f 1.0f 0.0f 0.0f)) (float c) "identity"
        }

        test "shadows brighten, highlights darken, only where the mask is" {
            let c = 0.3f
            Expect.isGreaterThan (ShadowsHighlights.apply c 1.0f 0.0f 1.0f 0.0f) c "shadow amount brightens"
            Expect.isLessThan (ShadowsHighlights.apply c 0.0f 1.0f 0.0f 1.0f) c "highlight amount darkens"
            Expect.floatClose Accuracy.high (float (ShadowsHighlights.apply c 0.0f 0.0f 1.0f 1.0f)) (float c) "no mask, no change"
        }

        test "the surface effect samples the masks and needs no GL extension (macOS: core 4.1)" {
            let code = OutcropTraceShaderTest.surfaceEffectGlsl.Value
            // the GLSL sampler is named after the F# sampler value, not the texture uniform
            Expect.stringContains code "texture(projectedStackMasks" "the stack stage samples the mask array"
            Expect.stringContains code "ProjectedStackToneAmounts[i]" "per-layer amounts"
            let extensions =
                code.Split('\n')
                |> Array.filter (fun l -> l.TrimStart().StartsWith "#extension")
            printfn "surface effect #extension lines: %A" extensions
            Expect.isEmpty extensions "an extension pragma fails the whole program on macOS"
        }

        test "no mask key while both amounts are 0" {
            let off = PRo3D.ImageMapping.ToneAdjustment.init
            let on = { off with amount = { off.amount with value = 0.5 } }
            Expect.isNone (ShadowsHighlights.maskKey "a" 0 V2d.OI 2 off off) "both off"
            Expect.isSome (ShadowsHighlights.maskKey "a" 0 V2d.OI 2 on off) "shadows on"
            Expect.isSome (ShadowsHighlights.maskKey "a" 0 V2d.OI 2 off on) "highlights on"
        }
    ]
