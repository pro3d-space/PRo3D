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

    type UniformScope with
        member x.MinValue : float32 = uniform?MinValue
        member x.MaxValue : float32 = uniform?MaxValue
        member x.UseFalseColor : bool = uniform?UseFalseColor
        member x.DataType : int = uniform?DataType
        member x.Brightness : float32 = uniform?Brightness
        member x.Contrast : float32 = uniform?Contrast

    let hshColors (v : Vertex)  = 
        fragment {
            let hshValueX = instrumentSampler.Sample(v.tc).X 
            let remappedClampedNormalizedXInt16 =
                ((min uniform.MaxValue (max uniform.MinValue (hshValueX * 65000.0f))) - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
            let remappedClampedNormalizedXFloat =
                (hshValueX - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
            let normalized =
                PRo3D.ImageMapping.BrightnessContrastShader.apply
                    (if uniform.DataType = 2 then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16)
                    uniform.Brightness uniform.Contrast
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
        resolution = None;
        phaseAngle = None;
        time = new DateTime();
        falseColorModel = projectedImageLegend (Range1d(numericInput.min, numericInput.max)) (InstrumentImageVisualization.getResourceStream (ColorMap.getColorMapFileName ColorMap.Magma) ())
        falseColorPreview = false
        brightnessContrast = BrightnessContrast.init
    }

    /// Data type and per-band (min, max) of the finite samples, read from the
    /// TIFF itself -- for images delivered without the `<image>.tif.json`
    /// statistics sidecar. Without it the range was 0..0, which the shader
    /// divides by: the projection came out black. One pass over the pixels,
    /// at import only.
    let private tryStatisticsFromImage (texturePath : string) : Option<DataType * (float * float)[]> =
        let range (n : int) (value : int -> float) =
            let mutable lo = Double.PositiveInfinity
            let mutable hi = Double.NegativeInfinity
            for i in 0 .. n - 1 do
                let v = value i
                if Double.IsFinite v then
                    if v < lo then lo <- v
                    if v > hi then hi <- v
            if lo < hi then (lo, hi)
            elif lo = hi then (lo, lo + 1.0)
            else (0.0, 1.0)
        try
            match MultiBandReader.tryReadMultiBandTiff texturePath false with
            | Result.Error e ->
                Log.warn "[ProjectedImage] no statistics for %s: %s" texturePath e
                None
            | Result.Ok r ->
                match r.buffers with
                | Float32Bands b -> Some (DataType.Float,  b |> Array.map (fun a -> range a.Length (fun i -> float a.[i])))
                | UInt16Bands b  -> Some (DataType.UInt16, b |> Array.map (fun a -> range a.Length (fun i -> float a.[i])))
                | UInt32Bands b  -> Some (DataType.UInt32, b |> Array.map (fun a -> range a.Length (fun i -> float a.[i])))
                | Int16Bands b   -> Some (DataType.UInt16, b |> Array.map (fun a -> range a.Length (fun i -> float a.[i])))
                | Int32Bands b   -> Some (DataType.UInt32, b |> Array.map (fun a -> range a.Length (fun i -> float a.[i])))
        with e ->
            Log.warn "[ProjectedImage] no statistics for %s: %s" texturePath e.Message
            None

    let loadFile (texturePath : string) =
        let (tiffMbiJson, tiffJson) = InstrumentMetadata.tryParseMetadataForImagePath texturePath

        // Plain image formats (synthetic COP renders) come without the
        // statistics sidecar and are sampled as normalized [0,1] -- treat them
        // as float over the unit range, otherwise the defaults (uint16 remap
        // over an empty 0..0 range) turn the projection into NaNs.
        let isPlainImage =
            match (Path.GetExtension texturePath).ToLowerInvariant() with
            | ".png" | ".jpg" | ".jpeg" -> true
            | _ -> false

        // a TIFF without the statistics sidecar: take them from the image
        let imageStatistics =
            match tiffJson with
            | None when not isPlainImage -> tryStatisticsFromImage texturePath
            | _ -> None

        let channels =
            match tiffJson, imageStatistics with
            | Some tf, _ -> tf.channels
            | None, Some (_, ranges) when ranges.Length > 0 -> ranges.Length
            | _ -> 1

        let channelOptions = [ 0 .. channels - 1 ] |> List.map (fun channel -> {idx = channel; name = None})

        let selectedChannelIdx = 0

        let defaultMinValues =
            match tiffJson, imageStatistics with
            | Some tf, _ -> tf.image_statistics |> Array.toList |> List.map (fun x -> x.minimum)
            | None, Some (_, ranges) when ranges.Length > 0 -> ranges |> Array.toList |> List.map fst
            | _ -> [0.0]

        let defaultMaxValues =
            match tiffJson, imageStatistics with
            | Some tf, _ -> tf.image_statistics |> Array.toList|> List.map (fun x -> x.maximum)
            | None, Some (_, ranges) when ranges.Length > 0 -> ranges |> Array.toList |> List.map snd
            | _ -> if isPlainImage then [1.0] else [0.0]

        let dataType =
            match tiffJson, imageStatistics with
            | Some tf, _ ->
                match tf.data_type with
                | "uint16" -> DataType.UInt16
                | "uint32" -> DataType.UInt32
                | "float" -> DataType.Float
                | _ -> DataType.UInt16
            | None, Some (dt, _) -> dt
            | None, None -> if isPlainImage then DataType.Float else DataType.UInt16

        let rangeOffset = 0.1 * (defaultMaxValues[selectedChannelIdx] - defaultMinValues[selectedChannelIdx])

        let (rangeMin, rangeMax) = (defaultMinValues[selectedChannelIdx] - rangeOffset, defaultMaxValues[selectedChannelIdx] + rangeOffset)

        let stepSize = (rangeMax-rangeMin) / 1000.0;

        let inputMinValue = { numericInput with value = defaultMinValues[selectedChannelIdx]; min = rangeMin; max = rangeMax; step = stepSize}

        let inputMaxValue = { numericInput with value = defaultMaxValues[selectedChannelIdx]; min = rangeMin; max = rangeMax; step = stepSize }

        // what the filters compare: one function for library images and catalog
        // results (ImageFilter.valuesOf), so both are filtered alike
        let values = ImageFilter.valuesOf (tiffMbiJson, tiffJson)
        // the image list's Preview column may serve this file's thumbnail
        PRo3D.GIS.LibraryThumbnails.register texturePath
        let distance = values.distance
        let instrument = values.instrument
        let resolution = values.resolution
        let phaseAngle = values.phaseAngle
        let time = values.time



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
                resolution       = resolution;
                phaseAngle       = phaseAngle;
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

            | BrightnessContrastMessage action ->
                let bc = m.brightnessContrast
                let bc =
                    match action with
                    | SetBrightness a -> { bc with brightness = Numeric.update bc.brightness a }
                    | SetContrast a -> { bc with contrast = Numeric.update bc.contrast a }
                    | ResetBrightnessContrast -> BrightnessContrast.init
                { m with brightnessContrast = bc }
            | ImageMessage.Empty ->
                m


    let whitePix =
        let pi = PixImage<byte>(Col.Format.RGBA, V2i.II)
        pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
        pi

    let whiteTex =
        PixTexture2d(PixImageMipMap [| whitePix :> PixImage |], false) :> ITexture

    /// A Properties section: a header row (like Registration) -- `lead` before the
    /// title (e.g. a switch), `extras` after it (e.g. a hint), an optional reset icon at
    /// the right -- then label | control rows.
    let private section (title : string) (tooltip : Option<string>)
                        (lead : list<DomNode<ImageMessage>>) (extras : list<DomNode<ImageMessage>>)
                        (reset : Option<string * ImageMessage>) (rows : list<DomNode<ImageMessage>>) =
        let header =
            tr [] [
                th [
                    yield attribute "colspan" "2"
                    match tooltip with
                    | Some t -> yield attribute "title" t
                    | None -> ()
                ] [
                    div [style "display: flex; align-items: center; gap: 6px"] [
                        yield! lead
                        yield text title
                        yield! extras
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

    /// A row that is dimmed and inert while `enabled` is false.
    let private switchedRow (enabled : aval<bool>) (label : string) (control : list<DomNode<ImageMessage>>) =
        let attributes =
            amap {
                let! enabled = enabled
                if not enabled then yield style "opacity: 0.4; pointer-events: none"
            } |> AttributeMap.ofAMap
        Incremental.tr attributes (
            AList.ofList [
                td [clazz "collapsing"] [text label]
                td [] control
            ])

    /// Data: what is shown -- the band and the range of its values that spans the
    /// colour map (and the legend's labels). Reset: the range to the image's statistics.
    let private viewData (m : AdaptiveProjectedImageModel) =
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
        section "Data" (Some "which band is shown, and the range of its values the colour map and the legend span")
            [] [] (Some ("reset the range to the image's statistics", ResetCustomMinMax)) [
            propertyRow "Channel:" [
                Html.SemUi.dropDown' (AList.ofAVal m.channelOptions) m.selectedChannel SetEXRChannel channelRepr
            ]
            // min | two-handle slider (as the library filters) | max
            propertyRow "Range:" [
                div [style "display: flex; align-items: center; gap: 6px"] [
                    div [clazz "pro3d-range-bound"; style "width: 80px; flex-shrink: 0"] [
                        Numeric.view' [NumericInputType.InputBox] lower |> UI.map (onValue SetCustomMin)
                    ]
                    // padded at both ends: a gap to the min and max boxes
                    div [style "flex: 1; min-width: 80px; padding: 0 6px"] [
                        DualRangeSlider.view lower upper (AVal.constant false) SetCustomMin SetCustomMax ImageMessage.Empty
                    ]
                    div [clazz "pro3d-range-bound"; style "width: 80px; flex-shrink: 0"] [
                        Numeric.view' [NumericInputType.InputBox] upper |> UI.map (onValue SetCustomMax)
                    ]
                ]
            ]
        ]

    /// Tone: brightness / contrast on the normalised value, before the colour map --
    /// purely visual.
    let private viewTone (m : AdaptiveProjectedImageModel) =
        let bc = m.brightnessContrast
        let row (label : string) (input : AdaptiveNumericInput) (action : Numeric.Action -> BrightnessContrastAction) =
            propertyRow label [ Numeric.view' [NumericInputType.Slider] input |> UI.map (action >> BrightnessContrastMessage) ]
        section "Tone"
            (Some "visual only: on the image's values within its Data range, before the colour map; on the surface only with Apply Image Adjustments on")
            [] [] (Some ("reset brightness and contrast", BrightnessContrastMessage ResetBrightnessContrast)) [
            row "Brightness:" bc.brightness SetBrightness
            row "Contrast:" bc.contrast SetContrast
        ]

    /// False Colour: the header's checkbox switches the colour map on (off: grey
    /// values); the map and the legend are dimmed while it is off.
    let private viewFalseColour (m : AdaptiveProjectedImageModel) =
        let on = m.falseColorPreview
        section "False Colour" (Some "off: grey values, in the preview and on the surface")
            [ div [style "font-weight: normal"] [ GuiEx.iconCheckBox on ToggleFalseColor ] ] [] None [
            switchedRow on "Colour Map:" [
                Html.SemUi.dropDown m.colorMap SetColorMap
            ]
            switchedRow on "Legend:" [
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
                    viewData m
                    viewTone m
                    viewFalseColour m
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
        let brightness = img |> extract (AVal.constant 0.0f) (fun m -> m.brightnessContrast.brightness.value |> AVal.map float32)
        let contrast = img |> extract (AVal.constant 1.0f) (fun m -> m.brightnessContrast.contrast.value |> AVal.map float32)

        Sg.fullScreenQuad
        |> Sg.noEvents
        |> Sg.texture "InstrumentImage" imageTexture
        |> Sg.texture "ColormapTexture" colormapTexture
        |> Sg.uniform "MinValue" min
        |> Sg.uniform "MaxValue" max
        |> Sg.uniform "UseFalseColor" falseColor
        |> Sg.uniform "DataType" dataType
        |> Sg.uniform "Brightness" brightness
        |> Sg.uniform "Contrast" contrast
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
    
        
        
        