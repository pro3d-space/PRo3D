# Direct Tool Mode

Direct Tool Mode is a session-only toggle (top toolbar, next to the other
interaction-mode switches) that changes how the active annotation tool and the
camera share the mouse.

| | Classic scheme | Direct Tool Mode |
|---|---|---|
| Run the active tool | hold **Ctrl** and left-click | plain **left-click** |
| Orbit / look | left-drag | **right-drag** |
| Pan | middle-drag | middle-drag |
| Zoom | wheel | wheel |
| Temporarily hand the left button back to the camera | — | hold **Ctrl** |

The camera is never fully asleep in Direct Tool Mode: middle, right and the wheel
keep driving it while the tool owns the left button, so you can reframe without
letting go of the tool. Holding Ctrl in Direct Tool Mode is the escape hatch —
it disarms the tool and gives the left button back to the orbit controller.

State: `Model.directToolMode`. Never persisted (interaction state, not scene
state). Toggled by `ViewerAction.ToggleDirectToolMode`.

## The modifier key

"Ctrl" in this page means the tool modifier, `Model.ctrlFlag`, set by the
`Keyboard.Modifier` pattern (`Viewer-Utils.fs`):

| Platform | Keys that count |
|---|---|
| Windows, Linux | **Ctrl** (left or right) |
| macOS | **Ctrl** and **Cmd** (left or right) |

On macOS Ctrl+click is a right-click, so **Cmd** is the key to use there.

**Alt / Option is not a modifier.** It used to be one, undocumented: Alt armed
every tool, Alt+S saved the scene, Alt+Z undid, and in Direct Tool Mode Alt
disarmed the tool and orbited the camera, which made box selection's Alt toggle
impossible. Alt is now only read where a feature asks for it, from the mouse
event (box selection's toggle).

**Losing focus releases the modifier.** When the render control loses keyboard
focus (Cmd+Tab, Alt+Tab, a click into a panel), it sends the key-ups for Ctrl and
Shift itself. A key-up that happens in another window never reaches PRo3D, and a
stuck `ctrlFlag` freezes the camera and arms every tool.

The Windows key is deliberately not a modifier on Windows: it opens the Start
menu, and its key-up would be lost.

## Arming model

Whether the active tool "owns the left click right now" is a single predicate:

```
toolArmed  =  ctrlFlag <> directToolMode
```

i.e. classic = *armed while Ctrl held*, Direct Tool Mode = *armed unless Ctrl
held*. It has two representations that must stay in step:

- `ViewerApp.toolArmed : Model -> bool` — used by the `DrawLog` pick guard.
- `ViewerUtils.toolArmed : AdaptiveModel -> aval<bool>` — used by the surface-pick
  scene-event gate (`surfacePickingActivated`) and by `allowAnnotationPicking`.
  The camera-live check spells out a related predicate inline
  (`directToolMode || not ctrlFlag`).

There is **no stored arm state**. `toolArmed` together with the active
`interaction` *is* the whole decision, evaluated fresh at each gate:

| what fires | gated by |
|---|---|
| place a point (`AddPointAdv`), cut-stroke point, coordinate cross, rover, … | `surfacePicking` (interaction ≠ Pick*) **&&** `toolArmed` **&&** left button — `ViewerUtils`, feeds `matchPickingInteraction` |
| select an annotation / grab or drop a control point | `allowAnnotationPicking` = `toolArmed` **&&** interaction ∈ {`PickAnnotation`, `EditAnnotation`, `DrawLog`} — gates the annotation pick target |
| start a box selection rectangle | `BoxSelectStart`: left button **&&** interaction = `PickAnnotation` **&&** `toolArmed` — see [AnnotationBoxSelection.md](AnnotationBoxSelection.md) |
| draw the control-point handles | `allowVertexEditing` = interaction is `EditAnnotation` (no `toolArmed`, so handles stay visible while you reach for Ctrl) |

`Model.ctrlFlag` / `Model.directToolMode` are the only inputs the handlers touch:
`ToggleDirectToolMode` flips `directToolMode`, the `Keyboard.Modifier`
KeyDown/KeyUp handlers set `ctrlFlag`. `SetInteraction` just sets `interaction`.

### History

`DrawingModel` used to carry `draw` / `pick` bool flags, mirrored by hand from
four handlers, and `DrawingApp.update` matched `(act, draw, pick)`. Direct Tool
Mode's toggle set `draw = true` for every tool — correct only for
`DrawAnnotation` — so `PickAnnotation` / `EditAnnotation` / `CutAnnotation` could
never match their `(_, false, true)` arm while it was on. The flags are now gone;
`DrawingApp.update` matches `act` alone. `StartDrawing` / `StopDrawing` /
`StartPicking` / `StopPicking` remain as no-op `DrawingAction` cases for the test
harness (`StopDrawing` still clears the hover preview).

## Related

- `docs/story-picking-during-navigation.md` — why a held Ctrl stops the camera
  dead in the classic scheme (keeps a navigation drag from re-firing a pick).
- `docs/AnnotationVertexEditing.md` — `EditAnnotation` is the one mode where
  annotation picking and KdTree surface picking are both live.
