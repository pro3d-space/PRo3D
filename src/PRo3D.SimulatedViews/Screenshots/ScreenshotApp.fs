namespace PRo3D.SimulatedViews

open FSharp.Data.Adaptive
open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open System.IO

open System.Net.Http

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ScreenshotApp =

    let imageFormatToString (format : ImageFormat) =
        match format with
        | ImageFormat.JPEG -> ".jpg"
        | ImageFormat.PNG  -> ".png"
        | _ -> ".png"
        
    let mutable imgNr = 0
    let rec findFreeName outputPath (m : ScreenshotModel) numberOfSamples = 

        let filename = sprintf "img%03i_n%i" imgNr numberOfSamples
        let filenamepath = Path.combine [outputPath;filename+(imageFormatToString m.imageFormat)]        
        if not (File.Exists filenamepath) then
            filename
        else 
            imgNr <- imgNr + 1
            findFreeName outputPath m numberOfSamples


    let update baseUrl numberOfSamples outputPath (m : ScreenshotModel) (action : ScreenshotAction) =
        match action with
        | SetWidth msg -> 
            { m with width = Numeric.update m.width msg }
        | SetHeight msg -> 
            { m with height = Numeric.update m.height msg }
        | SetBackgroundColor msg ->
            { m with backgroundColor = ColorPicker.update m.backgroundColor msg }
        | CreateScreenshot ->
            Utilities.takeScreenshot
                baseUrl
                (int m.width.value)
                (int m.height.value)
                (findFreeName outputPath m numberOfSamples)
                outputPath
                (imageFormatToString m.imageFormat)
                numberOfSamples
            m
        | SetImageFormat format ->
            { m with imageFormat = format }

    let view (screenshotFolder : aval<string>) (m : AdaptiveScreenshotModel) = 
        let formatDropdown =
            Html.SemUi.dropDown m.imageFormat SetImageFormat


        let openFolderAttributes = 
            amap {
                yield clazz "ui icon button"; 
                let! screenshotFolder = screenshotFolder
                let electronCommand = Electron.openPath screenshotFolder  
                yield clientEvent "onclick" electronCommand
            } |> AttributeMap.ofAMap

        require GuiEx.semui (
            div [] [
                button [clazz "ui icon button"; onClick (fun _ -> CreateScreenshot)] [
                    i [clazz "camera icon"] [] ] 
                Incremental.button openFolderAttributes (AList.ofList [i [clazz "folder icon"] []])
                Html.table [  
                    Html.row "Width (pixel):"  [Numeric.view' [NumericInputType.InputBox] m.width]
                    |> UI.map SetWidth

                    Html.row "Height (pixel):" [Numeric.view' [NumericInputType.InputBox] m.height]  
                    |> UI.map SetHeight

                    Html.row "Background Color:"  [ColorPicker.view m.backgroundColor] 
                    |> UI.map SetBackgroundColor

                    Html.row "Image Format:"  [formatDropdown]  
                ]
            ]
        )

