namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open System.IO

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ScreenshotApp =

    let imageFormatToString (format : ImageFormat) =
        match format with
        | ImageFormat.JPEG -> "jpg"
        | ImageFormat.PNG  -> "png"
        | _ -> "png"
    
    let createUrl 
        baseUrl 
        numberOfSamples 
        (m : ScreenshotModel) 
        (wc : System.Net.WebClient) =

        let stats = ScreenshotUtilities.Utilities.downloadClientStatistics baseUrl wc
        
        let color = m.backgroundColor.c.ToC4f().ToV4f()
        let renderingNodeId = stats.[0].name
        let url = 
            sprintf "%s/rendering/screenshot/%s?w=%i&h=%i&samples=%i&fmt=%s&background=[%f,%f,%f,%f]" 
                baseUrl 
                renderingNodeId 
                (int m.width.value) 
                (int m.height.value)
                numberOfSamples 
                (imageFormatToString m.imageFormat)
                color.X color.Y color.Z color.W

        Log.line "[Screenshots] URL: %s" url
        url
        
    let mutable imgNr = 0
    let rec findFreeName outputPath (m : ScreenshotModel) = 
        let filename = sprintf "img%03i.%s" imgNr (imageFormatToString m.imageFormat)
        let filenamepath = Path.combine [outputPath;filename]        
        if not (File.Exists filenamepath) then
            filenamepath
        else 
            imgNr <- imgNr + 1
            findFreeName outputPath m
    
    let makeScreenshot baseUrl numberOfSamples outputPath (m : ScreenshotModel) =
        let wc = new System.Net.WebClient()
        let url = createUrl baseUrl numberOfSamples m wc
        let filenamepath = findFreeName outputPath m 
        imgNr <- imgNr + 1
        
        wc.DownloadFile(url, filenamepath)
        Log.line "[Screenshot] Screenshot saved to %s" filenamepath

    let update baseUrl numberOfSamples outputPath (m : ScreenshotModel) (action : ScreenshotAction) =
        match action with
        | SetWidth msg -> 
            { m with width = Numeric.update m.width msg }
        | SetHeight msg -> 
            { m with height = Numeric.update m.height msg }
        | SetBackgroundColor msg ->
            { m with backgroundColor = ColorPicker.update m.backgroundColor msg }
        | CreateScreenshot ->
            makeScreenshot baseUrl numberOfSamples outputPath m
            m
        | SetImageFormat format ->
            { m with imageFormat = format }
        | OpenFolder ->
            System.Diagnostics.Process.Start("explorer.exe", outputPath) |> ignore
            m

    let view (m : AdaptiveScreenshotModel) = 
        let formatDropdown =
            Html.SemUi.dropDown m.imageFormat SetImageFormat

        require GuiEx.semui (
            div [] [
                button [clazz "ui icon button"; onClick (fun _ -> CreateScreenshot)] [
                    i [clazz "camera icon"] [] ] 
                button [clazz "ui icon button"; onClick (fun _ -> OpenFolder)] [
                    i [clazz "folder icon"] [] ] 
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

