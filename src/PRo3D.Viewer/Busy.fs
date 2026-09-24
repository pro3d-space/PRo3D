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

    /// Two slots, not one, because the two kinds of slow work mean different things.
    ///
    /// `onUpdateThread` is work that holds `app.lock`: the window is frozen for its whole
    /// duration. `inBackground` is work on a worker thread - the hover pick loading a cold
    /// patch - where the app stays responsive and only its *answer* is late.
    ///
    /// One shared slot would let a short background pick clear the cell while a long update
    /// is still blocking, hiding exactly the case this exists for. Keeping them apart also
    /// means a background writer cannot disturb the update thread's report at all.
    ///
    /// Written by those threads, read by the `/busy` request handler. A reference assignment
    /// is atomic and the reader is a fresh closure per request, so no barrier is needed: the
    /// worst case is one poll's worth of staleness, well inside the threshold the indicator
    /// uses anyway.
    let private onUpdateThread : ref<Option<Op>> = ref None
    let private inBackground   : ref<Option<Op>> = ref None

    /// Runs `f`, recording that `opName` is in flight in `cell` for its duration. A nested
    /// call keeps the outermost label. The `finally` is what guarantees the indicator cannot
    /// stick on when `f` throws - whoever set a slot always clears it.
    let private enter (cell : ref<Option<Op>>) (opName : string) (f : unit -> 'a) : 'a =
        match cell.Value with
        | Some _ -> f ()
        | None ->
            cell.Value <- Some { name = opName; startedUtc = DateTime.UtcNow }
            try f () finally cell.Value <- None

    /// Work on the update thread, i.e. work that freezes the window.
    ///
    /// Updates normally come from the one media update thread, but `RemoteApi` drives some
    /// through `UpdateSync` on an HTTP thread, so two can overlap. Unsynchronised on
    /// purpose: the worst a race can do is report one of the two labels, or neither.
    let scope (opName : string) (f : unit -> 'a) : 'a = enter onUpdateThread opName f

    /// `scope` when a label was assigned, a straight call otherwise - an unlabelled message
    /// never touches the cell, so the common path costs nothing.
    let scopeOpt (opName : Option<string>) (f : unit -> 'a) : 'a =
        match opName with
        | Some name -> scope name f
        | None      -> f ()

    /// Work on a background worker, which does not block the UI but does leave what is on
    /// screen out of date until it finishes. Used by the hover preview pick, whose cold
    /// patch loads are what make the 3D cursor lag behind the mouse.
    let scopeBackground (opName : string) (f : unit -> 'a) : 'a = enter inBackground opName f

    /// `{"busy":true,"op":"picking","ms":1234}`. Hand-written on purpose: this must not
    /// depend on a serializer, and four lines of JavaScript are the only consumer. `name`
    /// only ever comes from the literal table in `ViewerApp.busyLabel`, so there is nothing
    /// to escape - keep it that way.
    let toJson () =
        // Both slots are reported, update thread first: while it is blocked nothing can
        // repaint at all, so it is the more important of the two, but hiding the other
        // would just lose information. Each carries its own elapsed time, because the
        // threshold is applied per operation - a 50 ms pick alongside a 3 s camera stall
        // should not add noise to the pill.
        //
        // `op`/`ms` repeat the first entry so that a reader of the older, single-operation
        // shape keeps working.
        let ops = [ onUpdateThread.Value; inBackground.Value ] |> List.choose id
        match ops with
        | [] -> "{\"busy\":false}"
        | first :: _ ->
            // one clock for all of them, so the numbers in a single answer agree
            let now = DateTime.UtcNow
            let elapsed (o : Op) = int (now - o.startedUtc).TotalMilliseconds
            let entries =
                ops
                |> List.map (fun o -> sprintf "{\"op\":\"%s\",\"ms\":%d}" o.name (elapsed o))
                |> String.concat ","
            sprintf "{\"busy\":true,\"op\":\"%s\",\"ms\":%d,\"ops\":[%s]}"
                first.name (elapsed first) entries
