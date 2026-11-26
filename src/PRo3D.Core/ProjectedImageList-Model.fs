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

/// Brightness and contrast of one image, on its normalised value (0..1):
/// brightness shifts it, contrast scales it around mid grey.
[<ModelType>]
type BrightnessContrast =
    {
        brightness : NumericInput
        contrast   : NumericInput
    }

module BrightnessContrast =
    /// Brightness 0, contrast 1: the image unchanged.
    let init : BrightnessContrast =
        {
            brightness = { Numeric.init with min = -1.0; max = 1.0; step = 0.01; value = 0.0; format = "{0:0.00}" }
            contrast   = { Numeric.init with min = 0.0;  max = 3.0; step = 0.01; value = 1.0; format = "{0:0.00}" }
        }

type BrightnessContrastAction =
    | SetBrightness of Numeric.Action
    | SetContrast   of Numeric.Action
    | ResetBrightnessContrast

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
        /// Ground resolution at the target, m/px: distance × the angle one pixel spans
        /// (InstrumentProjection.tryResolution). None without sidecar distance, image
        /// width or a known instrument field of view.
        resolution        : Option<float>
        /// Phase angle at the target, degrees: the angle between the directions to the
        /// sun and to the camera (ProjectedImageModel.phaseAngleOf). None without sidecar.
        phaseAngle        : Option<float>
        time              : System.DateTime
        /// Curated header: instrument + camera system, e.g. "AFC1 (AFC)" --
        /// from the mbi sidecar's INSTRUME and the statistics sidecar's
        /// camera_system/mission_name when present. Empty when unknown.
        instrument        : string
        falseColorPreview : bool
        falseColorModel   : FalseColorsModel
        /// On the normalised value, before the colour map. Not persisted.
        brightnessContrast : BrightnessContrast
    }

module ProjectedImageModel =

    /// Phase angle in degrees from an image's sidecar vectors (km, J2000): `sunFromTarget`
    /// (SUN_POS*, sun relative to the target) and `targetFromCamera` (TRG_POS*, target
    /// minus spacecraft). The angle at the target between the sun and the camera: 0° the
    /// sun behind the camera (no shadows), 90° side lighting, toward 180° backlit.
    /// None when either vector has no length.
    let phaseAngleOf (sunFromTarget : V3d) (targetFromCamera : V3d) : Option<float> =
        let toSun = sunFromTarget
        let toCamera = -targetFromCamera
        if toSun.Length <= 0.0 || toCamera.Length <= 0.0 then None
        else
            let c = Vec.dot (Vec.normalize toSun) (Vec.normalize toCamera) |> max -1.0 |> min 1.0
            Some (Conversion.DegreesFromRadians (acos c))

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

/// How the scene is lit (GIS View -> Settings -> Sun / Lighting Mode). Saved as its
/// number: 0 is what "Off" meant before Headlight existed (a camera light on untextured
/// surfaces), so older scenes keep their look.
type LightingMode =
    /// A light at the camera on every surface (textured or not), so the shape shows.
    /// Never on projected images.
    | Headlight = 0
    /// shaded by the real sun direction (Lommel-Seeliger over the terrain normal)
    | SunDirect = 1
    /// SunDirect plus cast shadows
    | SunShadow = 2
    /// no lighting at all: textures and projected images as they are, untextured
    /// surfaces flat. The default of a new scene.
    | Off = 3

module LightingMode =
    /// a mode that lights by the sun
    let isSun (mode : LightingMode) =
        mode = LightingMode.SunDirect || mode = LightingMode.SunShadow

/// Where a projected image's pose comes from (the *Orientation Source*); maps 1:1 to
/// PRo3D.SPICE.ProjectorPose, see there.
/// - Spice: SPICE only -- SPK position and the instrument's own frame (CK x frames
///   kernel), NOT aimed: the pointing SPICE predicts, FK mounting/misalignment included.
/// - MbiBased: the sidecar's SC_QUAT attitude and TRG_POS.
/// - MbiAttitudeSpiceRange: the sidecar's SC_QUAT attitude, target on the boresight at
///   the SPICE range (for sidecars without TRG_POS).
/// - SpiceAimed: SPK position and spacecraft CK attitude (no frames kernel), then turned
///   onto the target centre -- what "Spice" used to be. Lands every image on the body,
///   and with that hides any pointing error.
type ProjectionMethod =
    | Spice = 0
    | MbiBased = 1
    | MbiAttitudeSpiceRange = 2
    | SpiceAimed = 3

