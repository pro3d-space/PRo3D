# MapView camera vs. drawing-mode (inverseFlag) — modifier-key collision

## Symptom

When the "invert drawing" toggle is on, the natural Windows gesture to drive
the camera (hold **Ctrl** + drag with the left mouse button) does not rotate
the map view — it zooms instead. Other navigation modes (FreeFly, ArcBall)
behave correctly under the same toggle.

## Setup (verified against current code)

1. **App-level `ctrlFlag`** (`Viewer.fs:1366`) is set by the `Modifier` active
   pattern (`Viewer-Utils.fs:1248`):
   `LeftCtrl ∨ LeftAlt ∨ (key 70 ∧ isMac)`.
   So *any* of those keys flips `ctrlFlag` true. The name is misleading; it is
   a generic "modifier held" flag, not specifically Ctrl.

2. **Routing gate** (`Viewer.fs:2035`): the camera controller is wired to
   render-control attributes only when `ctrlFlag = inverseFlag`. Otherwise the
   events are routed to drawing.

   - `inverseFlag = false` (default): camera active when no modifier is held.
   - `inverseFlag = true`: camera active when a modifier is held.

3. **`m.ctrl` in the pointer event** comes from
   `Aardvark.UI 5.6 EventModifiers`, which is populated directly from DOM
   `event.ctrlKey`. It is **true iff physical Ctrl is held** — it does *not*
   fire for Alt, Cmd, or Shift. Verified by inspecting the published
   `Aardvark.UI.dll` (5.6.0): the only fields exposed are
   `ctrlKey / altKey / shiftKey / metaKey`, mapped 1:1 to DOM properties.

4. **MapView translation** (`MapViewCameraController.fs:395`):

   ```fsharp
   let b = if b = MouseButtons.Left && m.ctrl then MouseButtons.Right else b
       // Workaround for ctrl click on Mac, not sure if still required
   ```

   In MapView, Right is bound to **zoom** (line 271).

5. FreeFly and ArcBall do not see modifiers at all — they use
   `onCapturedPointerDown` (without the `Modifiers` variant) — so no
   analogous bug exists there.

## Matrix (Mac / Win × inverseFlag false / true) for MapView

| Platform | inverseFlag | Drawing gesture | Camera gesture | Camera works? |
|---|---|---|---|---|
| **Win** | false (default) | hold Ctrl or LeftAlt + drag | plain drag (no modifier) | yes — `m.ctrl = false`, no translation |
| **Win** | true | plain drag | hold **Ctrl** + drag | **no — `m.ctrl = true`, Left → Right → zoom. Rotate is broken.** |
| **Win** | true (LeftAlt user) | plain drag | hold **LeftAlt** + drag | yes — `m.ctrl = false`, translation skipped |
| **Mac** | false (default) | hold Alt / Cmd / Ctrl + drag | plain drag | yes — `m.ctrl = false`, rotate OK |
| **Mac** | true (Alt or Cmd) | plain drag | hold Alt or Cmd + drag | yes — `m.ctrl = false`, rotate OK |
| **Mac** | true (Ctrl) | plain drag | hold Ctrl + drag | no — same Left → Right → zoom failure |

## Findings

- The only broken cells are "user holds **physical Ctrl** while
  `inverseFlag = true`." Every other combination already works.
- The Mac-Ctrl-click workaround was misconceived from the start. In this app
  Mac users press Alt or Cmd as their `Modifier` (per `Viewer-Utils.fs:1248`),
  not Ctrl. So the check on `m.ctrl` never fires for them. The workaround
  has never actually helped Mac users.
- In default mode (`inverseFlag = false`), the workaround is unreachable on
  Windows either: the routing gate unsubscribes the camera the moment
  `ctrlFlag` flips. The translation only becomes reachable once
  `inverseFlag = true` — and at that point it actively breaks rotate for the
  natural modifier (Ctrl) on Windows.
- LeftAlt happens to be a workable Windows escape hatch in `inverseFlag` mode,
  but it is an undocumented coincidence, not a designed path.

## Fix

Delete the Left → Right translation on `MapViewCameraController.fs:395`.

Effects per matrix cell:

- Win + inverse=false: unchanged (translation was unreachable).
- **Win + inverse=true + Ctrl: fixed.** Left now rotates.
- Win + inverse=true + LeftAlt: unchanged (already worked).
- Mac + inverse=false: unchanged.
- Mac + inverse=true + Alt / Cmd: unchanged.
- Mac + inverse=true + Ctrl (if anyone does this): fixed.

Net: one bug fixed, zero regressions. The obsolete `onMouseDown` helper on
line 369 carries the same translation but is dead code (no in-file callers)
and can be dropped separately.
