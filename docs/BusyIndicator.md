# Busy indicator

Shows what PRo3D is doing while it is too busy to draw anything.

Synopsis: a small pill at the bottom of the window during long-blocking operations
Status: Released
Interacts with: nothing — it observes, it does not participate

Some operations run on the update thread and block the whole application for seconds:
loading a KdTree the first time you click a patch, switching the camera to ArcBall,
importing OPCs, exporting annotations, resampling Color by Category. Before this, the
window simply froze. Now a pill fades in at the bottom, naming the operation and counting
the seconds:

```
 ◐  importing surfaces  3.4s
```

It disappears the moment the operation finishes. Operations shorter than
`Config.busyIndicatorMilliseconds` (400 ms) never show it, so ordinary clicking and
navigating look exactly as they did.

It reports **that** PRo3D is working, not how far along it is — see
[why there is no progress bar](#why-there-is-no-progress-bar).

## Which operations are labelled

| Label | Actions |
| --- | --- |
| `camera` | `NavigationMessage` — the ArcBall switch picks the centre ray against every active surface synchronously |
| `picking` | `PickSurface`, `PreviewPickSurfaceFinished` — a cold patch loads its triangle set |
| `importing surfaces` | `ImportSurface`, `DiscoverAndImportOpcs`, `ImportDiscoveredSurfacesThreads` |
| `scene file` | `LoadScene`, `OpenScene`, `SaveScene`, `SaveAs` |
| `exporting annotations` | `AnnotationExportMessage` |
| `sampling surface` | Color by Category's *resample surface* |

Everything else is deliberately unlabelled: an unlabelled message never touches the busy
cell at all, so the ordinary path costs nothing. A slow action that is *not* in the table
therefore shows nothing — adding it is one line in `ViewerApp.busyLabel`
(`src/PRo3D.Viewer/Viewer/Viewer.fs`).

`plans/stallFeedback.md` is the full inventory of what actually stalls and why; this table
is its shortlist.

### Work that does not freeze the window

One reported operation is *not* on the update thread: the **hover preview pick**. It runs on
a background worker, so the window stays responsive — but the first hover over a cold patch
loads its KdTree and triangle set, and until that returns the 3D cursor and the *Under
Cursor* read-out are still showing the **previous** hit. That is worse than a freeze in one
respect: a freeze is self-evident, whereas a stale cursor looks like a current answer in the
wrong place. The pill reading `picking` is what says the position on screen has not caught
up yet.

The two kinds live in **separate slots** — `Busy.scope` for the update thread,
`Busy.scopeBackground` for workers — and the update thread wins when both are set. With one
shared slot a short background pick could clear the cell while a long update was still
blocking, hiding exactly the case the indicator exists for.

The label is not the cure: the cold patch load is slow partly because of the
evict-everything cache in `ProfileAttributeExtraction` and the whole-grid `List.ofArray` in
`Surface.fs`, both written up in `plans/stallFeedback.md`.

## Turning it off

| Flag | Effect |
| --- | --- |
| `-nobusy` | no overlay and no polling at all |
| `-busyms <n>` | show after `n` ms instead of 400; `0` is the same as `-nobusy` |

These exist so a misbehaving indicator in the field is a flag rather than a hotfix build.

## Why it is built the way it is

**Read this before changing anything here.** The implementation looks like it is going the
long way round, and it is not.

aardvark.media's update thread, its DOM-diff thread and the render service all take the
**same `app.lock`** (`Aardvark.UI/MutableApp.fs`, `App.fs`). While one `update` call is
slow:

- no HTML change can reach the browser,
- the 3D image stream stops,
- and the update thread batches the whole pending message queue into *one* transaction,
  so a "please wait" message emitted just before the work shares its transaction.

An indicator driven from the model can therefore only ever say *that was slow*, after the
fact. That is the trap the existing `UserFeedback` toast (`scene.userFeedback`) falls into:
`ImportDiscoveredSurfacesThreads` queues the text "Importing OPCs..." with the import
itself as the follow-up message, and whether the text is rendered before the import runs is
a scheduling race.

So the indicator is built out of the two things that keep working during a stall:

1. **`GET /busy`** (`Program.fs`) reads one `ref` cell (`Busy.fs`) and returns
   `{"busy":true,"op":"picking","ms":1234}`. It touches no adaptive value and takes no
   lock, which is why it still answers while the update thread is wedged. It is mounted at
   the top level, not under `/api` — that one is only present with `-remoteApi`.
2. **The browser.** `startBusyIndicator` in `resources/utilities.js` polls that route every
   200 ms and toggles a fixed-position pill. The spinner is a CSS `@keyframes` animation,
   so the compositor keeps it turning with the .NET side completely frozen.

The cell is written by a `try/finally` around the existing `updateInternal` body — the only
change to the update path, and the `finally` is what guarantees the indicator cannot stick
on when an update throws.

The pill is `pointer-events: none`, always. However it ends up, it must never be able to
swallow a click meant for the scene or a panel. That — and being hidden by default — is set
**inline on the element**, not in the injected stylesheet: with `-nobusy` the script returns
before injecting anything, and an unstyled `div` would otherwise sit visible in the page's
normal flow and be able to take a click. The script only ever overrides `display`.

### Why there is no progress bar

A percentage would have to come from inside the operation, and the operation is holding the
lock that stops it being reported. Real progress needs the work moved off the update thread
first; `plans/stallFeedback.md` sketches that as a separate `Jobs` layer, with the Color by
Category resample and the annotation export as the first candidates. This change
deliberately does none of it and makes nothing faster — it only stops the freeze being
silent.

## Tests

`tests-ui/tests/busy-indicator.spec.ts` drives the real viewer and proves both halves of the
design against a real stall: a switch to ArcBall, which picks the centre ray against every
active surface synchronously.

Measured on the Dimorphos test data, cold:

| | |
| --- | --- |
| the ArcBall switch blocked the update thread for | **3.98 s** |
| worst `/busy` round trip during it | **27 ms** |
| what the overlay showed | `camera 0.1s` … `camera 3.9s`, then gone |

The 27 ms is the assertion that matters: a route behind `app.lock` would have blocked for
the whole 3.98 s, which is the failure mode this design exists to avoid. The spec also
imports an OPC — short here, because the scene already holds it, so that case covers a
second *label* rather than a second stall — and checks the idle contract, that the pill is
click-through, and that `-nobusy` leaves nothing behind (that last case caught a real bug:
the overlay used to take its hidden/inert state from the stylesheet the script never
injects when it is switched off).

It launches with `-busyms 1`, because the shipped 400 ms threshold exists precisely so
short operations stay invisible.