/// The *Orientation Source* dropdown: order and labels (the enum names are not for users).
module OrientationSource =
    let all =
        [ ProjectionMethod.Spice; ProjectionMethod.SpiceAimed
          ProjectionMethod.MbiAttitudeSpiceRange; ProjectionMethod.MbiBased ]
    let label (m : ProjectionMethod) =
        match m with
        | ProjectionMethod.MbiAttitudeSpiceRange -> "MBI attitude, SPICE range"
        | ProjectionMethod.MbiBased -> "MBI (SC_QUAT + TRG_POS)"
        | ProjectionMethod.Spice -> "SPICE (instrument frame)"
        | ProjectionMethod.SpiceAimed -> "SPICE, aimed at target"
        | other -> string other

/// What is projected (Projection Settings -> Projection). Saved with the scene as its
/// number (GisApp "projectionStackMode"); a scene without it is FullStack.
/// - FullStack: every stack image, the top one wins where they overlap; a hovered
///   image is previewed on top.
/// - Selected: only the selected image; a hovered image is shown instead while hovered.
/// - FirstInStack: only the stack's top image; a hovered image is shown instead while
///   hovered.
type ProjectionStackMode =
    | FullStack = 0
    | Selected = 1
    | FirstInStack = 2

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
        /// spacecraft to the picked point, metres
        rangeM           : float
        /// ground size of one pixel at rangeM, metres
        pixelSizeM       : float
        /// sun incidence at the picked point itself, degrees (0 = sun overhead,
        /// > 90 = night): what the incidence filter compares. None when the image
        /// carries no sun position or the point has no normal.
        pointIncidenceDeg : Option<float>
        /// the queried point in the image, pixels, 0-based, origin top-left
        pixel            : V2d
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

/// What the image lists show of a catalog result, from its downloaded metadata --
/// computed like a library image's (ImageFilter.valuesOfWith, with the surface's body
/// as the fallback target, which a library import does not have).
type StacImageValues =
    {
        /// INSTRUME (and camera system), as the library shows it
        instrument : string
        /// distance to the target's centre, km: |TRG_POS|, else the SPICE range; 0 when unknown
        distanceKm : float
        /// ground resolution at the target's centre, m/px
        resolution : Option<float>
    }

type StacCandidate =
    {
        itemId     : string
        collection : string
        title      : string
        time       : DateTime
        /// the catalog's instrument name; `values` has the metadata's once checked
        instrument : string
        assets     : StacAssets
        check      : StacCheck
        /// from the downloaded metadata; None until the candidate is checked
        values     : Option<StacImageValues>
        /// Some: the catalog lists the item for another body (its targets, or "no
        /// target" for an item naming none), found by pointing alone
        /// (StacSearchModel.includeOtherTargets). None: listed for the scene body.
        otherTarget : Option<string>
    }

/// The sortable columns of the image lists (library and Image Search).
type ImageSortKey =
    | SortInstrument
    | SortDistance
    | SortDate
    | SortResolution

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
        /// outside the panel's shared filters (ImageFilter), checked on the downloaded
        /// metadata
        outsideFilters   : int
        /// outside the sun incidence range at the picked point
        /// (StacSearchModel.incidenceMin/Max)
        outsideIncidence : int
        /// listed in red: the footprint matched, the pointing did not resolve
        unresolvedListed : int
        /// not checkable because the loaded SPICE kernel does not cover the image's
        /// time (or its frames). Counted apart from `count`: a kernel that covers them
        /// fixes these, and the user should be told so.
        noSpiceCoverage  : int
    }

module StacDropped =
    let none = { count = 0; reason = None; outsideFilters = 0; outsideIncidence = 0; unresolvedListed = 0; noSpiceCoverage = 0 }

