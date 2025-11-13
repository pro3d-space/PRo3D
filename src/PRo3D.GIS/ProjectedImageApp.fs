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
        member x.MinValue : float = uniform?MinValue
        member x.MaxValue : float = uniform?MaxValue
        member x.UseFalseColor : bool = uniform?UseFalseColor
        member x.DataType : int = uniform?DataType

    let hshColors (v : Vertex)  = 
        fragment {
            let hshValueX = instrumentSampler.Sample(v.tc).X 
            let remappedClampedNormalizedXInt16 =
                ((min uniform.MaxValue (max uniform.MinValue (hshValueX * 65000.0))) - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
            let remappedClampedNormalizedXFloat =
                (hshValueX - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
            let remapClampNormalize =
                if uniform.UseFalseColor then
                    V4d(
                        (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                        (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                        (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                        1.0
                    )
                else 
                    colormapTextureSampler.Sample(V2d ((if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16), 0.0))
            return remapClampNormalize
        }


module ProjectedImageApp =

    let initialPath = ""

    let minValue = {
        value   = 0.0
        min     = 0.0
        max     = 65000.0
        step    = 1
        format  = "{0:0.00}"
    }

    let maxValue = {
        value   = 0.0
        min     = 0.0
        max     = 65000.0
        step    = 1
        format  = "{0:0.00}"
    }

    let initial = { 
        colorMap = ColorMap.Magma;
        useFalseColor = true;
        selectedChannel = { idx = 0; name = None }
        channelOptions = [];
        dataType = DataType.UInt16;
        defaultMinValues = [minValue.value];
        defaultMaxValues = [maxValue.value];
        inputMinValue = minValue;
        inputMaxValue = maxValue;
        texture = initialPath;
        distance = 0;
        time = new DateTime();
    }

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

        let defaultMinValues = 
            match tiffJson with
            | Some tf -> tf.image_statistics |> Array.toList |> List.map (fun x -> x.minimum)
            | None -> [0.0]

        let defaultMaxValues = 
            match tiffJson with
            | Some tf -> tf.image_statistics |> Array.toList|> List.map (fun x -> x.maximum)
            | None -> [0.0]

        let dataType = 
            match tiffJson with
            | Some tf -> 
                match tf.data_type with
                | "uint16" -> DataType.UInt16
                | "uint32" -> DataType.UInt32
                | "float" -> DataType.Float
                | _ -> DataType.UInt16
            | None -> DataType.UInt16

        let (rangeMin, rangeMax) =
            match dataType with
            | DataType.Float -> (defaultMinValues[selectedChannelIdx], defaultMaxValues[selectedChannelIdx])
            | DataType.UInt16 
            | _ -> (0, 65536)

        let inputMinValue = { minValue with value = defaultMinValues[selectedChannelIdx]; min = rangeMin; max = rangeMax}

        let inputMaxValue = { minValue with value = defaultMaxValues[selectedChannelIdx]; min = rangeMin; max = rangeMax }

        let distance =
            match tiffMbiJson with
            | Some mbi -> mbi.targetPos.Length
            | None -> 0.0

        let time =
            match tiffMbiJson with
            | Some mbi -> mbi.obs_date
            | None -> System.DateTime.MinValue // which default time?

        { initial with
            texture = Path.GetFullPath(texturePath);
            defaultMinValues = defaultMinValues;
            defaultMaxValues = defaultMaxValues;
            inputMinValue = inputMinValue;
            inputMaxValue = inputMaxValue;
            selectedChannel = channelOptions[selectedChannelIdx];
            channelOptions = channelOptions;
            dataType = dataType;
            distance = distance;
            time = time;
        }


    let update (m : ProjectedImageModel) (msg : ImageMessage) =
        match msg with
            | SetDataTypeAndRange (dataType, min, max) ->
                { m with inputMinValue = { minValue with min = min}; inputMaxValue = {minValue with max = max} }
            | SetCustomMin v -> 
                { m with inputMinValue = {minValue with value = v} }
            | SetCustomMax v -> 
                { m with inputMaxValue = {maxValue with value = v} }
            | ResetCustomMinMax ->
                { m with inputMinValue = {minValue with value = m.defaultMinValues[m.selectedChannel.idx]}; inputMaxValue = {maxValue with value = m.defaultMaxValues[m.selectedChannel.idx]} }
            | SetColorMap (map : ColorMap) ->
                { m with colorMap = map }
            | SetEXRChannel channel ->
                let (min, max) = (m.defaultMinValues[channel.idx], m.defaultMaxValues[channel.idx])
                { m with 
                    inputMinValue = {minValue with value = min};
                    inputMaxValue = {maxValue with value = max};
                    selectedChannel = channel
                }
            | ToggleFalseColor ->
                { m with useFalseColor = not m.useFalseColor }
            | ImageMessage.Empty ->
                m


    let whitePix =
        let pi = PixImage<byte>(Col.Format.RGBA, V2i.II)
        pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
        pi

    let whiteTex =
        PixTexture2d(PixImageMipMap [| whitePix :> PixImage |], false) :> ITexture

    let view (m : AdaptiveProjectedImageModel) =
        let content = 
            Html.table [ 
                Html.row "EXR Channel:" [
                    div [style "color: white;"] [
                        let channelRepr (c : Channel) = 
                            match c.name with
                            | None -> string c.idx
                            | Some name -> name
                        Html.SemUi.dropDown' (AList.ofAVal m.channelOptions) m.selectedChannel (fun value -> SetEXRChannel value) channelRepr
                        // Html.SemUi.dropDown m.channel SetEXRChannel
                    ]
                ]
                Html.row "False Color:" [
                    text "Activate: " 
                    Html.SemUi.toggleBox m.useFalseColor ToggleFalseColor
                    br []
                    Html.SemUi.dropDown m.colorMap SetColorMap
                ]
                Html.row "Minimum:" [
                    SimplePrimitives.numeric { min = 0.0; max = 65535.0; largeStep = 0.1; smallStep = 0.01 } AttributeMap.empty (m.inputMinValue.value) SetCustomMin
                    br []
                    Numeric.view' [Slider] m.inputMinValue
                    |> UI.map (fun action -> 
                        match action with
                        | Numeric.Action.SetValue v ->
                            SetCustomMin v
                        | _ ->
                            ImageMessage.Empty
                        )
                    ]
                Html.row "Maximum:"  [
                    SimplePrimitives.numeric { min = 0.0; max = 65535.0; largeStep = 0.1; smallStep = 0.01 } AttributeMap.empty (m.inputMaxValue.value) SetCustomMax
                    br []
                    div [style "width: 100%"] [
                        Numeric.numericField' m.inputMaxValue Slider
                        |> UI.map (fun action -> 
                            match action with
                            | Numeric.Action.SetValue v ->
                                SetCustomMax v
                            | _ ->
                                ImageMessage.Empty
                            )
                        ]
                    ] 
                Html.row "" [button [clazz "ui inverted button"; onClick (fun _ -> ResetCustomMinMax)] [
                        text "Reset"
                    ]
                ]
            ]

        require Html.semui (
            div [] [
                div [style "position: relative; paddingLeft: 25px; paddingTop: 25px; width: 100%"] [
                    content
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
                            PixTexture2d(exrTexture, TextureParams.empty)
                        | ".tiff" | ".tif" -> 
                            let ifUsefulThisIsHowToExtractInfos = MultiBandReader.tryGetChannels path
                            match MultiBandReader.tryReadMultiBandTiff path false with
                            | Result.Ok img -> 
                                let images = InstrumentImageTextures.instrumentImageToTexture true img 
                                match Array.tryItem channel.idx images with
                                | Some img -> 
                                    PixTexture2d(img.pi, TextureParams.empty)
                                | _ -> 
                                    Log.warn "channel of out of bounds"
                                    DefaultTextures.checkerboard.GetValue()
                            | _ -> 
                                Log.warn "could not load texture"
                                DefaultTextures.checkerboard.GetValue()
                        | ".png" | _ -> whiteTex 
                ) 
            )

        let min = img |> extract (AVal.constant 0.0) (fun m -> m.inputMinValue.value |> AVal.map (fun v -> float v))
        let max = img |> extract (AVal.constant 1.0) (fun m -> m.inputMaxValue.value |> AVal.map (fun v -> float v))
        let falseColor = img |> extract (AVal.constant false) (fun m -> m.useFalseColor)
        let dataType = img |> extract (AVal.constant 2) (fun m -> m.dataType |> AVal.map (fun dt -> int dt))

        Sg.fullScreenQuad
        |> Sg.noEvents
        |> Sg.texture "InstrumentImage" imageTexture
        |> Sg.texture "ColormapTexture" colormapTexture
        |> Sg.uniform "MinValue" min
        |> Sg.uniform "MaxValue" max
        |> Sg.uniform "UseFalseColor" falseColor
        |> Sg.uniform "DataType" dataType
        |> Sg.shader {
            do! (Shaders.hshColors)
        }

    let view2DRelative (img : aval<Option<AdaptiveProjectedImageModel>>) =

        let instrumentVisualization = createInstrumentScene img

        let cameraView = CameraView.look V3d.OOI V3d.OON V3d.OIO
        let frustum' = Frustum.ortho (Box3d.FromMinAndSize(-V3d.III, V3d.III))

        require Html.semui (
            div [style "width: 100%; height: 200px; display: flex; align-items: center; justify-content: center; margin-top: 10px; border: solid 2px black; background: rgb(0, 0, 0, 0.5);"] [
                let style = [style "position: relative; width: 200px; height: 200px; padding: 2px"; attribute "showLoader" "false"]
                renderControl (AVal.constant (Camera.create cameraView frustum')) style instrumentVisualization
            ]
        )
