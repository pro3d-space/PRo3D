namespace PRo3D

open System
open System.IO

open Aardvark.Base
open Aardvark.UI
open PRo3D
open OpcViewer.Base

open PRo3D
open PRo3D.Base.Annotation

open System.Net.Http
          
module Mod =
    open FSharp.Data.Adaptive

    let bindOption (m : aval<Option<'a>>) (defaultValue : 'b) (project : 'a -> aval<'b>)  : aval<'b> =
        m
        |> AVal.bind (
            function | None -> AVal.constant defaultValue | Some v -> project v
        )

module Console =    

    let print (x:'a) : 'a =
        printfn "%A" x
        x

module Net =
    open System.Threading
    open Aardvark.UI
    let getClient () =
        let downloadString_ (httpClient: HttpClient) (path: string) = async {
            let! result = httpClient.GetStringAsync(path) |> Async.AwaitTask
            return result
        }
        use cancelToken = new CancellationTokenSource()
        let waitForClient =
            async {
                for i in 1..100 do
                    let httpClient = new HttpClient()
                    try
                        let lst = downloadString_ httpClient "http://localhost:54321/rendering/stats.json" |> Async.RunSynchronously
                        match String.length lst > 3 with
                        | true -> cancelToken.Cancel ()
                        | false -> do! Async.Sleep 1000
                    with ex -> do! Async.Sleep 1000
            }
        try Async.RunSynchronously (waitForClient, -1, cancelToken.Token) with e -> ()
        let httpClient = new HttpClient()
        let jsonString = downloadString_ httpClient "http://localhost:54321/rendering/stats.json" |> Async.RunSynchronously
        let clientStats : list<PRo3D.Base.Utilities.ClientStatistics> =
            Pickler.unpickleOfJson jsonString
        (httpClient, clientStats)


namespace Aardvark.UI

module Events =
    open Aardvark.Application

    let onKeyDown' (cb : Keys -> seq<'msg>) =
        "onkeydown" ,
        AttributeValue.Event(
            Event.ofDynamicArgs
                ["event.repeat"; "event.keyCode"]
                (fun args ->
                    match args with
                        | rep :: keyCode :: _ ->
                            if rep <> "true" then
                                let keyCode = int (float keyCode)
                                let key = KeyConverter.keyFromVirtualKey keyCode
                                Seq.delay (fun () -> cb key)
                            else
                                Seq.empty
                        | _ ->
                            Seq.empty
                )
        )

    /// Ctrl (Cmd on macOS) + Z / Y / S, for a page's body: key events bubble up from
    /// anywhere in the page, so the shortcut no longer needs the render control to have
    /// focus. Which keys are sent, and what text fields keep, is decided client side by
    /// pro3dCtrlShortcut in resources/utilities.js (docs/KeyboardShortcuts.md).
    let onCtrlShortcut (cb : Keys -> 'msg) =
        "onkeydown",
        AttributeValue.Event {
            clientSide = fun send id ->
                "if (window.pro3dCtrlShortcut && pro3dCtrlShortcut(event)) { " + send id ["event.keyCode"] + " }"
            serverSide = fun _ _ args ->
                match args with
                | keyCode :: _ ->
                    match System.Int32.TryParse keyCode with
                    | true, code -> Seq.delay (fun () -> Seq.singleton (cb (KeyConverter.keyFromVirtualKey code)))
                    | _ -> Seq.empty
                | _ ->
                    Seq.empty
        }

