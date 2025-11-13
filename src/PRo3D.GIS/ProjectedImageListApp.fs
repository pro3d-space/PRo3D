namespace PRo3D.ImageMapping

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open PRo3D.ImageMapping
open PRo3D.Extensions.FSharp
open PRo3D.Base

open System.IO

type Self = Self

module ProjectedImageListApp =

    let borderColor = "rgba(255,255,255,.1)"

    let loadDirMessage (dir : string) = ProjectedImageListMessage.LoadImagesDir dir

    let initial : ProjectedImageListModel = {
        images = IndexList.Empty;
        selectedImage = None;
        editImages = List.Empty;
        projectionOpacity = { Numeric.init with min = 0.0; max = 1.0; step = 0.01; value = 1.0 }
        boresightAdjustment = BoresightAdjustment.identity
        cameraState = OrbitState.create V3d.Zero 0.0 0.0 (2.0 * (3389.5 * 1000.0))
    }

    let update (m : ProjectedImageListModel) (msg : ProjectedImageListMessage) = 
        match msg with
        | Nop -> m
        | OrbitCameraMessage msg ->
            { m with cameraState = OrbitController.update m.cameraState msg }
        | SetProjectionOpacity opacity -> 
            { m with projectionOpacity = Numeric.update m.projectionOpacity opacity }
        | SetRoll r -> { m with boresightAdjustment = { m.boresightAdjustment with roll = Numeric.update m.boresightAdjustment.roll r } }
        | SetPitch r -> { m with boresightAdjustment = { m.boresightAdjustment with pitch = Numeric.update m.boresightAdjustment.pitch r } }
        | SetYaw r -> { m with boresightAdjustment = { m.boresightAdjustment with yaw = Numeric.update m.boresightAdjustment.yaw r } }
        | LoadImagesDir directory -> 
            let imageExts = [".tif";".tiff";".jpg";".exr"]
            let images' = 
                Directory.EnumerateFiles(directory) 
                |> Seq.filter (fun p -> 
                    let e = Path.GetExtension p
                    List.contains e imageExts 
                )
                |> Seq.map (fun path -> 
                    ProjectedImageApp.loadFile(path)
                ) |> IndexList.ofSeq
            let firstIndex = 
                if IndexList.isEmpty images' then
                    None
                else
                    Some (IndexList.firstIndex images')

            { m with images = images'; selectedImage = firstIndex }
        | SelectImage idx -> 
            { m with selectedImage = Some idx }
        | EditImage idx ->
            let editImages' =
                if List.contains idx m.editImages then
                    m.editImages |> List.filter ((<>) idx)
                else
                    idx :: m.editImages
            { m with editImages = editImages'} 
        | ImageMessage (idx, imageMessage) ->
            let images' = m.images |> IndexList.mapi (fun index img ->
                    if index = idx then
                        ProjectedImageApp.update img imageMessage
                    else
                        img
                )
            { m with images = images' }
        | SortEntriesByDistance ->
            let images' = 
                m.images
                |> IndexList.mapi (fun idx e -> (idx, e))
                |> IndexList.toList
                |> List.sortBy (fun (idx, p) -> p.distance)
                |> IndexList.ofList
            let newSelectedIdx = 
                match m.selectedImage with
                | Some selectedImage ->
                    let (newIdx, (oldIdx, img) )= 
                        images'
                        |> IndexList.mapi (fun newIdx (idx, p) -> (newIdx, (idx, p)))
                        |> IndexList.filter (fun (newIdx, (idx, p)) -> idx = selectedImage)
                        |> IndexList.toSeq
                        |> Seq.head
                    Some newIdx
                | None -> None
            let editImages' = 
                images'
                |> IndexList.mapi (fun newIdx (idx, p) -> (newIdx, (idx, p)))
                |> IndexList.filter (fun (newIdx, (idx, p)) -> List.contains idx m.editImages)
                |> IndexList.map (fun (newIdx, (idx, p)) -> newIdx)
                |> IndexList.toList
            { m with
                images = images' |> IndexList.map (fun (idx, img) -> img);
                selectedImage = newSelectedIdx;
                editImages = editImages'
            }
        | SortEntriesByDate ->
            let images' = 
                m.images
                |> IndexList.mapi (fun idx e -> (idx, e))
                |> IndexList.toList
                |> List.sortBy (fun (idx, p) -> p.time)
                |> IndexList.ofList
            let newSelectedIdx = 
                match m.selectedImage with
                | Some selectedImage ->
                    let (newIdx, (oldIdx, img) )= 
                        images'
                        |> IndexList.mapi (fun newIdx (idx, p) -> (newIdx, (idx, p)))
                        |> IndexList.filter (fun (newIdx, (idx, p)) -> idx = selectedImage)
                        |> IndexList.toSeq
                        |> Seq.head
                    Some newIdx
                | None -> None
            let editImages' = 
                images'
                |> IndexList.mapi (fun newIdx (idx, p) -> (newIdx, (idx, p)))
                |> IndexList.filter (fun (newIdx, (idx, p)) -> List.contains idx m.editImages)
                |> IndexList.map (fun (newIdx, (idx, p)) -> newIdx)
                |> IndexList.toList
            { m with 
                images = images' |> IndexList.map (fun (idx, img) -> img);
                selectedImage = newSelectedIdx;
                editImages = editImages'}

    let selectedImage
        (m : AdaptiveProjectedImageListModel) = 
        adaptive {
            let! selectedImage = m.selectedImage
            match selectedImage with
            | Some sel -> 
                let! img = AList.tryGet sel m.images
                match img with
                | Some img' -> return Some img'
                | None -> return None
            | None -> return None
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

        let contentImages = 
            let attributesSelect = attribute "style" $"cursor: pointer; width: 50px; height: 40px; border-right: 1px solid {borderColor}; display: flex; justify-content: center; align-items: center;"
            let attributesEdit = attribute "style" $"cursor: pointer; width: 50px; height: 40px; border-right: 1px solid {borderColor}; display: flex; justify-content: center; align-items: center;"
            let attributesAttr1 = attribute "style" $"cursor: pointer; width: 120px; height: 40px; border-right: 1px solid {borderColor}; display: flex; justify-content: center; align-items: center;"
            let attributesAttr2 = attribute "style" $"cursor: pointer; width: 120px; height: 40px; display: flex; justify-content: center; align-items: center;"

            let header =
                div [ 
                    // attribute "clazz" "title active inverted"
                    attribute "style" $"display: flex; font-weight: bold; border-bottom: 2px solid black; background: black" 
                ] [
                    div [ attributesSelect ] [text "Select"]
                    div [ attributesEdit ] [text "Edit"]
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
                                    |> AList.mapi (fun index img ->
                                        // let distanceToPlanet = CooTransformation.getRelState "HERA" "SUN" "MARS"  
                                        div [attribute "style" $"border: 1px solid rgba(255,255,255,0.5);"] [
                                            div [attribute "style" $"border-bottom: 1px solid {borderColor}; background: #333"] [ Incremental.text (img.texture |> AVal.map (fun t -> Path.GetFileName(t))) ]
                                            div [attribute "style" "display: flex; font-weight: bold"] 
                                                [
                                                    div [attributesSelect] [ Html.SemUi.iconCheckBox (m.selectedImage |> AVal.map (fun selIdx -> selIdx = Some index)) (SelectImage index)]
                                                    div [attributesEdit] [ Html.SemUi.iconCheckBox (m.editImages |> AVal.map (fun editImages -> List.contains index editImages)) (EditImage index)]
                                                    div [attributesAttr1] [ Incremental.text (img.distance |> AVal.map (fun f -> sprintf "%.2f" f)) ]
                                                    div [attributesAttr2] [ Incremental.text (img.time |> AVal.map string) ]
                                                ]
                                        
                                            Incremental.div AttributeMap.empty (
                                                alist { 
                                                    let! isInEditMode = m.editImages |> AVal.map (fun editEntries -> List.contains index editEntries)
                                                    if isInEditMode then
                                                        div [attribute "style" $"border-top: 1px dotted rgba(255,255,255,0.5)"] [
                                                            showDOM img |> UI.map (fun msg -> ProjectedImageListMessage.ImageMessage (index, msg))
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
                    div [clazz "item"; style "border-bottom: solid 1px black; height: 30px; padding: 5px; display: flex; justify-content: space-between; align-items: center;"] [
                        div [] [text "Visualization:"]
                        div [style "margin-left: auto;"] [
                            Numeric.view' [NumericInputType.Slider] m.projectionOpacity |> UI.map SetProjectionOpacity
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

                div [] [
                    div [] [showRelative2DImage (selectedImage m)]
                    div [style $"border: 2px solid black; margin-top: 10px"] [
                            contentImages
                    ]
                ]
            ]
            

        require Html.semui (content)