type StacSearchStatus =
    | Idle
    /// waiting for the server
    | Searching
    /// the server found `matched` items; `progress` of them are checked so far
    | Checking of matched : int * progress : int * StacListing
    /// `matched` is how many items the server found, `kept` how many survive the
    /// local check that the point really projects into the image (and the
    /// shared filters); the list shows at most StacSearchModel.maxResults
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
        /// also check the items the catalog lists for other bodies (or none), by
        /// their pointing: an image of another target can still show this body
        includeOtherTargets : bool
        /// how many of the listed items are of other targets (includeOtherTargets);
        /// the status line tells them apart from the footprint hits
        otherTargetsListed : int
        query        : Option<StacQueryPoint>
        status       : StacSearchStatus
        candidates   : IndexList<StacCandidate>
        /// item ids whose files are being downloaded (for +, a selection or a hover
        /// preview)
        downloading  : HashSet<string>
        /// item id -> the image its download became (ProjectedImageListModel.stacImages:
        /// a hover preview, or selected / on the stack). An id whose image has gone
        /// since (the bin) is not found by tryFind.
        fetched      : HashMap<string, Guid>
        /// items to put on the stack once their download finishes (+ clicked)
        addWhenFetched : HashSet<string>
        /// the item to select once its download finishes (its name clicked)
        selectWhenFetched : Option<string>
        /// the result row under the mouse: its frustum and footprint are drawn like
        /// a hovered library image's (from the sidecar the check downloaded)
        hovered      : Option<string>
        connection   : StacConnection
        /// the user name the sign-in dialog starts with: the last one that worked.
        /// The password is never kept, here or anywhere else.
        lastUser     : string
        /// the results' sort: column and ascending; None = best resolution at the
        /// point first (StacSearch.sortCandidates)
        sort         : Option<ImageSortKey * bool>
        /// the results' Images fold is unfolded
        imagesExpanded : bool
        /// the folder the user chose for downloads (the Download Directory row); the
        /// files go into it, one folder per item (StacSearch.itemDirectory). Remembered
        /// between sessions, not saved with the scene.
        downloadDirectory : string
        /// the trash icon's confirmation dialog is open
        confirmClearCache : bool
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

    /// The remembered fields: "url", "user", "downloadDirectory"; empty strings for
    /// what is not known
    let private rememberedFields () : Map<string, string> =
        try
            let f = rememberedFile
            if System.IO.File.Exists f then
                use doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText f)
                let field (name : string) =
                    match doc.RootElement.TryGetProperty name with
                    | true, v when v.ValueKind = System.Text.Json.JsonValueKind.String -> v.GetString().Trim()
                    | _ -> ""
                Map.ofList [ "url", field "url"; "user", field "user"; "downloadDirectory", field "downloadDirectory" ]
            else Map.empty
        with _ -> Map.empty

    let private rememberedField (name : string) =
        rememberedFields () |> Map.tryFind name |> Option.defaultValue ""

    /// (url, user); empty strings for what is not known
    let remembered () : string * string =
        rememberedField "url", rememberedField "user"

    /// The remembered download directory; empty when none was chosen
    let rememberedDownloadDirectory () = rememberedField "downloadDirectory"

    /// Rewrites the file with these fields changed; the others stay.
    let private rememberFields (changes : list<string * string>) =
        let fields =
            changes |> List.fold (fun (m : Map<string, string>) (k, v) -> Map.add k v m) (rememberedFields ())
        let f = rememberedFile
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName f) |> ignore
        let json = System.Text.Json.JsonSerializer.Serialize(fields |> Map.toSeq |> dict)
        System.IO.File.WriteAllText(f, json)

    /// After a successful connect: this URL, and the user if one signed in (else the
    /// remembered user stays).
    let remember (url : string) (user : Option<string>) =
        try
            let user = user |> Option.map (fun u -> [ "user", u ]) |> Option.defaultValue []
            rememberFields (("url", url.Trim()) :: user)
        with e -> Log.warn "[STAC] could not remember the catalog: %s" e.Message

    /// The Download Directory the user chose: the next session starts with it.
    let rememberDownloadDirectory (dir : string) =
        try rememberFields [ "downloadDirectory", dir.Trim() ]
        with e -> Log.warn "[STAC] could not remember the download directory: %s" e.Message

    /// Where downloads go unless the user chose otherwise: %TEMP%/PRo3D/stac (where
    /// they went before the Download Directory existed).
    let defaultDownloadDirectory () =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PRo3D", "stac")

    /// The Download Directory at start: the remembered one, if it is a usable path,
    /// else the default.
    let initialDownloadDirectory () =
        let dir = rememberedDownloadDirectory ()
        let usable =
            dir <> "" && (try System.IO.Path.IsPathRooted dir && System.IO.Path.GetFullPath dir <> "" with _ -> false)
        if usable then dir else defaultDownloadDirectory ()

    /// This session's Download Directory: where StacSearch downloads to, and what every
    /// new panel model starts with (create), so a scene load or a new scene keeps the
    /// folder chosen in this session. The only copy besides the model's field; set it
    /// through StacSearch.setDownloadRoot.
    let mutable sessionDownloadDirectory = initialDownloadDirectory ()

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
            includeOtherTargets = true
            otherTargetsListed = 0
            query        = None
            status       = Idle
            candidates   = IndexList.empty
            downloading  = HashSet.empty
            fetched      = HashMap.empty
            addWhenFetched = HashSet.empty
            selectWhenFetched = None
            hovered      = None
            connection   = Disconnected
            lastUser     = snd (remembered ())
            sort         = None
            imagesExpanded = true
            downloadDirectory = sessionDownloadDirectory
            confirmClearCache = false
        }

    /// A new panel's search, for a new or loaded scene: `initial` (a value, made at
    /// start), with the Download Directory of this session.
    let create () = { initial with downloadDirectory = sessionDownloadDirectory }

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

