namespace PRo3D.ImageMapping

open System

open Aardvark.Base
open Aardvark.UI.Primitives
open Adaptify
open PRo3D.Base

// last, so its HashSet/IndexList/Index win: Aardvark.Base carries a HashSet
// module over System.Collections.Generic.HashSet that otherwise shadows the
// adaptive one.
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
type ProjectedImageModel =
    {
        /// Stable identity. The library list is sorted destructively
        /// (SortEntriesByDistance/Date rewrite the IndexList), so an Index is
        /// unusable as a reference; the projection stack, the hover preview and
        /// the edit-panel selection all key off this instead.
        /// NonAdaptive because it never changes for a given image -- that keeps
        /// it a plain Guid on the adaptive type, so lookups and click handlers
        /// do not have to bind it (same as Bookmark.key).
        [<NonAdaptive>]
        id                : Guid
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

module ProjectedImages =
    /// Upper bound on how many images can be projected at once. Sizes the
    /// shader's uniform matrix/min-max arrays and the projected-texture array:
    /// 32 * M44f = 2 KB, far under the 16 KB UBO floor guaranteed by GL 4.1 (so
    /// no storage buffer, so macOS works), and ~128 MB of texture at AFC
    /// 1024^2 x R32f.
    ///
    /// The shader side spells the same bound as a *type* (`Arr<N<32>, M44f>` in
    /// ImageProjection.fs) and F# has no way to derive one from the other, so
    /// raising the cap means editing both, together.
    [<Literal>]
    let maxCount = 32

[<ModelType>]
type ProjectedImageListModel =
    {
        /// The library: every loaded image. Sorted destructively by the
        /// SortEntries* messages, so positions here are not identities.
        images               : IndexList<ProjectedImageModel>
        /// Draw order, bottom -> top; the topmost layer covering a fragment
        /// wins. Holds ids into `images`, so sorting the library leaves it
        /// alone. Never longer than ProjectedImages.maxCount.
        stack                : IndexList<Guid>
        /// Library or stack entry under the mouse. Previewed on top of the
        /// stack and given a footprint; see ProjectedImageListModel.effectiveStack.
        hoveredImage         : Option<Guid>
        /// Target of the edit panel / 2D preview.
        selectedImage        : Option<Guid>
        /// Library rows whose inline edit panel is expanded. A set, not a list:
        /// membership is the only question ever asked of it.
        editImages           : HashSet<Guid>
        projectionOpacity    : NumericInput
        boresightAdjustment  : BoresightAdjustment
        cameraState          : OrbitState
        instrumentVisibility : InstrumentVisibilityMode
        lightingMode         : LightingMode
        projectionMethod     : ProjectionMethod
    }

module ProjectedImageListModel =

    let tryFind (id : Guid) (m : ProjectedImageListModel) =
        m.images |> IndexList.tryFind (fun _ i -> i.id = id)

    let isInStack (id : Guid) (m : ProjectedImageListModel) =
        m.stack |> IndexList.exists (fun _ i -> i = id)

    /// What actually gets projected: the stack, plus the hovered image on top
    /// as a preview when it is not already part of the stack (D4). Hovering an
    /// image that *is* in the stack must not duplicate its layer -- it only
    /// drives the footprint and the UI badge -- so it is filtered out here.
    /// Truncated to the cap, dropping from the bottom so the preview is always
    /// the layer you see.
    let effectiveStack (m : ProjectedImageListModel) : IndexList<Guid> =
        let stack =
            match m.hoveredImage with
            | Some h when not (isInStack h m) -> m.stack |> IndexList.add h
            | _ -> m.stack
        let overflow = stack.Count - ProjectedImages.maxCount
        if overflow > 0 then stack |> IndexList.skip overflow else stack

    let initial : ProjectedImageListModel = {
        images = IndexList.Empty;
        stack = IndexList.Empty;
        hoveredImage = None;
        selectedImage = None;
        editImages = HashSet.empty;
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
    | SelectImage of Guid
    | EditImage of Guid
    | LoadImagesDir of string
    | ImageMessage of Guid * ImageMessage
    /// Append to the top of the projection stack. Ignored when the image is
    /// already in the stack or the stack is at ProjectedImages.maxCount.
    | AddToStack of Guid
    | RemoveFromStack of Guid
    /// Move a stack entry to the given position, 0 = bottom. Clamped.
    | MoveInStack of Guid * int
    /// Mouse entered/left a library or stack row; drives the preview layer and
    /// the footprint. None on leave.
    | HoverImage of Option<Guid>
    /// Frame this image's footprint. Handled by the Viewer (it owns the camera
    /// animation), not by ProjectedImageListApp.update.
    | FlyToImage of Guid
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