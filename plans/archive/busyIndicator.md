# Busy indicator — minimal, shippable

Goal: when PRo3D freezes, the user sees *that* it is working and *what* it is working on,
instead of a dead window. Nothing else changes. No stall gets faster in this change.

Background and the full stall inventory: [../stallFeedback.md](../stallFeedback.md). This plan
is deliberately a strict subset of Phase 1 there.

## Why it has to be built this way

The update thread, the DOM-diff thread and the render service all take the **same
`app.lock`** (`aardvark.media/src/Aardvark.UI/MutableApp.fs:195,332`). During a slow
`update` the HTML cannot change and the 3D image stops. So the indicator cannot come from
the model — it has to come from a path that never takes that lock. That is the whole
design: a mutable cell, an HTTP route that only reads it, and a CSS animation in the
browser (a separate process, so it keeps painting).

## Scope

In:

- A busy cell written around `updateViewer`.
- A `/busy` route.
- A client-side overlay in the main page.
- Labels for **six** action groups only (below).
- An off switch.

Out (explicitly, for this PR): progress percentages, cancel buttons, background jobs,
touching `UserFeedback`, touching `Scene`, and fixing any actual stall — including the
synchronous ArcBall centre-ray pick (`Navigation.fs:286`), which is the next PR.

## The change — 1 new file, 4 edits

### 1. `src/PRo3D.Viewer/Busy.fs` (new)

```fsharp
namespace PRo3D.Viewer

open System

/// What the media update thread is doing right now, readable from *outside* `app.lock`.
///
/// Deliberately a plain mutable cell rather than model state: while a slow `update` holds
/// the lock, nothing adaptive can reach the browser, so the only honest source for a busy
/// indicator is something the HTTP layer can read without synchronising with the update
/// thread at all.
module Busy =

    type private Op = { name : string; startedUtc : DateTime }

    // A ref cell, not a model field. Written only by the update thread, read only by the
    // `/busy` request handler. A reference assignment is atomic, and the reader is a fresh
    // closure per request, so no barrier is needed: the worst case is one poll of staleness.
    let private current : ref<Option<Op>> = ref None

    /// Runs `f`, recording that `opName` is in flight for its duration. Re-entrant calls
    /// keep the outermost label. The `finally` is what guarantees the indicator cannot
    /// stick on when an update throws.
    let scope (opName : string) (f : unit -> 'a) : 'a =
        match current.Value with
        | Some _ -> f ()                       // already inside one, outer label wins
        | None ->
            current.Value <- Some { name = opName; startedUtc = DateTime.UtcNow }
            try f () finally current.Value <- None

    /// `scope` when a label was assigned, a straight call otherwise. Unlabelled messages
    /// pay nothing at all.
    let scopeOpt (opName : Option<string>) (f : unit -> 'a) : 'a =
        match opName with
        | Some n -> scope n f
        | None   -> f ()

    /// `{"busy":true,"op":"picking","ms":1234}`, hand-written: this must not depend on a
    /// serializer, and it is read by four lines of JavaScript.
    let toJson () =
        match current.Value with
        | None -> """{"busy":false}"""
        | Some op ->
            let ms = int (DateTime.UtcNow - op.startedUtc).TotalMilliseconds
            sprintf """{"busy":true,"op":"%s","ms":%d}""" op.name ms
```

`Op.name` only ever comes from the literal table in step 2, so no escaping is needed in
`toJson`. Keep it that way.

Add to `PRo3D.Viewer.fsproj` right after `<Compile Include="Viewer-Model.g.fs" />` — it
needs nothing from the model, and everything that uses it comes later.

### 2. Labels + the wrap — `src/PRo3D.Viewer/Viewer/Viewer.fs`

Next to `updateInternal` (`:2441`):

```fsharp
/// The operations worth telling the user about — the stalls from plans/stallFeedback.md.
/// `None` for everything else: an unlabelled message never touches the busy cell, so the
/// common case costs nothing and can misbehave in no way.
let private busyLabel (msg : ViewerAnimationAction) : Option<string> =
    match msg with
    | ViewerMessage m ->
        match m with
        | NavigationMessage _                                  -> Some "camera"
        | PickSurface _ | PreviewPickSurfaceFinished _          -> Some "picking"
        | ImportSurface _ | DiscoverAndImportOpcs _
        | ImportDiscoveredSurfacesThreads _                     -> Some "importing surfaces"
        | LoadScene _ | SaveScene _                             -> Some "scene file"
        | AnnotationExportMessage _                             -> Some "exporting annotations"
        | DrawingMessage (Drawing.ColorByCategoryMessage
                            ColorByCategoryAction.ResampleSurface) -> Some "sampling surface"
        | _                                                     -> None
    | _ -> None
```