/// A range filter of the image library: `lower`/`upper` are the selected bounds,
/// their min/max the whole range. Selecting the whole range switches it off.
[<ModelType>]
type RangeFilter =
    {
        lower : NumericInput
        upper : NumericInput
    }

/// The filters of the Projected Images panel, shared by the Image Library and the
/// Image Search (STAC): they hide library rows and drop catalog results by the same
/// rules (ImageFilter.passes). The stack and the selected image are left alone.
/// Transient: not saved with the scene; an import keeps them. Fixed ranges
/// (ImageFilter.initial), independent of the loaded images.
[<ModelType>]
type ImageFilter =
    {
        /// sun–target–camera angle, degrees (ProjectedImageModel.phaseAngle)
        phaseAngle   : RangeFilter
        /// ground resolution at the target, m/px
        resolution   : RangeFilter
        /// distance to the target, km (ProjectedImageModel.distance)
        distance     : RangeFilter
        /// observation time, UTC, as seconds since 1970 (ImageFilter.timeKey)
        time         : RangeFilter
        /// Instruments shown. Empty: all of them (filter off).
        instruments  : HashSet<string>
        /// the filter block was unfolded; unused since the filters are their own
        /// section (the accordion folds them), kept to leave the model alone
        expanded     : bool
    }

type RangeFilterKind =
    | PhaseAngle
    | Resolution
    | Distance
    | Time

type ImageFilterMessage =
    | SetLower of RangeFilterKind * Numeric.Action
    | SetUpper of RangeFilterKind * Numeric.Action
    | ResetRange of RangeFilterKind
    | ToggleInstrument of string
    | ClearInstruments
    | ResetAll
    | ToggleExpanded

module RangeFilter =

    /// The whole range [min, max] selected.
    let create (min : float) (max : float) (step : float) (format : string) : RangeFilter =
        let input value = { value = value; min = min; max = max; step = step; format = format }
        { lower = input min; upper = input max }

    let isActive (r : RangeFilter) =
        r.lower.value > r.lower.min || r.upper.value < r.upper.max

    let contains (v : float) (r : RangeFilter) =
        v >= r.lower.value && v <= r.upper.value

    /// Clamped to the range, and snapped to its min/max when within one step of it.
    /// The slider's step ((max - min) / 1000, ImageFilter.rangeOverWith) rarely divides
    /// the range exactly, and then the browser stops the thumb one step short of max:
    /// dragged back to the end, a bound would land just inside the range and leave the
    /// filter on, hiding the image at the end and every image without a value.
    let private clampTo (r : RangeFilter) (v : float) =
        let lo, hi = r.lower.min, r.lower.max
        let v = v |> max lo |> min hi
        // a hair over one step: the step's decimal string and the binary sum differ
        let near = r.lower.step * (1.0 + 1e-6)
        if r.lower.step <= 0.0 then v
        elif hi - v < near then hi
        elif v - lo < near then lo
        else v

    /// Clamped (and snapped) to the range; an upper bound below it is pushed along.
    let setLower (v : float) (r : RangeFilter) : RangeFilter =
        let v = clampTo r v
        { lower = { r.lower with value = v }
          upper = { r.upper with value = max v r.upper.value } }

    /// Clamped (and snapped) to the range; a lower bound above it is pushed along.
    let setUpper (v : float) (r : RangeFilter) : RangeFilter =
        let v = clampTo r v
        { lower = { r.lower with value = min v r.lower.value }
          upper = { r.upper with value = v } }

    let reset (r : RangeFilter) : RangeFilter =
        { lower = { r.lower with value = r.lower.min }
          upper = { r.upper with value = r.upper.max } }

