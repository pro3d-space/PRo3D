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
        /// Curated header: instrument + camera system, e.g. "AFC1 (AFC)" --
        /// from the mbi sidecar's INSTRUME and the statistics sidecar's
        /// camera_system/mission_name when present. Empty when unknown.
        instrument        : string
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

    /// How strongly the stack layers other than the one in focus are faded
    /// (their opacity factor): strongly while a stack image is hovered or
    /// dragged, slightly while a library image is previewed.
    [<Literal>]
    let fadeForStackFocus = 0.35
    [<Literal>]
    let fadeForLibraryFocus = 0.75

/// A stack row being dragged (drag & drop reordering). Transient UI state, never
/// saved. `target`: the stack position (0 = bottom) the row would be dropped at,
/// previewed in effectiveStack; None until the drag enters a row.
type StackDrag =
    {
        image  : Guid
        target : Option<int>
    }

[<ModelType>]
type ProjectedImageListModel =
    {
        /// The library: every loaded image. Sorted destructively by the
        /// SortEntries* messages, so positions here are not identities.
        images               : IndexList<ProjectedImageModel>
        /// The folder the library was imported from. Saved with the scene
        /// (GisApp "projectedImagesDirectory") and re-imported on load.
        imageDirectory       : Option<string>
        /// Stack file names (bottom -> top) read from a scene, waiting for the
        /// library to be imported from imageDirectory (restoreStack). Stays set
        /// while that folder is missing, so saving the scene keeps the stack.
        /// Empty otherwise.
        [<NonAdaptive>]
        pendingStack         : list<string>
        /// Draw order, bottom -> top; the topmost layer covering a fragment
        /// wins. Holds ids into `images`, so sorting the library leaves it
        /// alone. Never longer than ProjectedImages.maxCount.
        stack                : IndexList<Guid>
        /// Library or stack entry under the mouse. Previewed on top of the
        /// stack and given a footprint; see ProjectedImageListModel.effectiveStack.
        hoveredImage         : Option<Guid>
        /// A stack row being dragged; its drop position is previewed by
        /// effectiveStack. None when no drag is in progress.
        stackDrag            : Option<StackDrag>
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
        /// Send the projected samples through the colour map and the per-image
        /// min/max remap (true), or paint the image's own RGB untouched (false).
        ///
        /// Instrument data is usually single-band with a range that means
        /// nothing on screen, so the transfer function is the default. It is in
        /// the way for an ordinary RGB image, and it is in the way for checking
        /// a projection at all: only untransformed pixels can be compared with
        /// the source image.
        useTransferFunction  : bool
        /// Estimate each OPC hierarchy's triangle winding and correct inward-wound
        /// ones, so the projector-facing test does not reject the terrain that faces
        /// the projector. Off by default: current OPC exports are wound outward, the
        /// vote loads a patch per hierarchy, and it is unreliable for near-vertical
        /// terrain (see NormalWinding). Off costs nothing.
        windingCorrection    : bool
        /// Field of view per instrument (SPICE frame name) used to project images:
        /// the kernel defaults unless the scene changed them. Saved per scene, only
        /// the values that differ from the kernels (GisApp "instrumentFovs").
        instrumentFovs       : HashMap<string, InstrumentFov>
    }

