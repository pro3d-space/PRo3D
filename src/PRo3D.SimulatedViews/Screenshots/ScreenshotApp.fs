namespace PRo3D.SimulatedViews

open FSharp.Data.Adaptive
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open System.IO
open System.Threading

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ScreenshotApp =

    let imageFormatToString (format : ImageFormat) =
        match format with
        | ImageFormat.JPEG -> "jpg"
        | ImageFormat.PNG  -> "png"
        | _ -> "png"
    
    let private urlOfStats
        baseUrl
        numberOfSamples
        (m : ScreenshotModel)
        (stats : list<ScreenshotUtilities.Utilities.ClientStatistics>) =
        let color = m.backgroundColor.c.ToC4f().ToV4f()
        let renderingNodeId =
            match stats with
            | first :: _ -> first.name
            | [] -> failwith "[Screenshots] no rendering client available"
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

    let createUrlAsync
        baseUrl
        numberOfSamples
        (m : ScreenshotModel)
        (httpClient : System.Net.Http.HttpClient)
        (ct : CancellationToken) =
        task {
            let! stats = ScreenshotUtilities.Utilities.downloadClientStatisticsAsync baseUrl httpClient ct
            return urlOfStats baseUrl numberOfSamples m stats
        }

    let mutable imgNr = 0
    let private imgNrLock = obj()

    let rec findFreeName outputPath (m : ScreenshotModel) =
        let filename = sprintf "img%03i.%s" imgNr (imageFormatToString m.imageFormat)
        let filenamepath = Path.combine [outputPath;filename]
        if not (File.Exists filenamepath) then
            filenamepath
        else
            imgNr <- imgNr + 1
            findFreeName outputPath m

    /// Screenshots run on a background thread (see makeScreenshot), so hand out names
    /// under a lock - otherwise two shots in flight can pick the same file.
    let private nextFreeName outputPath (m : ScreenshotModel) =
        lock imgNrLock (fun () ->
            if not (Directory.Exists outputPath) then
                Directory.CreateDirectory outputPath |> ignore
            let filenamepath = findFreeName outputPath m
            imgNr <- imgNr + 1
            filenamepath
        )

    /// a large, multisampled shot can take a while to render and to transfer
    let private requestTimeout = System.TimeSpan.FromMinutes 10.0

    /// One client for the whole process: a new HttpClient per shot opens a fresh
    /// connection and leaves the socket in TIME_WAIT. The timeout is set here *and*
    /// per request - HttpClient.Timeout cannot be changed once a request has gone out,
    /// so the per-request token is what makes a deadline adjustable, while this one is
    /// the backstop that keeps a future call without a token from hanging forever.
    let private httpClient =
        new System.Net.Http.HttpClient(Timeout = requestTimeout)

    /// The rendering service only renders the screenshot once its render task can read
    /// the scene graph. `update` runs inside `transact`, so waiting there for the HTTP
    /// response keeps that transaction open, the server never gets to evaluate the scene,
    /// and the request dies in HttpClient's 100s timeout ("A task was canceled.").
    /// So the round trip has to happen off the update thread - and once it does, there is
    /// no reason to block a thread on it either.
    let makeScreenshotAsync baseUrl numberOfSamples outputPath (m : ScreenshotModel) =
        task {
            try
                use cts = new CancellationTokenSource(requestTimeout)
                let! url = createUrlAsync baseUrl numberOfSamples m httpClient cts.Token
                let filenamepath = nextFreeName outputPath m
                let! data = httpClient.GetByteArrayAsync(url, cts.Token)
                do! File.WriteAllBytesAsync(filenamepath, data, cts.Token)
                Log.line "[Screenshot] Screenshot saved to %s" filenamepath
            with e ->
                Log.error "[Screenshot] taking a screenshot failed: %A" e
        }

    /// Fire and forget: `update` cannot await, and every failure is already logged inside
    /// makeScreenshotAsync, so there is no fault left for anyone to observe.
    let makeScreenshot baseUrl numberOfSamples outputPath (m : ScreenshotModel) =
        makeScreenshotAsync baseUrl numberOfSamples outputPath m |> ignore

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