module ImageFilter =

    /// Shown for images whose instrument is unknown (ProjectedImageModel.instrument = "").
    [<Literal>]
    let unknownInstrument = "(unknown)"

    let instrumentKey (instrument : string) =
        if String.IsNullOrWhiteSpace instrument then unknownInstrument else instrument

    /// The UTC epoch the time filter counts from.
    let private epoch = DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)

    /// An image's observation time as the time filter compares it: UTC seconds since
    /// 1970. None for an image without one (no sidecar: ProjectedImageModel.time is
    /// DateTime.MinValue). Converted like the library's date column, so both agree.
    let timeKey (t : DateTime) : Option<float> =
        if t = DateTime.MinValue then None
        else Some (t.ToUniversalTime() - epoch).TotalSeconds

    /// A time-filter value back to its UTC time.
    let timeOfKey (seconds : float) : DateTime =
        epoch.AddSeconds seconds

    /// The Date filter's range start: HERA at Didymos.
    let timeRangeStart = DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)

    /// The Date filter's range: 2026-11-01 to the end of today (UTC), today taken when
    /// the filter is created (at start, and by a new scene).
    let timeRange () =
        let endOfToday = DateTime.UtcNow.Date.AddDays(1.0).AddSeconds(-1.0)
        let key (t : DateTime) = (t - epoch).TotalSeconds
        key timeRangeStart, key endOfToday

    /// Distance and resolution span orders of magnitude (cruise to proximity): their
    /// sliders run on a log scale (DualRangeSlider), the bound boxes show real values.
    let isLogScale (kind : RangeFilterKind) =
        match kind with
        | Distance | Resolution -> true
        | PhaseAngle | Time -> false

    /// Fixed ranges, independent of the loaded images: the same filter applies to the
    /// library and to catalog results not loaded yet (docs/MultiImageProjection.md).
    /// All off: the whole ranges selected.
    let initial : ImageFilter =
        let timeMin, timeMax = timeRange ()
        let time = RangeFilter.create timeMin timeMax 1.0 "{0:0}"
        {
            phaseAngle   = RangeFilter.create 0.0 180.0 0.1 "{0:0.0}"
            resolution   = RangeFilter.create 0.001 100000.0 0.001 "{0:0.000}"
            distance     = RangeFilter.create 0.1 1000000000.0 0.01 "{0:0.00}"
            time         = time
            instruments  = HashSet.empty
            expanded     = true
        }

    let activeCount (f : ImageFilter) =
        [ f.phaseAngle; f.resolution; f.distance; f.time ]
        |> List.sumBy (fun r -> if RangeFilter.isActive r then 1 else 0)
        |> (+) (if f.instruments.IsEmpty then 0 else 1)

    /// What the filters let through, without their UI state: an active range's bounds,
    /// None for a range that is off. Built once for all rows (the view binds the filter
    /// once, not per row).
    type Bounds =
        {
            instruments : HashSet<string>
            distance    : Option<float * float>
            resolution  : Option<float * float>
            phaseAngle  : Option<float * float>
            time        : Option<float * float>
        }

    let private boundsOf (r : RangeFilter) =
        if RangeFilter.isActive r then Some (r.lower.value, r.upper.value) else None

    let bounds (f : ImageFilter) : Bounds =
        {
            instruments = f.instruments
            distance    = boundsOf f.distance
            resolution  = boundsOf f.resolution
            phaseAngle  = boundsOf f.phaseAngle
            time        = boundsOf f.time
        }

    /// A value against a range filter: anything passes while it is off; while it is
    /// set, an image without the value is hidden -- it cannot be said to lie inside a
    /// range someone narrowed.
    let private within (bounds : Option<float * float>) (v : Option<float>) =
        match bounds, v with
        | None, _ -> true
        | Some (lo, hi), Some v -> v >= lo && v <= hi
        | Some _, None -> false

    /// Whether a library image passes.
    let passes (b : Bounds) (instrument : string) (distance : float) (resolution : Option<float>)
               (phaseAngle : Option<float>) (time : DateTime) =
        (b.instruments.IsEmpty || HashSet.contains (instrumentKey instrument) b.instruments)
        && within b.distance (Some distance)
        && within b.resolution resolution
        && within b.phaseAngle phaseAngle
        && within b.time (timeKey time)

    /// `passes` for an image.
    let matches (f : ImageFilter) (img : ProjectedImageModel) =
        passes (bounds f) img.instrument img.distance img.resolution img.phaseAngle img.time

    /// What the filters compare, for an image known only by its metadata (a catalog
    /// result before it is loaded). Computed exactly as a library image's
    /// (ProjectedImageApp.loadFile uses this too), so a catalog image passes the
    /// filters exactly when it would pass them in the library.
    type Values =
        {
            instrument : string
            distance   : float
            resolution : Option<float>
            phaseAngle : Option<float>
            time       : DateTime
        }

    /// `valuesOf`, with the body that stands in for a TARGET SPICE cannot place (the
    /// surface's body, as for the projection), when the caller knows it.
    let valuesOfWith (fallbackBody : Option<string>) ((mbi, tif) : PRo3D.Core.InstrumentMetadata.ParsedMetadata) : Values =
        // |TRG_POS|; a sidecar without it (zero) gets the range from SPICE at the
        // observation time, as the SPICE orientation sources place the camera -- else
        // the Distance shows 0 and Resolution none, and an active Resolution filter
        // hides every such image
        let distance =
            match mbi with
            | Some mbi when mbi.targetPos.Length > 0.0 -> mbi.targetPos.Length
            | Some mbi ->
                let targets = Option.toList mbi.target @ Option.toList fallbackBody
                PRo3D.SPICE.InstrumentProjection.trySpiceDistanceKm mbi.instrument targets mbi.obs_date
                |> Option.defaultValue 0.0
            | None -> 0.0
        // curated: instrument, plus the camera system when the statistics sidecar
        // declares one and it adds information
        let instrument =
            match mbi, tif with
            | Some mbi, Some tf when not (String.IsNullOrWhiteSpace tf.camera_system) && tf.camera_system <> mbi.instrument ->
                sprintf "%s (%s)" mbi.instrument tf.camera_system
            | Some mbi, _ -> mbi.instrument
            | None, Some tf -> tf.camera_system
            | None, None -> ""
        {
            instrument = instrument
            distance   = distance
            resolution =
                match mbi, tif with
                | Some mbi, Some tf -> PRo3D.SPICE.InstrumentProjection.tryResolution mbi.instrument distance tf.image_width
                | _ -> None
            phaseAngle = mbi |> Option.bind (fun mbi -> ProjectedImageModel.phaseAngleOf mbi.sunPos mbi.targetPos)
            time       = match mbi with Some mbi -> mbi.obs_date | None -> DateTime.MinValue
        }

    /// The values of an image from its metadata alone (no surface body known: a TARGET
    /// SPICE cannot place leaves the distance unknown).
    let valuesOf (metadata : PRo3D.Core.InstrumentMetadata.ParsedMetadata) : Values =
        valuesOfWith None metadata

    /// `passes` for metadata values.
    let passesValues (b : Bounds) (v : Values) =
        passes b v.instrument v.distance v.resolution v.phaseAngle v.time

    let private mapRange (kind : RangeFilterKind) (g : RangeFilter -> RangeFilter) (f : ImageFilter) =
        match kind with
        | PhaseAngle   -> { f with phaseAngle = g f.phaseAngle }
        | Resolution   -> { f with resolution = g f.resolution }
        | Distance     -> { f with distance = g f.distance }
        | Time         -> { f with time = g f.time }

    let update (f : ImageFilter) (msg : ImageFilterMessage) : ImageFilter =
        match msg with
        | SetLower (kind, a) ->
            f |> mapRange kind (fun r -> RangeFilter.setLower (Numeric.update r.lower a).value r)
        | SetUpper (kind, a) ->
            f |> mapRange kind (fun r -> RangeFilter.setUpper (Numeric.update r.upper a).value r)
        | ResetRange kind -> f |> mapRange kind RangeFilter.reset
        | ToggleInstrument i -> { f with instruments = f.instruments |> HashSet.alter i not }
        | ClearInstruments -> { f with instruments = HashSet.empty }
        | ResetAll ->
            { f with
                phaseAngle   = RangeFilter.reset f.phaseAngle
                resolution   = RangeFilter.reset f.resolution
                distance     = RangeFilter.reset f.distance
                time         = RangeFilter.reset f.time
                instruments  = HashSet.empty }
        | ToggleExpanded -> { f with expanded = not f.expanded }

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
        /// Library, stack or STAC result entry under the mouse. Previewed on top of
        /// the stack and given a footprint; see ProjectedImageListModel.effectiveStack.
        hoveredImage         : Option<Guid>
        /// Images downloaded from the Image Search (STAC): hover previews, and those
        /// selected or put on the stack (+ or a click on the result's name). Never in
        /// the library (`images`), never saved. Found by tryFind like library images,
        /// so they render, select and edit the same. Kept while their result is listed,
        /// or while they are on the stack, selected or hovered
        /// (ProjectedImageListApp.pruneStacImages).
        stacImages           : HashMap<Guid, ProjectedImageModel>
        /// What is projected: the whole stack, the selected image or the stack's top.
        projectionMode       : ProjectionStackMode
        /// A stack row being dragged; its drop position is previewed by
        /// effectiveStack. None when no drag is in progress.
        stackDrag            : Option<StackDrag>
        /// Target of the edit panel / 2D preview.
        selectedImage        : Option<Guid>
        /// Selected Image's Preview Image sub-section is unfolded.
        previewExpanded      : bool
        /// Selected Image's Properties sub-section (edit parameters) is unfolded.
        propertiesExpanded   : bool
        /// The Images block (the library's column header and rows) is unfolded.
        libraryExpanded      : bool
        /// The library's last sort: column and ascending (the header's sort icons).
        /// Sorting rewrites `images`; None after an import.
        librarySort          : Option<ImageSortKey * bool>
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
        /// Library filters; transient, see ImageFilter.
        filter               : ImageFilter
        stacSearch           : StacSearchModel
        /// Field of view per instrument (SPICE frame name) used to project images:
        /// the kernel defaults unless the scene changed them. Saved per scene, only
        /// the values that differ from the kernels (GisApp "instrumentFovs").
        instrumentFovs       : HashMap<string, InstrumentFov>
        /// Set when the loaded frames kernel names an instrument's axes differently from
        /// its instrument kernel's pixel layout (InstrumentProjection.layoutMismatches):
        /// the warning the panel shows under its info bar. Not saved; checked whenever a
        /// kernel is loaded.
        frameKernelWarning   : Option<string>
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

    /// A library image, else a downloaded STAC image (stacImages).
    let tryFind (id : Guid) (m : ProjectedImageListModel) =
        match m.images |> IndexList.tryFind (fun _ i -> i.id = id) with
        | Some i -> Some i
        | None -> HashMap.tryFind id m.stacImages

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
    ///
    /// projectionMode Selected / FirstInStack project one image only: the hovered
    /// one while something is hovered (and no stack row is dragged), else the
    /// selected image / the top of the (drag-previewed) stack.
    let effectiveStack (m : ProjectedImageListModel) : IndexList<Guid> =
        let committed =
            match m.stackDrag with
            | Some { image = id; target = Some p } -> moveInStack id p m.stack
            | _ -> m.stack
        let solo (fallback : Option<Guid>) =
            let shown =
                match m.stackDrag, m.hoveredImage with
                | None, Some h -> Some h
                | _ -> fallback
            match shown with
            | Some id -> IndexList.single id
            | None -> IndexList.empty
        match m.projectionMode with
        | ProjectionStackMode.Selected -> solo m.selectedImage
        | ProjectionStackMode.FirstInStack -> solo (committed |> IndexList.tryLast)
        | _ ->
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
        stacImages = HashMap.empty;
        projectionMode = ProjectionStackMode.FullStack;
        stackDrag = None;
        selectedImage = None;
        previewExpanded = true;
        propertiesExpanded = true;
        libraryExpanded = true;
        librarySort = None;
        projectionOpacity = { Numeric.init with min = 0.0; max = 1.0; step = 0.01; value = 1.0 }
        boresightAdjustment = BoresightAdjustment.identity
        cameraState = OrbitState.create V3d.Zero 0.0 0.0 (2.0 * (3389.5 * 1000.0))
        instrumentVisibility = InstrumentVisibilityMode.Off
        lightingMode = LightingMode.Off
        // Spice: SPK position and the instrument's own frame (CK x frames kernel), not
        // aimed -- the pointing SPICE predicts, independent of the sidecar's SC_QUAT and
        // TRG_POS. Needs CK and frames-kernel coverage of the epoch; MbiAttitudeSpiceRange
        // / SpiceAimed for images without it. Not persisted in a scene, so every session
        // starts here.
        projectionMethod = ProjectionMethod.Spice
        useTransferFunction = true
        windingCorrection = false
        filter = ImageFilter.initial
        stacSearch = StacSearchModel.initial
        instrumentFovs = InstrumentFov.defaults
        frameKernelWarning = None
    }

    /// The panel of a new or loaded scene: `initial`, with this session's Download
    /// Directory (StacSearchModel.create). Use this, not `initial`, wherever a scene's
    /// model is built, or the panel shows the folder of the session's start.
    let create () = { initial with stacSearch = StacSearchModel.create () }