module ProjectedImageListModel =

    /// `images` as the new library, under indices the current library never used.
    ///
    /// ProjectedImageModel.id is NonAdaptive: the adaptive row built for an index
    /// keeps the id it was created with. IndexList.ofList / sortBy hand out the
    /// SAME indices every time, so a re-import or a sort would update the existing
    /// rows in place -- new texture, old id -- and +, fly-to and the stack would act
    /// on a different image than the row shows (or on none). Appending after the
    /// old list and dropping it gives every entry a fresh index, so the rows are
    /// rebuilt with the right ids.
    let replaceImages (images : seq<ProjectedImageModel>) (m : ProjectedImageListModel) : IndexList<ProjectedImageModel> =
        IndexList.append m.images (IndexList.ofSeq images) |> IndexList.skip m.images.Count

    let tryFind (id : Guid) (m : ProjectedImageListModel) =
        m.images |> IndexList.tryFind (fun _ i -> i.id = id)

    let isInStack (id : Guid) (m : ProjectedImageListModel) =
        m.stack |> IndexList.exists (fun _ i -> i = id)

    /// `stack` with `id` moved to `position` (0 = bottom, clamped): removed, then
    /// inserted, so the entries between the old and the new place shift by one.
    /// Unchanged when `id` is not in the stack.
    let moveInStack (id : Guid) (position : int) (stack : IndexList<Guid>) : IndexList<Guid> =
        match stack |> IndexList.tryFindIndex id with
        | None -> stack
        | Some idx ->
            let without = stack |> IndexList.remove idx
            let position = position |> max 0 |> min without.Count
            without |> IndexList.insertAt position id

    /// The image the projection is focused on -- the dragged stack row, else the
    /// hovered row -- and the opacity factor for every *other* layer
    /// (ProjectedImages.fadeFor*). None: nothing faded.
    let stackFocus (m : ProjectedImageListModel) : Option<Guid * float> =
        match m.stackDrag with
        | Some d -> Some (d.image, ProjectedImages.fadeForStackFocus)
        | None ->
            m.hoveredImage |> Option.map (fun id ->
                if isInStack id m then id, ProjectedImages.fadeForStackFocus
                else id, ProjectedImages.fadeForLibraryFocus)

    /// What actually gets projected: the stack, plus the hovered image on top
    /// as a preview when it is not already part of the stack (D4). Hovering an
    /// image that *is* in the stack must not duplicate its layer -- it only
    /// drives the footprint and the UI badge -- so it is filtered out here.
    /// Truncated to the cap, dropping from the bottom so the preview is always
    /// the layer you see.
    /// While a stack row is dragged, the stack is shown as it would be after the
    /// drop (live preview); cancelling the drag simply clears stackDrag.
    let effectiveStack (m : ProjectedImageListModel) : IndexList<Guid> =
        let committed =
            match m.stackDrag with
            | Some { image = id; target = Some p } -> moveInStack id p m.stack
            | _ -> m.stack
        let stack =
            match m.hoveredImage with
            | Some h when not (isInStack h m) -> committed |> IndexList.add h
            | _ -> committed
        let overflow = stack.Count - ProjectedImages.maxCount
        if overflow > 0 then stack |> IndexList.skip overflow else stack

    let private fileKey (path : string) = (System.IO.Path.GetFileName path).ToLowerInvariant()

    /// The stack as file names, bottom -> top -- what a scene stores (ids are
    /// minted per import). A stack still waiting for its folder is passed through.
    let stackFileNames (m : ProjectedImageListModel) : list<string> =
        if not m.pendingStack.IsEmpty then m.pendingStack
        else
            m.stack
            |> IndexList.toList
            |> List.choose (fun id -> tryFind id m |> Option.map (fun i -> System.IO.Path.GetFileName i.texture))

    /// Rebuilds the stack from file names after the library was imported.
    /// Returns the names not found in the library (matched exactly, else
    /// case-insensitively: a scene moved between file systems).
    let restoreStack (names : list<string>) (m : ProjectedImageListModel) : ProjectedImageListModel * list<string> =
        let images = m.images |> IndexList.toList
        let exact =
            images |> List.map (fun i -> System.IO.Path.GetFileName i.texture, i.id) |> HashMap.ofList
        let byKey =
            images |> List.map (fun i -> fileKey i.texture, i.id) |> HashMap.ofList
        let ids, _, missing =
            names
            |> List.fold (fun (ids, used, missing) name ->
                let found =
                    match HashMap.tryFind name exact with
                    | Some id -> Some id
                    | None -> HashMap.tryFind (fileKey name) byKey
                match found with
                | Some id when not (HashSet.contains id used) -> id :: ids, HashSet.add id used, missing
                | Some _ -> ids, used, missing
                | None -> ids, used, name :: missing
            ) ([], HashSet.empty, [])
        let stack = ids |> List.rev |> List.truncate ProjectedImages.maxCount |> IndexList.ofList
        { m with stack = stack; pendingStack = [] }, List.rev missing

    let initial : ProjectedImageListModel = {
        images = IndexList.Empty;
        imageDirectory = None;
        pendingStack = [];
        stack = IndexList.Empty;
        hoveredImage = None;
        stackDrag = None;
        selectedImage = None;
        editImages = HashSet.empty;
        projectionOpacity = { Numeric.init with min = 0.0; max = 1.0; step = 0.01; value = 1.0 }
        boresightAdjustment = BoresightAdjustment.identity
        cameraState = OrbitState.create V3d.Zero 0.0 0.0 (2.0 * (3389.5 * 1000.0))
        instrumentVisibility = InstrumentVisibilityMode.Off
        lightingMode = LightingMode.Off
        // MbiBased, not Spice: Spice ignores the image's own attitude (it aims the
        // boresight at the body centre with a fixed roll); MbiBased uses the sidecar's
        // SC_QUAT + TRG_POS. Not persisted in a scene, so every session starts here.
        projectionMethod = ProjectionMethod.MbiBased
        useTransferFunction = true
        windingCorrection = false
        instrumentFovs = InstrumentFov.defaults
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
    /// Drag & drop in the stack panel: a row starts being dragged ...
    | StackDragStart of Guid
    /// ... enters the row at this stack position (previewed live) ...
    | StackDragOver of int
    /// ... is dropped there: moves it and ends the drag, in one message (the
    /// move rebuilds the panel, so the source row may never see its dragend) ...
    | StackDrop of Guid * int
    /// ... or the drag ends without a drop (Esc, released elsewhere): back to
    /// the committed order.
    | StackDragEnd
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
    | ToggleTransferFunction
    | ToggleWindingCorrection
    | InstrumentFovMessage of InstrumentFovMessage
    /// User picked a SPICE kernel root folder to load the kernel (and
    /// observation time) the selected image's mbi sidecar was generated
    /// against. Handled by GisApp.update (needs the mbi + spice state that
    /// live outside this model), not locally.
    | LoadSpiceAndTime of directory : string
    | Nop