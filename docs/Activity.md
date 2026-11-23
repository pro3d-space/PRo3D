# Activity endpoint

`GET /activity` tells automation (UI specs, remote clients) what PRo3D is doing right now, so
that it can send the next input when it will be taken, instead of sleeping and hoping.

Synopsis: a JSON answer with counted running work, the update queue, and whether the active tool is armed
Status: Released
Interacts with: [BusyIndicator.md](BusyIndicator.md) (separate, unchanged)

```
GET http://localhost:<port>/activity
{"idle":false,"updating":0,"queued":0,"processed":1873,"armed":true,"running":{"kdtree":1}}
```

| Field | Meaning |
| --- | --- |
| `idle` | nothing running, no update in progress, nothing queued |
| `updating` | updates of the viewer running now (0 or 1; RemoteApi's synchronous updates can overlap) |
| `queued` | messages waiting in the viewer's own mailbox (background results, imports) |
| `processed` | updates finished since start - grows when an input has been through `update` |
| `armed` | after the last update, whether the active tool owns the left mouse button (Ctrl held, or not held in [Direct Tool Mode](DirectToolMode.md)) |
| `running` | work in flight, counted per label: `picking` (hover preview pick), `kdtree` (a patch's KdTree and triangles being loaded), `geological map` (contacts to units), plus every label the busy pill knows for background work |

It is top level, like `/busy`, so it is there without `-remoteApi`, and it takes no lock: it
answers while an update is wedged.

## Why automation needs it

PRo3D drops some inputs without a trace when they arrive at the wrong moment:

- **A tool click before Ctrl has armed the tool.** Arming goes through the model
  (`toolArmed`), so a click sent right after the Ctrl keydown can be taken as navigation.
- **A pick while a patch's KdTree is loading.** A first pick on a cold patch loads it, seconds
  on a busy machine.

Fixed sleeps either waste time or lose inputs, depending on how loaded the machine is. Wait for
the state instead:

```ts
import { untilIdle, untilArmed } from "../src/activity";

await untilIdle(app);               // nothing running, nothing queued (3 polls in a row)
await page.keyboard.down("Control");
await untilArmed(app, true);        // the keydown has been through update
await page.mouse.click(x, y);
await page.keyboard.up("Control");
await untilArmed(app, false);
```

`tests-ui/src/activity.ts` has the helpers; `tests-ui/tests/activity.spec.ts` checks the
endpoint against a real Ctrl arming and a cold KdTree load.

## `/activity` and `/busy`

Both answer "what is PRo3D doing", for different readers.

| | `/busy` | `/activity` |
| --- | --- | --- |
| For | the busy pill: a person, "why is the window frozen?" | automation: "may I send the next input?" |
| Update thread | the one labelled slow operation, if any (`ViewerApp.busyLabel`) | every update, counted, plus a processed counter |
| Background | one slot, the outermost label | every job, counted per label; overlapping jobs all show |
| Unlabelled work | invisible by design (costs nothing) | KdTree loads and the geological map recompute are tracked |
| Input state | - | `armed`, `processed` |
| Threshold | the pill waits 400 ms (`-busyms`) | none: anything running counts |

`/busy` stays the pill's source and is unchanged. Adding a label to `/activity` is one
`Activity.track "label" (fun () -> ...)` around the work (`src/PRo3D.Base/Activity.fs`); a
`Busy.scopeBackground` is counted automatically.

## Limits

- aardvark.media's own queue of client events is not visible. `processed` and `armed` are what
  show that an input has arrived; `idle` alone does not.
- Rendering-side work (texture uploads, shader compilation) is not tracked.
