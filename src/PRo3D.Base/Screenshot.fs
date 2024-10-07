namespace PRo3D.Base

open Aardvark.Base
open Aardvark.UI

open System.Net.Http
open System.IO

[<AutoOpen>]
module ScreenshotUtilities =
    module Utilities =

        type ClientStatistics =
            {
                session         : System.Guid
                name            : string
                frameCount      : int
                invalidateTime  : float
                renderTime      : float
                compressTime    : float
                frameTime       : float
            }


        let downloadClientStatistics baseAddress (httpClient : HttpClient) = 
            let path = sprintf "%s/rendering/stats.json" baseAddress //sprintf "%s/rendering/stats.json" baseAddress
            let downloadClientStatistics_ = async {
                Log.line "[Screenshot] querying rendering stats at: %s" path
                let! result = httpClient.GetStringAsync(path) |> Async.AwaitTask
                return result
            }

            let result = downloadClientStatistics_ |> Async.RunSynchronously

            let clientBla : list<ClientStatistics> =
                Pickler.unpickleOfJson result

            match clientBla.Length with
            | 1 | 2 -> clientBla // clientBla.[1] 
            | _ -> failwith (sprintf "Could not download client statistics for %s" path)  //"no client bla"


        let getScreenshotUrl baseAddress clientStatistic width height samples = 
            let screenshot = sprintf "%s/rendering/screenshot/%s?w=%d&h=%d&samples=%d" baseAddress clientStatistic.name width height samples
            Log.line "[Screenshot] Running screenshot on: %s" screenshot    
            screenshot

        let getScreenshotFilename folder name format =
            match System.IO.Directory.Exists folder with
            | true -> ()
            | false -> System.IO.Directory.CreateDirectory folder |> ignore
                
            Path.combine [folder; name + format]


        let downloadFile_ (url: string) (fileStream: FileStream) (client : HttpClient) = async {
            let! responseStream = client.GetStreamAsync(url) |> Async.AwaitTask
            do! responseStream.CopyToAsync(fileStream) |> Async.AwaitTask
        }

        let takeScreenshotFromAllViews baseAddress (width:int) (height:int) (fileName: string) (filePath: string) (format: string) (samples: int) =
                let client = new HttpClient()
                let clientStatistics = downloadClientStatistics baseAddress client

                for cs in clientStatistics do
                    let screenshotUrl = getScreenshotUrl baseAddress cs width height samples
                    let filePathWithName = getScreenshotFilename filePath fileName format
                    use fileStream = File.Create(filePathWithName)
                    downloadFile_ screenshotUrl fileStream client |> Async.RunSynchronously


        let takeScreenshot (baseAddress: string) (width:int) (height:int) (fileName: string) (filePath: string) (format: string) (samples: int) =
            let client = new HttpClient()
            let clientStatistics = downloadClientStatistics baseAddress client
            let cs =
                match clientStatistics.Length with
                | 2 -> clientStatistics.[1] 
                | 1 -> clientStatistics.[0]
                | _ -> failwith (sprintf "Could not download client statistics")

            let screenshotUrl = getScreenshotUrl baseAddress cs width height samples
            let filePathWithName = getScreenshotFilename filePath fileName format
            use fileStream = File.Create(filePathWithName)

            downloadFile_ screenshotUrl fileStream client |> Async.RunSynchronously
            Log.line "Screenshot saved under %s" filePathWithName
