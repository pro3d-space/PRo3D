namespace PRo3D.Core

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives

open FSharp.Data.Adaptive

open PRo3D.Base
 
module Dialogs = 
    let jsImportImagesDialog =
            "parent.aardvark.dialog.showOpenDialog({title:'Import Images' , filters: [{ name: 'Images (*.jpg|*.tif)', extensions: ['jpg','tif','tiff']},], properties: ['openFile', 'multiSelections']}).then(result => {parent.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

module ImagesView = 


        let viewImageHeader (index : Index) (textRepresentation : aval<string>)= 
            [
                Incremental.text textRepresentation; 
                text " "

                i [ clazz "Remove icon red";                                             
                    //onClick (fun _ -> removeImage (index.GetValue()))
                ] [] |> UI.wrapToolTip DataPosition.Bottom "Remove"                                         
            ]  

        let viewProjectedImages (selectedImage : aval<Option<Index>>) (images : alist<'image>) 
                                (selectImage : Index -> 'msg) (name : 'image -> aval<string>) =
                               
            let listAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap


            let imagesGui = 
                images |> AList.mapi (fun index img -> 
                    
                    let isSelected = 
                        selectedImage 
                        |> AVal.map (function 
                            | Some selected when selected = index -> true
                            | _ -> false
                        )
       
                    let itemAttributes = 
                        amap {                                
                            yield clazz "large cube middle aligned icon";

                            let color = isSelected |> AVal.map (function true -> C4b.VRVisGreen | _ -> C4b.Gray)
                            let! c = color
                            let bgc = sprintf "color: %s" (Html.color c)

                            yield style bgc
                            yield onClick (fun _ -> selectImage index)
                        } |> AttributeMap.ofAMap

                    div [clazz "item"] [
                        Incremental.i itemAttributes AList.empty
                        div [clazz "content"] [
                            //Incremental.i listAttributes AList.empty
                            div [clazz "header"] (
                                viewImageHeader index (name img) 
                            )      
                        ]
                    ]
                )

            Incremental.div listAttributes imagesGui
             

        let viewProjectedImagesProperties<'msg, 'image> 
                                          (loadImagesDir : string -> 'msg) 
                                          (selectedImage : aval<Option<Index>>) 
                                          (images : alist<'image>) 
                                          (selectImage : Index -> 'msg) 
                                          (name : 'image -> aval<string>) = 
            
            //let readImagesGui =
            //    //let attributes = 
            //    //    [
            //    //        clazz "ui button tiny"
            //    //        onEvent "onchoose2" [] (fun _ -> loadImages [])
            //    //        Dialogs.onChooseFiles (fun a -> loadImages a)
            //    //        clientEvent "onclick" (Dialogs.jsImportImagesDialog)
            //    //        style "word-break: break-all"
            //    //    ]
            //    div [ clazz "ui item"; Dialogs.onChooseFiles (fun a -> loadImagesDir a); clientEvent "onclick" Dialogs.jsImportImagesDialog ] [
            //        text "Import Traverses (*.json)"
            //    ]
                //button attributes [text "load images"]
            let jsImportImageInfoDialog =
                "top.aardvark.dialog.showOpenDialog({title:'Import Imageinfo files' , filters: [{ name: '(*.json)', extensions: ['json']},], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

            let readImageInfoGui =
                let attributes = 
                    alist {
                        //yield Dialogs.onChooseFiles ImportExInTrinsics;
                        yield clientEvent "onclick" (jsImportImageInfoDialog)
                        yield (style "word-break: break-all")
                    } |> AttributeMap.ofAList 

                let content =
                        alist {
                            yield i [clazz "ui button tiny"] []
                        }
                Incremental.div attributes content


            require GuiEx.semui (
                Html.table [
                        //Html.row "use image:"  [ GuiEx.iconCheckBox m.footPrint.useProjectedImage ToggleProjectedImage ]
                        //Html.row "load image info"  [readImageInfoGui]
                        Html.row "Images:" [ viewProjectedImages selectedImage images selectImage name ]
                ] 
            )


module ProjectedImagesApp = 

    open System.IO
    open PRo3D.Core.Gis

    let update (msg : ImageProjectionMessage) (m : ProjectedImages) = 
        match msg with
        | ImageProjectionMessage.LoadImagesDir d -> 
            let imageExts = [".tif";".tiff";".jpg"]
            let images = 
                Directory.EnumerateFiles(d) 
                |> Seq.filter (fun p -> 
                    let e = Path.GetExtension p
                    List.contains e imageExts 
                )
                |> Seq.map (fun path -> 
                    let infoFile = Path.ChangeExtension(path, ".json")
                    let info = 
                        if File.Exists infoFile then
                            try 
                                File.ReadAllText infoFile |> InstrumentProjection.Serialization.deserialize |> Some
                            with e -> 
                                Log.warn $"image info deserialization failed. {e}"
                                None
                        else
                            None
                    { fullName = path ; projection = info }
                )
                
            { m with images = images |> IndexList.ofSeq }
        | ImageProjectionMessage.SelectImage img -> { m with selectedImage = Some img }

    let viewProjectedImages (m : AdaptiveProjectedImages) =
        ImagesView.viewProjectedImagesProperties (fun s -> ImageProjectionMessage.LoadImagesDir s) m.selectedImage m.images ImageProjectionMessage.SelectImage  (fun (i : ProjectedImage) -> Path.GetFileName(i.fullName) |> AVal.constant)