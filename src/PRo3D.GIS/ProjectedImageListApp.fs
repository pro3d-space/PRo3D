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

    let update (m : ProjectedImageListModel) (msg : ProjectedImageListMessage) =
        match msg with
        | Nop -> m
        | SetLightingMode mode ->
            { m with lightingMode = mode }
        | SetInstrumentVisbilityMode mode ->
            { m with instrumentVisibility = mode }
        | SetProjectionMethod method ->
            { m with projectionMethod = method }
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
        | LoadImagesDir directory -> 
            let imageExts = [".tif";".tiff";".jpg";".jpeg";".png";".exr"]
            let images' = 
                Directory.EnumerateFiles(directory) 
                |> Seq.filter (fun p ->
                    let e = (Path.GetExtension p).ToLowerInvariant()
                    List.contains e imageExts
                )
                |> Seq.map (fun path -> 
                    ProjectedImageApp.loadFile(path)
                ) |> IndexList.ofSeq
            let first = images' |> IndexList.tryFirst |> Option.map (fun i -> i.id)

            // a fresh library invalidates every id the old one handed out
            { m with
                images = images'
                stack = IndexList.Empty
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
            match m.stack |> IndexList.tryFindIndex id with
            | None -> m
            | Some idx ->
                let without = m.stack |> IndexList.remove idx
                let position = position |> max 0 |> min without.Count
                { m with stack = without |> IndexList.insertAt position id }
        | HoverImage id ->
            { m with hoveredImage = id }
        | FlyToImage _ ->
            // handled by the Viewer, which owns the camera animation
            m
        // Sorting permutes the library only. selectedImage/editImages/stack are
        // keyed by Guid, so none of them needs remapping any more -- this used
        // to be ~30 lines of index bookkeeping per sort, via a partial Seq.head.
        | SortEntriesByDistance ->
            { m with images = m.images |> IndexList.sortBy (fun p -> p.distance) }
        | SortEntriesByDate ->
            { m with images = m.images |> IndexList.sortBy (fun p -> p.time) }

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
                                    
                onBoot "$('#__ID__').accordion();" (
                    div [styling] [
                        div [clazz "ui inverted accordion fluid"] [
                            div [clazz title; style "background-color: #282828"] [
                                    i [clazz ("dropdown icon")] []
                                    text text'                                
                                    div [style "float:right"] [i [clazz (icon + " icon")] []]
                                
                            ]
                            div [clazz content;  style "overflow-y : auto; "] content' 
                        ]
                    ]
                )

        let isInStack (id : Guid) =
            m.stack.Content |> AVal.map (IndexList.exists (fun _ i -> i = id))

        /// The projection stack panel: draw order bottom -> top, displayed top
        /// first (what you see on the surface wins at the top). Minimal
        /// controls -- add/remove from the library, reorder with arrows; drag
        /// & drop can replace the arrows later without touching the model.
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
                        yield div [attribute "style" "padding: 5px; color: #999"] [
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
                                attribute "style" $"display: flex; align-items: center; gap: 4px; padding: 3px 5px; border-bottom: 1px solid {borderColor}"
                                onMouseEnter (fun _ -> HoverImage (Some id))
                                onMouseLeave (fun _ -> HoverImage None)
                            ] [
                                i [clazz "arrow up icon"; style "cursor: pointer"; onClick (fun _ -> MoveInStack (id, stackPos + 1))] []
                                i [clazz "arrow down icon"; style "cursor: pointer"; onClick (fun _ -> MoveInStack (id, stackPos - 1))] []
                                i [clazz "remove icon"; style "cursor: pointer"; onClick (fun _ -> RemoveFromStack id)] []
                                i [clazz "location arrow icon"; style "cursor: pointer"; onClick (fun _ -> FlyToImage id)] []
                                Incremental.text name
                            ]
                })

        let contentImages =
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
            Incremental.div (AttributeMap.ofList [ attribute "class" "table-container" ]) (
                alist {
                    yield header
                    yield div [attribute "style" "max-height: calc(100vh - 300px); overflow: auto;" ] [
                        yield Incremental.div (AttributeMap.ofList [ attribute "style" "overflow-y: visible; " ]) (
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
                    ]
                })


        let content = 
            div [style "overlow-y: auto; max-height: calc(100vh - 95px);"] [

                div [clazz "ui inverted list"] [
                    div [clazz "item"; style "border-bottom: solid 1px black; padding: 5px; display: flex; justify-content: space-between; align-items: center;"] [
                        div [] [text "Data:"]
                        button [clazz "ui button tiny";
                                style "margin-left: auto;"
                                Dialogs.onChooseDirectory (Guid.NewGuid()) (fun (guid, chosen) -> LoadImagesDir (chosen) );
                                clientEvent "onclick" (jsImportDialog) ] [
                                text "Import Directory"
                        ]
                    ]
                ]

                // settings and the 2D preview fold away (the GIS tab is tight
                // on space): import, stack and library stay in view
                accordion "Projection Settings" "settings" false (style "margin-top: 6px") [
                    div [clazz "ui inverted list"] [
                        div [clazz "item"; style "border-bottom: solid 1px black; height: 30px; padding: 5px; display: flex; justify-content: space-between; align-items: center;"] [
                            div [] [text "Visualization:"]
                            div [style "margin-left: auto;"] [
                                Numeric.view' [NumericInputType.Slider] m.projectionOpacity |> UI.map SetProjectionOpacity
                            ]
                        ]

                        div [clazz "item"; style "border-bottom: solid 1px black; height: 30px; padding: 5px; display: flex; justify-content: space-between; align-items: center;"] [
                            div [] [text "Visibility:"]
                            div [style "margin-left: auto;"] [
                                Html.SemUi.dropDown m.instrumentVisibility SetInstrumentVisbilityMode
                            ]
                        ]

                        div [clazz "item"; style "border-bottom: solid 1px black; height: 30px; padding: 5px; display: flex; justify-content: space-between; align-items: center;"] [
                            div [] [text "Sun / Lighting Mode:"]
                            div [style "margin-left: auto;"] [
                                Html.SemUi.dropDown m.lightingMode SetLightingMode
                            ]
                        ]

                        div [clazz "item"; style "border-bottom: solid 1px black; height: 30px; padding: 5px; display: flex; justify-content: space-between; align-items: center;"] [
                            div [] [text "Orientation Source:"]
                            div [style "margin-left: auto;"] [
                                Html.SemUi.dropDown m.projectionMethod SetProjectionMethod
                            ]
                        ]

                        div [clazz "item"; style "border-bottom: solid 1px black; padding: 5px; display: flex; justify-content: space-between; align-items: center;"] [
                            div [] [text "SPICE Kernel:"]
                            button [clazz "ui button tiny";
                                    style "margin-left: auto;"
                                    Dialogs.onChooseDirectory (Guid.NewGuid()) (fun (guid, chosen) -> LoadSpiceAndTime (chosen) );
                                    clientEvent "onclick" (jsImportDialog) ] [
                                    text "Load Spice and Time"
                            ]
                        ]

                        div [clazz "item"; style "margin-top: 10px;"] [
                            div [style "padding-left: 5px"] [text "Registration:"]
                            Html.table [
                                Html.row "Roll:" [Numeric.view' [NumericInputType.InputBox] m.boresightAdjustment.roll |> UI.map SetRoll]
                                Html.row "Pitch:" [Numeric.view' [NumericInputType.InputBox] m.boresightAdjustment.pitch |> UI.map SetPitch]
                                Html.row "Yaw:" [Numeric.view' [NumericInputType.InputBox] m.boresightAdjustment.yaw |> UI.map SetYaw]
                            ]
                        ]
                    ]
                ]

                accordion "Selected Image" "image" false (style "margin-top: 6px") [
                    div [style "width: 100%"] [showRelative2DImage (selectedImage m)]
                ]

                div [] [
                    stackPanel
                    div [style $"border: 2px solid black; margin-top: 10px"] [
                            contentImages
                    ]
                ]
            ]
            

        require Html.semui (content)
