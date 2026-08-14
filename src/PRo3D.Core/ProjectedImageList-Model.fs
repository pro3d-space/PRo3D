namespace PRo3D.ImageMapping

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI.Primitives
open Adaptify
open PRo3D.Base

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
type ProjectedImageModel =
    {
        colorMap          : ColorMap
        selectedChannel   : Channel
        channelOptions    : list<Channel>
        dataType          : DataType
        defaultMinValues  : list<float>
        defaultMaxValues  : list<float>
        texture           : string
        distance          : float
        time              : System.DateTime
        falseColorPreview : bool
        falseColorModel   : FalseColorsModel
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

type InstrumentVisibilityMode = 
    | Off = 0
    | RelativeCount = 1

type LightingMode =
    | Off = 0
    | SunDirect = 1
    | SunShadow = 2

/// Which of InstrumentProjection's two orientation computations is used to place a
/// projected image: the SPICE-derived (target-body-lookat) trafo, or the mbi
/// sidecar's measured spacecraft quaternion.
type ProjectionMethod =
    | Spice = 0
    | MbiBased = 1

[<ModelType>]
type ProjectedImageListModel =
    {
        images               : IndexList<ProjectedImageModel>
        selectedImage        : Option<Index>
        editImages           : Index list
        projectionOpacity    : NumericInput
        boresightAdjustment  : BoresightAdjustment
        cameraState          : OrbitState
        instrumentVisibility : InstrumentVisibilityMode
        lightingMode         : LightingMode
        projectionMethod     : ProjectionMethod
    }

module ProjectedImageListModel =
    let initial : ProjectedImageListModel = {
        images = IndexList.Empty;
        selectedImage = None;
        editImages = List.Empty;
        projectionOpacity = { Numeric.init with min = 0.0; max = 1.0; step = 0.01; value = 1.0 }
        boresightAdjustment = BoresightAdjustment.identity
        cameraState = OrbitState.create V3d.Zero 0.0 0.0 (2.0 * (3389.5 * 1000.0))
        instrumentVisibility = InstrumentVisibilityMode.Off
        lightingMode = LightingMode.Off
        projectionMethod = ProjectionMethod.Spice
    }

type ImageMessage =
    | SetCustomMin of float
    | SetCustomMax of float
    | ResetCustomMinMax
    | SetColorMap of ColorMap
    | ToggleFalseColor
    | ToggleFalseColorLegend
    | SetEXRChannel of Channel
    | Empty


type ProjectedImageListMessage = 
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
    | SetInstrumentVisbilityMode of InstrumentVisibilityMode
    | SetLightingMode of LightingMode
    | SetProjectionMethod of ProjectionMethod
    /// User picked a SPICE kernel root folder to load the kernel (and
    /// observation time) the selected image's mbi sidecar was generated
    /// against. Handled by GisApp.update (needs the mbi + spice state that
    /// live outside this model), not locally.
    | LoadSpiceAndTime of directory : string
    | Nop