and in `updateInternal`, wrap the existing body — the *only* change to the update path:

```fsharp
Busy.scopeOpt (busyLabel msg) (fun () ->
    match msg with
    | ViewerMessage msg -> updateViewer runtime signature sendQueue mailbox m msg
    | AnewmationMessage msg -> Animation.Animator.update msg m
    | ProvenanceMessage msg -> ProvenanceApp.update msg m
)
```

`NavigationMessage` covers the ArcBall switch (which is the stall that prompted this) and
fires on every mouse move; that is fine — it is two field writes, and the client only shows
anything past the threshold, so ordinary navigation never lights it up.

Exact case names must be checked against `Viewer-Model.fs:85+` when writing this; the
compiler will catch any that are wrong.

### 3. The route — `src/PRo3D.Viewer/Program.fs`

Into the `startServer port [ ... ]` list (`:374-383`), beside `/crash.txt`:

```fsharp
http.route "/busy" >=> http.mimeType "application/json" >=> http.request (fun _ ->
    http.ok (Busy.toJson ()))
```

Top level, **not** under `/api` — `/api` is only mounted when `enableRemoteApi` is set
(`:353-365`) and this has to work in a normal build. The handler reads one ref cell: it
touches no adaptive value, takes no lock, and cannot block. That is the entire reason it
answers while the update thread is wedged.

### 4. The overlay — `src/PRo3D.Viewer/Viewer/ViewerGUI.fs`

In `pageRouting`'s `| None ->` branch (`:2266-2286`), the page that hosts the dock, as a
sibling of `AnnotationExport.exportWindow`. Dock panels are iframes inside this page, so
one fixed-position overlay here covers the whole window:

```fsharp
busyOverlay
```

with, next to `textOverlaysUserFeedback`:

```fsharp
/// Client-side busy indicator. Polls `/busy` and fades in while an operation has been
/// running longer than the threshold. It must be client-side: during the stall it is
/// reporting on, the server cannot update this DOM at all (see plans/busyIndicator.md).
let busyOverlay =
    onBoot (sprintf "startBusyIndicator('__ID__', %d);" Config.busyIndicatorMilliseconds) (
        div [ clazz "pro3d-busy"; style "display:none" ] [
            div [ clazz "pro3d-busy-spinner" ] []
            div [ clazz "pro3d-busy-text" ] []
        ])
```

`startBusyIndicator` goes into the existing embedded `resources/utilities.js` (already in
`viewerDependencies`), plus a `@keyframes` spin rule in `resources/semui-overrides.css`.
Shape:

```js
function startBusyIndicator(id, thresholdMs) {
    if (!thresholdMs) return;                     // 0 disables
    var el = document.getElementById(id);
    setInterval(function () {
        fetch('/busy')
            .then(function (r) { return r.json(); })
            .then(function (s) {
                var on = s.busy && s.ms >= thresholdMs;
                el.style.display = on ? 'flex' : 'none';
                if (on) el.querySelector('.pro3d-busy-text').textContent =
                    s.op + '  ' + (s.ms / 1000).toFixed(1) + 's';
            })
            .catch(function () { /* server busy shutting down, or gone - ignore */ });
    }, 200);
}
```

Rules the CSS must obey, all of them for safety rather than looks:

- `position: fixed; pointer-events: none;` — the overlay can **never** swallow a click,
  whatever state it gets stuck in.
- Not full-screen: a small pill, bottom-centre. It must not hide the scene.
- The spinner is a CSS `@keyframes` animation, not JS-driven — that is what keeps it moving
  while the server is frozen.

### 5. The off switch — `src/PRo3D.Viewer/Config.fs`

```fsharp
/// Show the busy indicator once an operation has run this long. 0 disables it entirely.
let mutable busyIndicatorMilliseconds = 400
```

