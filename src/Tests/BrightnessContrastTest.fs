module BrightnessContrastTest

open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Core.Gis
open PRo3D.ImageMapping
// last: an earlier open shadows Expecto's `test`
open Expecto

// Brightness / contrast of projected images (docs/MultiImageProjection.md): the
// adjustment the preview and the stack shader share, and its uniform in the surface GLSL.

module private UniformArrayShader =
    open FShade
    open Aardvark.Rendering
    type UniformScope with
        member x.TestPairs : Arr<N<32>, V2f> = uniform?TestPairs
        member x.TestIndex : int = uniform?TestIndex
    let readPair (v : Effects.Vertex) =
        fragment {
            let p = uniform.TestPairs.[uniform.TestIndex]
            return V4f(p.X, p.Y, 0.0f, 1.0f)
        }

/// Renders a quad whose colour is TestPairs.[index] and reads back the centre pixel (0..1).
let private renderPair (pairs : aval<V2f[]>) (index : int) : Option<V2f> =
    match PRo3D.Tests.Render.context.Value with
    | None -> None
    | Some (runtime, signature) ->
        let size = V2i(8, 8)
        let sg =
            Sg.fullScreenQuad
            |> Sg.uniform "TestPairs" pairs
            |> Sg.uniform' "TestIndex" index
            |> Sg.shader { do! UniformArrayShader.readPair }
        use task = runtime.CompileRender(signature, sg)
        let color = runtime.CreateTexture2D(size, TextureFormat.Rgba8, 1, signature.Samples)
        let depth = runtime.CreateRenderbuffer(size, TextureFormat.Depth24Stencil8, signature.Samples)
        let resolved = runtime.CreateTexture2D(size, TextureFormat.Rgba8)
        use fbo =
            runtime.CreateFramebuffer(signature, [
                DefaultSemantic.Colors, color.GetOutputView()
                DefaultSemantic.DepthStencil, depth :> IFramebufferOutput ])
        task.Run(RenderToken.Empty, fbo)
        runtime.ResolveMultisamples(color.GetOutputView(), resolved)
        let pi = runtime.Download(resolved).ToPixImage<byte>()
        let ch (c : int64) = float32 (pi.GetChannel(c).[4L, 4L]) / 255.0f
        resolved.Dispose(); depth.Dispose(); color.Dispose()
        Some (V2f(ch 0L, ch 1L))

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
    
        test "the surface layers follow the selected image's range and brightness/contrast" {
            let image = { ProjectedImageApp.initial with id = Guid.NewGuid(); texture = "image.tif" }
            let observation =
                { (GisApp.initial None).defaultObservationInfo with
                    observer = Some (EntitySpiceName "MARS")
                    referenceFrame = Some (FrameSpiceName "IAU_MARS") }
            let initial = GisApp.initial None
            let m =
                { initial with
                    defaultObservationInfo = observation
                    projectedImageList =
                        { initial.projectedImageList with
                            images = IndexList.single image
                            stack = IndexList.single image.id
                            selectedImage = Some image.id } }
            let g = AdaptiveGisApp.Create m
            let layers =
                match PRo3D.GIS.ProjectedImagesListAppHelper.getProjectedImageData g (AVal.constant None) (Guid.NewGuid()) with
                | Some p -> p.stackProjections
                | None -> failtest "no projection data"
            let layer () =
                match AVal.force layers |> Array.tryHead with
                | Some l -> l
                | None -> failtest "no stack layer"
            Expect.equal (layer ()).brightnessContrast V2f.OI "unchanged at first"

            let edit (msg : ImageMessage) (m : GisApp) =
                { m with projectedImageList = ProjectedImageListApp.update m.projectedImageList (ProjectedImageListMessage.ImageMessage (image.id, msg)) }
            let m' =
                m
                |> edit (SetCustomMin 3.0)
                |> edit (SetCustomMax 7.0)
                |> edit (BrightnessContrastMessage (SetBrightness (Numeric.Action.SetValue 0.25)))
                |> edit (BrightnessContrastMessage (SetContrast (Numeric.Action.SetValue 2.0)))
            transact (fun () -> g.Update m')
            Expect.equal (layer ()).minMax (V2f(3.0f, 7.0f)) "range reaches the surface"
            Expect.equal (layer ()).brightnessContrast (V2f(0.25f, 2.0f)) "brightness/contrast reach the surface"
        }
    
        test "a per-layer vec2 array reaches the shader for every layer (std140 stride)" {
            let pairs = AVal.constant [| V2f(0.2f, 0.4f); V2f(0.6f, 0.8f); V2f(1.0f, 0.0f) |]
            for i, expected in [0, V2f(0.2f, 0.4f); 1, V2f(0.6f, 0.8f); 2, V2f(1.0f, 0.0f)] do
                match renderPair pairs i with
                | None -> skiptest "no OpenGL runtime in this environment"
                | Some got ->
                    Expect.isTrue (abs (got.X - expected.X) < 0.02f && abs (got.Y - expected.Y) < 0.02f)
                        (sprintf "layer %d: expected %A, got %A" i expected got)
        }
    ]
