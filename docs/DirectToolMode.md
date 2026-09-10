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

## Arming model

Whether the active tool "owns the left click right now" is a single predicate:

```
toolArmed  =  ctrlFlag <> directToolMode
```

i.e. classic = *armed while Ctrl held*, Direct Tool Mode = *armed unless Ctrl
held*. It has two representations that must stay in step:

- `ViewerApp.toolArmed : Model -> bool` — for the update handlers.
- `ViewerUtils.toolArmed : AdaptiveModel -> aval<bool>` — for the surface-pick
  scene-event gate (`surfacePickingActivated`) and the camera-live check.

### `syncToolArm` — the single writer for `draw` / `pick`

`DrawingModel.draw` and `DrawingModel.pick` (and `Model.picking`) carry no state
of their own. They are a pure function of `(interaction, ctrlFlag,
directToolMode)`:

| interaction | armed → | disarmed → |
|---|---|---|
| `DrawAnnotation` | `draw = true` | `draw = false` |
| `PickAnnotation`, `EditAnnotation`, `CutAnnotation`, `DrawLog` | `pick = true` | `pick = false` |
| anything else | — | — |

`ViewerApp.syncToolArm : Model -> Model` computes that table. **Every handler
that moves one of the three inputs ends by calling it** instead of poking the
flags directly:

- `ToggleDirectToolMode`
- Ctrl `KeyDown` / `KeyUp` (`Keyboard.Modifier`)
- `SetInteraction`

This is what keeps the classic scheme and Direct Tool Mode from having to agree
by hand. Before it existed, `ToggleDirectToolMode` bluntly set `draw = true` for
every tool — correct only for `DrawAnnotation` — which left `PickAnnotation`,
`EditAnnotation` and `CutAnnotation` unable to match their `(_, false, true)`
gate in `DrawingApp.update` while Direct Tool Mode was on.

The `(act, draw, pick)` gates inside `DrawingApp.update` are left in place as a
safety net; they are now always fed consistent flags.

## Related

- `docs/story-picking-during-navigation.md` — why a held Ctrl stops the camera
  dead in the classic scheme (keeps a navigation drag from re-firing a pick).
- `docs/AnnotationVertexEditing.md` — `EditAnnotation` is the one mode where
  annotation picking and KdTree surface picking are both live.
