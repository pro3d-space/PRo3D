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

/// Where a projected image's pose comes from (the *Orientation Source*); maps 1:1 to
/// PRo3D.SPICE.ProjectorPose, see there.
/// - Spice: position and attitude from SPICE (SPK + spacecraft CK), aimed at the target.
/// - MbiBased: the sidecar's SC_QUAT attitude and TRG_POS.
/// - MbiAttitudeSpiceRange: the sidecar's SC_QUAT attitude, target on the boresight at
///   the SPICE range (for sidecars without TRG_POS).
type ProjectionMethod =
    | Spice = 0
    | MbiBased = 1
    | MbiAttitudeSpiceRange = 2

/// Where a STAC item keeps the files a projection needs. `mbi` and `image` are
/// what the projection reads; the statistics sidecar and the overview are optional.
type StacAssets =
    {
        mbi           : string
        image         : string
        imageMetadata : Option<string>
        overview      : Option<string>
    }

/// Observation geometry of one STAC hit, see StacSearch.metrics.
type StacMetrics =
    {
        /// spacecraft to the central pixel's surface point, metres; to the queried
        /// point when the boresight misses the loaded surface
        rangeM           : float
        /// ground size of one pixel at rangeM, metres
        pixelSizeM       : float
        /// sun incidence at the central pixel (at the queried point when the
        /// boresight misses), degrees. None when the image carries no sun position.
        incidenceDeg     : Option<float>
        /// sun incidence at the picked point itself, degrees (0 = sun overhead,
        /// > 90 = night): what the incidence filter compares. None when the image
        /// carries no sun position or the point has no normal.
        pointIncidenceDeg : Option<float>
        /// the queried point in the image, pixels, 0-based, origin top-left
        pixel            : V2d
        /// true: range and incidence are measured at the central pixel's surface
        /// point; false: the boresight missed the loaded surface, they are the
        /// queried point's
        centralOnSurface : bool
    }

/// Where the local check of one catalog hit stands.
type StacCheck =
    /// listed by the server, not checked yet
    | Checking
    /// the point is in the image and visible: its observation geometry
    | Resolved   of StacMetrics
    /// the image's pointing could not be resolved (no sidecar, no attitude, no
    /// frustum for the instrument, no SPICE coverage); it would not project either
    | Unresolved of string

type StacCandidate =
    {
        itemId     : string
        collection : string
        title      : string
        time       : DateTime
        instrument : string
        assets     : StacAssets
        check      : StacCheck
    }

/// One cursor position being searched for. `seq` orders the searches, so a slow
/// answer to an old position cannot overwrite a newer one.
type StacQueryPoint =
    {
        seq  : int
        /// SPICE body of the picked surface, e.g. "MARS" -- matched against the
        /// items' ssys:targets
        body : string
        /// planetocentric, east-positive, degrees -- the convention of the STAC footprints
        lon  : float
        lat  : float
    }

/// How the server's list for a point came about.
type StacListing =
    /// the items whose footprint contains the point
    | ByFootprint
    /// no footprint contains the point: the items without any footprint (in the
    /// time range), to be found by their pointing alone. See docs/StacImageSearch.md.
    | WithoutFootprint

/// Images a search dropped although the server listed them.
type StacDropped =
    {
        /// pointing did not resolve. Only for WithoutFootprint: there nothing says
        /// the image shows the point, so an image that cannot be checked is not
        /// listed (in red) but counted.
        count            : int
        /// the first reason, as an example
        reason           : Option<string>
        /// the point is in the image, but its sun incidence is outside the filter
        outsideIncidence : int
        /// listed in red: the footprint matched, the pointing did not resolve
        unresolvedListed : int
        /// not checkable because the loaded SPICE kernel does not cover the image's
        /// time (or its frames). Counted apart from `count`: a kernel that covers them
        /// fixes these, and the user should be told so.
        noSpiceCoverage  : int
    }

module StacDropped =
    let none = { count = 0; reason = None; outsideIncidence = 0; unresolvedListed = 0; noSpiceCoverage = 0 }

type StacSearchStatus =
    | Idle
    /// waiting for the server
    | Searching
    /// the server found `matched` items; `checked` of them are checked so far
    | Checking of matched : int * checked : int * StacListing
    /// `matched` is how many items the server found, `kept` how many survive the
    /// local check that the point really projects into the image (and the
    /// incidence filter); the list shows at most StacSearchModel.maxResults
    | Found  of matched : int * kept : int * StacListing * StacDropped
    | Failed of string

