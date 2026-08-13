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

        /// GetAwaiter().GetResult() rethrows the original exception, while Wait() wraps it
        /// in an AggregateException whose message is the useless "One or more errors
        /// occurred." - keep the actual cause visible in the log.
        member x.DownloadFile(uri : string, filename : string) =
            x.DownloadFileAsync(uri, filename).GetAwaiter().GetResult()