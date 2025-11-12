namespace PRo3D.ImageMapping.Model

open Aardvark.UI.Primitives
open Adaptify

open FSharp.Data.Adaptive


type ColorMap =
    | Magma = 0
    | Plasma = 1
    | TwilightShifted = 2
    | Viridis = 3
    | PiYG = 4
    | Vanimo = 5 

type DataType =
    | UInt32 = 0
    | UInt16 = 1
    | Float = 2

module ColorMap =
    let getColorMapFileName (map: ColorMap) =
        match map with
        | ColorMap.Magma -> "magma.png"
        | ColorMap.Plasma -> "plasma.png"
        | ColorMap.TwilightShifted -> "twilight_shifted.png"
        | ColorMap.Viridis -> "viridis.png"
        | ColorMap.PiYG -> "piyg.png"
        | ColorMap.Vanimo -> "vanimo.png"
        | _ -> "magma.png"


type Channel = 
    {
        idx : int
        name : Option<string>
    }

[<ModelType>]
type Image =
    {
        colorMap        : ColorMap
        useFalseColor   : bool
        selectedChannel : Channel
        channelOptions  : list<Channel>
        dataType        : DataType
        defaultMinValues : list<float>
        defaultMaxValues : list<float>
        inputMinValue : NumericInput
        inputMaxValue : NumericInput
        texture : string
        distance: float
        time: System.DateTime
    }

[<ModelType>]
type BoresightAdjustment =
    {
        roll : NumericInput
        pitch : NumericInput
        yaw : NumericInput

    }

module BoresightAdjustment =
    let identity =
        {
            roll = { Numeric.init with min = -180.0; max = 180.0; value = 0.0 }
            pitch = { Numeric.init with min = -180.0; max = 180.0; value = 0.0 }
            yaw = { Numeric.init with min = -180.0; max = 180.0; value = 0.0 }
        }

[<ModelType>]
type ProjectedImagesModel =
    {
        images          : IndexList<Image>
        selectedImage   : Option<Index>
        editImages      : Index list
        projectionOpacity : NumericInput
        boresightAdjustment : BoresightAdjustment
        cameraState     : OrbitState
    }

type ImageMessage =
    | SetCustomMin of float
    | SetCustomMax of float
    | ResetCustomMinMax
    | SetColorMap of ColorMap
    | ToggleFalseColor
    | SetEXRChannel of Channel
    | SetDataTypeAndRange of DataType * float * float
    | Empty


type ProjectedImagesMessage = 
    | OrbitCameraMessage of OrbitMessage
    | SelectImage of Index
    | EditImage of Index
    | LoadImagesDir of string
    | ImageMessage of Index * ImageMessage
    | SortEntriesByDistance
    | SortEntriesByDate
    | SetProjectionOpacity of Numeric.Action
    | SetRoll of Numeric.Action
    | SetYaw of Numeric.Action
    | SetPitch of Numeric.Action
    | Nop