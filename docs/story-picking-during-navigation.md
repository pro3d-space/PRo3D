# Bug story: surface picks fire during camera navigation

A walkthrough of how the "coordinate system gets re-placed while I move the
camera" bug was found and fixed — kept as a record of the reasoning, because the
root cause sits in a feature that touched a lot of files and the fix is a single
line in a non-obvious place.

---

## The symptom

1. Place a coordinate system (press **F4**, then click a surface).
2. Move the camera with the **arcball** (or free-fly).
3. The coordinate system is **placed again** — the reference frame jumps to
   wherever the navigation drag started.

It wasn't specific to coordinate systems: *any* place/pick interaction
(DrawAnnotation, PlaceRover, PlaceSurface, PlaceScaleBar, PickPivotPoint,
PickExploreCenter, …) could re-fire on a navigation mouse-down. Coordinate-system
placement just made it obvious.

## How picks and navigation are supposed to be separated

PRo3D uses one modifier to switch between "move the camera" and "interact with the
scene": hold **Ctrl** to pick/draw, release to navigate. An "invert drawing"
toggle (`m.inverseFlag`) can flip that, so the real predicate is:

- **picking mode**  = `m.ctrlFlag <> m.inverseFlag`
- **navigation mode** = `m.ctrlFlag =  m.inverseFlag`

So a click should only run an interaction in picking mode, and a drag should only
move the camera in navigation mode.

## What actually happens on a click

A surface's scene graph wires two pointer handlers (`Viewer-Utils.fs`,
`Sg.withEvents`):

- **Move**  → spawns `PreviewPickSurface (hit, name, surfacePicking)` (the hover
  cursor preview)
- **Click** → spawns `PickSurface (hit, name, surfacePicking)` (the real pick)

Both carry a bool, `surfacePicking`. The update loop only *acts* on a pick when
that bool is `true`:

```fsharp
| ViewerAction.PickSurface (p,name,true), _ -> ... matchPickingInteraction ...
| ViewerAction.PickSurface _, _            -> m      // catch-all: ignored
```

`matchPickingInteraction` then dispatches on `m.interaction` — `PlaceCoordinateSystem`,
`DrawAnnotation`, etc. So `surfacePicking` is effectively the master gate for
"should this click do something".

## The regression

`surfacePicking` was computed **only** from the interaction, never from the
ctrl/picking mode:

```fsharp
// before
let surfacePicking =
    m.interaction |> AVal.map (function
        | Interactions.PickAnnotation | Interactions.PickLog -> false
        | _ -> true)
```

So a click always produced `PickSurface(…, true)` and always ran the interaction —
**even while navigating**.

It used to be guarded elsewhere. The big `update` match was originally keyed on a
third element:

```fsharp
match msg, m.interaction, (m.ctrlFlag <> m.inverseFlag) with
| ViewerAction.PickSurface (p,name,true), _ , true -> ...   // only in picking mode
```

Commit **`c07773ab` "Fix camera change mode"** (sudokuMonaco, 2026-01-19) removed
that third match element across the whole `update` function and moved the
**navigation** gating to the attribute-emission layer (`renderControlAtts`, which
only yields FreeFly/ArcBall attributes when `inverseFlag = ctrlFlag`). That is a
cleaner design — but it gated only *navigation*. The equivalent guard for
*picking* (the `, true` on the `PickSurface` arm) was dropped and never replaced.
Result: navigation is gated at emission, picking is gated nowhere.

Confirmed via `git blame` / the commit diff:

```diff
- | ViewerAction.PickSurface (p,name,true), _ , true ->
+ | ViewerAction.PickSurface (p,name,true), _ ->
```

It also collapsed five other arms (`SetCamera`, `SetCameraAndFrustum`,
`SetCameraAndFrustum2`, `HeightValidation`, `NavigationMessage`) from a `false`
(navigation-only) guard to "any mode". Those turned out benign — programmatic
camera sets applying in any mode is harmless/arguably better, and `NavigationMessage`
is compensated because nav attributes are only *emitted* in navigation mode. The
only user-visible regression was `PickSurface`.

`origin/develop` carries the same regression: `c07773ab` is in develop, the
`PickSurface` arm there is still unguarded, and the later `c6570674` "Fix Arcball
camera" does not restore it.

## Choosing where to put the guard back

Two equally-correct, one-line options, differing only in one side effect:

1. **Gate the consumer** — add `when (m.ctrlFlag <> m.inverseFlag)` to the
   `PickSurface (p,name,true)` arm. Fixes only the pick; the hover preview cursor
   keeps showing during navigation (its own config guard `previewPickingEnabled` /
   `Config.previewIntersections`, added in `4a00c18f`, is untouched).
2. **Gate the spawn** — make `surfacePicking` itself require picking mode. The
   unguarded arm then works as-is, but because `surfacePicking` is shared by the
   Click and Move handlers, the **preview cursor** is also limited to picking mode.

The subtlety that decided it: the spawn site *already* has a guard, but it's a
**config/preview** guard (`previewPickingEnabled`), not a ctrl guard. Gating the
shared `surfacePicking` composes with it (preview shows only if config-enabled
**and** in picking mode); gating the consumer leaves preview in any mode.

**Chosen: gate the spawn**, accepting that the preview cursor is now picking-mode
only — it mirrors the navigation gate (both gated at emission, same layer) and
keeps the update arm clean.

## The landed fix (PR #608)

This bug was independently rediscovered and fixed here. While integrating, we found
the same bug had **already been fixed upstream** by Sophie Pichler — `14583377`
"Only spawn picking when necessary" (PR #608, `bugs/fix-surface-picking-message-spawning`,
merged 2026-04-20). Notably, Sophie (`sudokuMonaco`) is also the author of the
`c07773ab` regression — same author, fixed the proper way ~7 weeks later.

Her fix gates **only the Click handler**, inline, leaving the shared `surfacePicking`
and the Move/preview handler untouched (`Viewer-Utils.fs`):

```fsharp
yield SceneEventKind.Click, (fun sceneHit ->
    let surfacePicking = surfacePicking |> AVal.force
    let surfacePickingActivated = (m.ctrlFlag |> AVal.force) <> (m.inverseFlag |> AVal.force)
    if surfacePicking && surfacePickingActivated then true, [PickSurface (sceneHit, name, true)]
    else true, [])
```

This is the "gate only the click" variant from the section above — observably the
same as gating the consuming arm, but more efficient (no no-op action spawned). We
adopted it and dropped our own (redundant) `surfacePicking` ctrl-gate.

On top of #608 we also applied the **same `&& (ctrl <> inverse)` gate to the Move
handler**, so the hover preview cursor is hidden while navigating too (the chosen
behavior). Both handlers now spawn only in picking mode.

## Takeaways

- `c07773ab` moved navigation gating from the `update` match to emission but only
  did half the job; **picking needed the same treatment**. Sophie's #608 gates
  picking at emission too — symmetric with navigation.
- The single click spawn point is the right place to gate "are we allowed to pick" —
  it feeds `matchPickingInteraction`, so all place/pick interactions are covered at
  once.
- The hover preview cursor and the actual pick are gated independently (each
  handler checks `ctrl <> inverse` itself). #608 gated only the click; we then
  gated the Move handler too, so neither fires during navigation.

## Upstream / follow-up

- The fix is already on this branch via the `develop` merge (PR #608). The
  `SetCamera*` / `HeightValidation` arms that `c07773ab` collapsed are not bugs but
  are worth a glance if exact pre-`c07773ab` parity is ever wanted.
