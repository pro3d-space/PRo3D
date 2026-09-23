namespace PRo3D.Viewer

open System

/// What the aardvark.media update thread is doing right now, readable from *outside*
/// `app.lock`.
///
/// Deliberately a plain mutable cell rather than model state. The update thread, the
/// DOM-diff thread and the render service all take the same `app.lock`, so while a slow
/// `update` holds it nothing adaptive can reach the browser and the 3D stream stops too.
/// An indicator driven from the model can therefore only ever say "that was slow" after
/// the fact. The only honest source is something the HTTP layer can read without
/// synchronising with the update thread at all - which is what this is.
///
/// See docs/BusyIndicator.md, and plans/stallFeedback.md for what else stalls.
module Busy =

    type private Op = { name : string; startedUtc : DateTime }

    /// Written only by the update thread, read only by the `/busy` request handler. A
    /// reference assignment is atomic and the reader is a fresh closure per request, so no
    /// barrier is needed: the worst case is one poll's worth of staleness, well inside the
    /// threshold the indicator uses anyway.
    let private current : ref<Option<Op>> = ref None

    /// Runs `f`, recording that `opName` is in flight for its duration. A nested call keeps
    /// the outermost label. The `finally` is what guarantees the indicator cannot stick on
    /// when an update throws.
    ///
    /// Updates normally come from the one media update thread, but `RemoteApi` drives some
    /// through `UpdateSync` on an HTTP thread, so two can overlap. Unsynchronised on
    /// purpose: the worst a race can do is report one of the two labels, or neither, and
    /// whoever set the cell always clears it - it cannot latch on.
    let scope (opName : string) (f : unit -> 'a) : 'a =
        match current.Value with
        | Some _ -> f ()
        | None ->
            current.Value <- Some { name = opName; startedUtc = DateTime.UtcNow }
            try f () finally current.Value <- None

    /// `scope` when a label was assigned, a straight call otherwise - an unlabelled message
    /// never touches the cell, so the common path costs nothing.
    let scopeOpt (opName : Option<string>) (f : unit -> 'a) : 'a =
        match opName with
        | Some name -> scope name f
        | None      -> f ()

    /// `{"busy":true,"op":"picking","ms":1234}`. Hand-written on purpose: this must not
    /// depend on a serializer, and four lines of JavaScript are the only consumer. `name`
    /// only ever comes from the literal table in `ViewerApp.busyLabel`, so there is nothing
    /// to escape - keep it that way.
    let toJson () =
        match current.Value with
        | None -> "{\"busy\":false}"
        | Some op ->
            let ms = int (DateTime.UtcNow - op.startedUtc).TotalMilliseconds
            sprintf "{\"busy\":true,\"op\":\"%s\",\"ms\":%d}" op.name ms
