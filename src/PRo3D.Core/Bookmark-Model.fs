namespace PRo3D.Core

open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI.Primitives
open PRo3D
open PRo3D.Base

open Chiron

#nowarn "0686"

[<ModelType>]
type Bookmark = {
    version         : int
    key             : Guid
    name            : string
    cameraView      : CameraView
    exploreCenter   : V3d
    navigationMode  : NavigationMode
}

module Bookmark =

    let current = 0   

    let read0 = 
        json {
            let! key            = Json.read "key"
            let! name           = Json.read "name"
            let! cameraView     = Json.readWith Ext.fromJson<CameraView,Ext> "cameraView"
            let! exploreCenter  = Json.read "exploreCenter"
            let! navigationMode = Json.read "navigationMode"
            
            return
                {
                    version        = current
                    key            = key
                    name           = name
                    cameraView     = cameraView
                    exploreCenter  = exploreCenter |> V3d.Parse
                    navigationMode = navigationMode |> enum<NavigationMode>
                }
        }

type Bookmark with 
    static member FromJson( _ : Bookmark) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Bookmark.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of Bookmark" 
                |> Json.error
        }

    static member ToJson(x : Bookmark) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "key"        x.key
            do! Json.write "name"       x.name
            do! Json.writeWith Ext.toJson<CameraView,Ext> "cameraView" x.cameraView
            do! Json.write "exploreCenter"  (x.exploreCenter.ToString())
            do! Json.write "navigationMode" (x.navigationMode |> int)
        }

//type HarriSchirchWrongBlockingCollection<'a>() =
//    let sema = new System.Threading.SemaphoreSlim(0)
//    let l = obj()
//    let queue = System.Collections.Generic.Queue<'a>()
//    let mutable finished = false

//    member x.TakeAsync() =
//        async {
//            do! sema.WaitAsync() |> Async.AwaitTask
//            if finished then return None
//            else
//                return 
//                    lock l (fun _ -> 
//                        if queue.IsEmptyOrNull () then None
//                        else 
//                          let item = queue.Dequeue() |> Some
//                          if queue.IsEmptyOrNull () then finished <- true
//                          item
//                    )
//        }

//    member x.Enqueue(v) =
//        lock l (fun _ -> 
//            queue.Enqueue(v)
//        )
//        sema.Release() |> ignore

//    member x.CompleteAdding() =
//        finished <- true
//        sema.Release() |> ignore

//    member x.Restart() =
//        finished <- false
//        sema.Release() |> ignore

//    member x.Start() =
//        finished <- false
//        queue.Clear()
//        sema.Release() |> ignore

//    member x.IsCompleted = finished