Wired to a `-nobusy` command-line flag next to the other flags in `Program.fs`, which sets
it to 0. This is the production escape hatch: if the polling or the overlay misbehaves in
the field, the answer is a flag, not a hotfix build.

## Why this is safe to ship

- **No type changes.** No model field, no `Scene` field, so no version bump, no `readN`, no
  `adapt.cmd` run, no `.g.fs` churn. Scene files are untouched.
- **No behaviour change on the update path.** `scopeOpt` with `None` is a direct call. With
  `Some`, it is two ref-cell writes around the call it already made. The `finally` means a
  throwing update leaves the cell clean.
- **The new route cannot break the old ones.** It is read-only, it is additive, and if it
  ever threw, the client's `.catch` swallows it.
- **The overlay cannot eat input.** `pointer-events: none`, and it is a sibling of the dock
  rather than a wrapper around it.
- **One-commit revert.** One new file and four localised edits, no shared code touched.

Known limits, stated up front so nobody expects more: no percentage (a stalled update
cannot report from inside itself), no cancel, and an unlabelled slow action shows nothing
— adding it is one line in `busyLabel`.

## Verification

1. Build, start on a large cold scene.
2. Switch the camera to **ArcBall** (`Navigation.fs:286` picks the centre ray synchronously
   across all active surfaces). The overlay must appear reading `camera 1.4s` while the
   window is otherwise dead, and disappear the moment it returns.
3. Normal FreeFly navigation, panel clicks, drawing a short annotation: overlay must never
   appear.
4. `curl http://localhost:<port>/busy` during the ArcBall stall → `{"busy":true,...}`;
   idle → `{"busy":false}`.
5. Start with `-nobusy`: no polling in the network tab, no overlay.
6. Existing `tests-ui` suite green — the overlay is `display:none` and `pointer-events:none`,
   so it must not intercept any test's click.

## Delivery

- Branch `features/<issue#>_busy-indicator` off `develop` (not the current
  `features/801_simulate-series`).
- `docs/BusyIndicator.md` in the same change: what it watches, the threshold, `-nobusy`,
  and one paragraph on why it is client-side — otherwise the next person will "simplify" it
  into a model field and silently break it.
- Follow-up PR, separately: remove the synchronous ArcBall pick (`Navigation.fs:286`), which
  is the stall this indicator will be pointing at most often.

---

## As built

Implemented on `features/busy-indicator`. [docs/BusyIndicator.md](../../docs/BusyIndicator.md)
is the reference from here on; this file is the reasoning that led to it. Deltas from the
plan above, all small:

- **The CSS is injected by `startBusyIndicator`**, not added to `semui-overrides.css` — the
  overlay then needs no new embedded resource and no change to `viewerDependencies`.
- **Two flags, not one**: `-nobusy` and `-busyms <n>`. The test needs a low threshold, and
  a threshold is the more useful knob in the field anyway.
- **`busyLabel`'s cases are fully qualified** (`ViewerAction.ImportSurface`, …). Several of
  those names also exist on `SurfaceAppAction`, which is opened in `Viewer.fs` and wins.
- **`OpenScene` joined the `scene file` group**; the plan had listed only `LoadScene`/`SaveScene`.
- The overlay is the last child of the `| None ->` page body, after `LayoutApp.UI.dialogs`.
- `launchPro3d` gained an optional `extraArgs` (additive) so the spec can pass `-busyms 1`.

What the spec measured on the Dimorphos test data, cold: the **ArcBall switch blocked the
update thread for 3.98 s**, `/busy` answered throughout with a worst-case round trip of
**27 ms**, and the overlay counted up `camera 0.1s … 3.9s` in the browser while the server
was frozen. That is the design working, and it is also the measurement that justifies the
follow-up PR above — four seconds, on a small body, for one click on a camera button.

The OPC import in the spec turned out *not* to be a stall (~84 ms: the scene already holds
that OPC, so it short-circuits). It stays in as label coverage. A cold import into an empty
scene is the long one, and is not what this spec sets up.

Checking the escape hatch by hand — which the plan above listed as a verification step and
which nothing else would have exercised — found a real bug: `startBusyIndicator` returns
before injecting its stylesheet when the indicator is off, and the overlay was taking its
`display:none; pointer-events:none` from that stylesheet, so `-nobusy` left a visible,
clickable `div` in the page's normal flow. Both properties are now inline on the element,
and the case has its own test.
