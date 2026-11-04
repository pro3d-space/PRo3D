namespace PRo3D.Base

open System
open System.Threading
open System.Collections.Concurrent

/// Everything PRo3D is doing right now, readable from *outside* `app.lock` - for automation
/// that has to know when the next input is safe to send (`GET /activity`, docs/Activity.md).
///
/// `Busy` (PRo3D.Viewer) answers a different question - "is the window frozen, and why?" -
/// for the busy pill: one update-thread slot and one background slot, labelled operations
/// only. That is right for a person looking at a pill and wrong for a test: two overlapping
/// background jobs clear each other's slot, unlabelled work does not show at all, and nothing
/// says whether the last input has been processed. This module counts instead.
///
/// Plain cells and counters, no adaptive state, no lock shared with the update thread: the
/// HTTP handler must answer while an update is wedged, as `/busy` does.
module Activity =

    /// Work in flight per label. A counter, not a slot: every `track` adds one and removes
    /// it in its `finally`, so overlapping and nested work all show.
    let private running = ConcurrentDictionary<string, int>()

    let private change (label : string) (delta : int) =
        running.AddOrUpdate(label, delta, fun _ n -> n + delta) |> ignore

    /// Runs `f`, counting it under `label` while it runs. Cheap enough for hot paths that are
    /// not per-element: one dictionary update on entry and one on exit.
    let track (label : string) (f : unit -> 'a) : 'a =
        change label 1
        try f () finally change label -1

    /// Updates of the viewer's update function: how many are running (normally 0 or 1;
    /// RemoteApi's UpdateSync can overlap) and how many have finished since start. The
    /// counter lets a client see that an input it sent has been through `update`.
    let private updating  = [| 0 |]
    let private processed = [| 0L |]

    let update (f : unit -> 'a) : 'a =
        Interlocked.Increment(&updating.[0]) |> ignore
        try f ()
        finally
            Interlocked.Decrement(&updating.[0]) |> ignore
            Interlocked.Increment(&processed.[0]) |> ignore

    /// Whether the active tool owns the left mouse button after the last update
    /// (`ViewerApp.toolArmed`). Set by the viewer; a client waits for it before a tool click,
    /// because a click that arrives before the Ctrl keydown has armed the tool is taken as
    /// navigation and dropped without a trace.
    let mutable private armed = false
    let setArmed (value : bool) = armed <- value

    /// Messages waiting in the viewer's own mailbox (`MessagingMailbox`), registered by the
    /// viewer at start. aardvark.media's queue of client events is not visible here; `processed`
    /// and `armed` are what tell a client its input has arrived.
    let mutable private queueLength : unit -> int = fun () -> 0
    let registerQueue (length : unit -> int) = queueLength <- length

    /// `{"idle":true,"updating":0,"queued":0,"processed":1234,"armed":false,"running":{"picking":1}}`.
    /// Hand-written like `Busy.toJson`: labels are literals from the call sites, nothing to escape.
    let toJson () =
        let active =
            running.ToArray()
            |> Array.filter (fun kv -> kv.Value > 0)
            |> Array.sortBy (fun kv -> kv.Key)
        let queued = try queueLength () with _ -> 0
        let updating = Volatile.Read(&updating.[0])
        let idle = active.Length = 0 && updating = 0 && queued = 0
        let runningJson =
            active
            |> Array.map (fun kv -> sprintf "\"%s\":%d" kv.Key kv.Value)
            |> String.concat ","
        sprintf "{\"idle\":%b,\"updating\":%d,\"queued\":%d,\"processed\":%d,\"armed\":%b,\"running\":{%s}}"
            idle updating queued (Interlocked.Read(&processed.[0])) armed runningJson
