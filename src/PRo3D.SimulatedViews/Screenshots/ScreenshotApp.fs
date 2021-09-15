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

    let init url outputPath samples =
        let (initNumeric : NumericInput) = 
            {
                min = 1.0
                max = 8000.0
                step = 1.0
                value = 500.0
                format = "{0:0}"
            }

        {
            width   = initNumeric
            height  = initNumeric
            samples = samples
            url     = url
            imageFormat = ImageFormat.PNG
            outputPath = outputPath
        }

    let createUrl (m : ScreenshotApp) (wc : System.Net.WebClient) =
        let stats = ScreenshotUtilities.Utilities.downloadClientStatistics m.url wc
        let renderingNodeId = stats.[0].name
        let url = sprintf "%s/rendering/screenshot/%s?w=%i&h=%i&samples=%i&fmt=%s" 
                          m.url renderingNodeId (int m.width.value) (int m.height.value)
                          m.samples (imageFormatToString m.imageFormat)
        Log.line "[Screenshots] URL: %s" url
        url

    let mutable imgNr = 0
    let makeScreenshot (m : ScreenshotApp) =
        let wc = new System.Net.WebClient()
        let url = createUrl m wc

        let filename = sprintf "img%03i.%s" imgNr (imageFormatToString m.imageFormat)
        imgNr <- imgNr + 1
        let filenamepath = Path.combine [m.outputPath;filename]        

        wc.DownloadFile(url, filenamepath)
        Log.line "[Screenshot] Screenshot saved to %s" filenamepath

    let update (m : ScreenshotApp) ( action : ScreenshotAppAction) = 
        match action with
        | SetWidth msg -> 
            {m with width = Numeric.update m.width msg}
        | SetHeight msg -> 
            {m with height = Numeric.update m.height msg}
        | CreateScreenshot ->
            makeScreenshot m
            m
        | SetImageFormat format ->
            {m with imageFormat = format}
        | OpenFolder ->
            System.Diagnostics.Process.Start("explorer.exe", m.outputPath) |> ignore
            m

    let view (m : AdaptiveScreenshotApp) = 
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
                    Html.row "Height (pixel):"  [Numeric.view' [NumericInputType.InputBox] m.height]  
                        |> UI.map SetHeight
                    Html.row "Image Format:"  [formatDropdown]  
                ]
            ]
        )