/// A password. Prints as *** (ToString and %A), so a message or model that carries
/// one cannot write it into a log. The text is private: outside this assembly
/// `Secret.create` makes one and `Secret.reveal` is the only way to read it, so
/// every use of the plain text is found by searching for `Secret.reveal`.
[<Sealed; StructuredFormatDisplay("***")>]
type Secret internal (value : string) =
    member internal _.Value = value
    override _.ToString() = "***"

module Secret =
    let create (text : string) = Secret(text)
    /// The plain text. Keep the calls few: each one is a place the password exists.
    let reveal (secret : Secret) = secret.Value

/// What a connection attempt found (StacSearch.probe).
type StacProbe =
    /// the catalog answered a search
    | ProbeOk
    /// it wants a sign-in (401), and none was sent
    | ProbeNeedsSignIn
    /// it refused the user name and password sent (401)
    | ProbeRejected
    /// anything else: unreachable, 403, not a STAC catalog, ...
    | ProbeFailed of string

/// The sign-in dialog: an error to show (wrong password, session expired), and
/// whether an attempt is running.
type StacSignIn =
    {
        message : Option<string>
        busy    : bool
    }

/// The catalog connection (docs/StacImageSearch.md). Off at start: nothing is sent
/// to the catalog until the user enables it, or selects the Image Search tool.
type StacConnection =
    | Disconnected
    /// asking the catalog, without credentials
    | Connecting
    /// the sign-in dialog is open
    | SigningIn of StacSignIn
    /// searches run; the user signed in as, if the catalog wanted a sign-in
    | Connected of user : Option<string>
    /// the last attempt failed, and why; searches stay off
    | ConnectionFailed of string

/// STAC search for the images containing a point picked with the Image Search
/// tool. Session state only; nothing of it is saved with the scene.
[<ModelType>]
type StacSearchModel =
    {
        serverUrl    : string
        /// observation time range, UTC, "YYYY-MM-DD" (an ISO 8601 time also
        /// works); empty = open. Parsed when searching (StacSearch.parseTimeRange).
        timeFrom     : string
        timeTo       : string
        /// sun incidence at the picked point, degrees: images outside are dropped.
        /// 0 = sun overhead, 90 = sun on the horizon.
        incidenceMin : float
        incidenceMax : float
        query        : Option<StacQueryPoint>
        status       : StacSearchStatus
        candidates   : IndexList<StacCandidate>
        /// item ids whose files are being downloaded
        downloading  : HashSet<string>
        /// the result row under the mouse: its frustum and footprint are drawn like
        /// a hovered library image's (from the sidecar the check downloaded)
        hovered      : Option<string>
        connection   : StacConnection
        /// the user name the sign-in dialog starts with: the last one that worked.
        /// The password is never kept, here or anywhere else.
        lastUser     : string
    }

