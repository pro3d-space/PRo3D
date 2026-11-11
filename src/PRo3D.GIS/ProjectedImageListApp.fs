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

    let update (m : ProjectedImageListModel) (msg : ProjectedImageListMessage) =
        match msg with
        | Nop -> m
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
        | LoadSpiceAndTime _ ->
            // handled by GisApp.update, which owns the spice kernel + observation time state
            m
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
                selectedImage = first
                filter = ImageFilter.forImages images' m.filter.expanded }
        | SelectImage id ->
            { m with selectedImage = Some id }
        | TogglePreviewExpanded ->
            { m with previewExpanded = not m.previewExpanded }
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
        // Sorting permutes the library only. selectedImage/stack are
        // keyed by Guid, so none of them needs remapping any more -- this used
        // to be ~30 lines of index bookkeeping per sort, via a partial Seq.head.
        // Fresh indices (replaceImages): sortBy reuses the old ones, which left
        // every row with the id of the image that used to be there.
        | SortEntriesByDistance ->
            { m with images = ProjectedImageListModel.replaceImages (m.images |> IndexList.sortBy (fun p -> p.distance)) m }
        | SortEntriesByDate ->
            { m with images = ProjectedImageListModel.replaceImages (m.images |> IndexList.sortBy (fun p -> p.time)) m }

    /// Adaptive counterpart of ProjectedImageListModel.tryFind. `id` is
    /// NonAdaptive, so this is a plain lookup inside a single AVal.map rather
    /// than a bind over every image's own aval.
    let tryFindAdaptive (id : Guid) (m : AdaptiveProjectedImageListModel) =
        m.images.Content
        |> AVal.map (IndexList.tryFind (fun _ (img : AdaptiveProjectedImageModel) -> img.id = id))

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


        let accordion text' icon active styling content' =
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
                                    text text'                                
                                    div [style "float:right"] [i [clazz (icon + " icon")] []]
                                
                            ]
                            // no overflow here: the page is the panel's only scroller.
                            // Indented, with a thin line along its left edge: one level in
                            div [clazz content; style "border-left: 1px solid rgba(255,255,255,0.2); padding-left: 3px"] content'
                        ]
                    ]
                )

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

        /// The library rows the filters let through. filterA, then the row map: a
        /// row's DomNode is built once per image, not rebuilt whenever a filter moves.
        let visibleImages =
            m.images |> AList.filterA (fun img ->
                adaptive {
                    let! instruments = filter.instruments.Content
                    let! lower = filter.distance.lower.value
                    let! upper = filter.distance.upper.value
                    let! instrument = img.instrument
                    let! distance = img.distance
                    return ImageFilter.passes instruments lower upper instrument distance
                })

        let rangeIsActive (r : AdaptiveRangeFilter) =
            adaptive {
                let! lower = r.lower.value
                let! min = r.lower.min
                let! upper = r.upper.value
                let! max = r.upper.max
                return lower > min || upper < max
            }

        let rangeOf (kind : RangeFilterKind) =
            match kind with
            | RangeFilterKind.SunElevation -> filter.sunElevation
            | RangeFilterKind.SunAzimuth   -> filter.sunAzimuth
            | RangeFilterKind.Resolution   -> filter.resolution
            | RangeFilterKind.Distance     -> filter.distance

        let activeCount =
            let count (n : aval<int>) (active : aval<bool>) =
                (n, active) ||> AVal.map2 (fun n active -> if active then n + 1 else n)
            [ RangeFilterKind.SunElevation; RangeFilterKind.SunAzimuth; RangeFilterKind.Resolution; RangeFilterKind.Distance ]
            |> List.map (rangeOf >> rangeIsActive)
            |> List.fold count (filter.instruments.Content |> AVal.map (fun s -> if s.IsEmpty then 0 else 1))

        // the accent the busy chip and the tool strip use: a filter that hides rows
        let filterAccent = "#e3b341"

        let invariant (v : float) = v.ToString("R", Globalization.CultureInfo.InvariantCulture)

        let filterMsg (f : 'a -> ImageFilterMessage) (a : 'a) = FilterMessage (f a)

        /// Two range inputs over one track: the lower and the upper bound. A bound is
        /// sent on release (onchange) -- the browser moves the thumb on its own while
        /// dragging, so the update thread is not flooded.
        let dualSlider (kind : RangeFilterKind) (r : AdaptiveRangeFilter) =
            let thumb (input : AdaptiveNumericInput) (isLower : bool) =
                let attributes =
                    amap {
                        yield attribute "type" "range"
                        let! min = input.min
                        let! max = input.max
                        let! step = input.step
                        let! value = input.value
                        yield attribute "min" (invariant min)
                        yield attribute "max" (invariant max)
                        yield attribute "step" (invariant step)
                        yield attribute "value" (invariant value)
                        // a lower thumb pushed to the far right would sit under the
                        // upper one and could never be dragged back: lift it then
                        let! upper = r.upper.value
                        if isLower && value > min + 0.5 * (max - min) && value >= upper then
                            yield style "z-index: 2"
                        yield onChange (fun s ->
                            match Double.TryParse(s.Trim('"'), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                            | true, v ->
                                let action = Numeric.SetValue v
                                FilterMessage (if isLower then SetLower (kind, action) else SetUpper (kind, action))
                            | _ -> Nop)
                    } |> AttributeMap.ofAMap
                Incremental.input attributes

            let fillAttributes =
                amap {
                    let! lower = r.lower.value
                    let! upper = r.upper.value
                    let! min = r.lower.min
                    let! max = r.upper.max
                    let span = if max > min then max - min else 1.0
                    let left = 100.0 * (lower - min) / span
                    let width = 100.0 * (upper - lower) / span
                    yield clazz "pro3d-dual-range-fill"
                    yield style (sprintf "left: %.3f%%; width: %.3f%%" left width)
                } |> AttributeMap.ofAMap

            let containerAttributes =
                amap {
                    let! active = rangeIsActive r
                    yield clazz (if active then "pro3d-dual-range active" else "pro3d-dual-range")
                } |> AttributeMap.ofAMap

            Incremental.div containerAttributes (
                AList.ofList [
                    div [clazz "pro3d-dual-range-track"] []
                    Incremental.div fillAttributes AList.empty
                    thumb r.lower true
                    thumb r.upper false
                ])

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

        /// The instruments of the library as toggle chips; none selected = all shown.
        let instrumentFilterRow =
            let instruments =
                m.images
                |> AList.mapA (fun img -> img.instrument |> AVal.map ImageFilter.instrumentKey)
                |> AList.toASet
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

        let notComputed = Some "not computed yet: this filter has no effect for now"

        let filterPanel =
            let header =
                let total = m.images.Content |> AVal.map (fun l -> l.Count)
                let shown = visibleImages.Content |> AVal.map (fun l -> l.Count)
                tr [] [
                    th [attribute "colspan" "5"; style subHeaderStyle] [
                        div [style "display: flex; align-items: center; gap: 6px"] [
                            Incremental.span
                                (AttributeMap.ofList [style "cursor: pointer"; onClick (fun _ -> FilterMessage ToggleExpanded)])
                                (alist {
                                    let! expanded = filter.expanded
                                    yield i [clazz (if expanded then "caret down icon" else "caret right icon")] []
                                    yield text "Filters"
                                    let! n = activeCount
                                    if n > 0 then
                                        yield span [style $"color: {filterAccent}; margin-left: 6px"] [text (sprintf "(%d active)" n)]
                                })
                            div [style "margin-left: auto; font-weight: normal"] [
                                Incremental.text ((shown, total) ||> AVal.map2 (sprintf "showing %d of %d"))
                            ]
                            Incremental.span AttributeMap.empty (
                                alist {
                                    let! n = activeCount
                                    if n > 0 then
                                        yield i [clazz "undo icon"; style "cursor: pointer"
                                                 attribute "title" "reset all filters"
                                                 onClick (fun _ -> FilterMessage ResetAll)] []
                                })
                        ]
                    ]
                ]
            table [clazz "ui celled striped inverted table unstackable"; style "margin: 0"] [
                Incremental.tbody AttributeMap.empty (
                    alist {
                        yield header
                        let! expanded = filter.expanded
                        if expanded then
                            yield rangeFilterRow "Sun Elevation [°]" notComputed RangeFilterKind.SunElevation
                            yield rangeFilterRow "Sun Azimuth [°]" notComputed RangeFilterKind.SunAzimuth
                            yield instrumentFilterRow
                            yield rangeFilterRow "Resolution [m/px]" notComputed RangeFilterKind.Resolution
                            yield rangeFilterRow "Distance [km]" None RangeFilterKind.Distance
                    })
            ]

        /// Header (folds like Filters), then the selected image's 2D preview and its
        /// edit parameters -- or a note when nothing is selected.
        let selectedImagePanel =
            let selected = selectedImage m
            // built once: binding on the selection would rebuild the preview's render
            // control on every click; it follows `selected` itself
            let preview = div [style "width: 100%"] [showRelative2DImage selected]
            let header =
                tr [] [
                    th [style subHeaderStyle] [
                        Incremental.span
                            (AttributeMap.ofList [style "cursor: pointer"; onClick (fun _ -> TogglePreviewExpanded)])
                            (alist {
                                let! expanded = m.previewExpanded
                                yield i [clazz (if expanded then "caret down icon" else "caret right icon")] []
                                yield text "Selected Image"
                            })
                    ]
                ]
            div [] [
                table [clazz "ui celled striped inverted table unstackable"; style "margin: 0"] [
                    tbody [] [ header ]
                ]
                Incremental.div AttributeMap.empty (
                    alist {
                        let! expanded = m.previewExpanded
                        if expanded then
                            let! hasSelection = selected |> AVal.map Option.isSome
                            if hasSelection then
                                yield preview
                                yield Incremental.div AttributeMap.empty (
                                    alist {
                                        match! selected with
                                        | Some img ->
                                            yield showDOM img |> UI.map (fun msg -> ProjectedImageListMessage.ImageMessage (img.id, msg))
                                        | None -> ()
                                    })
                            else
                                yield div [style "padding: 5px; font-style: italic"] [text "No image is currently selected"]
                    })
            ]

        // library column header and rows, framed together by imagesPanel
        let libraryHeader, libraryRows =
            // one column: its width (shared by header and rows, so they line up) and the
            // cell height. Rows are 40px; the header is as high as the Projection
            // Stack's header (5px padding around one line)
            let column (width : int) (pointer : bool) (separator : bool) (height : string) =
                let cursor = if pointer then "cursor: pointer; " else ""
                let border = if separator then $"border-right: 1px solid {borderColor}; " else ""
                attribute "style" $"{cursor}width: {width}px; {height}; {border}display: flex; justify-content: center; align-items: center;"
            let rowHeight = "height: 40px"
            let headerHeight = "padding: 5px 0"
            let attributesSelect = column 50 true true rowHeight
            let attributesStack = column 50 true true rowHeight
            let attributesInstrument = column 90 false true rowHeight
            let attributesAttr1 = column 120 true true rowHeight
            let attributesAttr2 = column 120 true false rowHeight

            let header =
                div [
                    attribute "style" $"display: flex; font-weight: bold; border-bottom: 2px solid black; background: black"
                ] [
                    div [ column 50 true true headerHeight ] [text "Select"]
                    div [ column 50 true true headerHeight ] [text "Stack"]
                    div [ column 90 false true headerHeight ] [text "Instrument"]
                    div [ column 120 true true headerHeight ] [
                        i [clazz "sort icon"; onClick (fun _ -> SortEntriesByDistance);] []
                        text "Dist. to Planet"
                    ]
                    div [ column 120 true false headerHeight ] [
                        i [clazz "sort icon"; onClick (fun _ -> SortEntriesByDate);] []
                        text "OBS Date"
                    ]
                ]
            let rows =
                Incremental.div (AttributeMap.ofList [ attribute "class" "table-container" ]) (
                    alist {
                        let domNodes =
                            visibleImages
                            |> AList.map (fun img ->
                                // img.id is NonAdaptive, so rows can be keyed by it directly
                                let id = img.id
                                div [
                                    // black row separators, like the Projection Stack's frame
                                    attribute "style" "border-bottom: 1px solid black;"
                                    onMouseEnter (fun _ -> HoverImage (Some id))
                                    onMouseLeave (fun _ -> HoverImage None)
                                ] [
                                    div [attribute "style" $"border-bottom: 1px solid {borderColor}; background: #333"] [ Incremental.text (img.texture |> AVal.map (fun t -> Path.GetFileName(t))) ]
                                    div [attribute "style" "display: flex; font-weight: bold"]
                                        [
                                            div [attributesSelect] [ Html.SemUi.iconCheckBox (m.selectedImage |> AVal.map (fun sel -> sel = Some id)) (SelectImage id)]
                                            div [attributesStack] [
                                                Incremental.div AttributeMap.empty (
                                                    alist {
                                                        let! inStack = isInStack id
                                                        if inStack then
                                                            yield i [clazz "layer group icon"; style "cursor: pointer"; onClick (fun _ -> RemoveFromStack id)] []
                                                        else
                                                            yield i [clazz "plus icon"; style "cursor: pointer; opacity: 0.5"; onClick (fun _ -> AddToStack id)] []
                                                        yield i [clazz "location arrow icon"; style "cursor: pointer; opacity: 0.7; margin-left: 4px"; onClick (fun _ -> FlyToImage id)] []
                                                    }
                                                )
                                            ]
                                            div [attributesInstrument] [ Incremental.text img.instrument ]
                                            div [attributesAttr1] [ Incremental.text (img.distance |> AVal.map (fun f -> sprintf "%.2f" f)) ]
                                            div [attributesAttr2] [ Incremental.text (img.time |> AVal.map (fun t -> t.ToUniversalTime().ToString())) ]
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
            let header =
                tr [] [
                    th [style subHeaderStyle] [
                        Incremental.span
                            (AttributeMap.ofList [style "cursor: pointer"; onClick (fun _ -> ToggleLibraryExpanded)])
                            (alist {
                                let! expanded = m.libraryExpanded
                                yield i [clazz (if expanded then "caret down icon" else "caret right icon")] []
                                yield text "Images"
                            })
                    ]
                ]
            div [] [
                table [clazz "ui celled striped inverted table unstackable"; style "margin: 0"] [
                    tbody [] [ header ]
                ]
                Incremental.div AttributeMap.empty (
                    alist {
                        let! expanded = m.libraryExpanded
                        if expanded then yield list
                    })
            ]

        // label | control, in the inverted table style of Html.row; the label carries
        // an optional tooltip
        let settingRow (label : string) (tooltip : Option<string>) (control : list<DomNode<_>>) =
            let labelAttributes =
                match tooltip with
                | Some t -> [clazz "collapsing"; attribute "title" t]
                | None   -> [clazz "collapsing"]
            tr [] [ td labelAttributes [text label]; td [clazz "right aligned"] control ]

        let content = 
            // no scroller of its own: the page scrolls. A max-height here left an empty
            // strip at the bottom of the tab, under which the panel scrolled away.
            div [style "color: white;"] [

                div [style "padding: 5px; font-style: italic"] [
                    text "ensure that the correct SPICE kernel is set for the image projection"
                ]

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

                accordion "Projection Settings" "settings" false (style "margin-top: 6px") [
                    Html.table [
                        // blends the projected stack over the terrain
                        settingRow "Image Opacity:" None [
                            Numeric.view' [NumericInputType.Slider] m.projectionOpacity |> UI.map SetProjectionOpacity
                        ]
                        settingRow "Visibility:" None [
                            Html.SemUi.dropDown m.instrumentVisibility SetInstrumentVisbilityMode
                        ]
                        settingRow "Sun / Lighting Mode:" None [
                            Html.SemUi.dropDown m.lightingMode SetLightingMode
                        ]
                        // off = the image's own RGB, untouched. Instrument data needs
                        // the colour map to be readable at all, but an ordinary RGB
                        // image does not -- and only untransformed pixels can be
                        // compared against the source to check a projection.
                        settingRow "Transfer Function:" None [
                            GuiEx.iconCheckBox m.useTransferFunction ToggleTransferFunction
                        ]
                        // for OPCs whose triangles are wound inward: without it the
                        // projection only survives near the limb
                        settingRow "Winding Correction:"
                                   (Some "Estimate each OPC hierarchy's triangle winding and correct inward-wound ones. Only needed when a projection survives only near the limb. Off costs nothing.") [
                            GuiEx.iconCheckBox m.windingCorrection ToggleWindingCorrection
                        ]
                        settingRow "Orientation Source:" None [
                            Html.SemUi.dropDown m.projectionMethod SetProjectionMethod
                        ]
                        settingRow "SPICE Kernel:" None [
                            button [clazz "ui button tiny";
                                    Dialogs.onChooseDirectory (Guid.NewGuid()) (fun (guid, chosen) -> LoadSpiceAndTime (chosen) );
                                    clientEvent "onclick" (jsImportDialog) ] [
                                    text "Load Spice and Time"
                            ]
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

                accordion "Image Library" "images" true (style "margin-top: 6px") [
                    // flows with the page: nothing pinned, no scroll area of its own
                    selectedImagePanel
                    filterPanel
                    imagesPanel
                ]

                accordion "Projected Images" "layer group" true (style "margin-top: 6px") [
                    stackPanel
                ]
            ]
            

        require Html.semui (content)
