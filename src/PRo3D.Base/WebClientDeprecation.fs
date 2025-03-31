namespace PRo3D.Base

open System.IO
open System.Net.Http

[<AutoOpen>]
module Helpers =
    type HttpClient with
        member x.DownloadFileAsync(uri : string, filename : string) =
            task {
                let! data = x.GetByteArrayAsync(uri)
                return! File.WriteAllBytesAsync(filename, data)
            }

        member x.DownloadFile(uri : string, filename : string) =
            x.DownloadFileAsync(uri, filename).Result