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
            Expect.stringContains code "ProjectedStackFalseColor[i]" "per-layer false colour switch"
            // sun off: the camera headlight leaves the projected image unshaded
            Expect.stringContains code "ProjectedCover" "the stack hands its coverage to the headlight"
            Expect.stringContains code "ProjectedStackColorMap[i]" "per-layer colour map"
            Expect.stringContains code "ProjectedStackDataType[i]" "per-layer data type"
            let extensions =
                code.Split('\n')
                |> Array.filter (fun l -> l.TrimStart().StartsWith "#extension")
            Expect.isEmpty extensions "an extension pragma fails the whole program on macOS"
        }
    
        test "the surface layers follow the selected image's range, brightness/contrast and False Colour switch" {
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
            Expect.isFalse (layer ()).falseColor "False Colour off by default, as in the preview"

            let edit (msg : ImageMessage) (m : GisApp) =
                { m with projectedImageList = ProjectedImageListApp.update m.projectedImageList (ProjectedImageListMessage.ImageMessage (image.id, msg)) }
            let m' =
                m
                |> edit (SetCustomMin 3.0)
                |> edit (SetCustomMax 7.0)
                |> edit (BrightnessContrastMessage (SetBrightness (Numeric.Action.SetValue 0.25)))
                |> edit (BrightnessContrastMessage (SetContrast (Numeric.Action.SetValue 2.0)))
                |> edit ToggleFalseColor
            transact (fun () -> g.Update m')
            Expect.equal (layer ()).minMax (V2f(3.0f, 7.0f)) "range reaches the surface"
            Expect.equal (layer ()).brightnessContrast (V2f(0.25f, 2.0f)) "brightness/contrast reach the surface"
            Expect.isTrue (layer ()).falseColor "the False Colour switch reaches the surface"
        }
    
        test "each stack layer carries its own image's colour map and data type" {
            let a = { ProjectedImageApp.initial with id = Guid.NewGuid(); texture = "a.tif"; dataType = DataType.Float }
            let b = { ProjectedImageApp.initial with id = Guid.NewGuid(); texture = "b.tif"; dataType = DataType.UInt16 }
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
                            images = IndexList.ofList [ a; b ]
                            stack = IndexList.ofList [ a.id; b.id ]
                            // the selected image used to set every layer's colour map
                            selectedImage = Some a.id } }
            let edit (id : Guid) (msg : ImageMessage) (m : GisApp) =
                { m with projectedImageList = ProjectedImageListApp.update m.projectedImageList (ProjectedImageListMessage.ImageMessage (id, msg)) }
            let m =
                m
                |> edit a.id (SetColorMap ColorMap.Viridis)
                |> edit b.id (SetColorMap ColorMap.PiYG)
            let g = AdaptiveGisApp.Create m
            match PRo3D.GIS.ProjectedImagesListAppHelper.getProjectedImageData g (AVal.constant None) (Guid.NewGuid()) with
            | None -> failtest "no projection data"
            | Some p ->
                let layers = AVal.force p.stackProjections
                Expect.equal (layers |> Array.map (fun l -> l.colorMap)) [| int ColorMap.Viridis; int ColorMap.PiYG |] "each layer its own colour map"
                Expect.equal (layers |> Array.map (fun l -> l.dataType)) [| int DataType.Float; int DataType.UInt16 |] "each layer its own data type"
        }

        test "the colour-map atlas has a segment for every colour map, and lookups stay inside it" {
            Expect.equal (Enum.GetValues(typeof<ColorMap>).Length) PRo3D.InstrumentVisualization.ColormapAtlas.count "one segment per ColorMap value"
            let n = float32 PRo3D.InstrumentVisualization.ColormapAtlas.count
            let w = float32 PRo3D.InstrumentVisualization.ColormapAtlas.segmentWidth
            for map in 0 .. PRo3D.InstrumentVisualization.ColormapAtlas.count - 1 do
                let lo = PRo3D.Core.ImageProjection.Shaders.stackColormapCoordinate map 0.0f
                let hi = PRo3D.Core.ImageProjection.Shaders.stackColormapCoordinate map 1.0f
                Expect.floatClose Accuracy.high (float lo) (float ((float32 map * w + 0.5f) / (w * n))) "first texel centre"
                Expect.floatClose Accuracy.high (float hi) (float ((float32 map * w + w - 0.5f) / (w * n))) "last texel centre"
                Expect.isGreaterThan (float lo) (float (float32 map / n)) "not into the map before"
                Expect.isLessThan (float hi) (float (float32 (map + 1) / n)) "not into the map after"
        }

        test "the colour-map atlas loads from the embedded colour maps" {
            match PRo3D.InstrumentVisualization.InstrumentImageVisualization.stackColormapAtlas.Value with
            | :? PixTexture2d as t ->
                let size = t.PixImageMipMap.ImageArray.[0].Size
                Expect.equal size (V2i(PRo3D.InstrumentVisualization.ColormapAtlas.segmentWidth * PRo3D.InstrumentVisualization.ColormapAtlas.count, 1)) "all maps in one row"
                // magma (segment 0) runs from (0, 0, 3) to (251, 252, 191): red and blue not swapped
                let row = (t.PixImageMipMap.ImageArray.[0] :?> PixImage<byte>).GetMatrix<C4b>()
                let close (c : C4b) (r, g, b) = abs (int c.R - r) <= 6 && abs (int c.G - g) <= 6 && abs (int c.B - b) <= 6
                Expect.isTrue (close row.[0L, 0L] (0, 0, 3)) (sprintf "magma's black, got %A" row.[0L, 0L])
                let last = int64 (PRo3D.InstrumentVisualization.ColormapAtlas.segmentWidth - 1)
                Expect.isTrue (close row.[last, 0L] (251, 252, 191)) (sprintf "magma's cream, got %A" row.[last, 0L])
            | other -> failtestf "unexpected texture %A" other
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
    
        // magma.png's own pixels (first row): x = 0 is (0, 0, 3), x = 639 is (251, 252, 191).
        // The legend runs top = high values, so its first stop is the bright end.
        test "the legend shows the colour map's colours (red and blue not swapped)" {
            let stream = PRo3D.InstrumentVisualization.InstrumentImageVisualization.getResourceStream "magma.png" ()
            let stops = PRo3D.FalseColorLegendApp.Draw.buildGradiantsOfImage false 100 stream
            let close (c : C3b) (r, g, b) =
                abs (int c.R - r) <= 6 && abs (int c.G - g) <= 6 && abs (int c.B - b) <= 6
            match List.tryHead stops, List.tryLast stops with
            | Some (_, top), Some (_, bottom) ->
                Expect.isTrue (close top (251, 252, 191)) (sprintf "top: magma's cream, got %A" top)
                Expect.isTrue (close bottom (0, 0, 3)) (sprintf "bottom: magma's black, got %A" bottom)
            | _ -> failtest "no legend stops"
        }
    ]
