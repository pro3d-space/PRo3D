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
                editImages = HashSet.empty
                selectedImage = first }
        | SelectImage id ->
            { m with selectedImage = Some id }
        | EditImage id ->
            { m with editImages = m.editImages |> HashSet.alter id not }
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
        | FlyToImage _ ->
            // handled by the Viewer, which owns the camera animation
            m
        // Sorting permutes the library only. selectedImage/editImages/stack are
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
                            // no overflow here: it would become the sticky container
                            // of the Images section's preview, which then never sticks
                            div [clazz content] content'
                        ]
                    ]
                )

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
                        yield div [attribute "style" "padding: 5px; color: white"] [
                            text "empty -- add images from the library below"
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

        // library header and rows are separate: the header goes into the Images
        // section's sticky block, the rows scroll with the panel underneath it
        let libraryHeader, libraryRows =
            let attributesSelect = attribute "style" $"cursor: pointer; width: 50px; height: 40px; border-right: 1px solid {borderColor}; display: flex; justify-content: center; align-items: center;"
            let attributesEdit = attribute "style" $"cursor: pointer; width: 50px; height: 40px; border-right: 1px solid {borderColor}; display: flex; justify-content: center; align-items: center;"
            let attributesStack = attribute "style" $"cursor: pointer; width: 50px; height: 40px; border-right: 1px solid {borderColor}; display: flex; justify-content: center; align-items: center;"
            let attributesInstrument = attribute "style" $"width: 90px; height: 40px; border-right: 1px solid {borderColor}; display: flex; justify-content: center; align-items: center;"
            let attributesAttr1 = attribute "style" $"cursor: pointer; width: 120px; height: 40px; border-right: 1px solid {borderColor}; display: flex; justify-content: center; align-items: center;"
            let attributesAttr2 = attribute "style" $"cursor: pointer; width: 120px; height: 40px; display: flex; justify-content: center; align-items: center;"

            let header =
                div [
                    // attribute "clazz" "title active inverted"
                    attribute "style" $"display: flex; font-weight: bold; border-bottom: 2px solid black; background: black"
                ] [
                    div [ attributesSelect ] [text "Select"]
                    div [ attributesEdit ] [text "Edit"]
                    div [ attributesStack ] [text "Stack"]
                    div [ attributesInstrument ] [text "Instrument"]
                    div [ attributesAttr1 ] [
                        i [clazz "sort icon"; onClick (fun _ -> SortEntriesByDistance);] []
                        text "Dist. to Planet"
                    ]
                    div [ attributesAttr2 ] [
                        i [clazz "sort icon"; onClick (fun _ -> SortEntriesByDate);] []
                        text "OBS Date"
                    ]
                ]
            let rows =
                Incremental.div (AttributeMap.ofList [ attribute "class" "table-container" ]) (
                    alist {
                        let domNodes =
                            m.images
                            |> AList.map (fun img ->
                                // img.id is NonAdaptive, so rows can be keyed by it directly
                                let id = img.id
                                div [
                                    attribute "style" $"border: 1px solid rgba(255,255,255,0.5);"
                                    onMouseEnter (fun _ -> HoverImage (Some id))
                                    onMouseLeave (fun _ -> HoverImage None)
                                ] [
                                    div [attribute "style" $"border-bottom: 1px solid {borderColor}; background: #333"] [ Incremental.text (img.texture |> AVal.map (fun t -> Path.GetFileName(t))) ]
                                    div [attribute "style" "display: flex; font-weight: bold"]
                                        [
                                            div [attributesSelect] [ Html.SemUi.iconCheckBox (m.selectedImage |> AVal.map (fun sel -> sel = Some id)) (SelectImage id)]
                                            div [attributesEdit] [ Html.SemUi.iconCheckBox (m.editImages |> ASet.contains id) (EditImage id)]
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
    
                                    Incremental.div AttributeMap.empty (
                                        alist {
                                            let! isInEditMode = m.editImages |> ASet.contains id
                                            if isInEditMode then
                                                div [attribute "style" $"border-top: 1px dotted rgba(255,255,255,0.5)"] [
                                                    showDOM img |> UI.map (fun msg -> ProjectedImageListMessage.ImageMessage (id, msg))
                                                ]
                                            else
                                                div [] []
                                        }
                                    )
                                ]
                            )
                        for domNode in domNodes do
                            yield domNode
                })
            header, rows


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

                div [style "padding: 5px; display: flex; align-items: center; gap: 8px;"] [
                    button [clazz "ui button tiny";
                            style "flex-shrink: 0"
                            Dialogs.onChooseDirectory (Guid.NewGuid()) (fun (guid, chosen) -> LoadImagesDir (chosen) );
                            clientEvent "onclick" (jsImportDialog) ] [
                            text "Import Directory"
                    ]
                    // long paths are cut off; the tooltip shows the whole one
                    Incremental.div
                        (AttributeMap.ofAMap (amap {
                            yield style "overflow: hidden; text-overflow: ellipsis; white-space: nowrap"
                            let! dir = m.imageDirectory
                            match dir with
                            | Some d -> yield attribute "title" d
                            | None -> ()
                        }))
                        (alist {
                            let! dir = m.imageDirectory
                            yield text (dir |> Option.defaultValue "not set")
                        })
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

                accordion "Images" "images" true (style "margin-top: 6px") [
                    // the selected image's preview and the column header stay in
                    // view while the library scrolls under them. The page is the
                    // scroller: no ancestor in between may set overflow.
                    div [style "position: sticky; top: 0; z-index: 1; background: #1b1c1d"] [
                        // header styled like Registration in Projection Settings
                        table [clazz "ui celled striped inverted table unstackable"; style "margin-bottom: 0"] [
                            tbody [] [
                                tr [] [ th [] [text "Selected Image"] ]
                            ]
                        ]
                        div [style "width: 100%"] [showRelative2DImage (selectedImage m)]
                        div [style "border: 2px solid black; border-bottom: none; margin-top: 10px"] [libraryHeader]
                    ]
                    div [style "border: 2px solid black; border-top: none"] [libraryRows]
                ]

                accordion "Projected Images" "layer group" true (style "margin-top: 6px") [
                    stackPanel
                ]
            ]
            

        require Html.semui (content)
