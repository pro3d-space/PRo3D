namespace PRo3D.ImageMapping

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering
open FSharp.Data.Adaptive
open PRo3D.ImageMapping

open System.IO

open Aardvark.GeoSpatial.Opc
open Aardvark.PixImage.LibTiff
open PRo3D.InstrumentData
open PRo3D.InstrumentProjection
open PRo3D.InstrumentVisualization
open PRo3D.Core
open PRo3D.SPICE
open PRo3D.FalseColorLegendApp.Draw
open PRo3D.Base.FalseColorsModel
open PRo3D.Base


module Shaders = 
    open FShade
    open Aardvark.Rendering.Effects

    let instrumentSampler = 
        sampler2d {
            texture uniform?InstrumentImage
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let colormapTextureSampler =
        sampler2d {
            texture uniform?ColormapTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    // shadows/highlights masks, R = shadow, G = highlight (ShadowsHighlights)
    let shadowsHighlightsMaskSampler =
        sampler2d {
            texture uniform?ShadowsHighlightsMask
            filter Filter.MinMagMipLinear
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
        }

    type UniformScope with
        member x.MinValue : float32 = uniform?MinValue
        member x.MaxValue : float32 = uniform?MaxValue
        member x.UseFalseColor : bool = uniform?UseFalseColor
        member x.DataType : int = uniform?DataType
        member x.ShadowAmount : float32 = uniform?ShadowAmount
        member x.HighlightAmount : float32 = uniform?HighlightAmount

    let hshColors (v : Vertex)  = 
        fragment {
            let hshValueX = instrumentSampler.Sample(v.tc).X 
            let remappedClampedNormalizedXInt16 =
                ((min uniform.MaxValue (max uniform.MinValue (hshValueX * 65000.0f))) - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
            let remappedClampedNormalizedXFloat =
                (hshValueX - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
            let normalized0 =
                if uniform.DataType = 2 then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16
            // shadows/highlights (docs/ShadowsHighlights.md); amounts 0: untouched
            let normalized =
                if uniform.ShadowAmount > 0.0f || uniform.HighlightAmount > 0.0f then
                    let masks = shadowsHighlightsMaskSampler.Sample(v.tc)
                    PRo3D.ImageMapping.ShadowsHighlights.apply normalized0 masks.X masks.Y uniform.ShadowAmount uniform.HighlightAmount
                else normalized0
            let remapClampNormalize =
                if uniform.UseFalseColor then
                    V4f(normalized, normalized, normalized, 1.0f)
                else 
                    colormapTextureSampler.Sample(V2f (normalized, 0.0f))
            return remapClampNormalize
        }


module ProjectedImageApp =

    let initialPath = ""

    let numericInput = {
        value   = 0.0
        min     = 0.0
        max     = 65000.0
        step    = 0.1
        format  = "{0:0.00}"
    }

    let initial = {
        // a template, not a loaded image; loadFile mints the real id
        id = Guid.Empty;
        instrument = "";
        colorMap = ColorMap.Magma;
        selectedChannel = { idx = 0; name = None }
        channelOptions = [];
        dataType = DataType.UInt16;
        defaultMinValues = [numericInput.value];
        defaultMaxValues = [numericInput.value];
        texture = initialPath;
        distance = 0;
        time = new DateTime();
        falseColorModel = projectedImageLegend (Range1d(numericInput.min, numericInput.max)) (InstrumentImageVisualization.getResourceStream (ColorMap.getColorMapFileName ColorMap.Magma) ())
        falseColorPreview = false
        shadows = ToneAdjustment.init
        highlights = ToneAdjustment.init
    }

    let private updateTone (a : ToneAdjustment) (action : ToneAdjustmentAction) =
        match action with
        | SetAmount n -> { a with amount = Numeric.update a.amount n }
        | SetTone n   -> { a with tone   = Numeric.update a.tone n }
        | SetRadius n -> { a with radius = Numeric.update a.radius n }
        | ResetTone   -> ToneAdjustment.init

    let loadFile (texturePath : string) =
        // this could be a fallback
        let ifUsefulThisIsHowToExtractInfos = MultiBandReader.tryGetChannels texturePath

        let (tiffMbiJson, tiffJson) = InstrumentMetadata.tryParseMetadataForImagePath texturePath

        let channels =
            match tiffJson with
            | Some tf -> tf.channels
            | None -> 1

        let channelOptions = [ 0 .. channels - 1 ] |> List.map (fun channel -> {idx = channel; name = None})

        let selectedChannelIdx = 0

        // Plain image formats (synthetic COP renders) come without the
        // statistics sidecar and are sampled as normalized [0,1] -- treat them
        // as float over the unit range, otherwise the defaults (uint16 remap
        // over an empty 0..0 range) turn the projection into NaNs.
        let isPlainImage =
            match (Path.GetExtension texturePath).ToLowerInvariant() with
            | ".png" | ".jpg" | ".jpeg" -> true
            | _ -> false

        let defaultMinValues =
            match tiffJson with
            | Some tf -> tf.image_statistics |> Array.toList |> List.map (fun x -> x.minimum)
            | None -> [0.0]

        let defaultMaxValues =
            match tiffJson with
            | Some tf -> tf.image_statistics |> Array.toList|> List.map (fun x -> x.maximum)
            | None -> if isPlainImage then [1.0] else [0.0]

        let dataType =
            match tiffJson with
            | Some tf ->
                match tf.data_type with
                | "uint16" -> DataType.UInt16
                | "uint32" -> DataType.UInt32
                | "float" -> DataType.Float
                | _ -> DataType.UInt16
            | None -> if isPlainImage then DataType.Float else DataType.UInt16

        let rangeOffset = 0.1 * (defaultMaxValues[selectedChannelIdx] - defaultMinValues[selectedChannelIdx])

        let (rangeMin, rangeMax) = (defaultMinValues[selectedChannelIdx] - rangeOffset, defaultMaxValues[selectedChannelIdx] + rangeOffset)

        let stepSize = (rangeMax-rangeMin) / 1000.0;

        let inputMinValue = { numericInput with value = defaultMinValues[selectedChannelIdx]; min = rangeMin; max = rangeMax; step = stepSize}

        let inputMaxValue = { numericInput with value = defaultMaxValues[selectedChannelIdx]; min = rangeMin; max = rangeMax; step = stepSize }

        let distance =
            match tiffMbiJson with
            | Some mbi -> mbi.targetPos.Length
            | None -> 0.0

        // curated header: instrument, plus the camera system when the
        // statistics sidecar declares one and it adds information
        let instrument =
            match tiffMbiJson, tiffJson with
            | Some mbi, Some tf when not (String.IsNullOrWhiteSpace tf.camera_system) && tf.camera_system <> mbi.instrument ->
                sprintf "%s (%s)" mbi.instrument tf.camera_system
            | Some mbi, _ -> mbi.instrument
            | None, Some tf -> tf.camera_system
            | None, None -> ""

        let time =
            match tiffMbiJson with
            | Some mbi -> mbi.obs_date
            | None -> System.DateTime.MinValue // which default time?



        let falseColorModel = {
            projectedImageLegend (Range1d(inputMinValue.value, inputMaxValue.value)) (InstrumentImageVisualization.getResourceStream(ColorMap.getColorMapFileName initial.colorMap)()) with
                lowerBound = inputMinValue;
                upperBound = inputMaxValue;
                //interval   = 
            }

        {
            initial with
                id               = Guid.NewGuid();
                instrument       = instrument;
                texture          = Path.GetFullPath(texturePath);
                defaultMinValues = defaultMinValues;
                defaultMaxValues = defaultMaxValues;
                selectedChannel  = channelOptions[selectedChannelIdx];
                channelOptions   = channelOptions;
                dataType         = dataType;
                distance         = distance;
                time             = time;
                falseColorModel  = falseColorModel
        }


    let update (m : ProjectedImageModel) (msg : ImageMessage) =
        match msg with
            | SetCustomMin v -> 
                let falseColorModel = { m.falseColorModel with lowerBound = { m.falseColorModel.lowerBound with value = v}}
                { m with falseColorModel = falseColorModel}
            | SetCustomMax v -> 
                let falseColorModel = { m.falseColorModel with upperBound = { m.falseColorModel.upperBound with value = v}}
                { m with falseColorModel = falseColorModel }
            | ResetCustomMinMax ->
                let falseColorModel = { 
                    m.falseColorModel with 
                        lowerBound = { m.falseColorModel.lowerBound with value = m.defaultMinValues[m.selectedChannel.idx]}
                        upperBound = { m.falseColorModel.upperBound with value = m.defaultMaxValues[m.selectedChannel.idx]}
                }
                { m with falseColorModel = falseColorModel }
            | SetColorMap (map : ColorMap) ->
                m.falseColorModel.imageFileStream |> Option.map(fun s -> s.Close()) |> ignore

                let falseColorModel = { m.falseColorModel with imageFileStream = Some(InstrumentImageVisualization.getResourceStream(ColorMap.getColorMapFileName map) ()) }
                
                { m with colorMap = map; falseColorModel = falseColorModel }
            | SetEXRChannel channel ->
                let (min, max) = (m.defaultMinValues[channel.idx], m.defaultMaxValues[channel.idx])

                let falseColorModel = { 
                    m.falseColorModel with 
                        lowerBound = { m.falseColorModel.lowerBound with value = min }
                        upperBound = { m.falseColorModel.upperBound with value = max }
                }

                { m with falseColorModel = falseColorModel }
            | ToggleFalseColor ->
                { m with falseColorPreview = (not m.falseColorPreview) }
            | ToggleFalseColorLegend -> 
                { m with falseColorModel = { m.falseColorModel with useFalseColors = not m.falseColorModel.useFalseColors } }
            | ShadowsMessage action ->
                { m with shadows = updateTone m.shadows action }
            | HighlightsMessage action ->
                { m with highlights = updateTone m.highlights action }

            | ImageMessage.Empty ->
                m


    let whitePix =
        let pi = PixImage<byte>(Col.Format.RGBA, V2i.II)
        pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
        pi

    let whiteTex =
        PixTexture2d(PixImageMipMap [| whitePix :> PixImage |], false) :> ITexture

    /// A Properties section: a header row (like Registration) with an optional reset
    /// icon, then label | control rows. Display, Shadows and Highlights all use it.
    let private section (title : string) (tooltip : Option<string>) (reset : Option<string * ImageMessage>) (rows : list<DomNode<ImageMessage>>) =
        let header =
            tr [] [
                th [
                    yield attribute "colspan" "2"
                    match tooltip with
                    | Some t -> yield attribute "title" t
                    | None -> ()
                ] [
                    div [style "display: flex; align-items: center"] [
                        yield text title
                        match reset with
                        | Some (what, msg) ->
                            yield i [clazz "undo icon"
                                     style "margin-left: auto; cursor: pointer"
                                     attribute "title" what
                                     onClick (fun _ -> msg)] []
                        | None -> ()
                    ]
                ]
            ]
        table [clazz "ui celled striped inverted table unstackable"; style "margin: 6px 0 0 0"] [
            tbody [] (header :: rows)
        ]

    let private propertyRow (label : string) (control : list<DomNode<ImageMessage>>) =
        tr [] [
            td [clazz "collapsing"] [text label]
            td [] control
        ]

    /// Shadows or Highlights: Amount / Tone / Radius (docs/ShadowsHighlights.md).
    let private viewTone (title : string) (tooltip : string) (a : AdaptiveToneAdjustment) (msg : ToneAdjustmentAction -> ImageMessage) =
        let row (label : string) (input : AdaptiveNumericInput) (action : Numeric.Action -> ToneAdjustmentAction) =
            propertyRow label [ Numeric.view' [NumericInputType.Slider] input |> UI.map (action >> msg) ]
        section title (Some tooltip) (Some (sprintf "reset %s" (title.ToLowerInvariant()), msg ResetTone)) [
            row "Amount:" a.amount SetAmount
            row "Tone:" a.tone SetTone
            row "Radius:" a.radius SetRadius
        ]

    /// Display: band, display range, colour map and legend.
    let private viewDisplay (m : AdaptiveProjectedImageModel) =
        let channelRepr (c : Channel) =
            match c.name with
            | None -> string c.idx
            | Some name -> name
        let onValue (f : float -> ImageMessage) (action : Numeric.Action) =
            match action with
            | Numeric.Action.SetValue v -> f v
            | _ -> ImageMessage.Empty
        let lower = m.falseColorModel.lowerBound
        let upper = m.falseColorModel.upperBound
        section "Display" None (Some ("reset the range to the image's statistics", ResetCustomMinMax)) [
            propertyRow "Channel:" [
                Html.SemUi.dropDown' (AList.ofAVal m.channelOptions) m.selectedChannel SetEXRChannel channelRepr
            ]
            // min | two-handle slider (as the library filters) | max
            propertyRow "Range:" [
                div [style "display: flex; align-items: center; gap: 6px"] [
                    div [style "width: 80px; flex-shrink: 0"] [
                        Numeric.view' [NumericInputType.InputBox] lower |> UI.map (onValue SetCustomMin)
                    ]
                    div [style "flex: 1; min-width: 80px"] [
                        DualRangeSlider.view lower upper (AVal.constant false) SetCustomMin SetCustomMax ImageMessage.Empty
                    ]
                    div [style "width: 80px; flex-shrink: 0"] [
                        Numeric.view' [NumericInputType.InputBox] upper |> UI.map (onValue SetCustomMax)
                    ]
                ]
            ]
            // on/off and which map, in one line; off: grey values
            propertyRow "Colour Map:" [
                div [style "display: flex; align-items: center; gap: 8px"] [
                    div [attribute "title" "off: grey values"] [
                        GuiEx.iconCheckBox m.falseColorPreview ToggleFalseColor
                    ]
                    Html.SemUi.dropDown m.colorMap SetColorMap
                ]
            ]
            propertyRow "Legend:" [
                div [style "display: flex; align-items: center; gap: 8px"] [
                    GuiEx.iconCheckBox m.falseColorModel.useFalseColors ToggleFalseColorLegend
                    span [style "opacity: 0.75"] [text "show in main view"]
                ]
            ]
        ]

    let view (m : AdaptiveProjectedImageModel) =
        require Html.semui (
            div [] [
                div [style "position: relative; width: 100%"] [
                    viewDisplay m
                    viewTone "Shadows" "brightens the darkest tones; Tone: how far into the range counts as shadow, Radius: softening in pixels" m.shadows ShadowsMessage
                    viewTone "Highlights" "darkens the brightest tones; Tone: how far into the range counts as highlight, Radius: softening in pixels" m.highlights HighlightsMessage
                ]
            ]
        )

    let createInstrumentScene (img : aval<Option<AdaptiveProjectedImageModel>>) =
        let extract  (defaultValue : aval<'a>) (f : AdaptiveProjectedImageModel -> aval<'a>) (m : aval<Option<AdaptiveProjectedImageModel>>) =
            m |> AVal.bind (function
                | None -> defaultValue 
                | Some v -> f v
            )

        let colormapTexture : aval<ITexture> =
            img |> extract DefaultTextures.checkerboard (fun img -> 
                img.colorMap
                |> AVal.map (fun map ->
                    let resourceName = ColorMap.getColorMapFileName(map)
                    InstrumentImageVisualization.getColorMapTexture resourceName
                )
            )

        let imageTexture : aval<ITexture> =
            img |> extract DefaultTextures.checkerboard (fun img -> 
                (img.texture, img.selectedChannel) 
                ||> AVal.map2 (fun (path : string) channel ->
                        match Path.GetExtension(path).ToLower() with
                        | ".exr" ->
                            let stream = File.OpenRead path
                            let exrTexture = TextureLoading.loadImageFromStream stream (ChannelReference.ChannelWithIndex channel.idx) (Some TextureLoading.TextureFormat.OpenEXR)
                            PixTexture2d(exrTexture, true)
                        | ".tiff" | ".tif" -> 
                            let ifUsefulThisIsHowToExtractInfos = MultiBandReader.tryGetChannels path
                            match MultiBandReader.tryReadMultiBandTiff path false with
                            | Result.Ok img -> 
                                let images = InstrumentImageTextures.instrumentImageToTexture true img 
                                match Array.tryItem channel.idx images with
                                | Some img -> 
                                    PixTexture2d(img.pi, true)
                                | _ -> 
                                    Log.warn "channel of out of bounds"
                                    DefaultTextures.checkerboard.GetValue()
                            | _ -> 
                                Log.warn "could not load texture"
                                DefaultTextures.checkerboard.GetValue()
                        // as the projection loads them (Visualization.createProjectedPixTexture):
                        // the shader reads the red channel, the grayscale image
                        | ".png" | ".jpg" | ".jpeg" ->
                            try
                                PixTexture2d(PixImageMipMap [| PixImage.Load path |], true) :> ITexture
                            with e ->
                                Log.warn "[ProjectedImage] preview could not load %s: %s" path e.Message
                                whiteTex
                        | ext ->
                            Log.warn "[ProjectedImage] no preview for image format %s (%s)" ext path
                            whiteTex
                ) 
            )

        let min = img |> extract (AVal.constant 0.0) (fun m -> m.falseColorModel.lowerBound.value |> AVal.map (fun v -> float v))
        let max = img |> extract (AVal.constant 1.0) (fun m -> m.falseColorModel.upperBound.value |> AVal.map (fun v -> float v))
        let falseColor = img |> extract (AVal.constant false) (fun m -> m.falseColorPreview |> AVal.map (fun f -> not f))
        let dataType = img |> extract (AVal.constant 2) (fun m -> m.dataType |> AVal.map (fun dt -> int dt))

        // shadows/highlights: the same cached masks the surface projection samples
        let shadowAmount = img |> extract (AVal.constant 0.0f) (fun m -> m.shadows.amount.value |> AVal.map float32)
        let highlightAmount = img |> extract (AVal.constant 0.0f) (fun m -> m.highlights.amount.value |> AVal.map float32)
        let maskKey =
            img |> extract (AVal.constant None) (fun m ->
                adaptive {
                    let! path = m.texture
                    let! channel = m.selectedChannel
                    let! lo = m.falseColorModel.lowerBound.value
                    let! hi = m.falseColorModel.upperBound.value
                    let! dataType = m.dataType
                    let! shadows = m.shadows.Current
                    let! highlights = m.highlights.Current
                    return PRo3D.ImageMapping.ShadowsHighlights.maskKey path channel.idx (V2d(lo, hi)) (int dataType) shadows highlights
                })
        let maskTexture : aval<ITexture> =
            maskKey |> AVal.map (fun key ->
                match key |> Option.bind PRo3D.ImageMapping.ShadowsHighlights.tryMask with
                | Some masks -> PixTexture2d(PixImageMipMap [| masks |], false) :> ITexture
                | None -> whiteTex)

        Sg.fullScreenQuad
        |> Sg.noEvents
        |> Sg.texture "InstrumentImage" imageTexture
        |> Sg.texture "ColormapTexture" colormapTexture
        |> Sg.uniform "MinValue" min
        |> Sg.uniform "MaxValue" max
        |> Sg.uniform "UseFalseColor" falseColor
        |> Sg.uniform "DataType" dataType
        |> Sg.texture "ShadowsHighlightsMask" maskTexture
        |> Sg.uniform "ShadowAmount" shadowAmount
        |> Sg.uniform "HighlightAmount" highlightAmount
        |> Sg.shader {
            do! (Shaders.hshColors)
        }

    let view2DRelative (img : aval<Option<AdaptiveProjectedImageModel>>) =

        let instrumentVisualization = createInstrumentScene img

        let cameraView = CameraView.look V3d.OOI V3d.OON V3d.OIO
        let frustum' = Frustum.ortho (Box3d.FromMinAndSize(-V3d.III, V3d.III))

        require Html.semui (
            div [style "width: 100%; height: 200px; display: flex; align-items: center; justify-content: center; border: solid 2px black; background: rgb(0, 0, 0, 0.5);"] [
                let style = [style "position: relative; width: 200px; height: 100%; padding: 2px; display: block; min-width: 0;"; attribute "showLoader" "false"]
                renderControl (AVal.constant (Camera.create cameraView frustum')) style instrumentVisualization
            ]
        )        
    
        
        
        