module StacSearchModel =
    /// No catalog is built in: the default comes from the PRO3D_STAC_URL environment
    /// variable, otherwise the user enters the server in the GIS tab.
    let defaultServerUrl =
        match System.Environment.GetEnvironmentVariable "PRO3D_STAC_URL" with
        | null -> ""
        | url -> url.Trim()

    /// What PRo3D remembers of the catalog between sessions, per user: the last catalog
    /// URL that connected and the last user name that signed in, so the panel and the
    /// sign-in dialog start with them. Never the password: it is asked for in every
    /// session. PRO3D_STAC_REMEMBERED puts it elsewhere (UI tests, so they do not
    /// overwrite the developer's own); tests in process set it directly.
    let mutable rememberedFile =
        match System.Environment.GetEnvironmentVariable "PRO3D_STAC_REMEMBERED" with
        | null | "" ->
            System.IO.Path.Combine(System.Environment.GetFolderPath System.Environment.SpecialFolder.ApplicationData, "Pro3D", "stac.json")
        | path -> path

    /// (url, user); empty strings for what is not known
    let remembered () : string * string =
        try
            let f = rememberedFile
            if System.IO.File.Exists f then
                use doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText f)
                let field (name : string) =
                    match doc.RootElement.TryGetProperty name with
                    | true, v when v.ValueKind = System.Text.Json.JsonValueKind.String -> v.GetString().Trim()
                    | _ -> ""
                field "url", field "user"
            else "", ""
        with _ -> "", ""

    /// After a successful connect: this URL, and the user if one signed in (else the
    /// remembered user stays).
    let remember (url : string) (user : Option<string>) =
        try
            let _, previousUser = remembered ()
            let user = user |> Option.defaultValue previousUser
            let f = rememberedFile
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName f) |> ignore
            let json = System.Text.Json.JsonSerializer.Serialize(dict [ "url", url.Trim(); "user", user ])
            System.IO.File.WriteAllText(f, json)
        with e -> Log.warn "[STAC] could not remember the catalog: %s" e.Message

    /// The Catalog field at start: PRO3D_STAC_URL when set (tests, scripted runs), else
    /// the last catalog that connected.
    let initialServerUrl () =
        if defaultServerUrl <> "" then defaultServerUrl
        else fst (remembered ())

    /// Searches run only while connected.
    let isConnected (s : StacSearchModel) =
        match s.connection with
        | Connected _ -> true
        | _ -> false

    /// The result list shows the best this many, by pixel size.
    [<Literal>]
    let maxResults = 100

    let initial =
        {
            serverUrl    = initialServerUrl ()
            // HERA at Didymos: arrival to the end of the nominal mission
            timeFrom     = "2026-10-01"
            timeTo       = "2027-05-31"
            // daylight only
            incidenceMin = 0.0
            incidenceMax = 90.0
            query        = None
            status       = Idle
            candidates   = IndexList.empty
            downloading  = HashSet.empty
            hovered      = None
            connection   = Disconnected
            lastUser     = snd (remembered ())
        }

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
        stacSearch           : StacSearchModel
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
        // MbiAttitudeSpiceRange: the image's own attitude and roll (SC_QUAT) with the
        // range from SPICE. Works for sidecars without TRG_POS
        // and puts the target on the boresight, which Registration then refines. MbiBased
        // is exact only where TRG_POS is; Spice ignores the image's pointing. Not persisted
        // in a scene, so every session starts here.
        projectionMethod = ProjectionMethod.MbiAttitudeSpiceRange
        useTransferFunction = true
        windingCorrection = false
        stacSearch = StacSearchModel.initial
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


type StacSearchMessage =
    | SetServerUrl    of string
    | SetTimeFrom     of string
    | SetTimeTo       of string
    | SetIncidenceMin of float
    | SetIncidenceMax of float
    /// The viewer's STAC worker started searching this point. The Query*
    /// messages below carry the search's `seq` and are dropped once a newer
    /// search has started.
    | QueryStarted  of StacQueryPoint
    /// The server's answer, every candidate still Checking, and how many items
    /// it matched.
    | QueryListed   of seq : int * array<StacCandidate> * matched : int * StacListing
    /// One candidate checked. None: the point is not in that image (or hidden
    /// in it), so the candidate goes.
    | QueryChecked  of seq : int * itemId : string * Option<StacCheck>
    /// All candidates checked (and those dropped as unresolvable), or the search failed.
    | QueryFinished of seq : int * Result<StacDropped, string>
    /// Download a candidate's files and put it on top of the projection stack.
    /// The download is started by the Viewer (it owns the worker); this model
    /// only tracks that it is running.
    | Fetch         of itemId : string
    /// Download finished: the local image path, or why it failed.
    | FetchFinished of itemId : string * Result<string, string>
    | ClearResults
    /// The result row under the mouse, or None.
    | HoverCandidate of itemId : Option<string>
    /// Connect to the catalog (the panel's switch, or selecting the Image Search
    /// tool): asks it without credentials, and opens the sign-in dialog if it wants
    /// one. The Viewer runs the attempt; ConnectFinished brings the answer.
    | Connect
    /// Stop searching and forget the credentials.
    | Disconnect
    /// The sign-in dialog's button: try these. The Viewer runs the attempt.
    | SignIn of user : string * password : Secret
    | CancelSignIn
    /// A connection attempt's answer, with the user it was made for.
    | ConnectFinished of user : Option<string> * StacProbe
    /// Delete the downloaded files (%TEMP%\PRo3D\stac). Done by the Viewer.
    | ClearCache

type ProjectedImageListMessage =
    | StacMessage of StacSearchMessage
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