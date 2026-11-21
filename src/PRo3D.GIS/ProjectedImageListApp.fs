namespace PRo3D.ImageMapping

open System
open System.IO
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.ImageMapping
open PRo3D.Extensions.FSharp
open PRo3D.Base

// last, so its HashSet/IndexList win over Aardvark.Base's BCL-collection helpers
open FSharp.Data.Adaptive

type Self = Self

module ProjectedImageListApp =

    let borderColor = "rgba(255,255,255,.1)"

    let loadDirMessage (dir : string) = ProjectedImageListMessage.LoadImagesDir dir

    let initial : ProjectedImageListModel = ProjectedImageListModel.initial

    let rec update (m : ProjectedImageListModel) (msg : ProjectedImageListMessage) =
        match msg with
        | Nop -> m
        | StacMessage msg ->
            let s = m.stacSearch
            /// a 401 during a session: the dialog again, with a word why
            let signInAgain (s : StacSearchModel) =
                { s with connection = SigningIn { message = Some "The catalog asks to sign in again."; busy = false }
                         status = StacSearchStatus.Idle }
            match msg with
            | SetServerUrl url when url = s.serverUrl -> m
            | SetServerUrl url ->
                // another catalog: the sign-in was for the old one (the Viewer drops it)
                // and its results: a click on one would go to the old catalog
                { m with stacSearch = { s with serverUrl = url; connection = Disconnected; candidates = IndexList.empty; query = None; hovered = None; status = StacSearchStatus.Idle } }
            | Connect ->
                match s.connection with
                | Connected _ | Connecting | SigningIn _ -> m
                | Disconnected | ConnectionFailed _ -> { m with stacSearch = { s with connection = Connecting } }
            | Disconnect ->
                let status =
                    match s.status with
                    | StacSearchStatus.Searching | StacSearchStatus.Checking _ -> StacSearchStatus.Idle
                    | other -> other
                // the results go too: their downloads would need the sign-in just dropped
                { m with stacSearch = { s with connection = Disconnected; status = status; candidates = IndexList.empty; query = None; hovered = None } }
            | SignIn (user, _) ->
                // the Viewer sends the attempt; the password is not kept here
                { m with stacSearch = { s with connection = SigningIn { message = None; busy = true }; lastUser = user } }
            | CancelSignIn ->
                { m with stacSearch = { s with connection = Disconnected } }
            | ConnectFinished (user, probe) ->
                match s.connection with
                // only the answer to the attempt in progress: a cancel or disconnect since
                // leaves the state alone
                | Connecting | SigningIn { busy = true } ->
                    let connection =
                        match probe with
                        | ProbeOk -> Connected user
                        | ProbeNeedsSignIn -> SigningIn { message = None; busy = false }
                        | ProbeRejected -> SigningIn { message = Some "Wrong user name or password."; busy = false }
                        // a failed sign-in stays in the dialog, which the user is looking at
                        | ProbeFailed e when user.IsSome -> SigningIn { message = Some e; busy = false }
                        | ProbeFailed e -> ConnectionFailed e
                    { m with stacSearch = { s with connection = connection } }
                | _ -> m
            // a 401 reopens the dialog only while connected: not after a disconnect or a URL change
            | QueryFinished (n, Result.Error e) when PRo3D.GIS.StacSearch.isSignInNeeded e && StacSearchModel.isConnected s && s.query |> Option.exists (fun q -> q.seq = n) ->
                { m with stacSearch = signInAgain s }
            | FetchFinished (itemId, Result.Error e) when PRo3D.GIS.StacSearch.isSignInNeeded e && StacSearchModel.isConnected s ->
                { m with stacSearch = signInAgain { s with downloading = s.downloading |> HashSet.remove itemId } }
            | SetTimeFrom t ->
                { m with stacSearch = { s with timeFrom = t } }
            | SetTimeTo t ->
                { m with stacSearch = { s with timeTo = t } }
            // SUN INCIDENCE (disabled, see docs/StacImageSearch.md): no row sends these
            | SetIncidenceMin _ -> m
                // { m with stacSearch = { s with incidenceMin = clamp 0.0 180.0 a } }
            | SetIncidenceMax _ -> m
                // { m with stacSearch = { s with incidenceMax = clamp 0.0 180.0 a } }
            | QueryStarted q ->
                { m with stacSearch = { s with query = Some q; status = StacSearchStatus.Searching } }
            // the Query* answers of a search that is no longer the current one are
            // dropped: a newer point is being searched already
            | QueryListed (n, candidates, matched, listing) when s.query |> Option.exists (fun q -> q.seq = n) ->
                { m with stacSearch = { s with status = StacSearchStatus.Checking (matched, 0, listing); candidates = IndexList.ofArray candidates } }
            | QueryChecked (n, itemId, check, values) when s.query |> Option.exists (fun q -> q.seq = n) ->
                // choose keeps the other rows' indices, so only this row changes on screen
                let candidates =
                    s.candidates |> IndexList.choose (fun c ->
                        if c.itemId = itemId then
                            check |> Option.map (fun check -> { c with check = check; values = values |> Option.orElse c.values })
                        else Some c)
                let status =
                    match s.status with
                    | StacSearchStatus.Checking (matched, n, listing) -> StacSearchStatus.Checking (matched, n + 1, listing)
                    | other -> other
                { m with stacSearch = { s with candidates = candidates; status = status } }
            | QueryFinished (n, result) when s.query |> Option.exists (fun q -> q.seq = n) ->
                match result with
                | Ok dropped ->
                    let matched, listing =
                        match s.status with
                        | StacSearchStatus.Checking (matched, _, listing) -> matched, listing
                        | _ -> s.candidates.Count, ByFootprint
                    let sorted = s.candidates |> IndexList.toArray |> PRo3D.GIS.StacSearch.sortCandidates
                    // the best maxResults only: the rest would bury them
                    let shown = if sorted.Length > StacSearchModel.maxResults then Array.sub sorted 0 StacSearchModel.maxResults else sorted
                    // the user's column sort, on the best maxResults
                    let shown =
                        match s.sort with
                        | Some sort -> PRo3D.GIS.StacSearch.sortCandidatesBy sort shown
                        | None -> shown
                    // kept = the images that show the point; red rows are counted apart
                    let kept = sorted |> Array.sumBy (fun c -> match c.check with Resolved _ -> 1 | _ -> 0)
                    let dropped = { dropped with unresolvedListed = sorted.Length - kept }
                    { m with stacSearch = { s with status = StacSearchStatus.Found (matched, kept, listing, dropped); candidates = IndexList.ofArray shown } }
                | Result.Error e ->
                    { m with stacSearch = { s with status = StacSearchStatus.Failed e; candidates = IndexList.empty } }
            | QueryListed _ | QueryChecked _ | QueryFinished _ ->
                m
            | Fetch _ when not (StacSearchModel.isConnected s) -> m
            | Fetch itemId ->
                // the download itself is started by the Viewer, which owns the worker
                { m with stacSearch = { s with downloading = s.downloading |> HashSet.add itemId } }
            | FetchFinished (itemId, result) ->
                let m = { m with stacSearch = { s with downloading = s.downloading |> HashSet.remove itemId } }
                match result with
                | Result.Error e ->
                    Log.warn "[STAC] could not download %s: %s" itemId e
                    { m with stacSearch = { m.stacSearch with status = StacSearchStatus.Failed (sprintf "download failed: %s" e) } }
                | Ok path ->
                    // fetching the same item twice must not duplicate it in the library
                    let image, images =
                        match m.images |> IndexList.tryFind (fun _ img -> img.texture = path) with
                        | Some img -> img, m.images
                        | None ->
                            let img = ProjectedImageApp.loadFile path
                            img, m.images |> IndexList.add img
                    update { m with images = images; selectedImage = Some image.id } (AddToStack image.id)
            // done by the Viewer (files); nothing to change here
            | ClearCache -> m
            | ClearResults ->
                { m with stacSearch = { s with candidates = IndexList.empty; query = None; status = StacSearchStatus.Idle; hovered = None } }
            | HoverCandidate itemId ->
                { m with stacSearch = { s with hovered = itemId } }
            | SortCandidatesBy key ->
                let sort = PRo3D.GIS.StacSearch.nextSort s.sort key
                let candidates = s.candidates |> IndexList.toArray |> PRo3D.GIS.StacSearch.sortCandidatesBy sort |> IndexList.ofArray
                { m with stacSearch = { s with sort = Some sort; candidates = candidates } }
            | ToggleImagesExpanded ->
                { m with stacSearch = { s with imagesExpanded = not s.imagesExpanded } }
            // the time and the camera are GisApp's and the Viewer's (FlyToImage alike)
            | FlyToCandidate _ -> m
            | SetTimeToCandidate _ -> m
        | SetLightingMode mode ->
            { m with lightingMode = mode }
        | SetInstrumentVisbilityMode mode ->
            { m with instrumentVisibility = mode }
        | SetProjectionMethod method ->
            { m with projectionMethod = method }
        | ToggleTransferFunction ->
            { m with useTransferFunction = not m.useTransferFunction }
        | ToggleWindingCorrection ->
            { m with windingCorrection = not m.windingCorrection }
        | InstrumentFovMessage msg ->
            { m with instrumentFovs = InstrumentFovApp.update m.instrumentFovs msg }
        | OrbitCameraMessage msg ->
            { m with cameraState = OrbitController.update m.cameraState msg }
        | SetProjectionOpacity opacity -> 
            { m with projectionOpacity = Numeric.update m.projectionOpacity opacity }
        | SetRoll r -> { m with boresightAdjustment = { m.boresightAdjustment with roll = Numeric.update m.boresightAdjustment.roll r } }
        | SetPitch r -> { m with boresightAdjustment = { m.boresightAdjustment with pitch = Numeric.update m.boresightAdjustment.pitch r } }
        | SetYaw r -> { m with boresightAdjustment = { m.boresightAdjustment with yaw = Numeric.update m.boresightAdjustment.yaw r } }
        | LoadImagesDir directory when not (Directory.Exists directory) ->
            // e.g. a scene saved on another machine: keep the current library
            // (and the scene's path, so saving does not drop it)
            Log.warn "[ProjectedImages] image directory not found: %s" directory
            m
        | LoadImagesDir directory -> 
            let imageExts = [".tif";".tiff";".jpg";".jpeg";".png";".exr"]
            let loaded =
                Directory.EnumerateFiles(directory)
                |> Seq.filter (fun p ->
                    let e = (Path.GetExtension p).ToLowerInvariant()
                    List.contains e imageExts
                )
                |> Seq.map (fun path ->
                    ProjectedImageApp.loadFile(path)
                )
            // fresh indices, so no library row keeps an id of the previous import
            let images' = ProjectedImageListModel.replaceImages loaded m
            let first = images' |> IndexList.tryFirst |> Option.map (fun i -> i.id)

            // a fresh library invalidates every id the old one handed out
            { m with
                images = images'
                imageDirectory = Some directory
                pendingStack = []
                stack = IndexList.Empty
                stackDrag = None
                hoveredImage = None
                // the filters stay: they are shared with the Image Search (STAC)
                // and have fixed ranges, independent of the library
                selectedImage = first
                librarySort = None }
        | SelectImage id ->
            // single selection; clicking the selected image again deselects it
            { m with selectedImage = if m.selectedImage = Some id then None else Some id }
        | TogglePreviewExpanded ->
            { m with previewExpanded = not m.previewExpanded }
        | TogglePropertiesExpanded ->
            { m with propertiesExpanded = not m.propertiesExpanded }
        | ToggleLibraryExpanded ->
            { m with libraryExpanded = not m.libraryExpanded }
        | ImageMessage (id, imageMessage) ->
            let images' =
                m.images |> IndexList.map (fun img ->
                    if img.id = id then ProjectedImageApp.update img imageMessage else img
                )
            { m with images = images' }
        | AddToStack id ->
            if ProjectedImageListModel.isInStack id m
               || m.stack.Count >= ProjectedImages.maxCount
               || (ProjectedImageListModel.tryFind id m |> Option.isNone) then
                m
            else
                { m with stack = m.stack |> IndexList.add id }
        | RemoveFromStack id ->
            { m with stack = m.stack |> IndexList.filter ((<>) id) }
        | MoveInStack (id, position) ->
            { m with stack = ProjectedImageListModel.moveInStack id position m.stack }
        | StackDragStart id when ProjectedImageListModel.isInStack id m ->
            // mouse events pause during a native drag: hold the dragged image as
            // the hovered one, for its footprint and frustum
            { m with stackDrag = Some { image = id; target = None }; hoveredImage = Some id }
        | StackDragStart _ -> m
        | StackDragOver position ->
            match m.stackDrag with
            | Some d -> { m with stackDrag = Some { d with target = Some position } }
            | None -> m
        | StackDrop (id, position) ->
            { m with
                stack = ProjectedImageListModel.moveInStack id position m.stack
                stackDrag = None
                hoveredImage = None }
        | StackDragEnd ->
            match m.stackDrag with
            | Some _ -> { m with stackDrag = None; hoveredImage = None }
            | None -> m
        | HoverImage id ->
            { m with hoveredImage = id }
        | FilterMessage msg ->
            { m with filter = ImageFilter.update m.filter msg }
        | FlyToImage _ ->
            // handled by the Viewer, which owns the camera animation
            m
        // the time is GisApp's
        | SetTimeToImage _ -> m
        // Sorting permutes the library only. selectedImage/stack are
        // keyed by Guid, so none of them needs remapping any more -- this used
        // to be ~30 lines of index bookkeeping per sort, via a partial Seq.head.
        // Fresh indices (replaceImages): sortBy reuses the old ones, which left
        // every row with the id of the image that used to be there.
        | SortEntriesByDistance -> update m (SortLibraryBy SortDistance)
        | SortEntriesByDate -> update m (SortLibraryBy SortDate)
        | SortLibraryBy key ->
            let (key, ascending) as sort = PRo3D.GIS.StacSearch.nextSort m.librarySort key
            let byKey : ProjectedImageModel[] -> ProjectedImageModel[] =
                match key with
                | SortInstrument -> PRo3D.GIS.StacSearch.sortByOptional ascending (fun i -> Some (ImageFilter.instrumentKey i.instrument))
                | SortDistance -> PRo3D.GIS.StacSearch.sortByOptional ascending (fun i -> Some i.distance)
                | SortDate -> PRo3D.GIS.StacSearch.sortByOptional ascending (fun i -> if i.time = DateTime.MinValue then None else Some i.time)
                | SortResolution -> PRo3D.GIS.StacSearch.sortByOptional ascending (fun i -> i.resolution)
            let sorted = m.images |> IndexList.toArray |> byKey |> IndexList.ofArray
            { m with images = ProjectedImageListModel.replaceImages sorted m; librarySort = Some sort }

    /// Adaptive counterpart of ProjectedImageListModel.tryFind. `id` is
    /// NonAdaptive, so this is a plain lookup inside a single AVal.map rather
    /// than a bind over every image's own aval.
    let tryFindAdaptive (id : Guid) (m : AdaptiveProjectedImageListModel) =
        m.images.Content
        |> AVal.map (IndexList.tryFind (fun _ (img : AdaptiveProjectedImageModel) -> img.id = id))

    /// The catalog's sign-in dialog, over the whole window: the Viewer mounts it on
    /// its main page (ViewerGUI), since the GIS tab may be hidden when the Image
    /// Search tool connects. Absent from the DOM unless signing in.
    ///
    /// The password is not bound to the model: it is read from the field when the
    /// button is clicked and crosses to PRo3D once, as a Secret. Enter and Escape are
    /// handled in the browser, so typing sends nothing. The dialog is rebuilt on a
    /// failed attempt, which clears the password field.
    let signInDialog (m : AdaptiveProjectedImageListModel) : DomNode<ProjectedImageListMessage> =
        let s = m.stacSearch
        // marked with data-stac, never with id: aardvark.media owns the elements' ids and
        // finds them by id to update them
        let userId = "user"
        let passwordId = "password"
        let submitId = "submit"
        let cancelId = "cancel"
        let find (key : string) = sprintf "document.querySelector('.stac-signin-dimmer [data-stac=%s]')" key
        let fieldValue (key : string) = find key + ".value"
        /// the password, read and cleared in one step, before it is sent: it does not
        /// stay in the page for anyone with the developer tools to read later
        let takePassword (key : string) = sprintf "(function () { var e = %s; var v = e.value; e.value = ''; return v; })()" (find key)
        let unquote (json : string) =
            try System.Text.Json.JsonSerializer.Deserialize<string> json with _ -> ""
        let keys =
            clientEvent "onkeydown"
                (sprintf "if (event.key === 'Enter') { event.preventDefault(); %s.click(); } else if (event.key === 'Escape') { event.preventDefault(); %s.click(); }" (find submitId) (find cancelId))
        let fieldStyle = "width: 100%; box-sizing: border-box; background: #111; color: #eee; border: 1px solid #555; border-radius: 3px; padding: 6px 8px; margin-top: 3px"
        Incremental.div AttributeMap.empty (
            alist {
                let! connection = s.connection
                match connection with
                | SigningIn signIn ->
                    let! serverUrl = s.serverUrl
                    let! lastUser = s.lastUser
                    let host =
                        match Uri.TryCreate(serverUrl, UriKind.Absolute) with
                        | true, uri -> uri.Host
                        | _ -> serverUrl
                    // the field to type into first: the password when the name is known
                    let focus (node : DomNode<ProjectedImageListMessage>) = onBoot "setTimeout(() => document.getElementById('__ID__').focus(), 0);" node
                    let userField =
                        input [
                            attribute "data-stac" userId; attribute "type" "text"; attribute "value" lastUser
                            attribute "autocomplete" "username"; attribute "spellcheck" "false"
                            style fieldStyle; keys
                        ]
                    let passwordField =
                        input [
                            attribute "data-stac" passwordId; attribute "type" "password"
                            attribute "autocomplete" "current-password"
                            style fieldStyle; keys
                        ]
                    yield div [
                        clazz "stac-signin-dimmer"
                        style "position: fixed; inset: 0; background: rgba(0,0,0,0.55); z-index: 20000; display: flex; align-items: center; justify-content: center"
                    ] [
                        div [style "width: 340px; background: #1b1c1d; color: #eee; border: 1px solid #555; border-radius: 6px; box-shadow: 0 8px 30px rgba(0,0,0,0.7); padding: 18px 20px; font-size: 14px"] [
                            div [style "display: flex; align-items: center; gap: 8px; font-size: 16px; font-weight: bold"] [
                                i [clazz "lock icon"] []
                                text "Sign in to the image catalog"
                            ]
                            div [style "color: #aaa; margin: 4px 0 14px 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap"; attribute "title" serverUrl] [
                                text host
                            ]
                            label [style "display: block; color: #ccc"] [ text "User name"; (if lastUser = "" then focus userField else userField) ]
                            label [style "display: block; color: #ccc; margin-top: 10px"] [ text "Password"; (if lastUser = "" then passwordField else focus passwordField) ]
                            div [style "min-height: 20px; margin-top: 10px; color: #f88"] [
                                text (signIn.message |> Option.defaultValue "")
                            ]
                            div [style "color: #888; font-size: 12px; margin-top: 2px"] [
                                text "Kept for this session only; PRo3D does not store the password."
                            ]
                            div [style "display: flex; justify-content: flex-end; gap: 8px; margin-top: 14px"] [
                                button [attribute "data-stac" cancelId; clazz "ui button"; onClick (fun _ -> StacMessage CancelSignIn)] [ text "Cancel" ]
                                button [
                                    yield attribute "data-stac" submitId
                                    yield clazz (if signIn.busy then "ui primary loading disabled button" else "ui primary button")
                                    if not signIn.busy then
                                        yield onEvent' "onclick" [fieldValue userId; takePassword passwordId] (function
                                            | [user; password] ->
                                                Seq.singleton (StacMessage (SignIn ((unquote user).Trim(), Secret.create (unquote password))))
                                            | _ -> Seq.empty)
                                ] [ text "Sign in" ]
                            ]
                        ]
                    ]
                | _ -> ()
            })

    let selectedImage (m : AdaptiveProjectedImageListModel) =
        adaptive {
            match! m.selectedImage with
            | None -> return None
            | Some id -> return! tryFindAdaptive id m
        }

    let view 
        (m : AdaptiveProjectedImageListModel)
        (showDOM : AdaptiveProjectedImageModel -> DomNode<ImageMessage>) 
        (showRelative2DImage : aval<Option<AdaptiveProjectedImageModel>> -> DomNode<ProjectedImageListMessage>) =
    
        let listAttributes =
            amap {
                yield clazz "ui divided list inverted segment"
                yield style "overflow-y : hidden"
            } |> AttributeMap.ofAMap

        let jsImportDialog =
            "top.aardvark.dialog.showOpenDialog({tile: 'Select directory', filters: [{ name: 'directories'}], properties: ['openDirectory']}).then(result => {aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"


        /// A section with a title node: plain text, or text with extras such as the
        /// Filters section's "(n active)".
        let accordion' (titleNode : DomNode<ProjectedImageListMessage>) icon active styling content' =
                let title = if active then "title active inverted" else "title inverted"
                let content = if active then "content active" else "content"
                                    
                // Each section initialises itself: they are top level in the Projected
                // Images panel and siblings of each other. Do not nest one inside another
                // accordion that is initialised too -- Semantic UI then resolves the titles
                // across all descendants, the outer call steals them and the inner panel
                // never opens.
                onBoot "$('#__ID__').accordion();" (
                    div [styling] [
                        div [clazz "ui inverted accordion fluid"] [
                            div [clazz title; style "background-color: #282828"] [
                                    i [clazz ("dropdown icon")] []
                                    titleNode
                                    div [style "float:right"] [i [clazz (icon + " icon")] []]
                                
                            ]
                            // no overflow here: the page is the panel's only scroller.
                            // Indented, with a thin line along its left edge: one level in
                            div [clazz content; style "border-left: 1px solid rgba(255,255,255,0.2); padding-left: 3px"] content'
                        ]
                    ]
                )

        let accordion (text' : string) icon active styling content' =
            accordion' (text text') icon active styling content'

        /// Sub-headers inside a section (Filters, Selected Image): lighter than the
        /// section titles -- not bold, a bit smaller, slightly gray.
        let subHeaderStyle = "font-weight: normal; font-size: 0.92em; color: rgba(255,255,255,0.75)"

        let isInStack (id : Guid) =
            m.stack.Content |> AVal.map (IndexList.exists (fun _ i -> i = id))

        /// HTML5 drag & drop on a stack row. The dragged row carries its Guid in
        /// the DataTransfer; dropping it onto another row moves it to that
        /// row's position (moveInStack: the rows in between shift by one).
        ///
        /// While dragging, entering a row previews the drop in 3D (StackDragOver
        /// -> effectiveStack) -- sent only when the target row changes, since
        /// dragover fires continuously and dragenter fires for every child icon.
        /// The panel itself keeps the committed order until the drop: rebuilding
        /// it mid-drag would replace the row the browser is dragging.
        /// window.__pro3dStackDrag marks a drag that started here, so foreign
        /// drags (files) are ignored, and lets dragend tell a cancel from a drop.
        let stackDragDrop (id : Guid) (stackPos : int) : list<Attribute<ProjectedImageListMessage>> =
            // works whether the handler sees the native or a jQuery-wrapped event
            let ev = "var e=(event.originalEvent||event);"
            let highlight = "e.currentTarget.style.boxShadow='inset 0 0 0 2px #2185d0';"
            let clear = "e.currentTarget.style.boxShadow='';"
            // only for drags that started in this panel
            let ours (body : string) = "if(window.__pro3dStackDrag){" + body + "}"
            [
                attribute "draggable" "true"
                clientEvent "ondragstart" (
                    ev + $"e.dataTransfer.setData('text/plain','{id}'); e.dataTransfer.effectAllowed='move';"
                    + "window.__pro3dStackDrag=true; window.__pro3dStackTarget=null; e.currentTarget.style.opacity='0.4';"
                    + "aardvark.processEvent('__ID__','stackdragstart');")
                clientEvent "ondragenter" (
                    ev + ours ("e.preventDefault();" + highlight
                    + $"if(window.__pro3dStackTarget!=={stackPos}){{window.__pro3dStackTarget={stackPos}; aardvark.processEvent('__ID__','stackdragover');}}"))
                clientEvent "ondragover" (ev + ours ("e.preventDefault(); e.dataTransfer.dropEffect='move';" + highlight))
                clientEvent "ondragleave" (ev + clear)
                clientEvent "ondrop" (
                    ev + ours ("e.preventDefault();" + clear + "window.__pro3dStackDrag=false;"
                    + "aardvark.processEvent('__ID__','stackdrop',e.dataTransfer.getData('text/plain'));"))
                clientEvent "ondragend" (
                    ev + "e.currentTarget.style.opacity='';"
                    + "if(window.__pro3dStackDrag){window.__pro3dStackDrag=false; aardvark.processEvent('__ID__','stackdragend');}")
                onEvent' "stackdragstart" [] (fun _ -> Seq.singleton (StackDragStart id))
                onEvent' "stackdragover" [] (fun _ -> Seq.singleton (StackDragOver stackPos))
                onEvent' "stackdragend" [] (fun _ -> Seq.singleton StackDragEnd)
                onEvent' "stackdrop" [] (fun args ->
                    // processEvent JSON-encodes its arguments, hence the quotes;
                    // a drop onto itself still ends the drag (moving to its own
                    // position is a no-op)
                    match args with
                    | [ s ] ->
                        match Guid.TryParse (s.Trim('"')) with
                        | true, src -> Seq.singleton (StackDrop (src, stackPos))
                        | _ -> Seq.singleton StackDragEnd
                    | _ -> Seq.singleton StackDragEnd
                )
            ]

        /// The projection stack panel: draw order bottom -> top, displayed top
        /// first (what you see on the surface wins at the top). Reorder by
        /// dragging a row onto another (stackDragDrop) or with the arrows;
        /// add/remove from the library.
        let stackPanel =
            Incremental.div (AttributeMap.ofList [ attribute "style" $"border: 2px solid black; margin-top: 10px" ]) (
                alist {
                    let! stack = m.stack.Content
                    let! images = m.images.Content
                    let count = stack.Count
                    yield div [attribute "style" $"display: flex; font-weight: bold; border-bottom: 2px solid black; background: black; padding: 5px"] [
                        text (sprintf "Projection Stack (%d/%d)" count ProjectedImages.maxCount)
                    ]
                    if count = 0 then
                        yield div [attribute "style" "padding: 5px; font-style: italic; color: white"] [
                            text "No Images currently projected. Add images from the image library"
                        ]
                    else
                        // display top layer first
                        let entries = stack |> IndexList.toArray |> Array.rev
                        for displayIdx in 0 .. entries.Length - 1 do
                            let id = entries.[displayIdx]
                            let stackPos = entries.Length - 1 - displayIdx // bottom-based
                            let name =
                                images
                                |> IndexList.tryFind (fun _ img -> img.id = id)
                                |> Option.map (fun img -> img.texture |> AVal.map Path.GetFileName)
                                |> Option.defaultValue (AVal.constant "(missing image)")
                            yield div [
                                yield attribute "style" $"display: flex; align-items: center; gap: 4px; padding: 3px 5px; border-bottom: 1px solid {borderColor}"
                                yield onMouseEnter (fun _ -> HoverImage (Some id))
                                yield onMouseLeave (fun _ -> HoverImage None)
                                yield! stackDragDrop id stackPos
                            ] [
                                i [clazz "bars icon"; style "cursor: grab; opacity: 0.6"; attribute "title" "drag to reorder"] []
                                i [clazz "arrow up icon"; style "cursor: pointer"; onClick (fun _ -> MoveInStack (id, stackPos + 1))] []
                                i [clazz "arrow down icon"; style "cursor: pointer"; onClick (fun _ -> MoveInStack (id, stackPos - 1))] []
                                i [clazz "remove icon"; style "cursor: pointer"; onClick (fun _ -> RemoveFromStack id)] []
                                i [clazz "location arrow icon"; style "cursor: pointer"; onClick (fun _ -> FlyToImage id)] []
                                Incremental.text name
                            ]
                })

        // ---- library filters --------------------------------------------------------

        let filter = m.filter

        let rangeIsActive (r : AdaptiveRangeFilter) =
            adaptive {
                let! lower = r.lower.value
                let! min = r.lower.min
                let! upper = r.upper.value
                let! max = r.upper.max
                return lower > min || upper < max
            }

        let rangeBounds (r : AdaptiveRangeFilter) =
            adaptive {
                let! active = rangeIsActive r
                if active then
                    let! lower = r.lower.value
                    let! upper = r.upper.value
                    return Some (lower, upper)
                else
                    return None
            }

        /// What the filters let through, shared by every row's predicate: built once,
        /// and it only changes when a filter does.
        let bounds =
            adaptive {
                let! instruments = filter.instruments.Content
                let! distance = rangeBounds filter.distance
                let! resolution = rangeBounds filter.resolution
                let! phaseAngle = rangeBounds filter.phaseAngle
                let! time = rangeBounds filter.time
                let b : ImageFilter.Bounds =
                    { instruments = instruments; distance = distance; resolution = resolution; phaseAngle = phaseAngle; time = time }
                return b
            }

        /// The library rows the filters let through. filterA, then the row map: a
        /// row's DomNode is built once per image, not rebuilt whenever a filter moves.
        let visibleImages =
            m.images |> AList.filterA (fun img ->
                adaptive {
                    let! b = bounds
                    let! instrument = img.instrument
                    let! distance = img.distance
                    let! resolution = img.resolution
                    let! phaseAngle = img.phaseAngle
                    let! time = img.time
                    return ImageFilter.passes b instrument distance resolution phaseAngle time
                })

        let rangeOf (kind : RangeFilterKind) =
            match kind with
            | RangeFilterKind.PhaseAngle   -> filter.phaseAngle
            | RangeFilterKind.Resolution   -> filter.resolution
            | RangeFilterKind.Distance     -> filter.distance
            | RangeFilterKind.Time         -> filter.time

        let activeCount =
            let count (n : aval<int>) (active : aval<bool>) =
                (n, active) ||> AVal.map2 (fun n active -> if active then n + 1 else n)
            [ RangeFilterKind.PhaseAngle; RangeFilterKind.Resolution; RangeFilterKind.Distance; RangeFilterKind.Time ]
            |> List.map (rangeOf >> rangeIsActive)
            |> List.fold count (filter.instruments.Content |> AVal.map (fun s -> if s.IsEmpty then 0 else 1))

        // the accent the busy chip and the tool strip use: a filter that hides rows
        let filterAccent = "#e3b341"

        let filterMsg (f : 'a -> ImageFilterMessage) (a : 'a) = FilterMessage (f a)

        /// The filter's two-handle slider (DualRangeSlider, shared with an image's
        /// display range); amber while the filter hides rows. Log scale for the
        /// ranges spanning orders of magnitude (ImageFilter.isLogScale).
        let dualSlider (kind : RangeFilterKind) (r : AdaptiveRangeFilter) =
            DualRangeSlider.viewWith (ImageFilter.isLogScale kind) r.lower r.upper (rangeIsActive r)
                (fun v -> FilterMessage (SetLower (kind, Numeric.SetValue v)))
                (fun v -> FilterMessage (SetUpper (kind, Numeric.SetValue v)))
                Nop

        /// Label (amber with a filter icon while it hides rows) | lower | slider |
        /// upper | reset.
        let filterLabel (label : string) (tooltip : Option<string>) (active : aval<bool>) =
            let attributes =
                amap {
                    yield clazz "collapsing"
                    match tooltip with
                    | Some t -> yield attribute "title" t
                    | None -> ()
                    let! active = active
                    if active then yield style $"color: {filterAccent}; font-weight: bold"
                } |> AttributeMap.ofAMap
            Incremental.td attributes (
                alist {
                    let! active = active
                    if active then yield i [clazz "filter icon"] []
                    yield text label
                })

        let resetCell (active : aval<bool>) (msg : ProjectedImageListMessage) =
            td [clazz "collapsing"] [
                Incremental.div AttributeMap.empty (
                    alist {
                        let! active = active
                        if active then
                            yield i [clazz "undo icon"; style "cursor: pointer"
                                     attribute "title" "reset: whole range"; onClick (fun _ -> msg)] []
                    })
            ]

        let rangeFilterRow (label : string) (tooltip : Option<string>) (kind : RangeFilterKind) =
            let r = rangeOf kind
            let active = rangeIsActive r
            tr [] [
                filterLabel label tooltip active
                td [style "width: 80px"] [
                    Numeric.view' [NumericInputType.InputBox] r.lower |> UI.map (filterMsg (fun a -> SetLower (kind, a)))
                ]
                td [style "min-width: 80px"] [ dualSlider kind r ]
                td [style "width: 80px"] [
                    Numeric.view' [NumericInputType.InputBox] r.upper |> UI.map (filterMsg (fun a -> SetUpper (kind, a)))
                ]
                resetCell active (FilterMessage (ResetRange kind))
            ]

        /// The known HERA instruments and any further ones the library holds, as
        /// toggle chips; none selected = all shown. Fixed ones too: the filter also
        /// applies to catalog results that are not loaded.
        let instrumentFilterRow =
            let known =
                PRo3D.SPICE.InstrumentProjection.instrumentNames
                |> Map.toList |> List.map fst |> ASet.ofList
            let instruments =
                m.images
                |> AList.mapA (fun img -> img.instrument |> AVal.map ImageFilter.instrumentKey)
                |> AList.toASet
                |> ASet.union known
                |> ASet.sortBy id
            let active = filter.instruments.Content |> AVal.map (fun s -> not s.IsEmpty)
            tr [] [
                filterLabel "Instrument" None active
                td [attribute "colspan" "3"] [
                    Incremental.div (AttributeMap.ofList [style "display: flex; flex-wrap: wrap; gap: 4px"]) (
                        alist {
                            for instrument in instruments do
                                let selected = filter.instruments |> ASet.contains instrument
                                let attributes =
                                    amap {
                                        let! selected = selected
                                        yield clazz (if selected then "ui mini yellow button" else "ui mini inverted basic button")
                                        yield style "margin: 0"
                                        yield onClick (fun _ -> FilterMessage (ToggleInstrument instrument))
                                    } |> AttributeMap.ofAMap
                                yield Incremental.div attributes (AList.single (text instrument))
                        })
                ]
                resetCell active (FilterMessage ClearInstruments)
            ]

        /// Observation time: the slider over the library's span, and below it the two
        /// bounds as UTC date/time inputs (too wide for the bound columns of the other rows).
        let timeFilterRow =
            let r = filter.time
            let active = rangeIsActive r
            let timeInput (input : AdaptiveNumericInput) (setBound : float -> ImageFilterMessage) =
                let attributes =
                    amap {
                        yield attribute "type" "datetime-local"
                        yield attribute "step" "1"
                        yield clazz "pro3d-time-input"
                        let! min = input.min
                        let! max = input.max
                        let! value = input.value
                        let format (seconds : float) = (ImageFilter.timeOfKey seconds).ToString("yyyy-MM-ddTHH:mm:ss", Globalization.CultureInfo.InvariantCulture)
                        yield attribute "min" (format min)
                        yield attribute "max" (format max)
                        yield attribute "value" (format value)
                        yield onChange (fun s ->
                            let styles = Globalization.DateTimeStyles.AssumeUniversal ||| Globalization.DateTimeStyles.AdjustToUniversal
                            match DateTime.TryParse(s.Trim('"'), Globalization.CultureInfo.InvariantCulture, styles) with
                            | true, t ->
                                match ImageFilter.timeKey t with
                                | Some k -> FilterMessage (setBound k)
                                | None -> Nop
                            | _ -> Nop)
                    } |> AttributeMap.ofAMap
                Incremental.input attributes
            tr [] [
                filterLabel "Date [UTC]" (Some "Observation time, from the image's metadata. Images without one (no metadata) are hidden while this filter is set") active
                td [attribute "colspan" "3"] [
                    dualSlider RangeFilterKind.Time r
                    div [style "display: flex; justify-content: space-between; gap: 6px; margin-top: 4px"] [
                        timeInput r.lower (fun v -> SetLower (RangeFilterKind.Time, Numeric.SetValue v))
                        timeInput r.upper (fun v -> SetUpper (RangeFilterKind.Time, Numeric.SetValue v))
                    ]
                ]
                resetCell active (FilterMessage (ResetRange RangeFilterKind.Time))
            ]

        let phaseAngleTooltip =
            Some "Phase angle: the angle at the target between the sun and the camera. Low: lit from behind the camera, flat with few shadows; around 90°: side lighting, long shadows; high: backlit. Images without it (no metadata) are hidden while this filter is set"
        let resolutionTooltip =
            Some "Ground resolution at the target: distance × the angle one pixel spans (instrument field of view / image width). Images without it (no metadata or unknown instrument) are hidden while this filter is set"
        let distanceTooltip =
            Some "Distance from the camera to the target's centre at the time of the image: the length of the target position vector in the image's metadata. Images without metadata count as 0 km"

        /// The filter rows; the section title says how many are active
        /// (filtersTitle), and a reset-all line shows while any is.
        let filterPanel =
            let resetAll =
                Incremental.div AttributeMap.empty (
                    alist {
                        let! n = activeCount
                        if n > 0 then
                            yield div [style "display: flex; justify-content: flex-end; padding: 2px 5px"] [
                                span [style "cursor: pointer; opacity: 0.85"; attribute "title" "reset all filters"
                                      onClick (fun _ -> FilterMessage ResetAll)] [
                                    i [clazz "undo icon"] []
                                    text "reset all"
                                ]
                            ]
                    })
            div [] [
                resetAll
                table [clazz "ui celled striped inverted table unstackable"; style "margin: 0"] [
                    tbody [] [
                        rangeFilterRow "Phase Angle [°]" phaseAngleTooltip RangeFilterKind.PhaseAngle
                        instrumentFilterRow
                        rangeFilterRow "Resolution [m/px]" resolutionTooltip RangeFilterKind.Resolution
                        rangeFilterRow "Distance [km]" distanceTooltip RangeFilterKind.Distance
                        timeFilterRow
                    ]
                ]
            ]

        /// The Filters section's title: "Filters", and "(n active)" in amber while any is.
        let filtersTitle =
            span [] [
                text "Filters"
                Incremental.span AttributeMap.empty (
                    alist {
                        let! n = activeCount
                        if n > 0 then
                            yield span [style $"color: {filterAccent}; margin-left: 6px"] [text (sprintf "(%d active)" n)]
                    })
            ]

        /// A sub-section inside a section: a caret header in the sub-header style
        /// (like Filters and Images) that folds `content` away.
        let subFold (title : string) (expanded : aval<bool>) (toggle : ProjectedImageListMessage) (content : DomNode<ProjectedImageListMessage>) =
            let header =
                tr [] [
                    th [style subHeaderStyle] [
                        Incremental.span
                            (AttributeMap.ofList [style "cursor: pointer"; onClick (fun _ -> toggle)])
                            (alist {
                                let! expanded = expanded
                                yield i [clazz (if expanded then "caret down icon" else "caret right icon")] []
                                yield text title
                            })
                    ]
                ]
            div [] [
                table [clazz "ui celled striped inverted table unstackable"; style "margin: 0"] [
                    tbody [] [ header ]
                ]
                // content built once, re-yielded on unfold
                Incremental.div AttributeMap.empty (
                    alist {
                        let! expanded = expanded
                        if expanded then yield content
                    })
            ]

        /// The Selected Image section: Preview Image (the 2D preview) and Properties
        /// (its edit parameters), each folding on its own -- or a note when nothing
        /// is selected.
        let selectedImageContent =
            let selected = selectedImage m
            let hasSelection = selected |> AVal.map Option.isSome
            // the surface ignores Data/Tone/False Colour while it shows the original
            // pixels: say so where they are edited, with the switch at hand
            let originalPixelsHint =
                Incremental.div AttributeMap.empty (
                    alist {
                        let! adjusted = m.useTransferFunction
                        if not adjusted then
                            yield div [style "display: flex; align-items: center; gap: 8px; margin: 4px 0 8px 0; padding: 6px 8px; border: 1px solid rgba(255,255,255,0.15); border-radius: 4px"] [
                                i [clazz "exclamation triangle icon"; style "color: #e3b341; margin: 0"] []
                                span [style "flex: 1"] [ text "Image adjustments are off: these settings only change the preview, not the projection." ]
                                button [clazz "ui mini button"; style "flex-shrink: 0"; onClick (fun _ -> ToggleTransferFunction)] [ text "Apply image adjustments" ]
                            ]
                    })
            // built once: binding on the selection would rebuild the preview's render
            // control on every click; it follows `selected` itself
            let preview = div [style "width: 100%"] [showRelative2DImage selected]
            let properties =
                Incremental.div AttributeMap.empty (
                    alist {
                        match! selected with
                        | Some img ->
                            yield originalPixelsHint
                            yield showDOM img |> UI.map (fun msg -> ProjectedImageListMessage.ImageMessage (img.id, msg))
                        | None -> ()
                    })
            let sections =
                div [] [
                    subFold "Preview Image" m.previewExpanded TogglePreviewExpanded preview
                    subFold "Properties" m.propertiesExpanded TogglePropertiesExpanded properties
                ]
            Incremental.div AttributeMap.empty (
                alist {
                    let! hasSelection = hasSelection
                    if hasSelection then
                        yield sections
                    else
                        yield div [style "padding: 5px; font-style: italic"] [text "No image is currently selected"]
                })

        // ---- image lists: the library's and the Image Search's share one layout ------

        /// One column of an image list: its width (shared by header and rows, so they
        /// line up) and height. Both lists use the same columns (imageListHeader).
        let listCell (width : int) (pointer : bool) (separator : bool) (height : string) =
            let cursor = if pointer then "cursor: pointer; " else ""
            let border = if separator then $"border-right: 1px solid {borderColor}; " else ""
            attribute "style" $"{cursor}width: {width}px; flex-shrink: 0; {height}; {border}display: flex; justify-content: center; align-items: center; overflow: hidden; white-space: nowrap;"
        let listRowHeight = "height: 44px"
        let listHeaderHeight = "padding: 5px 0"
        let wPreview, wAdd, wFlyTo, wInstrument, wDistance, wDate, wResolution = 44, 32, 56, 70, 84, 110, 84

        /// A length for the lists: cm, m or km.
        let formatLength (metres : float) =
            if metres >= 10000.0 then sprintf "%.0f km" (metres / 1000.0)
            elif metres >= 1000.0 then sprintf "%.1f km" (metres / 1000.0)
            elif metres >= 1.0 then sprintf "%.1f m" metres
            else sprintf "%.0f cm" (metres * 100.0)
        /// Distance to the target, km; compact for cruise distances.
        let formatDistanceKm (km : float) =
            if km < 1000.0 then sprintf "%.2f km" km
            elif km < 1000000.0 then sprintf "%.0f km" km
            else sprintf "%.2e km" km
        /// UTC, to the minute: the full time is in the cell's tooltip.
        let formatListDate (t : DateTime) =
            if t = DateTime.MinValue then "-"
            else t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", Globalization.CultureInfo.InvariantCulture)
        let formatFullDate (t : DateTime) =
            if t = DateTime.MinValue then "no observation time"
            else t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture) + " UTC"
        let formatResolution (r : Option<float>) =
            r |> Option.map formatLength |> Option.defaultValue "-"

        let resolutionTip = "Resolution: ground size of one pixel at the target's centre (distance × the angle one pixel spans), from the image's metadata"
        let distanceTip = "Distance from the camera to the target's centre, from the image's metadata"

        /// The column header of both image lists: Preview | Add | FlyTo | Instrument |
        /// Distance | Date | Res. The last four sort (again: the other way round); the
        /// active sort shows its direction.
        let imageListHeader (sort : aval<Option<ImageSortKey * bool>>) (onSort : ImageSortKey -> ProjectedImageListMessage) =
            let plain (width : int) (title : string) (tooltip : string) =
                div [listCell width false true listHeaderHeight; attribute "title" tooltip] [text title]
            let sortable (width : int) (separator : bool) (title : string) (tooltip : string) (key : ImageSortKey) =
                Incremental.div
                    (AttributeMap.ofList [listCell width true separator listHeaderHeight; attribute "title" tooltip; onClick (fun _ -> onSort key)])
                    (alist {
                        let! sort = sort
                        let icon =
                            match sort with
                            | Some (k, true) when k = key -> "sort up icon"
                            | Some (k, false) when k = key -> "sort down icon"
                            | _ -> "sort icon"
                        yield i [clazz icon; style "margin-right: 2px"] []
                        yield text title
                    })
            div [attribute "style" "display: flex; font-weight: bold; border-bottom: 2px solid black; background: black"] [
                plain wPreview "" "preview"
                plain wAdd "Add" "add to the projection stack"
                plain wFlyTo "Go to" "the arrow flies to the image's camera; the clock sets the GIS time to the image's observation time"
                sortable wInstrument true "Instr." "Instrument" SortInstrument
                sortable wDistance true "Distance" distanceTip SortDistance
                sortable wDate true "Date (UTC)" "Observation time, UTC" SortDate
                sortable wResolution false "Res." resolutionTip SortResolution
            ]

        /// A fold inside a section, in the sub-header style, with "showing x of y" on
        /// the right. `content` stays in the page and is only hidden while folded: it
        /// is built once and never removed, so folding neither rebuilds the rows nor
        /// re-inserts a removed element (the Image Search list, updated while a search
        /// runs, did not come back after that).
        let countedFold (title : string) (expanded : aval<bool>) (toggle : ProjectedImageListMessage) (count : aval<string>) (content : DomNode<ProjectedImageListMessage>) =
            let header =
                tr [] [
                    th [style subHeaderStyle] [
                        div [style "display: flex; align-items: center; gap: 6px"] [
                            Incremental.span
                                (AttributeMap.ofList [style "cursor: pointer"; onClick (fun _ -> toggle)])
                                (alist {
                                    let! expanded = expanded
                                    yield i [clazz (if expanded then "caret down icon" else "caret right icon")] []
                                    yield text title
                                })
                            div [style "margin-left: auto; font-weight: normal"] [ Incremental.text count ]
                        ]
                    ]
                ]
            div [] [
                table [clazz "ui celled striped inverted table unstackable"; style "margin: 0"] [
                    tbody [] [ header ]
                ]
                Incremental.div
                    (AttributeMap.ofAMap (amap {
                        let! expanded = expanded
                        if not expanded then yield style "display: none"
                    }))
                    (AList.single content)
            ]

        /// STAC search: the images containing the point picked with the Image Search
        /// tool, with their pixel size, range and sun incidence at the picked point.
        /// Clicking one downloads it and puts it on top of the stack.
        let stacPanel =
            let s = m.stacSearch
            let formatLength (metres : float) =
                if metres >= 10000.0 then sprintf "%.0f km" (metres / 1000.0)
                elif metres >= 1000.0 then sprintf "%.1f km" (metres / 1000.0)
                elif metres >= 1.0 then sprintf "%.1f m" metres
                else sprintf "%.0f cm" (metres * 100.0)
            let cell (width : int) = attribute "style" $"width: {width}px; text-align: right; flex-shrink: 0"
            // native inputs in the panel's dark style; color-scheme darkens the date picker
            let inputStyle (width : string) =
                $"width: {width}; background: #1b1c1d; color: #eee; border: 1px solid #555; border-radius: 3px; padding: 2px 5px; color-scheme: dark"
            let dateInput (value : aval<string>) (msg : string -> StacSearchMessage) =
                Incremental.input (AttributeMap.ofAMap (amap {
                    let! v = value
                    yield attribute "type" "date"
                    yield attribute "value" v
                    yield style (inputStyle "135px")
                    yield onChange (fun t -> StacMessage (msg (t.Trim())))
                }))
            let degreeInput (value : aval<float>) (msg : float -> StacSearchMessage) =
                Incremental.input (AttributeMap.ofAMap (amap {
                    let! v = value
                    yield attribute "type" "number"
                    yield attribute "min" "0"
                    yield attribute "max" "180"
                    yield attribute "step" "5"
                    yield attribute "value" (sprintf "%g" v)
                    yield style (inputStyle "64px")
                    yield onChange (fun t ->
                        match Double.TryParse(t, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                        | true, a -> StacMessage (msg a)
                        | _ -> Nop)
                }))
            /// the status line: a few words, and the whole story for its tooltip
            let status =
                (s.query, s.status) ||> AVal.map2 (fun query status ->
                    let at =
                        query |> Option.map (fun q -> sprintf "%.4f°E %.4f°N (%s)" q.lon q.lat q.body)
                    // the short line's optional parts
                    let tags (parts : list<bool * string>) =
                        parts |> List.choose (fun (shown, t) -> if shown then Some (" · " + t) else None) |> String.concat ""
                    let best kept = kept > StacSearchModel.maxResults, sprintf "best %d" StacSearchModel.maxResults
                    let more kept =
                        if kept > StacSearchModel.maxResults then sprintf " (the best %d shown)" StacSearchModel.maxResults else ""
                    let outside (dropped : StacDropped) =
                        if dropped.outsideFilters > 0 then sprintf "; %d outside the filters" dropped.outsideFilters else ""
                    match status, at with
                    | StacSearchStatus.Searching, Some at ->
                        "searching ...", sprintf "searching at %s ..." at
                    | StacSearchStatus.Checking (matched, n, ByFootprint), Some at ->
                        sprintf "checking %d/%d ..." n matched,
                        sprintf "checking %d of %d footprint(s) at %s ..." n matched at
                    | StacSearchStatus.Checking (matched, n, WithoutFootprint), Some at ->
                        sprintf "checking %d/%d by pointing ..." n matched,
                        sprintf "no footprint at %s -- checking the pointing of %d of %d image(s) without footprint ..." at n matched
                    | StacSearchStatus.Found (matched, kept, ByFootprint, dropped), Some at ->
                        sprintf "%d images%s" kept (tags [ dropped.unresolvedListed > 0, sprintf "%d red" dropped.unresolvedListed; dropped.outsideFilters > 0, sprintf "%d filtered" dropped.outsideFilters; best (kept + dropped.unresolvedListed) ]),
                        sprintf "%d image(s) at %s -- %d footprint(s) matched%s%s" kept at matched (outside dropped) (more (kept + dropped.unresolvedListed))
                    | StacSearchStatus.Found (matched, kept, WithoutFootprint, dropped), Some at ->
                        let unresolved =
                            match dropped.reason with
                            | Some reason when dropped.count > 0 -> sprintf "; %d not checkable, e.g.: %s" dropped.count reason
                            | _ -> ""
                        let spice =
                            if dropped.noSpiceCoverage > 0 then
                                sprintf "; %d not covered by the loaded SPICE kernel (load one that covers their time)" dropped.noSpiceCoverage
                            else ""
                        sprintf "%d images by pointing%s" kept
                            (tags [ dropped.noSpiceCoverage > 0, sprintf "%d no SPICE coverage" dropped.noSpiceCoverage
                                    dropped.count > 0, sprintf "%d uncheckable" dropped.count
                                    dropped.outsideFilters > 0, sprintf "%d filtered" dropped.outsideFilters
                                    best (kept + dropped.unresolvedListed) ]),
                        sprintf "%d image(s) at %s by pointing, of %d without footprint%s%s%s%s" kept at matched (outside dropped) spice unresolved (more (kept + dropped.unresolvedListed))
                    | StacSearchStatus.Failed e, _ when e = PRo3D.GIS.StacSearch.noSpiceKernel -> "no SPICE kernel (hover)", e
                    | StacSearchStatus.Failed e, _ -> "error (hover)", sprintf "error: %s" e
                    | _ ->
                        "use the Image Search tool",
                        "Select the Image Search tool in the toolbar (magnifier), then click the surface, or hold Ctrl and move over it.")
            let header = imageListHeader s.sort (fun key -> StacMessage (SortCandidatesBy key))
            /// A result: its name line (red when its pointing did not resolve), then the
            /// image list's columns. The values are the metadata's, like a library row's;
            /// what the check measured at the point is in the tooltip.
            let row (c : StacCandidate) =
                let id = c.itemId
                let atPoint =
                    match c.check with
                    | Resolved v ->
                        sprintf ". Measured at the picked point: range %s, pixel %s" (formatLength v.rangeM) (formatLength v.pixelSizeM)
                    | Unresolved e -> sprintf ". Pointing not resolved: %s" (e.TrimEnd([|'.'; ' '|]))
                    | StacCheck.Checking -> ""
                // one line: aardvark.media sends attribute values to the page as JavaScript
                // string literals without escaping line breaks, and a newline in one breaks
                // every later DOM update (the list stayed empty, the fold stuck)
                let tooltip =
                    (sprintf "%s. %s. %s%s" c.title c.instrument (formatFullDate c.time) atPoint)
                        .Replace('\r', ' ').Replace('\n', ' ')
                let nameColor = match c.check with Unresolved _ -> "#e88" | _ -> "inherit"
                let instrument, distance, resolution =
                    match c.values with
                    | Some v -> ImageFilter.instrumentKey v.instrument, formatDistanceKm v.distanceKm, formatResolution v.resolution
                    | None -> c.instrument, "...", "..."
                let cell (width : int) (separator : bool) = listCell width false separator listRowHeight
                div [
                    attribute "style" "border-bottom: 1px solid black;"
                    attribute "title" tooltip
                    // the frustum and footprint of the hovered row
                    onMouseEnter (fun _ -> StacMessage (HoverCandidate (Some id)))
                    onMouseLeave (fun _ -> StacMessage (HoverCandidate None))
                ] [
                    div [style $"padding: 0 5px; border-bottom: 1px solid {borderColor}; background: #333; color: {nameColor}; white-space: nowrap; overflow: hidden; text-overflow: ellipsis"] [
                        text c.title
                    ]
                    div [attribute "style" "display: flex; font-weight: bold"] [
                        div [cell wPreview true] [
                            // through PRo3D's own server, which has the sign-in (StacSearch.overviewPath)
                            match c.assets.overview with
                            | Some _ -> img [attribute "src" (PRo3D.GIS.StacSearch.overviewPath c.itemId); attribute "style" "width: 40px; height: 40px; object-fit: contain; background: #111"]
                            | None -> div [] []
                        ]
                        Incremental.div (AttributeMap.ofList [listCell wAdd true true listRowHeight; attribute "title" "Download and add to the projection stack and the Image Library. The downloaded images can be selected for editing from the Image Library. They are not saved with the scene."; onClick (fun _ -> StacMessage (Fetch id))]) (
                            alist {
                                let! downloading = s.downloading |> ASet.contains id
                                if downloading then yield i [clazz "spinner loading icon"] []
                                else yield i [clazz "plus icon"; style "opacity: 0.5"] []
                            })
                        div [cell wFlyTo true] [
                            // needs the downloaded metadata (the check fetched it)
                            match c.values with
                            | Some _ -> i [clazz "location arrow icon"; style "cursor: pointer; opacity: 0.7"; attribute "title" "fly to the image's camera"; onClick (fun _ -> StacMessage (FlyToCandidate id))] []
                            | None -> i [clazz "location arrow icon"; style "opacity: 0.2"] []
                            // the catalog's observation time: no download needed
                            i [clazz "clock outline icon"; style "cursor: pointer; opacity: 0.7; margin-left: 4px"
                               attribute "title" (sprintf "set the GIS time to the image's observation time (%s)" (formatFullDate c.time))
                               onClick (fun _ -> StacMessage (SetTimeToCandidate id))] []
                        ]
                        div [cell wInstrument true] [text instrument]
                        div [cell wDistance true] [text distance]
                        div [cell wDate true; attribute "title" (formatFullDate c.time)] [text (formatListDate c.time)]
                        div [cell wResolution false] [text resolution]
                        // SUN INCIDENCE (disabled, see docs/StacImageSearch.md): the inc. column
                        // div [cell 40 false] [text ((v.pointIncidenceDeg |> Option.map (sprintf "%.0f°") |> Option.defaultValue "-") + mark)]
                    ]
                ]
            div [] [
                Html.table [
                    PRo3D.Core.UI.rowWithTip "Catalog:"
                        "The STAC catalog's API root (/search is appended). Default: the PRO3D_STAC_URL environment variable."
                        [
                            Incremental.input (AttributeMap.ofAMap (amap {
                                let! url = s.serverUrl
                                yield attribute "type" "text"
                                yield attribute "value" url
                                yield attribute "placeholder" "https://.../api"
                                yield style (inputStyle "100%")
                                yield onChange (fun u -> StacMessage (SetServerUrl (u.Trim())))
                            }))
                        ]
                    PRo3D.Core.UI.rowWithTip "Connection:"
                        "Off at start: nothing is sent to the catalog until you connect (or select the Image Search tool). If the catalog wants a sign-in, a dialog asks for it; the password is kept for this session only."
                        [
                            Incremental.div (AttributeMap.ofList [style "display: flex; align-items: center; gap: 8px"]) (
                                alist {
                                    let! connection = s.connection
                                    let chip (color : string) (label : string) (tip : string) =
                                        span [style "display: inline-flex; align-items: center; gap: 5px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap"; attribute "title" tip] [
                                            span [style ("width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; background: " + color)] []
                                            text label
                                        ]
                                    let action (label : string) (icon : string) (msg : StacSearchMessage) =
                                        button [clazz "ui mini compact button"; style ("margin: 0; flex-shrink: 0"); onClick (fun _ -> StacMessage msg)] [
                                            i [clazz (icon + " icon")] []
                                            text label
                                        ]
                                    match connection with
                                    | Disconnected ->
                                        yield chip "#777" "off" "not connected"
                                        yield action "Connect" "plug" Connect
                                    | Connecting ->
                                        yield chip "#e0b040" "connecting ..." "asking the catalog"
                                    | SigningIn { busy = true } ->
                                        yield chip "#e0b040" "signing in ..." "checking the user name and password"
                                    | SigningIn _ ->
                                        yield chip "#e0b040" "sign-in needed" "the sign-in dialog is open"
                                    | Connected user ->
                                        let label = match user with Some u -> sprintf "connected as %s" u | None -> "connected"
                                        yield chip "#3c3" label label
                                        yield action "Disconnect" "power off" Disconnect
                                    | ConnectionFailed e ->
                                        yield chip "#e55" "failed (hover)" e
                                        yield action "Retry" "redo" Connect
                                })
                        ]
                    // The time range is the shared Date filter now (Filters section).
                    // PRo3D.Core.UI.rowWithTip "Time (UTC):"
                    //     "Observation date range, both days included. Clear a field for an open end."
                    //     [
                    //         div [style "display: flex; align-items: center; gap: 6px"] [
                    //             dateInput s.timeFrom SetTimeFrom
                    //             text "to"
                    //             dateInput s.timeTo SetTimeTo
                    //         ]
                    //     ]
                    // SUN INCIDENCE (disabled, see docs/StacImageSearch.md)
                    // PRo3D.Core.UI.rowWithTip "Sun incidence:"
                    //     "Sun incidence at the picked point, degrees: 0 = sun overhead, 90 = sun on the horizon, above 90 = night. Low (0-40) for albedo and colour, high (55-80) for relief: long shadows. Images whose sidecar has no sun position are kept."
                    //     [
                    //         div [style "display: flex; align-items: center; gap: 6px"] [
                    //             degreeInput s.incidenceMin SetIncidenceMin
                    //             text "° to"
                    //             degreeInput s.incidenceMax SetIncidenceMax
                    //             text "°"
                    //         ]
                    //     ]
                ]
                div [attribute "style" "display: flex; align-items: center; gap: 6px; padding: 2px 5px; color: #bbb; font-size: 0.85em"] [
                    // one line; the details are in the tooltip
                    Incremental.div
                        (AttributeMap.ofAMap (amap {
                            let! (_, full) = status
                            yield attribute "title" full
                            yield style "flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis"
                        }))
                        (alist {
                            let! (short, _) = status
                            yield text short
                        })
                    i [clazz "eraser icon"; style "cursor: pointer"; attribute "title" "clear the results"; onClick (fun _ -> StacMessage ClearResults)] []
                    i [clazz "trash alternate outline icon"; style "cursor: pointer"
                       attribute "title" "delete the downloaded images and sidecars (%TEMP%\\PRo3D\\stac); a search downloads them again"
                       onClick (fun _ -> StacMessage ClearCache)] []
                ]
                // the hovered row's overview, large, over the image column (its
                // caption repeats the title); ignores the mouse so the row stays hovered.
                // Off for now: it hides too much of the list.
                let showLargePreview = false
                let preview =
                    alist {
                        let! hovered = s.hovered
                        match hovered with
                        | _ when not showLargePreview -> ()
                        | None -> ()
                        | Some id ->
                            let! candidates = s.candidates.Content
                            match candidates |> IndexList.tryFind (fun _ c -> c.itemId = id) with
                            | Some c ->
                                match c.assets.overview with
                                | Some _ ->
                                    yield div [attribute "style" "position: absolute; left: 4px; top: 26px; width: 208px; z-index: 10; pointer-events: none; background: #111; border: 1px solid #666; padding: 4px; box-shadow: 0 2px 8px rgba(0,0,0,0.8)"] [
                                        img [attribute "src" (PRo3D.GIS.StacSearch.overviewPath c.itemId); attribute "style" "display: block; width: 200px; height: 200px; object-fit: contain"]
                                        div [attribute "style" "color: #ccc; font-size: 0.8em; padding-top: 3px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis"] [
                                            text (sprintf "%s  %s" c.title (c.time.ToString("yyyy-MM-dd HH:mm")))
                                        ]
                                    ]
                                | None -> ()
                            | None -> ()
                    }
                // not-yet-checked rows stay hidden: hundreds of "checking" rows would
                // bury the hits; the status line counts them instead
                let shown = s.candidates |> AList.choose (fun c -> match c.check with StacCheck.Checking -> None | _ -> Some c)
                // showing the rows listed of the items the catalog matched
                let count =
                    (shown.Content, s.status) ||> AVal.map2 (fun shown status ->
                        let total =
                            match status with
                            | StacSearchStatus.Checking (matched, _, _)
                            | StacSearchStatus.Found (matched, _, _, _) -> matched
                            | _ -> shown.Count
                        sprintf "showing %d of %d" shown.Count total)
                let results =
                    div [attribute "style" "border: 2px solid black; position: relative"] [
                        Incremental.div AttributeMap.empty preview
                        header
                        Incremental.div (AttributeMap.ofList [attribute "style" "max-height: 320px; overflow-y: auto"]) (
                            shown |> AList.map row
                        )
                    ]
                countedFold "Images" s.imagesExpanded (StacMessage ToggleImagesExpanded) count results
            ]

        // library column header and rows, framed together by imagesPanel: the image
        // list layout the Image Search shares (imageListHeader)
        let libraryHeader, libraryRows =
            let header = imageListHeader m.librarySort SortLibraryBy
            let cell (width : int) (separator : bool) = listCell width false separator listRowHeight
            let rows =
                Incremental.div (AttributeMap.ofList [ attribute "class" "table-container" ]) (
                    alist {
                        let domNodes =
                            visibleImages
                            |> AList.map (fun image ->
                                // image.id is NonAdaptive, so rows can be keyed by it directly
                                let id = image.id
                                div [
                                    // black row separators, like the Projection Stack's frame
                                    attribute "style" "border-bottom: 1px solid black;"
                                    onMouseEnter (fun _ -> HoverImage (Some id))
                                    onMouseLeave (fun _ -> HoverImage None)
                                ] [
                                    // the name selects the image; the selected one is highlighted
                                    Incremental.div
                                        (AttributeMap.ofAMap (amap {
                                            let! selected = m.selectedImage |> AVal.map (fun sel -> sel = Some id)
                                            let background = if selected then "rgba(33,133,208,0.6)" else "#333"
                                            yield style $"cursor: pointer; padding: 0 5px; border-bottom: 1px solid {borderColor}; background: {background}; white-space: nowrap; overflow: hidden; text-overflow: ellipsis"
                                            yield attribute "title" (if selected then "click to deselect" else "click to select")
                                            yield onClick (fun _ -> SelectImage id)
                                        }))
                                        (AList.single (Incremental.text (image.texture |> AVal.map Path.GetFileName)))
                                    div [attribute "style" "display: flex; font-weight: bold"]
                                        [
                                            // a thumbnail of the file (LibraryThumbnails, rendered on request)
                                            Incremental.div (AttributeMap.ofList [cell wPreview true]) (
                                                alist {
                                                    let! texture = image.texture
                                                    yield img [attribute "src" (PRo3D.GIS.LibraryThumbnails.thumbnailPath texture)
                                                               attribute "style" "width: 40px; height: 40px; object-fit: contain; background: #111"]
                                                })
                                            Incremental.div (AttributeMap.ofList [cell wAdd true]) (
                                                alist {
                                                    let! inStack = isInStack id
                                                    if inStack then
                                                        yield i [clazz "layer group icon"; style "cursor: pointer"; attribute "title" "in the projection stack: click to remove"; onClick (fun _ -> RemoveFromStack id)] []
                                                    else
                                                        yield i [clazz "plus icon"; style "cursor: pointer; opacity: 0.5"; attribute "title" "add to the projection stack"; onClick (fun _ -> AddToStack id)] []
                                                })
                                            Incremental.div (AttributeMap.ofList [cell wFlyTo true]) (
                                                alist {
                                                    yield i [clazz "location arrow icon"; style "cursor: pointer; opacity: 0.7"; attribute "title" "fly to the image's camera"; onClick (fun _ -> FlyToImage id)] []
                                                    // only with an observation time (from the image's metadata)
                                                    let! t = image.time
                                                    if t <> DateTime.MinValue then
                                                        yield i [clazz "clock outline icon"; style "cursor: pointer; opacity: 0.7; margin-left: 4px"
                                                                 attribute "title" (sprintf "set the GIS time to the image's observation time (%s)" (formatFullDate t))
                                                                 onClick (fun _ -> SetTimeToImage id)] []
                                                })
                                            div [cell wInstrument true] [ Incremental.text (image.instrument |> AVal.map ImageFilter.instrumentKey) ]
                                            // distance = |TRG_POS|; zero means the sidecar does not say where the
                                            // camera was, which only the MbiBased orientation source needs
                                            Incremental.div (AttributeMap.ofList [ cell wDistance true ]) (
                                                alist {
                                                    let! f = image.distance
                                                    if f > 0.0 then yield text (formatDistanceKm f)
                                                    else
                                                        yield span [ style "color: #e07070"
                                                                     attribute "title" "The metadata's target position (TRG_POS) is zero: no distance, and the MbiBased orientation source cannot place this image. Use Spice or MbiAttitudeSpiceRange." ] [
                                                            text "no TRG_POS" ]
                                                })
                                            // ISO and UTC, like the Date filter -- not the system's local date format
                                            Incremental.div
                                                (AttributeMap.ofAMap (amap {
                                                    yield cell wDate true
                                                    let! t = image.time
                                                    yield attribute "title" (formatFullDate t)
                                                }))
                                                (AList.single (Incremental.text (image.time |> AVal.map formatListDate)))
                                            div [cell wResolution false] [ Incremental.text (image.resolution |> AVal.map formatResolution) ]
                                        ]
                                ]
                            )
                        for domNode in domNodes do
                            yield domNode
                })
            header, rows

        /// Under the library: why it is empty. No images at all, or images that the
        /// filters all hide (with the way out); italic. Nothing while a row is shown.
        let emptyLibraryNote =
            // equal values do not re-render, so moving a filter
            // slider only touches the DOM when the library becomes (non-)empty
            let note =
                (m.images.Content, visibleImages.Content)
                ||> AVal.map2 (fun all shown ->
                    if all.IsEmpty then Some "No images found"
                    elif shown.IsEmpty then Some "No images found. Adjust filter settings"
                    else None)
            Incremental.div AttributeMap.empty (
                alist {
                    match! note with
                    | Some message ->
                        yield div [style "padding: 5px; font-style: italic; color: white"] [text message]
                    | None -> ()
                })


        /// Header (folds like Selected Image), then the library: its column header
        /// and rows, with the empty note inside the frame like the stack's.
        let imagesPanel =
            // built once, re-yielded on unfold: folding does not rebuild the rows
            let list =
                div [] [
                    div [style "border: 2px solid black; border-bottom: none"] [libraryHeader]
                    div [style "border: 2px solid black; border-top: none"] [libraryRows; emptyLibraryNote]
                ]
            let count =
                (visibleImages.Content, m.images.Content)
                ||> AVal.map2 (fun shown all -> sprintf "showing %d of %d" shown.Count all.Count)
            countedFold "Images" m.libraryExpanded ToggleLibraryExpanded count list

        // label | control, in the inverted table style of Html.row; the label carries
        // an optional tooltip
        let settingRow (label : string) (tooltip : Option<string>) (control : list<DomNode<_>>) =
            let labelAttributes =
                match tooltip with
                | Some t -> [clazz "collapsing"; attribute "title" t]
                | None   -> [clazz "collapsing"]
            tr [] [ td labelAttributes [text label]; td [clazz "right aligned"] control ]

        /// The library's source folder: Import Directory and the folder's path. At the
        /// top of the Image Library section.
        let importBox =
            div [style "padding: 5px;"] [
                // a subtle box over the panel's width; the button sits on top of its
                // left end (pulled over the 1px border), so the label reads as the
                // button's extension and the text keeps its place after the button
                div [style "display: flex; align-items: center; gap: 8px; border: 1px solid rgba(255,255,255,0.15); border-radius: .28571429rem; background: rgba(255,255,255,0.03);"] [
                    button [clazz "ui button tiny";
                            style "flex-shrink: 0; margin: -1px 0 -1px -1px"
                            Dialogs.onChooseDirectory (Guid.NewGuid()) (fun (guid, chosen) -> LoadImagesDir (chosen) );
                            clientEvent "onclick" (jsImportDialog) ] [
                            text "Import Directory"
                    ]
                    // long paths are cut off; the tooltip shows the whole one
                    Incremental.div
                        (AttributeMap.ofAMap (amap {
                            yield style "flex: 1; min-width: 0; padding-right: 8px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap"
                            let! dir = m.imageDirectory
                            match dir with
                            | Some d -> yield attribute "title" d
                            | None -> ()
                        }))
                        (alist {
                            let! dir = m.imageDirectory
                            yield text (dir |> Option.defaultValue "no directory selected")
                        })
                ]
            ]

        let content = 
            // no scroller of its own: the page scrolls. A max-height here left an empty
            // strip at the bottom of the tab, under which the panel scrolled away.
            div [style "color: white;"] [

                // stays at the top while the panel scrolls (.pro3d-info-bar, semui-overrides.css)
                div [clazz "pro3d-info-bar"] [
                    // underlined: the term has a tooltip saying where the kernel is set
                    text "ensure that the correct "
                    span [style "text-decoration: underline; text-underline-offset: 2px; cursor: help"
                          attribute "title" "Load it in the top bar: Load SPICE kernel, next to Reference System"] [
                        text "SPICE kernel"
                    ]
                    text " is set for the image projection"
                ]

                accordion "Projection Settings" "settings" false (style "margin-top: 6px") [
                    Html.table [
                        // blends the projected stack over the terrain
                        settingRow "Image Opacity:" None [
                            Numeric.view' [NumericInputType.Slider] m.projectionOpacity |> UI.map SetProjectionOpacity
                        ]
                        settingRow "Visibility:" None [
                            Html.SemUi.dropDown m.instrumentVisibility SetInstrumentVisbilityMode
                        ]
                        // off = the image's own RGB, untouched (internally: the
                        // transfer function off). Instrument data needs the colour
                        // map to be readable at all, but an ordinary RGB image does not
                        // -- and only untransformed pixels can be compared against the
                        // source to check a projection.
                        settingRow "Apply Image Adjustments:"
                                   (Some "On: each image's Range, Brightness/Contrast and False Colour (Selected Image › Properties) are applied to its projection on the surface. Off: the images are projected as stored, untouched -- for RGB images and for checking a projection against its source.") [
                            GuiEx.iconCheckBox m.useTransferFunction ToggleTransferFunction
                        ]
                        // for OPCs whose triangles are wound inward: without it the
                        // projection only survives near the limb
                        settingRow "Winding Correction:"
                                   (Some "Estimate each OPC hierarchy's triangle winding and correct inward-wound ones. Only needed when a projection survives only near the limb. Off costs nothing.") [
                            GuiEx.iconCheckBox m.windingCorrection ToggleWindingCorrection
                        ]
                        settingRow "Orientation Source:"
                                   (Some "Where each projector's position and attitude come from. MBI: the image's own SC_QUAT and TRG_POS. MBI attitude, SPICE range: SC_QUAT with the target on the boresight at the SPICE range (sidecars without TRG_POS). SPICE: SPK position and the instrument's own frame (spacecraft CK x frames kernel), not aimed -- the pointing SPICE predicts. SPICE, aimed: spacecraft CK without the frames kernel, turned onto the target centre -- always lands on the body and so hides pointing errors.") [
                            Html.SemUi.dropDown' (AList.ofList OrientationSource.all) m.projectionMethod SetProjectionMethod OrientationSource.label
                        ]
                    ]

                    table [clazz "ui celled striped inverted table unstackable"] [
                        tbody [] [
                            // header row like the GIS View tables (Entities, Reference
                            // Frames): a plain th row, no <thead>
                            tr [] [ th [attribute "colspan" "2"] [text "Registration"] ]
                            Html.row "Roll:"  [Numeric.view' [NumericInputType.InputBox] m.boresightAdjustment.roll  |> UI.map SetRoll]
                            Html.row "Pitch:" [Numeric.view' [NumericInputType.InputBox] m.boresightAdjustment.pitch |> UI.map SetPitch]
                            Html.row "Yaw:"   [Numeric.view' [NumericInputType.InputBox] m.boresightAdjustment.yaw   |> UI.map SetYaw]
                        ]
                    ]
                ]

                // the fields of view projection uses, next to the projection settings
                accordion "Instrument Settings" "camera" false (style "margin-top: 6px") [
                    InstrumentFovApp.view m.instrumentFovs |> UI.map InstrumentFovMessage
                ]

                // shared by the library and the Image Search (STAC): one set of filters,
                // applied by the same rules to both (ImageFilter.passes)
                accordion' filtersTitle "filter" false (style "margin-top: 6px") [
                    div [style "padding: 4px 5px; font-style: italic; opacity: 0.8"] [
                        text "Apply to the Image Library and to the Image Search (STAC) results."
                    ]
                    filterPanel
                ]

                accordion "Selected Image" "image" false (style "margin-top: 6px") [
                    selectedImageContent
                ]

                accordion "Image Library" "images" false (style "margin-top: 6px") [
                    importBox
                    // flows with the page: nothing pinned, no scroll area of its own
                    imagesPanel
                ]

                accordion "Image Search (STAC)" "search" false (style "margin-top: 6px") [
                    stacPanel
                ]

                accordion "Projected Images" "layer group" false (style "margin-top: 6px") [
                    stackPanel
                ]
            ]
            

        require Html.semui (content)