type ImageMessage =
    | SetCustomMin of float
    | SetCustomMax of float
    | ResetCustomMinMax
    | SetColorMap of ColorMap
    | ToggleFalseColor
    | ToggleFalseColorLegend
    | SetEXRChannel of Channel
    | BrightnessContrastMessage of BrightnessContrastAction
    | Empty


type StacSearchMessage =
    | SetServerUrl    of string
    | SetTimeFrom     of string
    | SetTimeTo       of string
    | SetIncidenceMin of float
    | SetIncidenceMax of float
    /// StacSearchModel.includeOtherTargets on/off
    | ToggleIncludeOtherTargets
    /// The viewer's STAC worker started searching this point. The Query*
    /// messages below carry the search's `seq` and are dropped once a newer
    /// search has started.
    | QueryStarted  of StacQueryPoint
    /// The server's answer, every candidate still Checking, and how many items
    /// it matched.
    | QueryListed   of seq : int * array<StacCandidate> * matched : int * StacListing * otherTargets : int
    /// One candidate checked. None: the point is not in that image (or hidden
    /// in it), so the candidate goes.
    | QueryChecked  of seq : int * itemId : string * Option<StacCheck> * Option<StacImageValues>
    /// All candidates checked (and those dropped as unresolvable), or the search failed.
    | QueryFinished of seq : int * Result<StacDropped, string>
    /// Download a candidate's files, select it and put it on top of the projection
    /// stack (not into the library).
    /// The download is started by the Viewer (it owns the worker); this model
    /// only tracks that it is running.
    | Fetch         of itemId : string
    /// Download finished: the local image path, or why it failed. Becomes a
    /// stacImages entry: selected (and on the stack) when + or the name asked for
    /// it, else a hover preview.
    | FetchFinished of itemId : string * Result<string, string>
    /// A click on a result's name: select its image (downloaded first if needed;
    /// not into the library, not on the stack); the selected one again deselects.
    | SelectCandidate of itemId : string
    /// The mouse rested on a result row (the Viewer's dwell timer): download its
    /// image to preview it, if the row is still hovered.
    | PreviewDue of itemId : string
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
    /// The trash icon: asks first (the confirmation dialog)
    | ClearCache
    /// The dialog's Delete: deletes PRo3D's item folders in the download
    /// directory. The files are deleted by the Viewer.
    | ConfirmClearCache
    | CancelClearCache
    /// The Download Directory button: downloads go into this folder from now on
    /// (remembered by the Viewer).
    | SetDownloadDirectory of string
    /// Fly to a checked result's projector, from its downloaded metadata (the
    /// Viewer frames it; GisApp moves the scene time to its epoch).
    | FlyToCandidate of itemId : string
    /// Set the GIS time to a result's observation time (the catalog's); handled by GisApp.
    | SetTimeToCandidate of itemId : string
    /// Sort the results by a column; again on the same column: the other way round.
    | SortCandidatesBy of ImageSortKey
    | ToggleImagesExpanded

type ProjectedImageListMessage =
    | StacMessage of StacSearchMessage
    | OrbitCameraMessage of OrbitMessage
    /// Selects the image; the already selected image is deselected (single selection).
    | SelectImage of Guid
    | TogglePreviewExpanded
    | TogglePropertiesExpanded
    | ToggleLibraryExpanded
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
    /// The mouse left the whole panel: no row is hovered (also one that vanished
    /// under the mouse, which never gets its mouseleave).
    | ClearHover
    | SetProjectionMode of ProjectionStackMode
    /// Frame this image's footprint. Handled by the Viewer (it owns the camera
    /// animation), not by ProjectedImageListApp.update.
    | FlyToImage of Guid
    /// Set the GIS time to this image's observation time (the camera stays). Handled by
    /// GisApp, which owns the time.
    | SetTimeToImage of Guid
    | SortEntriesByDistance
    | SortEntriesByDate
    /// Sort the library by a column; again on the same column: the other way round.
    | SortLibraryBy of ImageSortKey
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
    | FilterMessage of ImageFilterMessage
    | Nop